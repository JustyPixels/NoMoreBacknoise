use anyhow::{bail, Result};
use df::tract::{DfParams, DfTract, RuntimeParams};
use ndarray::{ArrayView2, ArrayViewMut2};
use serde::{Deserialize, Serialize};
use std::{collections::VecDeque, ffi::c_void, ptr::NonNull, time::Instant};

pub const RATE: usize = 48_000;
pub const FRAME: usize = 480;

extern "C" {
    fn rnnoise_create() -> *mut c_void;
    fn rnnoise_destroy(state: *mut c_void);
    fn rnnoise_process_frame(state: *mut c_void, output: *mut f32, input: *const f32) -> f32;
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase", default)]
pub struct Settings {
    pub engine: String,
    pub backend: String,
    pub strength: f32,
    pub attenuation_db: f32,
    pub speech_threshold: f32,
    pub attack_ms: f32,
    pub hold_ms: f32,
    pub release_ms: f32,
    pub floor_db: f32,
    pub gain_db: f32,
    pub gate_enabled: bool,
}
impl Default for Settings {
    fn default() -> Self {
        Self { engine: "auto".into(), backend: "cpu".into(), strength: 75., attenuation_db: 35.,
            speech_threshold: 0.35, attack_ms: 2., hold_ms: 180., release_ms: 140., floor_db: -35., gain_db: 0., gate_enabled: true }
    }
}
impl Settings {
    pub fn validate(&self) -> Result<()> {
        if !["auto", "rnnoise", "deepfilter"].contains(&self.engine.as_str()) { bail!("Unknown engine"); }
        if !["cpu", "directml"].contains(&self.backend.as_str()) { bail!("Unknown backend"); }
        for (name, value, low, high) in [
            ("strength", self.strength, 0., 100.), ("attenuationDb", self.attenuation_db, 0., 60.),
            ("speechThreshold", self.speech_threshold, 0.05, 0.95), ("attackMs", self.attack_ms, 0.1, 100.),
            ("holdMs", self.hold_ms, 0., 2000.), ("releaseMs", self.release_ms, 1., 2000.),
            ("floorDb", self.floor_db, -80., 0.), ("gainDb", self.gain_db, -12., 12.)] {
            if !value.is_finite() || value < low || value > high { bail!("Invalid {name}"); }
        }
        Ok(())
    }
}

pub struct RnNoise(NonNull<c_void>);
impl RnNoise {
    pub fn new() -> Result<Self> { NonNull::new(unsafe { rnnoise_create() }).map(Self).ok_or_else(|| anyhow::anyhow!("RNNoise allocation failed")) }
    pub fn process(&mut self, input: &[f32; FRAME], output: &mut [f32; FRAME]) -> f32 {
        let scaled = input.map(|x| x.clamp(-1., 1.) * 32768.);
        let probability = unsafe { rnnoise_process_frame(self.0.as_ptr(), output.as_mut_ptr(), scaled.as_ptr()) };
        for x in output { *x /= 32768.; }
        probability.clamp(0., 1.)
    }
}
impl Drop for RnNoise { fn drop(&mut self) { unsafe { rnnoise_destroy(self.0.as_ptr()) }; } }

pub struct Expander { gain: f32, hold_samples: usize, open: bool }
impl Expander {
    pub fn new() -> Self { Self { gain: 1., hold_samples: 0, open: true } }
    pub fn apply(&mut self, samples: &mut [f32], probability: f32, cfg: &Settings) {
        let threshold = cfg.speech_threshold;
        if probability >= threshold { self.open = true; self.hold_samples = (cfg.hold_ms * 48.) as usize; }
        else if probability < threshold * 0.65 {
            self.hold_samples = self.hold_samples.saturating_sub(samples.len());
            if self.hold_samples == 0 { self.open = false; }
        }
        let target = if self.open || !cfg.gate_enabled { 1. } else { 10f32.powf(cfg.floor_db / 20.) };
        let time = if target > self.gain { cfg.attack_ms } else { cfg.release_ms };
        let coefficient = (-1. / (time * 48.).max(1.)).exp();
        let output_gain = 10f32.powf(cfg.gain_db / 20.);
        for x in samples { self.gain = target + coefficient * (self.gain - target); *x = (*x * self.gain * output_gain).clamp(-0.999, 0.999); }
    }
}

pub struct FrameResult {
    pub raw: [f32; FRAME], pub clean: [f32; FRAME], pub speech: f32,
    pub processing_ms: f64, pub algorithm_ms: f64, pub warning: Option<String>,
}
pub struct Pipeline {
    rn: Option<RnNoise>, df: Option<DfTract>, pub selected: String,
    pub reason: String, pub settings: Settings, expander: Expander,
    raw_delay: VecDeque<f32>, voice_delay: VecDeque<f32>, delay_frames: usize,
    pub degraded: bool,
}
impl Pipeline {
    pub fn new(settings: Settings) -> Result<Self> {
        settings.validate()?;
        let rn = RnNoise::new().ok();
        let mut result = Self { rn, df: None, selected: "rnnoise".into(), reason: String::new(),
            settings, expander: Expander::new(), raw_delay: VecDeque::new(), voice_delay: VecDeque::new(), delay_frames: 1, degraded: false };
        if result.settings.backend != "cpu" { bail!("DirectML is unavailable for the pinned stateful Tract backend. CPU processing remains available."); }
        if result.settings.engine != "rnnoise" {
            let initialization = (|| -> Result<()> {
            let rp = RuntimeParams::default_with_ch(1).with_atten_lim(result.settings.attenuation_db * result.settings.strength / 100.);
            match DfTract::new(DfParams::default(), &rp) {
                Ok(mut model) => {
                    if model.sr != RATE || model.hop_size != FRAME { bail!("Unexpected DeepFilterNet model format"); }
                    let sample: [f32; FRAME] = std::array::from_fn(|i| 0.1 * ((i as f32 * 0.029).sin() + 0.3 * (i as f32 * 0.061).cos()));
                    let mut out = [0.; FRAME];
                    let mut timings = Vec::new();
                    for i in 0..80 { let t = Instant::now(); model.process(ArrayView2::from_shape((1, FRAME), &sample)?, ArrayViewMut2::from_shape((1, FRAME), &mut out)?)?; if i >= 10 { timings.push(t.elapsed().as_secs_f64() * 1000.); } }
                    timings.sort_by(f64::total_cmp);
                    let p95 = timings[(timings.len() as f32 * 0.95) as usize];
                    let delay = (model.fft_size - model.hop_size + model.lookahead * model.hop_size) as f64 / 48.;
                    if p95 < 5. && delay + 20. <= 50. {
                        result.delay_frames = ((model.fft_size - model.hop_size) / FRAME) + model.lookahead;
                        // Fresh state avoids carrying the synthetic startup benchmark into live audio.
                        result.df = Some(DfTract::new(DfParams::default(), &rp)?);
                        result.selected = "deepfilter".into();
                        result.reason = format!("DeepFilterNet passed startup benchmark: p95 {p95:.2} ms/frame, {delay:.0} ms model delay. Device buffering is additional.");
                    } else {
                        result.reason = format!("RNNoise selected: DeepFilterNet p95 {p95:.2} ms/frame, model delay {delay:.0} ms exceeded the startup budget.");
                        result.degraded = result.settings.engine == "deepfilter";
                    }
                }
                Err(e) => { result.reason = format!("RNNoise fallback: DeepFilterNet initialization failed: {e}"); result.degraded = true; }
            }
            Ok(())
            })();
            if let Err(e) = initialization {
                result.df = None; result.selected = "rnnoise".into(); result.delay_frames = 1;
                result.reason = format!("RNNoise fallback: DeepFilterNet benchmark failed: {e}"); result.degraded = true;
            }
        } else { result.reason = "RNNoise selected by user.".into(); }
        if result.df.is_none() && result.rn.is_none() {
            result.selected = "raw".into(); result.delay_frames = 0; result.degraded = true;
            result.reason = "Unfiltered microphone: neither engine initialized. Retry to restore filtering.".into();
        }
        result.reset_alignment();
        Ok(result)
    }
    fn reset_alignment(&mut self) {
        self.raw_delay = VecDeque::from(vec![0.; self.delay_frames * FRAME]);
        self.voice_delay = VecDeque::from(vec![0.; self.delay_frames]);
    }
    pub fn delay_samples(&self) -> usize { self.delay_frames * FRAME }
    pub fn update(&mut self, settings: Settings) -> Result<()> {
        settings.validate()?;
        if settings.engine != self.settings.engine || settings.backend != self.settings.backend { bail!("Engine changes require Retry or restart"); }
        if let Some(model) = &mut self.df { model.set_atten_lim(settings.attenuation_db * settings.strength / 100.); }
        self.settings = settings;
        Ok(())
    }
    pub fn process(&mut self, input: &[f32; FRAME], muted: bool, bypass: bool) -> FrameResult {
        let timer = Instant::now();
        let sanitized = input.map(|x| if x.is_finite() { x.clamp(-1., 1.) } else { 0. });
        let mut rn_out = [0.; FRAME];
        let probability = self.rn.as_mut().map(|rn| rn.process(&sanitized, &mut rn_out)).unwrap_or(0.);
        let mut output = rn_out;
        let mut warning = None;
        if let Some(model) = &mut self.df {
            let result = model.process(ArrayView2::from_shape((1, FRAME), &sanitized).unwrap(), ArrayViewMut2::from_shape((1, FRAME), &mut output).unwrap())
                .and_then(|_| { anyhow::ensure!(output.iter().all(|x| x.is_finite()), "DeepFilterNet produced invalid samples"); Ok(()) });
            if let Err(e) = result {
                self.df = None; self.selected = if self.rn.is_some() { "rnnoise" } else { "raw" }.into();
                self.delay_frames = if self.rn.is_some() { 1 } else { 0 }; self.reset_alignment(); self.degraded = true;
                warning = Some(format!("Filtering failed. {} fallback active: {e}", self.selected)); output = rn_out;
            }
        }
        if output.iter().any(|x| !x.is_finite()) {
            self.df = None; self.rn = None; self.selected = "raw".into(); self.delay_frames = 0; self.reset_alignment(); self.degraded = true;
            warning = Some("Unfiltered microphone: processing produced invalid samples. Retry to restore filtering.".into());
        }
        self.raw_delay.extend(sanitized);
        let aligned_raw = std::array::from_fn(|_| self.raw_delay.pop_front().unwrap_or(0.));
        self.voice_delay.push_back(probability);
        let delayed_probability = self.voice_delay.pop_front().unwrap_or(probability);
        if self.selected == "rnnoise" { let wet = self.settings.strength / 100.; for i in 0..FRAME { output[i] = output[i] * wet + aligned_raw[i] * (1. - wet); } }
        self.expander.apply(&mut output, delayed_probability, &self.settings);
        let raw_active = bypass || self.selected == "raw";
        if raw_active { output = sanitized.map(|x| x.clamp(-0.999, 0.999)); }
        if muted { output.fill(0.); }
        FrameResult { raw: if raw_active { sanitized } else { aligned_raw }, clean: output,
            speech: delayed_probability, processing_ms: timer.elapsed().as_secs_f64() * 1000.,
            algorithm_ms: if raw_active { 0. } else { self.delay_frames as f64 * 10. }, warning }
    }
}

pub fn levels(samples: &[f32]) -> (f32, f32) {
    let peak = samples.iter().fold(0f32, |a, x| a.max(x.abs()));
    let rms = (samples.iter().map(|x| x * x).sum::<f32>() / samples.len().max(1) as f32).sqrt();
    (20. * peak.max(1e-6).log10(), 20. * rms.max(1e-6).log10())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn silence_is_finite_and_quiet() { let mut p = Pipeline::new(Settings { engine: "rnnoise".into(), ..Settings::default() }).unwrap(); for _ in 0..100 { let r = p.process(&[0.; FRAME], false, false); assert!(r.clean.iter().all(|x| x.is_finite() && x.abs() < 1e-6)); } }
    #[test] fn mute_wins_over_bypass_and_raw() { let mut p = Pipeline::new(Settings { engine: "rnnoise".into(), ..Settings::default() }).unwrap(); p.rn = None; p.selected = "raw".into(); assert!(p.process(&[0.2; FRAME], true, true).clean.iter().all(|x| *x == 0.)); }
    #[test] fn bypass_has_no_expander_or_gain() { let mut p = Pipeline::new(Settings { engine: "rnnoise".into(), gain_db: 12., ..Settings::default() }).unwrap(); assert_eq!(p.process(&[0.2; FRAME], false, true).clean, [0.2; FRAME]); }
    #[test] fn rejects_nonfinite_and_unknown_settings() { assert!(Settings { gain_db: f32::NAN, ..Settings::default() }.validate().is_err()); assert!(Settings { engine: "fake".into(), ..Settings::default() }.validate().is_err()); }
    #[test] fn gate_preserves_held_word_endings() { let mut gate = Expander::new(); let cfg = Settings::default(); let mut frame = [0.2; FRAME]; gate.apply(&mut frame, 1., &cfg); for _ in 0..10 { frame.fill(0.2); gate.apply(&mut frame, 0., &cfg); assert!(frame[FRAME - 1] > 0.19); } for _ in 0..200 { frame.fill(0.2); gate.apply(&mut frame, 0., &cfg); } assert!(frame[FRAME - 1] < 0.01); }
    #[test] fn gate_hysteresis_avoids_chatter() { let mut gate = Expander::new(); let cfg = Settings::default(); let mut frame = [0.2; FRAME]; gate.apply(&mut frame, 0.8, &cfg); for _ in 0..100 { frame.fill(0.2); gate.apply(&mut frame, cfg.speech_threshold * 0.8, &cfg); assert!(frame[FRAME - 1] > 0.19); } }
    #[test] fn missing_rnnoise_falls_back_to_raw_without_gain() { let mut p = Pipeline::new(Settings { engine: "rnnoise".into(), ..Settings::default() }).unwrap(); p.rn = None; p.selected = "raw".into(); assert_eq!(p.process(&[0.1; FRAME], false, false).clean, [0.1; FRAME]); }
    #[test] fn raw_alignment_matches_declared_delay() { let mut p = Pipeline::new(Settings { engine: "rnnoise".into(), ..Settings::default() }).unwrap(); assert_eq!(p.process(&[0.2; FRAME], false, false).raw, [0.; FRAME]); assert_eq!(p.process(&[0.3; FRAME], false, false).raw, [0.2; FRAME]); }
}
