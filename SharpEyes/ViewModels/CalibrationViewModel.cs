using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
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
			}
		} = null;

		// == State markers: red absent, green present; pupils to map is blue when only the calibration trace is available to map ==

		private static readonly IBrush AbsentMarkerBrush = new SolidColorBrush(Colors.Red);
		private static readonly IBrush PresentMarkerBrush = new SolidColorBrush(Colors.LimeGreen);
		private static readonly IBrush SameTraceMarkerBrush = new SolidColorBrush(Colors.DodgerBlue);

		public IBrush CalibrationPupilsMarkerBrush => (object)PupilInfo != null ? PresentMarkerBrush : AbsentMarkerBrush;

		public IBrush MappingMarkerBrush => calibrator.IsFitted ? PresentMarkerBrush : AbsentMarkerBrush;

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
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0.6;

		// == Calibration timing ==

		public bool UseTimestampForStart
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;

		public int CalibrationStartFrame
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0;

		public string CalibrationStartTimeStamp
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = "0:00:00.000";

		public double CalibrationDuration
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 2.0;

		public double CalibrationDelay
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 2.0;

		public double PointDelay
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0.167;

		public double EndSkip
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0;

		public int EyetrackingFPS
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
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
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;

		public int FilterWindowSize
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 15;

		public bool FilterPupilSize
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;

		public bool EnableOutlierRemoval
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;

		public double OutlierThresholdX
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 95;

		public double OutlierThresholdY
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 95;

		public double OutlierThresholdRadius
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 95;

		public bool UseVarianceThreshold
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;

		public double VarianceThresholdPercentile
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
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

		public ObservableCollection<CalibrationNode> CalibrationNodes
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new ObservableCollection<CalibrationNode>();

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

		// Canvas size in drawing units; the drawing is zoomed to the bounding box of the nodes once there are any
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
			RebuildKernelOptions();

			CalibrationParameters defaults = CalibrationParameters.GetDefault35PointCalibrationParameters();
			foreach (CalibrationIndex index in defaults.calibrationSequence)
				CalibrationPoints.Add(defaults.calibrationPoints[index.Index]);

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
		/// Aggregates the calibration nodes from the loaded pupils with the sidebar settings, selects the kernel and
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

				CalibrationNode[] nodes = Array.Empty<CalibrationNode>();
				await Task.Run(() =>
				{
					NDArray processed = GazeCalibrator.PreprocessPupils(pupils, blinkThreshold, filterSettings);
					nodes = GazeCalibrator.AggregateCalibrationNodes(processed, screenPoints, onsetFrame, delay, duration, pointDelay, endSkip, fps);
					if (variancePercentile.HasValue)
						GazeCalibrator.ApplyVarianceThreshold(nodes, variancePercentile.Value);
					double[,,] stack = new double[1, nodes.Length, 2];
					for (int node = 0; node < nodes.Length; node++)
					{
						stack[0, node, 0] = nodes[node].PupilX;
						stack[0, node, 1] = nodes[node].PupilY;
					}
					double[,,] errors = RBFMapping.EvaluateHyperparameterGrid(stack, screenPoints, kernels, smoothnesses, useLegacy, epsilon);
					FindMinimum(errors, out int combinationIndex, out int kernelIndex, out int smoothnessIndex);
					calibrator.Fit(nodes, kernels[kernelIndex], smoothnesses[smoothnessIndex], useLegacy, epsilon);
				});
				mappedGaze = null;
				RecordSettings();
				ShowFitResults();
				UpdateStateMarkers();
				StatusText = String.Format("Mapping computed with {0} of {1} points", nodes.Count(node => node.IsValid), nodes.Length);
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
		/// Loads a saved calibration, restores its settings into the sidebar, and refits the mapping from its nodes.
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
		}

		/// <summary>
		/// Redraws the aggregated pupil position of each valid node with its index and leave-one-out error, zoomed so
		/// the nodes' bounding box fills the canvas. Without nodes the canvas shows the video frame outline instead.
		/// </summary>
		public void ForceRedraw()
		{
			ShapesToDraw.Clear();
			List<CalibrationNode> validNodes = CalibrationNodes.Where(node => node.IsValid).ToList();
			double scale = 1;
			double offsetX = 0;
			double offsetY = 0;
			if (validNodes.Count == 0)
			{
				CanvasWidth = VideoWidth;
				CanvasHeight = VideoHeight;
			}
			else
			{
				double minX = validNodes.Min(node => node.PupilX);
				double maxX = validNodes.Max(node => node.PupilX);
				double minY = validNodes.Min(node => node.PupilY);
				double maxY = validNodes.Max(node => node.PupilY);
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
			foreach (CalibrationNode node in validNodes)
			{
				double x = node.PupilX * scale + offsetX;
				double y = node.PupilY * scale + offsetY;
				ShapesToDraw.Add(new Ellipse()
				{
					Width = 12,
					Height = 12,
					Fill = new SolidColorBrush(Colors.LimeGreen),
					RenderTransform = new TranslateTransform(x - 6, y - 6),
				});
				ShapesToDraw.Add(new TextBlock()
				{
					Text = String.Format("{0}: {1:F1}", node.Index + 1, node.Error),
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
			ObservableCollection<KernelOption> options = new ObservableCollection<KernelOption>();
			foreach (RBFKernel kernel in UseLegacyConvention ? RBFMapping.LegacyKernels : RBFMapping.NewKernels)
				options.Add(new KernelOption(kernel, DefaultSelectedKernels.Contains(kernel)));
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
		/// Writes the sidebar settings that produced the nodes into the calibrator's parameters so they are saved with the fit.
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
			UseLegacyConvention = parameters.UseLegacyConvention;
			UseDefaultEpsilon = false;
			Epsilon = parameters.Epsilon;
			CalibrationPoints.Clear();
			foreach (CalibrationNode node in parameters.Nodes)
				CalibrationPoints.Add(new Point(node.ScreenX, node.ScreenY));
		}

		/// <summary>
		/// Shows the fitted hyperparameters, error, and nodes in the sidebar and on the canvas.
		/// </summary>
		private void ShowFitResults()
		{
			GazeCalibrationParameters? parameters = calibrator.Parameters;
			if (parameters == null) return;
			CalibrationRMSError = String.Format("Leave-one-out RMS error: {0:F2} px", parameters.RMSError);
			IsRMSErrorHigh = parameters.RMSError > HighRMSErrorPixels;
			FittedKernel = parameters.Kernel.ToString();
			FittedSmoothness = String.Format("{0:G4}", parameters.Smoothness);
			FittedEpsilon = String.Format("{0:G4}", parameters.Epsilon);
			CalibrationNodes = new ObservableCollection<CalibrationNode>(parameters.Nodes);
			ForceRedraw();
		}
	}
}
