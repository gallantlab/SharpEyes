using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Eyetracking;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	public enum EditingState
	{
		None,
		DrawWindow,
		MovePupil
	}

	// note the ordering here should match the combobox items
	public enum PupilFinderType
	{
		Template,
		HoughCircles,
	}

	public class PupilFindingUserControlViewModel : ViewModelBase
	{
		// =========
		// UI things
		// =========

		// Commands
		public ReactiveCommand<Unit, Unit>? LoadVideoCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? FindPupilsCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? PlayPauseCommand { get; private set; } = null;
		public ReactiveCommand<Unit, Unit>? PreviousFrameCommand { get; private set; } = null;
		public ReactiveCommand<Unit, Unit>? NextFrameCommand { get; private set; } = null;
		public ReactiveCommand<Unit, Unit>? ReadTimestampsCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? LoadTimestampsCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? SaveDataCommand { get; private set; } = null;

		// == UI elements ==
		private EditingState _editingState = EditingState.None;
		public EditingState EditingState
		{
			get => _editingState;
			set
			{
				_editingState = value;
				// apparently this is not the ReactiveUI way to do things, but their documentation is nonexistent.
				// There is literally no page describing the syntax for the WhenAny method, only a poorly written set of
				// examples. Remember kids, examples are not documentation.
				// Imo this is also clearer.
				this.RaisePropertyChanged("IsMovingPupil");
				this.RaisePropertyChanged("IsDrawingWindow");
			}
		}
		public bool IsDrawingWindow
		{
			get => _editingState == EditingState.DrawWindow;
			set
			{
				this.RaiseAndSetIfChanged(ref _editingState, value ? EditingState.DrawWindow : EditingState.None);
				this.RaisePropertyChanged("IsMovingPupil");
			}
		}
		public bool IsMovingPupil
		{
			get => _editingState == EditingState.MovePupil;
			set
			{
				this.RaiseAndSetIfChanged(ref _editingState, value ? EditingState.MovePupil : EditingState.None);
				this.RaisePropertyChanged("IsDrawingWindow");
			}
		}

		private bool _isPupilManuallyEdited = false;
		public bool IsPupilManuallyEdited
		{
			get => _isPupilManuallyEdited;
			set
			{
				_isPupilManuallyEdited = value;
				if (value)
					PupilStrokeColor = Colors.LimeGreen;
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

		// video playback
		private int _videoWidth = 400;
		public int VideoWidth
		{
			get => _videoWidth;
			set => this.RaiseAndSetIfChanged(ref _videoWidth, value);
		}

		private int _videoHeight = 300;
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

		private Bitmap _framesProcessedPreviewImage = null;
		public Bitmap FramesProcessedPreviewImage
		{
			get => _framesProcessedPreviewImage;
			set => this.RaiseAndSetIfChanged(ref _framesProcessedPreviewImage, value);
		}
		public double FramesProcessedPreviewWidth { get; set; }

		// pupil overlay info
		private double _pupilX = 0;
		private double _pupilY = 0;
		public double PupilX
		{
			get => _pupilX;
			set
			{
				this.RaiseAndSetIfChanged(ref _pupilX, value);
				this.RaisePropertyChanged("PupilCircleLeft");
				this.RaisePropertyChanged("PupilXText");
			}
		}
		public double PupilY
		{
			get => _pupilY;
			set
			{
				this.RaiseAndSetIfChanged(ref _pupilY, value);
				this.RaisePropertyChanged("PupilCircleTop");
				this.RaisePropertyChanged("PupilYText");
			}
		}
		public double PupilCircleLeft => _pupilX - PupilRadius;
		public double PupilCircleTop => _pupilY - PupilRadius;

		private double _pupilDiameter = double.NaN;
		public double PupilRadius => _pupilDiameter / 2;
		public double PupilDiameter
		{
			get => _pupilDiameter;
			set
			{
				// pupil diameter is bounded
				_pupilDiameter = value;
				if (_pupilDiameter > MaxPupilDiameter)
					_pupilDiameter = MaxPupilDiameter;
				else if (_pupilDiameter < MinPupilDiameter)
					_pupilDiameter = MinPupilDiameter;

				this.RaisePropertyChanged("PupilDiameter");
				this.RaisePropertyChanged("PupilRadius");
				this.RaisePropertyChanged("PupilRadiusText");
				// because the circle is set by its left/top corner
				this.RaisePropertyChanged("PupilCircleLeft");
				this.RaisePropertyChanged("PupilCircleTop");
			}
		}

		private double _pupilConfidence = Double.NaN;
		public double PupilConfidence
		{
			get => _pupilConfidence;
			set
			{
				this.RaiseAndSetIfChanged(ref _pupilConfidence, value);
				this.RaisePropertyChanged("PupilConfidenceText");
			}
		}

		private int _pupilWindowLeft = 0;
		public int PupilWindowLeft
		{
			get => _pupilWindowLeft;
			set => this.RaiseAndSetIfChanged(ref _pupilWindowLeft, value);
		}

		private int _pupilWindowTop = 0;
		public int PupilWindowTop
		{
			get => _pupilWindowTop;
			set => this.RaiseAndSetIfChanged(ref _pupilWindowTop, value);
		}

		private int _pupilWindowWidth = 0;
		public int PupilWindowWidth
		{
			get => _pupilWindowWidth;
			set => this.RaiseAndSetIfChanged(ref _pupilWindowWidth, value);
		}

		private int _pupilWindowHeight = 0;
		public int PupilWindowHeight
		{
			get => _pupilWindowHeight;
			set => this.RaiseAndSetIfChanged(ref _pupilWindowHeight, value);
		}

		private double _pupilStrokeThickness = 4.0;
		public double PupilStrokeThickness
		{
			get => _pupilStrokeThickness;
			set => this.RaiseAndSetIfChanged(ref _pupilStrokeThickness, value);
		}

		private double _pupilStrokeOpacity = 0.75;
		public double PupilStrokeOpacity
		{
			get => _pupilStrokeOpacity;
			set => this.RaiseAndSetIfChanged(ref _pupilStrokeOpacity, value);
		}
		public Color PupilStrokeColor
		{
			get => PupilStrokeBrush.Color;
			set
			{
				PupilStrokeBrush.Color = value;
				this.RaisePropertyChanged("PupilStrokeBrush");
			}
		}

		public SolidColorBrush PupilStrokeBrush { get; set; } =  new SolidColorBrush(Colors.LimeGreen);

		// pupil finding info
		public string PupilXText => String.Format("X: {0:F1}", PupilX);
		public string PupilYText => String.Format("Y: {0:F1}", PupilY);
		public string PupilRadiusText => String.Format("Radius: {0:F1}", PupilRadius);
		public string PupilConfidenceText => String.Format("Confidence: {0:F4}", PupilConfidence);
		public bool ProcessAllFrames { get; set; } = false;
		public int FramesToProcess { get; set; } = 120;
		public int MinPupilDiameter { get; set; } = 10;
		public int MaxPupilDiameter { get; set; } = 75;
		public int PupilFinderTypeIndex { get; set; } = 0;
		public PupilFinderType PupilFinderType => (PupilFinderType)PupilFinderTypeIndex;

		private bool _isFindingPupils = false;
		public bool IsFindingPupils
		{
			get => _isFindingPupils;
			set
			{
				this.RaiseAndSetIfChanged(ref _isFindingPupils, value);
				this.RaisePropertyChanged("PupilFindingButtonText");
			}
		}
		public string PupilFindingButtonText => IsFindingPupils ? "Cancel" : "Find Pupils";
		private bool _isDataDirty = false;
		public bool IsDataDirty
		{
			get => _isDataDirty;
			set => this.RaiseAndSetIfChanged(ref _isDataDirty, value);
		}

		// confidence options - here because they can apply to all pupil finders
		public bool StopOnLowConfidence { get; set; } = true;
		public double LowConfidenceThreshold { get; set; } = 0.985;
		public int LowConfidenceFrameCountThreshold { get; set; } = 12;
		public bool EnableBlinkRejection { get; set; } = true;
		public double BlinkRejectionBlinkSigma { get; set; } = 2.0;
		public double BlinkRejectionPupilSigma { get; set; } = 2.0;

		// Timestamps
		private bool _showTimestampParsing = false;

		public bool ShowTimestampParsing
		{
			get => _showTimestampParsing;
			set => this.RaiseAndSetIfChanged(ref _showTimestampParsing, value);
		}
		public bool AutoReadTimestamps => true;

		// image pre-filtering
		private bool _showFilteredImage = false;
		public bool ShowFilteredImage
		{
			get => _showFilteredImage;
			set
			{
				if (pupilFinder != null)
					pupilFinder.UpdateDisplays();
				this.RaiseAndSetIfChanged(ref _showFilteredImage, value);
			}
		}

		public bool UseBilateralBlur { get; set; } = true;
		public int BilateralBlurSize { get; set; } = 3;
		public int BilateralBlurSigmaColor { get; set; } = 30;
		public int BilateralBlurSigmaSpace { get; set; } = 10;
		public bool UseMedianBlur { get; set; } = true;
		public int MedianBlurSize { get; set; } = 3;

		// manual adjustment
		public bool AutoEnterPupilEditMode { get; set; } = true;
		public bool UseLinearDecay { get; set; } = true;
		public bool UseExponentialDecay { get; set; } = false;
		public bool UseNoDecay { get; set; } = false;
		public int LinearDecayFrames { get; set; } = 180;
		public int ExponentialDecayTimeConstant { get; set; } = 30;


		// general state things for figuring out what buttons are active
		private bool _canPlayVideo = false;
		public bool CanPlayVideo
		{
			get => _canPlayVideo;
			set
			{
				this.RaiseAndSetIfChanged(ref _canPlayVideo, value);
				this.RaisePropertyChanged("CanFindPupils");
			}
		}

		private bool _isTimestampsRead = false;
		public bool IsTimestampsRead
		{
			get => _isTimestampsRead;
			set
			{
				this.RaiseAndSetIfChanged(ref _isTimestampsRead, value);
				this.RaisePropertyChanged("CanFindPupils");
			}
		}

		public bool CanFindPupils => _isTimestampsRead && _canPlayVideo;

		// children view models
		public TemplatePupilFinderConfigUserControlViewModel TemplatePupilFinderConfigUserControlViewModel { get; }


		// ============
		// Logic things
		// ============


		public PupilFinder? pupilFinder = null;

		private DispatcherTimer videoPlaybackTimer;

		public PupilFindingUserControlViewModel()
		{
			TemplatePupilFinderConfigUserControlViewModel = new TemplatePupilFinderConfigUserControlViewModel(this);

			videoPlaybackTimer = new DispatcherTimer(DispatcherPriority.Render);
			videoPlaybackTimer.Tick += this.VideoTimerTick;

			LoadVideoCommand = ReactiveCommand.Create(LoadVideo);
			FindPupilsCommand = ReactiveCommand.Create(FindPupils);
			ReadTimestampsCommand = ReactiveCommand.Create(ReadTimestamps);
			LoadTimestampsCommand = ReactiveCommand.Create(LoadTimeStamps);
			PreviousFrameCommand = ReactiveCommand.Create(PreviousFrame);
			NextFrameCommand = ReactiveCommand.Create(NextFrame);
			PlayPauseCommand = ReactiveCommand.Create(PlayPause);
			SaveDataCommand = ReactiveCommand.Create(SaveData);
		}

		private void VideoTimerTick(object? sender, EventArgs e)
		{
			if (pupilFinder.CurrentFrameNumber >= pupilFinder.frameCount - 1)
				PlayPause();
			pupilFinder.ReadGrayscaleFrame();
			pupilFinder.UpdateDisplays();
		}

		// command backings
		public async void LoadVideo()
		{
			// save stuff if a video is already loaded
			if (pupilFinder != null)
				SaveData();

			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load eyetracking video"
			};
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "AVI",
				Extensions = { "avi" }
			});
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;

			switch (PupilFinderType)
			{
				case PupilFinderType.Template:
					pupilFinder = new TemplatePupilFinder(fileName[0], this);
					break;
				case PupilFinderType.HoughCircles:
					pupilFinder = new HoughPupilFinder(fileName[0], this);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			CanPlayVideo = true;

			videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)pupilFinder.fps);

			pupilFinder.ReadGrayscaleFrame();
			pupilFinder.UpdateDisplays();

			if (!pupilFinder.isTimestampParsed)
			{
				ShowTimestampParsing = true;
				if (AutoReadTimestamps)
					pupilFinder.ParseTimeStamps();
			}

			if (pupilFinder is TemplatePupilFinder templatePupilFinder)
			{
				TemplatePupilFinderConfigUserControlViewModel.CurrentTemplateIndex = 0;
				TemplatePupilFinderConfigUserControlViewModel.TemplatePreviewImage =
					templatePupilFinder.GetTemplateImage(0);
			}

			FramesProcessedPreviewImage =
				pupilFinder.GetFramesProcessedPreviewImage(1920, 6);
		}

		public void FindPupils()
		{
			if (IsFindingPupils)
				pupilFinder.CancelPupilFindingDelegate();
			else
			{
				if (IsPupilManuallyEdited && TemplatePupilFinderConfigUserControlViewModel.AutoAddNewTemplate)
				{
					TemplatePupilFinderConfigUserControlViewModel.AddCurrentAsTemplate();
					IsPupilManuallyEdited = false;
				}

				PupilStrokeColor = Colors.LimeGreen;
				pupilFinder.FindPupils();
			}

			IsFindingPupils = !IsFindingPupils;
		}

		public void PlayPause()
		{
			if (IsVideoPlaying)
				videoPlaybackTimer.Stop();
			else
				videoPlaybackTimer.Start();
			IsVideoPlaying = !IsVideoPlaying;
		}

		public void PreviousFrame()
		{
			if (!CanPlayVideo) return;

			if (pupilFinder.CurrentFrameNumber <= 0)
			{
				return;
			}

			pupilFinder.CancelPupilFindingDelegate?.Invoke();
			ShowFrame(CurrentVideoFrame - 1);
		}

		public void NextFrame()
		{
			if (!CanPlayVideo) return;

			if (pupilFinder.CurrentFrameNumber >= pupilFinder.frameCount - 1)
			{
				return;
			}

			pupilFinder.CancelPupilFindingDelegate?.Invoke();
			ShowFrame(CurrentVideoFrame + 1);
		}

		public Func<int, string> TimeFormatter { get; set; }

		public void UpdateTimecodeDisplay()
		{
			if (pupilFinder == null)
				return;
			if (Settings.Current.ShowFrameNumber)
				TimeFormatter = frame => String.Format("Frame {0}", frame);
			else
				TimeFormatter = pupilFinder.FramesToTimecode;
			CurrentVideoTime = TimeFormatter(CurrentVideoFrame);
			TotalVideoTime = TimeFormatter(pupilFinder.frameCount);
		}

		public void ShowFrame()
		{
			ShowFrame(CurrentVideoFrame);
		}

		public void ShowFrame(int frame)
		{
			pupilFinder.Seek(frame);
			pupilFinder.ReadGrayscaleFrame();
			pupilFinder.UpdateDisplays();
		}

		public void ReadTimestamps()
		{
			if (pupilFinder != null)
				pupilFinder.ParseTimeStamps();
		}

		public async void LoadTimeStamps()
		{
			if (pupilFinder != null)
			{
				OpenFileDialog openFileDialog = new OpenFileDialog()
				{
					Title = "Load Timestamps...",
					Filters = { new FileDialogFilter() { Name = "Numpy File (*.npy)", Extensions = { "npy" } } }
				};
				string[] fileName = await openFileDialog.ShowAsync(MainWindow);

				if (fileName == null || fileName.Length == 0)
					return;
				pupilFinder.LoadTimestamps(fileName[0]);
			}
		}

		public void SaveData()
		{
			IsDataDirty = false;
			if (pupilFinder != null)
			{
				pupilFinder.SavePupilLocations();
				pupilFinder.SaveTimestamps();
				if (pupilFinder is TemplatePupilFinder templatePupilFinder)
					templatePupilFinder.SaveTemplates();
			}
		}
		public void OnClosing()
		{
			SaveData();
		}
	}
}
