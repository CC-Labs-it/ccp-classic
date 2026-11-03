param([Parameter(Mandatory)][string]$Runtime, [Parameter(Mandatory)][string]$Payload, [Parameter(Mandatory)][string]$AssetsPath)
$ErrorActionPreference = 'Stop'
$assets = Get-Content -Raw -LiteralPath $AssetsPath | ConvertFrom-Json -AsHashtable
$destination = Join-Path $Payload 'licenses'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$components = [System.Collections.Generic.List[object]]::new()
$componentIndex = 0
foreach ($identity in $assets.libraries.Keys | Sort-Object) {
    $library = $assets.libraries[$identity]
    if ($library.type -ne 'package') { continue }
    $package = $assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $library.path } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
    if (!$package) { throw "Package metadata is unavailable: $identity" }
    $noticeDirectory = 'nuget-{0:d3}' -f (++$componentIndex)
    $folder = Join-Path $destination $noticeDirectory
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $nuspec = Get-ChildItem -LiteralPath $package -Filter '*.nuspec' -File | Select-Object -First 1
    Copy-Item -LiteralPath $nuspec.FullName -Destination (Join-Path $folder 'package.nuspec')
    [xml]$xml = Get-Content -Raw -LiteralPath $nuspec.FullName
    $metadata = $xml.package.metadata
    $license = $metadata.SelectSingleNode('*[local-name()="license"]')
    $components.Add(@{ package = $identity; noticeDirectory = $noticeDirectory;
        license = if ($license) { $license.InnerText } else { '' }; sha512 = $library.sha512 })
    Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD.PARTY.NOTICES)' } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $folder }
}
$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $Payload 'CCP.Classic.App.runtimeconfig.json') | ConvertFrom-Json
$runtimeVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
$runtimePackage = $assets.packageFolders.Keys | ForEach-Object { Join-Path $_ "microsoft.netcore.app.runtime.$Runtime/$runtimeVersion" } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
if (!$runtimePackage) { throw ".NET runtime notices are unavailable for $Runtime/$runtimeVersion" }
$runtimeNotices = Join-Path $destination 'dotnet-runtime'
New-Item -ItemType Directory -Path $runtimeNotices -Force | Out-Null
foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) { Copy-Item -LiteralPath (Join-Path $runtimePackage $name) -Destination $runtimeNotices }
$components.Add(@{ package = "Microsoft.NETCore.App.Runtime.$Runtime/$runtimeVersion"; license = 'MIT and bundled notices' })
if ($Runtime -eq 'win-x64') {
    $analysis = Get-Content -Raw -LiteralPath (Join-Path $Payload 'Analysis/analysis-helper.json') | ConvertFrom-Json
    $components.Add(@{ package = "FFmpeg/$($analysis.version)"; license = $analysis.license; noticeDirectory = 'analysis';
        sourceCommit = $analysis.sourceCommit; buildCommit = $analysis.buildCommit; archiveSha256 = $analysis.archive.sha256 })
} else { $components.Add(@{ package = 'FFmpeg (distribution dependency)'; license = 'Supplied by the Linux distribution' }) }
$sources = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'license-sources.json') | ConvertFrom-Json
$cache = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/packaging-license-cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
foreach ($source in $sources) {
    $file = Join-Path $cache $source.file
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { Invoke-WebRequest -Uri $source.url -OutFile $file -TimeoutSec 60 }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $source.sha256) { throw "Upstream license checksum differs: $($source.file)" }
    Copy-Item -LiteralPath $file -Destination $destination
}
$components | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'components.json') -Encoding utf8NoBOM
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'license-sources.json') -Destination $destination
foreach ($name in @('ASSET-PROVENANCE.md', 'STARTER-PROVENANCE.md')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) "docs/$name") -Destination $destination
}
