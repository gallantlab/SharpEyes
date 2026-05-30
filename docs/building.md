# Building from source

SharpEyes is a .NET 10 application that can run on any supported desktop platform.

## Recommended building:

Open the solution with Visual Studio (Windows) or Jetbrains Rider (Windows, Linux, macOS), and build with the release configuration.

Run configurations for Rider have been included, including a Publish configuration for Ubuntu 24.04.
- CrashReporter: builds the crash reporter
- SharpEyes: default, will run SharpEyes
- Full Ubuntu 24 publish: run this with the publish config, and it will put a compiled executable in the SharpEyes/Release folder, along with the crash reporter and the OpenCVSHarp externs library. This is the run configuration used to produce the Ubuntu 24 release compiled executables.

If you have .Net set up in your environment, you can also run the following in a command line:

```bash
dotnet build SharpEyes/SharpEyes.csproj -c Release -r [win-x64|linux-x64|osx-[x64|arm64]]
```

## Manual building without Visual Studio/Rider

These steps produce an architecture-specific build. Runtime ID (RID) specifiers that we should be able to run include:
- `win-[x64|x86]`
- `linux-x64`
- `osx-[x64|arm64]` (YMMV; there appear to be intermittent OpenCV/Sharp library issues)

### 1. Install the .NET 10 SDK

Download and install the .NET 10 SDK for your machine architecture from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0). 

If `dotnet` is not found in your environment afterwards, add the `dotnet` executable to your `PATH`.

### 2. Clone and build

```bash
git clone https://github.com/gallantlab/SharpEyes.git
cd SharpEyes
dotnet build SharpEyes/SharpEyes.csproj -c Release -r <your RID>
```

It can be run with
```bash
dotnet run --project SharpEyes/SharpEyes.csproj
```

### 3. Produce a self-contained build for distribution

```bash
dotnet publish SharpEyes/SharpEyes.csproj -c Release -r <your RID> --self-contained true -p:PublishSingleFile=true
```

The executable will be in `SharpEyes/bin/Release/net10.0/<your RID>/publish/`.

## macOS notes

- **OpenCV**: is currently provided by `OneWare.OpenCvSharp4.runtime.osx-arm64` NuGet
  package because the official runtime is still on 4.7. Whenever that gets updated, we'll switch it back over. Note this is a minimal OpenCVSharp runtime meant to do ML work, and may not include all the OpenCV features we need for eyetracking and making retinotopic videos.
- **Eyelink `.edf` import is unavailable**: There is no EDF library from SR Research for macOS.
- **Gatekeeper**: what you build on your machine will run fine. Another Mac will block it on first launch. Right-click the executable and choose **Open** once to allow it. Distributing more broadly requires would code signing and notarization with an Apple Developer account that we don't have.


## Linux notes
I developed this on 24.04.

OpenCVSharp4 uses `ubuntu.24.04-x64` to identify its linux package rather than a generic `linux-x64`.

SharpEyes will run normally if you hit "Run" from Rider, but the simple Publish-to-folder config will not copy the OpenCVSharp runtime over to the release folder. Use the provided 'Full Ubuntu 24 publish' run configuration, which will properly include the OpenCVSharp runtime in the output.