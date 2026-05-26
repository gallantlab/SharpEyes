# SharpEyes
A companion to (and eventual replacement for) the [Eyetracking](https://github.com/gallantlab/Eyetracking) python package. 
SharpEyes offers a UI for finding pupils in eyetracking videos, and the ability to edit the pupil locations manually, 
which is way better than the Eyetracking package way of setting values and hoping for the best.

![](screenshot.png)

See the [wiki](https://github.com/candytaco/SharpEyes/wiki) for more information.

### Resources used
* [Avalonia UI](https://avaloniaui.net) for display
* [NumSharp Lite](https://github.com/SciSharp/NumSharp.Lite) for interop with NumPy
* [OpenCvSharp](https://github.com/shimat/opencvsharp) for video processing
* Icons8 application icon.

##### Requirements
This is targeted at .NET 10 on Window 10/11 (though I see no reason why it shouldn't work down to Windows 7) and Ubuntu 24 (some fudging might be needed to get OpenCVSharp working on other versions)

If you would like to do things to the code, this is written using VS 2022 Community with occasional testing on Jetbrains Rider 2021
