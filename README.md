# MIDIHelperLibrary

One independently built and versioned MIDI library for PresetMaestro, Preset Sender and MIDI-Forwarder, maintained in [PaulAngus/MIDIHelperLibrary](https://github.com/PaulAngus/MIDIHelperLibrary).

The NuGet packages and .NET namespaces retain the `MightBeUseful.Midi` names. Moving this repository does not change the pinned 0.3.1 packages used by the three applications.

The platform-neutral package owns messages, MIDI 1.0/UMP codecs and port contracts. The Windows package owns both Windows MIDI Services and legacy WinRT/WinMM handling. The CoreMidi package owns the standard DryWetMIDI adapter, the bulk RtMidi adapter and the native virtual/RTP adapters. Application routing, device protocols and settings remain in the applications.

Version 0.3.1 is the initial qualified migration release. See [architecture and deployment](docs/architecture.md) for backend selection, optional Windows API setup, package updates and validation limits.
