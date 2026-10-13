using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ReactiveUI;
using Eyetracking;
using NumSharp;
using SharpEyes.Models;
using Num = NumSharp.np;

namespace SharpEyes.ViewModels
{
	/// <summary>
	/// A kernel that can be ticked on or off for the hyperparameter grid.
	/// </summary>
	public class KernelOption : ReactiveObject
	{
		public RBFKernel Kernel { get; }

		public string Name => Kernel.ToString();

		public bool IsSelected
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="kernel">The kernel.</param>
		/// <param name="isSelected">Whether the kernel starts ticked.</param>
		public KernelOption(RBFKernel kernel, bool isSelected)
		{
			Kernel = kernel;
			IsSelected = isSelected;
		}
	}

	public class CalibrationViewModel : ViewModelBase
	{
		private static readonly RBFKernel[] DefaultSelectedKernels = { RBFKernel.ThinPlate, RBFKernel.Multiquadric, RBFKernel.Linear, RBFKernel.Cubic };

		public ReactiveCommand<Unit, Unit> LoadCalibrationPupilsCommand { get; set; }

		public ReactiveCommand<Unit, Unit> LoadPupilsToConvertCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ConvertPupilFinderCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ComputeMappingCommand { get; set; }

		public ReactiveCommand<Unit, Unit> SearchTimingCommand { get; set; }

		public ReactiveCommand<Unit, Unit> SaveCalibrationCommand { get; set; }

		public ReactiveCommand<Unit, Unit> LoadCalibrationCommand { get; set; }

		public ReactiveCommand<Unit, Unit> SaveGazeCommand { get; set; }

		public ReactiveCommand<Unit, Unit> SendToStimulusGazeCommand { get; set; }

		public ReactiveCommand<Unit, Unit> RegeneratePointsCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ForceRedrawCommand { get; set; }

		// == Other tabs, wired by the main window view model ==

		public PupilFindingUserControlViewModel? PupilFindingViewModel { get; set; } = null;

		public StimulusGazeViewModel? StimulusGazeViewModel { get; set; } = null;

		public Action? SwitchToStimulusGazeTab { get; set; } = null;

		// == Data ==

		private readonly GazeCalibrator calibrator = new GazeCalibrator();

		private NDArray? pupilsToMap = null;

		private NDArray? mappedGaze = null;

		public PupilInfo PupilInfo
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				UpdateStateMarkers();
				RefreshCalibrationFixations();
			}
		} = null;

		// Cancels the fixation aggregation in flight when the settings change again or a fit replaces the fixations
		private CancellationTokenSource? fixationRefreshCancellation = null;

		// True when a calibration or preprocessing setting changed and the fixations have not been re-aggregated since
		private bool areCalibrationSettingsDirty = false;

		/// <summary>
		/// Sets a sidebar property and, when its value changed, marks the calibration settings as changed so the
		/// fixations are re-aggregated once the edit is accepted.
		/// </summary>
		/// <typeparam name="T">Type of the property.</typeparam>
		/// <param name="backingField">Backing field of the property.</param>
		/// <param name="value">New value.</param>
		/// <param name="propertyName">Name of the property, supplied by the compiler.</param>
		private void SetCalibrationSetting<T>(ref T backingField, T value, [CallerMemberName] string propertyName = "")
		{
			bool changed = !EqualityComparer<T>.Default.Equals(backingField, value);
			this.RaiseAndSetIfChanged(ref backingField, value, propertyName);
			if (changed)
				areCalibrationSettingsDirty = true;
		}

		/// <summary>
		/// Re-aggregates the calibration fixations if a setting changed since the last aggregation. The view calls this
		/// when an edited control is accepted, such as by losing focus.
		/// </summary>
		public void CommitCalibrationSettings()
		{
			SaveCalibrationSettings();
			if (!areCalibrationSettingsDirty) return;
			areCalibrationSettingsDirty = false;
			RefreshCalibrationFixations();
		}

		/// <summary>
		/// Writes the calibration, preprocessing, interpolator, search, and calibration point settings to the settings file.
		/// </summary>
		private void SaveCalibrationSettings()
		{
			Settings settings = Settings.Current;
			settings.CalibrationBlinkConfidenceThreshold = BlinkConfidenceThreshold;
			settings.CalibrationStimulusWidth = StimulusWidth;
			settings.CalibrationStimulusHeight = StimulusHeight;
			settings.CalibrationDelay = CalibrationDelay;
			settings.CalibrationDuration = CalibrationDuration;
			settings.CalibrationPointDelay = PointDelay;
			settings.CalibrationEndSkip = EndSkip;
			settings.CalibrationEyetrackingFPS = EyetrackingFPS;
			settings.CalibrationDPIUnscaleFactor = DPIUnscaleFactor;
			settings.CalibrationFilterEnabled = IsFilterEnabled;
			settings.CalibrationFilterWindowSize = FilterWindowSize;
			settings.CalibrationFilterPupilSize = FilterPupilSize;
			settings.CalibrationEnableOutlierRemoval = EnableOutlierRemoval;
			settings.CalibrationOutlierThresholdX = OutlierThresholdX;
			settings.CalibrationOutlierThresholdY = OutlierThresholdY;
			settings.CalibrationOutlierThresholdRadius = OutlierThresholdRadius;
			settings.CalibrationUseVarianceThreshold = UseVarianceThreshold;
			settings.CalibrationVarianceThresholdPercentile = VarianceThresholdPercentile;
			settings.CalibrationUseLegacyConvention = UseLegacyConvention;
			settings.CalibrationSelectedKernels = KernelOptions.Where(option => option.IsSelected).Select(option => option.Kernel.ToString()).ToList();
			settings.CalibrationUseDefaultEpsilon = UseDefaultEpsilon;
			settings.CalibrationEpsilon = Epsilon;
			settings.CalibrationSmoothnessMin = SmoothnessMin;
			settings.CalibrationSmoothnessMax = SmoothnessMax;
			settings.CalibrationSmoothnessCount = SmoothnessCount;
			settings.CalibrationLogSpaceSmoothness = LogSpaceSmoothness;
			settings.CalibrationSearchDelayMin = SearchDelayMin;
			settings.CalibrationSearchDelayMax = SearchDelayMax;
			settings.CalibrationSearchDelayStep = SearchDelayStep;
			settings.CalibrationSearchDurationMin = SearchDurationMin;
			settings.CalibrationSearchDurationMax = SearchDurationMax;
			settings.CalibrationSearchDurationStep = SearchDurationStep;
			settings.CalibrationPoints = CalibrationPoints.Select(point => new SavedPoint { X = point.X, Y = point.Y }).ToList();
			settings.Save();
		}

		/// <summary>
		/// Reads the calibration, preprocessing, interpolator, search, and calibration point settings from the settings
		/// file, leaving the calibration start untouched.
		/// </summary>
		private void LoadCalibrationSettings()
		{
			Settings settings = Settings.Current;
			BlinkConfidenceThreshold = settings.CalibrationBlinkConfidenceThreshold;
			StimulusWidth = settings.CalibrationStimulusWidth;
			StimulusHeight = settings.CalibrationStimulusHeight;
			CalibrationDelay = settings.CalibrationDelay;
			CalibrationDuration = settings.CalibrationDuration;
			PointDelay = settings.CalibrationPointDelay;
			EndSkip = settings.CalibrationEndSkip;
			EyetrackingFPS = settings.CalibrationEyetrackingFPS;
			DPIUnscaleFactor = settings.CalibrationDPIUnscaleFactor;
			IsFilterEnabled = settings.CalibrationFilterEnabled;
			FilterWindowSize = settings.CalibrationFilterWindowSize;
			FilterPupilSize = settings.CalibrationFilterPupilSize;
			EnableOutlierRemoval = settings.CalibrationEnableOutlierRemoval;
			OutlierThresholdX = settings.CalibrationOutlierThresholdX;
			OutlierThresholdY = settings.CalibrationOutlierThresholdY;
			OutlierThresholdRadius = settings.CalibrationOutlierThresholdRadius;
			UseVarianceThreshold = settings.CalibrationUseVarianceThreshold;
			VarianceThresholdPercentile = settings.CalibrationVarianceThresholdPercentile;
			UseLegacyConvention = settings.CalibrationUseLegacyConvention;
			UseDefaultEpsilon = settings.CalibrationUseDefaultEpsilon;
			Epsilon = settings.CalibrationEpsilon;
			SmoothnessMin = settings.CalibrationSmoothnessMin;
			SmoothnessMax = settings.CalibrationSmoothnessMax;
			SmoothnessCount = settings.CalibrationSmoothnessCount;
			LogSpaceSmoothness = settings.CalibrationLogSpaceSmoothness;
			SearchDelayMin = settings.CalibrationSearchDelayMin;
			SearchDelayMax = settings.CalibrationSearchDelayMax;
			SearchDelayStep = settings.CalibrationSearchDelayStep;
			SearchDurationMin = settings.CalibrationSearchDurationMin;
			SearchDurationMax = settings.CalibrationSearchDurationMax;
			SearchDurationStep = settings.CalibrationSearchDurationStep;
			CalibrationPoints.Clear();
			if (settings.CalibrationPoints != null && settings.CalibrationPoints.Count > 0)
			{
				foreach (SavedPoint point in settings.CalibrationPoints)
					CalibrationPoints.Add(new Point(point.X, point.Y));
			}
			else
			{
				CalibrationParameters defaults = CalibrationParameters.GetDefault35PointCalibrationParameters();
				foreach (CalibrationIndex index in defaults.calibrationSequence)
					CalibrationPoints.Add(defaults.calibrationPoints[index.Index]);
			}
			areCalibrationSettingsDirty = false;
		}

		/// <summary>
		/// Re-aggregates the calibration fixations from the loaded pupils with the current sidebar settings and redraws
		/// them without leave-one-out errors, interrupting any aggregation still running.
		/// </summary>
		private async void RefreshCalibrationFixations()
		{
			fixationRefreshCancellation?.Cancel();
			if (calibrator.IsFitted && !isMappingStale)
				MarkMappingStale();
			if ((object)PupilInfo == null) return;
			CancellationTokenSource cancellation = new CancellationTokenSource();
			fixationRefreshCancellation = cancellation;
			CancellationToken token = cancellation.Token;
			try
			{
				NDArray pupils = PupilInfo.Pupils;
				double[,] screenPoints = GetScreenPoints();
				int onsetFrame = GetOnsetFrame();
				double fps = EyetrackingFPS;
				double delay = CalibrationDelay;
				double duration = CalibrationDuration;
				double pointDelay = PointDelay;
				double endSkip = EndSkip;
				double blinkThreshold = BlinkConfidenceThreshold;
				GazeFilterSettings filterSettings = BuildFilterSettings();
				double? variancePercentile = UseVarianceThreshold ? VarianceThresholdPercentile : (double?)null;
				CalibrationFixation[] fixations = await Task.Run(() => GazeCalibrator.BuildCalibrationFixations(pupils, screenPoints, onsetFrame, delay,
					duration, pointDelay, endSkip, fps, blinkThreshold, filterSettings, variancePercentile, token), token);
				if (token.IsCancellationRequested) return;
				CalibrationFixations = new ObservableCollection<CalibrationFixation>(fixations);
				ForceRedraw();
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Could not aggregate calibration points: {0}", exception.Message);
			}
		}

		// == State markers: red absent, green present; pupils to map is blue when only the calibration trace is available to map ==

		private static readonly IBrush AbsentMarkerBrush = new SolidColorBrush(Colors.Red);
		private static readonly IBrush PresentMarkerBrush = new SolidColorBrush(Colors.LimeGreen);
		private static readonly IBrush SameTraceMarkerBrush = new SolidColorBrush(Colors.DodgerBlue);
		private static readonly IBrush StaleMarkerBrush = new SolidColorBrush(Colors.Orange);

		// True once the calibration settings have changed since the mapping was fitted or loaded
		private bool isMappingStale = false;

		/// <summary>
		/// Flags the mapping as no longer matching the sidebar settings and shows that in the marker and the error line.
		/// </summary>
		private void MarkMappingStale()
		{
			isMappingStale = true;
			CalibrationRMSError = "Mapping is stale: settings changed since it was computed";
			IsRMSErrorHigh = false;
			UpdateStateMarkers();
		}

		public IBrush CalibrationPupilsMarkerBrush => (object)PupilInfo != null ? PresentMarkerBrush : AbsentMarkerBrush;

		public IBrush MappingMarkerBrush
		{
			get
			{
				if (!calibrator.IsFitted) return AbsentMarkerBrush;
				return isMappingStale ? StaleMarkerBrush : PresentMarkerBrush;
			}
		}

		public IBrush PupilsToMapMarkerBrush
		{
			get
			{
				if ((object)pupilsToMap != null && !ReferenceEquals(pupilsToMap, PupilInfo?.Pupils))
					return PresentMarkerBrush;
				return (object)PupilInfo != null ? SameTraceMarkerBrush : AbsentMarkerBrush;
			}
		}

		public IBrush GazeMarkerBrush => (object)mappedGaze != null ? PresentMarkerBrush : AbsentMarkerBrush;

		// == Button enabling ==

		public bool HasCalibrationPupils => (object)PupilInfo != null;

		public bool HasMapping => calibrator.IsFitted;

		public bool HasMappedGaze => (object)mappedGaze != null;

		/// <summary>
		/// Raises the four state marker brushes and the button enabling flags so the view updates them.
		/// </summary>
		private void UpdateStateMarkers()
		{
			this.RaisePropertyChanged("CalibrationPupilsMarkerBrush");
			this.RaisePropertyChanged("MappingMarkerBrush");
			this.RaisePropertyChanged("PupilsToMapMarkerBrush");
			this.RaisePropertyChanged("GazeMarkerBrush");
			this.RaisePropertyChanged("HasCalibrationPupils");
			this.RaisePropertyChanged("HasMapping");
			this.RaisePropertyChanged("HasMappedGaze");
		}

		public int StimulusWidth
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1024;

		public int StimulusHeight
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 768;

		public double BlinkConfidenceThreshold
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 0.6;

		// == Calibration timing ==

		// Switching between frame and timestamp entry only changes which field is shown, so the hidden field is
		// populated from the shown one and the fixations are not re-aggregated.
		public bool UseTimestampForStart
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				if (value)
					PopulateStartTimeStampFromFrame();
				else
					PopulateStartFrameFromTimeStamp();
			}
		} = false;

		private int _calibrationStartFrame = 0;
		public int CalibrationStartFrame
		{
			get => _calibrationStartFrame;
			set => SetCalibrationSetting(ref _calibrationStartFrame, value);
		}

		private string _calibrationStartTimeStamp = "0:00:00.000";
		public string CalibrationStartTimeStamp
		{
			get => _calibrationStartTimeStamp;
			set => SetCalibrationSetting(ref _calibrationStartTimeStamp, value);
		}

		/// <summary>
		/// Sets the start timestamp field to the timestamp of the start frame without marking the settings changed.
		/// </summary>
		private void PopulateStartTimeStampFromFrame()
		{
			if ((object)PupilInfo == null || (object)PupilInfo.Timestamps == null) return;
			int frame = Math.Clamp(_calibrationStartFrame, 0, PupilInfo.Timestamps.shape[0] - 1);
			Tuple<int, int, int, int> timestamp = PupilInfo.GetTimestampForFrame(frame);
			this.RaiseAndSetIfChanged(ref _calibrationStartTimeStamp,
				String.Format("{0}:{1:D2}:{2:D2}.{3:D3}", timestamp.Item1, timestamp.Item2, timestamp.Item3, timestamp.Item4), "CalibrationStartTimeStamp");
		}

		/// <summary>
		/// Sets the start frame field to the frame closest to the start timestamp without marking the settings changed.
		/// An unparsable timestamp leaves the frame unchanged.
		/// </summary>
		private void PopulateStartFrameFromTimeStamp()
		{
			if ((object)PupilInfo == null || (object)PupilInfo.Timestamps == null) return;
			try
			{
				this.RaiseAndSetIfChanged(ref _calibrationStartFrame, PupilInfo.TimeStampToFrameNumber(_calibrationStartTimeStamp), "CalibrationStartFrame");
			}
			catch (ArgumentException)
			{
			}
		}

		public double CalibrationDuration
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 2.0;

		public double CalibrationDelay
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 2.0;

		public double PointDelay
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 0.167;

		public double EndSkip
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 0;

		public int EyetrackingFPS
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 60;

		public double DPIUnscaleFactor
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1.0;

		// == Preprocessing ==

		public bool IsFilterEnabled
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = true;

		public int FilterWindowSize
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 15;

		public bool FilterPupilSize
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = true;

		public bool EnableOutlierRemoval
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = false;

		public double OutlierThresholdX
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 95;

		public double OutlierThresholdY
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 95;

		public double OutlierThresholdRadius
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 95;

		public bool UseVarianceThreshold
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = false;

		public double VarianceThresholdPercentile
		{
			get;
			set => SetCalibrationSetting(ref field, value);
		} = 90;

		// == Interpolator ==

		public bool UseLegacyConvention
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				RebuildKernelOptions();
			}
		} = true;

		public ObservableCollection<KernelOption> KernelOptions
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new ObservableCollection<KernelOption>();

		public bool UseDefaultEpsilon
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;

		public double Epsilon
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1.0;

		public double SmoothnessMin
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0;

		public double SmoothnessMax
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 10;

		public int SmoothnessCount
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 100;

		public bool LogSpaceSmoothness
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;

		// == Timing search ==

		public double SearchDelayMin
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1.98;

		public double SearchDelayMax
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 2.41;

		public double SearchDelayStep
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0.01;

		public double SearchDurationMin
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1.98;

		public double SearchDurationMax
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 2.21;

		public double SearchDurationStep
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0.01;

		// == Results ==

		public string CalibrationRMSError
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = "Mapping not yet computed";

		private const double HighRMSErrorPixels = 60;

		public bool IsRMSErrorHigh
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;

		public string FittedKernel
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = String.Empty;

		public string FittedSmoothness
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = String.Empty;

		public string FittedEpsilon
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = String.Empty;

		public ObservableCollection<CalibrationFixation> CalibrationFixations
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new ObservableCollection<CalibrationFixation>();

		public ObservableCollection<Point> CalibrationPoints
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new ObservableCollection<Point>();

		// objects drawn on the screen to visualize things

		public ObservableCollection<Control> ShapesToDraw
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new ObservableCollection<Control>();

		// Canvas size in drawing units; the drawing is zoomed to the bounding box of the fixations once there are any
		private const double CanvasLongSide = 900;
		private const double CanvasMargin = 50;

		public double CanvasWidth
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 640;

		public double CanvasHeight
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 480;

		// == Expander states ==

		public bool IsCalibrationParametersExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationParametersExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationParametersExpanded;

		public bool IsCalibrationPointsExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationPointsExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationPointsExpanded;

		public bool IsDataExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationDataExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationDataExpanded;

		public bool IsPreprocessingExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationPreprocessingExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationPreprocessingExpanded;

		public bool IsInterpolatorExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationInterpolatorExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationInterpolatorExpanded;

		public bool IsSearchExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationSearchExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationSearchExpanded;

		public bool IsResultsExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationResultsExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationResultsExpanded;

		public CalibrationViewModel()
		{
			LoadCalibrationPupilsCommand = ReactiveCommand.Create(LoadCalibrationPupils);
			LoadPupilsToConvertCommand = ReactiveCommand.Create(LoadPupilsToConvert);
			ComputeMappingCommand = ReactiveCommand.Create(ComputeMapping);
			SearchTimingCommand = ReactiveCommand.Create(SearchTiming);
			ConvertPupilFinderCommand = ReactiveCommand.Create(ConvertPupilFinderData);
			SaveCalibrationCommand = ReactiveCommand.Create(SaveCalibration);
			LoadCalibrationCommand = ReactiveCommand.Create(LoadCalibration);
			SaveGazeCommand = ReactiveCommand.Create(SaveGaze);
			SendToStimulusGazeCommand = ReactiveCommand.Create(SendToStimulusGaze);
			RegeneratePointsCommand = ReactiveCommand.Create(RegeneratePoints);
			ForceRedrawCommand = ReactiveCommand.Create(ForceRedraw);

			VideoWidth = 640;
			VideoHeight = 480;
			LoadCalibrationSettings();

			ForceRedraw();
		}

		// == Loading calibration pupils ==

		public async void LoadCalibrationPupils()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load Pupils...",
				Filters = { new FileDialogFilter() { Name = "Numpy File (*.npy)", Extensions = { "npy" } } }
			};
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;
			string pupilsFile = fileName[0];

			openFileDialog.Title = "Load Timestamps...";
			fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;
			string timestampsFile = fileName[0];

			PupilInfo = new PupilInfo(Num.load(pupilsFile), Num.load(timestampsFile), EyetrackingFPS);
			StatusText = String.Format("Loaded {0} calibration pupil frames", PupilInfo.Pupils.Shape[0]);
		}

		/// <summary>
		/// Takes the pupils, timestamps, frame rate, and video size of a pupil finder as the calibration data.
		/// </summary>
		/// <param name="pupilFinder">The pupil finder sent from the pupil finding tab.</param>
		public void LoadFromPupilFinding(PupilFinder pupilFinder)
		{
			EyetrackingFPS = pupilFinder.fps;
			VideoWidth = pupilFinder.width;
			VideoHeight = pupilFinder.height;
			PupilInfo = new PupilInfo(pupilFinder.pupilLocations, pupilFinder.timeStamps, pupilFinder.fps);
			ForceRedraw();
			StatusText = String.Format("Received {0} calibration pupil frames from pupil finding", PupilInfo.Pupils.Shape[0]);
		}

		// == Fitting ==

		/// <summary>
		/// Aggregates the calibration fixations from the loaded pupils with the sidebar settings, selects the kernel and
		/// smoothness by leave-one-out error over the grid, and fits the mapping.
		/// </summary>
		public async void ComputeMapping()
		{
			if ((object)PupilInfo == null)
			{
				StatusText = "Load calibration pupils first";
				return;
			}

			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = true;
			StatusText = "Computing mapping...";
			try
			{
				NDArray pupils = PupilInfo.Pupils;
				double[,] screenPoints = GetScreenPoints();
				int onsetFrame = GetOnsetFrame();
				double fps = EyetrackingFPS;
				double delay = CalibrationDelay;
				double duration = CalibrationDuration;
				double pointDelay = PointDelay;
				double endSkip = EndSkip;
				double blinkThreshold = BlinkConfidenceThreshold;
				GazeFilterSettings filterSettings = BuildFilterSettings();
				double? variancePercentile = UseVarianceThreshold ? VarianceThresholdPercentile : (double?)null;
				List<RBFKernel> kernels = GetSelectedKernels();
				double[] smoothnesses = BuildSmoothnessGrid();
				bool useLegacy = UseLegacyConvention;
				double? epsilon = GetEpsilon();

				CalibrationFixation[] fixations = Array.Empty<CalibrationFixation>();
				await Task.Run(() =>
				{
					fixations = GazeCalibrator.BuildCalibrationFixations(pupils, screenPoints, onsetFrame, delay, duration, pointDelay, endSkip, fps,
						blinkThreshold, filterSettings, variancePercentile);
					double[,,] stack = new double[1, fixations.Length, 2];
					for (int fixation = 0; fixation < fixations.Length; fixation++)
					{
						stack[0, fixation, 0] = fixations[fixation].PupilX;
						stack[0, fixation, 1] = fixations[fixation].PupilY;
					}
					double[,,] errors = RBFMapping.EvaluateHyperparameterGrid(stack, screenPoints, kernels, smoothnesses, useLegacy, epsilon);
					FindMinimum(errors, out int combinationIndex, out int kernelIndex, out int smoothnessIndex);
					calibrator.Fit(fixations, kernels[kernelIndex], smoothnesses[smoothnessIndex], useLegacy, epsilon);
				});
				mappedGaze = null;
				RecordSettings();
				ShowFitResults();
				UpdateStateMarkers();
				StatusText = String.Format("Mapping computed with {0} of {1} points", fixations.Count(fixation => fixation.IsValid), fixations.Length);
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Mapping failed: {0}", exception.Message);
			}
			finally
			{
				IsProgressBarVisible = false;
				IsProgressBarIndeterminate = false;
			}
		}

		/// <summary>
		/// Evaluates the hyperparameter grid for every delay and duration combination in the search ranges, writes
		/// the best combination into the sidebar, and refits with it.
		/// </summary>
		public async void SearchTiming()
		{
			if ((object)PupilInfo == null)
			{
				StatusText = "Load calibration pupils first";
				return;
			}

			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = true;
			StatusText = "Searching delay and duration...";
			try
			{
				NDArray pupils = PupilInfo.Pupils;
				double[,] screenPoints = GetScreenPoints();
				int onsetFrame = GetOnsetFrame();
				double fps = EyetrackingFPS;
				double[] delays = BuildRange(SearchDelayMin, SearchDelayMax, SearchDelayStep);
				double[] durations = BuildRange(SearchDurationMin, SearchDurationMax, SearchDurationStep);
				double pointDelay = PointDelay;
				double endSkip = EndSkip;
				double blinkThreshold = BlinkConfidenceThreshold;
				GazeFilterSettings filterSettings = BuildFilterSettings();
				double? variancePercentile = UseVarianceThreshold ? VarianceThresholdPercentile : (double?)null;
				List<RBFKernel> kernels = GetSelectedKernels();
				double[] smoothnesses = BuildSmoothnessGrid();
				bool useLegacy = UseLegacyConvention;
				double? epsilon = GetEpsilon();

				int bestCombination = 0;
				double bestError = Double.PositiveInfinity;
				await Task.Run(() =>
				{
					NDArray processed = GazeCalibrator.PreprocessPupils(pupils, blinkThreshold, filterSettings);
					double[,,] stack = GazeCalibrator.BuildSearchStack(processed, screenPoints, onsetFrame, delays, durations, pointDelay, endSkip, fps, variancePercentile);
					double[,,] errors = RBFMapping.EvaluateHyperparameterGrid(stack, screenPoints, kernels, smoothnesses, useLegacy, epsilon);
					bestError = FindMinimum(errors, out bestCombination, out int kernelIndex, out int smoothnessIndex);
				});
				if (Double.IsInfinity(bestError))
				{
					StatusText = "No delay and duration combination produced a usable fit";
					return;
				}
				CalibrationDelay = delays[bestCombination / durations.Length];
				CalibrationDuration = durations[bestCombination % durations.Length];
				StatusText = String.Format("Best delay {0:F3} s, duration {1:F3} s, error {2:F2} px", CalibrationDelay, CalibrationDuration, bestError);
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Search failed: {0}", exception.Message);
				return;
			}
			finally
			{
				IsProgressBarVisible = false;
				IsProgressBarIndeterminate = false;
			}
			ComputeMapping();
		}

		// == Mapping pupils to gaze ==

		public async void LoadPupilsToConvert()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load Pupils to Map...",
				Filters = { new FileDialogFilter() { Name = "Numpy File (*.npy)", Extensions = { "npy" } } }
			};
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);
			if (fileName == null || fileName.Length == 0)
				return;
			pupilsToMap = Num.load(fileName[0]);
			UpdateStateMarkers();
			await MapPupils();
		}

		/// <summary>
		/// Maps the pupils currently held by the pupil finding tab.
		/// </summary>
		public async void ConvertPupilFinderData()
		{
			PupilFinder? pupilFinder = PupilFindingViewModel?.pupilFinder;
			if (pupilFinder == null || (object)pupilFinder.pupilLocations == null)
			{
				StatusText = "No pupils found in the pupil finding tab";
				return;
			}
			pupilsToMap = pupilFinder.pupilLocations;
			UpdateStateMarkers();
			await MapPupils();
		}

		/// <summary>
		/// Preprocesses the pupils to map with the sidebar settings and maps them to screen space with the fitted mapping.
		/// </summary>
		private async Task MapPupils()
		{
			if (!calibrator.IsFitted)
			{
				StatusText = "Compute or load a mapping first";
				return;
			}
			if ((object)pupilsToMap == null)
				return;
			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = true;
			StatusText = "Mapping pupils to gaze...";
			try
			{
				NDArray pupils = pupilsToMap;
				double blinkThreshold = BlinkConfidenceThreshold;
				GazeFilterSettings filterSettings = BuildFilterSettings();
				mappedGaze = await Task.Run(() =>
				{
					NDArray processed = GazeCalibrator.PreprocessPupils(pupils, blinkThreshold, filterSettings);
					return calibrator.MapToScreen(processed);
				});
				StatusText = String.Format("Mapped {0} frames to gaze", mappedGaze.Shape[0]);
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Mapping pupils failed: {0}", exception.Message);
			}
			finally
			{
				IsProgressBarVisible = false;
				IsProgressBarIndeterminate = false;
				UpdateStateMarkers();
			}
		}

		public async void SaveGaze()
		{
			if ((object)mappedGaze == null)
			{
				StatusText = "No mapped gaze to save";
				return;
			}
			SaveFileDialog saveFileDialog = new SaveFileDialog()
			{
				Title = "Save gaze locations",
				InitialFileName = "gaze locations.npy",
				Filters = { new FileDialogFilter() { Name = "Numpy File (*.npy)", Extensions = { "npy" } } }
			};
			string? fileName = await saveFileDialog.ShowAsync(MainWindow);
			if (fileName == null)
				return;
			GazeLoader.Save(fileName, mappedGaze);
			StatusText = String.Format("Saved gaze to {0}", fileName);
		}

		/// <summary>
		/// Hands the mapped gaze and the sidebar frame rate to the Stimulus &amp; Gaze tab and switches to it.
		/// </summary>
		public void SendToStimulusGaze()
		{
			if ((object)mappedGaze == null)
			{
				StatusText = "No mapped gaze to send";
				return;
			}
			if (StimulusGazeViewModel == null)
				return;
			StimulusGazeViewModel.LoadFromCalibration(mappedGaze, EyetrackingFPS);
			SwitchToStimulusGazeTab?.Invoke();
		}

		// == Calibration persistence ==

		public async void SaveCalibration()
		{
			if (calibrator.Parameters == null)
			{
				StatusText = "No mapping to save";
				return;
			}
			SaveFileDialog saveFileDialog = new SaveFileDialog()
			{
				Title = "Save calibration",
				InitialFileName = "calibration.xml",
				Filters = { new FileDialogFilter() { Name = "XML File (*.xml)", Extensions = { "xml" } } }
			};
			string? fileName = await saveFileDialog.ShowAsync(MainWindow);
			if (fileName == null)
				return;
			try
			{
				RecordSettings();
				calibrator.SaveParameters(fileName);
				StatusText = String.Format("Saved calibration to {0}", fileName);
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Saving calibration failed: {0}", exception.Message);
			}
		}

		/// <summary>
		/// Loads a saved calibration, restores its settings into the sidebar, and refits the mapping from its fixations.
		/// </summary>
		public async void LoadCalibration()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load calibration",
				Filters = { new FileDialogFilter() { Name = "XML File (*.xml)", Extensions = { "xml" } } }
			};
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);
			if (fileName == null || fileName.Length == 0)
				return;
			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = true;
			StatusText = "Loading calibration...";
			try
			{
				string path = fileName[0];
				await Task.Run(() => calibrator.LoadParameters(path));
				mappedGaze = null;
				RestoreSettings();
				ShowFitResults();
				UpdateStateMarkers();
				StatusText = String.Format("Loaded calibration from {0}", path);
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Loading calibration failed: {0}", exception.Message);
			}
			finally
			{
				IsProgressBarVisible = false;
				IsProgressBarIndeterminate = false;
			}
		}

		// == Calibration points ==

		/// <summary>
		/// Regenerates the 7 by 5 calibration grid in the 35 point presentation order from the stimulus size and DPI unscale factor.
		/// </summary>
		public void RegeneratePoints()
		{
			CalibrationParameters generated = new CalibrationParameters(
				CalibrationParameters.GeneratePoints(StimulusWidth, StimulusHeight, 7, 5, DPIUnscaleFactor),
				CalibrationParameters.CalibrationSequence35);
			CalibrationPoints.Clear();
			foreach (CalibrationIndex index in generated.calibrationSequence)
				CalibrationPoints.Add(generated.calibrationPoints[index.Index]);
			SaveCalibrationSettings();
			RefreshCalibrationFixations();
		}

		/// <summary>
		/// Redraws the aggregated pupil position of each valid fixation with its index and leave-one-out error, zoomed so
		/// the fixations' bounding box fills the canvas. Without fixations the canvas shows the video frame outline instead.
		/// </summary>
		public void ForceRedraw()
		{
			ShapesToDraw.Clear();
			List<CalibrationFixation> validFixations = CalibrationFixations.Where(fixation => fixation.IsValid).ToList();
			double scale = 1;
			double offsetX = 0;
			double offsetY = 0;
			if (validFixations.Count == 0)
			{
				CanvasWidth = VideoWidth;
				CanvasHeight = VideoHeight;
			}
			else
			{
				double minX = validFixations.Min(fixation => fixation.PupilX);
				double maxX = validFixations.Max(fixation => fixation.PupilX);
				double minY = validFixations.Min(fixation => fixation.PupilY);
				double maxY = validFixations.Max(fixation => fixation.PupilY);
				double rangeX = Math.Max(maxX - minX, 1);
				double rangeY = Math.Max(maxY - minY, 1);
				scale = CanvasLongSide / Math.Max(rangeX, rangeY);
				offsetX = CanvasMargin - minX * scale;
				offsetY = CanvasMargin - minY * scale;
				CanvasWidth = rangeX * scale + 2 * CanvasMargin;
				CanvasHeight = rangeY * scale + 2 * CanvasMargin;
			}
			ShapesToDraw.Add(new Rectangle()
			{
				Width = CanvasWidth,
				Height = CanvasHeight,
				StrokeThickness = 4,
				Stroke = new SolidColorBrush(Colors.DodgerBlue)
			});
			foreach (CalibrationFixation fixation in validFixations)
			{
				double x = fixation.PupilX * scale + offsetX;
				double y = fixation.PupilY * scale + offsetY;
				ShapesToDraw.Add(new Ellipse()
				{
					Width = 12,
					Height = 12,
					Fill = new SolidColorBrush(Colors.LimeGreen),
					RenderTransform = new TranslateTransform(x - 6, y - 6),
				});
				ShapesToDraw.Add(new TextBlock()
				{
					Text = Double.IsNaN(fixation.Error)
						? String.Format("{0}", fixation.Index + 1)
						: String.Format("{0}: {1:F1}", fixation.Index + 1, fixation.Error),
					FontSize = 16,
					Foreground = new SolidColorBrush(Colors.OrangeRed),
					RenderTransform = new TranslateTransform(x + 8, y - 10),
				});
			}
		}

		// == Helpers ==

		/// <summary>
		/// Replaces the kernel options with the names of the selected convention, ticking the reference's four kernels.
		/// </summary>
		private void RebuildKernelOptions()
		{
			List<string>? savedSelection = Settings.Current.CalibrationSelectedKernels;
			ObservableCollection<KernelOption> options = new ObservableCollection<KernelOption>();
			foreach (RBFKernel kernel in UseLegacyConvention ? RBFMapping.LegacyKernels : RBFMapping.NewKernels)
			{
				bool isSelected = savedSelection != null ? savedSelection.Contains(kernel.ToString()) : DefaultSelectedKernels.Contains(kernel);
				options.Add(new KernelOption(kernel, isSelected));
			}
			KernelOptions = options;
		}

		/// <summary>
		/// Lists the ticked kernels.
		/// </summary>
		/// <returns>The selected kernels in display order.</returns>
		private List<RBFKernel> GetSelectedKernels()
		{
			List<RBFKernel> kernels = KernelOptions.Where(option => option.IsSelected).Select(option => option.Kernel).ToList();
			if (kernels.Count == 0)
				throw new InvalidOperationException("Select at least one kernel");
			return kernels;
		}

		/// <summary>
		/// Reads the shape parameter from the sidebar.
		/// </summary>
		/// <returns>Null when the convention's default is used, otherwise the sidebar value.</returns>
		private double? GetEpsilon()
		{
			return UseDefaultEpsilon ? (double?)null : Epsilon;
		}

		/// <summary>
		/// Copies the calibration points into an array in presentation order.
		/// </summary>
		/// <returns>An (nPoints x 2) array of screen positions.</returns>
		private double[,] GetScreenPoints()
		{
			double[,] screenPoints = new double[CalibrationPoints.Count, 2];
			for (int point = 0; point < CalibrationPoints.Count; point++)
			{
				screenPoints[point, 0] = CalibrationPoints[point].X;
				screenPoints[point, 1] = CalibrationPoints[point].Y;
			}
			return screenPoints;
		}

		/// <summary>
		/// Resolves the calibration onset frame from the sidebar, from the timestamp when that option is selected.
		/// </summary>
		/// <returns>Zero-based frame index of the calibration onset.</returns>
		private int GetOnsetFrame()
		{
			if (UseTimestampForStart)
				PopulateStartFrameFromTimeStamp();
			else
				PopulateStartTimeStampFromFrame();
			return UseTimestampForStart ? PupilInfo.TimeStampToFrameNumber(CalibrationStartTimeStamp) : CalibrationStartFrame;
		}

		/// <summary>
		/// Collects the sidebar filter settings.
		/// </summary>
		/// <returns>Settings for the gaze filter.</returns>
		private GazeFilterSettings BuildFilterSettings()
		{
			return new GazeFilterSettings
			{
				IsEnabled = IsFilterEnabled,
				MedianFilterWindowSize = FilterWindowSize,
				FilterPupilSize = FilterPupilSize,
				EnableOutlierRemoval = EnableOutlierRemoval,
				OutlierThresholdX = OutlierThresholdX,
				OutlierThresholdY = OutlierThresholdY,
				OutlierThresholdRadius = OutlierThresholdRadius
			};
		}

		/// <summary>
		/// Builds the smoothness grid from the sidebar, spaced in log10 when requested and both bounds are positive.
		/// </summary>
		/// <returns>The smoothness values to evaluate.</returns>
		private double[] BuildSmoothnessGrid()
		{
			int count = Math.Max(1, SmoothnessCount);
			double[] grid = new double[count];
			if (count == 1)
			{
				grid[0] = SmoothnessMin;
				return grid;
			}
			bool useLog = LogSpaceSmoothness && SmoothnessMin > 0 && SmoothnessMax > 0;
			double start = useLog ? Math.Log10(SmoothnessMin) : SmoothnessMin;
			double end = useLog ? Math.Log10(SmoothnessMax) : SmoothnessMax;
			for (int i = 0; i < count; i++)
			{
				double value = start + (end - start) * i / (count - 1);
				grid[i] = useLog ? Math.Pow(10, value) : value;
			}
			return grid;
		}

		/// <summary>
		/// Builds an inclusive range of values.
		/// </summary>
		/// <param name="minimum">First value.</param>
		/// <param name="maximum">Upper bound, included when the step lands on it.</param>
		/// <param name="step">Spacing between values, must be positive.</param>
		/// <returns>The range values.</returns>
		private static double[] BuildRange(double minimum, double maximum, double step)
		{
			if (step <= 0)
				throw new InvalidOperationException("Search step must be positive");
			int count = Math.Max(1, (int)Math.Floor((maximum - minimum) / step + 1e-9) + 1);
			double[] range = new double[count];
			for (int i = 0; i < count; i++)
				range[i] = minimum + i * step;
			return range;
		}

		/// <summary>
		/// Finds the smallest entry of an error grid.
		/// </summary>
		/// <param name="errors">An (nSets x nKernels x nSmoothnesses) error array.</param>
		/// <param name="setIndex">Receives the set index of the minimum.</param>
		/// <param name="kernelIndex">Receives the kernel index of the minimum.</param>
		/// <param name="smoothnessIndex">Receives the smoothness index of the minimum.</param>
		/// <returns>The minimum error, infinite when no entry is finite.</returns>
		private static double FindMinimum(double[,,] errors, out int setIndex, out int kernelIndex, out int smoothnessIndex)
		{
			setIndex = 0;
			kernelIndex = 0;
			smoothnessIndex = 0;
			double best = Double.PositiveInfinity;
			for (int set = 0; set < errors.GetLength(0); set++)
				for (int kernel = 0; kernel < errors.GetLength(1); kernel++)
					for (int smoothness = 0; smoothness < errors.GetLength(2); smoothness++)
					{
						if (errors[set, kernel, smoothness] < best)
						{
							best = errors[set, kernel, smoothness];
							setIndex = set;
							kernelIndex = kernel;
							smoothnessIndex = smoothness;
						}
					}
			return best;
		}

		/// <summary>
		/// Writes the sidebar settings that produced the fixations into the calibrator's parameters so they are saved with the fit.
		/// </summary>
		private void RecordSettings()
		{
			GazeCalibrationParameters? parameters = calibrator.Parameters;
			if (parameters == null) return;
			parameters.UseTimestampForStart = UseTimestampForStart;
			parameters.CalibrationStartFrame = CalibrationStartFrame;
			parameters.CalibrationStartTimeStamp = CalibrationStartTimeStamp;
			parameters.CalibrationDelay = CalibrationDelay;
			parameters.CalibrationDuration = CalibrationDuration;
			parameters.PointDelay = PointDelay;
			parameters.EndSkip = EndSkip;
			parameters.EyetrackingFPS = EyetrackingFPS;
			parameters.VideoWidth = VideoWidth;
			parameters.VideoHeight = VideoHeight;
			parameters.BlinkConfidenceThreshold = BlinkConfidenceThreshold;
			parameters.FilterEnabled = IsFilterEnabled;
			parameters.FilterWindowSize = FilterWindowSize;
			parameters.FilterPupilSize = FilterPupilSize;
			parameters.EnableOutlierRemoval = EnableOutlierRemoval;
			parameters.OutlierThresholdX = OutlierThresholdX;
			parameters.OutlierThresholdY = OutlierThresholdY;
			parameters.OutlierThresholdRadius = OutlierThresholdRadius;
			parameters.UseVarianceThreshold = UseVarianceThreshold;
			parameters.VarianceThresholdPercentile = VarianceThresholdPercentile;
		}

		/// <summary>
		/// Copies the settings of a loaded calibration into the sidebar.
		/// </summary>
		private void RestoreSettings()
		{
			GazeCalibrationParameters? parameters = calibrator.Parameters;
			if (parameters == null) return;
			UseTimestampForStart = parameters.UseTimestampForStart;
			CalibrationStartFrame = parameters.CalibrationStartFrame;
			CalibrationStartTimeStamp = parameters.CalibrationStartTimeStamp;
			CalibrationDelay = parameters.CalibrationDelay;
			CalibrationDuration = parameters.CalibrationDuration;
			PointDelay = parameters.PointDelay;
			EndSkip = parameters.EndSkip;
			EyetrackingFPS = parameters.EyetrackingFPS;
			if (parameters.VideoWidth > 0 && parameters.VideoHeight > 0)
			{
				VideoWidth = parameters.VideoWidth;
				VideoHeight = parameters.VideoHeight;
			}
			BlinkConfidenceThreshold = parameters.BlinkConfidenceThreshold;
			IsFilterEnabled = parameters.FilterEnabled;
			FilterWindowSize = parameters.FilterWindowSize;
			FilterPupilSize = parameters.FilterPupilSize;
			EnableOutlierRemoval = parameters.EnableOutlierRemoval;
			OutlierThresholdX = parameters.OutlierThresholdX;
			OutlierThresholdY = parameters.OutlierThresholdY;
			OutlierThresholdRadius = parameters.OutlierThresholdRadius;
			UseVarianceThreshold = parameters.UseVarianceThreshold;
			VarianceThresholdPercentile = parameters.VarianceThresholdPercentile;
			areCalibrationSettingsDirty = false;
			UseLegacyConvention = parameters.UseLegacyConvention;
			UseDefaultEpsilon = false;
			Epsilon = parameters.Epsilon;
			CalibrationPoints.Clear();
			foreach (CalibrationFixation fixation in parameters.Fixations)
				CalibrationPoints.Add(new Point(fixation.ScreenX, fixation.ScreenY));
			SaveCalibrationSettings();
		}

		/// <summary>
		/// Shows the fitted hyperparameters, error, and fixations in the sidebar and on the canvas.
		/// </summary>
		private void ShowFitResults()
		{
			GazeCalibrationParameters? parameters = calibrator.Parameters;
			if (parameters == null) return;
			isMappingStale = false;
			areCalibrationSettingsDirty = false;
			CalibrationRMSError = String.Format("Leave-one-out RMS error: {0:F2} px", parameters.RMSError);
			IsRMSErrorHigh = parameters.RMSError > HighRMSErrorPixels;
			FittedKernel = parameters.Kernel.ToString();
			FittedSmoothness = String.Format("{0:G4}", parameters.Smoothness);
			FittedEpsilon = String.Format("{0:G4}", parameters.Epsilon);
			fixationRefreshCancellation?.Cancel();
			CalibrationFixations = new ObservableCollection<CalibrationFixation>(parameters.Fixations);
			ForceRedraw();
		}
	}
}
