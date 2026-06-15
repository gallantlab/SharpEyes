# FAQs

**What does SharpEyes use under the hood?**

- SharpEyes is built on .NET 10. 

- [Avalonia UI](https://avaloniaui.net) is used for the UI. 

- [OpenCvSharp](https://github.com/shimat/opencvsharp) wraps OpenCV for video processing. 

- [NumSharp Lite](https://github.com/SciSharp/NumSharp.Lite) handles NumPy array interop. 

- [Python.Net](https://github.com/pythonnet/pythonnet) bridges to Python

- [PyMoten](https://github.com/gallantlab/pymoten) does the motion-energy feature extraction.

**What do I do with the data SharpEyes saves out?**

It depends on how far through the workflow you go. 
The [Pupil Finding](workflow/pupil%20finding.md) tab saves pupil positions and timestamps as numpy arrays, 
which can be loaded into the [Calibration](workflow/calibration.md) tab or passed to the `SharpEyesCalibrator` class 
in the [Eyetracking](https://github.com/gallantlab/Eyetracking) Python repo for external calibration. That can then be brought back to SharpEyes.

The [Stimulus & Gaze](workflow/stimulus%20and%20gaze.md) tab can load gaze positions in numpy, CSV, or Eyelink formats for viewing and editing. 
The [Recentering](workflow/recentering.md) tab exports retinotopic frames as PNG files or a numpy array. 
The [Motion-Energy](workflow/motion%20energy.md) tab saves a numpy array of features, 
a plain-text info file recording all parameters, and a CSV describing each filter in the pyramid.

**There's no option to read .edf files from Eyelink**

You need to provide your own copy of the edfapi.dll/so library from the SR Research Eyelink SDK. 
SharpEyes will find it automatically if it is on your system path, 
or you can specify its location explicitly in General Settings. There is no EDF support for macOS from SR Research.

**The videos don't play in real time**

Because SharpEyes is running image processing under the hood, either for pupil finding or recentering, 
SharpEyes deals with the raw pixel values on each frame of the video as a bitmap array. 
We have to manually draw this to the canvas, which takes more overhead than a normal video player.

**What do I need to build SharpEyes from source?**

SharpEyes was built with Visual Studio and JetBrains Rider.

**What about GPU support?**

Motion-energy feature extraction can run on an NVIDIA GPU via CUDA or on Apple Silicon via MPS, 
if PyTorch is installed and the hardware is available. The available backends are listed in Python Settings, 
where you can also set your preferred order. All other processing runs on CPU.

**There's a bug. What do I do?**

Open an issue on the [GitHub issues page](https://github.com/gallantlab/SharpEyes/issues) and describe the problem.
