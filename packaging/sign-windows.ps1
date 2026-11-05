[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path,
    [string]$Signer = 'C:/downloads/ActalisCodeSigner-win-x64-latest.new/ActalisCodeSigner.exe')
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Windows signing requires Windows.' }
if (!(Test-Path -LiteralPath $Signer -PathType Leaf)) { throw 'Actalis signer is unavailable.' }
if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Signing target is missing.' }
$password = [Environment]::GetEnvironmentVariable('ACTALIS_PW', 'User')
if ([string]::IsNullOrWhiteSpace($password)) { throw 'The user ACTALIS_PW credential is unavailable.' }
try {
    $signOutput = & $Signer -fu conditioning_control_labs_srls -fp $password -in $Path -ts 2>&1
    $signExit = $LASTEXITCODE
    if ($signExit -ne 0) {
        $safe = ($signOutput | Out-String).Replace($password, '[redacted]')
        throw "Actalis signing failed ($signExit): $safe"
    }
} finally { $password = $null; $signOutput = $null }
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) {
    throw "Signature or timestamp verification failed: $($signature.Status)"
}
Write-Host "Signed and timestamped: $([IO.Path]::GetFileName($Path))"
Write-Host "Publisher: $($signature.SignerCertificate.Subject)"
