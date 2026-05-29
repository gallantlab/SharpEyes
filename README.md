# SharpEyes
[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.20401807.svg)](https://doi.org/10.5281/zenodo.20401807)

A program for doing eyetracking and making motion-energy features out of stimulus videos.
SharpEyes offers a UI for finding pupils in eyetracking videos, and the ability to edit the pupil locations manually to ensure data quality.
It then lets you overlay the gaze on the stimulus to look at eyetraces, and then also the option to manually correct that again.

Given traces and stimulus video, you can then recenter the video to be retinotopic, and generate motion-energy features from stimuli.
The motion-energy features are actually processed by [PyMoten](https://github.com/gallantlab/pymoten), but the user doesn't have to interact at all with it. Sharpeyes will handle everything under the hood, including setup/

![](./docs/assets/recenetering.png)

See the [Documentation](https://gallantlab.org/SharpEyes) for full details.

## Workflow

SharpEyes is organized into five tabs, each corresponding to a stage of processing:

**Pupil Finding** — Takes a raw eyetracking video and finds the pupil location in each frame using template matching. Supports manual correction and accumulation of templates to improve accuracy over time.

**Calibration** — Maps from pupil video space to stimulus display space, producing gaze positions that can be overlaid on the stimulus.

**Stimulus & Gaze** — Overlays the gaze location on the stimulus video. Supports temporal alignment of the eyetracking data to the video, gaze filtering (median filter and outlier removal), and manual editing of individual gaze positions. Reads numpy arrays, CSV, and Eyelink EDF files.

**Recentering** — Shifts each frame of the stimulus video so that the gaze position is always at the center of the output, producing a retinotopic video. Exports as PNG frames or a numpy array.

**Motion-Energy** — Computes motion-energy features from the recentered or raw stimulus video using [PyMoten](https://github.com/gallantlab/pymoten). SharpEyes manages the Python environment setup. Saves a numpy feature array, a plain-text parameter log, and a CSV describing each filter in the pyramid.

## Installation

Executables and installers are available on the [Releases page](https://github.com/gallantlab/SharpEyes/releases). Windows has both a standalone `.exe` and an installer. Linux has a standalone `.exe`; additional system libraries may be required and SharpEyes will report any that are missing. macOS (Apple Silicon / arm64) is supported and builds from source; see below.

## Requirements

This is targeted at .NET 10 on Window 10/11 (though I see no reason why it shouldn't work down to Windows 7) and Ubuntu 24 (some fudging might be needed to get OpenCVSharp working on other versions).

macOS support targets Apple Silicon (arm64) on .NET 10. The OpenCvSharp native runtime is bundled via NuGet, so no separate OpenCV install is needed. Note that Eyelink `.edf` import is unavailable on macOS (SR Research does not ship the `edfapi` library for macOS); NumPy and CSV gaze import work as normal.

If you would like to do things to the code, this is written using Visual Studio (not code) and Jetbrains Rider.

## Resources used

* [Avalonia UI](https://avaloniaui.net) for the UI
* [OpenCvSharp](https://github.com/shimat/opencvsharp) for video processing
* [NumSharp Lite](https://github.com/SciSharp/NumSharp.Lite) for NumPy interop
* [Python.Net](https://github.com/pythonnet/pythonnet) for Python interop
* [PyMoten](https://github.com/gallantlab/pymoten) for motion-energy feature extraction
* Icons8 application icon