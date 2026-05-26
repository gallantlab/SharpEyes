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
	public class RecenteringViewModel : ViewModelBase
	{
		// == Commands ==
		public ReactiveCommand<Unit, Unit> LoadVideoCommand { get; set; }
		public ReactiveCommand<Unit, Unit>? PlayPauseCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit>? PreviousFrameCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit>? NextFrameCommand { get; set; } = null;
		public ReactiveCommand<Unit, Unit> LoadGazeCommand { get; set; }

		// == window reference for showing dialogs
		public Window? MainWindow =>
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
		private NDArray? gazeLocations = null;
		private int? dataStartFrame = null;

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

		private double _gazeX = 0;
		private double _gazeY = 0;
		public double GazeX
		{
			get => _gazeX;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeX, value);
				this.RaisePropertyChanged("GazeCircleLeft");
			}
		}
		public double GazeY
		{
			get => _gazeY;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeY, value);
				this.RaisePropertyChanged("GazeCircleTop");
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

		public RecenteringViewModel()
		{
			LoadVideoCommand = ReactiveCommand.Create(LoadVideo);
			PlayPauseCommand = ReactiveCommand.Create(PlayPause);
			LoadGazeCommand = ReactiveCommand.Create(LoadGaze);
			videoPlaybackTimer = new DispatcherTimer();
			videoPlaybackTimer.Tick += this.VideoTimerTick;

			PreviousFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(-1); });
			NextFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(1); });
			TrailPoints = new ObservableCollection<TrailGazePoint>(
				Enumerable.Range(0, 10).Select(_ => new TrailGazePoint()));
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

			videoReader = new VideoReader(fileName[0]);
			videoReader.ReadFrame();
			VideoFrame = videoReader.GetFrameForDisplay();
			videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)videoReader.fps);
			TotalVideoFrames = videoReader.frameCount;
			TotalVideoTime = videoReader.FramesToTimecode(videoReader.frameCount - 1);
			this.RaisePropertyChanged("CanPlayVideo");
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

		public void UpdateDisplay()
		{
			VideoFrame = videoReader.GetFrameForDisplay();
			CurrentVideoFrame = videoReader.CurrentFrameNumber;
			CurrentVideoTime = videoReader.FramesToTimecode(videoReader.CurrentFrameNumber);
			if (dataStartFrame != null)
			{
				double gazeXValue = gazeLocations[dataFrame, 0];
				double gazeYValue = gazeLocations[dataFrame, 1];
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
				Extensions = { "txt" }
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
			string extension = System.IO.Path.GetExtension(fileName[0]);
			if (extension == ".npy")
				gazeLocations = Num.load(fileName[0]);
			else if (extension == ".txt")
			{
				int parsedSampleRate;
				gazeLocations = EyelinkParser.ParseTextFile(fileName[0], out parsedSampleRate);
				if (parsedSampleRate > 0)
					EyetrackingFPS = parsedSampleRate;
			}
			else if (extension == ".edf")
			{
				int parsedSampleRate;
				gazeLocations = EyelinkParser.ParseEDFFile(fileName[0], out parsedSampleRate);
				if (parsedSampleRate > 0)
					EyetrackingFPS = parsedSampleRate;
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

				gazeLocations = new NDArray(NPTypeCode.Double, Shape.Matrix(values.Count, 2));
				for (int i = 0; i < values.Count; i++)
				{
					gazeLocations[i, 0] = values[i][0];
					gazeLocations[i, 1] = values[i][1];
				}
			}
			IsGazeLoaded = true;
			if (videoReader != null)
				dataStartFrame = videoReader.CurrentFrameNumber;
		}
	}
}
