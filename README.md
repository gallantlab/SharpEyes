# SharpEyes
[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.20401807.svg)](https://doi.org/10.5281/zenodo.20401807)
A program for doing eyetracking and making motion-energy features out of stimulus videos.
SharpEyes offers a UI for finding pupils in eyetracking videos, and the ability to edit the pupil locations manually, 
which is way better than the Eyetracking package way of setting values and hoping for the best.
It then lets you overlay the gaze on the stimulus to look at eyetraces, and then also the option to manually correct that again.
Given traces and stimulus video, you can then recenter the video to be retinotopic, and generate motion-energy features from stimuli.
The motion-energy features are actually processed by [PyMoten](https://github.com/gallantlab/pymoten), but the user doesn't have to interact at all with it. Sharpeyes will handle everything under the hood, including setup/

![](screenshot.png)

See the [Documentation](https://gallantlab.org/SharpEyes) for more information.

### Resources used
* [Avalonia UI](https://avaloniaui.net) for display
* [NumSharp Lite](https://github.com/SciSharp/NumSharp.Lite) for interop with NumPy
* [OpenCvSharp](https://github.com/shimat/opencvsharp) for video processing
* [Python.Net](https://github.com/pythonnet/pythonnet) for interop with python packages
* [PyMoten](https://github.com/gallantlab/pymoten) for extracting motion-energy features
* Icons8 application icon.

##### Requirements
This is targeted at .NET 10 on Window 10/11 (though I see no reason why it shouldn't work down to Windows 7) and Ubuntu 24 (some fudging might be needed to get OpenCVSharp working on other versions)

If you would like to do things to the code, this is written using Visual Studio (not code) and Jetbrains Rider.
