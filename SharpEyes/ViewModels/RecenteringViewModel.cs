using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Eyetracking;
using NumSharp;
using OpenCvSharp;
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
			set
			{
				this.RaiseAndSetIfChanged(ref _videoWidth, value);
				this.RaisePropertyChanged("RecenteringCanvasWidth");
				this.RaisePropertyChanged("RecenteringImageLeft");
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

		private double _gazeX = 0;
		private double _gazeY = 0;
		public double GazeX
		{
			get => _gazeX;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeX, value);
				this.RaisePropertyChanged("GazeCircleLeft");
				this.RaisePropertyChanged("RecenteringImageLeft");
			}
		}
		public double GazeY
		{
			get => _gazeY;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeY, value);
				this.RaisePropertyChanged("GazeCircleTop");
				this.RaisePropertyChanged("RecenteringImageTop");
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

		private int? FindFirstTTLGazeIndex()
		{
			if (gazeLocations == null || gazeLocations.Shape[1] < 4)
				return null;
			for (int i = 0; i < gazeLocations.Shape[0]; i++)
				if ((double)gazeLocations[i, 3] != 0.0)
					return i;
			return null;
		}

		public bool HasTTLData => IsGazeLoaded && FindFirstTTLGazeIndex() != null;

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
				this.RaisePropertyChanged("RecenteringImageLeft");
			}
		}

		private int _gazeSpaceHeight = 768;
		public int GazeSpaceHeight
		{
			get => _gazeSpaceHeight;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeSpaceHeight, value);
				this.RaisePropertyChanged("RecenteringImageTop");
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

		public RecenteringViewModel()
		{
			LoadVideoCommand = ReactiveCommand.Create(LoadVideo);
			PlayPauseCommand = ReactiveCommand.Create(PlayPause);
			LoadGazeCommand = ReactiveCommand.Create(LoadGaze);
			ExportCommand = ReactiveCommand.Create(Export);
			JumpToFirstTTLCommand = ReactiveCommand.Create(JumpToFirstTTL);
			videoPlaybackTimer = new DispatcherTimer();
			videoPlaybackTimer.Tick += this.VideoTimerTick;

			PreviousFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(-1); });
			NextFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(1); });
			TrailPoints = new ObservableCollection<TrailGazePoint>(
				Enumerable.Range(0, 10).Select(_ => new TrailGazePoint()));
		}

		public void JumpToFirstTTL()
		{
			int? firstTTLGazeIndex = FindFirstTTLGazeIndex();
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
			TotalVideoTime = videoReader.FramesToTimecode(videoReader.frameCount - 1);
			VideoWidth = videoReader.width;
			VideoHeight = videoReader.height;
			this.RaisePropertyChanged("CanPlayVideo");
			this.RaisePropertyChanged("ExportEnabled");
		}

		/// <summary>
		/// Loads video and gaze directly from existing data, without file dialogs.
		/// Called from the Stimulus and Gaze tab's "Send to Recentering" button.
		/// </summary>
		public void LoadFromStimulusGaze(string videoFilePath, NDArray gazeData, int gazeDataStartFrame, int eyetrackingFPS)
		{
			if (IsVideoPlaying)
				PlayPause();

			SetupVideo(videoFilePath);
			this.gazeLocations = gazeData.Clone() as NDArray;
			this.dataStartFrame = gazeDataStartFrame;
			EyetrackingFPS = eyetrackingFPS;
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

		public void UpdateDisplay()
		{
			VideoFrame = videoReader.GetFrameForDisplay();
			CurrentVideoFrame = videoReader.CurrentFrameNumber;
			CurrentVideoTime = videoReader.FramesToTimecode(videoReader.CurrentFrameNumber);
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

		// == Export ==

		public async void Export()
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

			string path = destinationPath;
			await Task.Run(() => RunExport(path, isPng, includeRaw));

			IsProgressBarVisible = false;
			IsPlaybackEnabled = true;
			StatusText = "Export complete";
		}

		private void RunExport(string destinationPath, bool isPng, bool includeRaw)
		{
			int scaledWidth  = Math.Max(1, (int)(VideoWidth  * FrameScale));
			int scaledHeight = Math.Max(1, (int)(VideoHeight * FrameScale));
			int outputWidth  = scaledWidth  * 2;
			int outputHeight = scaledHeight * 2;

			double frameIndexingFactor = (double)EyetrackingFPS / videoReader.fps;

			int gazeStartIndex = 0;
			int videoStartFrame = dataStartFrame.Value;
			if (StartFromFirstTTL)
			{
				int? firstTTLGazeIndex = FindFirstTTLGazeIndex();
				if (firstTTLGazeIndex != null)
				{
					gazeStartIndex = firstTTLGazeIndex.Value;
					double gazeElapsedTime = (double)gazeStartIndex / EyetrackingFPS;
					videoStartFrame = dataStartFrame.Value + (int)(gazeElapsedTime * videoReader.fps);
				}
			}

			int videoFramesAvailable = videoReader.frameCount - videoStartFrame;
			int maxFrameFromGaze = (int)Math.Floor((gazeLocations.Shape[0] - gazeStartIndex) / frameIndexingFactor);
			int nFramesToExport = Math.Min(videoFramesAvailable, maxFrameFromGaze);

			double padBGR = PadValue * 255.0;

			// Create output directories for PNG
			if (isPng)
			{
				if (includeRaw)
				{
					Directory.CreateDirectory(System.IO.Path.Combine(destinationPath, "recentered"));
					Directory.CreateDirectory(System.IO.Path.Combine(destinationPath, "raw"));
				}
				else
				{
					Directory.CreateDirectory(destinationPath);
				}
			}

			// For numpy: pre-allocate flat byte arrays, fill during loop, build NDArray after
			byte[] allRecenterredGray = isPng ? null : new byte[nFramesToExport * outputHeight * outputWidth];
			byte[] allRawGray = (isPng || !includeRaw) ? null : new byte[nFramesToExport * outputHeight * outputWidth];

			// Translation matrix: [[1, 0, tx], [0, 1, ty]] in CV_64F
			Mat translationMatrix = new Mat(2, 3, MatType.CV_64F, Scalar.All(0.0));
			translationMatrix.At<double>(0, 0) = 1.0;
			translationMatrix.At<double>(1, 1) = 1.0;

			videoReader.Seek(videoStartFrame);
			int actualFrameCount = 0;

			for (int frame = 0; frame < nFramesToExport; frame++)
			{
				if (!videoReader.ReadFrame()) break;

				int eyetrackingIndex = gazeStartIndex + (int)(frame * frameIndexingFactor);
				if (eyetrackingIndex >= gazeLocations.Shape[0]) break;

				double gazeXValue = (double)gazeLocations[eyetrackingIndex, 0];
				double gazeYValue = (double)gazeLocations[eyetrackingIndex, 1];
				// Replace NaN gaze with center of gaze space
				if (Double.IsNaN(gazeXValue)) gazeXValue = GazeSpaceWidth  / 2.0;
				if (Double.IsNaN(gazeYValue)) gazeYValue = GazeSpaceHeight / 2.0;

				double dx = GazeSpaceWidth  / 2.0 - gazeXValue;
				double dy = GazeSpaceHeight / 2.0 - gazeYValue;
				double tx = dx * FrameScale + scaledWidth  / 2.0;
				double ty = dy * FrameScale + scaledHeight / 2.0;

				// Downscale frame
				Mat scaledFrame = new Mat();
				Cv2.Resize(videoReader.cvFrame, scaledFrame, new OpenCvSharp.Size(scaledWidth, scaledHeight));

				// Apply recentered warp (inverse mapping via warpAffine)
				translationMatrix.At<double>(0, 2) = tx;
				translationMatrix.At<double>(1, 2) = ty;
				Mat recenterredFrame = new Mat();
				Cv2.WarpAffine(scaledFrame, recenterredFrame, translationMatrix,
					new OpenCvSharp.Size(outputWidth, outputHeight),
					borderMode: BorderTypes.Constant,
					borderValue: new Scalar(padBGR, padBGR, padBGR));

				if (isPng)
				{
					Mat frameToSave = recenterredFrame;
					Mat flipped = null;
					if (FlipColors)
					{
						flipped = new Mat();
						Cv2.CvtColor(recenterredFrame, flipped, ColorConversionCodes.BGR2RGB);
						frameToSave = flipped;
					}
					string pngPath = includeRaw
						? System.IO.Path.Combine(destinationPath, "recentered", String.Format("frame-{0:000000}.png", frame))
						: System.IO.Path.Combine(destinationPath, String.Format("frame-{0:000000}.png", frame));
					Cv2.ImWrite(pngPath, frameToSave);
					flipped?.Dispose();
				}
				else
				{
					// Convert to grayscale and copy row-by-row into pre-allocated flat array
					Mat grayFrame = new Mat();
					Cv2.CvtColor(recenterredFrame, grayFrame, ColorConversionCodes.BGR2GRAY);
					int offset = actualFrameCount * outputHeight * outputWidth;
					int step = (int)grayFrame.Step();
					for (int y = 0; y < outputHeight; y++)
						Marshal.Copy(IntPtr.Add(grayFrame.Data, y * step), allRecenterredGray, offset + y * outputWidth, outputWidth);
					grayFrame.Dispose();
				}

				if (includeRaw)
				{
					// Centered warp: frame is always placed at the center of the doubled canvas
					translationMatrix.At<double>(0, 2) = scaledWidth  / 2.0;
					translationMatrix.At<double>(1, 2) = scaledHeight / 2.0;
					Mat rawFrame = new Mat();
					Cv2.WarpAffine(scaledFrame, rawFrame, translationMatrix,
						new OpenCvSharp.Size(outputWidth, outputHeight),
						borderMode: BorderTypes.Constant,
						borderValue: new Scalar(padBGR, padBGR, padBGR));

					if (isPng)
					{
						Mat frameToSave = rawFrame;
						Mat flipped = null;
						if (FlipColors)
						{
							flipped = new Mat();
							Cv2.CvtColor(rawFrame, flipped, ColorConversionCodes.BGR2RGB);
							frameToSave = flipped;
						}
						string rawPngPath = System.IO.Path.Combine(destinationPath, "raw", String.Format("frame-{0:000000}.png", frame));
						Cv2.ImWrite(rawPngPath, frameToSave);
						flipped?.Dispose();
					}
					else
					{
						Mat grayRaw = new Mat();
						Cv2.CvtColor(rawFrame, grayRaw, ColorConversionCodes.BGR2GRAY);
						int offset = actualFrameCount * outputHeight * outputWidth;
						int step = (int)grayRaw.Step();
						for (int y = 0; y < outputHeight; y++)
							Marshal.Copy(IntPtr.Add(grayRaw.Data, y * step), allRawGray, offset + y * outputWidth, outputWidth);
						grayRaw.Dispose();
					}

					rawFrame.Dispose();
				}

				scaledFrame.Dispose();
				recenterredFrame.Dispose();
				actualFrameCount++;

				double progress = (double)actualFrameCount / nFramesToExport * 100.0;
				Dispatcher.UIThread.Post(() => ProgressBarValue = progress);
			}

			translationMatrix.Dispose();

			// Save numpy arrays
			if (!isPng)
			{
				NDArray recenterredArray = new NDArray(NPTypeCode.Byte, new Shape(actualFrameCount, outputHeight, outputWidth));
				for (int f = 0; f < actualFrameCount; f++)
					for (int y = 0; y < outputHeight; y++)
						for (int x = 0; x < outputWidth; x++)
							recenterredArray[f, y, x] = allRecenterredGray[f * outputHeight * outputWidth + y * outputWidth + x];
				Num.save(destinationPath, recenterredArray);

				if (includeRaw)
				{
					NDArray rawArray = new NDArray(NPTypeCode.Byte, new Shape(actualFrameCount, outputHeight, outputWidth));
					for (int f = 0; f < actualFrameCount; f++)
						for (int y = 0; y < outputHeight; y++)
							for (int x = 0; x < outputWidth; x++)
								rawArray[f, y, x] = allRawGray[f * outputHeight * outputWidth + y * outputWidth + x];

					string rawNpyPath = System.IO.Path.GetFileNameWithoutExtension(destinationPath) + " raw.npy";
					rawNpyPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(destinationPath)!, rawNpyPath);
					Num.save(rawNpyPath, rawArray);
				}
			}
		}
	}
}
