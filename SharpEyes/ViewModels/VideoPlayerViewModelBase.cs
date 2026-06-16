using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	/// <summary>
	/// Base view model for the tabs that play a stimulus video: it owns the video
	/// reader, the playback timer, the frame transport (play/pause, step, slider),
	/// the timecode display, and the status/progress bar. Subclasses implement
	/// <see cref="LoadVideo"/> to open their video and <see cref="UpdateDisplay"/> to
	/// render the current frame plus whatever overlay they draw on top of it.
	/// </summary>
	public abstract class VideoPlayerViewModelBase : ViewModelBase
	{
		// == Commands ==
		public ReactiveCommand<Unit, Unit> LoadVideoCommand { get; }
		public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
		public ReactiveCommand<Unit, Unit> PreviousFrameCommand { get; }
		public ReactiveCommand<Unit, Unit> NextFrameCommand { get; }

		// == Video state ==
		protected VideoReader? videoReader = null;
		protected DispatcherTimer videoPlaybackTimer;
		protected Func<int, string> _timeFormatter;

		// == Progress bar ==
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

		// == Video dimensions ==
		// Backing fields are protected so subclasses can read them in their layout
		// computations; the setters are virtual so subclasses can raise their own
		// derived layout properties when the size changes.
		protected int _videoWidth = 1024;
		public virtual int VideoWidth
		{
			get => _videoWidth;
			set => this.RaiseAndSetIfChanged(ref _videoWidth, value);
		}

		protected int _videoHeight = 768;
		public virtual int VideoHeight
		{
			get => _videoHeight;
			set => this.RaiseAndSetIfChanged(ref _videoHeight, value);
		}

		// == Playback state ==
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

		public bool CanPlayVideo => videoReader != null;

		private bool _isPlaybackEnabled = true;
		public bool IsPlaybackEnabled
		{
			get => _isPlaybackEnabled;
			set => this.RaiseAndSetIfChanged(ref _isPlaybackEnabled, value);
		}

		protected VideoPlayerViewModelBase()
		{
			LoadVideoCommand = ReactiveCommand.Create(LoadVideo);
			PlayPauseCommand = ReactiveCommand.Create(PlayPause);
			PreviousFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(-1); });
			NextFrameCommand = ReactiveCommand.Create(() => { ChangeFrame(1); });
			videoPlaybackTimer = new DispatcherTimer();
			videoPlaybackTimer.Tick += VideoTimerTick;
		}

		/// <summary>
		/// Opens a stimulus video. Each tab resets its own gaze/feature state and
		/// configures the reader differently, so this is left to the subclass, which
		/// typically calls <see cref="PromptForVideoAsync"/> then <see cref="OpenVideoReader"/>.
		/// </summary>
		public abstract void LoadVideo();

		/// <summary>
		/// Shows the shared "Load stimulus video" open-file dialog and returns the
		/// chosen path, or null if the user cancelled.
		/// </summary>
		protected async Task<string?> PromptForVideoAsync()
		{
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
			return (fileName == null || fileName.Length == 0) ? null : fileName[0];
		}

		/// <summary>
		/// Opens the video reader for <paramref name="filePath"/> and initializes the
		/// shared playback state: first frame, playback timer interval, total frame
		/// count, timecode display, and the play-enabled state. The canvas size
		/// (<see cref="VideoWidth"/>/<see cref="VideoHeight"/>) is left to the caller,
		/// because tabs differ on whether the canvas tracks the native video size.
		/// </summary>
		protected void OpenVideoReader(string filePath)
		{
			videoReader = new VideoReader(filePath);
			videoReader.ReadFrame();
			VideoFrame = videoReader.GetFrameForDisplay();
			videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)videoReader.fps);
			TotalVideoFrames = videoReader.frameCount;
			UpdateTimecodeDisplay();
			this.RaisePropertyChanged("CanPlayVideo");
		}

		/// <summary>
		/// Renders the current video frame and any overlay the tab draws on top of it.
		/// Called by the playback timer and the frame transport.
		/// </summary>
		public abstract void UpdateDisplay();

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
		/// Called by the dispatcher timer to play the video. This is called once every frame to read
		/// in a video frame and update the display.
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
	}
}
