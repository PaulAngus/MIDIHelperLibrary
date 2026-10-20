#requires -Version 7.0
[CmdletBinding()]
param([string]$ResultsDirectory = (Join-Path $PSScriptRoot ('../artifacts/signing-tests-' + [guid]::NewGuid().ToString('N'))))

# Isolated control-flow tests. Certificate/trust results and SignTool are fixtures;
# these do not qualify a real certificate, smart card, TSA or Authenticode signature.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Run these signing control-flow tests on Windows.' }
$root = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$helper = Join-Path $PSScriptRoot 'Sign-WindowsExecutable.ps1'
$exe = Join-Path $root 'fixture application.exe'
$tool = Join-Path $root 'fixture signtool.cmd'
$log = Join-Path $root 'signtool.log'
$results = [Collections.Generic.List[object]]::new()
$thumb = '0123456789ABCDEF0123456789ABCDEF01234567'
$state = @{}

# Mock only the helper's certificate lookup; all file operations remain real.
function Get-Item {
    [CmdletBinding()]
    param([string]$LiteralPath)
    if ($LiteralPath.StartsWith('Cert:')) {
        if ($state.MissingCertificate) { throw 'Fixture certificate is missing.' }
        return $state.Certificate
    }
    Microsoft.PowerShell.Management\Get-Item @PSBoundParameters
}
function Get-AuthenticodeSignature {
    [CmdletBinding()]
    param([string]$LiteralPath)
    if ($state.RealTrustCheck) {
        return Microsoft.PowerShell.Security\Get-AuthenticodeSignature @PSBoundParameters
    }
    [pscustomobject]@{
        Status = $state.Status
        SignerCertificate = if ($state.NoSigner) { $null } else { [pscustomobject]@{ Thumbprint = $state.Signer } }
        TimeStamperCertificate = if ($state.Timestamp) { [pscustomobject]@{} } else { $null }
    }
}
Set-Content -LiteralPath $tool -Encoding ascii -Value @'
@echo off
echo %*>>"%MBU_TEST_SIGNTOOL_LOG%"
if not "%1"=="sign" exit /b %MBU_TEST_VERIFY_EXIT%
for %%F in (%*) do set "last=%%~F"
echo fixture-signature>>"%last%"
exit /b %MBU_TEST_SIGN_EXIT%
'@
$environmentNames = @('MBU_WINDOWS_SIGNING_THUMBPRINT', 'MBU_SIGNTOOL_PATH', 'MBU_TEST_SIGNTOOL_LOG', 'MBU_TEST_SIGN_EXIT', 'MBU_TEST_VERIFY_EXIT')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

function Reset-Fixture {
    [IO.File]::WriteAllText($exe, 'unsigned fixture')
    if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log }
    $state.MissingCertificate = $false
    $state.Certificate = [pscustomobject]@{
        Subject = 'CN=Fixture'; HasPrivateKey = $true
        NotBefore = (Get-Date).AddDays(-1); NotAfter = (Get-Date).AddDays(1)
        EnhancedKeyUsageList = @([pscustomobject]@{ ObjectId = '1.3.6.1.5.5.7.3.3' })
    }
    $state.Status = 'Valid'; $state.Signer = $thumb; $state.Timestamp = $true
    $state.NoSigner = $false; $state.RealTrustCheck = $false
    $env:MBU_TEST_SIGN_EXIT = '0'; $env:MBU_TEST_VERIFY_EXIT = '0'
    $env:MBU_WINDOWS_SIGNING_THUMBPRINT = $null; $env:MBU_SIGNTOOL_PATH = $null
}
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Invoke-Case([string]$Name, [scriptblock]$Body) {
    Reset-Fixture
    & $Body
    Assert-True (@(Get-ChildItem -LiteralPath $root -Filter '.sign-*.exe').Count -eq 0) 'A signing temporary file leaked.'
    $results.Add([pscustomobject]@{ Name = $Name; Result = 'PASS'; Evidence = 'isolated fixture' })
    Write-Host "PASS: $Name"
}
function Assert-Rejected([hashtable]$Options, [string]$Message) {
    $rejected = $false
    try { & $helper @Options }
    catch {
        if ($_.Exception.Message -notmatch $Message) { throw }
        $rejected = $true
    }
    Assert-True $rejected 'Expected signing to be rejected.'
    Assert-True ([IO.File]::ReadAllText($exe) -eq 'unsigned fixture') 'A failed signing attempt replaced the EXE.'
}

try {
    $env:MBU_TEST_SIGNTOOL_LOG = $log
    $signed = @{ Sign = $true; SigningCertificateThumbprint = $thumb; SignToolPath = $tool; ExecutablePath = $exe }
    Invoke-Case 'Unconfigured development build stays unsigned' {
        & $helper -ExecutablePath $exe
        Assert-True (-not (Test-Path -LiteralPath $log)) 'Unsigned mode invoked SignTool.'
        Assert-True ([IO.File]::ReadAllText($exe) -eq 'unsigned fixture') 'Unsigned mode changed the EXE.'
    }
    Invoke-Case 'Explicit signing requires a certificate' { Assert-Rejected @{ Sign = $true } 'exact 40-character' }
    Invoke-Case 'Malformed thumbprint rejected' { Assert-Rejected @{ Sign = $true; SigningCertificateThumbprint = 'abc' } 'exact 40-character' }
    Invoke-Case 'Missing certificate rejected' { $state.MissingCertificate = $true; Assert-Rejected $signed 'was not found' }
    Invoke-Case 'Certificate needs a private key' { $state.Certificate.HasPrivateKey = $false; Assert-Rejected $signed 'no associated private key' }
    Invoke-Case 'Expired certificate rejected' { $state.Certificate.NotAfter = (Get-Date).AddDays(-1); Assert-Rejected $signed 'not currently valid' }
    Invoke-Case 'Future certificate rejected' { $state.Certificate.NotBefore = (Get-Date).AddDays(1); Assert-Rejected $signed 'not currently valid' }
    Invoke-Case 'Code-signing EKU required' { $state.Certificate.EnhancedKeyUsageList = @(); Assert-Rejected $signed 'does not permit code signing' }
    Invoke-Case 'Invalid timestamp URL rejected' { $options = $signed.Clone(); $options.TimestampServer = 'file:///fixture'; Assert-Rejected $options 'absolute HTTP or HTTPS' }
    Invoke-Case 'Missing SignTool rejected' { $options = $signed.Clone(); $options.SignToolPath = Join-Path $root 'missing.exe'; Assert-Rejected $options 'SignTool was not found' }
    Invoke-Case 'Preflight does not sign or require a published file' {
        & $helper -CheckOnly -Sign -SigningCertificateThumbprint $thumb -SignToolPath $tool
        Assert-True (-not (Test-Path -LiteralPath $log)) 'Preflight invoked SignTool.'
    }
    Invoke-Case 'Token/signing failure leaves the EXE intact' { $env:MBU_TEST_SIGN_EXIT = '1'; Assert-Rejected $signed 'signing/timestamping failed' }
    Invoke-Case 'Timestamp warning leaves the EXE intact' { $env:MBU_TEST_SIGN_EXIT = '2'; Assert-Rejected $signed 'signing/timestamping failed' }
    Invoke-Case 'Verification failure leaves the EXE intact' { $env:MBU_TEST_VERIFY_EXIT = '1'; Assert-Rejected $signed 'verification failed' }
    Invoke-Case 'Verification warning leaves the EXE intact' { $env:MBU_TEST_VERIFY_EXIT = '2'; Assert-Rejected $signed 'verification failed' }
    Invoke-Case 'Untrusted signature rejected' { $state.Status = 'NotTrusted'; Assert-Rejected $signed 'trusted signature' }
    Invoke-Case 'Wrong signer rejected' { $state.Signer = 'F' * 40; Assert-Rejected $signed 'trusted signature' }
    Invoke-Case 'Missing signer rejected' { $state.NoSigner = $true; Assert-Rejected $signed 'trusted signature' }
    Invoke-Case 'Missing timestamp rejected' { $state.Timestamp = $false; Assert-Rejected $signed 'trusted signature' }
    Invoke-Case 'Real Windows trust check rejects a fake signature' { $state.RealTrustCheck = $true; Assert-Rejected $signed 'trusted signature' }
    Invoke-Case 'Successful flow selects the exact signer, uses SHA-256 and verifies timestamps' {
        & $helper @signed
        Assert-True ([IO.File]::ReadAllText($exe) -match 'fixture-signature') 'Verified temporary output did not replace the EXE.'
        $commands = @(Get-Content -LiteralPath $log)
        Assert-True ($commands.Count -eq 2) 'Expected signing followed by verification.'
        Assert-True ($commands[0] -match "sign /s My /sha1 $thumb /fd SHA256 /tr http://time.certum.pl/ /td SHA256 ") 'Signing flags differ.'
        Assert-True ($commands[1] -match '^verify /pa /all /tw /v ') 'Verification flags differ.'
    }
    Invoke-Case 'Environment configuration enables signing without a switch' {
        $env:MBU_WINDOWS_SIGNING_THUMBPRINT = $thumb; $env:MBU_SIGNTOOL_PATH = $tool
        & $helper -ExecutablePath $exe
        Assert-True (Test-Path -LiteralPath $log) 'Environment configuration did not enable signing.'
    }
    Invoke-Case 'Thumbprint whitespace is normalized and timestamp URL can be overridden' {
        & $helper -ExecutablePath $exe -SigningCertificateThumbprint (" `u{200e}" + $thumb.ToLowerInvariant() + ' ') -SignToolPath $tool -TimestampServer 'https://timestamp.example.test/'
        Assert-True ((Get-Content -LiteralPath $log -Raw) -match '/tr https://timestamp.example.test/ /td SHA256') 'Timestamp override was not forwarded.'
    }
    Invoke-Case 'Explicit empty configuration permits unsigned development' {
        $env:MBU_WINDOWS_SIGNING_THUMBPRINT = $thumb
        & $helper -ExecutablePath $exe -SigningCertificateThumbprint ''
        Assert-True (-not (Test-Path -LiteralPath $log)) 'Explicit unsigned build invoked SignTool.'
    }
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'results.json')
    Write-Host "$($results.Count) signing control-flow checks passed. Evidence: $root"
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
