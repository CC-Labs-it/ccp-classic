# Third-party notices

Original CCP Classic code and CodeBambi artwork are covered by the repository's
MIT license. Bundled third-party components retain their own licenses.
Packages include full upstream texts under `licenses`, not just this summary.

| Component | Resolved version | License and notices |
|---|---|---|
| .NET runtime | .NET 10 patch recorded in package metadata | MIT and upstream third-party notices; `licenses/dotnet-runtime` |
| Avalonia and desktop backends | 12.1.2 | MIT; `Avalonia-LICENSE.txt` and `Avalonia-NOTICE.md` |
| ANGLE Windows natives | 2.1.27548.20260419 | BSD-style license copied from the native NuGet package |
| SkiaSharp | 3.119.4 | MIT bindings; Skia uses BSD-3-Clause; full native dependency notices copied from NuGet |
| HarfBuzzSharp | 8.3.1.3 | MIT bindings and HarfBuzz's upstream license; full native notices copied from NuGet |
| Inter typeface | As embedded by Avalonia.Fonts.Inter 12.1.2 | SIL Open Font License 1.1; `Inter-OFL.txt` |
| LibVLCSharp | 3.10.1 | LGPL-2.1-or-later; `LibVLCSharp-LGPL-2.1.txt` |
| Windows LibVLC | 3.0.24 | LGPL-2.1-or-later NuGet distribution; `LibVLC-LGPL-2.1.txt` |
| MicroCom.Runtime | 0.11.6 | MIT; `MicroCom-LICENSE.txt` |
| Tmds.DBus.Protocol | 0.94.1 | MIT; `Tmds-DBus-COPYING.txt` |
| Windows FFmpeg analysis helper | n9.0.2-39-g7adfbe40dc-20261031 | LGPL-3.0-or-later shared build; `licenses/analysis` and the accompanying analysis source archive |

Avalonia is copyright AvaloniaUI OÜ and contributors. SkiaSharp and HarfBuzzSharp
bindings are copyright Xamarin and Microsoft; their native libraries carry the
additional copyrights in their complete upstream notices. Inter is copyright
2016 The Inter Project Authors and is used unmodified. MicroCom is copyright
2021 Nikita Tsukanov. D-Bus copyright notices name Alp Toker, other contributors
and Tom Deseyn. VideoLAN libraries remain copyright their respective contributors.

## VideoLAN source and replacement

LibVLCSharp and LibVLC are distributed as separate, unmodified, dynamically
loaded libraries. Compatible modified versions can replace the installed shared
libraries. The package disables trimming and single-file bundling to preserve
that arrangement. Classic does not impose restrictions on modification or
reverse engineering for debugging modifications to those libraries.

Corresponding upstream sources and build material:

- [LibVLCSharp 3.10.1 source](https://code.videolan.org/videolan/LibVLCSharp/-/tree/4896d0e06d19ac40b46802cf7dc167d432f3fd63)
- [VLC 3.0.24 source archive](https://download.videolan.org/pub/videolan/vlc/3.0.24/vlc-3.0.24.tar.xz)
- [VideoLAN contribution sources](https://download.videolan.org/pub/videolan/contrib/)
- [Official LibVLC NuGet packaging](https://code.videolan.org/videolan/libvlc-nuget)

The Windows package uses `VideoLAN.LibVLC.Windows`, not the separate GPL plugin
package. Linux LibVLC and system fonts are installed through the distribution;
their source, updates and copyright files are supplied by that distribution.

## FFmpeg source and replacement

The Windows analysis helper is the unmodified LGPL shared build from BtbN's
[retained October 2026 release](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-10-31-12-59).
Its build enables LGPL version 3 and shared libraries, without enabling GPL or
nonfree FFmpeg components. Classic uses it only for offline PCM/RMS analysis.
Playback uses LibVLC. FFmpeg is copyright its respective contributors and remains
available under its own license; Classic's MIT license does not replace it.

`Analysis/analysis-helper.json` records the release, exact source/build commits,
archive digest and every runtime file's length and SHA-256. The package contains
ffmpeg.exe and its seven required shared libraries, excluding ffplay and ffprobe.
The full LGPLv3 and referenced GPLv3 texts, FFmpeg's licensing notes, configure
arguments and source index are under `licenses/analysis`.

The accompanying `CCP.Classic-<version>-analysis-sources.zip` supplies exact FFmpeg
source and BtbN recipes, plus BtbN's original dependency source cache. That cache
includes dependency working trees, vendored Rust crates, and their complete
license and copyright texts. Its digest is checked against the original public
GitHub Actions artifact. Redistribute the source archive alongside Windows binary
packages and installers with equivalent access; it is part of the distribution.
An owner-only review package marked source-pending cannot be redistributed.

Compatible modified FFmpeg executables and DLLs may replace the separate files
in `Analysis`. Update the editable local `analysis-helper.json` file entries with
their new lengths and SHA-256 values. Classic imposes no restriction on modification
or reverse engineering for debugging such replacements. No application relinking
is needed. Linux FFmpeg is supplied by the distribution, including its notices and source.

## Package evidence

`licenses/components.json` records the restored package graph, license metadata
and NuGet content hashes. It can include build tools and other-platform native
packages that are not copied into a particular runtime's payload.
NuGet license and notice files are preserved in component-specific directories.
The runtime's complete license and notices are copied from its exact runtime pack.

Additional upstream texts are pinned by URL and SHA-256 in
`licenses/license-sources.json`. Packaging refuses a mismatched legal file.
The asset provenance files are included alongside these notices.
The application and installer do not retrieve license text or other content at runtime.
