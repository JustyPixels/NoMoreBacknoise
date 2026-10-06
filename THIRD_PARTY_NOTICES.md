# Third-party notices

The top-level MIT license applies to NoMoreBacknoise++ original source. It does not replace any upstream license.

| Dependency | Pinned revision / version | License |
|---|---|---|
| [RNNoise](https://github.com/xiph/rnnoise) | v0.1, `cdf196b1e9de2f8ff1003328ebf9a4316477429d` | BSD-3-Clause; retain COPYING |
| [DeepFilterNet / libDF / DF3 weights](https://github.com/Rikorose/DeepFilterNet) | v0.5.6, `978576aa8400552a4ce9730838c635aa30db5e61` | MIT OR Apache-2.0; retain both license texts |
| [wasapi](https://github.com/HEnquist/wasapi-rs) | 0.25.0, locally patched silent-buffer handling | MIT, Henrik Enquist; LICENSE.txt retained in third_party/wasapi |
| [Microsoft SYSVAD](https://github.com/microsoft/Windows-driver-samples) | `2dc3fd3a0cc84a2933f2194e7ec0871584979071` | MIT; source generated only for driver development |
| [.NET / Windows Desktop runtime](https://github.com/dotnet/runtime) | .NET 10, resolved by publish | MIT and runtime third-party notices included by publish |
| [NSIS](https://nsis.sourceforge.io/) | 3.11, packaging tool only | NSIS/zlib and bundled component licenses; tool is not redistributed |

DF3 model archive `DeepFilterNet3_onnx.tar.gz` SHA256:

`c94d91f70911001c946e0fabb4aa9adc37045f45a03b56008cb0c8244cb63616`

Bootstrap verifies source commits and the model hash. Original preprocessing, normalization, recurrent/streaming state and spectral synthesis use libDF directly. RNNoise's published C implementation and bundled trained model are compiled statically. The Windows build enables math constants and replaces eight C99 variable-length stack arrays with same-sized `_alloca` allocations in generated build files for MSVC 2022 compatibility. Upstream checked-out source remains unchanged; these portability changes retain the original algorithm and notices.

Rust direct/transitive versions are locked in Cargo.lock. Packaging copies each downloaded crate's LICENSE/COPYING/NOTICE files and generates a complete dependency inventory in `third-party/RUST-INVENTORY.txt`; runtime notices remain alongside the executable. RustFFT, realfft, ndarray, Tract/ONNX, hound, serde, rtrb, Windows bindings and their dependencies retain the licenses recorded there.

VB-CABLE is not bundled and is not covered by this project's MIT license. Obtain it from [VB-Audio](https://vb-audio.com/Cable/). The application does not change or claim ownership of the cable.
