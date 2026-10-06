mod dsp;
mod audio;
mod session;
use anyhow::{bail, Context, Result};
use serde::Deserialize;
use serde_json::{json, Value};
use std::{fs::OpenOptions, sync::{mpsc, atomic::Ordering, Arc}, time::Instant};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt};
use dsp::{Settings, Pipeline, FRAME, RATE};

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Request {
    version: u32, id: String, op: String,
    settings: Option<Settings>, route: Option<session::Route>, muted: Option<bool>, bypass: Option<bool>,
    monitor_raw: Option<bool>, path: Option<String>, include_inactive: Option<bool>,
}
fn main() { if let Err(error) = run() { eprintln!("{error:#}"); std::process::exit(1); } }
fn run() -> Result<()> {
    let args: Vec<String> = std::env::args().collect();
    match args.get(1).map(String::as_str) {
        Some("--devices") => { println!("{}", serde_json::to_string_pretty(&audio::devices()?)?); Ok(()) }
        Some("--self-test") => self_test(),
        Some("--process-file") => process_file(&args[2..]),
        Some("--pipe") => pipe(args.get(2).context("Missing pipe name")?),
        _ => bail!("Use --pipe NAME, --devices, --self-test, or --process-file INPUT.wav OUTPUT.wav [rnnoise|deepfilter]."),
    }
}
fn pipe(name: &str) -> Result<()> {
    anyhow::ensure!(name.starts_with("nmb-") && name.len() < 100 && name.chars().all(|c| c.is_ascii_alphanumeric() || c == '-'), "Invalid pipe name");
    let runtime = Arc::new(tokio::runtime::Runtime::new()?);
    let file = { let _guard = runtime.enter(); tokio::net::windows::named_pipe::ClientOptions::new().open(format!(r"\\.\pipe\{name}"))? };
    let (read_half, mut write_half) = tokio::io::split(file);
    let mut reader = tokio::io::BufReader::new(read_half);
    let (events, rx) = mpsc::sync_channel::<Value>(32);
    let writer_runtime = runtime.clone();
    let writer = std::thread::spawn(move || { while let Ok(mut value) = rx.recv() { value["version"] = json!(1); let line = format!("{value}\n"); if writer_runtime.block_on(write_half.write_all(line.as_bytes())).is_err() { break; } } });
    events.send(json!({"type":"hello","hostVersion":"0.2.0-preview.1","capabilities":{"engines":["rnnoise","deepfilter"],"backends":["cpu"],"controls":{"rnnoise":["strength","speechThreshold","attackMs","holdMs","releaseMs","floorDb","gainDb","gateEnabled"],"deepfilter":["strength","attenuationDb","speechThreshold","attackMs","holdMs","releaseMs","floorDb","gainDb","gateEnabled"]},"voiceIsolation":false,"directml":false}}))?;
    let mut current: Option<session::Session> = None;
    let mut line = String::new();
    loop {
        line.clear();
        // The UI creates a same-user-only pipe. Cap incoming messages to avoid unbounded reads.
        if runtime.block_on((&mut reader).take(65537).read_line(&mut line))? == 0 { break; }
        if line.len() > 65536 { bail!("Oversized protocol message"); }
        let request: Request = match serde_json::from_str(&line) { Ok(x) => x, Err(e) => { let _ = events.send(json!({"type":"error","message":e.to_string()})); continue; } };
        let result = (|| -> Result<Value> {
            anyhow::ensure!(request.version == 1, "Unsupported protocol version");
            match request.op.as_str() {
                "devices" => Ok(json!({"type":"devices","devices":audio::inventory(request.include_inactive.unwrap_or(false))?,"defaults":audio::defaults()?})),
                "start" => {
                    if let Some(mut session) = current.take() { session.stop(); }
                    let route = request.route.clone().context("Missing route")?;
                    let endpoints = audio::devices()?;
                    anyhow::ensure!(endpoints.iter().any(|d| d.id == route.input_id && d.direction == "capture"), "Selected input is unavailable");
                    for id in [route.output_id.as_ref(), route.monitor_id.as_ref()].into_iter().flatten() { anyhow::ensure!(endpoints.iter().any(|d| &d.id == id && d.direction == "render"), "Selected output is unavailable"); }
                    current = Some(session::Session::start(route, request.settings.clone().unwrap_or_default(), request.muted.unwrap_or(false), request.bypass.unwrap_or(false), events.clone())?);
                    Ok(json!({"type":"ack","state":"starting"}))
                }
                "stop" => { if let Some(mut session) = current.take() { session.stop(); } Ok(json!({"type":"ack","state":"stopped"})) }
                "configure" => {
                    let session = current.as_ref().context("Processing is stopped")?;
                    let mut control = session.controls.lock().unwrap();
                    if let Some(settings) = &request.settings { settings.validate()?; control.settings = settings.clone(); }
                    if let Some(value) = request.muted { control.muted = value; }
                    if let Some(value) = request.bypass { control.bypass = value; }
                    if let Some(value) = request.monitor_raw { control.monitor_raw = value; }
                    Ok(json!({"type":"ack"}))
                }
                "recordStart" => {
                    let session = current.as_ref().context("Start processing before recording")?;
                    let mut rec = session.recording.lock().unwrap();
                    rec.raw = Vec::with_capacity(60 * RATE); rec.clean = Vec::with_capacity(60 * RATE); rec.active = true; rec.playback = None;
                    Ok(json!({"type":"ack","recording":true}))
                }
                "recordStop" => { let session = current.as_ref().context("Processing is stopped")?; session.recording.lock().unwrap().active = false; Ok(json!({"type":"ack","recording":false})) }
                "recordPlay" => {
                    let session = current.as_ref().context("Processing is stopped")?;
                    anyhow::ensure!(session.monitor_enabled, "Choose a headphone output and enable monitoring before recording a comparison");
                    let mut rec = session.recording.lock().unwrap();
                    anyhow::ensure!(!rec.active && !rec.raw.is_empty(), "Stop a non-empty recording before playback");
                    let cursor = rec.playback.map(|(_,position)|position).unwrap_or(0);
                    rec.playback = Some((request.monitor_raw.unwrap_or(false),cursor));
                    Ok(json!({"type":"ack"}))
                }
                "recordPlaybackStop" => {
                    let session = current.as_ref().context("Processing is stopped")?;
                    session.recording.lock().unwrap().playback = None;
                    Ok(json!({"type":"ack"}))
                }
                "recordExport" => {
                    let session = current.as_ref().context("The recording belongs to the current processing session")?;
                    let path = std::path::PathBuf::from(request.path.as_ref().context("Choose an export path")?);
                    anyhow::ensure!(path.is_absolute(), "Export requires an absolute path");
                    let clean_path = path.with_extension("clean.wav"); let raw_path = path.with_extension("raw.wav");
                    anyhow::ensure!(!clean_path.exists() && !raw_path.exists(), "Choose a new file name; existing recordings will not be overwritten");
                    let (raw, clean) = {
                        let rec = session.recording.lock().unwrap();
                        anyhow::ensure!(!rec.active && !rec.raw.is_empty(), "Stop a non-empty recording before exporting");
                        (rec.raw.clone(), rec.clean.clone())
                    };
                    // File I/O must never hold the audio worker's recording lock.
                    write_wav(&raw_path, &raw)?; write_wav(&clean_path, &clean)?;
                    Ok(json!({"type":"ack","rawPath":raw_path,"cleanPath":clean_path}))
                }
                "shutdown" => { if let Some(mut session) = current.take() { session.stop(); } Ok(json!({"type":"ack"})) }
                _ => bail!("Unknown operation"),
            }
        })();
        let mut response = match result { Ok(x) => x, Err(error) => json!({"type":"error","message":error.to_string()}) };
        response["requestId"] = json!(request.id); events.send(response)?;
        if request.op == "shutdown" { break; }
    }
    if let Some(mut session) = current { session.state.stop.store(true, Ordering::Relaxed); session.stop(); }
    drop(events); let _ = writer.join();
    Ok(())
}
fn write_wav(path: &std::path::Path, samples: &[f32]) -> Result<()> {
    let file = OpenOptions::new().write(true).create_new(true).open(path)?;
    let mut wav = hound::WavWriter::new(file, hound::WavSpec { channels: 1, sample_rate: RATE as u32, bits_per_sample: 32, sample_format: hound::SampleFormat::Float })?;
    for &sample in samples { wav.write_sample(sample)?; } wav.finalize()?; Ok(())
}
fn self_test() -> Result<()> {
    let mut reports = Vec::new();
    for name in ["rnnoise", "deepfilter"] {
        let t = Instant::now(); let mut pipeline = Pipeline::new(Settings { engine: name.into(), ..Settings::default() })?;
        let init_ms = t.elapsed().as_secs_f64() * 1000.;
        let mut timings = vec![]; let mut raw_energy = 0f64; let mut clean_energy = 0f64; let mut seed = 123456789u32;
        for n in 0..400 {
            let input = std::array::from_fn(|i| { seed = seed.wrapping_mul(1664525).wrapping_add(1013904223); let noise = ((seed >> 8) as f32 / 16777216. - 0.5) * 0.05; let voice = if (n / 50) % 2 == 0 { 0.1 * (std::f32::consts::TAU * 170. * (n * FRAME + i) as f32 / RATE as f32).sin() } else { 0. }; noise + voice });
            let result = pipeline.process(&input, false, false);
            anyhow::ensure!(result.clean.iter().all(|x| x.is_finite() && x.abs() <= 1.), "Invalid DSP output");
            timings.push(result.processing_ms);
            if n > 250 && (n / 50) % 2 == 1 { raw_energy += input.iter().map(|x| (*x as f64).powi(2)).sum::<f64>(); clean_energy += result.clean.iter().map(|x| (*x as f64).powi(2)).sum::<f64>(); }
        }
        timings.sort_by(f64::total_cmp);
        reports.push(json!({"requested":name,"selected":pipeline.selected,"initializationMs":init_ms,"p95ProcessingMs":timings[380],"syntheticNoiseReductionDb":10. * (raw_energy / clean_energy.max(1e-12)).log10(),"reason":pipeline.reason,"note":"Synthetic signal smoke test; not a speech-quality or hardware-routing benchmark."}));
    }
    println!("{}", serde_json::to_string_pretty(&reports)?); Ok(())
}
fn process_file(args: &[String]) -> Result<()> {
    anyhow::ensure!(args.len() >= 2, "INPUT.wav OUTPUT.wav [engine] required");
    let mut reader = hound::WavReader::open(&args[0])?;
    anyhow::ensure!(reader.spec().channels == 1 && reader.spec().sample_rate == RATE as u32, "Fixture must be mono 48 kHz WAV");
    let spec = reader.spec();
    let samples: Vec<f32> = match spec.sample_format {
        hound::SampleFormat::Float => reader.samples::<f32>().collect::<std::result::Result<_,_>>()?,
        hound::SampleFormat::Int => reader.samples::<i32>().map(|x| x.map(|v| v as f32 / 2f32.powi(spec.bits_per_sample as i32 - 1))).collect::<std::result::Result<_,_>>()?,
    };
    let mut pipeline = Pipeline::new(Settings { engine: args.get(2).cloned().unwrap_or_else(|| "auto".into()), ..Settings::default() })?;
    let mut clean = Vec::with_capacity(samples.len() + RATE);
    for chunk in samples.chunks(FRAME) { let mut frame = [0.; FRAME]; frame[..chunk.len()].copy_from_slice(chunk); clean.extend(pipeline.process(&frame, false, false).clean); }
    let delay = pipeline.delay_samples();
    for _ in 0..delay.div_ceil(FRAME) { clean.extend(pipeline.process(&[0.; FRAME], false, false).clean); }
    let output = &clean[delay..delay+samples.len()]; write_wav(std::path::Path::new(&args[1]), output)?;
    println!("{}", json!({"engine":pipeline.selected,"frames":samples.len(),"delayCompensatedSamples":delay,"reason":pipeline.reason})); Ok(())
}
