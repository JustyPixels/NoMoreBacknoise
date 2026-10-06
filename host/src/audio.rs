use anyhow::{Context, Result};
use rtrb::{Consumer, Producer, RingBuffer};
use serde::Serialize;
use std::sync::{atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering}, mpsc::SyncSender, Arc};
use std::{thread, time::Duration};
use wasapi::*;

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Endpoint { pub id: String, pub name: String, pub direction: String }
pub fn devices() -> Result<Vec<Endpoint>> {
    initialize_mta().ok()?;
    let enumerator = DeviceEnumerator::new()?;
    let mut endpoints = Vec::new();
    for (direction, name) in [(Direction::Capture, "capture"), (Direction::Render, "render")] {
        let collection = enumerator.get_device_collection(&direction)?;
        for device in collection.into_iter().flatten() {
            endpoints.push(Endpoint { id: device.get_id()?, name: device.get_friendlyname()?, direction: name.into() });
        }
    }
    Ok(endpoints)
}
pub struct AudioState {
    pub stop: AtomicBool, pub capture_ms: AtomicU32, pub render_ms: AtomicU32,
    pub input_live: AtomicBool, pub dropped: AtomicU64, pub underruns: AtomicU64,
    pub errors: SyncSender<String>,
}
pub struct AudioIo { pub input: Consumer<f32>, pub output: Option<Producer<f32>>, pub monitor: Option<Producer<f32>>, threads: Vec<thread::JoinHandle<()>>, state: Arc<AudioState> }
impl Drop for AudioIo {
    fn drop(&mut self) {
        self.state.stop.store(true, Ordering::Relaxed);
        for thread in self.threads.drain(..) { let _ = thread.join(); }
    }
}
pub fn open(input_id: String, output_id: Option<String>, monitor_id: Option<String>, state: Arc<AudioState>) -> AudioIo {
    let (input_tx, input) = RingBuffer::new(2400);
    let mut threads = vec![];
    let input_state = state.clone();
    threads.push(thread::spawn(move || capture(input_id, input_tx, input_state)));
    let output = output_id.map(|id| { let (tx, rx) = RingBuffer::new(2400); let s = state.clone(); threads.push(thread::spawn(move || render(id, rx, s, false))); tx });
    let monitor = monitor_id.map(|id| { let (tx, rx) = RingBuffer::new(2400); let s = state.clone(); threads.push(thread::spawn(move || render(id, rx, s, true))); tx });
    AudioIo { input, output, monitor, threads, state }
}
fn pause(state: &AudioState) { for _ in 0..10 { if state.stop.load(Ordering::Relaxed) { break; } thread::sleep(Duration::from_millis(100)); } }
fn capture(id: String, mut tx: Producer<f32>, state: Arc<AudioState>) {
    if let Err(e) = initialize_mta().ok() { let _ = state.errors.try_send(format!("Microphone initialization failed: {e}")); return; }
    while !state.stop.load(Ordering::Relaxed) {
        let result = (|| -> Result<()> {
            let enumerator = DeviceEnumerator::new()?;
            let device = enumerator.get_device(&id).context("Selected microphone is unavailable")?;
            let mut client = device.get_iaudioclient()?;
            let format = WaveFormat::new(32, 32, &SampleType::Float, 48000, 1, None);
            client.initialize_client(&format, &Direction::Capture, &StreamMode::EventsShared { autoconvert: true, buffer_duration_hns: 100_000 })?;
            let event = client.set_get_eventhandle()?;
            let size = client.get_buffer_size()? as usize;
            state.capture_ms.store((size as f32 / 48.).ceil() as u32, Ordering::Relaxed);
            let capture = client.get_audiocaptureclient()?;
            let mut bytes = vec![0u8; size * 4];
            client.start_stream()?;
            state.input_live.store(true, Ordering::Release);
            let mut timeouts = 0;
            while !state.stop.load(Ordering::Relaxed) {
                // Event timeouts allow prompt shutdown without replacing the saved device.
                if event.wait_for_event(100).is_err() {
                    timeouts += 1;
                    if timeouts >= 5 { anyhow::bail!("Microphone stopped delivering audio"); }
                    continue;
                }
                timeouts = 0;
                loop {
                    let (frames, _) = capture.read_from_device(&mut bytes)?;
                    if frames == 0 { break; }
                    for sample in bytes[..frames as usize * 4].chunks_exact(4) {
                        let value = f32::from_le_bytes(sample.try_into().unwrap());
                        if tx.push(if value.is_finite() { value } else { 0. }).is_err() { state.dropped.fetch_add(1, Ordering::Relaxed); }
                    }
                }
            }
            client.stop_stream()?;
            Ok(())
        })();
        state.input_live.store(false, Ordering::Release);
        if let Err(e) = result { let _ = state.errors.try_send(format!("Microphone unavailable; sending silence and waiting for the same device: {e}")); }
        if !state.stop.load(Ordering::Relaxed) { pause(&state); }
    }
}

// Small, smoothly varying clock correction. WASAPI performs the device-rate conversion;
// this interpolator only compensates drift between the independent capture/render clocks.
struct DriftReader { a: f32, b: f32, phase: f64, initialized: bool, ratio: f64 }
impl DriftReader {
    fn new() -> Self { Self { a: 0., b: 0., phase: 0., initialized: false, ratio: 1. } }
    fn next(&mut self, rx: &mut Consumer<f32>, state: &AudioState) -> f32 {
        if !state.input_live.load(Ordering::Acquire) { while rx.pop().is_ok() {} self.initialized = false; return 0.; }
        if !self.initialized {
            if rx.slots() < 480 { return 0.; }
            self.a = rx.pop().unwrap_or(0.); self.b = rx.pop().unwrap_or(0.); self.initialized = true;
        }
        let error = (rx.slots() as f64 - 480.) / 480.;
        let desired = 1. + (error * 0.0001).clamp(-0.0005, 0.0005);
        self.ratio += (desired - self.ratio) * 0.0005;
        let value = self.a + (self.b - self.a) * self.phase as f32;
        self.phase += self.ratio;
        while self.phase >= 1. {
            self.phase -= 1.; self.a = self.b;
            match rx.pop() { Ok(x) => self.b = x, Err(_) => { self.initialized = false; self.a = 0.; self.b = 0.; state.underruns.fetch_add(1, Ordering::Relaxed); return 0.; } }
        }
        value
    }
}
fn render(id: String, mut rx: Consumer<f32>, state: Arc<AudioState>, monitor: bool) {
    if let Err(e) = initialize_mta().ok() { let _ = state.errors.try_send(format!("Output initialization failed: {e}")); return; }
    while !state.stop.load(Ordering::Relaxed) {
        let result = (|| -> Result<()> {
            let enumerator = DeviceEnumerator::new()?;
            let device = enumerator.get_device(&id).context("Selected output is unavailable")?;
            let mut client = device.get_iaudioclient()?;
            let format = WaveFormat::new(32, 32, &SampleType::Float, 48000, 1, None);
            client.initialize_client(&format, &Direction::Render, &StreamMode::EventsShared { autoconvert: true, buffer_duration_hns: 100_000 })?;
            let event = client.set_get_eventhandle()?;
            let size = client.get_buffer_size()? as usize;
            if !monitor { state.render_ms.store((size as f32 / 48.).ceil() as u32, Ordering::Relaxed); }
            let render = client.get_audiorenderclient()?;
            let mut bytes = vec![0u8; size * 4];
            while rx.pop().is_ok() {}
            let mut drift = DriftReader::new();
            client.start_stream()?;
            while !state.stop.load(Ordering::Relaxed) {
                let frames = client.get_available_space_in_frames()? as usize;
                for sample in bytes[..frames * 4].chunks_exact_mut(4) { sample.copy_from_slice(&drift.next(&mut rx, &state).to_le_bytes()); }
                if frames > 0 { render.write_to_device(frames, &bytes[..frames * 4], None)?; }
                let _ = event.wait_for_event(100);
            }
            client.stop_stream()?;
            Ok(())
        })();
        if let Err(e) = result { let _ = state.errors.try_send(format!("{} output unavailable; waiting for the same device: {e}", if monitor { "Monitor" } else { "Cable" })); }
        if !state.stop.load(Ordering::Relaxed) { pause(&state); }
    }
}

#[cfg(test)] mod tests {
    use super::*;
    #[test] fn disconnected_input_never_replays_buffered_audio() {
        let (errors, _) = std::sync::mpsc::sync_channel(1);
        let s = AudioState { stop: AtomicBool::new(false), capture_ms: AtomicU32::new(0), render_ms: AtomicU32::new(0), input_live: AtomicBool::new(false), dropped: AtomicU64::new(0), underruns: AtomicU64::new(0), errors };
        let (mut tx, mut rx) = RingBuffer::new(1000); for _ in 0..900 { tx.push(0.5).unwrap(); }
        assert_eq!(DriftReader::new().next(&mut rx, &s), 0.); assert_eq!(rx.slots(), 0);
    }
}
