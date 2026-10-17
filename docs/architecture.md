# Shared MIDI architecture

## Consumer-provided app-local API (0.3.4)

`ConfigureAppLocalApiDirectory` lets a consumer provide its own API before the first probe. The explicit `MBU_MIDI_API_DIRECTORY` diagnostic override still takes precedence, and other consumers retain their shared per-user API installation. `ApiDirectory` exposes the selected location for diagnostics. No transport implementation is copied into consumers.

At the user's request, Preset Sender 0.1.3 restores a single standalone EXE. The app embeds the Preview 10 DLL/resource pair as managed resources, then extracts verified copies into an app-private cache only on Windows 11 25H2 or later. Windows 10 does not extract or load them and uses existing hardware/loopMIDI. This is a deliberate change from the earlier universal-package policy below: the EXE contains the API resources even when copied to legacy Windows. Microsoft's [Preview 10 distribution terms](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-10) distinguish distribution from activation and restrict shipping the API below 25H2; runtime gating alone should not be represented as establishing compliance with those distribution terms.

The app's publisher prepares the pinned build dependency without installing the shared API on the build PC. Native validation uses an EXE-only directory and fresh runtime/MIDI cache, verifies embedded-file hashes, concurrent startup, damaged-cache repair, two legacy receivers, all channels and cleanup. Desktop validation must include a real File Explorer launch with the shared per-user API unavailable.

## Missing optional API diagnosis (0.3.3)

The Windows loader now reports the missing native API's full expected path and the explicit per-user setup command instead of falling through to a misleading WinRT "Class not registered" activation error. On the development PC, Explorer launches reproduced the failure while tool launches passed: the tool's local filesystem view contained the optional API, but Explorer's view did not. Installing the verified DLL/resource pair in the desktop-visible per-user location restored discovery through Refresh Devices in the same failing process. Preset Sender 0.1.2 records backend selection and the three MIDI override values in a local diagnostic file. Future validation must include actual Explorer launches and loaded-module verification.

## Startup recovery (0.3.2)

Windows service discovery retries temporary exceptions and unavailable service/loopback results up to three times, 250 ms apart, within a ten-second caller wait. If the native probe outlives that wait, the next explicit capability refresh reuses it and can observe its late result; it does not launch another native call while one is pending. Completed failures can be retried by `WindowsMidiBackend.RefreshCapabilities()`. OS eligibility, explicit legacy selection and machine compatibility modes still bypass the modern probe. Successful capabilities stay stable until explicitly refreshed, preserving endpoint/catalog consistency.

Preset Sender 0.1.1 refreshes capabilities before enumerating ports on a background worker at startup and on Refresh Devices. It updates the virtual-output choice and existing help text together after discovery. Regression coverage includes transient exceptions, unavailable loopback transport, persistent failure, recovery on refresh, late success after timeout without duplicate native calls, and recovery of the sender's saved virtual-output selection without sending MIDI.

The source repository is [PaulAngus/MIDIHelperLibrary](https://github.com/PaulAngus/MIDIHelperLibrary). Package, assembly and namespace names remain `MightBeUseful.Midi`, `MightBeUseful.Midi.Windows` and `MightBeUseful.Midi.CoreMidi` so consumers keep their existing references. Run the following commands from the MIDIHelperLibrary checkout.

The 0.3.1 migration replaces application-owned MIDI transports in PresetMaestro, Preset Sender and MIDI-Forwarder with three independently built packages. The applications retain their UI, settings, routing choices, TCP framing/authentication and device protocols.

| Package | Shared responsibilities |
|---|---|
| `MightBeUseful.Midi` | Input/output and duplex contracts; complete MIDI 1.0 messages; validated packet envelopes; short-message and UMP/SysEx7 codecs; per-group assembly; bounded duplex receive queue; serialized output ownership; pure current/legacy selection policy. |
| `MightBeUseful.Midi.Windows` | The common WinRT input / WinMM output legacy adapter; Windows MIDI Services physical endpoints and virtual loopbacks; native API loading; shared discovery and fallback; diagnostic raw WinMM access. |
| `MightBeUseful.Midi.CoreMidi` | Standard DryWetMIDI/CoreMIDI input/output and an explicit bulk-relay profile retaining RtMidi physical I/O; Apple local RTP transport, codec and paced virtual-source compatibility route. |

## Current and legacy Windows handling

All three apps consult `WindowsMidiBackend`. On Windows 11 25H2 or later, Automatic probes the current API, service and loopback independently. The optional probe has a three-second deadline. Physical endpoints can use Services without loopback; an unavailable loopback leaves the client using explicitly selected legacy cables. Earlier Windows never enters current API activation. A failed optional probe falls back before opening a port; failed or partially transmitted sends are never replayed through another backend.

Set `MBU_MIDI_BACKEND` in the launching process to `auto`, `current` or `legacy`. The default is `auto`. An explicit `current` request fails clearly if the requested operation is unavailable. `legacy` selects the same WinRT-input/WinMM-output implementation even on current Windows. `MIDI_FORWARDER_MIDI_API=winmm` remains a compatibility alias; the canonical variable takes precedence. These preferences do not change Windows' machine-wide Full/Legacy/Hybrid API mode. Automatic respects Legacy mode without probing the current service. In Hybrid mode it defaults to the legacy driver path; select current explicitly for devices on the current driver. An unreadable or unknown machine mode also avoids current-service activation.

New Services selections use endpoint IDs. Output tokens retain their ID association across refreshes and cannot be reassigned to another device by an enumeration-order change. A missing explicitly selected endpoint produces an error rather than silently targeting another device. Legacy name/index and saved application settings retain their existing compatibility boundaries.

Virtual publication uses one shared transient loopback implementation: one public legacy-visible input/output name, plus a UMP-only internal relay. Sender retains its PC-only boundary, singleton ownership and bounded helper process, but delegates SDK calls, endpoint topology and removal to this shared provider. Forwarder's transparent duplex path uses the same implementation and preserves UMP when both endpoints support it.

## Optional current Windows native component

The application EXEs include the managed current and legacy implementations. They do not embed the preview `Windows.Devices.Midi2.dll` or `.pri`. Microsoft restricts distributing that preview API below Windows 11 25H2, so merely guarding activation in a universal EXE is insufficient.

On an eligible Windows PC, run this from any migrated checkout, or from this library checkout:

```powershell
./Initialize-SharedMidi.ps1 -EnableWindowsServices
```

The shared installer downloads Microsoft's SHA-256-pinned Preview 10 package and installs the native API and resources under `%LOCALAPPDATA%/MightBeUseful/Midi/0.99.88-preview.10/<architecture>`. All apps load that same per-user component. The loader can also use a system-provided API. `MBU_MIDI_API_DIRECTORY` supplies an explicit diagnostic directory containing both files. No Windows service or global API-mode setting is changed by the installer.

Preview 10 is the single qualified API version for this migration; Forwarder's former Preview 9 dependency is removed. Future SDK updates happen in this repository and are qualified once against all consumers. Legacy MIDI hardware and existing virtual cables continue to work without this optional component. Sender's local publish wrapper installs it on an eligible development PC; copying its EXE to another PC does not install the component there.

Vendor references: [API system requirements](https://microsoft.github.io/MIDI/kb/minimum-requirements/), [Preview distribution terms](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-11), [Windows API modes](https://microsoft.github.io/MIDI/kb/how-to-change-api-mode/).

## macOS handling

All apps consume the same shared CoreMIDI package. Its Standard profile uses DryWetMIDI 8.0.3 for independently opened inputs and outputs. Its BulkRelay profile retains the existing RtMidi physical duplex implementation required by Forwarder, together with the matching RtMidi catalogue. This capability choice is implemented once in the library, not by checking an application name. Any consumer can request the same profile.

The standard native adapter has a conservative 60,000-byte SysEx transmit ceiling, based on the existing recorded 60 KB success and 70 KB failure. Oversized sends fail explicitly before reaching that adapter. The bulk profile preserves Forwarder's existing large-message implementation rather than substituting an unverified native path. Its native asset remains arm64-only. Consolidating these physical adapters further requires native workload parity tests on Mac.

Forwarder's default Mac client route remains local Apple RTP into the built-in network MIDI driver. Its TCP connection, RTP sequence/loss handling, calibration, SysEx segmentation and reconnect behaviour are preserved. The alternative virtual source retains its 12,000 UMP-byte/second pacing and 8,192-byte burst. Those limits remain scoped to that transport. Maestro's 10 ms device-query spacing remains in its protocol client.

The platform package carries the correct DryWetMIDI native asset for both Mac architectures and the existing arm64 RtMidi asset. Forwarder's application-created virtual compatibility transport is currently packaged for arm64; Maestro/Sender's Intel physical paths use DryWetMIDI. No Windows MIDI dependency is added to Mac releases.

## Building and updating consumers

On Windows with .NET 10 and PowerShell 7:

```powershell
./Initialize-SharedMidi.ps1 -PrepareBuild
dotnet build MightBeUseful.Midi.slnx -c Release
dotnet test tests/MightBeUseful.Midi.Tests/MightBeUseful.Midi.Tests.csproj -c Release
./Publish-SharedMidi.ps1 -ConsumerRoot ../PresetMaestro,../preset-forwarder,../MIDI-Forwarder
dotnet run --project tools/MidiCheck/MidiCheck.csproj -c Release -- --self-test
```

The native self-test creates unique temporary virtual endpoints, exercises current and legacy receivers and removes the endpoints. It does not send to physical hardware. On Mac, build/test the neutral and CoreMidi projects; the Windows WinRT projection is built on Windows.

Each app checks in the selected `.nupkg` files and `packages.sha256` under `dependencies/midi`, with a relative NuGet feed. A clean checkout can build without a sibling library checkout or an externally published first-party feed. These are compiled package copies, not duplicated source implementations. Keep the packages and their references at the same release across all consumers. Increment the package version before a release; do not replace a published package version.

For library development, each application supports an explicit source override:

```powershell
dotnet build -c Release -p:SharedMidiSourceRoot=/absolute/path/to/MIDIHelperLibrary
```

Normal builds do not use that override. Final validation must include ordinary package-based application builds.

## Validation scope

The migration is validated on Windows 11 26H2 using pure policy/codec/lifecycle tests, the existing application suites, native virtual MIDI including multiple legacy receivers, and package/payload checks. Mac dependency graphs and managed tests can run on Windows; native CoreMIDI, Mac signing/launch and physical hardware acceptance still require a Mac. Earlier Windows and supported Mac releases require native release-matrix acceptance; a successful cross-build does not certify those systems.
