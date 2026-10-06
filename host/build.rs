fn main() {
    let root = std::path::Path::new("../.deps/rnnoise");
    assert!(root.join("src/denoise.c").exists(), "Run scripts/bootstrap.ps1 first.");
    let mut build = cc::Build::new();
    build.include(root.join("include")).include(root.join("src"));
    for file in ["denoise.c", "rnn.c", "rnn_data.c", "pitch.c", "kiss_fft.c", "celt_lpc.c"] {
        if std::env::var("TARGET").unwrap().ends_with("-msvc") && ["pitch.c", "celt_lpc.c"].contains(&file) {
            // MSVC 2022 lacks C99 variable-length arrays. Keep the exact upstream
            // lengths and stack lifetime, without altering the checked-out source.
            let mut source = std::fs::read_to_string(root.join("src").join(file)).unwrap();
            let arrays: &[(&str, &str, &str)] = if file == "pitch.c" {
                &[("opus_val16", "x_lp4", "len>>2"), ("opus_val16", "y_lp4", "lag>>2"),
                  ("opus_val32", "xcorr", "max_pitch>>1"), ("opus_val32", "yy_lookup", "maxperiod+1")]
            } else {
                &[("opus_val16", "rnum", "ord"), ("opus_val16", "rden", "ord"),
                  ("opus_val16", "xx", "n"), ("opus_val16", "y", "N+ord")]
            };
            for (kind, name, length) in arrays {
                let declaration = format!("{kind} {name}[{length}];");
                assert_eq!(source.matches(&declaration).count(), 1, "Pinned RNNoise declaration changed");
                source = source.replace(&declaration, &format!("{kind} *{name} = ({kind}*)_alloca(sizeof({kind})*({length}));"));
            }
            let generated = std::path::PathBuf::from(std::env::var("OUT_DIR").unwrap()).join(file);
            std::fs::write(&generated, format!("#include <malloc.h>\n{source}")).unwrap();
            build.file(generated);
        } else { build.file(root.join("src").join(file)); }
    }
    build.define("RNNOISE_BUILD", None).define("_USE_MATH_DEFINES", None).warnings(false).compile("rnnoise");
    println!("cargo:rerun-if-changed=../.deps/rnnoise/src");
}
