# SharpEyes

SharpEyes is a tool for eyetracking, gaze correction, and motion-energy feature extraction.

SharpEyes offers a UI for finding pupils in eyetracking videos, and the ability to edit the pupil locations manually. 

It then lets you overlay the gaze on the stimulus to look at eyetraces, and then also the option to manually correct that again. 
It can also filter the gaze traces to smooth things out.

Given traces and stimulus video, you can then recenter the video to be retinotopic, and generate motion-energy features from stimuli. 

The motion-energy features are actually processed by [PyMoten](https://github.com/gallantlab/pymoten), but the user doesn't have to interact with it. 
Sharpeyes will handle everything under the hood, including setup.

<video width="800" controls>
    <source src="assets/live%20motion-energy.mp4" type = "video/mp4">
</video>

See [Getting Started](getting%20started.md) for installation and Python setup. See the [Workflow](workflow/index.md) section for documentation on each tab. 
If you use SharpEyes in your work, see [Citing SharpEyes](cite.md).