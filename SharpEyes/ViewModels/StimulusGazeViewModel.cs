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
		internal NDArray? DisplayedGazeLocations => _isGazeFilterEnabled && ((object)FilteredGazeLocations != null) ? FilteredGazeLocations : RawGazeLocations;

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

		private ObservableCollection<VideoKeyFrame> _videoKeyFrames = new ObservableCollection<VideoKeyFrame>(new List<VideoKeyFrame>());
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

		/// <summary>
		/// For a given index in the data, get the corresponding video frame
		/// </summary>
		/// <param name="dataIndex">index in eyetracking data</param>
		/// <returns>video frame number</returns>
		private int DataIndexToVideoTime(int dataIndex)
		{
			double dataElapsedTime = (double)dataIndex / EyetrackingFPS; // in seconds
			int dataElapsedFrames = (int)Math.Round(dataElapsedTime * videoReader.fps);
			return dataStartFrame.Value + dataElapsedFrames;
		}

		// after gaze is manually edited, updates it.
		public void UpdateGaze()
		{
			if ((object)RawGazeLocations == null || dataFrame == null)
				return;

			double deltaX = GazeX - RawGazeLocations[dataFrame, 0];
			double deltaY = GazeY - RawGazeLocations[dataFrame, 1];

			AddKeyFrame();

			// if there is a keyframe before, ramp the delta from the previous keyframe
			// to this new keyframe
			if (PreviousDataKeyFrame.HasValue)
			{
				int numFrames = dataFrame.Value - PreviousDataKeyFrame.Value;
				double multiplier;
				for (int i = PreviousDataKeyFrame.Value; i < dataFrame; i++)
				{
					multiplier = (double)(i - PreviousDataKeyFrame.Value) / numFrames;
					RawGazeLocations[i, 0] += multiplier * deltaX;
					RawGazeLocations[i, 1] += multiplier * deltaY;
				}
			}
			// update all following data frames with this delta
			RawGazeLocations[new Slice(dataFrame.Value, null), 0] += deltaX;
			RawGazeLocations[new Slice(dataFrame.Value, null), 1] += deltaY;
		}

		public void AddKeyFrame()
		{
			AddKeyFrame(CurrentVideoFrame);
		}

		public void AddKeyFrame(int frame)
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
															RawGazeLocations[index, 0], RawGazeLocations[index, 1]));
				VideoKeyFrames = new ObservableCollection<VideoKeyFrame>(VideoKeyFrames.OrderBy((keyframe) => keyframe.VideoFrame));
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
			if (videoReader != null)
				SetCurrentAsDataStart();
			if (_isGazeFilterEnabled)
				await ApplyFilter();
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
				AddKeyFrame(dataStartFrame.Value);	// start of data
				AddKeyFrame(dataStartFrame.Value + videoReader.fps * 37 * 2);	// end of eyetracking calibration in driving
				if (dataEndFrame >= videoReader.frameCount)
					AddKeyFrame(videoReader.frameCount - 1);
				else AddKeyFrame(dataEndFrame.Value - 1);
			}
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
			RecenteringViewModel.LoadFromStimulusGaze(videoFilePath, DisplayedGazeLocations, dataStartFrame.Value, EyetrackingFPS, filterSettings, gazeFileName);
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

			IsProgressBarVisible = false;
			IsProgressBarIndeterminate = false;
			StatusText = "Idle";
			IsPlaybackEnabled = true;

			if (videoReader != null)
				UpdateDisplay();
		}
	}

	public class TrailGazePoint : ReactiveObject
	{
		public double Left
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}

		public double Top
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}

		public double Opacity
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}

		public bool IsVisible
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;
	}
}
