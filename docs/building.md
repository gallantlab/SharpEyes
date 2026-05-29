# Building from source

SharpEyes is a .NET 10 application that can run on any supported desktop platform.

## Recommended building:

Open the solution with Visual Studio (Windows) or Jetbrains Rider (Windows, Linux, macOS), and build with the release configuration.

If you have .Net set up in your environment, you can also run the following in a command line:

```bash
dotnet build SharpEyes/SharpEyes.csproj -c Release -r [win-x64|linux-x64|osx-[x64|arm64]]
```

## Manual building without Visual Studio/Rider

These steps produce an arm64 build - switch the identifier if you need another architecture.

### 1. Install the .NET 10 SDK

Download and install the .NET 10 SDK for your machine architecture from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0). 

If `dotnet` is not found in your environment afterwards, add `/usr/local/share/dotnet` to your `PATH`.

### 2. Clone and build

```bash
git clone https://github.com/gallantlab/SharpEyes.git
cd SharpEyes
dotnet build SharpEyes/SharpEyes.csproj -c Release -r <your arch>
```

It can be run with
```bash
dotnet run --project SharpEyes/SharpEyes.csproj
```

### 3. Produce a self-contained build for distribution

```bash
dotnet publish SharpEyes/SharpEyes.csproj \
  -c Release -r osx-<your arch> --self-contained true \
  -p:PublishSingleFile=true
```

The exectuable will be in `SharpEyes/bin/Release/net10.0/<your arch>/publish/`.

## macOS notes

- **OpenCV**: is currently provided `OneWare.OpenCvSharp4.runtime.osx-arm64` NuGet
  package because the official runtime is still on 4.7. Whenever that gets updated, we'll switch it back over.
- **Eyelink `.edf` import is unavailable**: There is no EDF library from SR Research for macOS.
- **Gatekeeper**: what you build on your machine will run fine. Another Mac will block it on first launch. Right-click the executable and choose **Open** once to allow it. Distributing more broadly requires will code signing and notarization with an Apple Developer account.


## Linux notes
I developed this on 24.04.

OpenCVSharp4 uses `ubuntu.24.04-x64` to identify its linux package rather than a generic `linux-x64`.

SharpEyes will run normally if you hit "Run" from Rider, but if you publish it, you either need to copy the OpenCVSharp shared library over from `SharpEyes/bin/Release/runtimes/ubuntu.24.04-x64/` to the publish folder, or set the runtime ID in the Publish configuration to be `ubuntu.24.04-x64`.