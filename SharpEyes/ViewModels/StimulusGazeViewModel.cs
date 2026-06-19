using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Eyetracking;
using NumSharp;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	public class StimulusGazeViewModel : GazeVideoPlayerViewModelBase
	{
		// == Commands (video transport and JumpToFirstTTL come from the base) ==
		public ReactiveCommand<Unit, Unit> LoadGazeCommand { get; set; }
		public ReactiveCommand<Unit, Unit>? SaveGazeCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit> SetCurrentAsDataStartCommand { get; set; }
		public ReactiveCommand<Unit, Unit>? FindDataStartCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit> SendToRecenteringCommand { get; set; }
		public ReactiveCommand<Unit, Unit> ShiftEyetrackingForwardCommand { get; set; }
		public ReactiveCommand<Unit, Unit> ShiftEyetrackingBackCommand { get; set; }
		public ReactiveCommand<Unit, Unit>? SetCurrentAsFirstTTLCommand { get; set; } = null;

		// Set by MainWindowViewModel after construction
		public RecenteringViewModel? RecenteringViewModel { get; set; }
		public Action? SwitchToRecenteringTab { get; set; }

		// == UI elements ==
		public bool IsMovingGaze
		{
			get;
			set { this.RaiseAndSetIfChanged(ref field, value); }
		} = false;

		// Gaze overlay info
		internal NDArray? RawGazeLocations = null;
		internal NDArray? FilteredGazeLocations = null;

		// the filtered or raw gaze, before manual keyframe corrections are applied
		private NDArray? GazeBaseline => _isGazeFilterEnabled && ((object)FilteredGazeLocations != null)
			? FilteredGazeLocations
			: RawGazeLocations;

		// the gaze that is displayed and saved: the baseline with keyframe corrections applied
		internal NDArray? CorrectedGazeLocations = null;

		internal NDArray? DisplayedGazeLocations => (object)CorrectedGazeLocations != null
			? CorrectedGazeLocations
			: GazeBaseline;

		protected override NDArray? GazeData => DisplayedGazeLocations;

		public Color GazeStrokeColor
		{
			get => GazeStrokeBrush.Color;
			set
			{
				GazeStrokeBrush.Color = value;
				this.RaisePropertyChanged("GazeStrokeBrush");
			}
		}

		public int? DataStartFrame
		{
			get => dataStartFrame;
			set
			{
				dataStartFrame = value;
				this.RaisePropertyChanged("DataStartFrame");
				if (videoReader != null)
					UpdateDisplay();
			}
		}

		public int EyetrackingShiftFrameCount
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1;

		private int? dataEndFrame
		{
			get
			{
				if ((object)RawGazeLocations == null || dataStartFrame == null)
					return null;
				return DataIndexToVideoTime(RawGazeLocations.Shape[0]);
			}
		}

		private ObservableCollection<VideoKeyFrame> _videoKeyFrames =
			new ObservableCollection<VideoKeyFrame>(new List<VideoKeyFrame>());

		public ObservableCollection<VideoKeyFrame> VideoKeyFrames
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new ObservableCollection<VideoKeyFrame>(new List<VideoKeyFrame>());

		// set some default keyframes when the data start is set?
		public bool SetDefaultKeyFrames
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;

		// Gaze filtering
		private bool _isGazeFilterEnabled = Settings.Current.GazeFilterEnabled;

		public bool IsGazeFilterEnabled
		{
			get => _isGazeFilterEnabled;
			set
			{
				this.RaiseAndSetIfChanged(ref _isGazeFilterEnabled, value);
				Settings.Current.GazeFilterEnabled = value;
				Settings.Current.Save();
				if (value)
					ApplyFilter();
				else
				{
					RebuildCorrectedGaze();
					RebuildTTLMarkerCache();
				}
			}
		}

		private int _filterWindowSize = Settings.Current.GazeFilterWindowSize;

		public int FilterWindowSize
		{
			get => _filterWindowSize;
			set
			{
				this.RaiseAndSetIfChanged(ref _filterWindowSize, value);
				Settings.Current.GazeFilterWindowSize = value;
				Settings.Current.Save();
			}
		}

		private bool _filterPupilSize = Settings.Current.GazeFilterPupilSize;

		public bool FilterPupilSize
		{
			get => _filterPupilSize;
			set
			{
				this.RaiseAndSetIfChanged(ref _filterPupilSize, value);
				Settings.Current.GazeFilterPupilSize = value;
				Settings.Current.Save();
				ReapplyFilterIfEnabled();
			}
		}

		private bool _enableOutlierRemoval = Settings.Current.GazeFilterEnableOutlierRemoval;

		public bool EnableOutlierRemoval
		{
			get => _enableOutlierRemoval;
			set
			{
				this.RaiseAndSetIfChanged(ref _enableOutlierRemoval, value);
				Settings.Current.GazeFilterEnableOutlierRemoval = value;
				Settings.Current.Save();
				ReapplyFilterIfEnabled();
			}
		}

		private double _outlierThresholdX = Settings.Current.GazeFilterOutlierThresholdX;

		public double OutlierThresholdX
		{
			get => _outlierThresholdX;
			set
			{
				this.RaiseAndSetIfChanged(ref _outlierThresholdX, value);
				Settings.Current.GazeFilterOutlierThresholdX = value;
				Settings.Current.Save();
			}
		}

		private double _outlierThresholdY = Settings.Current.GazeFilterOutlierThresholdY;

		public double OutlierThresholdY
		{
			get => _outlierThresholdY;
			set
			{
				this.RaiseAndSetIfChanged(ref _outlierThresholdY, value);
				Settings.Current.GazeFilterOutlierThresholdY = value;
				Settings.Current.Save();
			}
		}

		private double _outlierThresholdRadius = Settings.Current.GazeFilterOutlierThresholdRadius;

		public double OutlierThresholdRadius
		{
			get => _outlierThresholdRadius;
			set
			{
				this.RaiseAndSetIfChanged(ref _outlierThresholdRadius, value);
				Settings.Current.GazeFilterOutlierThresholdRadius = value;
				Settings.Current.Save();
			}
		}

		private string? videoFilePath = null;
		private string gazeFileName = null;

		private string defaultSaveName => gazeFileName == null
			? "gaze locations"
			: System.IO.Path.GetFileNameWithoutExtension(gazeFileName) + " corrected.npy";

		public bool IsTemporalAlignmentExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				Settings.Current.StimulusTemporalAlignmentExpanded = value;
				Settings.Current.Save();
			}
		} = Settings.Current.StimulusTemporalAlignmentExpanded;

		public bool IsGazeInfoExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				Settings.Current.StimulusGazeInfoExpanded = value;
				Settings.Current.Save();
			}
		} = Settings.Current.StimulusGazeInfoExpanded;

		public bool IsGazeFilteringExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				Settings.Current.StimulusGazeFilteringExpanded = value;
				Settings.Current.Save();
			}
		} = Settings.Current.StimulusGazeFilteringExpanded;

		public bool IsKeyframesExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				Settings.Current.StimulusKeyframesExpanded = value;
				Settings.Current.Save();
			}
		} = Settings.Current.StimulusKeyframesExpanded;

		public StimulusGazeViewModel()
		{
			LoadGazeCommand = ReactiveCommand.Create(LoadGaze);
			SaveGazeCommand = ReactiveCommand.Create(SaveGaze);
			SetCurrentAsDataStartCommand = ReactiveCommand.Create(SetCurrentAsDataStart);
			SendToRecenteringCommand = ReactiveCommand.Create(SendToRecentering);
			ShiftEyetrackingForwardCommand = ReactiveCommand.Create(ShiftEyetrackingForward);
			ShiftEyetrackingBackCommand = ReactiveCommand.Create(ShiftEyetrackingBack);
			SetCurrentAsFirstTTLCommand = ReactiveCommand.Create(SetCurrentAsFirstTTL);
		}

		public override async void LoadVideo()
		{
			// reset gaze stuff
			dataStartFrame = null;
			IsGazeLoaded = false;
			RawGazeLocations = null;
			FilteredGazeLocations = null;
			CorrectedGazeLocations = null;
			VideoKeyFrames.Clear();
			GazeX = 0;
			GazeY = 0;

			string? fileName = await PromptForVideoAsync();
			if (fileName == null)
				return;

			videoFilePath = fileName;
			OpenVideoReader(fileName);
			SaveGazeCommand = null;
		}

		// after gaze is manually edited, updates it.
		public void UpdateGaze()
		{
			if ((object)DisplayedGazeLocations == null || dataFrame == null)
				return;

			AddKeyFrame(CurrentVideoFrame, GazeX, GazeY);
			UpdateCorrectedGazeAround(dataFrame.Value);
		}

		/// <summary>
		/// Adds a keyframe at the current video frame, recording the displayed gaze position.
		/// </summary>
		public void AddKeyFrame()
		{
			AddKeyFrame(CurrentVideoFrame);
		}

		/// <summary>
		/// Adds a keyframe at the given video frame, recording the currently displayed gaze position
		/// at that frame.
		/// </summary>
		/// <param name="frame">Video frame at which to add the keyframe</param>
		public void AddKeyFrame(int frame)
		{
			if (!(dataStartFrame.HasValue && (frame >= dataStartFrame.Value) &&
			      (frame <= dataEndFrame)))
				return;
			if ((object)DisplayedGazeLocations == null)
				return;
			int index = VideoTimeToDataIndex(frame);
			AddKeyFrame(frame, (double)DisplayedGazeLocations[index, 0], (double)DisplayedGazeLocations[index, 1]);
		}

		/// <summary>
		/// Adds a keyframe at the given video frame with an explicit gaze position, replacing any
		/// existing keyframe at the same data index. The keyframe list is kept sorted by video frame.
		/// </summary>
		/// <param name="frame">Video frame at which to add the keyframe</param>
		/// <param name="gazeX">Screen-space X of the gaze at the keyframe</param>
		/// <param name="gazeY">Screen-space Y of the gaze at the keyframe</param>
		public void AddKeyFrame(int frame, double gazeX, double gazeY)
		{
			if (dataStartFrame.HasValue && (frame >= dataStartFrame.Value) &&
			    (frame <= dataEndFrame))
			{
				int index = VideoTimeToDataIndex(frame);
				for (int i = 0; i < VideoKeyFrames.Count; i++)
				{
					// if this frame already exists as a keyframe, remove it
					if (VideoKeyFrames[i].DataIndex == index)
					{
						VideoKeyFrames.RemoveAt(i);
						break;
					}
				}

				VideoKeyFrames.Add(new VideoKeyFrame(frame, index, videoReader.FramesToTimecode(frame),
					gazeX, gazeY));
				VideoKeyFrames =
					new ObservableCollection<VideoKeyFrame>(VideoKeyFrames.OrderBy((keyframe) => keyframe.VideoFrame));
			}
		}

		/// <summary>
		/// Rebuilds the corrected gaze array from the baseline and keyframes. Frames before the first
		/// keyframe stay at the baseline, the correction is linearly interpolated between consecutive
		/// keyframes, and the last keyframe's correction is held flat to the end of the data.
		/// </summary>
		private void RebuildCorrectedGaze()
		{
			NDArray? baseline = GazeBaseline;
			if ((object)baseline == null)
			{
				CorrectedGazeLocations = null;
				return;
			}

			NDArray corrected = baseline.Clone() as NDArray;
			if (VideoKeyFrames.Count > 0)
			{
				// interpolate the correction between each pair of consecutive keyframes
				for (int keyFrameIndex = 0; keyFrameIndex < VideoKeyFrames.Count - 1; keyFrameIndex++)
					ApplyInterpolatedSegment(baseline, corrected, VideoKeyFrames[keyFrameIndex],
						VideoKeyFrames[keyFrameIndex + 1]);

				// hold the last keyframe's correction flat to the end of the data
				ApplyFlatTail(baseline, corrected, VideoKeyFrames[VideoKeyFrames.Count - 1]);
			}

			CorrectedGazeLocations = corrected;
		}

		/// <summary>
		/// Updates only the corrected-gaze segments adjacent to the keyframe at the given data index,
		/// after that keyframe was added or moved. Recomputes the segment from the previous keyframe and
		/// the segment to the next keyframe, or the flat tail if it is the last keyframe.
		/// </summary>
		/// <param name="dataIndex">Data index of the keyframe that changed</param>
		private void UpdateCorrectedGazeAround(int dataIndex)
		{
			NDArray? baseline = GazeBaseline;
			if ((object)baseline == null || (object)CorrectedGazeLocations == null)
			{
				RebuildCorrectedGaze();
				return;
			}

			int keyFramePosition = -1;
			for (int i = 0; i < VideoKeyFrames.Count; i++)
				if (VideoKeyFrames[i].DataIndex == dataIndex)
				{
					keyFramePosition = i;
					break;
				}
			if (keyFramePosition < 0)
			{
				RebuildCorrectedGaze();
				return;
			}

			VideoKeyFrame editedKeyFrame = VideoKeyFrames[keyFramePosition];

			// recompute the segment behind the edited keyframe
			if (keyFramePosition > 0)
				ApplyInterpolatedSegment(baseline, CorrectedGazeLocations,
					VideoKeyFrames[keyFramePosition - 1], editedKeyFrame);

			// recompute the segment ahead of the edited keyframe, or the flat tail if it is last
			if (keyFramePosition < VideoKeyFrames.Count - 1)
				ApplyInterpolatedSegment(baseline, CorrectedGazeLocations,
					editedKeyFrame, VideoKeyFrames[keyFramePosition + 1]);
			else
				ApplyFlatTail(baseline, CorrectedGazeLocations, editedKeyFrame);
		}

		/// <summary>
		/// Writes the baseline plus a linearly interpolated correction into the corrected array over the
		/// half-open range from the first keyframe's data index to the second keyframe's data index. The
		/// correction ramps from the start keyframe's offset to the end keyframe's offset.
		/// </summary>
		/// <param name="baseline">Baseline gaze the correction is added to</param>
		/// <param name="corrected">Corrected gaze array written into</param>
		/// <param name="startKeyFrame">Keyframe at the start of the segment</param>
		/// <param name="endKeyFrame">Keyframe at the end of the segment</param>
		private void ApplyInterpolatedSegment(NDArray baseline, NDArray corrected,
			VideoKeyFrame startKeyFrame, VideoKeyFrame endKeyFrame)
		{
			int startIndex = startKeyFrame.DataIndex;
			int endIndex = endKeyFrame.DataIndex;
			double startDeltaX = startKeyFrame.GazeX - (double)baseline[startIndex, 0];
			double startDeltaY = startKeyFrame.GazeY - (double)baseline[startIndex, 1];
			double endDeltaX = endKeyFrame.GazeX - (double)baseline[endIndex, 0];
			double endDeltaY = endKeyFrame.GazeY - (double)baseline[endIndex, 1];
			int numFrames = endIndex - startIndex;
			for (int i = startIndex; i < endIndex; i++)
			{
				double multiplier = numFrames == 0 ? 0.0 : (double)(i - startIndex) / numFrames;
				corrected[i, 0] = (double)baseline[i, 0] + startDeltaX + multiplier * (endDeltaX - startDeltaX);
				corrected[i, 1] = (double)baseline[i, 1] + startDeltaY + multiplier * (endDeltaY - startDeltaY);
			}
		}

		/// <summary>
		/// Writes the baseline plus the keyframe's correction held flat from the keyframe's data index to
		/// the end of the data.
		/// </summary>
		/// <param name="baseline">Baseline gaze the correction is added to</param>
		/// <param name="corrected">Corrected gaze array written into</param>
		/// <param name="keyFrame">Keyframe whose correction is held flat to the end</param>
		private void ApplyFlatTail(NDArray baseline, NDArray corrected, VideoKeyFrame keyFrame)
		{
			int startIndex = keyFrame.DataIndex;
			double deltaX = keyFrame.GazeX - (double)baseline[startIndex, 0];
			double deltaY = keyFrame.GazeY - (double)baseline[startIndex, 1];
			for (int i = startIndex; i < baseline.Shape[0]; i++)
			{
				corrected[i, 0] = (double)baseline[i, 0] + deltaX;
				corrected[i, 1] = (double)baseline[i, 1] + deltaY;
			}
		}

		private int? PreviousVideoKeyFrame
		{
			get
			{
				for (int i = VideoKeyFrames.Count - 1; i > -1; i--)
				{
					if (VideoKeyFrames[i] < CurrentVideoFrame)
						return VideoKeyFrames[i].VideoFrame;
				}

				return null;
			}
		}

		private int? PreviousDataKeyFrame
		{
			get
			{
				if (PreviousVideoKeyFrame.HasValue)
					return VideoTimeToDataIndex(PreviousVideoKeyFrame.Value);
				return null;
			}
		}


		private int? NextVideoKeyFrame
		{
			get
			{
				for (int i = 0; i < VideoKeyFrames.Count; i++)
				{
					if (VideoKeyFrames[i] > CurrentVideoFrame)
						return VideoKeyFrames[i].VideoFrame;
				}

				return null;
			}
		}

		private int? NextDataKeyFrame
		{
			get
			{
				if (NextVideoKeyFrame.HasValue)
					return VideoTimeToDataIndex(NextVideoKeyFrame.Value);
				return null;
			}
		}

		public async void LoadGaze()
		{
			string? fileName = await GazeFileDialog.ShowAsync(MainWindow);
			if (fileName == null)
				return;
			IsLoadingGaze = true;
			int capturedSampleRate = 0;
			NDArray? loadedGaze = null;
			await Task.Run(() =>
			{
				loadedGaze = GazeLoader.Load(fileName, out int sampleRate);
				capturedSampleRate = sampleRate;
			});
			RawGazeLocations = loadedGaze;
			if (capturedSampleRate > 0)
				EyetrackingFPS = capturedSampleRate;
			IsGazeLoaded = true;
			gazeFileName = fileName;
			if (_isGazeFilterEnabled)
				await ApplyFilter();
			else
			{
				RebuildCorrectedGaze();
				RebuildTTLMarkerCache();
			}
			if (videoReader != null)
			{
				SetCurrentAsDataStart();
				UpdateDisplay();
			}
			IsLoadingGaze = false;
		}

		public async void SaveGaze()
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog()
			{
				Title = "Load gaze locations",
				InitialFileName = defaultSaveName
			};
			saveFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Numpy file",
				Extensions = { "npy" },
			});
			string? fileName = await saveFileDialog.ShowAsync(MainWindow);

			if (fileName != null)
			{
				GazeLoader.Save(fileName, DisplayedGazeLocations);
			}
		}

		public void SetCurrentAsDataStart()
		{
			dataStartFrame = videoReader.CurrentFrameNumber;
			this.RaisePropertyChanged("DataStartFrame");

			VideoKeyFrames.Clear();
			if (SetDefaultKeyFrames)
			{
				AddKeyFrame(dataStartFrame.Value); // start of data
				AddKeyFrame(dataStartFrame.Value +
				            videoReader.fps * 37 * 2); // end of eyetracking calibration in driving
				if (dataEndFrame >= videoReader.frameCount)
					AddKeyFrame(videoReader.frameCount - 1);
				else AddKeyFrame(dataEndFrame.Value - 1);
			}

			RebuildCorrectedGaze();
		}

		public void SendToRecentering()
		{
			if (RecenteringViewModel == null || videoFilePath == null || !IsGazeLoaded || dataStartFrame == null)
				return;
			GazeFilterSettings filterSettings = new GazeFilterSettings
			{
				IsEnabled = _isGazeFilterEnabled,
				MedianFilterWindowSize = _filterWindowSize,
				FilterPupilSize = _filterPupilSize,
				EnableOutlierRemoval = _enableOutlierRemoval,
				OutlierThresholdX = _outlierThresholdX,
				OutlierThresholdY = _outlierThresholdY,
				OutlierThresholdRadius = _outlierThresholdRadius
			};
			RecenteringViewModel.LoadFromStimulusGaze(videoFilePath, DisplayedGazeLocations, dataStartFrame.Value,
				EyetrackingFPS, filterSettings, gazeFileName);
			SwitchToRecenteringTab?.Invoke();
		}

		public void SetCurrentAsFirstTTL()
		{
			if (videoReader == null || (object)DisplayedGazeLocations == null) return;
			int? firstTTLGazeIndex = Recenterer.FindFirstTTLGazeIndex(DisplayedGazeLocations);
			if (firstTTLGazeIndex == null) return;
			double gazeElapsedTime = (double)firstTTLGazeIndex.Value / EyetrackingFPS;
			dataStartFrame = CurrentVideoFrame - (int)(gazeElapsedTime * videoReader.fps);
			this.RaisePropertyChanged("DataStartFrame");
			UpdateDisplay();
		}

		public void ShiftEyetrackingForward()
		{
			if (dataStartFrame == null) return;
			dataStartFrame = dataStartFrame.Value + EyetrackingShiftFrameCount;
			this.RaisePropertyChanged("DataStartFrame");
			if (videoReader != null)
				UpdateDisplay();
		}

		public void ShiftEyetrackingBack()
		{
			if (dataStartFrame == null) return;
			dataStartFrame = dataStartFrame.Value - EyetrackingShiftFrameCount;
			this.RaisePropertyChanged("DataStartFrame");
			if (videoReader != null)
				UpdateDisplay();
		}

		public void ReapplyFilterIfEnabled()
		{
			if (_isGazeFilterEnabled)
				ApplyFilter();
		}

		private async Task ApplyFilter()
		{
			if ((object)RawGazeLocations == null) return;

			if (IsVideoPlaying)
				PlayPause();
			IsPlaybackEnabled = false;
			StatusText = "Applying filter...";
			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = true;

			NDArray rawSnapshot = RawGazeLocations;
			NDArray result = await Task.Run(() => GazeFilter.Filter(
				rawSnapshot,
				FilterWindowSize,
				FilterPupilSize,
				EnableOutlierRemoval,
				OutlierThresholdX,
				OutlierThresholdY,
				OutlierThresholdRadius));
			FilteredGazeLocations = result;
			RebuildCorrectedGaze();
			RebuildTTLMarkerCache();

			IsProgressBarVisible = false;
			IsProgressBarIndeterminate = false;
			StatusText = "Idle";
			IsPlaybackEnabled = true;

			if (videoReader != null)
				UpdateDisplay();
		}
	}
}