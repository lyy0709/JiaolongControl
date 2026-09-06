# Safe tuning fork implementation plan

Base: upstream 10.13.25 (`746546a5d643bc80ebb1ee9e9b3187a25cde7615`).

## Scope and defaults

- Repair confirmed GPU/SMU safety defects; do not change the user's installed application, live hardware settings, BIOS, security configuration, or drivers during development.
- Add an explicitly opt-in GPU voltage/frequency curve editor. Reading/previewing must never write hardware. Driver rejection must remain an error, with an attempted rollback and honest recovery status. No automatic application of experimental curves at boot.
- Integrate detection and a user-initiated official signed PawnIO installer. CPU SMU/CO still requires a kernel driver; Windows power-plan controls do not. No unsigned driver, signature bypass, or driver installation during tests.
- Preserve upstream licensing. Do not upload crash dumps, local configuration, credentials, or personal diagnostics to the fork.
- Use isolated pure-logic/fake-driver tests and frontend mocks; do not stress-test or tune the user's GPU to validate the implementation.

## Tasks

- [x] Create and verify the user's fork; use a separate repair branch.
- [x] Remove write-based capability probes and unsafe read fallbacks; make GPU clock reset persistent.
- [x] Validate SMU inputs, serialize complete transactions, stop saving failed applications, distinguish uninitialized inputs from telemetry, and disable unverified fixed-clock/voltage commands.
- [x] Add bounded curve planning, explicit application, readback, original-offset restoration and a trial rollback timer; never claim unsupported hardware is supported.
- [x] Integrate official PawnIO installer/status and separate basic CPU controls from CO dependency.
- [x] Add regression tests, run frontend typecheck/tests/build and backend build/tests; inspect the UI using a mock bridge only.
- [x] Review diffs, document limitations/recovery, commit and push only to the user's fork.
- [x] User-authorized extension: package a Windows x64 self-contained portable build, verify its contents/checksum, and publish an experimental GitHub prerelease with deployment and rollback instructions. Do not run the app or install drivers.

## Validation and rollback

Tests must cover no writes on reads, invalid values, partial application, driver rejection, rollback failure, curve bounds, reset/startup flags, and installer integrity. Hardware compatibility/stability remains unverified unless separately tested with informed user approval. The installed `D:/JiaoLongControl` application is left untouched; source changes can be reverted by reverting this branch's commits.

## Review and verification — 2026-09-06

- Backend regression runner: **18 passed**, using fake GPU offsets/pure logic and rejected SMU inputs. No native GPU calls, driver initialization, valid SMU writes, installer launch or WPF application startup.
- Frontend Vitest: **16 passed** (6 curve editor, 4 SMU persistence, 6 source safety guards including disabled upstream auto-update). Vue typecheck and production build passed.
- Windows x64 Release build passed: **0 errors, 28 existing/upstream-style warnings** (nullable references, hidden/obsolete fan members, unused catch variable). Vite reports the existing large bundle and external output-directory warnings; these are not hardware tests.
- Real Vue/Arco UI tested with an ephemeral headless Edge and a mocked bridge: read/preview/consent/trial/restore and installer gate passed at 1024px and 560px. Screenshots visually inspected. Fixed input-event stale-preview behavior and SVG grid fill during this review.
- Reviewed initialization/cleanup races: native exports now bind to the loaded PawnIO DLL; a failed SMU module load does not free resources a telemetry reader may use. Official installed header confirms output lengths are element counts.
- Backup version/hash and device/driver/topology checks reject corrupt or mismatched recovery records. The backend trial timer is independent of page lifetime; actual driver hangs/OS crashes are not simulated by the tests and remain a recovery limitation.
- User-provided installer verified without execution: file/product version 2.2.0.0, valid Authenticode signer namazso.eu, exact same SHA-256 as the official bundled 2.2.0 asset. Do not label it 3.1.0 without evidence.
- Known limits: private NVAPI ABI on 4060 Laptop, hardware stability/efficiency, interactive UAC installation and true crash recovery remain untested. CPU multi-step apply is explicitly non-atomic; other tuning programs cannot be coordinated by this process's lock. Broader upstream features are outside this review.
- No crash dumps, credentials, personal diagnostics, installed configuration or mock screenshots are to be published. Only source, tests, documentation, release scripts and the unmodified redistributable official installer belong in the commit. Only verified distribution artifacts belong in release assets.
- Release review: disable the inherited upstream automatic installer updater so it cannot replace the experimental fork. Use a new extraction directory with no old configuration; do not change the existing installation or autostart task.
- Distribution rehearsal passed: Windows x64 self-contained publish with .NET / Windows Desktop 8.0.30; 301 files, production WebRoot, native WebView2 loader, OEM driver files and notices present. Every ZIP entry was opened and SHA-256 compared against its source file. No config.yaml, dump, PDB, log, node_modules or mock preview was packaged. Rebuild from the committed source before uploading; hardware/application startup remains intentionally untested.

## Published result — 2026-09-06

- Source commit and release tag: `fdbb95b33970db98853301b7771afd5a463dc18c`, `v10.13.25-safe.1`, repair branch `fix/safe-tuning-and-driver-setup` in the user's fork. The package assembly's informational version includes this exact commit.
- [Experimental prerelease](https://github.com/lyy0709/JiaolongControl/releases/tag/v10.13.25-safe.1), not latest stable. Uploaded Windows x64 ZIP and SHA256SUMS.txt only; draft published after both server-side asset digests matched local files.
- Final committed-source rebuild: backend 18 / frontend 16 tests passed, typecheck and publish succeeded. All 301 ZIP entries verified; all three embedded PawnIO resources match the source files; official installer signature remains valid.
- ZIP: 75,654,238 bytes, SHA-256 `b95d890ff0ed2319b8aed5ac89e32cf78ddb1e35360e09e09d8877f9a7c433c0`. An unauthenticated public download was fetched and independently hashed to the same value. Remote tag resolves to the source commit above.
- Stopped the mock UI development server. No actual WPF application launch, hardware writes, driver installation, reboot, installed-application/config changes, or stress tests were performed. Remaining next step belongs to the user: new-directory deployment and read-only compatibility checks before any opted-in tuning.

## Follow-up: user-authorized local 4060 compatibility tests and safe.2

The statements above describe the safe.1 release validation, before the subsequent authorization for real GPU testing.

- User reported a curve-layout rejection, requested GitHub research and local hardware testing, then confirmed applications were closed and approved the write test. Two bounded tests were performed through user-approved Windows administrator prompts.
- Identified the actual v1 protocol: device-provided 256-bit mask, core prefix ending before memory-domain slot 127, 28-byte status and 36-byte control strides from 0x40. A matching total buffer size had hidden incorrect safe.1 field placement.
- The independent MIT open-source interop and the corrected project backend each lowered only the 900 mV / slot 72 point by 15 MHz, 2250 to 2235 MHz, then restored. Full raw control, all offsets, P0 clock/voltage settings and voltage boost were verified against the original. Local logs/backups remain in ignored bin/ only.
- Scope stays GPU compatibility: no CPU changes, driver installs, BIOS, startup persistence, stress tests or installed application replacements. Whole-curve application and long-term undervolt stability remain unverified.
- Corrected the native structs/masks/domain boundary, full read-modify-write and backup schema. Added field-offset/domain-boundary/legacy-backup regression tests and opt-in read-only integration probes. Keep local packaging separate from publishing a new GitHub release unless requested.
- Verification: 21 backend regressions passed; 16 frontend tests, Vue typecheck and production build passed. The actual public read/preview API ran on this GPU without WPF/Bridge initialization: both succeeded, with 127 core points, 255 offsets, and no offset change after preview. Package the committed safe.2 source for local testing; do not claim full-curve stability.
- Local safe.2 package built from `f51d532638391def3eb8c4856e96dd101c427fa7`: 303 files, 75,660,023 bytes; every ZIP entry matched its source SHA-256. ZIP SHA-256: `aeb7c9180d667eca74901ce8e3ec91c9d4f2077ff88c2c367d2a2b4ffc56f3e0`. Output: `bin/releases/10.13.25-safe.2/JiaoLongControl-10.13.25-safe.2-win-x64.zip`. This follow-up remains local; GitHub safe.1 was not replaced.
- Final independent read after both hardware tests confirmed 127 core points, zero remaining curve offsets, and the 900 mV anchor restored to 2250 MHz. Both test workers had exited. No persistent tuning was left by the tests.

## Follow-up: safe.3 complete package and publication

The user reported an immovable 8001 MHz memory slider, then explicitly requested a complete build and GitHub Release with a conservative 4060 Laptop curve preset and real hardware testing. User confirmed exiting the running control center before a new administrator prompt.

- [x] Identify/fix memory bounds: base/boost were both 8001; query driver-reported discrete values instead. This GPU returns 405/810/6001/7001/8001 MHz. Reject out-of-list values and textual nvidia-smi failures, including exit-code-zero refusals. Do not claim this proves memory lock write support.
- [x] Add an opt-in 4060 Laptop preset using the device's original 900 mV frequency, with zero existing offsets and no uplift of any point. No universal stability promise, automatic startup curve, memory OC, voltage boost, CPU or BIOS changes.
- [x] Test the actual production full-curve API in an isolated host after explicit consent: two 54-point applications at 900 mV / 2250 MHz, complete independent raw-table/255-offset/P0 checks, manual restoration and the real 60-second timer restoration passed. Final native reads confirmed restoration; recovery workers exited. Logs/backups remain local/ignored.
- [x] Backend regressions: 28 passed. Frontend: 29 passed plus Vue typecheck. Real Vue/Arco mock full GPU page supports the memory selection and preset consent flow, error gates, desktop/narrow layouts; old curve/installer UI smoke also passes. These UI checks complement, not replace, the full-curve hardware test.
- [ ] Final diff/package review, committed-source Windows x64 self-contained build, ZIP entry/hash verification, push to user's fork, and publish an explicitly experimental safe.3 prerelease after matching uploaded digests. Do not alter safe.1 assets, installed software, local configurations or system settings.

Remaining limits: no long-duration game/stress testing, power/voltage savings guarantee, memory-lock hardware write verification, crash/driver-hang recovery certification or full upstream OEM-feature audit. Preset is a conservative trial starting point, not an established stable OC profile. Rollback: restore offsets first, exit, then switch to the previous directory; source may be reverted by commit. Pending release results go below.
