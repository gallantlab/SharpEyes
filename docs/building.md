# Building from source

SharpEyes is a .NET 10 application. On Windows and Linux it is typically built
with Visual Studio or JetBrains Rider, but it can also be built from the command
line with the .NET SDK on any supported platform — including macOS.

## Building on macOS (Apple Silicon / arm64)

These steps produce a native arm64 build. No Homebrew OpenCV install is required:
the OpenCvSharp native runtime is pulled in automatically as a NuGet package.

### 1. Install the .NET 10 SDK

Download the **arm64** .NET 10 SDK from
[dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) and run
the installer. Verify the install:

```bash
dotnet --version        # should print 10.0.xxx
dotnet --list-sdks      # should list a 10.0.x SDK
```

The installer puts `dotnet` at `/usr/local/share/dotnet/dotnet`. If `dotnet` is
not found in your shell, add it to your `PATH`:

```bash
echo 'export PATH="/usr/local/share/dotnet:$PATH"' >> ~/.zshrc
```

### 2. Clone and build

```bash
git clone https://github.com/gallantlab/SharpEyes.git
cd SharpEyes
dotnet build SharpEyes/SharpEyes.csproj -c Debug -r osx-arm64
```

Run it directly with:

```bash
dotnet run --project SharpEyes/SharpEyes.csproj
```

### 3. Produce a self-contained build for distribution

To create a standalone folder that bundles the .NET runtime (so the target Mac
does not need .NET installed):

```bash
dotnet publish SharpEyes/SharpEyes.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false
```

The output lands in `SharpEyes/bin/Release/net10.0/osx-arm64/publish/`. Launch
the `SharpEyes` executable there, or zip the `publish` folder to copy to another
arm64 Mac.

!!! note "Gatekeeper on other Macs"
    The build is unsigned. It runs fine on the machine that built it, but another
    Mac will block it on first launch. The user can right-click the executable and
    choose **Open** once to allow it. Distributing to others without that step
    requires code signing and notarization with an Apple Developer account.

### macOS notes and limitations

- **OpenCV**: provided by the `OneWare.OpenCvSharp4.runtime.osx-arm64` NuGet
  package; the native `libOpenCvSharpExtern.dylib` is self-contained, so no
  separate OpenCV install is needed.
- **Eyelink `.edf` import is unavailable**: SR Research does not ship the
  `edfapi` library for macOS. SharpEyes detects this and disables EDF parsing;
  NumPy and CSV gaze import still work.
- **Intel (x86_64) Macs / Rosetta**: not built or tested here. The same approach
  works by publishing with `-r osx-x64` and using the `osx-x64` OpenCvSharp
  runtime package instead, but this is unsupported.

## Building on Windows and Linux

The project is developed with Visual Studio (not VS Code) and JetBrains Rider.
You can also build Windows from the command line:

```bash
# Windows
dotnet build SharpEyes/SharpEyes.csproj -c Release -r win-x64
```

### Linux runtime identifier

Build Linux on an Ubuntu 24 host with the distro-specific RID, **not** the
portable `linux-x64`:

```bash
dotnet build SharpEyes/SharpEyes.csproj -c Release -r ubuntu.24.04-x64
```

The project references `OpenCvSharp4.official.runtime.ubuntu.24.04-x64`, whose
package ships `libOpenCvSharpExtern.so` only under `runtimes/ubuntu.24.04-x64/`.
The .NET RID graph does not fall forward from the generic `linux-x64` to that
asset, so a `linux-x64` build compiles successfully but ships **without** the
native library and crashes when OpenCV is first used. The csproj sets
`<UseRidGraph>true</UseRidGraph>` so the distro RID is available. Note that the
SDK may only recognize `ubuntu.24.04-x64` when building on a matching Linux
host; cross-building this RID from Windows or macOS is not supported.
