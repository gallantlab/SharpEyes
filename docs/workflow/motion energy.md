# Motion-Energy

<img src="../../assets/motion-energy.png" width="800" />

The Motion-Energy tab computes motion-energy features from a video using [PyMoten](https://github.com/gallantlab/pymoten). It can operate on recentered video sent from the [Recentering](recentering.md) tab, or on a raw video loaded directly. If you want to compute motion-energy for retinotopic videos, then they _have_ to be sent over from the [Recentering](./recentering.md) tab. The video preview overlays the motion-energy pyramid on the frames so you can visually verify the filter parameters before committing to a full computation.

## Motion-energy frame parameters

These control how the input video frames are prepared before being passed to PyMoten.

| Control | Description |
|---|---|
| Pad percent | Amount of padding added around each frame as a percentage of the frame size. Must be at least 100 (i.e. the padded frame is at least the original size). Recentered/retinotopic videos must be padded, but a raw video can do without padding. |
| Pad value | Pixel value (0.0–1.0) used to fill the padding area |
| Frame scale | Scale factor applied to each frame before processing. Lower values reduce computation time at the cost of spatial resolution. |
| Output frame | Displays the resulting output frame size given the current parameters |
| Video frame | Displays the original video frame size |

Click **Restore defaults** to reset these parameters.

## Motion-energy pyramid

These control the filter bank that PyMoten uses to decompose the video into motion-energy features.

| Control | Description |
|---|---|
| Video FPS | Frame rate of the input video, used to convert temporal frequencies from cycles/second to cycles/frame. This is read from the video but can be overridden|
| Spatial frequencies | The spatial frequencies (cycles/image) to include in the pyramid. Select multiple; use + and − to add or remove values. |
| Temporal frequencies | The temporal frequencies (cycles/second) to include in the pyramid. Select multiple; use + and − to add or remove values. |
| Directions | The motion directions (degrees) to include in the pyramid. Select multiple; use + and − to add or remove values. |
| Show motion-energy pyramid | When enabled, the pyramid filters are overlaid on the video preview as circles and arrows so you can verify coverage. |

Click **Compute pyramid** to compute and display the pyramid for the current frame. Click **Restore defaults** to reset the pyramid parameters to their defaults.

## Compute features

Once the frame parameters and pyramid are configured, click **Compute features** to run the full feature extraction over the video. Set **Start frame** to begin extraction from a specific frame rather than the beginning. While computing, the button changes to **Cancel** to interrupt the run.

After extraction completes, a save dialog prompts for the output path. Three files are saved, all sharing the same base name:

**`{name} motion energy.npy`** — the features array, shape `[frames × filters]`. Each row is one video frame; each column is one filter from the pyramid.

**`{name} info.txt`** — a human-readable record of all parameters used for the run, including:

- Stimulus video path, FPS, frame size, and duration
- Gaze file path
- Gaze info: eyetracking FPS, data start frame, gaze space dimensions
- Gaze filtering settings (median filter window, outlier removal thresholds), or "None" if filtering was disabled
- Motion-energy parameters: pad percent, pad value, frame scale, output and video frame sizes, video FPS, spatial/temporal frequencies, directions, and start frame

**`{name} motion-energy filters.csv`** — one row per filter in the pyramid, with columns: Y center, X center, Direction (degrees), Spatial Frequency, Spatial Envelope, Temporal Frequency, Temporal Envelope, Temporal width, Spatial phase offset. All spatial values are in pixels at the output frame size.
