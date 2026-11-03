# Install CCP Classic

Packages currently default to `1.0.0-preview.1`. They are unsigned previews until
the owner signs and publishes a release. The application works offline and does
not check for updates. Install later versions manually.

## Windows x64

Run `CCP.Classic-1.0.0-preview.1-win-x64-setup.exe`. It installs for the current
user under `%LOCALAPPDATA%\Programs\CCP Classic`, without administrator access.
Launch CCP Classic from the Start menu. The installer does not launch the app,
enable startup or start effects automatically.

Alternatively, extract the entire Windows ZIP and run `CCP.Classic.App.exe`.
Keep its libraries, `libvlc`, `Analysis`, `Starter` and `licenses` folders together.
The package contains .NET 10, Windows LibVLC and an offline FFmpeg analysis helper, so these need no separate
installation. Use a Windows version supported by [.NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

Settings, presets and progress stay in `%LOCALAPPDATA%\CCP Classic`. Uninstalling
does not remove that folder. Back it up before resetting or removing it yourself.

## Linux x64

Use a glibc distribution supported by [.NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).
The tarball includes the .NET runtime; Linux desktop libraries and LibVLC are
provided by the distribution. The package workflow uses Ubuntu 24.04.

On Ubuntu 24.04, install these dependencies once:

```sh
sudo apt-get update
sudo apt-get install --no-install-recommends \
  libx11-6 libice6 libsm6 libfontconfig1 libxfixes3 libxrandr2 libxcursor1 libxi6 \
  libgl1 libdbus-1-3 libicu74 libssl3t64 libc6 libgcc-s1 libstdc++6 zlib1g \
  libgssapi-krb5-2 ca-certificates tzdata \
  libvlc5 vlc-plugin-base vlc-plugin-video-output fonts-noto-core fonts-noto-cjk ffmpeg
```

LibVLC alone is insufficient; its codec and audio plugins are needed too.
Audio analysis uses the distribution's `/usr/bin/ffmpeg`; it does not search PATH.
Playback uses LibVLC. The helper analyzes selected local media and does not download files.
The Noto CJK package supplies Chinese, Japanese and Korean glyphs that the
bundled Inter font does not contain. Other distributions use different package
names and ICU/OpenSSL versions. See [Avalonia's Linux requirements](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)
and [Microsoft's native dependency list](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-decision#dependencies).

Extract and launch:

```sh
tar -xzf CCP.Classic-1.0.0-preview.1-linux-x64.tar.gz
cd CCP.Classic-1.0.0-preview.1-linux-x64
./CCP.Classic
```

For full desktop overlays and global input, use a local X11 session with a
compositor. Remote X displays are refused. On unsupported backends the app
reports unavailable capabilities; in-app panic remains available. Wallpaper
is Windows-only. No AppImage or automatic dependency installer is included.

User data is normally under `~/.local/share/CCP Classic`, or the configured
XDG data location. Removing the extracted application folder preserves user data.

## Verify a download

Every archive and installer has a matching `.sha256` file. On Linux, use
`sha256sum -c <filename>.sha256`. In PowerShell, run:

```powershell
Get-FileHash -Algorithm SHA256 .\CCP.Classic-1.0.0-preview.1-win-x64-setup.exe
```

Compare the result with the published checksum. A checksum detects changed bytes;
the preview files do not yet carry an Authenticode signature.

## Build packages

Install the .NET 10 SDK, PowerShell 7 and Git LFS, then fetch the repository's LFS
objects. Run each command on its matching operating system:

```powershell
./packaging/package.ps1 -Runtime win-x64 -Version 1.0.0-preview.1
./packaging/package.ps1 -Runtime linux-x64 -Version 1.0.0-preview.1
```

Windows installer compilation also needs an existing Inno Setup 6 installation.
Pass `-InnoCompiler 'C:\path\to\ISCC.exe'` if it is not in a standard location.
`-RequireInstaller` fails rather than accepting a ZIP-only build. Scripts do not
install build tools. Besides NuGet restore and pinned license texts, Windows builds
retrieve the pinned LGPL shared FFmpeg archive and its corresponding source material.
The helper's executable and seven DLLs are checked against the recorded sizes and
SHA-256 hashes. Only FFmpeg is included; ffplay and ffprobe are excluded.

Windows builds also create `CCP.Classic-<version>-analysis-sources.zip`. Keep this
archive with the binaries and installer when redistributing them. It contains the
exact FFmpeg source, BtbN build recipes and the original dependency source cache,
including vendored Rust sources and their complete license and copyright files.
The source cache is about 2.24 GB and is retained under `artifacts/analysis-helper-cache`.
Its upstream Actions artifact expires on November 14, 2026. After that date, restore
the verified `btbn-download-cache.zip` from an earlier source bundle into that cache;
the build checks its pinned official digest before use.

`-LocalReview` permits an owner-only test package while that large source bundle is
pending. Such packages explicitly record `externalDistributionBlocked: true` and
must not be redistributed. Normal builds and CI require the complete source bundle.

Output is in `artifacts/packages`. Each package includes its version, source
commit, dirty-tree flag and SDK version in `package.json`, plus dependency
metadata and licenses. Fresh staging directories prevent stale publish files.
`-SkipTests` is for a coordinated run that already passed Release tests.
The package workflow builds review artifacts without publishing a GitHub release.
On Linux it also runs the desktop and audio probe under its own private virtual
display and null audio sink, with no desktop session or physical output required.
