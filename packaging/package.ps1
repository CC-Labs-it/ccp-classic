[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime,
    [ValidatePattern('\A\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\z')][string]$Version = '1.0.0-preview.1',
    [string]$DotNet = 'dotnet',
    [string]$InnoCompiler,
    [switch]$SkipTests,
    [switch]$RequireInstaller,
    [switch]$LocalReview,
    [switch]$SignWindows
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
$windowsPackage = $Runtime -eq 'win-x64'
if ($SignWindows -and !$windowsPackage) { throw 'Authenticode signing requires win-x64.' }
if (($windowsPackage -and !$IsWindows) -or (!$windowsPackage -and !$IsLinux)) {
    throw "Build $Runtime on its matching operating system so native permissions and archives are preserved."
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$output = Join-Path $repo 'artifacts/packages'
$work = Join-Path $repo "artifacts/package-work/$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
$name = "CCP.Classic-$Version-$Runtime"
$payload = Join-Path $work $name
$built = [System.Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Path $output, $payload -Force | Out-Null
Push-Location $repo
try {
    if (!$SkipTests) {
        & $DotNet test 'CCP.Classic.slnx' -c Release -m:4
        if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
    }
    $publish = @('publish', 'src/CCP.Classic.App/CCP.Classic.App.csproj', '-c', 'Release', '-r', $Runtime,
        '--self-contained', 'true', '--output', $payload, '--artifacts-path', (Join-Path $work 'build'),
        "-p:Version=$Version", '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-m:4')
    if ($windowsPackage) { $publish += @('-p:VlcWindowsX64Enabled=true', '-p:VlcWindowsX86Enabled=false', '-p:VlcWindowsArm64Enabled=false') }
    & $DotNet @publish
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    $analysisSourcesComplete = $true
    if ($windowsPackage) {
        $analysisProof = & (Join-Path $PSScriptRoot 'analysis-helper.ps1') -Payload $payload -SourceOutput $output -Version $Version -AllowPendingSources:$LocalReview
        $analysisSourcesComplete = $analysisProof.sourceBundleComplete
        if ($analysisProof.sourceArchive) { $built.Add($analysisProof.sourceArchive) }
    }
    foreach ($file in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $payload }
    Copy-Item -LiteralPath (Join-Path $repo 'docs/INSTALL.md') -Destination (Join-Path $payload 'INSTALL.md')
    & (Join-Path $PSScriptRoot 'collect-notices.ps1') -Runtime $Runtime -Payload $payload `
        -AssetsPath (Join-Path $work 'build/obj/CCP.Classic.App/project.assets.json')
    Copy-Item -LiteralPath (Join-Path $repo 'src/CCP.Classic.App/Assets/Fonts/Fredoka.OFL.txt') -Destination (Join-Path $payload 'licenses/Fredoka-OFL.txt')
    $required = @('CCP.Classic.App.dll', 'CCP.Classic.App.runtimeconfig.json', 'Starter/Images/spiral.png',
        'Starter/Words/focus.mp3', 'Starter/Words/breathe.mp3', 'Starter/Words/relax.mp3', 'Starter/Words/listen.mp3', 'Starter/Words/deeper.mp3',
        'Starter/Audio/Bubbles/Pop.mp3', 'Starter/Audio/Bubbles/Pop2.mp3', 'Starter/Audio/Bubbles/Pop3.mp3',
        'Starter/Spirals/spiral.gif', 'Starter/Spirals/spiral_corner.gif', 'Starter/Barks/flash_1.mp3', 'licenses/Fredoka-OFL.txt')
    $required += if ($windowsPackage) { @('CCP.Classic.App.exe', 'coreclr.dll', 'hostfxr.dll', 'libSkiaSharp.dll',
        'libHarfBuzzSharp.dll', 'libvlc/win-x64/libvlc.dll', 'libvlc/win-x64/libvlccore.dll',
        'libvlc/win-x64/plugins/audio_output/libdirectsound_plugin.dll', 'Analysis/ffmpeg.exe', 'Analysis/analysis-helper.json') } else {
        @('CCP.Classic.App', 'libcoreclr.so', 'libhostfxr.so', 'libSkiaSharp.so', 'libHarfBuzzSharp.so')
    }
    foreach ($file in $required) {
        $path = Join-Path $payload $file
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Missing package file: $file" }
    }
    if (!$windowsPackage) {
        if (!(Test-Path -LiteralPath '/usr/bin/ffmpeg' -PathType Leaf)) { throw 'Linux analysis requires the distribution ffmpeg package.' }
        foreach ($file in @('CCP.Classic.App', 'libSkiaSharp.so', 'libHarfBuzzSharp.so')) {
            $dependencies = & ldd (Join-Path $payload $file) 2>&1
            if ($LASTEXITCODE -ne 0 -or ($dependencies -match 'not found')) { throw "Unresolved Linux dependency for ${file}: $dependencies" }
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath @((Join-Path $payload 'Starter/Words'), (Join-Path $payload 'Starter/Audio/Bubbles'), (Join-Path $payload 'Starter/Barks')) -File) {
        if ($file.Length -lt 256) { throw "Starter recording is missing or an LFS pointer: $($file.Name)" }
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $payload 'Starter/Spirals') -Filter '*.gif' -File) {
        if ($file.Length -lt 256) { throw "Starter animation is missing or an LFS pointer: $($file.Name)" }
    }
    if ($SignWindows) { & (Join-Path $PSScriptRoot 'sign-windows.ps1') -Path (Join-Path $payload 'CCP.Classic.App.exe') }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not record source revision.' }
    $dirty = @(& git status --porcelain).Count -gt 0
    @{ product = 'CCP Classic'; company = 'CC labs Srls'; version = $Version; runtime = $Runtime; sourceCommit = $commit; dirty = $dirty;
        sdk = (& $DotNet --version).Trim(); selfContained = $true; signed = [bool]$SignWindows; ownerLocalReview = [bool]$LocalReview;
        analysisSourceBundleComplete = $analysisSourcesComplete; externalDistributionBlocked = !$analysisSourcesComplete } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payload 'package.json') -Encoding utf8NoBOM
    if ($windowsPackage) {
        $archive = Join-Path $output "$name.zip"
        Compress-Archive -Path (Join-Path $work $name) -DestinationPath $archive -CompressionLevel Optimal -Force
        $built.Add($archive)
        if (!$InnoCompiler) {
            $candidates = @((Join-Path ${env:LOCALAPPDATA} 'Programs/Inno Setup 6/ISCC.exe'),
                (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'))
            $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        }
        if (!$InnoCompiler) {
            if ($RequireInstaller) { throw 'Inno Setup 6 is required. Supply -InnoCompiler with the installed ISCC.exe path.' }
            Write-Warning 'Installer was not built. Install Inno Setup 6 and rerun with -InnoCompiler.'
        } else {
            $fileVersion = ($Version -split '-')[0] + '.0'
            & $InnoCompiler '/Q' "/DPayloadDir=$payload" "/DRepoRoot=$repo" "/DAppVersion=$Version" "/DFileVersion=$fileVersion" `
                "/DOutputDir=$output" (Join-Path $PSScriptRoot 'windows.iss')
            if ($LASTEXITCODE -ne 0) { throw 'Windows installer compilation failed.' }
            if ($SignWindows) { & (Join-Path $PSScriptRoot 'sign-windows.ps1') -Path (Join-Path $output "$name-setup.exe") }
            $built.Add((Join-Path $output "$name-setup.exe"))
        }
    } else {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launch-linux.sh') -Destination (Join-Path $payload 'CCP.Classic')
        & chmod 755 (Join-Path $payload 'CCP.Classic') (Join-Path $payload 'CCP.Classic.App')
        if ($LASTEXITCODE -ne 0) { throw 'Could not set executable permissions.' }
        $archive = Join-Path $output "$name.tar.gz"
        & tar -czf $archive -C $work $name
        if ($LASTEXITCODE -ne 0) { throw 'Linux archive creation failed.' }
        $built.Add($archive)
    }
    foreach ($path in $built) {
        $file = Get-Item -LiteralPath $path
        [IO.File]::WriteAllText($file.FullName + '.sha256',
            "$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($file.Name)`n", [Text.Encoding]::ASCII)
    }
    foreach ($path in $built) { Get-Item -LiteralPath $path, ($path + '.sha256') | Select-Object Name, Length }
} finally { Pop-Location }
