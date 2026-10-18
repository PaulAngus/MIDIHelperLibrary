# MIDIHelperLibrary

One independently built and versioned MIDI library for PresetMaestro, Preset Sender and MIDI-Forwarder, maintained in [PaulAngus/MIDIHelperLibrary](https://github.com/PaulAngus/MIDIHelperLibrary).

The NuGet packages and .NET namespaces retain the `MightBeUseful.Midi` names. Moving this repository does not change the pinned 0.3.1 packages used by the three applications.

The platform-neutral package owns messages, MIDI 1.0/UMP codecs and port contracts. The Windows package owns both Windows MIDI Services and legacy WinRT/WinMM handling. The CoreMidi package owns the standard DryWetMIDI adapter, the bulk RtMidi adapter and native virtual ports. The platform-neutral Rtp package owns RTP command coding and explicit-initiator, loopback relay and diagnostic responder session lifecycles; CoreMidiNetworkPort only validates the native endpoint and delegates to it. Application routing, device protocols and settings remain in the applications.

Version 0.3.1 is the initial qualified migration release. See [architecture and deployment](docs/architecture.md) for backend selection, optional Windows API setup, package updates and validation limits.

Release 0.3.5 extracts shared RTP-MIDI for AI Toolkit for Fractal and MIDI Forwarder. See [RTP policies](docs/rtp-midi.md). This is an offline-tested source release, not new hardware qualification.

Release 0.3.6 moves the loopback diagnostic responder, clocks, feedback and counters into `MightBeUseful.Midi.Rtp`. Diagnostic failure policy remains distinct from the production relay. The toolkit uses the existing shared CC/PC encoders after its own validation. Both affected consumers carry exact 0.3.6 packages.
