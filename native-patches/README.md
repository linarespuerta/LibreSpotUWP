# Native background-control source

`0001-uwp-background-control.patch` records native commit `b62320452eb02621f0c2315cdabaae8ea412c5fd` against `9f55482` in the sibling librespot repository. Apply with `git apply` to that clean baseline before following the ARM UWP command in docs/BUILD_GUIDE.md. Keep the existing SDK/windows-sys vendor fixes and feature set. The patch adds two FFI exports and preserves existing exports.

The sibling native origin is third-party-owned and was not pushed. This application repository carries the implementation delta, binary hash and tests without claiming ownership of that origin. Pre-existing local dependency/tooling changes are outside this patch; see the build guide for the local environment prerequisites.

The source-importing DealerTransportProbe requires this patched sibling checkout. These host tests do not validate Windows Mobile hardware-slot allocation or Spotify service behavior.
