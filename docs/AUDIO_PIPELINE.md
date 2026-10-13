# Audio pipeline audit

## Confirmed code path

`AudioFormatProbe` requests 44,100 Hz, stereo PCM and tries F64, F32, S32, S24, packed S24_3, then S16 with `AudioGraph`. It accepts the first creation-compatible format whose reported UWP bytes-per-sample match the Rust format representation. `LibrespotService.BuildConfig` sets Spotify bitrate to 320 and disables native volume normalization.

The managed fallback renderer reads the Rust-owned PCM ring with `AudioFrameInputNode`/`AudioGraph`; it has a 200 ms pre-roll and a producer watchdog. Native WASAPI/XAudio2 backends are selectable and may apply EQ, echo, reverb, and limiter according to settings. Therefore effects, graph conversion, endpoint mix format, system volume, and DAC conversion can all alter the signal.

## Consequences

The source demonstrates an intent to request 44.1 kHz PCM and to avoid Spotify normalization. It does not establish Spotify's delivered codec/format, whether Windows resamples, how analog hardware converts it, or bit-perfect playback. Bit-perfect is not an appropriate claim for this analog-output path without device measurements.

## Validation plan

Record the chosen native format/backend and output endpoint; test each backend with effects disabled, normalization disabled, and fixed volume; collect ring-buffer underflow/watchdog logs; and compare long-playback stability. Document audible/measured results separately from configuration. Avoid dither or resampling changes unless the active conversion path is known and a regression test supports the change.
