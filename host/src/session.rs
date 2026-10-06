use crate::{audio, dsp::{self, Pipeline, Settings, FRAME}};
use anyhow::Result;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::sync::{atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering}, mpsc::{self, SyncSender}, Arc, Mutex};
use std::{thread, time::{Duration, Instant}};
use rustfft::{FftPlanner, num_complex::Complex};

#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Route { pub input_id: String, pub output_id: Option<String>, pub monitor_id: Option<String>, #[serde(default)] pub monitor_raw: bool }
#[derive(Clone)]
pub struct Control { pub settings: Settings, pub muted: bool, pub bypass: bool, pub monitor_raw: bool }
pub struct Recording { pub raw: Vec<f32>, pub clean: Vec<f32>, pub active: bool, pub playback: Option<(bool, usize)> }
impl Recording { pub fn new() -> Self { Self { raw: Vec::new(), clean: Vec::new(), active: false, playback: None } } }
pub struct Session {
    pub controls: Arc<Mutex<Control>>, pub recording: Arc<Mutex<Recording>>,
    pub state: Arc<audio::AudioState>, worker: Option<thread::JoinHandle<()>>,
    pub monitor_enabled: bool,
}
impl Session {
    pub fn start(route: Route, settings: Settings, muted: bool, bypass: bool, events: SyncSender<Value>) -> Result<Self> {
        settings.validate()?;
        anyhow::ensure!(!route.input_id.is_empty(), "Choose a microphone first");
        anyhow::ensure!(route.monitor_id.is_none() || route.monitor_id != route.output_id, "Monitor output must differ from the cable feed");
        let (error_tx, error_rx) = mpsc::sync_channel(8);
        let state = Arc::new(audio::AudioState { stop: AtomicBool::new(false), capture_ms: AtomicU32::new(0), render_ms: AtomicU32::new(0), input_live: AtomicBool::new(false), dropped: AtomicU64::new(0), underruns: AtomicU64::new(0), errors: error_tx });
        let controls = Arc::new(Mutex::new(Control { settings, muted, bypass, monitor_raw: route.monitor_raw }));
        let recording = Arc::new(Mutex::new(Recording::new()));
        let monitor_enabled = route.monitor_id.is_some();
        let worker_state = state.clone(); let worker_controls = controls.clone(); let worker_recording = recording.clone();
        let worker = thread::spawn(move || {
            let initial = worker_controls.lock().unwrap().settings.clone();
            let mut pipeline = match Pipeline::new(initial) { Ok(p) => p, Err(e) => { let _ = events.send(json!({"type":"error","message":e.to_string()})); return; } };
            let _ = events.send(json!({"type":"status","engine":pipeline.selected,"message":pipeline.reason,"degraded":pipeline.degraded}));
            let mut io = audio::open(route.input_id, route.output_id, route.monitor_id, worker_state.clone());
            let mut frame = [0.; FRAME]; let mut used = 0;
            let mut raw_view = Vec::with_capacity(FRAME * 3); let mut clean_view = Vec::with_capacity(FRAME * 3);
            let mut peak_processing = 0f64; let mut last_emit = Instant::now(); let mut last_input = Instant::now();
            let mut fft_planner = FftPlanner::<f32>::new(); let fft = fft_planner.plan_fft_forward(512);
            let mut fft_buffer = vec![Complex::new(0., 0.); 512];
            let mut fft_scratch = vec![Complex::new(0., 0.); fft.get_inplace_scratch_len()];
            let mut recording_finished = false;
            while !worker_state.stop.load(Ordering::Relaxed) {
                while let Ok(error) = error_rx.try_recv() { let _ = events.try_send(json!({"type":"warning","message":error,"code":"device"})); }
                let ctrl = worker_controls.lock().unwrap().clone();
                if ctrl.settings != pipeline.settings { if let Err(e) = pipeline.update(ctrl.settings.clone()) { let _ = events.try_send(json!({"type":"error","message":e.to_string()})); worker_controls.lock().unwrap().settings = pipeline.settings.clone(); } }
                // Drain stale input on disconnect and reset filter state before a reconnection.
                if !worker_state.input_live.load(Ordering::Acquire) {
                    while io.input.pop().is_ok() {} used = 0; raw_view.clear(); clean_view.clear();
                    thread::sleep(Duration::from_millis(5)); continue;
                }
                if last_input.elapsed() > Duration::from_millis(500) && used == 0 {
                    if let Ok(fresh) = Pipeline::new(pipeline.settings.clone()) { pipeline = fresh; }
                    last_input = Instant::now();
                }
                if io.input.slots() > FRAME * 3 { while io.input.slots() > FRAME * 2 { let _ = io.input.pop(); worker_state.dropped.fetch_add(1, Ordering::Relaxed); } }
                while used < FRAME { match io.input.pop() { Ok(value) => { frame[used] = value; used += 1; }, Err(_) => break } }
                if used < FRAME { thread::sleep(Duration::from_millis(1)); continue; }
                used = 0; last_input = Instant::now();
                let result = pipeline.process(&frame, ctrl.muted, ctrl.bypass);
                if let Some(message) = &result.warning { let _ = events.try_send(json!({"type":"warning","message":message,"code":"fallback","engine":pipeline.selected})); }
                for &sample in &result.clean { if let Some(tx) = &mut io.output { let _ = tx.push(sample); } }
                let mut monitor_frame = if ctrl.monitor_raw { result.raw } else { result.clean };
                {
                    let mut rec = worker_recording.lock().unwrap();
                    if let Some((raw, cursor)) = rec.playback {
                        let samples = if raw { &rec.raw } else { &rec.clean };
                        let count = FRAME.min(samples.len().saturating_sub(cursor));
                        monitor_frame.fill(0.); monitor_frame[..count].copy_from_slice(&samples[cursor..cursor+count]);
                        rec.playback = if cursor+count >= samples.len() { None } else { Some((raw,cursor+count)) };
                    }
                    if rec.active {
                        if rec.raw.len() + FRAME <= 60 * dsp::RATE { rec.raw.extend_from_slice(&result.raw); rec.clean.extend_from_slice(&result.clean); recording_finished = false; }
                        else { rec.active = false; recording_finished = true; }
                    }
                }
                if let Some(tx) = &mut io.monitor { for &sample in &monitor_frame { let _ = tx.push(if ctrl.muted { 0. } else { sample }); } }
                if recording_finished { let _ = events.try_send(json!({"type":"recordingStopped","reason":"limit"})); recording_finished = false; }
                raw_view.extend(result.raw); clean_view.extend(result.clean); peak_processing = peak_processing.max(result.processing_ms);
                if last_emit.elapsed() >= Duration::from_millis(33) {
                    let decimate = |samples: &[f32]| -> Vec<f32> { samples.chunks(12).map(|block| *block.iter().max_by(|a,b| a.abs().total_cmp(&b.abs())).unwrap_or(&0.)).collect() };
                    let spectrum = |samples: &[f32], buffer: &mut [Complex<f32>], scratch: &mut [Complex<f32>]| -> Vec<f32> {
                        for (i, slot) in buffer.iter_mut().enumerate() { let offset = samples.len().saturating_sub(512); slot.re = samples.get(offset + i).copied().unwrap_or(0.) * (0.5 - 0.5 * (std::f32::consts::TAU * i as f32 / 511.).cos()); slot.im = 0.; }
                        fft.process_with_scratch(buffer, scratch);
                        (0..64).map(|i| { let magnitude = buffer[i*4..i*4+4].iter().map(|x| x.norm()).sum::<f32>() / 512.; (20. * magnitude.max(1e-6).log10()).clamp(-100., 0.) }).collect()
                    };
                    let (raw_peak, raw_rms) = dsp::levels(&raw_view); let (clean_peak, clean_rms) = dsp::levels(&clean_view);
                    let record_seconds = worker_recording.lock().unwrap().raw.len() as f64 / dsp::RATE as f64;
                    let message = json!({"type":"telemetry","raw":decimate(&raw_view),"clean":decimate(&clean_view),
                        "rawSpectrum":spectrum(&raw_view,&mut fft_buffer,&mut fft_scratch),"cleanSpectrum":spectrum(&clean_view,&mut fft_buffer,&mut fft_scratch),
                        "rawPeak":raw_peak,"rawRms":raw_rms,"cleanPeak":clean_peak,"cleanRms":clean_rms,"speech":result.speech,
                        "clipping":frame.iter().any(|x| x.abs() >= 0.999),"processingMs":peak_processing,"modelDelayMs":result.algorithm_ms,
                        "captureBufferMs":worker_state.capture_ms.load(Ordering::Relaxed),"renderBufferMs":worker_state.render_ms.load(Ordering::Relaxed),
                        "queuedMs":(io.input.slots() + io.output.as_ref().map(|p| 2400 - p.slots()).unwrap_or(0)) as f64 / 48.,"droppedSamples":worker_state.dropped.load(Ordering::Relaxed),"underruns":worker_state.underruns.load(Ordering::Relaxed),
                        "engine":pipeline.selected,"degraded":pipeline.degraded,"recordingSeconds":record_seconds});
                    let _ = events.try_send(message); raw_view.clear(); clean_view.clear(); peak_processing = 0.; last_emit = Instant::now();
                }
            }
            worker_state.stop.store(true, Ordering::Relaxed);
            drop(io);
        });
        Ok(Self { controls, recording, state, worker: Some(worker), monitor_enabled })
    }
    pub fn stop(&mut self) { self.state.stop.store(true, Ordering::Relaxed); if let Some(worker) = self.worker.take() { let _ = worker.join(); } }
}
impl Drop for Session { fn drop(&mut self) { self.stop(); } }
