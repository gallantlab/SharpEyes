using System;
using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NumSharp;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	public class MotionEnergyViewModel : ViewModelBase
	{
		public ReactiveCommand<Unit, Unit> LoadVideoCommand { get; }
		public ReactiveCommand<Unit, Unit>? PlayPauseCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? PreviousFrameCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? NextFrameCommand { get; } = null;

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

		private double _progressBarValue = 0;
		public double ProgressBarValue
		{
			get => _progressBarValue;
			set => this.RaiseAndSetIfChanged(ref _progressBarValue, value);
		}

		private readonly MotionEnergyModel _motionEnergyModel = new MotionEnergyModel();

		private VideoReader? _videoReader = null;
		private DispatcherTimer _videoPlaybackTimer;

		private int _videoWidth = 1024;
		public int VideoWidth
		{
			get => _videoWidth;
			set
			{
				this.RaiseAndSetIfChanged(ref _videoWidth, value);
				this.RaisePropertyChanged("RecenteringCanvasWidth");
				this.RaisePropertyChanged("RecenteringImageLeft");
				this.RaisePropertyChanged("OutputFrameSizeText");
				this.RaisePropertyChanged("VideoFrameSizeText");
			}
		}

		private int _videoHeight = 768;
		public int VideoHeight
		{
			get => _videoHeight;
			set
			{
				this.RaiseAndSetIfChanged(ref _videoHeight, value);
				this.RaisePropertyChanged("RecenteringCanvasHeight");
				this.RaisePropertyChanged("RecenteringImageTop");
				this.RaisePropertyChanged("OutputFrameSizeText");
				this.RaisePropertyChanged("VideoFrameSizeText");
			}
		}

		// == Gaze / recentering (populated via LoadFromRecentering) ==

		private NDArray? _gazeLocations = null;
		private int? _dataStartFrame = null;
		private int _eyetrackingFPS = 60;
		private int _gazeSpaceWidth = 1024;
		private int _gazeSpaceHeight = 768;

		private bool _isLoadedFromRecentering = false;
		public bool IsLoadedFromRecentering
		{
			get => _isLoadedFromRecentering;
			private set => this.RaiseAndSetIfChanged(ref _isLoadedFromRecentering, value);
		}

		private double _gazeX = 0;
		public double GazeX
		{
			get => _gazeX;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeX, value);
				this.RaisePropertyChanged("RecenteringImageLeft");
			}
		}

		private double _gazeY = 0;
		public double GazeY
		{
			get => _gazeY;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeY, value);
				this.RaisePropertyChanged("RecenteringImageTop");
			}
		}

		public int RecenteringCanvasWidth => (int)(VideoWidth * _padPercent / 100.0);
		public int RecenteringCanvasHeight => (int)(VideoHeight * _padPercent / 100.0);
		public double RecenteringImageLeft => VideoWidth * _padPercent / 100.0 / 2.0 - GazeX * (double)VideoWidth / _gazeSpaceWidth;
		public double RecenteringImageTop  => VideoHeight * _padPercent / 100.0 / 2.0 - GazeY * (double)VideoHeight / _gazeSpaceHeight;

		// == Video output parameters ==

		private double _padPercent = 200;
		public double PadPercent
		{
			get => _padPercent;
			set
			{
				this.RaiseAndSetIfChanged(ref _padPercent, value);
				this.RaisePropertyChanged("RecenteringCanvasWidth");
				this.RaisePropertyChanged("RecenteringCanvasHeight");
				this.RaisePropertyChanged("RecenteringImageLeft");
				this.RaisePropertyChanged("RecenteringImageTop");
				this.RaisePropertyChanged("OutputFrameSizeText");
			}
		}

		private double _padValue = 0.1;
		public double PadValue
		{
			get => _padValue;
			set
			{
				this.RaiseAndSetIfChanged(ref _padValue, value);
				this.RaisePropertyChanged("PadValueBrush");
			}
		}

		public SolidColorBrush PadValueBrush
		{
			get
			{
				byte grayValue = (byte)(_padValue * 255);
				return new SolidColorBrush(new Color(255, grayValue, grayValue, grayValue));
			}
		}

		private double _frameScale = 0.125;
		public double FrameScale
		{
			get => _frameScale;
			set
			{
				this.RaiseAndSetIfChanged(ref _frameScale, value);
				this.RaisePropertyChanged("OutputFrameSizeText");
				this.RaisePropertyChanged("VideoFrameSizeText");
			}
		}

		public string OutputFrameSizeText => String.Format("{0} x {1}",
			(int)(VideoWidth * _padPercent / 100.0 * _frameScale),
			(int)(VideoHeight * _padPercent / 100.0 * _frameScale));
		public string VideoFrameSizeText => String.Format("{0} x {1}",
			(int)(VideoWidth * _frameScale),
			(int)(VideoHeight * _frameScale));

		// == Motion-energy pyramid parameters ==

		private double _videoFps = 30;
		public double VideoFps
		{
			get => _videoFps;
			set => this.RaiseAndSetIfChanged(ref _videoFps, value);
		}

		public ObservableCollection<double> SpatialFrequencies { get; } =
			new ObservableCollection<double> { 0, 2, 4, 8, 16, 32 };
		public ObservableCollection<double> TemporalFrequencies { get; } =
			new ObservableCollection<double> { 0, 2, 4, 8, 16 };
		public ObservableCollection<double> Directions { get; } =
			new ObservableCollection<double> { 0, 45, 90, 135, 180, 225, 270, 315 };

		private int _selectedSpatialFrequencyIndex = -1;
		public int SelectedSpatialFrequencyIndex
		{
			get => _selectedSpatialFrequencyIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedSpatialFrequencyIndex, value);
		}

		private int _selectedTemporalFrequencyIndex = -1;
		public int SelectedTemporalFrequencyIndex
		{
			get => _selectedTemporalFrequencyIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedTemporalFrequencyIndex, value);
		}

		private int _selectedDirectionIndex = -1;
		public int SelectedDirectionIndex
		{
			get => _selectedDirectionIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedDirectionIndex, value);
		}

		private double _newSpatialFrequency = 0;
		public double NewSpatialFrequency
		{
			get => _newSpatialFrequency;
			set => this.RaiseAndSetIfChanged(ref _newSpatialFrequency, value);
		}

		private double _newTemporalFrequency = 0;
		public double NewTemporalFrequency
		{
			get => _newTemporalFrequency;
			set => this.RaiseAndSetIfChanged(ref _newTemporalFrequency, value);
		}

		private double _newDirection = 0;
		public double NewDirection
		{
			get => _newDirection;
			set => this.RaiseAndSetIfChanged(ref _newDirection, value);
		}

		public ReactiveCommand<Unit, Unit> AddSpatialFrequencyCommand { get; }
		public ReactiveCommand<Unit, Unit> RemoveSpatialFrequencyCommand { get; }
		public ReactiveCommand<Unit, Unit> AddTemporalFrequencyCommand { get; }
		public ReactiveCommand<Unit, Unit> RemoveTemporalFrequencyCommand { get; }
		public ReactiveCommand<Unit, Unit> AddDirectionCommand { get; }
		public ReactiveCommand<Unit, Unit> RemoveDirectionCommand { get; }

		private Bitmap? _videoFrame = null;
		public Bitmap? VideoFrame
		{
			get => _videoFrame;
			set => this.RaiseAndSetIfChanged(ref _videoFrame, value);
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

		public string PlayPauseButtonText => IsVideoPlaying ? "Pause" : "Play";
		public bool CanPlayVideo => _videoReader != null;

		public MotionEnergyViewModel()
		{
			LoadVideoCommand = ReactiveCommand.Create(LoadVideo);
			PlayPauseCommand = ReactiveCommand.Create(PlayPause);
			PreviousFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(-1); });
			NextFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(1); });
			_videoPlaybackTimer = new DispatcherTimer();
			_videoPlaybackTimer.Tick += VideoTimerTick;
			AddSpatialFrequencyCommand = ReactiveCommand.Create(() => SpatialFrequencies.Add(_newSpatialFrequency));
			RemoveSpatialFrequencyCommand = ReactiveCommand.Create(() =>
			{
				if (_selectedSpatialFrequencyIndex >= 0 && _selectedSpatialFrequencyIndex < SpatialFrequencies.Count)
					SpatialFrequencies.RemoveAt(_selectedSpatialFrequencyIndex);
			});
			AddTemporalFrequencyCommand = ReactiveCommand.Create(() => TemporalFrequencies.Add(_newTemporalFrequency));
			RemoveTemporalFrequencyCommand = ReactiveCommand.Create(() =>
			{
				if (_selectedTemporalFrequencyIndex >= 0 && _selectedTemporalFrequencyIndex < TemporalFrequencies.Count)
					TemporalFrequencies.RemoveAt(_selectedTemporalFrequencyIndex);
			});
			AddDirectionCommand = ReactiveCommand.Create(() => Directions.Add(_newDirection));
			RemoveDirectionCommand = ReactiveCommand.Create(() =>
			{
				if (_selectedDirectionIndex >= 0 && _selectedDirectionIndex < Directions.Count)
					Directions.RemoveAt(_selectedDirectionIndex);
			});
		}

		public void LoadFromRecentering(VideoReader videoReader, NDArray gazeLocations, int dataStartFrame, int eyetrackingFPS, int gazeSpaceWidth, int gazeSpaceHeight)
		{
			if (IsVideoPlaying) PlayPause();
			_videoReader = videoReader;
			_gazeLocations = gazeLocations;
			_dataStartFrame = dataStartFrame;
			_eyetrackingFPS = eyetrackingFPS;
			_gazeSpaceWidth = gazeSpaceWidth;
			_gazeSpaceHeight = gazeSpaceHeight;
			VideoWidth = videoReader.width;
			VideoHeight = videoReader.height;
			_videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)videoReader.fps);
			TotalVideoFrames = videoReader.frameCount;
			TotalVideoTime = videoReader.FramesToTimecode(videoReader.frameCount - 1);
			VideoFps = videoReader.fps;
			IsLoadedFromRecentering = true;
			this.RaisePropertyChanged("CanPlayVideo");
			UpdateDisplay();
		}

		public async void LoadVideo()
		{
			Avalonia.Controls.OpenFileDialog openFileDialog = new Avalonia.Controls.OpenFileDialog()
			{
				Title = "Load stimulus video"
			};
			openFileDialog.Filters.Add(new Avalonia.Controls.FileDialogFilter()
			{
				Name = "Videos",
				Extensions = { "avi", "mkv", "mp4", "m4v" }
			});
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);
			if (fileName == null || fileName.Length == 0) return;

			_videoReader = new VideoReader(fileName[0]);
			_videoReader.ReadFrame();
			VideoFrame = _videoReader.GetFrameForDisplay();
			_videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)_videoReader.fps);
			TotalVideoFrames = _videoReader.frameCount;
			TotalVideoTime = _videoReader.FramesToTimecode(_videoReader.frameCount - 1);
			VideoWidth = _videoReader.width;
			VideoHeight = _videoReader.height;
			VideoFps = _videoReader.fps;
			this.RaisePropertyChanged("CanPlayVideo");
		}

		public void PlayPause()
		{
			if (IsVideoPlaying)
				_videoPlaybackTimer.Stop();
			else
				_videoPlaybackTimer.Start();
			IsVideoPlaying = !IsVideoPlaying;
		}

		public void ChangeFrame(int delta)
		{
			if (_videoReader != null)
				ShowFrame(_videoReader.CurrentFrameNumber + delta);
		}

		public void VideoTimerTick(object? sender, EventArgs e)
		{
			if (_videoReader.CurrentFrameNumber >= _videoReader.frameCount - 1)
				PlayPause();
			_videoReader.ReadFrame();
			UpdateDisplay();
		}

		public void ShowFrame(int frame)
		{
			_videoReader.CurrentFrameNumber = frame;
			UpdateDisplay();
		}

		private int VideoTimeToDataIndex(int videoFrame)
		{
			int videoFramesElapsed = videoFrame - _dataStartFrame.Value;
			if (videoFramesElapsed < 0) return 0;
			double videoElapsedTime = (double)videoFramesElapsed / _videoReader.fps;
			return (int)(videoElapsedTime * _eyetrackingFPS);
		}

		public void UpdateDisplay()
		{
			VideoFrame = _videoReader.GetFrameForDisplay();
			CurrentVideoFrame = _videoReader.CurrentFrameNumber;
			CurrentVideoTime = _videoReader.FramesToTimecode(_videoReader.CurrentFrameNumber);
			if ((object)_gazeLocations != null && _dataStartFrame != null)
			{
				int dataIndex = Math.Clamp(VideoTimeToDataIndex(CurrentVideoFrame), 0, _gazeLocations.Shape[0] - 1);
				double gazeXValue = (double)_gazeLocations[dataIndex, 0];
				double gazeYValue = (double)_gazeLocations[dataIndex, 1];
				if (!Double.IsNaN(gazeXValue) && !Double.IsNaN(gazeYValue))
				{
					GazeX = gazeXValue;
					GazeY = gazeYValue;
				}
			}
		}
	}
}
