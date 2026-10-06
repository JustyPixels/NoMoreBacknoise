fn main() {
    let root = std::path::Path::new("../.deps/rnnoise");
    assert!(root.join("src/denoise.c").exists(), "Run scripts/bootstrap.ps1 first.");
    let mut build = cc::Build::new();
    build.include(root.join("include")).include(root.join("src"));
    for file in ["denoise.c", "rnn.c", "rnn_data.c", "pitch.c", "kiss_fft.c", "celt_lpc.c"] {
        build.file(root.join("src").join(file));
    }
    build.define("RNNOISE_BUILD", None).define("_USE_MATH_DEFINES", None).warnings(false).compile("rnnoise");
    println!("cargo:rerun-if-changed=../.deps/rnnoise/src");
}
