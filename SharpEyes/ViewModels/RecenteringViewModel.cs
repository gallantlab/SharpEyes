using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
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
	public enum ExportFormat { PngFrames, NumpyArray }
	public enum ExportContent { RecenterredOnly, RecenterredAndRaw }

	public class RecenteringViewModel : ViewModelBase
	{
		// == Commands ==
		public ReactiveCommand<Unit, Unit> LoadVideoCommand { get; set; }
		public ReactiveCommand<Unit, Unit>? PlayPauseCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit>? PreviousFrameCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit>? NextFrameCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit> LoadGazeCommand { get; set; }
		public ReactiveCommand<Unit, Unit> ExportCommand { get; set; }
		public ReactiveCommand<Unit, Unit> JumpToFirstTTLCommand { get; set; }
		public ReactiveCommand<Unit, Unit> SendToMotionEnergyCommand { get; set; }

		// Set by MainWindowViewModel
		public MotionEnergyViewModel? MotionEnergyViewModel { get; set; }
		public Action? SwitchToMotionEnergyTab { get; set; }

		// == window reference for showing dialogs
		public Avalonia.Controls.Window? MainWindow =>
			Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
				? desktop.MainWindow
				: null;

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
		private bool _isLoadingGaze = false;
		public bool IsLoadingGaze
		{
			get => _isLoadingGaze;
			set => this.RaiseAndSetIfChanged(ref _isLoadingGaze, value);
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
		private Func<int, string> _timeFormatter;

		private int _videoWidth = 1024;
		public int VideoWidth
		{
			get => _videoWidth;
			set
			{
				this.RaiseAndSetIfChanged(ref _videoWidth, value);
				this.RaisePropertyChanged("CanvasWidth");
				this.RaisePropertyChanged("ImageLeft");
			}
		}

		private int _videoHeight = 768;
		public int VideoHeight
		{
			get => _videoHeight;
			set
			{
				this.RaiseAndSetIfChanged(ref _videoHeight, value);
				this.RaisePropertyChanged("CanvasHeight");
				this.RaisePropertyChanged("ImageTop");
			}
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
		private NDArray? gazeLocations = null;
		private string? gazeFileName = null;
		private GazeFilterSettings? gazeFilterSettings = null;
		private int? dataStartFrame = null;
		public int DataStartFrame
		{
			get => dataStartFrame ?? 0;
			set
			{
				dataStartFrame = value;
				this.RaisePropertyChanged("DataStartFrame");
				if (videoReader != null)
					UpdateDisplay();
			}
		}

		private int? dataFrame
		{
			get
			{
				if (dataStartFrame == null) return null;
				return VideoTimeToDataIndex(CurrentVideoFrame);
			}
		}

		private bool _isGazeLoaded = false;
		public bool IsGazeLoaded
		{
			get => _isGazeLoaded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isGazeLoaded, value);
				this.RaisePropertyChanged("IsGazeEllipseVisible");
				this.RaisePropertyChanged("ExportEnabled");
				this.RaisePropertyChanged("HasTTLData");
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

		private bool _isTTL = false;
		public bool IsTTL
		{
			get => _isTTL;
			set => this.RaiseAndSetIfChanged(ref _isTTL, value);
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
				this.RaisePropertyChanged("ImageLeft");
			}
		}
		public double GazeY
		{
			get => _gazeY;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeY, value);
				this.RaisePropertyChanged("GazeCircleTop");
				this.RaisePropertyChanged("ImageTop");
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
				_gazeDiameter = value;
				this.RaisePropertyChanged("GazeDiameter");
				this.RaisePropertyChanged("GazeRadius");
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

		private bool _isPlaybackEnabled = true;
		public bool IsPlaybackEnabled
		{
			get => _isPlaybackEnabled;
			set => this.RaiseAndSetIfChanged(ref _isPlaybackEnabled, value);
		}

		public bool CanPlayVideo => videoReader != null;
		public bool ExportEnabled => IsGazeLoaded && videoReader != null;

		public bool HasTTLData => IsGazeLoaded && Recenterer.FindFirstTTLGazeIndex(gazeLocations) != null;

		private bool _startFromFirstTTL = true;
		public bool StartFromFirstTTL
		{
			get => _startFromFirstTTL;
			set => this.RaiseAndSetIfChanged(ref _startFromFirstTTL, value);
		}

		// == Gaze coordinate space (the space the gaze data is expressed in) ==
		private int _gazeSpaceWidth = 1024;
		public int GazeSpaceWidth
		{
			get => _gazeSpaceWidth;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeSpaceWidth, value);
				this.RaisePropertyChanged("ImageLeft");
			}
		}

		private int _gazeSpaceHeight = 768;
		public int GazeSpaceHeight
		{
			get => _gazeSpaceHeight;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeSpaceHeight, value);
				this.RaisePropertyChanged("ImageTop");
			}
		}

		// == Recentered canvas layout ==
		public int RecenteringCanvasWidth => VideoWidth * 2;
		public int RecenteringCanvasHeight => VideoHeight * 2;

		// Position of the inner VideoCanvas on the doubled outer canvas so gaze lands at the center
		public double RecenteringImageLeft => VideoWidth  - GazeX * (double)VideoWidth  / GazeSpaceWidth;
		public double RecenteringImageTop  => VideoHeight - GazeY * (double)VideoHeight / GazeSpaceHeight;

		// == Export parameters ==
		private double _frameScale = 0.125;
		public double FrameScale
		{
			get => _frameScale;
			set => this.RaiseAndSetIfChanged(ref _frameScale, value);
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
				return new SolidColorBrush(new Avalonia.Media.Color(255, grayValue, grayValue, grayValue));
			}
		}

		private bool _flipColors = false;
		public bool FlipColors
		{
			get => _flipColors;
			set => this.RaiseAndSetIfChanged(ref _flipColors, value);
		}

		private int _selectedExportFormatIndex = 0;
		public int SelectedExportFormatIndex
		{
			get => _selectedExportFormatIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedExportFormatIndex, value);
		}

		private int _selectedExportContentIndex = 0;
		public int SelectedExportContentIndex
		{
			get => _selectedExportContentIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedExportContentIndex, value);
		}

		private bool _isGazeInfoExpanded = Settings.Current.RecenteringGazeInfoExpanded;
		public bool IsGazeInfoExpanded
		{
			get => _isGazeInfoExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isGazeInfoExpanded, value);
				Settings.Current.RecenteringGazeInfoExpanded = value;
				Settings.Current.Save();
			}
		}

		private bool _isExportExpanded = Settings.Current.RecenteringExportExpanded;
		public bool IsExportExpanded
		{
			get => _isExportExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isExportExpanded, value);
				Settings.Current.RecenteringExportExpanded = value;
				Settings.Current.Save();
			}
		}

		public RecenteringViewModel()
		{
			LoadVideoCommand = ReactiveCommand.Create(LoadVideo);
			PlayPauseCommand = ReactiveCommand.Create(PlayPause);
			LoadGazeCommand = ReactiveCommand.CreateFromTask(LoadGaze);
			ExportCommand = ReactiveCommand.CreateFromTask(Export);
			JumpToFirstTTLCommand = ReactiveCommand.Create(JumpToFirstTTL);
			SendToMotionEnergyCommand = ReactiveCommand.Create(SendToMotionEnergy);
			videoPlaybackTimer = new DispatcherTimer();
			videoPlaybackTimer.Tick += this.VideoTimerTick;

			PreviousFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(-1); });
			NextFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(1); });
			TrailPoints = new ObservableCollection<TrailGazePoint>(
				Enumerable.Range(0, 10).Select(_ => new TrailGazePoint()));
		}

		public void JumpToFirstTTL()
		{
			int? firstTTLGazeIndex = Recenterer.FindFirstTTLGazeIndex(gazeLocations);
			if (firstTTLGazeIndex == null || videoReader == null || dataStartFrame == null) return;
			double gazeElapsedTime = (double)firstTTLGazeIndex.Value / EyetrackingFPS;
			int videoFrame = dataStartFrame.Value + (int)(gazeElapsedTime * videoReader.fps);
			videoFrame = Math.Clamp(videoFrame, 0, videoReader.frameCount - 1);
			ShowFrame(videoFrame);
		}

		public async void LoadVideo()
		{
			dataStartFrame = null;
			IsGazeLoaded = false;
			gazeLocations = null;
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

			SetupVideo(fileName[0]);
		}

		private void SetupVideo(string filePath)
		{
			videoReader = new VideoReader(filePath);
			videoReader.ReadFrame();
			VideoFrame = videoReader.GetFrameForDisplay();
			videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)videoReader.fps);
			TotalVideoFrames = videoReader.frameCount;
			UpdateTimecodeDisplay();
			VideoWidth = videoReader.width;
			VideoHeight = videoReader.height;
			this.RaisePropertyChanged("CanPlayVideo");
			this.RaisePropertyChanged("ExportEnabled");
		}

		/// <summary>
		/// Loads video and gaze directly from existing data, without file dialogs.
		/// Called from the Stimulus and Gaze tab's "Send to Recentering" button.
		/// </summary>
		public void LoadFromStimulusGaze(string videoFilePath, NDArray gazeData, int gazeDataStartFrame, int eyetrackingFPS, GazeFilterSettings? filterSettings = null)
		{
			if (IsVideoPlaying)
				PlayPause();

			SetupVideo(videoFilePath);
			this.gazeLocations = gazeData.Clone() as NDArray;
			this.dataStartFrame = gazeDataStartFrame;
			this.RaisePropertyChanged("DataStartFrame");
			EyetrackingFPS = eyetrackingFPS;
			gazeFilterSettings = filterSettings;
			IsGazeLoaded = true;
			UpdateDisplay();
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
		private int VideoTimeToDataIndex(int videoFrame)
		{
			int videoFramesElapsed = videoFrame - dataStartFrame.Value;
			if (videoFramesElapsed < 0)
				return 0;
			double videoElapsedTime = (double)videoFramesElapsed / videoReader.fps;
			return (int)(videoElapsedTime * EyetrackingFPS);
		}

		private bool CheckTTLInVideoFrame(int videoFrame)
		{
			if ((object)gazeLocations == null || gazeLocations.Shape[1] < 4) return false;
			int startIndex = VideoTimeToDataIndex(videoFrame);
			int endIndex = VideoTimeToDataIndex(videoFrame + 1);
			for (int i = startIndex; i < endIndex && i < gazeLocations.Shape[0]; i++)
				if ((double)gazeLocations[i, 3] != 0.0)
					return true;
			return false;
		}

		public void UpdateTimecodeDisplay()
		{
			if (videoReader == null)
				return;
			if (Settings.Current.ShowFrameNumber)
				_timeFormatter = frame => String.Format("Frame {0}", frame);
			else
				_timeFormatter = videoReader.FramesToTimecode;
			CurrentVideoTime = _timeFormatter(videoReader.CurrentFrameNumber);
			TotalVideoTime = _timeFormatter(videoReader.frameCount - 1);
		}

		public void UpdateDisplay()
		{
			VideoFrame = videoReader.GetFrameForDisplay();
			CurrentVideoFrame = videoReader.CurrentFrameNumber;
			CurrentVideoTime = _timeFormatter(videoReader.CurrentFrameNumber);
			if (dataStartFrame != null)
			{
				double gazeXValue = (double)gazeLocations[dataFrame, 0];
				double gazeYValue = (double)gazeLocations[dataFrame, 1];
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
				IsTTL = CheckTTLInVideoFrame(CurrentVideoFrame);
				UpdateTrailPoints();
			}
			else
			{
				IsGazeAtNaN = false;
				IsTTL = false;
				foreach (TrailGazePoint point in TrailPoints)
					point.IsVisible = false;
			}
		}

		private void UpdateTrailPoints()
		{
			const int trailCircleCount = 10;
			double stepSeconds = TrailLength / trailCircleCount;
			int stepVideoFrames = Math.Max(1, (int)(stepSeconds * videoReader.fps));
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
				if (trailDataIndex >= gazeLocations.Shape[0])
				{
					TrailPoints[i].IsVisible = false;
					continue;
				}
				double trailX = (double)gazeLocations[trailDataIndex, 0];
				double trailY = (double)gazeLocations[trailDataIndex, 1];
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

		public async Task LoadGaze()
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
			if (EyelinkParser.IsEDFSupported)
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
				Extensions = { "txt", "asc" }
			});
			if (EyelinkParser.IsEDFSupported)
				openFileDialog.Filters.Add(new FileDialogFilter()
				{
					Name = "Eyelink EDF file",
					Extensions = { "edf" }
				});
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;

			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = true;
			IsLoadingGaze = true;
			StatusText = "Loading gaze...";

			string extension = System.IO.Path.GetExtension(fileName[0]);
			NDArray? loadedGazeLocations = null;
			int parsedSampleRate = 0;

			try
			{
				await Task.Run(() =>
				{
					if (extension == ".npy")
						loadedGazeLocations = Num.load(fileName[0]);
					else if (extension == ".txt")
					{
						loadedGazeLocations = EyelinkParser.ParseTextFile(fileName[0], out parsedSampleRate);
					}
					else if (extension == ".edf")
					{
						loadedGazeLocations = EyelinkParser.ParseEDFFile(fileName[0], out parsedSampleRate);
					}
					else // parse a csv file
					{
						using StreamReader csvFile = new StreamReader(fileName[0]);
						string line = csvFile.ReadLine();
						List<double[]> values = new List<double[]>();
						bool isFirstLine = true;
						while (line != null)
						{
							try
							{
								string[] tokens = line.Split(',');
								double x = Double.Parse(tokens[0]);
								double y = Double.Parse(tokens[1]);
								values.Add(new double[]{x, y});
								isFirstLine = false;
								line = csvFile.ReadLine();
							}
							catch (Exception e)
							{	// so if the first line is a header, we throw it away,
								// but if there's a parsing error anywhere else we raise it
								if (!isFirstLine)
									throw;
							}
						}

						loadedGazeLocations = new NDArray(NPTypeCode.Double, Shape.Matrix(values.Count, 2));
						for (int i = 0; i < values.Count; i++)
						{
							loadedGazeLocations[i, 0] = values[i][0];
							loadedGazeLocations[i, 1] = values[i][1];
						}
					}
				});
			}
			catch (InvalidDataException)
			{
				IsProgressBarVisible = false;
				IsProgressBarIndeterminate = false;
				IsLoadingGaze = false;
				StatusText = "Gaze file is malformed.";
				return;
			}

			gazeLocations = loadedGazeLocations;
			gazeFileName = fileName[0];
			if (parsedSampleRate > 0)
				EyetrackingFPS = parsedSampleRate;

			IsProgressBarVisible = false;
			IsProgressBarIndeterminate = false;
			IsLoadingGaze = false;
			StatusText = "Idle";
			IsGazeLoaded = true;
			if (videoReader != null)
			{
				dataStartFrame = videoReader.CurrentFrameNumber;
				this.RaisePropertyChanged("DataStartFrame");
			}
		}

		// == Send to motion-energy ==

		private void SendToMotionEnergy()
		{
			if (!ExportEnabled || MotionEnergyViewModel == null) return;
			MotionEnergyViewModel.LoadFromRecentering(
				videoReader,
				gazeLocations,
				gazeFileName,
				gazeFilterSettings,
				dataStartFrame.Value,
				EyetrackingFPS,
				GazeSpaceWidth,
				GazeSpaceHeight);
			SwitchToMotionEnergyTab?.Invoke();
		}

		// == Export ==

		private async Task Export()
		{
			if (!ExportEnabled) return;

			bool isPng = SelectedExportFormatIndex == 0;
			bool includeRaw = SelectedExportContentIndex == 1;
			string? destinationPath = null;

			if (isPng)
			{
				OpenFolderDialog folderDialog = new OpenFolderDialog()
				{
					Title = "Select output folder"
				};
				destinationPath = await folderDialog.ShowAsync(MainWindow);
			}
			else
			{
				SaveFileDialog saveDialog = new SaveFileDialog()
				{
					Title = "Save numpy array",
					InitialFileName = "recentered frames.npy"
				};
				saveDialog.Filters.Add(new FileDialogFilter()
				{
					Name = "Numpy file",
					Extensions = { "npy" }
				});
				destinationPath = await saveDialog.ShowAsync(MainWindow);
			}

			if (destinationPath == null || destinationPath.Length == 0)
				return;

			IsPlaybackEnabled = false;
			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = false;
			ProgressBarValue = 0;
			StatusText = "Exporting...";

			IProgress<double> progressReporter = new Progress<double>(value => ProgressBarValue = value);
			Recenterer exporter = new Recenterer(
				videoReader,
				gazeLocations,
				dataStartFrame.Value,
				StartFromFirstTTL,
				EyetrackingFPS,
				GazeSpaceWidth,
				GazeSpaceHeight,
				FrameScale,
				PadValue,
				FlipColors);
			await exporter.ExportAsync(destinationPath, isPng, includeRaw, progressReporter);

			IsProgressBarVisible = false;
			IsPlaybackEnabled = true;
			StatusText = "Export complete";
		}

	}
}
