[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$SourceOutput,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot 'analysis-helper.json'
$specification = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$cache = Join-Path $repo 'artifacts/analysis-helper-cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null

function Test-PinnedFile($Source, [string]$Path) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and (Get-Item -LiteralPath $Path).Length -eq $Source.size `
        -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Source.sha256
}
function Save-LargeArchive($Source, [string]$Target) {
    $parts = Join-Path $cache ('source-' + $Source.sha256.Substring(0, 12))
    New-Item -ItemType Directory -Path $parts -Force | Out-Null
    $length = [long]$Source.size; $chunk = [long](64 * 1024 * 1024); $url = $Source.url
    $count = [int][Math]::Ceiling($length / [double]$chunk)
    $completed = 0..($count - 1) | ForEach-Object -Parallel {
        $index = $_; $start = [long]$index * $using:chunk; $end = [Math]::Min($using:length - 1, $start + $using:chunk - 1)
        $part = Join-Path $using:parts ('part-{0:d3}' -f $index)
        if (!(Test-Path -LiteralPath $part) -or (Get-Item -LiteralPath $part).Length -ne ($end - $start + 1)) {
            & curl.exe --location --fail --silent --retry 3 --retry-all-errors --retry-delay 2 --max-time 180 `
                --max-filesize ($end - $start + 1) --range "$start-$end" --output $part $using:url
            if ($LASTEXITCODE -ne 0) { return }
        }
        if ((Get-Item -LiteralPath $part).Length -eq ($end - $start + 1)) { $index }
    } -ThrottleLimit 4
    if (@($completed).Count -ne $count) { throw 'Public source-cache download failed. Restore its verified retained copy into artifacts/analysis-helper-cache; the upstream Actions artifact expires on 2026-11-14.' }
    $merged = [IO.File]::Create($Target)
    try {
        foreach ($index in 0..($count - 1)) {
            $sourceStream = [IO.File]::OpenRead((Join-Path $parts ('part-{0:d3}' -f $index)))
            try { $sourceStream.CopyTo($merged) } finally { $sourceStream.Dispose() }
        }
    } finally { $merged.Dispose() }
}
function Get-PinnedFile($Source, [string]$Name) {
    if ($Name -ne [IO.Path]::GetFileName($Name)) { throw 'Invalid analysis source filename.' }
    $target = Join-Path $cache $Name
    if (Test-PinnedFile $Source $target) { return $target }
    $verified = Join-Path $cache ([IO.Path]::GetFileNameWithoutExtension($Name) + '.verified' + [IO.Path]::GetExtension($Name))
    if (Test-PinnedFile $Source $verified) { return $verified }
    if ($Source.size -gt 536870912) {
        Save-LargeArchive $Source $target
    } else { Invoke-WebRequest -Uri $Source.url -OutFile $target -TimeoutSec 120 }
    if (!(Test-PinnedFile $Source $target)) { throw "Analysis archive checksum differs: $Name" }
    return $target
}

$archive = Get-PinnedFile $specification.archive 'ffmpeg-win64-lgpl-shared.zip'
$sources = foreach ($source in $specification.sources) { @{ metadata = $source; path = Get-PinnedFile $source $source.file } }
$analysis = Join-Path $Payload 'Analysis'
$notices = Join-Path $Payload 'licenses/analysis'
New-Item -ItemType Directory -Path $analysis, $notices, $SourceOutput -Force | Out-Null
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($file in $specification.files) {
        if ($file.name -ne [IO.Path]::GetFileName($file.name) -or $file.name -notmatch '^(ffmpeg\.exe|(?:avcodec|avdevice|avfilter|avformat|avutil|swresample|swscale)-\d+\.dll)$') { throw 'Unexpected analysis runtime file.' }
        $entry = $zip.GetEntry($specification.archive.root + '/bin/' + $file.name)
        if (!$entry -or $entry.Length -ne $file.size) { throw "Missing analysis runtime: $($file.name)" }
        $target = Join-Path $analysis $file.name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        if (!(Test-PinnedFile $file $target)) { throw "Analysis runtime checksum differs: $($file.name)" }
    }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($specification.archive.root + '/LICENSE.txt'), (Join-Path $notices 'COPYING.LGPLv3'), $true)
} finally { $zip.Dispose() }
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $analysis 'analysis-helper.json')
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $notices 'analysis-helper.json')
$ffmpegSource = ($sources | Where-Object { $_.metadata.name -eq 'FFmpeg' }).path
foreach ($name in @('COPYING.GPLv3', 'LICENSE.md')) {
    $member = 'FFmpeg-' + $specification.sourceCommit + '/' + $name
    $contents = & tar -xOf $ffmpegSource $member
    if ($LASTEXITCODE -ne 0) { throw "Missing FFmpeg notice: $name" }
    [IO.File]::WriteAllText((Join-Path $notices $name), ($contents -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
}
$configuration = & (Join-Path $analysis 'ffmpeg.exe') -hide_banner -buildconf 2>&1
if ($LASTEXITCODE -ne 0 -or ($configuration -match '--enable-gpl|--enable-nonfree') -or !($configuration -match '--enable-shared')) { throw 'The analysis helper is not the expected LGPL shared build.' }
$configuration | Set-Content -LiteralPath (Join-Path $notices 'buildconf.txt') -Encoding utf8NoBOM
$sourceName = "CCP.Classic-$Version-analysis-sources.zip"
$sourceArchive = Join-Path $SourceOutput $sourceName
$readme = @"
FFmpeg analysis helper corresponding source and notices

This archive accompanies the Windows binaries and installer of CCP Classic $Version.
Redistribute it alongside those files, with equivalent access to this source archive.
FFmpeg is unmodified, at commit $($specification.sourceCommit).
The BtbN build recipes are at commit $($specification.buildCommit), target win64 lgpl-shared 9.0.
The original dependency download cache contains the exact dependency working trees,
vendored Rust crates, their full copyright/license texts and build source material.
Open btbn-download-cache.zip, then .cache/cache.tar.gz, then the component .tar.xz files.
The retained source cache is verified against the original GitHub Actions artifact digest.
The build recipes contain component revisions, modifications and build instructions.
FFmpeg uses LGPL version 3 or later; COPYING.GPLv3 is included as required by LGPLv3.
Other libraries retain the terms in their source archive license/copyright files.
No FFmpeg source changes were made by Classic. No GPL/nonfree FFmpeg flavor is shipped.

Runtime shared libraries can be replaced with compatible modified versions. Update
Analysis/analysis-helper.json file sizes and SHA256 values to match those local files.
Classic imposes no restriction on modification or reverse engineering for that purpose.
Playback remains LibVLC. FFmpeg is used only to analyze owner-selected local media.
"@
$readme | Set-Content -LiteralPath (Join-Path $notices 'SOURCE-README.txt') -Encoding utf8NoBOM
# ZipArchive supports ZIP64, including the source-cache member larger than 2 GiB.
$stream = [IO.File]::Create($sourceArchive)
$bundle = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($source in $sources) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bundle, $source.path, $source.metadata.file, [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    }
    foreach ($file in Get-ChildItem -LiteralPath $notices -File) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bundle, $file.FullName, $file.Name, [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    }
} finally { $bundle.Dispose(); $stream.Dispose() }
$proof = @{ sourceArtifact = $sourceName; sourceArchiveSha256 = (Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash.ToLowerInvariant();
    sourceArchiveBytes = (Get-Item -LiteralPath $sourceArchive).Length; runtimeBytes = ($specification.files | Measure-Object size -Sum).Sum }
$proof | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $notices 'package-evidence.json') -Encoding utf8NoBOM
Write-Output $sourceArchive
