# Private Windows native runtime

The provider embeds the Microsoft Windows MIDI API DLL/PRI from the pinned
`Windows.Devices.Midi2` 0.99.88-preview.10 build package and its C++ dependency
closure. Consumers do not install these files or run a bootstrap script.
Only Windows 11 build 26200 or later extracts/loads them. Windows 10 and
earlier Windows 11 use the OS WinMM path without extracting this payload.

The C++ files here came from Microsoft's signed Visual C++ Redistributable
14.51.36247.0, downloaded on 20 October 2026 from
https://aka.ms/vc14/vc_redist.x64.exe.
Installer SHA-256:
`843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c`.
The installer and every copied DLL had a valid Microsoft Authenticode signature.
The installer was unpacked on the development PC; it was not installed on a target.

The x64 native import closure requires `msvcp140.dll`, `vcruntime140.dll` and
`vcruntime140_1.dll`. The arm64 closure requires only `msvcp140.dll` and
`vcruntime140.dll`; the redistributable's arm64 payload also contains an x64
compatibility `vcruntime140_1.dll`, which must not be loaded into an arm64 process.
The remaining CRT imports are Windows Universal CRT API sets, supplied by
Windows 10/11. No debug CRT or files copied from System32 are distributed.

Local redistribution follows Microsoft's
[redistribution terms](https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files)
and [local deployment guidance](https://learn.microsoft.com/cpp/windows/deployment-in-visual-cpp).
Application releases must service these private copies when Microsoft updates them.
`SHA256SUMS` records the exact checked-in DLLs.

`BundledWindowsMidiRuntime` hashes the entire architecture-specific payload into
a version-independent private directory, verifies files, replaces corrupt copies
atomically and preloads the C++ chain by absolute paths. Loader dependency search
is restricted to the DLL's directory and System32. Handles remain loaded for the
process lifetime. The application-supplied API directory and explicit
`MBU_MIDI_API_DIRECTORY` diagnostic override remain supported. Neither makes the
application depend on a globally installed C++ runtime.
