using System;
using System.IO;
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
	public enum ExportFormat { PngFrames, NumpyArray }
	public enum ExportContent { RecenterredOnly, RecenterredAndRaw }

	public class RecenteringViewModel : GazeVideoPlayerViewModelBase
	{
		// == Commands (video transport and JumpToFirstTTL come from the base) ==
		public ReactiveCommand<Unit, Unit> LoadGazeCommand { get; set; }
		public ReactiveCommand<Unit, Unit> ExportCommand { get; set; }
		public ReactiveCommand<Unit, Unit> SendToMotionEnergyCommand { get; set; }

		// Set by MainWindowViewModel
		public MotionEnergyViewModel? MotionEnergyViewModel { get; set; }
		public Action? SwitchToMotionEnergyTab { get; set; }

		// == Video dimensions: recentering also tracks the doubled canvas layout ==
		public override int VideoWidth
		{
			get => base.VideoWidth;
			set
			{
				base.VideoWidth = value;
				this.RaisePropertyChanged("RecenteringCanvasWidth");
				this.RaisePropertyChanged("RecenteringImageLeft");
			}
		}

		public override int VideoHeight
		{
			get => base.VideoHeight;
			set
			{
				base.VideoHeight = value;
				this.RaisePropertyChanged("RecenteringCanvasHeight");
				this.RaisePropertyChanged("RecenteringImageTop");
			}
		}

		// Gaze overlay info
		private NDArray? gazeLocations = null;
		private string? gazeFileName = null;
		private GazeFilterSettings? gazeFilterSettings = null;

		protected override NDArray? GazeData => gazeLocations;

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

		protected override void OnGazeXChanged() => this.RaisePropertyChanged("RecenteringImageLeft");
		protected override void OnGazeYChanged() => this.RaisePropertyChanged("RecenteringImageTop");
		protected override void OnGazeLoadedChanged() => this.RaisePropertyChanged("ExportEnabled");

		public bool ExportEnabled => IsGazeLoaded && videoReader != null;

		private bool _startFromFirstTTL = true;
		public bool StartFromFirstTTL
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;

		// == Gaze coordinate space (the space the gaze data is expressed in) ==
		public int GazeSpaceWidth
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				this.RaisePropertyChanged("RecenteringImageLeft");
			}
		} = 1024;

		public int GazeSpaceHeight
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				this.RaisePropertyChanged("RecenteringImageTop");
			}
		} = 768;

		// == Recentered canvas layout ==
		public int RecenteringCanvasWidth => VideoWidth * 2;
		public int RecenteringCanvasHeight => VideoHeight * 2;

		// Position of the inner VideoCanvas on the doubled outer canvas so gaze lands at the center
		public double RecenteringImageLeft => VideoWidth  - GazeX * (double)VideoWidth  / GazeSpaceWidth;
		public double RecenteringImageTop  => VideoHeight - GazeY * (double)VideoHeight / GazeSpaceHeight;

		// == Export parameters ==
		public double FrameScale
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0.125;

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

		public bool FlipColors
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;

		public int SelectedExportFormatIndex
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0;

		public int SelectedExportContentIndex
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0;

		public bool IsGazeInfoExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				Settings.Current.RecenteringGazeInfoExpanded = value;
				Settings.Current.Save();
			}
		} = Settings.Current.RecenteringGazeInfoExpanded;

		public bool IsExportExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				Settings.Current.RecenteringExportExpanded = value;
				Settings.Current.Save();
			}
		} = Settings.Current.RecenteringExportExpanded;

		public RecenteringViewModel()
		{
			LoadGazeCommand = ReactiveCommand.CreateFromTask(LoadGaze);
			ExportCommand = ReactiveCommand.CreateFromTask(Export);
			SendToMotionEnergyCommand = ReactiveCommand.Create(SendToMotionEnergy);
		}

		public override async void LoadVideo()
		{
			dataStartFrame = null;
			IsGazeLoaded = false;
			gazeLocations = null;
			GazeX = 0;
			GazeY = 0;

			string? fileName = await PromptForVideoAsync();
			if (fileName == null)
				return;

			SetupVideo(fileName);
		}

		private void SetupVideo(string filePath)
		{
			OpenVideoReader(filePath);
			VideoWidth = videoReader.width;
			VideoHeight = videoReader.height;
			this.RaisePropertyChanged("ExportEnabled");
		}

		/// <summary>
		/// Loads video and gaze directly from existing data, without file dialogs.
		/// Called from the Stimulus and Gaze tab's "Send to Recentering" button.
		/// </summary>
		public void LoadFromStimulusGaze(string videoFilePath, NDArray gazeData, int gazeDataStartFrame, int eyetrackingFPS, GazeFilterSettings? filterSettings = null, string? gazeFileName = null)
		{
			if (IsVideoPlaying)
				PlayPause();

			SetupVideo(videoFilePath);
			this.gazeLocations = gazeData.Clone() as NDArray;
			this.gazeFileName = gazeFileName;
			this.dataStartFrame = gazeDataStartFrame;
			this.RaisePropertyChanged("DataStartFrame");
			EyetrackingFPS = eyetrackingFPS;
			gazeFilterSettings = filterSettings;
			IsGazeLoaded = true;
			RebuildTTLMarkerCache();
			UpdateDisplay();
		}

		public async Task LoadGaze()
		{
			string? fileName = await GazeFileDialog.ShowAsync(MainWindow);
			if (fileName == null)
				return;

			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = true;
			IsLoadingGaze = true;
			StatusText = "Loading gaze...";

			NDArray? loadedGazeLocations = null;
			int parsedSampleRate = 0;

			try
			{
				await Task.Run(() =>
				{
					loadedGazeLocations = GazeLoader.Load(fileName, out parsedSampleRate);
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
			gazeFileName = fileName;
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
				RebuildTTLMarkerCache();
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
