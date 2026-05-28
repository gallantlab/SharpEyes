# Getting Started

## Installation

Executables and installers are available on the [Releases page](https://github.com/gallantlab/SharpEyes/releases). Unzip the zip file and run `SharpEyes`

### Windows

A standalone executable is available. It should have everything it needs.

### Linux

A standalone executable is available. Linux may require additional system libraries to be installed; SharpEyes will tell you which libraries are missing on launch.

### macOS

I have no macOS development environment, so it's not supported. But because all the packages here are cross-platform, I see no reason why it can't be compiled from source on a mac. However, I can't provide any support for that.

## Python interop setup

SharpEyes handles all eyetracking processing internally, but computing motion-energy features requires [PyMoten](https://github.com/gallantlab/pymoten) to be installed in a Python environment. SharpEyes will manage this for you. You can use either system python at a location you specify, a conda environment if you have conda installed, or have SharpEyes manage its own standalone python environment.

Open the Python settings from the menu in the top left. This lets you select which Python environment to use.

### System Python

SharpEyes will use the Python installation at the location you provide.

<img src="../assets/python%20settings%20system%20python.png" width="600" />

### Conda

SharpEyes will use a conda environment. You can select an existing environment from the list, or SharpEyes will create a new one for you.

<img src="../assets/python%20settings%20conda.png" width="600" />

### Standalone

SharpEyes will manage a self-contained Python environment with no additional setup required on your part.

<img src="../assets/python%20settings%20standalone.png" width="600" />

Once Python is configured, head to the [Workflow](workflow/index.md) section to get started processing data.