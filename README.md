# MIDIHelperLibrary

Current source release: [0.3.10](docs/releases/0.3.10.md).

One independently built and versioned MIDI library for PresetMaestro, Preset Sender and MIDI-Forwarder, maintained in [PaulAngus/MIDIHelperLibrary](https://github.com/PaulAngus/MIDIHelperLibrary).

See [building and using release packages](docs/release-packages.md) to download the libraries, build them from source, or add them to a .NET project.

The NuGet packages and .NET namespaces retain the `MightBeUseful.Midi` names. Maestro, Sender and Forwarder now pin the complete 0.3.10 family; other consumers retain their own pins.

The platform-neutral package owns messages, MIDI 1.0/UMP codecs and port contracts. The Windows package owns both Windows MIDI Services and legacy WinRT/WinMM handling. The CoreMidi package owns the standard DryWetMIDI adapter, the bulk RtMidi adapter and native virtual ports. The platform-neutral Rtp package owns RTP command coding and explicit-initiator, loopback relay and diagnostic responder session lifecycles; CoreMidiNetworkPort only validates the native endpoint and delegates to it. Application routing, device protocols and settings remain in the applications.

Version 0.3.1 is the initial qualified migration release. See [architecture and deployment](docs/architecture.md) for historical backend selection and validation. Current 0.3.10 consumers bundle their own native Windows API and C++ dependencies; they do not need the old optional API setup.

Release 0.3.5 extracts shared RTP-MIDI for AI Toolkit for Fractal and MIDI Forwarder. See [RTP policies](docs/rtp-midi.md). This is an offline-tested source release, not new hardware qualification.

Release 0.3.6 moves the loopback diagnostic responder, clocks, feedback and counters into `MightBeUseful.Midi.Rtp`. Diagnostic failure policy remains distinct from the production relay. The toolkit uses the existing shared CC/PC encoders after its own validation. Both affected consumers carry exact 0.3.6 packages.

## Local validation and releases

Build, test and package locally on Windows or macOS as appropriate. GitHub Actions
is disabled. Follow [release-package instructions](docs/release-packages.md) and
the audited offline profile below. Windows signing-helper regressions run with
`pwsh ./tools/Test-WindowsSigning.ps1`; these do not use a certificate or MIDI.
Keep native MIDI and hardware checks separate and explicitly authorized.

## Application Test Lab

Portable dashboard integration and build instructions: [Test Lab adapter](docs/test-lab.md).

## Offline Windows Test Lab profile

Build an audited bundle without MIDI access:

```text
python packaging/build_test_lab.py --rid win-x64 --config packaging/test-lab-offline-windows.json --output artifacts/offline-windows
```

Every bundle uses fresh build outputs and records its selected test-source hashes in
`offline-policy.json`. All covers only the profile's offline Functional stages.
Native and interactive Desktop requests report unavailable; no hardware or MIDI
routing qualification is implied. No native, virtual, TCP or UDP MIDI connections
are opened or created. In-memory logic fixtures remain included. The normal
development profile and its broader transport tests remain separate.
