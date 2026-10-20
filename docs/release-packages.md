# Building and using release packages

These are developer libraries for .NET 10 applications. Application users should use their application's installer or executable.

## Application release archives

The maintained publish helpers live in `tools/`. `Publish-SharedMidi.ps1 -ConsumerRoot`
copies the helpers into each consumer's `dependencies/midi` folder along with the
pinned packages. Consumer builds use these local copies, never a sibling checkout.

`New-WindowsReleaseArchive.ps1` packages only the published standalone EXE and
explicit notice files. It reads the version from the EXE and writes
`<name>-<version>-win-x64.zip` plus a matching `.zip.sha256`.
`Publish-MacApplication.ps1` creates `<name>-<version>-osx-<architecture>.zip` on
the signing Mac, preserving bundle permissions and symbolic links with `ditto`.
Its checksum is generated after signing and any notarization/stapling.
Windows cross-builds remain signing inputs, not Mac release assets.

`Sign-WindowsExecutable.ps1` signs the final standalone Windows EXE before the
consumer creates its ZIP/checksum. It uses
an exact certificate thumbprint from `Cert:\CurrentUser\My`, SHA-256 file and
RFC 3161 timestamp digests, and defaults to `http://time.certum.pl`. SignTool
verification and a trusted Authenticode signature from that same certificate,
with a timestamp, are required. A failure stops the publisher; signing happens
on a temporary sibling copy so a failed attempt does not replace the EXE.

Signing is enabled by `-Sign` or a nonempty `-SigningCertificateThumbprint`
(default: `MBU_WINDOWS_SIGNING_THUMBPRINT`). `-Sign` with no configured certificate
fails before building. No configuration produces a labelled unsigned development
build. The Windows SDK SignTool and Certum card software are build-machine
requirements only. The private key stays on the card; use its native PIN prompt,
never a script argument or environment variable. SignTool is discovered on PATH
or in the Windows 10/11 SDK; override with `-SignToolPath` / `MBU_SIGNTOOL_PATH`.
Consumer release documentation contains commands and the remaining hardware
qualification steps. No certificate or token is needed for the isolated helper
regressions: `./tools/Test-WindowsSigning.ps1`.

Applications upload these ZIPs and checksums to GitHub Releases; their
`docs/github-releases.md` files describe the process. The helper library's own
`.nupkg` and `.snupkg` files remain NuGet archives and need no extra ZIP wrapper.
Archive creation requires no signing credentials. Public Mac releases require
a Developer ID Application identity and notarization profile in the signing
Mac's keychain; never commit certificates, private keys or credentials.

The dependency audit's current locally built/pinned package set is **0.3.10**,
including the embedded Windows native runtime closure. It has not been published
as a GitHub release by this audit. The v0.3.6 download instructions below identify
the older public release, not the corrected application dependency set.

## Download v0.3.6

Open the [v0.3.6 GitHub release](https://github.com/PaulAngus/MIDIHelperLibrary/releases/tag/v0.3.6) and expand **Assets**. There are four `.nupkg` library packages and four matching `.snupkg` symbol packages:

| Package | Use |
| --- | --- |
| `MightBeUseful.Midi` | Platform-neutral messages, codecs, and port contracts. |
| `MightBeUseful.Midi.Rtp` | Platform-neutral RTP-MIDI transports and diagnostics; depends on the core package. |
| `MightBeUseful.Midi.CoreMidi` | macOS adapters; depends on the core and RTP packages. |
| `MightBeUseful.Midi.Windows` | Windows adapters; depends on the core package. |

Download all four `.nupkg` files into a local folder so the shared dependencies are available. Keep their version at **0.3.6**. These files can be used directly as a local NuGet feed; do not extract them. Third-party dependencies are restored from NuGet.org, so the first restore requires internet access unless those dependencies are already cached.

The `.snupkg` files contain debugging symbols and are optional for normal builds and execution. Save the matching files if you need to debug the libraries. GitHub release attachments are not a symbol server: a debugger will not discover these symbols automatically. For manual debugging, extract the matching portable PDBs and point your debugger's symbol search path at them; the source must match the `v0.3.6` tag. See Microsoft's [symbol package guidance](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg).

## Use the packages in a project

For a new project, install the .NET 10 SDK, then run:

```powershell
dotnet new console --framework net10.0 -n MidiPackageDemo
Set-Location MidiPackageDemo
New-Item -ItemType Directory -Path dependencies/midi -Force
```

Place the downloaded `.nupkg` files in `MidiPackageDemo/dependencies/midi`. Create `NuGet.Config` beside the project's `.csproj` with:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="midi-release" value="dependencies/midi" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="midi-release">
      <package pattern="MightBeUseful.Midi*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Add the package your application needs, for example RTP-MIDI, then build:

```powershell
dotnet add package MightBeUseful.Midi.Rtp --version '[0.3.6]'
dotnet build -c Release
```

The brackets pin the direct reference to exactly 0.3.6. NuGet restores its dependencies automatically. For an existing project, merge the feed and mappings into its existing configuration instead of replacing it. See Microsoft's [local feed documentation](https://learn.microsoft.com/en-us/nuget/hosting-packages/local-feeds).

Windows adapter projects must use a Windows target such as `net10.0-windows10.0.19041.0`; CoreMIDI adapters require macOS at runtime. Installing the Windows package does not install the optional Windows MIDI Services native API. See [architecture and deployment](architecture.md) for that explicit setup, backend selection, and macOS architecture limits.

## Build the release packages yourself

To build all four packages, use Windows, the .NET 10 SDK, PowerShell 7, Git, and internet access for dependencies. In a new directory, run:

```powershell
git clone --branch v0.3.6 https://github.com/PaulAngus/MIDIHelperLibrary.git MIDIHelperLibrary-v0.3.6
Set-Location MIDIHelperLibrary-v0.3.6
./Publish-SharedMidi.ps1
```

With no `-ConsumerRoot` argument, this script prepares the checksum-verified Windows build dependency, runs the automated tests, and builds the packages without copying them into another application. It produces four `.nupkg` and four `.snupkg` files in `artifacts/packages/0.3.6/`. Use the `.nupkg` files in the local feed described above. Preparing the build dependency does not install the optional Windows native API.

The complete packaging script runs on Windows. On macOS, build/test the platform-neutral and CoreMidi projects individually; native macOS and physical device compatibility still require separate validation.

For maintainers updating the existing applications, [building and updating consumers](architecture.md#building-and-updating-consumers) documents `-ConsumerRoot`, package checksums, and the native virtual MIDI self-test. Preserve published package bytes; use a new version when releasing changed library code.
