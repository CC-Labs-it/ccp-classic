[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('\A\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\z')][string]$Version,
    [string]$GnuPG = 'C:/Program Files/Git/usr/bin/gpg.exe',
    [string]$HomeDir = (Join-Path $env:USERPROFILE '.gnupg'),
    [string]$Fingerprint = '54D323FB3798AFE181F4003E910D8162E51F4553')
# Writes CCP.Classic-VERSION-SHA256SUMS for every package of one release and an armored
# detached GPG signature beside it. Linux has no Authenticode, so this signature is what
# proves a tarball came from CC labs. The public key is published at
# https://cclabs.app/keys/cclabs-release-signing.asc.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsWindows) { throw 'Release signing runs on the Windows packaging machine that holds the key.' }
if (!(Test-Path -LiteralPath $GnuPG -PathType Leaf)) { throw 'GnuPG is unavailable.' }
# Git for Windows' MSYS GnuPG, started outside its shell, reads C:\x and C:/x as relative paths.
function ConvertTo-MsysPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path).Replace('\', '/')
    if ($full -match '\A([A-Za-z]):/(.*)\z') { return "/$($Matches[1].ToLowerInvariant())/$($Matches[2])" }
    return $full
}
$HomeDir = ConvertTo-MsysPath $HomeDir
$packages = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/packages'
$files = @(Get-ChildItem -LiteralPath $packages -File | Where-Object {
    $_.Name.StartsWith("CCP.Classic-$Version-", [StringComparison]::Ordinal) -and
    $_.Name -match '\.(tar\.gz|zip|exe)\z' } | Sort-Object Name)
if ($files.Count -eq 0) { throw "No CCP.Classic-$Version packages in artifacts/packages." }
$sums = Join-Path $packages "CCP.Classic-$Version-SHA256SUMS"
$signature = "$sums.asc"
# sha256sum -c format: lowercase hash, two spaces, file name, LF line endings, no BOM.
$lines = foreach ($file in $files) {
    $expected = Join-Path $packages "$($file.Name).sha256"
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ((Test-Path -LiteralPath $expected) -and !((Get-Content -LiteralPath $expected -Raw).StartsWith($hash, [StringComparison]::Ordinal))) {
        throw "Checksum mismatch: $($file.Name)"
    }
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Remove-Item -LiteralPath $signature -ErrorAction SilentlyContinue
$password = [Environment]::GetEnvironmentVariable('CCLABS_GPG_PW', 'User')
if ([string]::IsNullOrWhiteSpace($password)) { throw 'The user CCLABS_GPG_PW credential is unavailable.' }
$signOutput = $null
try {
    # A PowerShell pipe appends CRLF and GnuPG would keep the CR in the passphrase: write it by hand.
    $start = [Diagnostics.ProcessStartInfo]::new($GnuPG)
    foreach ($argument in @('--homedir', $HomeDir, '--batch', '--yes', '--pinentry-mode', 'loopback', '--passphrase-fd', '0',
        '--local-user', $Fingerprint, '--armor', '--detach-sign', '--output', (ConvertTo-MsysPath $signature), (ConvertTo-MsysPath $sums))) {
        $start.ArgumentList.Add($argument)
    }
    $start.RedirectStandardInput = $true; $start.RedirectStandardError = $true; $start.UseShellExecute = $false
    $gpg = [Diagnostics.Process]::Start($start)
    $gpg.StandardInput.Write("$password`n"); $gpg.StandardInput.Close()
    $signOutput = $gpg.StandardError.ReadToEnd(); $gpg.WaitForExit()
    if ($gpg.ExitCode -ne 0) { throw "GPG signing failed ($($gpg.ExitCode)): $($signOutput.Replace($password, '[redacted]'))" }
} finally { $password = $null; $signOutput = $null }
$status = & $GnuPG --homedir $HomeDir --batch --status-fd 1 --verify (ConvertTo-MsysPath $signature) (ConvertTo-MsysPath $sums) 2>$null
if ($LASTEXITCODE -ne 0 -or !($status -match "^\[GNUPG:\] VALIDSIG $Fingerprint ")) { throw 'GPG signature verification failed.' }
Write-Host "Signed $($files.Count) packages: $([IO.Path]::GetFileName($sums)) + .asc"
Write-Host "Key: CC labs Srls Release Signing, $Fingerprint"
