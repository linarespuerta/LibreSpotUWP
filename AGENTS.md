# Lumia HiFi engineering instructions

- Write source, comments, documentation, commits, and pull requests in English. Report progress in Spanish.
- Preserve upstream attribution, `LICENSE.md` (CC BY-NC-SA 4.0), the application package identity, and Spotify authentication boundaries.
- Target Windows 10 Mobile ARM32 first. Do not substitute ARM64 desktop validation for Lumia validation.
- Do not commit credentials, tokens, audio keys, certificates with private keys, session blobs, device IPs, or diagnostic logs.
- Keep changes small and independently testable. Inspect the Rust FFI implementation before changing managed interop structures.
- Do not enable Connect discovery by default until it is behind a persisted opt-in flag, has the right capability, and passes physical-device validation.
- Treat Spotify discovery, network listeners, audio format, and Home Assistant state as hardware/integration claims: label them unverified until tested on the Lumia and LAN.
- Never alter the user’s Home Assistant or Docker deployment without explicit approval.
- Preserve a sideload rollback package before installing a changed APPX on the Lumia.
