# Roadmap

The long-term goal is to consolidate eyetracking capabilities from the [Eyetracking](https://github.com/gallantlab/Eyetracking) Python repository — which lacks interactive features — into SharpEyes.

## Planned

- Estimating calibration point positions
- Making the motion-energy filter visualization dynamic to show how the filters are responding to the stimulus video.

## Known issues

OpenCV can crash if you scrub through the video too quickly. This happens because much of the UI runs in async methods, which can cause race conditions in OpenCV. These crashes occur outside of SharpEyes's stack and are not captured by the crash reporter.
