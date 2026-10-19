# Goal 2: dedicated Hi-Fi Now Playing

Date: 2026-10-18. Status: planned; next delivery begins with R0 remote-control continuity, then the Hi-Fi view. No runtime code changed in this planning delivery.

## Outcome and boundaries

Turn the Lumia's player into a calm, legible music-receiver display beside the vintage Marantz: large album artwork, clear track/artist/album, accurate progress, simple transport and volume controls, and honest connection state. Support portrait handheld use and landscape placement beside the amplifier. Reuse the existing application and playback services.

Baseline: ARM32 package 1.0.15.0 received initial user acceptance, followed by a controller-lock regression: the Lumia keeps playing but no longer obeys the controller's Pause. This is an open functional defect. Preserve working playback, family-account transfer, lock-screen controls and background lifecycle while resolving it. Keep package identity, current authentication and native DLL unchanged for UI-only milestones. This is visual Hi-Fi styling, not Goal 4 audio optimization; do not display unmeasured bit-perfect, lossless, DAC, sample-rate or output-format claims.

## R0 prerequisite: wireless control survives controller lock

The primary appliance interaction is Spotify on a household member's phone. The Lumia must remain controllable after that phone locks/backgrounds, without walking over to the receiver or maintaining a queue on its screen. Autonomous audio with an unresponsive remote is a failed result. See the reopened investigation, source anchors and owner/guest acceptance matrix in [STABILITY_PLAN.md](STABILITY_PLAN.md#reopened-controller-lock-loses-remote-control-2026-10-18).

Implement and qualify the evidence-backed R0 correction before accepting the next combined UI candidate. Test controller lock independently from Lumia lock, remote commands from both lock screen and reopened Spotify, selected-device/state reconciliation, controller queue edits, account-safe recovery and no receiver interaction. Any native correction is a separate reviewed/tested change, not a reason to rebuild Rust for visual work. A Playing badge must describe playback only; it must not imply that controller reachability has been verified. Layout work can proceed while hardware diagnosis is pending, but does not close R0.

## Source audit and implementation map

| Existing source | Evidence | Planned use |
| --- | --- | --- |
| `Helpers/NavigationHelper.cs` | Player route selects Win10_1507, Win10_1709 or Win11 by appearance setting | Add one shared, opt-in Hi-Fi destination independent of the current appearance; preserve the old route as fallback. Inspect MainPage's shell/back behavior first. |
| `Views/Win10_1709/PlayerPage_Win10_1709.xaml` | Existing cover, transport/progress, lyrics and settings flyout; acrylic background | Reuse control semantics, replace the presentation with solid black and fewer visible elements. Do not clone the entire page into three themes. |
| Corresponding `.xaml.cs: UpdateUI` | Coalesces state via `UiWorkScheduler.RunLatest`, reuses artwork until URI changes | Keep event coalescing; extract the minimum shared presentation logic needed to avoid a fourth divergent player implementation. |
| `UpdateAlbumArtLayout` | Cover capped at 62% of page width and 46% of page height | New layout allocates the remaining space after controls/text and maximizes a square cover. No cropping of album art. |
| `Models/MediaState.cs` | Track, Metadata, ArtworkUri, display title/artist, position, volume, local/remote, offline/recovery state | Use existing state as authority. Album label can use metadata album then native track fallback; verify empty/guest metadata. |
| `Services/MediaService.cs` | Resolves artwork from native track, metadata and offline state; owner UI identity differs from guest playback | Show active playback metadata, not the owner's library. UI must not fetch owner-only album metadata as a prerequisite for guest presentation. |
| `Helpers/ImageUriHelper.cs` | Normalizes Spotify image IDs/URIs; creates BitmapImage; logo fallback | Reuse normalization; add decode sizing and failure handling specifically for the new view, without changing unrelated image consumers. |
| `Helpers/UserSettings.cs` | Persisted appearance/player preferences already exist | Add dedicated Hi-Fi/persistent-display preferences using current settings conventions. |

Source inspection is not visual acceptance. Confirm the installed appearance route and shell behavior on the Lumia during U0; audit equivalent 1507/Win11 event wiring before reusing code. Existing image caching behavior must be measured rather than assumed absent or sufficient.

## Visual direction

Use a solid `#000000` background, warm ivory primary text (`#F1E9D8`), muted gray secondary labels and a restrained amber accent (`#D7A65B`). Treat colors as initial tokens, then check contrast and physical readability. Use Segoe UI already available on the phone; no font download. Large artwork is the visual focus. Avoid simulated brushed metal, glowing panels, blur, spinning discs, continuous equalizers or decorative VU meters requiring animation/data we do not have.

Portrait: small receiver/status header, centered square artwork, title (up to two lines), artist and album, thin progress with elapsed/remaining time, then Previous / Play-Pause / Next and accessible volume. Landscape: cover on the left, metadata/progress/controls on the right. Use effective view dimensions and scale factor, not hardcoded panel resolution. Test narrow heights, larger text settings and rotation; controls must not be pushed off-screen.

Minimum touch targets: 48 effective pixels; primary Play/Pause approximately 64. Initial title sizing 26–32 effective pixels, secondary text 16–20, adjusted by available space. Long titles wrap or truncate with a way to inspect the full value; do not continuously marquee text. A visible Back/Exit control and system Back always restore the prior screen. Keep album/artist navigation available through a secondary menu; the primary surface stays focused on playback.

Show compact, text-backed states: Ready, Connecting, Playing, Paused, Offline, Reconnecting, or a specific actionable error. Do not equate a connected session with a reachable LAN discovery endpoint. Remote-target playback must identify that target rather than imply audio is coming from the Lumia. Empty state: receiver name and “Choose this device in Spotify,” with neutral artwork placeholder. Missing art never leaves the prior track's cover beside new metadata.

## Runtime architecture and artwork contract

1. Create a shared Hi-Fi player view hosted by existing navigation and MediaService, behind a persisted opt-in. Keep the established player as immediate rollback. Entering/exiting the view must not initialize another native session, player, AudioGraph or network listener.
2. Subscribe once on navigation and unsubscribe/cancel on exit. Use the existing latest-state dispatcher scheduling. Navigation, metadata updates, resize and animations must never invoke Play, reconnect, seek or refresh-current-track implicitly.
3. Keep seek drag separate from authoritative position updates using the existing position interaction logic. Delegate volume to the existing coalesced setter. Preserve DJ/narration seek restrictions, queue behavior, shuffle/repeat and lock-screen transport.
4. Use a cancellation token plus track/artwork request generation for artwork. A delayed image for track A cannot replace B after skip, account transfer or navigation. When a track changes, immediately clear/transition the old identity; publish new artwork only if its key is still current.
5. Select the best available image near the rendered size using the metadata/native fallback chain. Do not promise resolution beyond the supplied source. Set decode dimensions before loading; account for logical versus physical pixels explicitly. Start with a 1024-pixel decode ceiling and only current/pending decoded covers in memory (about 8 MiB for two square 32-bit covers, excluding framework overhead). Measure before raising the ceiling.
6. Reuse existing HTTP/image caching when sufficient. If an explicit disk cache is needed, give it a proposed 32 MiB LRU budget, atomic writes, bounded downloads and eviction tests. Cache public cover art only; never credentials, session blobs or complete diagnostic requests. Cache failure must not delay playback. Do not globally clear the existing application cache.
7. Handle ImageOpened/ImageFailed, offline cached images, invalid URIs and decoding errors. Use a neutral app-owned placeholder. Keep only current/pending image subscriptions and release them on exit/memory pressure. Do not download on every position tick.
8. Add a short opacity transition (initially 150–200 ms) only when the correct new image is decoded. Cap at two images, cancel stale transitions and provide an animation-off preference. No per-frame work outside an active transition. Fall back to immediate image replacement if it affects audio or responsiveness.

## Display, fullscreen and OLED behavior

Full-screen means a focused app view; it is not an OS kiosk or lock-screen replacement. Inspect the current shell, request supported fullscreen/status-bar changes only after API checks, and restore prior visibility on exit. Never rely on a desktop fullscreen result as Lumia validation.

Persistent display is optional, off by default. Acquire at most one DisplayRequest while the user explicitly enables it and the Hi-Fi view is visible; release on leaving the view/backgrounding, disabling the setting or disposal. A manual phone lock remains authoritative. Do not use screen-on mode as proof that locked idle discovery was fixed. Expose a concise power/burn-in explanation before enabling this preference.

Propose visual dimming after 60 seconds without interaction: reduce the opacity of UI layers over solid black, with a nonzero readable floor; first tap restores controls without triggering an underlying transport action. This is perceived dimming, not a claim that panel brightness changed. Test the actual appearance on AMOLED. If brightness APIs are considered later, require runtime availability checks and restoration of prior user settings.

Optional element movement can translate the safe content group by 1–3 effective pixels at infrequent intervals while visible; reserve margins, never move hit targets during touch, and stop all timers when hidden. Movement and dark styling reduce persistent static exposure but do not guarantee burn-in prevention. No always-running animation/timer is required in the first visual milestone.

## Delivery milestones

| Slice | Changes | Verification and acceptance | Effort |
| --- | --- | --- | --- |
| R0: remote-control continuity | Diagnose and correct the controller-lock regression using the reopened stability investigation. Preserve active account and Spotify queue/control ownership. | Owner/guest controller lock/unlock, remote transport/volume/seek/queue commands, Lumia on/off and controller-only recovery; audio continuing alone is insufficient. | Unknown until correlated trace; first functional priority |
| U0: baseline and layout | Confirm current appearance, usable view size/scaling, shell/back, art resolution and memory. Define XAML tokens and portrait/landscape arrangements with representative metadata. | Existing player remains reachable; review actual layouts on Lumia with short/long titles and no art. Record baseline memory and navigation behavior. | Small |
| U1: useful Hi-Fi screen | Shared opt-in view, large cover, title/artist/album, progress, transport, volume, honest status, back route. No persistent display or animation yet. | Local presentation/layout/command tests plus ARM build; owner and guest songs, rapid skips, seek, volume, rotate, enter/exit during playback; no playback or lock-screen regression. | Medium; first installable slice |
| U2: artwork quality and transitions | Cancellable image loader, decode sizing, fallback, optional bounded cache and short transitions. | Delayed A/B completion, broken image, offline, navigate-away, rapid skip and memory-pressure tests; no wrong-cover publication and no unbounded decoded images. | Medium |
| U3: optional appliance display | Fullscreen restoration, explicit persistent-display preference, visual dimming and optional movement. | Balanced display request ownership, manual lock respected, first tap wakes only, Back exits, timers stop in background; verify on battery/charger. | Medium |
| U4: qualification and default decision | Tune spacing/contrast/resources from device evidence; assess whether to promote the view. | Two-hour visible playback, repeated owner/guest transfers, screen lock/unlock and navigation. No audible regression or sustained memory growth; compare power/heat observations with baseline. | Small code, elapsed test time |

Resolve R0 before acceptance of the next combined candidate; implement U1 before U2/U3 polish. Share only logic genuinely required by the new screen; avoid a general UI framework refactor. All three old appearance modes remain usable. Do not make the new screen the default until physical acceptance; an opt-in screen can ship first.

## Test and release strategy

Automated tests should exercise behavior: layout breakpoint/cover bounds for constrained heights, stale artwork completion, cancellation, cache budget if introduced, display-request acquire/release, dimming wake-tap handling and one command per user interaction. Reuse existing seek/volume/state-coalescing tests. Source audits complement these tests but do not establish rendered layout or phone API behavior. Test metadata fallback with guest-only native data, narration, missing album and long/non-Latin strings.

Physical matrix: portrait/landscape; playing/paused/loading/offline/recovering; no art/broken art/large art; owner/guest/remote target; rapid next/previous/seek; font scaling; Back and navigating elsewhere; manual lock immediately after playback; lock-screen controls; persistent display on/off; app background/resume. Confirm all controls remain reachable and album art is square and uncropped. Record screenshots and redacted measurements outside Git if they contain private account/device data.

Run the portable suite and one managed ARM build for each code slice. Generate one version-incremented ARM APPX per accepted device candidate, reusing the known-good native DLL. Preserve 1.0.15.0 and its dependencies before testing. Check manifest identity/version, signing certificate, native DLL hash and package contents using the recorded build process. No Rust rebuild unless native source or ABI actually changes.

For performance, record baseline and candidate memory before/after 100 track changes and 20 view enter/exit cycles, steady-state CPU and observed audio continuity. Proposed image-cache/decode limits are implementation budgets, not measured device results. Set final CPU/memory targets after U0; use flat retained-resource trends and the existing OS memory limit as gates. Release image caches under pressure; hidden views perform no progress, dimming or movement work.

## Completion and handoff

Goal 2 is complete when artwork, text, controls and states meet the intended layouts on the Lumia; family playback and background/lock controls still work; remote control survives controller lock as required by R0; image and display lifetimes are bounded; optional display preferences restore correctly; and the physical qualification results are recorded. A desktop screenshot alone is insufficient.

Start the next session with AGENTS.md, this plan and the reopened R0 section of STABILITY_PLAN.md. Diagnose the controller-lock failure before accepting a new candidate. For UI work inspect NavigationHelper, MainPage, the three existing player pages, MediaState, ImageUriHelper and the playback tests; confirm the real Lumia appearance route, then implement U1 behind the opt-in. No design decision is blocking this plan: proceed with black/ivory/amber, shared presentation and conservative default-off display options. User feedback can tune styling after the first device preview.

## Platform references

Primary Microsoft documentation reviewed on 2026-10-18. Verify runtime availability and Mobile behavior before accepting a feature; current documentation may include desktop examples.

- [DisplayRequest](https://learn.microsoft.com/en-us/uwp/api/windows.system.display.displayrequest): balance each active request with release; this does not replace lock-screen lifecycle handling.
- [BitmapImage](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.media.imaging.bitmapimage): decode dimensions and image completion/failure handling for bounded artwork loading.
- [ApplicationView](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.applicationview): fullscreen/view APIs are candidates to check on the installed Mobile build, not assumed supported behavior.
