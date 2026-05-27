using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Eyetracking;
using NumSharp;
using ReactiveUI;
using SharpEyes.Models;
using Num = NumSharp.np;

namespace SharpEyes.ViewModels
{
	public class StimulusGazeViewModel : ViewModelBase
	{
		// == Commands ==
		public ReactiveCommand<Unit, Unit> LoadVideoCommand { get; set; }
		public ReactiveCommand<Unit, Unit>? PlayPauseCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit>? PreviousFrameCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit>? NextFrameCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit> LoadGazeCommand { get; set; }
		public ReactiveCommand<Unit, Unit>? SaveGazeCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit> SetCurrentAsDataStartCommand { get; set; }
		public ReactiveCommand<Unit, Unit>? FindDataStartCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit> SendToRecenteringCommand { get; set; }

		// Set by MainWindowViewModel after construction
		public RecenteringViewModel? RecenteringViewModel { get; set; }
		public Action? SwitchToRecenteringTab { get; set; }

		// == window reference for showing dialogs
		public Window? MainWindow =>
			Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
				? desktop.MainWindow
				: null;

		// == UI elements ==
		private bool _isMovingGaze = false;
		public bool IsMovingGaze
		{
			get => _isMovingGaze;
			set
			{
				this.RaiseAndSetIfChanged(ref _isMovingGaze, value);
			}
		}

		// progress bar
		private string _statusText = "Idle";
		public string StatusText
		{
			get => _statusText;
			set => this.RaiseAndSetIfChanged(ref _statusText, value);
		}
		private bool _isProgressBarVisible = false;
		public bool IsProgressBarVisible
		{
			get => _isProgressBarVisible;
			set => this.RaiseAndSetIfChanged(ref _isProgressBarVisible, value);
		}
		private bool _isProgressBarIndeterminate = false;
		public bool IsProgressBarIndeterminate
		{
			get => _isProgressBarIndeterminate;
			set => this.RaiseAndSetIfChanged(ref _isProgressBarIndeterminate, value);
		}
		private double _progressBarValue = 0;
		public double ProgressBarValue
		{
			get => _progressBarValue;
			set => this.RaiseAndSetIfChanged(ref _progressBarValue, value);
		}
		
		// == video stuff ==
		private VideoReader? videoReader = null;
		private DispatcherTimer videoPlaybackTimer;

		private int _videoWidth = 1024;
		public int VideoWidth
		{
			get => _videoWidth;
			set => this.RaiseAndSetIfChanged(ref _videoWidth, value);
		}

		private int _videoHeight = 768;
		public int VideoHeight
		{
			get => _videoHeight;
			set => this.RaiseAndSetIfChanged(ref _videoHeight, value);
		}

		private string _currentVideoTime = "0:00:00;00";

		public string CurrentVideoTime
		{
			get => _currentVideoTime;
			set => this.RaiseAndSetIfChanged(ref _currentVideoTime, value);
		}
		private string _totalVideoTime = "0:00:00;00";

		public string TotalVideoTime
		{
			get => _totalVideoTime;
			set => this.RaiseAndSetIfChanged(ref _totalVideoTime, value);
		}
		public string PlayPauseButtonText => IsVideoPlaying ? "Pause" : "Play";
		private bool _isVideoPlaying = false;

		public bool IsVideoPlaying
		{
			get => _isVideoPlaying;
			set
			{
				this.RaiseAndSetIfChanged(ref _isVideoPlaying, value);
				this.RaisePropertyChanged("PlayPauseButtonText");
			}
		}
		private int _currentVideoFrame = 0;
		public int CurrentVideoFrame
		{
			get => _currentVideoFrame;
			set => this.RaiseAndSetIfChanged(ref _currentVideoFrame, value);
		}

		private int _totalVideoFrames = 0;
		public int TotalVideoFrames
		{
			get => _totalVideoFrames;
			set => this.RaiseAndSetIfChanged(ref _totalVideoFrames, value);
		}

		private Bitmap? _videoFrame = null;
		public Bitmap? VideoFrame
		{
			get => _videoFrame;
			set => this.RaiseAndSetIfChanged(ref _videoFrame, value);
		}

		// Gaze overlay info
		internal NDArray? RawGazeLocations = null;
		internal NDArray? FilteredGazeLocations = null;
		internal NDArray? DisplayedGazeLocations => _isGazeFilterEnabled && ((object)FilteredGazeLocations != null) ? FilteredGazeLocations : RawGazeLocations;

		private bool _isGazeLoaded = false;
		public bool IsGazeLoaded
		{
			get => _isGazeLoaded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isGazeLoaded, value);
				this.RaisePropertyChanged("IsGazeEllipseVisible");
			}
		}

		private bool _isGazeAtNaN = false;
		public bool IsGazeAtNaN
		{
			get => _isGazeAtNaN;
			set
			{
				this.RaiseAndSetIfChanged(ref _isGazeAtNaN, value);
				this.RaisePropertyChanged("IsGazeEllipseVisible");
			}
		}

		public bool IsGazeEllipseVisible => IsGazeLoaded && !IsGazeAtNaN;
		internal int? dataStartFrame = null;

		private int? dataEndFrame
		{
			get
			{
				if ((object)RawGazeLocations == null || dataStartFrame == null)
					return null;
				return DataIndexToVideoTime(RawGazeLocations.Shape[0]);
			}
		}

		private int? dataFrame // used to index into the gaze matrix. Updated by UpdateDisplay
		{
			get
			{
				if (dataStartFrame == null) return null;
				return VideoTimeToDataIndex(CurrentVideoFrame);
			}
		}

		private double _gazeX = 0;
		private double _gazeY = 0;
		public double GazeX
		{
			get => _gazeX;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeX, value);
				this.RaisePropertyChanged("GazeCircleLeft");
				this.RaisePropertyChanged("GazeXText");
			}
		}
		public double GazeY
		{
			get => _gazeY;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeY, value);
				this.RaisePropertyChanged("GazeCircleTop");
				this.RaisePropertyChanged("GazeYText");
			}
		}
		public double GazeCircleLeft => _gazeX - GazeRadius;
		public double GazeCircleTop => _gazeY - GazeRadius;

		private double _gazeDiameter = 204;
		public double GazeRadius => _gazeDiameter / 2;
		public double GazeDiameter
		{
			get => _gazeDiameter;
			set
			{
				// gaze diameter is bounded
				_gazeDiameter = value;

				this.RaisePropertyChanged("GazeDiameter");
				this.RaisePropertyChanged("GazeRadius");
				this.RaisePropertyChanged("GazeRadiusText");
				// because the circle is set by its left/top corner
				this.RaisePropertyChanged("GazeCircleLeft");
				this.RaisePropertyChanged("GazeCircleTop");
			}
		}

		private double _gazeStrokeThickness = 4.0;
		public double GazeStrokeThickness
		{
			get => _gazeStrokeThickness;
			set => this.RaiseAndSetIfChanged(ref _gazeStrokeThickness, value);
		}

		private double _gazeStrokeOpacity = 0.75;
		public double GazeStrokeOpacity
		{
			get => _gazeStrokeOpacity;
			set => this.RaiseAndSetIfChanged(ref _gazeStrokeOpacity, value);
		}
		public Color GazeStrokeColor
		{
			get => GazeStrokeBrush.Color;
			set
			{
				GazeStrokeBrush.Color = value;
				this.RaisePropertyChanged("GazeStrokeBrush");
			}
		}

		public SolidColorBrush GazeStrokeBrush { get; set; } = new SolidColorBrush(Colors.LimeGreen);

		private int _eyetrackingFPS = 60;
		public int EyetrackingFPS
		{
			get => _eyetrackingFPS;
			set => this.RaiseAndSetIfChanged(ref _eyetrackingFPS, value);
		}

		private double _trailLength = 1.0;
		public double TrailLength
		{
			get => _trailLength;
			set => this.RaiseAndSetIfChanged(ref _trailLength, value);
		}

		private ObservableCollection<TrailGazePoint> _trailPoints;
		public ObservableCollection<TrailGazePoint> TrailPoints
		{
			get => _trailPoints;
			private set => this.RaiseAndSetIfChanged(ref _trailPoints, value);
		}

		private ObservableCollection<VideoKeyFrame> _videoKeyFrames = new ObservableCollection<VideoKeyFrame>(new List<VideoKeyFrame>());
		public ObservableCollection<VideoKeyFrame> VideoKeyFrames
		{
			get => _videoKeyFrames;
			set => this.RaiseAndSetIfChanged(ref _videoKeyFrames, value);
		}

		// set some default keyframes when the data start is set?
		private bool _setDefaultKeyFrames = false;
		public bool SetDefaultKeyFrames
		{
			get => _setDefaultKeyFrames;
			set => this.RaiseAndSetIfChanged(ref _setDefaultKeyFrames, value);
		}

		private bool _isPlaybackEnabled = true;
		public bool IsPlaybackEnabled
		{
			get => _isPlaybackEnabled;
			set => this.RaiseAndSetIfChanged(ref _isPlaybackEnabled, value);
		}

		// Gaze filtering
		private bool _isGazeFilterEnabled = false;
		public bool IsGazeFilterEnabled
		{
			get => _isGazeFilterEnabled;
			set
			{
				this.RaiseAndSetIfChanged(ref _isGazeFilterEnabled, value);
				if (value)
					ApplyFilter();
			}
		}

		private int _filterWindowSize = 15;
		public int FilterWindowSize
		{
			get => _filterWindowSize;
			set => this.RaiseAndSetIfChanged(ref _filterWindowSize, value);
		}

		private bool _filterPupilSize = true;
		public bool FilterPupilSize
		{
			get => _filterPupilSize;
			set
			{
				this.RaiseAndSetIfChanged(ref _filterPupilSize, value);
				ReapplyFilterIfEnabled();
			}
		}

		private bool _enableOutlierRemoval = false;
		public bool EnableOutlierRemoval
		{
			get => _enableOutlierRemoval;
			set
			{
				this.RaiseAndSetIfChanged(ref _enableOutlierRemoval, value);
				ReapplyFilterIfEnabled();
			}
		}

		private double _outlierThresholdX = 95;
		public double OutlierThresholdX
		{
			get => _outlierThresholdX;
			set => this.RaiseAndSetIfChanged(ref _outlierThresholdX, value);
		}

		private double _outlierThresholdY = 95;
		public double OutlierThresholdY
		{
			get => _outlierThresholdY;
			set => this.RaiseAndSetIfChanged(ref _outlierThresholdY, value);
		}

		private double _outlierThresholdRadius = 95;
		public double OutlierThresholdRadius
		{
			get => _outlierThresholdRadius;
			set => this.RaiseAndSetIfChanged(ref _outlierThresholdRadius, value);
		}

		private string? videoFilePath = null;
		private string gazeFileName = null;

		private string defaultSaveName => gazeFileName == null
			? "gaze locations"
			: System.IO.Path.GetFileNameWithoutExtension(gazeFileName) + " corrected.npy";

		public StimulusGazeViewModel()
		{
			LoadVideoCommand = ReactiveCommand.Create(LoadVideo);
			PlayPauseCommand = ReactiveCommand.Create(PlayPause);
			LoadGazeCommand = ReactiveCommand.Create(LoadGaze);
			SaveGazeCommand = ReactiveCommand.Create(SaveGaze);
			SetCurrentAsDataStartCommand = ReactiveCommand.Create(SetCurrentAsDataStart);
			videoPlaybackTimer = new DispatcherTimer();
			videoPlaybackTimer.Tick += this.VideoTimerTick;

			PreviousFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(-1); });
			NextFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(1); });
			SendToRecenteringCommand = ReactiveCommand.Create(SendToRecentering);
			TrailPoints = new ObservableCollection<TrailGazePoint>(
				Enumerable.Range(0, 10).Select(_ => new TrailGazePoint()));
		}

		public async void LoadVideo()
		{
			// reset gaze stuff
			dataStartFrame = null;
			IsGazeLoaded = false;
			RawGazeLocations = null;
			FilteredGazeLocations = null;
			VideoKeyFrames.Clear();
			GazeX = 0;
			GazeY = 0;

			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load stimulus video"
			};
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Videos",
				Extensions = { "avi", "mkv", "mp4", "m4v" }
			});
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;

			videoFilePath = fileName[0];
			videoReader = new VideoReader(fileName[0]);
			videoReader.ReadFrame();
			VideoFrame = videoReader.GetFrameForDisplay();
			videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)videoReader.fps);
			TotalVideoFrames = videoReader.frameCount;
			TotalVideoTime = videoReader.FramesToTimecode(videoReader.frameCount - 1);
			SaveGazeCommand = null;
		}

		public void PlayPause()
		{
			if (IsVideoPlaying)
				videoPlaybackTimer.Stop();
			else
				videoPlaybackTimer.Start();
			IsVideoPlaying = !IsVideoPlaying;
		}

		public void ChangeFrame(int delta)
		{
			if (videoReader != null)
				ShowFrame(videoReader.CurrentFrameNumber + delta);
		}

		/// <summary>
		/// Called by the dispatcher timer to play the video. this is called once every frame to read
		/// in a video frame and update the display
		/// </summary>
		public void VideoTimerTick(object? sender, EventArgs e)
		{
			if (videoReader.CurrentFrameNumber >= videoReader.frameCount - 1)
				PlayPause();
			videoReader.ReadFrame();
			UpdateDisplay();
		}

		public void ShowFrame()
		{
			ShowFrame(CurrentVideoFrame);
		}

		public void ShowFrame(int frame)
		{
			videoReader.CurrentFrameNumber = frame;
			UpdateDisplay();
		}

		/// <summary>
		/// Given a video frame index, gets the first corresponding index in the gaze locations
		/// </summary>
		/// <param name="videoFrame"></param>
		/// <returns></returns>
		private int VideoTimeToDataIndex(int videoFrame)
		{
			int videoFramesElapsed = videoFrame - dataStartFrame.Value;
			if (videoFramesElapsed < 0)
				return 0;
			double videoElapsedTime = (double)videoFramesElapsed / videoReader.fps;
			return (int)(videoElapsedTime * EyetrackingFPS);
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

		public void UpdateDisplay()
		{
			VideoFrame = videoReader.GetFrameForDisplay();
			CurrentVideoFrame = videoReader.CurrentFrameNumber;
			CurrentVideoTime = videoReader.FramesToTimecode(videoReader.CurrentFrameNumber);
			// TODO: set gaze circle location
			if (dataStartFrame != null)
			{
				double gazeXValue = DisplayedGazeLocations[dataFrame, 0];
				double gazeYValue = DisplayedGazeLocations[dataFrame, 1];
				if (Double.IsNaN(gazeXValue) || Double.IsNaN(gazeYValue))
				{
					IsGazeAtNaN = true;
				}
				else
				{
					IsGazeAtNaN = false;
					GazeX = gazeXValue;
					GazeY = gazeYValue;
				}
				UpdateTrailPoints();
			}
			else
			{
				IsGazeAtNaN = false;
				foreach (TrailGazePoint point in TrailPoints)
					point.IsVisible = false;
			}
		}

		private void UpdateTrailPoints()
		{
			const int trailCircleCount = 10;
			double stepSeconds = TrailLength / trailCircleCount;
			int stepVideoFrames = Math.Max(1, (int)(stepSeconds * videoReader.fps));
			// snap to multiples of the step so circles stay fixed between step intervals
			int currentFrameBase = (videoReader.CurrentFrameNumber / stepVideoFrames) * stepVideoFrames;
			for (int i = 0; i < trailCircleCount; i++)
			{
				int trailVideoFrame = currentFrameBase - (trailCircleCount - i) * stepVideoFrames;
				if (trailVideoFrame < 0 || trailVideoFrame < dataStartFrame.Value)
				{
					TrailPoints[i].IsVisible = false;
					continue;
				}
				int trailDataIndex = VideoTimeToDataIndex(trailVideoFrame);
				if (trailDataIndex >= DisplayedGazeLocations.Shape[0])
				{
					TrailPoints[i].IsVisible = false;
					continue;
				}
				double trailX = (double)DisplayedGazeLocations[trailDataIndex, 0];
				double trailY = (double)DisplayedGazeLocations[trailDataIndex, 1];
				if (Double.IsNaN(trailX) || Double.IsNaN(trailY))
				{
					TrailPoints[i].IsVisible = false;
					continue;
				}
				TrailPoints[i].Left = trailX - GazeRadius;
				TrailPoints[i].Top = trailY - GazeRadius;
				TrailPoints[i].Opacity = GazeStrokeOpacity * (double)i / (trailCircleCount - 1);
				TrailPoints[i].IsVisible = true;
			}
		}

		// after gaze is manually edited, updates it.
		public void UpdateGaze()
		{
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
			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load gaze locations"
			};
			FileDialogFilter allFilesFilter = new FileDialogFilter()
			{
				Name = "All supported files",
				Extensions = { "npy", "csv", "txt" }
			};
			if (GazeLoader.IsEDFSupported)
				allFilesFilter.Extensions.Add("edf");
			openFileDialog.Filters.Add(allFilesFilter);
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Numpy file",
				Extensions = { "npy" }
			});
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Comma-separated values",
				Extensions = { "csv" }
			});
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Eyelink text file",
				Extensions = { "txt" }
			});
			if (GazeLoader.IsEDFSupported)
				openFileDialog.Filters.Add(new FileDialogFilter()
				{
					Name = "Eyelink EDF file",
					Extensions = { "edf" }
				});
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;
			int parsedSampleRate;
			RawGazeLocations = GazeLoader.Load(fileName[0], out parsedSampleRate);
			if (parsedSampleRate > 0)
				EyetrackingFPS = parsedSampleRate;
			IsGazeLoaded = true;
			gazeFileName = fileName[0];
			ReapplyFilterIfEnabled();
			if (videoReader != null)
				SetCurrentAsDataStart();
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
			RecenteringViewModel.LoadFromStimulusGaze(videoFilePath, DisplayedGazeLocations, dataStartFrame.Value, EyetrackingFPS);
			SwitchToRecenteringTab?.Invoke();
		}

		public void ReapplyFilterIfEnabled()
		{
			if (_isGazeFilterEnabled)
				ApplyFilter();
		}

		private async void ApplyFilter()
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
		private double _left;
		public double Left
		{
			get => _left;
			set => this.RaiseAndSetIfChanged(ref _left, value);
		}

		private double _top;
		public double Top
		{
			get => _top;
			set => this.RaiseAndSetIfChanged(ref _top, value);
		}

		private double _opacity;
		public double Opacity
		{
			get => _opacity;
			set => this.RaiseAndSetIfChanged(ref _opacity, value);
		}

		private bool _isVisible = false;
		public bool IsVisible
		{
			get => _isVisible;
			set => this.RaiseAndSetIfChanged(ref _isVisible, value);
		}
	}
}
