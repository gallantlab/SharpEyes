using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Num = NumSharp.np;
using NumSharp;
using ReactiveUI;
using SharpEyes.Models;
using Eyetracking;

namespace SharpEyes.ViewModels
{
	public class PyramidCircleOverlay
	{
		public double Left { get; set; }
		public double Top { get; set; }
		public double Diameter { get; set; }
		public IBrush Stroke { get; set; }
		public double StrokeThickness { get; set; }
	}

	public class PyramidArrowOverlay
	{
		public double CanvasLeft { get; set; }
		public double CanvasTop { get; set; }
		public Geometry Geometry { get; set; }
		public IBrush Stroke { get; set; }
		public double StrokeThickness { get; set; }
	}

	public class MotionEnergyViewModel : ViewModelBase
	{
		public ReactiveCommand<Unit, Unit> LoadVideoCommand { get; }
		public ReactiveCommand<Unit, Unit>? PlayPauseCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? PreviousFrameCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? NextFrameCommand { get; } = null;
		public ReactiveCommand<Unit, Unit> RestoreVideoDefaultsCommand { get; }
		public ReactiveCommand<Unit, Unit> RestoreFilterDefaultsCommand { get; }

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

		private readonly MotionEnergyFeatures motionEnergyFeatures = new MotionEnergyFeatures();
		private Settings settings = Settings.Load();

		private VideoReader? _videoReader = null;
		private Func<int, string> _timeFormatter;
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
				SyncFrameSizeToModel();
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
				SyncFrameSizeToModel();
			}
		}

		// == Gaze / recentering (populated via LoadFromRecentering) ==

		private NDArray? _gazeLocations = null;
		private string? _gazeFileName = null;
		private GazeFilterSettings? _gazeFilterSettings = null;
		private int? _dataStartFrame = null;
		private int _eyetrackingFPS = 60;
		private int _gazeSpaceWidth = 1024;
		private int _gazeSpaceHeight = 768;
		private NDArray? _motionEnergyFeatures = null;

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
				SaveSettings();
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
				SaveSettings();
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
				SyncFrameSizeToModel();
				SaveSettings();
			}
		}

		public int ModelFrameWidth => motionEnergyFeatures.FrameWidth;
		public int ModelFrameHeight => motionEnergyFeatures.FrameHeight;

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

		public ObservableCollection<double> SpatialFrequencies { get; } = new ObservableCollection<double>();
		public ObservableCollection<double> TemporalFrequencies { get; } = new ObservableCollection<double>();
		public ObservableCollection<double> Directions { get; } = new ObservableCollection<double>();

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

		public List<int> SelectedSpatialFrequencyIndices { get; set; } = new List<int>();
		public List<int> SelectedTemporalFrequencyIndices { get; set; } = new List<int>();
		public List<int> SelectedDirectionIndices { get; set; } = new List<int>();

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

		private bool _isSpatialFrequenciesExpanded = false;
		public bool IsSpatialFrequenciesExpanded
		{
			get => _isSpatialFrequenciesExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isSpatialFrequenciesExpanded, value);
				this.RaisePropertyChanged("SpatialFrequenciesHeaderText");
			}
		}

		private bool _isTemporalFrequenciesExpanded = false;
		public bool IsTemporalFrequenciesExpanded
		{
			get => _isTemporalFrequenciesExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isTemporalFrequenciesExpanded, value);
				this.RaisePropertyChanged("TemporalFrequenciesHeaderText");
			}
		}

		private bool _isDirectionsExpanded = false;
		public bool IsDirectionsExpanded
		{
			get => _isDirectionsExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isDirectionsExpanded, value);
				this.RaisePropertyChanged("DirectionsHeaderText");
			}
		}

		public string SpatialFrequenciesHeaderText => _isSpatialFrequenciesExpanded
			? "Spatial frequencies"
			: String.Format("Spatial frequencies: {0}", String.Join(", ", SpatialFrequencies));
		public string TemporalFrequenciesHeaderText => _isTemporalFrequenciesExpanded
			? "Temporal frequencies"
			: String.Format("Temporal frequencies: {0}", String.Join(", ", TemporalFrequencies));
		public string DirectionsHeaderText => _isDirectionsExpanded
			? "Directions"
			: String.Format("Directions: {0}", String.Join(", ", Directions));

		private bool _showMotionEnergyPyramid = false;
		public bool ShowMotionEnergyPyramid
		{
			get => _showMotionEnergyPyramid;
			set
			{
				this.RaiseAndSetIfChanged(ref _showMotionEnergyPyramid, value);
				UpdatePyramidOverlay();
			}
		}

		public ObservableCollection<PyramidCircleOverlay> PyramidCircles { get; } = new ObservableCollection<PyramidCircleOverlay>();
		public ObservableCollection<PyramidArrowOverlay> PyramidArrows { get; } = new ObservableCollection<PyramidArrowOverlay>();

		public ReactiveCommand<Unit, Unit> ComputePyramidCommand { get; }

		// == Feature computation ==

		public ObservableCollection<PymotenBackend> AvailableBackends { get; } = new ObservableCollection<PymotenBackend>();

		private int _selectedBackendIndex = 0;
		public int SelectedBackendIndex
		{
			get => _selectedBackendIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedBackendIndex, value);
		}

		public string SelectedBackendKey =>
			_selectedBackendIndex >= 0 && _selectedBackendIndex < AvailableBackends.Count
				? AvailableBackends[_selectedBackendIndex].Key
				: "numpy";

		private int _startFrame = 0;
		public int StartFrame
		{
			get => _startFrame;
			set => this.RaiseAndSetIfChanged(ref _startFrame, value);
		}

		public ReactiveCommand<Unit, Unit> ComputeFeaturesCommand { get; }

		private CancellationTokenSource? _computeFeaturesTokenSource = null;

		private bool _isComputingFeatures = false;
		public bool IsComputingFeatures
		{
			get => _isComputingFeatures;
			set
			{
				this.RaiseAndSetIfChanged(ref _isComputingFeatures, value);
				this.RaisePropertyChanged("ComputeFeaturesButtonText");
			}
		}

		public string ComputeFeaturesButtonText => _isComputingFeatures ? "Cancel" : "Compute features";

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
			_padPercent = settings.MotionEnergyPadPercent;
			_padValue = settings.MotionEnergyPadValue;
			_frameScale = settings.MotionEnergyFrameScale;
			foreach (string backendKey in settings.BackendPreference)
				AvailableBackends.Add(new PymotenBackend(backendKey));
			foreach (double value in settings.MotionEnergySpatialFrequencies)
				SpatialFrequencies.Add(value);
			foreach (double value in settings.MotionEnergyTemporalFrequencies)
				TemporalFrequencies.Add(value);
			foreach (double value in settings.MotionEnergyDirections)
				Directions.Add(value);
			motionEnergyFeatures.SpatialFrequencies = new List<double>(SpatialFrequencies);
			motionEnergyFeatures.TemporalFrequencies = new List<double>(TemporalFrequencies);
			motionEnergyFeatures.Directions = new List<double>(Directions);

			RestoreVideoDefaultsCommand = ReactiveCommand.Create(RestoreVideoDefaults);
			RestoreFilterDefaultsCommand = ReactiveCommand.Create(RestoreFilterDefaults);
			LoadVideoCommand = ReactiveCommand.Create(LoadVideo);
			PlayPauseCommand = ReactiveCommand.Create(PlayPause);
			PreviousFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(-1); });
			NextFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(1); });
			_videoPlaybackTimer = new DispatcherTimer();
			_videoPlaybackTimer.Tick += VideoTimerTick;
			AddSpatialFrequencyCommand = ReactiveCommand.Create(() =>
			{
				SpatialFrequencies.Add(_newSpatialFrequency);
				motionEnergyFeatures.SpatialFrequencies = new List<double>(SpatialFrequencies);
			});
			RemoveSpatialFrequencyCommand = ReactiveCommand.Create(() =>
			{
				if (SelectedSpatialFrequencyIndices.Count == 0) return;
				int minimumIndex = SelectedSpatialFrequencyIndices[0];
				foreach (int index in SelectedSpatialFrequencyIndices)
					if (index < minimumIndex) minimumIndex = index;
				List<int> sortedDescending = new List<int>(SelectedSpatialFrequencyIndices);
				sortedDescending.Sort((a, b) => b.CompareTo(a));
				foreach (int index in sortedDescending)
					SpatialFrequencies.RemoveAt(index);
				motionEnergyFeatures.SpatialFrequencies = new List<double>(SpatialFrequencies);
				if (minimumIndex > 0)
					SelectedSpatialFrequencyIndex = minimumIndex - 1;
				else if (SpatialFrequencies.Count > 0)
					SelectedSpatialFrequencyIndex = 0;
				else
					SelectedSpatialFrequencyIndex = -1;
			});
			AddTemporalFrequencyCommand = ReactiveCommand.Create(() =>
			{
				TemporalFrequencies.Add(_newTemporalFrequency);
				motionEnergyFeatures.TemporalFrequencies = new List<double>(TemporalFrequencies);
			});
			RemoveTemporalFrequencyCommand = ReactiveCommand.Create(() =>
			{
				if (SelectedTemporalFrequencyIndices.Count == 0) return;
				int minimumIndex = SelectedTemporalFrequencyIndices[0];
				foreach (int index in SelectedTemporalFrequencyIndices)
					if (index < minimumIndex) minimumIndex = index;
				List<int> sortedDescending = new List<int>(SelectedTemporalFrequencyIndices);
				sortedDescending.Sort((a, b) => b.CompareTo(a));
				foreach (int index in sortedDescending)
					TemporalFrequencies.RemoveAt(index);
				motionEnergyFeatures.TemporalFrequencies = new List<double>(TemporalFrequencies);
				if (minimumIndex > 0)
					SelectedTemporalFrequencyIndex = minimumIndex - 1;
				else if (TemporalFrequencies.Count > 0)
					SelectedTemporalFrequencyIndex = 0;
				else
					SelectedTemporalFrequencyIndex = -1;
			});
			AddDirectionCommand = ReactiveCommand.Create(() =>
			{
				Directions.Add(_newDirection);
				motionEnergyFeatures.Directions = new List<double>(Directions);
			});
			RemoveDirectionCommand = ReactiveCommand.Create(() =>
			{
				if (SelectedDirectionIndices.Count == 0) return;
				int minimumIndex = SelectedDirectionIndices[0];
				foreach (int index in SelectedDirectionIndices)
					if (index < minimumIndex) minimumIndex = index;
				List<int> sortedDescending = new List<int>(SelectedDirectionIndices);
				sortedDescending.Sort((a, b) => b.CompareTo(a));
				foreach (int index in sortedDescending)
					Directions.RemoveAt(index);
				motionEnergyFeatures.Directions = new List<double>(Directions);
				if (minimumIndex > 0)
					SelectedDirectionIndex = minimumIndex - 1;
				else if (Directions.Count > 0)
					SelectedDirectionIndex = 0;
				else
					SelectedDirectionIndex = -1;
			});
			ComputePyramidCommand = ReactiveCommand.CreateFromTask(() => ComputePyramid());
			ComputeFeaturesCommand = ReactiveCommand.Create(() =>
			{
				if (_isComputingFeatures)
					_computeFeaturesTokenSource?.Cancel();
				else
					_ = ComputeFeatures();
			});
			SpatialFrequencies.CollectionChanged += (s, e) => { this.RaisePropertyChanged("SpatialFrequenciesHeaderText"); SaveSettings(); };
			TemporalFrequencies.CollectionChanged += (s, e) => { this.RaisePropertyChanged("TemporalFrequenciesHeaderText"); SaveSettings(); };
			Directions.CollectionChanged += (s, e) => { this.RaisePropertyChanged("DirectionsHeaderText"); SaveSettings(); };
		}

		private void RestoreVideoDefaults()
		{
			PadPercent = 200;
			PadValue = 0.1;
			FrameScale = 0.125;
		}

		private void RestoreFilterDefaults()
		{
			SpatialFrequencies.Clear();
			foreach (double value in new double[] { 0, 2, 4, 8, 16, 32 })
				SpatialFrequencies.Add(value);
			motionEnergyFeatures.SpatialFrequencies = new List<double>(SpatialFrequencies);
			TemporalFrequencies.Clear();
			foreach (double value in new double[] { 0, 2, 4, 8, 16 })
				TemporalFrequencies.Add(value);
			motionEnergyFeatures.TemporalFrequencies = new List<double>(TemporalFrequencies);
			Directions.Clear();
			foreach (double value in new double[] { 0, 45, 90, 135, 180, 225, 270, 315 })
				Directions.Add(value);
			motionEnergyFeatures.Directions = new List<double>(Directions);
		}

		private void SaveSettings()
		{
			settings.MotionEnergyPadPercent = _padPercent;
			settings.MotionEnergyPadValue = _padValue;
			settings.MotionEnergyFrameScale = _frameScale;
			settings.MotionEnergySpatialFrequencies = new List<double>(SpatialFrequencies);
			settings.MotionEnergyTemporalFrequencies = new List<double>(TemporalFrequencies);
			settings.MotionEnergyDirections = new List<double>(Directions);
			settings.Save();
		}

		private void SyncFrameSizeToModel()
		{
			motionEnergyFeatures.FrameWidth = (int)(_videoWidth * _padPercent / 100 * _frameScale);
			motionEnergyFeatures.FrameHeight = (int)(_videoHeight * _padPercent / 100 * _frameScale);
		}

		public void LoadFromRecentering(VideoReader videoReader, NDArray gazeLocations, string? gazeFileName, GazeFilterSettings? gazeFilterSettings, int dataStartFrame, int eyetrackingFPS, int gazeSpaceWidth, int gazeSpaceHeight)
		{
			if (IsVideoPlaying) PlayPause();
			_videoReader = videoReader;
			_gazeLocations = gazeLocations;
			_gazeFileName = gazeFileName;
			_gazeFilterSettings = gazeFilterSettings;
			_dataStartFrame = dataStartFrame;
			_eyetrackingFPS = eyetrackingFPS;
			_gazeSpaceWidth = gazeSpaceWidth;
			_gazeSpaceHeight = gazeSpaceHeight;
			VideoWidth = videoReader.width;
			VideoHeight = videoReader.height;
			_videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)videoReader.fps);
			TotalVideoFrames = videoReader.frameCount;
			VideoFps = videoReader.fps;
			IsLoadedFromRecentering = true;
			this.RaisePropertyChanged("CanPlayVideo");
			UpdateTimecodeDisplay();
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
			_videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)_videoReader.fps);
			TotalVideoFrames = _videoReader.frameCount;
			VideoWidth = _videoReader.width;
			VideoHeight = _videoReader.height;
			VideoFps = _videoReader.fps;
			_gazeSpaceWidth = _videoReader.width;
			_gazeSpaceHeight = _videoReader.height;
			_dataStartFrame = 0;
			_eyetrackingFPS = (int)_videoReader.fps;
			NDArray centeredGaze = new NDArray(NPTypeCode.Double, Shape.Matrix(_videoReader.frameCount, 2));
			for (int frameIndex = 0; frameIndex < _videoReader.frameCount; frameIndex++)
			{
				centeredGaze[frameIndex, 0] = (double)_videoReader.width / 2.0;
				centeredGaze[frameIndex, 1] = (double)_videoReader.height / 2.0;
			}
			_gazeLocations = centeredGaze;
			IsLoadedFromRecentering = true;
			this.RaisePropertyChanged("CanPlayVideo");
			UpdateTimecodeDisplay();
			UpdateDisplay();
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

		public void ShowFrame()
		{
			ShowFrame(CurrentVideoFrame);
		}

		public void ShowFrame(int frame)
		{
			_videoReader.CurrentFrameNumber = frame;
			UpdateDisplay();
		}

		public async void InitializePython()
		{
			if (PythonEnvironmentManager.Instance.IsInitialized) return;
			Settings pythonSettings = PythonEnvironmentManager.Instance.Settings;
			switch (pythonSettings.PythonSourceMode)
			{
				case PythonSourceMode.Bundled:
					if (!PythonEnvironmentManager.Instance.IsBundledPythonDownloaded())
					{
						StatusText = "Bundled Python is not installed. Download it in Settings.";
						return;
					}
					break;
				case PythonSourceMode.System:
					if (!File.Exists(pythonSettings.SystemPythonExecutablePath))
					{
						StatusText = String.Format("Python executable not found: {0}", pythonSettings.SystemPythonExecutablePath);
						return;
					}
					break;
				case PythonSourceMode.Conda:
					if (!Directory.Exists(pythonSettings.CondaEnvironmentPath))
					{
						StatusText = String.Format("Conda environment not found: {0}", pythonSettings.CondaEnvironmentPath);
						return;
					}
					break;
			}
			StatusText = "Initializing Python...";
			IsProgressBarVisible = true;
			try
			{
				await Task.Run(() => PythonEnvironmentManager.Instance.Initialize());
				StatusText = "Python initialized.";
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Failed to initialize Python: {0}", exception.Message);
			}
			finally
			{
				IsProgressBarVisible = false;
			}
		}

		private async Task UpdatePyramidOverlay()
		{
			PyramidCircles.Clear();
			PyramidArrows.Clear();
			if (!_showMotionEnergyPyramid) return;
			await ComputePyramid(false);
			if (motionEnergyFeatures.FilterCount < 1) return;

			IBrush overlayBrush = new SolidColorBrush(Color.FromArgb(200, 255, 220, 0));

			Dictionary<(double, double, double), HashSet<double>> filterDirectionsByCircle =
				new Dictionary<(double, double, double), HashSet<double>>();

			foreach (MotionEnergyFilterParameters filter in motionEnergyFeatures.FilterParameters)
			{
				(double, double, double) key = (filter.CenterHorizontal, filter.CenterVertical, filter.SpatialEnvelope);
				if (!filterDirectionsByCircle.ContainsKey(key))
					filterDirectionsByCircle[key] = new HashSet<double>();
				filterDirectionsByCircle[key].Add(filter.Direction);
			}

			List<double> uniqueSpatialEnvelopes = new List<double>();
			foreach (MotionEnergyFilterParameters filter in motionEnergyFeatures.FilterParameters)
			{
				if (!uniqueSpatialEnvelopes.Contains(filter.SpatialEnvelope))
					uniqueSpatialEnvelopes.Add(filter.SpatialEnvelope);
			}
			uniqueSpatialEnvelopes.Sort();

			Dictionary<double, double> strokeThicknessBySize = new Dictionary<double, double>();
			for (int index = 0; index < uniqueSpatialEnvelopes.Count; index++)
				strokeThicknessBySize[uniqueSpatialEnvelopes[index]] = index + 1;

			int frameHeight = RecenteringCanvasHeight;
			foreach (KeyValuePair<(double, double, double), HashSet<double>> circleEntry in filterDirectionsByCircle)
			{
				double centerX = circleEntry.Key.Item1 * frameHeight;
				double centerY = circleEntry.Key.Item2 * frameHeight;
				double radius  = circleEntry.Key.Item3 * frameHeight;
				double strokeThickness = strokeThicknessBySize[circleEntry.Key.Item3] * 2;

				PyramidCircles.Add(new PyramidCircleOverlay
				{
					Left           = centerX - radius,
					Top            = centerY - radius,
					Diameter       = 2 * radius,
					Stroke         = overlayBrush,
					StrokeThickness = strokeThickness
				});

				foreach (double direction in circleEntry.Value)
				{
					double directionRadians = direction * Math.PI / 180.0;
					double dx = Math.Cos(directionRadians);
					double dy = -Math.Sin(directionRadians);

					Point arrowStart = new Point(centerX + radius * dx,        centerY + radius * dy);
					Point arrowTip   = new Point(centerX + 1.25 * radius * dx, centerY + 1.25 * radius * dy);

					double canvasLeft = Math.Min(arrowStart.X, arrowTip.X);
					double canvasTop  = Math.Min(arrowStart.Y, arrowTip.Y);

					StreamGeometry arrowGeometry = new StreamGeometry();
					using (StreamGeometryContext streamContext = arrowGeometry.Open())
					{
						streamContext.BeginFigure(new Point(arrowStart.X - canvasLeft, arrowStart.Y - canvasTop), false);
						streamContext.LineTo(new Point(arrowTip.X - canvasLeft, arrowTip.Y - canvasTop));
						streamContext.EndFigure(false);
					}

					PyramidArrows.Add(new PyramidArrowOverlay
					{
						CanvasLeft      = canvasLeft,
						CanvasTop       = canvasTop,
						Geometry        = arrowGeometry,
						Stroke          = overlayBrush,
						StrokeThickness = strokeThickness
					});
				}
			}
		}

		private async Task ComputePyramid(bool updateDisplay = true)
		{
			StatusText = "Building pyramid...";
			IsProgressBarVisible = true;
			ProgressBarValue = 0;
			try
			{
				double fps = _videoFps;
				await Task.Run(() =>
				{
					PythonEnvironmentManager.Instance.Initialize();
					motionEnergyFeatures.BuildPyramid(fps);
				});
				StatusText = String.Format("Pyramid built: {0} filters",
					motionEnergyFeatures.FilterCount);
				this.RaisePropertyChanged("ModelFrameWidth");
				this.RaisePropertyChanged("ModelFrameHeight");
				if (updateDisplay) UpdatePyramidOverlay();
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Error building pyramid: {0}", exception.Message);
			}
			finally
			{
				IsProgressBarVisible = false;
			}
		}

		private async Task ComputeFeatures()
		{
			if (_videoReader == null || (object)_gazeLocations == null || _dataStartFrame == null) return;

			CancellationTokenSource tokenSource = new CancellationTokenSource();
			_computeFeaturesTokenSource = tokenSource;
			CancellationToken cancellationToken = tokenSource.Token;
			IsComputingFeatures = true;
			IsProgressBarVisible = true;

			try
			{
				// Phase 1: build recentered frames (determinate progress)
				StatusText = "Processing frames...";
				IsProgressBarIndeterminate = false;
				ProgressBarValue = 0;
				Recenterer recenterer = new Recenterer(
					_videoReader, _gazeLocations, _dataStartFrame.Value,
					false, _eyetrackingFPS, _gazeSpaceWidth, _gazeSpaceHeight,
					_frameScale, _padValue, false);
				IProgress<double> frameProgress = new Progress<double>(value => ProgressBarValue = value);
				NDArray frames;
				try
				{
					frames = await recenterer.BuildRecenteredFramesAsync(_startFrame, frameProgress, cancellationToken);
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception exception)
				{
					StatusText = String.Format("Error processing frames: {0}", exception.Message);
					return;
				}

				cancellationToken.ThrowIfCancellationRequested();

				// Phase 2: build pyramid (always, regardless of prior state)
				StatusText = "Building pyramid...";
				try
				{
					await Task.Run(() =>
					{
						PythonEnvironmentManager.Instance.Initialize();
						motionEnergyFeatures.BuildPyramid(_videoFps);
					}, cancellationToken);
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception exception)
				{
					StatusText = String.Format("Error building pyramid: {0}", exception.Message);
					return;
				}
				this.RaisePropertyChanged("ModelFrameWidth");
				this.RaisePropertyChanged("ModelFrameHeight");
				UpdatePyramidOverlay();

				cancellationToken.ThrowIfCancellationRequested();

				// Phase 3: extract features (indeterminate progress)
				StatusText = "Computing motion energy...";
				IsProgressBarIndeterminate = true;
				NDArray features;
				try
				{
					IProgress<double> extractProgress = new Progress<double>(_ => { });
					features = await motionEnergyFeatures.ExtractAsync(frames, extractProgress);
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception exception)
				{
					StatusText = String.Format("Error computing motion energy: {0}", exception.Message);
					return;
				}

				_motionEnergyFeatures = features;
				IsProgressBarIndeterminate = false;
				IsProgressBarVisible = false;

				// Save to disk
				SaveFileDialog saveDialog = new SaveFileDialog() { Title = "Save motion energy features" };
				saveDialog.Filters.Add(new FileDialogFilter() { Name = "NumPy arrays", Extensions = { "npy" } });
				saveDialog.InitialFileName = System.IO.Path.GetFileNameWithoutExtension(_videoReader.videoFileName) + " motion energy.npy";
				string? savePath = await saveDialog.ShowAsync(MainWindow);
				if (savePath != null)
				{
					await Task.Run(() => Num.save(savePath, features));

					string saveDirectory = System.IO.Path.GetDirectoryName(savePath);
					string saveBaseName = System.IO.Path.GetFileNameWithoutExtension(savePath);

					System.Text.StringBuilder textContent = new System.Text.StringBuilder();
					textContent.AppendLine(String.Format("Stimulus video: {0}", _videoReader.videoFileName));
					textContent.AppendLine(String.Format("Stimulus video frame rate: {0} FPS", _videoReader.fps));
					textContent.AppendLine(String.Format("Stimulus video frame size: {0} x {1}", _videoReader.width, _videoReader.height));
					textContent.AppendLine(String.Format("Stimulus video duration: {0} ({1} frames)", _videoReader.FramesToTimecode(_videoReader.frameCount), _videoReader.frameCount));
					textContent.AppendLine();
					textContent.AppendLine(String.Format("Gaze file: {0}", _gazeFileName ?? "none"));
					textContent.AppendLine();
					textContent.AppendLine("Gaze info:");
					textContent.AppendLine(String.Format("  Eyetracking FPS: {0}", _eyetrackingFPS));
					textContent.AppendLine(String.Format("  Data start frame: {0}", _dataStartFrame.Value));
					textContent.AppendLine(String.Format("  Gaze space width: {0}", _gazeSpaceWidth));
					textContent.AppendLine(String.Format("  Gaze space height: {0}", _gazeSpaceHeight));
					textContent.AppendLine();
					textContent.AppendLine("Gaze filtering:");
					if (_gazeFilterSettings == null || !_gazeFilterSettings.IsEnabled)
					{
						textContent.AppendLine("  None");
					}
					else
					{
						textContent.AppendLine(String.Format("  Median filter window size: {0}", _gazeFilterSettings.MedianFilterWindowSize));
						textContent.AppendLine(String.Format("  Filter pupil size: {0}", _gazeFilterSettings.FilterPupilSize));
						textContent.AppendLine(String.Format("  Outlier removal enabled: {0}", _gazeFilterSettings.EnableOutlierRemoval));
						if (_gazeFilterSettings.EnableOutlierRemoval)
						{
							textContent.AppendLine(String.Format("  Outlier threshold X: {0}", _gazeFilterSettings.OutlierThresholdX));
							textContent.AppendLine(String.Format("  Outlier threshold Y: {0}", _gazeFilterSettings.OutlierThresholdY));
							textContent.AppendLine(String.Format("  Outlier threshold radius: {0}", _gazeFilterSettings.OutlierThresholdRadius));
						}
					}
					textContent.AppendLine();
					textContent.AppendLine("Motion-energy parameters:");
					textContent.AppendLine(String.Format("  Pad percent: {0}", _padPercent));
					textContent.AppendLine(String.Format("  Pad value: {0}", _padValue));
					textContent.AppendLine(String.Format("  Frame scale: {0}", _frameScale));
					textContent.AppendLine(String.Format("  Output frame size: {0} x {1}",
						(int)(_videoReader.width * _padPercent / 100.0 * _frameScale),
						(int)(_videoReader.height * _padPercent / 100.0 * _frameScale)));
					textContent.AppendLine(String.Format("  Video frame in output size: {0} x {1}",
						(int)(_videoReader.width * _frameScale),
						(int)(_videoReader.height * _frameScale)));
					textContent.AppendLine(String.Format("  Video FPS: {0}", _videoFps));
					textContent.AppendLine(String.Format("  Spatial frequencies: {0}", String.Join(", ", SpatialFrequencies)));
					textContent.AppendLine(String.Format("  Temporal frequencies: {0}", String.Join(", ", TemporalFrequencies)));
					textContent.AppendLine(String.Format("  Directions: {0}", String.Join(", ", Directions)));
					textContent.AppendLine(String.Format("  Start frame: {0}", _startFrame));

					System.Text.StringBuilder csvContent = new System.Text.StringBuilder();
					csvContent.AppendLine("Y center,X center,Direction (degrees),Spatial Frequency,Spatial Envelope,Temporal Frequency,Temporal Envelope,Temporal width,Spatial phase offset");
					foreach (MotionEnergyFilterParameters filter in motionEnergyFeatures.FilterParameters)
						csvContent.AppendLine(String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
							filter.CenterVertical * motionEnergyFeatures.FrameHeight, 
							filter.CenterHorizontal * motionEnergyFeatures.FrameHeight, filter.Direction,
							filter.SpatialFrequency, filter.SpatialEnvelope * motionEnergyFeatures.FrameHeight,
							filter.TemporalFrequency, filter.TemporalEnvelope,
							filter.FilterTemporalWidth, filter.SpatialPhaseOffset));

					string textFilePath = System.IO.Path.Combine(saveDirectory, saveBaseName + " info.txt");
					string csvFilePath = System.IO.Path.Combine(saveDirectory, saveBaseName + " motion-energy filters.csv");
					await Task.Run(() =>
					{
						File.WriteAllText(textFilePath, textContent.ToString());
						File.WriteAllText(csvFilePath, csvContent.ToString());
					});
				}

				StatusText = String.Format("Motion energy computed: {0} frames x {1} features",
					features.Shape[0], features.Shape[1]);
			}
			catch (OperationCanceledException)
			{
				StatusText = "Cancelled.";
			}
			finally
			{
				tokenSource.Dispose();
				_computeFeaturesTokenSource = null;
				IsComputingFeatures = false;
				IsProgressBarVisible = false;
				IsProgressBarIndeterminate = false;
			}
		}

		private int VideoTimeToDataIndex(int videoFrame)
		{
			int videoFramesElapsed = videoFrame - _dataStartFrame.Value;
			if (videoFramesElapsed < 0) return 0;
			double videoElapsedTime = (double)videoFramesElapsed / _videoReader.fps;
			return (int)(videoElapsedTime * _eyetrackingFPS);
		}

		public void UpdateTimecodeDisplay()
		{
			if (_videoReader == null)
				return;
			if (Settings.Current.ShowFrameNumber)
				_timeFormatter = frame => String.Format("Frame {0}", frame);
			else
				_timeFormatter = _videoReader.FramesToTimecode;
			CurrentVideoTime = _timeFormatter(_videoReader.CurrentFrameNumber);
			TotalVideoTime = _timeFormatter(_videoReader.frameCount - 1);
		}

		public void UpdateDisplay()
		{
			VideoFrame = _videoReader.GetFrameForDisplay();
			CurrentVideoFrame = _videoReader.CurrentFrameNumber;
			CurrentVideoTime = _timeFormatter(_videoReader.CurrentFrameNumber);
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
