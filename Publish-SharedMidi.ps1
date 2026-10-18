[CmdletBinding()]
param([string[]]$ConsumerRoot = @())

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Initialize-SharedMidi.ps1') -PrepareBuild
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$taskOutput = Join-Path $PSScriptRoot "artifacts/packages/$taskVersion"
dotnet test (Join-Path $PSScriptRoot 'tests/MightBeUseful.Midi.Tests/MightBeUseful.Midi.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Shared MIDI core tests failed.' }
dotnet pack (Join-Path $PSScriptRoot 'MightBeUseful.Midi.slnx') -c Release -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Shared MIDI packaging failed. Build the Windows provider on Windows.' }
foreach ($taskConsumer in $ConsumerRoot) {
    $taskDestination = Join-Path $taskConsumer 'dependencies/midi'
    if (-not (Test-Path -LiteralPath $taskConsumer -PathType Container)) { throw "Consumer checkout does not exist: $taskConsumer" }
    New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Filter '*.txt' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskDestination -Force }
    Set-Content -LiteralPath (Join-Path $taskDestination 'version.txt') -Value $taskVersion
    Set-Content -LiteralPath (Join-Path $taskDestination 'MidiVersion.props') -Value ('<Project><PropertyGroup><SharedMidiVersion>' + $taskVersion + '</SharedMidiVersion><SharedMidiPackageVersion>[$(SharedMidiVersion)]</SharedMidiPackageVersion></PropertyGroup></Project>')
    foreach ($taskPackage in Get-ChildItem -LiteralPath $taskOutput -Filter '*.nupkg') {
        Copy-Item -LiteralPath $taskPackage.FullName -Destination $taskDestination -Force
        $taskCopied = Join-Path $taskDestination $taskPackage.Name
        if ((Get-FileHash -LiteralPath $taskPackage.FullName).Hash -ne (Get-FileHash -LiteralPath $taskCopied).Hash) { throw "Package copy verification failed: $taskCopied" }
    }
    Get-ChildItem -LiteralPath $taskOutput -Filter '*.nupkg' | ForEach-Object { "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" } | Set-Content -LiteralPath (Join-Path $taskDestination 'packages.sha256')
    Write-Output "Updated pinned MIDI package feed: $taskConsumer"
}
