# FAQs

**What does SharpEyes use under the hood?**

- SharpEyes is built on .NET 10. 

- [Avalonia UI](https://avaloniaui.net) is used for the UI. 

- [OpenCvSharp](https://github.com/shimat/opencvsharp) wraps OpenCV for video processing. 

- [NumSharp Lite](https://github.com/SciSharp/NumSharp.Lite) handles NumPy array interop. 

- [Python.Net](https://github.com/pythonnet/pythonnet) bridges to Python

- [PyMoten](https://github.com/gallantlab/pymoten) does the motion-energy feature extraction.

**What do I do with the data SharpEyes saves out?**

It depends on how far through the workflow you go. The [Pupil Finding](workflow/pupil%20finding.md) tab saves pupil positions and timestamps as numpy arrays, which can be loaded into the [Calibration](workflow/calibration.md) tab or passed to the `SharpEyesCalibrator` class in the [Eyetracking](https://github.com/gallantlab/Eyetracking) Python repo for external calibration. That can then be brought back to SharpEyes.

The [Stimulus & Gaze](workflow/stimulus%20and%20gaze.md) tab can load gaze positions in numpy, CSV, or Eyelink formats for viewing and editing. The [Recentering](workflow/recentering.md) tab exports retinotopic frames as PNG files or a numpy array. The [Motion-Energy](workflow/motion%20energy.md) tab saves a numpy array of features, a plain-text info file recording all parameters, and a CSV describing each filter in the pyramid.

**There's no option to read .edf files from Eyelink**

You need to provide your own copy of the edfapi.dll/so library from the SR Research Eyelink SDK. Putting that in your path or next to the SharpEyes executable should enable EDF reading. There is no EDF support for macOS

**What do I need to build SharpEyes from source?**

SharpEyes was built with Visual Studio and JetBrains Rider.

**What about GPU support?**

Everything runs on CPU. The code uses multithreading where possible, but the bottleneck is generally the human reviewing the output, so GPU support is not a priority.

**There's a bug. What do I do?**

Open an issue on the [GitHub issues page](https://github.com/gallantlab/SharpEyes/issues) and describe the problem.
