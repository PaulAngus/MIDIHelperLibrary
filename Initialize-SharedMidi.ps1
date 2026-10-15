[CmdletBinding()]
param([switch]$EnableWindowsServices, [switch]$PrepareBuild)

$ErrorActionPreference = 'Stop'
if (-not $EnableWindowsServices -and -not $PrepareBuild) { return }
if ($EnableWindowsServices -and ([Environment]::OSVersion.Platform -ne 'Win32NT' -or [Environment]::OSVersion.Version -lt [version]'10.0.26200')) {
    throw 'The optional Windows MIDI Services API can only be installed on Windows 11 25H2 or later. Legacy MIDI needs no installation.'
}
$taskVersion = '0.99.88-preview.10'
$taskPackageFolder = Join-Path $PSScriptRoot 'packages'
$taskPackage = Join-Path $taskPackageFolder "Windows.Devices.Midi2.$taskVersion.nupkg"
$taskExpectedHash = '6DAF121A3F76A4A1E0D46521C5072AC576477448C9332048383BB58960A4CE69'
New-Item -ItemType Directory -Path $taskPackageFolder -Force | Out-Null
if (-not (Test-Path -LiteralPath $taskPackage) -or (Get-FileHash -LiteralPath $taskPackage -Algorithm SHA256).Hash -ne $taskExpectedHash) {
    $taskDownload = $taskPackage + '.download'
    Invoke-WebRequest "https://github.com/microsoft/MIDI/releases/download/inbox-preview-10/Windows.Devices.Midi2.$taskVersion.nupkg" -OutFile $taskDownload
    if ((Get-FileHash -LiteralPath $taskDownload -Algorithm SHA256).Hash -ne $taskExpectedHash) { throw 'The Microsoft MIDI preview package checksum does not match.' }
    Move-Item -LiteralPath $taskDownload -Destination $taskPackage -Force
}
if (-not $EnableWindowsServices) { Write-Output "Verified Windows MIDI build dependency: $taskPackage"; return }
$taskArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if ($taskArchitecture -notin @('x64','arm64')) { throw "Unsupported MIDI component architecture: $taskArchitecture" }
$taskDestination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "MightBeUseful\Midi\$taskVersion\$taskArchitecture"
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchive = [System.IO.Compression.ZipFile]::OpenRead($taskPackage)
try {
    foreach ($taskName in @('Windows.Devices.Midi2.dll','Windows.Devices.Midi2.pri')) {
        $taskEntry = $taskArchive.GetEntry("runtimes/win-$taskArchitecture/native/$taskName")
        if ($null -eq $taskEntry) { throw "Microsoft package is missing $taskName." }
        $taskFile = Join-Path $taskDestination $taskName
        $taskTemporary = $taskFile + '.new'
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($taskEntry, $taskTemporary, $true)
        if ((Test-Path -LiteralPath $taskFile) -and (Get-FileHash -LiteralPath $taskFile).Hash -eq (Get-FileHash -LiteralPath $taskTemporary).Hash) {
            Remove-Item -LiteralPath $taskTemporary
        } else {
            Move-Item -LiteralPath $taskTemporary -Destination $taskFile -Force
        }
    }
} finally { $taskArchive.Dispose() }
Write-Output "Optional Windows MIDI API installed for all three applications: $taskDestination"
