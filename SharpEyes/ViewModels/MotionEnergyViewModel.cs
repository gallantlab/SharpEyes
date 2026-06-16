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
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mat = OpenCvSharp.Mat;
using NumSharp;
using Num = NumSharp.np;
using ReactiveUI;
using SharpEyes.Models;
using Eyetracking;

namespace SharpEyes.ViewModels
{
	public enum NormalizationScheme
	{
		PerFilter,
		Global,
		Percentile,
		Logarithmic
	}

	public enum MissingGazeTreatment
	{
		Zeros,
		NaN,
		DoNothing
	}

	public class PyramidCircleOverlay
	{
		public double Left { get; set; }
		public double Top { get; set; }
		public double Diameter { get; set; }
		public double StrokeThickness { get; set; }
		// Dynamic-overlay fields: the indices of every filter at this spatial
		// location (used to average their responses), the base color before
		// opacity is applied, and the opacity computed for the current frame.
		public List<int> FilterIndices { get; set; }
		public Color BaseColor { get; set; }
		public double CurrentOpacity { get; set; } = 1.0;
		// Cached pen carrying the base color at the current opacity, rebuilt only when the
		// opacity changes so that rendering allocates no brush or pen per element per frame.
		public IPen Pen { get; private set; }

		/// <summary>
		/// Rebuilds the cached pen from the base color, the opacity for the current frame,
		/// and the stroke thickness. Must be called after the opacity changes.
		/// </summary>
		public void RefreshPen()
		{
			Pen = PyramidOverlayPen.Build(BaseColor, CurrentOpacity, StrokeThickness);
		}
	}

	public class PyramidArrowOverlay
	{
		public double CanvasLeft { get; set; }
		public double CanvasTop { get; set; }
		public Geometry Geometry { get; set; }
		public double StrokeThickness { get; set; }
		// Dynamic-overlay fields: the index of the single filter this spoke line
		// draws from, the base color before opacity is applied, and the opacity
		// computed for the current frame.
		public int FilterIndex { get; set; }
		public Color BaseColor { get; set; }
		public double CurrentOpacity { get; set; } = 1.0;
		// Cached pen carrying the base color at the current opacity, rebuilt only when the
		// opacity changes so that rendering allocates no brush or pen per element per frame.
		public IPen Pen { get; private set; }

		/// <summary>
		/// Rebuilds the cached pen from the base color, the opacity for the current frame,
		/// and the stroke thickness. Must be called after the opacity changes.
		/// </summary>
		public void RefreshPen()
		{
			Pen = PyramidOverlayPen.Build(BaseColor, CurrentOpacity, StrokeThickness);
		}
	}

	/// <summary>
	/// Builds immutable pens for the motion-energy overlay elements. The pens are immutable
	/// so they can be cached on each element and reused across renders, which avoids
	/// allocating a brush and pen for every element on every frame.
	/// </summary>
	internal static class PyramidOverlayPen
	{
		/// <summary>
		/// Builds an immutable pen carrying a base color at a given opacity and thickness.
		/// </summary>
		/// <param name="baseColor">The element's base color before opacity is applied.</param>
		/// <param name="opacity">The opacity for the current frame, clamped into the range zero to one.</param>
		/// <param name="strokeThickness">The stroke thickness of the element's outline.</param>
		/// <returns>An immutable pen carrying the base color at the requested opacity.</returns>
		public static ImmutablePen Build(Color baseColor, double opacity, double strokeThickness)
		{
			byte alpha = (byte)(Math.Clamp(opacity, 0.0, 1.0) * 255);
			Color color = new Color(alpha, baseColor.R, baseColor.G, baseColor.B);
			return new ImmutablePen(new ImmutableSolidColorBrush(color), strokeThickness);
		}
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
				this.RaisePropertyChanged("CanvasWidth");
				this.RaisePropertyChanged("ImageLeft");
				this.RaisePropertyChanged("ImageWidth");
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
				this.RaisePropertyChanged("CanvasHeight");
				this.RaisePropertyChanged("ImageTop");
				this.RaisePropertyChanged("ImageHeight");
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

		private bool _isPreview = false;
		public bool IsPreview
		{
			get => _isPreview;
			set
			{
				this.RaiseAndSetIfChanged(ref _isPreview, value);
				_updateDisplayDelegate = value ? UpdateDisplayRecenteredPreview : UpdateDisplayRecentered;
				this.RaisePropertyChanged("ImageLeft");
				this.RaisePropertyChanged("ImageTop");
				this.RaisePropertyChanged("ImageWidth");
				this.RaisePropertyChanged("ImageHeight");
				if (_videoReader != null) UpdateDisplay();
			}
		}

		private double _gazeX = 0;
		public double GazeX
		{
			get => _gazeX;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeX, value);
				this.RaisePropertyChanged("ImageLeft");
			}
		}

		private double _gazeY = 0;
		public double GazeY
		{
			get => _gazeY;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeY, value);
				this.RaisePropertyChanged("ImageTop");
			}
		}

		public int CanvasWidth => (int)(VideoWidth * _padPercent / 100.0);
		public int CanvasHeight => (int)(VideoHeight * _padPercent / 100.0);
		public double ImageLeft   => _isPreview ? 0.0 : VideoWidth * _padPercent / 100.0 / 2.0 - GazeX * (double)VideoWidth / _gazeSpaceWidth;
		public double ImageTop    => _isPreview ? 0.0 : VideoHeight * _padPercent / 100.0 / 2.0 - GazeY * (double)VideoHeight / _gazeSpaceHeight;
		public int    ImageWidth  => _isPreview ? CanvasWidth  : VideoWidth;
		public int    ImageHeight => _isPreview ? CanvasHeight : VideoHeight;

		// == Video output parameters ==

		private double _padPercent = 200;
		public double PadPercent
		{
			get => _padPercent;
			set
			{
				this.RaiseAndSetIfChanged(ref _padPercent, value);
				this.RaisePropertyChanged("CanvasWidth");
				this.RaisePropertyChanged("CanvasHeight");
				this.RaisePropertyChanged("ImageLeft");
				this.RaisePropertyChanged("ImageTop");
				this.RaisePropertyChanged("OutputFrameSizeText");
				SaveSettings();
				MarkFilterResponsesStale();
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
				MarkFilterResponsesStale();
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
				MarkFilterResponsesStale();
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
			set
			{
				this.RaiseAndSetIfChanged(ref _videoFps, value);
				MarkFilterResponsesStale();
			}
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

		private bool _isFrameParametersExpanded;
		public bool IsFrameParametersExpanded
		{
			get => _isFrameParametersExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isFrameParametersExpanded, value);
				SaveSettings();
			}
		}

		private bool _isPyramidExpanded;
		public bool IsPyramidExpanded
		{
			get => _isPyramidExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isPyramidExpanded, value);
				SaveSettings();
			}
		}

		private bool _isSpatialFrequenciesExpanded;
		public bool IsSpatialFrequenciesExpanded
		{
			get => _isSpatialFrequenciesExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isSpatialFrequenciesExpanded, value);
				this.RaisePropertyChanged("SpatialFrequenciesHeaderText");
				SaveSettings();
			}
		}

		private bool _isTemporalFrequenciesExpanded;
		public bool IsTemporalFrequenciesExpanded
		{
			get => _isTemporalFrequenciesExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isTemporalFrequenciesExpanded, value);
				this.RaisePropertyChanged("TemporalFrequenciesHeaderText");
				SaveSettings();
			}
		}

		private bool _isDirectionsExpanded;
		public bool IsDirectionsExpanded
		{
			get => _isDirectionsExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isDirectionsExpanded, value);
				this.RaisePropertyChanged("DirectionsHeaderText");
				SaveSettings();
			}
		}

		private bool _isComputeFeaturesExpanded;
		public bool IsComputeFeaturesExpanded
		{
			get => _isComputeFeaturesExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isComputeFeaturesExpanded, value);
				SaveSettings();
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
				_ = UpdateOverlay();
			}
		}

		public ObservableCollection<PyramidCircleOverlay> PyramidCircles { get; } = new ObservableCollection<PyramidCircleOverlay>();
		public ObservableCollection<PyramidArrowOverlay> PyramidArrows { get; } = new ObservableCollection<PyramidArrowOverlay>();

		// The constant opacity applied to every overlay element when its opacity is not
		// being driven by the filter responses. Matches the alpha of the former static
		// overlay brush.
		private const double StaticOverlayOpacity = 200.0 / 255.0;

		// == Dynamic overlay ==

		// Incremented whenever dynamic-overlay opacities change so the overlay
		// control re-renders without a collection-changed notification.
		private int _overlayRenderTrigger = 0;
		public int OverlayRenderTrigger
		{
			get => _overlayRenderTrigger;
			set => this.RaiseAndSetIfChanged(ref _overlayRenderTrigger, value);
		}

		// When true, the overlay opacities correspond to the filter responses at each frame
		private bool showDynamicOverlay = false;
		public bool ShowDynamicOverlay
		{
			get => showDynamicOverlay;
			set
			{
				this.RaiseAndSetIfChanged(ref showDynamicOverlay, value);
				bool overlayBuilt = PyramidArrows.Count > 0 || PyramidCircles.Count > 0;
				bool shouldShowOverlay = _showMotionEnergyPyramid || showDynamicOverlay;

				if (value && _perFilterPercentile == null)
					ComputeNormalizationStatistics();
					
				if (overlayBuilt && shouldShowOverlay)
				{
					// The overlay geometry already exists, so only its opacity mode changes.
					if (showDynamicOverlay)
						UpdateDynamicOpacities();
					else
						ApplyStaticOpacity();
				}
				else
				{
					// The overlay must be built because it became visible, or cleared
					// because it is no longer visible.
					_ = UpdateOverlay(false);
				}
			}
		}

		// True once filter responses have been computed at least once.
		private bool _hasFilterResponses = false;
		// True when filter parameters have changed since filter responses were last
		// computed, making the dynamic overlay stale.
		private bool _areFilterResponsesStale = false;

		// The dynamic overlay can be shown only when fresh filter responses exist.
		public bool CanShowDynamicOverlay => _hasFilterResponses && !_areFilterResponsesStale;
		// True when stale filter responses exist, used to prompt the user to recompute.
		public bool IsDynamicOverlayStale => _hasFilterResponses && _areFilterResponsesStale;

		public ObservableCollection<string> NormalizationSchemeNames { get; } = new ObservableCollection<string>
		{
			"Per-filter", "Global", "Percentile", "Logarithmic"
		};

		private int _selectedNormalizationSchemeIndex = 0;
		public int SelectedNormalizationSchemeIndex
		{
			get => _selectedNormalizationSchemeIndex;
			set
			{
				this.RaiseAndSetIfChanged(ref _selectedNormalizationSchemeIndex, value);
				UpdateDynamicOpacities();
			}
		}

		private NormalizationScheme SelectedNormalizationScheme => (NormalizationScheme)_selectedNormalizationSchemeIndex;

		private double _dynamicBaseAlpha = 0.9;
		public double DynamicBaseAlpha
		{
			get => _dynamicBaseAlpha;
			set
			{
				this.RaiseAndSetIfChanged(ref _dynamicBaseAlpha, value);
				UpdateDynamicOpacities();
			}
		}

		// Normalization statistics, computed once when filter responses are retained.
		private int _filterResponsesStartFrame = 0;
		private float _globalMax = 0;
		private float[]? _perFilterMax = null;
		private float[]? _perFilterPercentile = null;

		// Row-major copy of the retained filter responses (frames x filters), read
		// once per displayed frame to drive the dynamic-overlay opacities. Caching a
		// flat array lets each frame index directly by offset instead of re-slicing
		// the NDArray and allocating a row array, which matters when the pyramid
		// contains thousands of filters. Rebuilt when responses are retained and
		// cleared when they are discarded.
		private float[]? _flatFilterResponses = null;
		private int _filterResponseColumnCount = 0;
		private int _filterResponseRowCount = 0;

		public ReactiveCommand<Unit, Unit> ComputePyramidCommand { get; }
		public ReactiveCommand<Unit, Unit> SetAllFiltersCommand { get; }

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

		// batch over motion-energy filters?
		private bool batchFilters = false;
		public bool BatchFilters
		{
			get => batchFilters;
			set
			{
				this.RaiseAndSetIfChanged(ref batchFilters, value);
				SaveSettings();
			}
		}

		private int _filterBatchSize = 128;
		public int FilterBatchSize
		{
			get => _filterBatchSize;
			set
			{
				this.RaiseAndSetIfChanged(ref _filterBatchSize, value);
				SaveSettings();
			}
		}
		
		// batch over stimulus frames?
		private bool batchFrames = false;
		public bool BatchFrames
		{
			get => batchFrames;
			set
			{
				this.RaiseAndSetIfChanged(ref batchFrames, value);
				SaveSettings();
			}
		}
		
		private int _frameBatchSize = 128;
		public int FrameBatchSize
		{
			get => _frameBatchSize;
			set
			{
				this.RaiseAndSetIfChanged(ref _frameBatchSize, value);
				SaveSettings();
			}
		}

		private bool _framesInCPU = false;
		public bool FramesInCPU
		{
			get => _framesInCPU;
			set
			{
				this.RaiseAndSetIfChanged(ref _framesInCPU, value);
				SaveSettings();
			}
		}

		private bool _responsesInCPU = false;
		public bool ResponsesInCPU
		{
			get => _responsesInCPU;
			set
			{
				this.RaiseAndSetIfChanged(ref _responsesInCPU, value);
				SaveSettings();
			}
		}

		public ObservableCollection<string> OutputDtypeNames { get; } = new ObservableCollection<string>
		{
			"float16", "float32", "float64"
		};

		private int _selectedOutputDtypeIndex = 1;
		public int SelectedOutputDtypeIndex
		{
			get => _selectedOutputDtypeIndex;
			set
			{
				this.RaiseAndSetIfChanged(ref _selectedOutputDtypeIndex, value);
				SaveSettings();
			}
		}

		public string SelectedOutputDtype =>
			_selectedOutputDtypeIndex >= 0 && _selectedOutputDtypeIndex < OutputDtypeNames.Count
				? OutputDtypeNames[_selectedOutputDtypeIndex]
				: "float32";

		public ObservableCollection<string> MissingGazeTreatmentNames { get; } = new ObservableCollection<string>
		{
			"Zeros", "NaN", "Do nothing"
		};

		private int _selectedMissingGazeTreatmentIndex = 0;
		public int SelectedMissingGazeTreatmentIndex
		{
			get => _selectedMissingGazeTreatmentIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedMissingGazeTreatmentIndex, value);
		}

		private MissingGazeTreatment SelectedMissingGazeTreatment => (MissingGazeTreatment)_selectedMissingGazeTreatmentIndex;

		public ReactiveCommand<Unit, Unit> ComputeFeaturesCommand { get; }
		public ReactiveCommand<Unit, Unit> LoadSavedFeaturesCommand { get; }

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
			_isFrameParametersExpanded = settings.MotionEnergyFrameParametersExpanded;
			_isPyramidExpanded = settings.MotionEnergyPyramidExpanded;
			_isSpatialFrequenciesExpanded = settings.MotionEnergySpatialFrequenciesExpanded;
			_isTemporalFrequenciesExpanded = settings.MotionEnergyTemporalFrequenciesExpanded;
			_isDirectionsExpanded = settings.MotionEnergyDirectionsExpanded;
			_isComputeFeaturesExpanded = settings.MotionEnergyComputeFeaturesExpanded;
			batchFilters = settings.MotionEnergyUseFilterBatching;
			_filterBatchSize = settings.MotionEnergyFilterBatchSize;
			batchFrames = settings.MotionEnergyBatchFrames;
			_frameBatchSize = settings.MotionEnergyFrameBatchSize;
			_framesInCPU = settings.MotionEnergyFramesInCPU;
			_responsesInCPU = settings.MotionEnergyResponsesInCPU;
			int savedDtypeIndex = OutputDtypeNames.IndexOf(settings.MotionEnergyOutputDtype);
			_selectedOutputDtypeIndex = savedDtypeIndex >= 0 ? savedDtypeIndex : 1;
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
			_updateDisplayDelegate = UpdateDisplayRecentered;
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
			SetAllFiltersCommand = ReactiveCommand.CreateFromTask(() => SetAllFilters());
			ComputeFeaturesCommand = ReactiveCommand.Create(() =>
			{
				if (_isComputingFeatures)
					_computeFeaturesTokenSource?.Cancel();
				else
					_ = ComputeFeatures();
			});
			LoadSavedFeaturesCommand = ReactiveCommand.Create(LoadSavedFeatures);
			SpatialFrequencies.CollectionChanged += (s, e) => { this.RaisePropertyChanged("SpatialFrequenciesHeaderText"); SaveSettings(); CommitFilterParameterChange(true); };
			TemporalFrequencies.CollectionChanged += (s, e) => { this.RaisePropertyChanged("TemporalFrequenciesHeaderText"); SaveSettings(); CommitFilterParameterChange(true); };
			Directions.CollectionChanged += (s, e) => { this.RaisePropertyChanged("DirectionsHeaderText"); SaveSettings(); CommitFilterParameterChange(true); };
		}

		private void RestoreVideoDefaults()
		{
			PadPercent = 200;
			PadValue = 0.1;
			FrameScale = 0.125;
			CommitFilterParameterChange(true);
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
			settings.MotionEnergyFrameParametersExpanded = _isFrameParametersExpanded;
			settings.MotionEnergyPyramidExpanded = _isPyramidExpanded;
			settings.MotionEnergySpatialFrequenciesExpanded = _isSpatialFrequenciesExpanded;
			settings.MotionEnergyTemporalFrequenciesExpanded = _isTemporalFrequenciesExpanded;
			settings.MotionEnergyDirectionsExpanded = _isDirectionsExpanded;
			settings.MotionEnergyComputeFeaturesExpanded = _isComputeFeaturesExpanded;
			settings.MotionEnergyUseFilterBatching = batchFilters;
			settings.MotionEnergyFilterBatchSize = _filterBatchSize;
			settings.MotionEnergyBatchFrames = batchFrames;
			settings.MotionEnergyFrameBatchSize = _frameBatchSize;
			settings.MotionEnergyFramesInCPU = _framesInCPU;
			settings.MotionEnergyResponsesInCPU = _responsesInCPU;
			settings.MotionEnergyOutputDtype = SelectedOutputDtype;
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
			_updateDisplayDelegate = _isPreview ? UpdateDisplayRecenteredPreview : UpdateDisplayRecentered;
			ResetDynamicState();
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
			_updateDisplayDelegate = _isPreview ? UpdateDisplayRecenteredPreview : UpdateDisplayRecentered;
			ResetDynamicState();
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

		/// <summary>
		/// Builds a mapping from each unique spatial envelope size to a stroke-thickness
		/// rank. The envelopes are sorted ascending and assigned ranks starting at one, so
		/// that larger circles receive proportionally thicker strokes. Callers multiply the
		/// rank by their own scale factor.
		/// </summary>
		/// <returns>A dictionary keyed by spatial envelope, valued by the one-based rank of that envelope among the sorted unique envelopes.</returns>
		private Dictionary<double, double> BuildStrokeThicknessBySize()
		{
			List<double> uniqueSpatialEnvelopes = new List<double>();
			foreach (MotionEnergyFilterParameters filter in motionEnergyFeatures.FilterParameters)
			{
				if (!uniqueSpatialEnvelopes.Contains(filter.SpatialEnvelope))
					uniqueSpatialEnvelopes.Add(filter.SpatialEnvelope);
			}
			uniqueSpatialEnvelopes.Sort();

			Dictionary<double, double> strokeThicknessBySize = new Dictionary<double, double>();
			for (int index = 0; index < uniqueSpatialEnvelopes.Count; index++)
				strokeThicknessBySize[uniqueSpatialEnvelopes[index]] = (index * 2) + 1;
			return strokeThicknessBySize;
		}

		/// <summary>
		/// Builds the stream geometry and stroke thickness for one direction spoke of the
		/// overlay. Within a direction group the filters are ordered ascending by temporal
		/// frequency, and slower filters are drawn as thicker and shorter lines so the spoke
		/// weight and length encode the temporal frequency.
		/// </summary>
		/// <param name="centerX">Horizontal center of the filter's circle in canvas coordinates.</param>
		/// <param name="centerY">Vertical center of the filter's circle in canvas coordinates.</param>
		/// <param name="radius">Radius of the filter's circle in canvas coordinates.</param>
		/// <param name="dx">Horizontal component of the spoke's unit direction vector.</param>
		/// <param name="dy">Vertical component of the spoke's unit direction vector.</param>
		/// <param name="rank">Index of this filter within its direction group after sorting ascending by temporal frequency.</param>
		/// <param name="directionFilterCount">Number of filters sharing this direction at this location.</param>
		/// <param name="spokeFloorThickness">Stroke thickness of the fastest filter's spoke, before temporal scaling.</param>
		/// <param name="canvasLeft">Outputs the left edge of the spoke geometry's bounding box in canvas coordinates.</param>
		/// <param name="canvasTop">Outputs the top edge of the spoke geometry's bounding box in canvas coordinates.</param>
		/// <param name="strokeThickness">Outputs the stroke thickness for this spoke after temporal scaling.</param>
		/// <returns>The stream geometry for the spoke, positioned relative to its bounding box.</returns>
		private StreamGeometry BuildSpokeGeometry(double centerX, double centerY, double radius, double dx, double dy, int rank, int directionFilterCount, double spokeFloorThickness, out double canvasLeft, out double canvasTop, out double strokeThickness)
		{
			double temporalThicknessMultiplier = 4.0;
			double temporalLengthMultiplier = 0.5;
			double spokeCeilingHalfLength = radius / 5.0;
			int stepsSlowerThanFastest = (directionFilterCount - 1) - rank;
			strokeThickness = spokeFloorThickness * Math.Pow(temporalThicknessMultiplier, stepsSlowerThanFastest);
			double spokeLength = 2 * spokeCeilingHalfLength * Math.Pow(temporalLengthMultiplier, stepsSlowerThanFastest);

			Point lineStart = new Point(centerX + (radius) * dx, centerY + (radius) * dy);
			Point lineEnd   = new Point(centerX + (radius + spokeLength) * dx, centerY + (radius + spokeLength) * dy);

			canvasLeft = Math.Min(lineStart.X, lineEnd.X);
			canvasTop  = Math.Min(lineStart.Y, lineEnd.Y);

			StreamGeometry spokeGeometry = new StreamGeometry();
			using (StreamGeometryContext streamContext = spokeGeometry.Open())
			{
				streamContext.BeginFigure(new Point(lineStart.X - canvasLeft, lineStart.Y - canvasTop), false);
				streamContext.LineTo(new Point(lineEnd.X - canvasLeft, lineEnd.Y - canvasTop));
				streamContext.EndFigure(false);
			}
			return spokeGeometry;
		}

		/// <summary>
		/// Builds the overlay geometry: one circle per spatial location and, at each
		/// location, one spoke line per filter grouped by direction. Within a direction the
		/// temporal frequencies are ordered ascending so that the fastest temporal frequency
		/// is thinnest and longest and each slower temporal frequency is thicker and shorter.
		/// Each element is tagged with the filter index (or indices) it draws from. Once the
		/// geometry exists the opacity is applied for the current mode: per frame from the
		/// filter responses when dynamic opacity is on, otherwise at the constant static
		/// opacity. Builds the pyramid first if required.
		/// </summary>
		/// <param name="rebuildPyramid">Whether the pyramid must be rebuilt: true when the change affects pyramid geometry, false for changes such as pad value that do not.</param>
		private async Task UpdateOverlay(bool rebuildPyramid = true)
		{
			PyramidCircles.Clear();
			PyramidArrows.Clear();
			if (!_showMotionEnergyPyramid && !showDynamicOverlay)
			{
				OverlayRenderTrigger++;
				return;
			}
			if (rebuildPyramid || motionEnergyFeatures.FilterCount < 1)
				await ComputePyramid(false);
			if (motionEnergyFeatures.FilterCount < 1) return;

			Color baseColor = Color.FromArgb(255, 255, 220, 0);
			double canvasHeight = CanvasHeight;

			Dictionary<double, double> strokeThicknessBySize = BuildStrokeThicknessBySize();

			Dictionary<(double, double, double), List<int>> filtersByLocation =
				new Dictionary<(double, double, double), List<int>>();
			for (int filterIndex = 0; filterIndex < motionEnergyFeatures.FilterParameters.Count; filterIndex++)
			{
				MotionEnergyFilterParameters filter = motionEnergyFeatures.FilterParameters[filterIndex];
				(double, double, double) key = (filter.CenterHorizontal, filter.CenterVertical, filter.SpatialEnvelope);
				if (!filtersByLocation.ContainsKey(key))
					filtersByLocation[key] = new List<int>();
				filtersByLocation[key].Add(filterIndex);
			}

			foreach (KeyValuePair<(double, double, double), List<int>> locationEntry in filtersByLocation)
			{
				double centerX = locationEntry.Key.Item1 * canvasHeight;
				double centerY = locationEntry.Key.Item2 * canvasHeight;
				double radius  = locationEntry.Key.Item3 * canvasHeight;

				PyramidCircles.Add(new PyramidCircleOverlay
				{
					Left            = centerX - radius,
					Top             = centerY - radius,
					Diameter        = 2 * radius,
					BaseColor       = baseColor,
					StrokeThickness = strokeThicknessBySize[locationEntry.Key.Item3] * 2,
					FilterIndices   = new List<int>(locationEntry.Value),
					CurrentOpacity  = 0
				});

				Dictionary<double, List<int>> filtersByDirection = new Dictionary<double, List<int>>();
				foreach (int filterIndex in locationEntry.Value)
				{
					double direction = motionEnergyFeatures.FilterParameters[filterIndex].Direction;
					if (!filtersByDirection.ContainsKey(direction))
						filtersByDirection[direction] = new List<int>();
					filtersByDirection[direction].Add(filterIndex);
				}

				foreach (KeyValuePair<double, List<int>> directionEntry in filtersByDirection)
				{
					double directionRadians = directionEntry.Key * Math.PI / 180.0;
					double dx = Math.Cos(directionRadians);
					double dy = -Math.Sin(directionRadians);

					List<int> directionFilters = directionEntry.Value;
					directionFilters.Sort((a, b) => motionEnergyFeatures.FilterParameters[a].TemporalFrequency
						.CompareTo(motionEnergyFeatures.FilterParameters[b].TemporalFrequency));
					int directionFilterCount = directionFilters.Count;

					double spokeFloorThickness = strokeThicknessBySize[locationEntry.Key.Item3];
					for (int rank = 0; rank < directionFilterCount; rank++)
					{
						int filterIndex = directionFilters[rank];
						double thickness;
						double canvasLeft;
						double canvasTop;
						StreamGeometry spokeGeometry = BuildSpokeGeometry(centerX, centerY, radius, dx, dy, rank, directionFilterCount, spokeFloorThickness, out canvasLeft, out canvasTop, out thickness);

						PyramidArrows.Add(new PyramidArrowOverlay
						{
							CanvasLeft      = canvasLeft,
							CanvasTop       = canvasTop,
							Geometry        = spokeGeometry,
							BaseColor       = baseColor,
							StrokeThickness = thickness,
							FilterIndex     = filterIndex,
							CurrentOpacity  = 0
						});
					}
				}
			}

			if (showDynamicOverlay)
				UpdateDynamicOpacities();
			else
				ApplyStaticOpacity();
		}

		/// <summary>
		/// Sets every overlay element to the constant static opacity, rebuilds each
		/// element's cached pen, and triggers a redraw. Used when the overlay is built with
		/// dynamic opacity off and when dynamic opacity is turned off.
		/// </summary>
		private void ApplyStaticOpacity()
		{
			foreach (PyramidArrowOverlay spoke in PyramidArrows)
			{
				spoke.CurrentOpacity = StaticOverlayOpacity;
				spoke.RefreshPen();
			}
			foreach (PyramidCircleOverlay circle in PyramidCircles)
			{
				circle.CurrentOpacity = StaticOverlayOpacity;
				circle.RefreshPen();
			}
			OverlayRenderTrigger++;
		}

		/// <summary>
		/// Marks the filter responses stale so the dynamic overlay is disabled until
		/// they are recomputed. Cheap; called immediately when a parameter value
		/// changes, before the change is committed.
		/// </summary>
		private void MarkFilterResponsesStale()
		{
			if (!_hasFilterResponses || _areFilterResponsesStale) return;
			_areFilterResponsesStale = true;
			this.RaisePropertyChanged(nameof(CanShowDynamicOverlay));
			this.RaisePropertyChanged(nameof(IsDynamicOverlayStale));
		}

		/// <summary>
		/// Commits a filter-parameter change. Called when a parameter control is
		/// accepted or loses focus, or on a discrete change such as adding or removing
		/// a frequency. Marks the responses stale, falls back from the dynamic overlay
		/// to the static pyramid overlay, and rebuilds whichever overlay is shown.
		/// </summary>
		/// <param name="rebuildPyramid">Whether the pyramid must be rebuilt: true when the change affects pyramid geometry, false for changes such as pad value that do not.</param>
		public void CommitFilterParameterChange(bool rebuildPyramid)
		{
			MarkFilterResponsesStale();
			if (showDynamicOverlay)
			{
				showDynamicOverlay = false;
				this.RaisePropertyChanged(nameof(ShowDynamicOverlay));
			}
			_ = UpdateOverlay(rebuildPyramid);
		}

		/// <summary>
		/// Clears all filter-response state and the dynamic overlay. Called when a new
		/// video or gaze is loaded, since previously computed responses no longer match
		/// the displayed content.
		/// </summary>
		private void ResetDynamicState()
		{
			_motionEnergyFeatures = null;
			_flatFilterResponses = null;
			_filterResponseColumnCount = 0;
			_filterResponseRowCount = 0;
			_hasFilterResponses = false;
			_areFilterResponsesStale = false;
			_globalMax = 0;
			_perFilterMax = null;
			_perFilterPercentile = null;
			if (showDynamicOverlay)
			{
				showDynamicOverlay = false;
				this.RaisePropertyChanged(nameof(ShowDynamicOverlay));
			}
			if (_showMotionEnergyPyramid)
			{
				ApplyStaticOpacity();
			}
			else
			{
				PyramidCircles.Clear();
				PyramidArrows.Clear();
				OverlayRenderTrigger++;
			}
			this.RaisePropertyChanged(nameof(CanShowDynamicOverlay));
			this.RaisePropertyChanged(nameof(IsDynamicOverlayStale));
		}

		/// <summary>
		/// Updates every dynamic-overlay element's opacity from the filter responses
		/// at the current video frame, computed on the fly. A spoke takes its own
		/// filter's normalized response; a circle takes the mean normalized response
		/// across all filters at its location. Increments the render trigger so the
		/// overlay control redraws.
		/// </summary>
		private void UpdateDynamicOpacities()
		{
			if (!_isOverlayOpacityDynamic) return;
			if (_flatFilterResponses == null) return;
			if (PyramidArrows.Count == 0 && PyramidCircles.Count == 0) return;

			int featureRow = Math.Clamp(CurrentVideoFrame - _filterResponsesStartFrame, 0, _filterResponseRowCount - 1);
			int rowOffset = featureRow * _filterResponseColumnCount;

			foreach (PyramidArrowOverlay spoke in PyramidArrows)
			{
				spoke.CurrentOpacity = _dynamicBaseAlpha * NormalizedOpacity(spoke.FilterIndex, rowOffset);
				spoke.RefreshPen();
			}

			foreach (PyramidCircleOverlay circle in PyramidCircles)
			{
				double opacitySum = 0;
				foreach (int filterIndex in circle.FilterIndices)
					opacitySum += NormalizedOpacity(filterIndex, rowOffset);
				double meanOpacity = circle.FilterIndices.Count > 0 ? opacitySum / circle.FilterIndices.Count : 0;
				circle.CurrentOpacity = _dynamicBaseAlpha * meanOpacity;
				circle.RefreshPen();
			}

			OverlayRenderTrigger++;
		}

		/// <summary>
		/// Maps a single filter's raw response at the current frame to an opacity in
		/// [0, 1] using the selected normalization scheme. A zero response maps to
		/// fully transparent.
		/// </summary>
		/// <param name="filterIndex">Index of the filter, matching the filter-response column and FilterParameters order.</param>
		/// <param name="rowOffset">Index into the cached flat filter-response array of the current frame's first filter column (i.e. featureRow times the filter count).</param>
		private double NormalizedOpacity(int filterIndex, int rowOffset)
		{
			float response = _flatFilterResponses![rowOffset + filterIndex];
			if (response <= 0) return 0;
			double opacity;
			switch (SelectedNormalizationScheme)
			{
				case NormalizationScheme.PerFilter:
					double filterMax = _perFilterMax != null ? _perFilterMax[filterIndex] : 0;
					opacity = filterMax > 0 ? response / filterMax : 0;
					break;
				case NormalizationScheme.Global:
					opacity = _globalMax > 0 ? response / _globalMax : 0;
					break;
				case NormalizationScheme.Percentile:
					double filterPercentile = _perFilterPercentile != null ? _perFilterPercentile[filterIndex] : 0;
					opacity = filterPercentile > 0 ? response / filterPercentile : 0;
					break;
				case NormalizationScheme.Logarithmic:
					double logMax = _perFilterMax != null ? _perFilterMax[filterIndex] : 0;
					opacity = logMax > 0 ? Math.Log(1 + response) / Math.Log(1 + logMax) : 0;
					break;
				default:
					opacity = 0;
					break;
			}
			return Math.Clamp(opacity, 0.0, 1.0);
		}

		/// <summary>
		/// Computes the per-filter maximum, global maximum, and per-filter 99th
		/// percentile of the retained filter-response array. Called once when filter
		/// responses are retained; the normalization schemes read these at display time.
		/// </summary>
		private void ComputeNormalizationStatistics()
		{
			if ((object)_motionEnergyFeatures == null)
			{
				_globalMax = 0;
				_perFilterMax = null;
				_perFilterPercentile = null;
				return;
			}

			NDArray columnMaxes = Num.amax(_motionEnergyFeatures, axis: 0);
			_perFilterMax = columnMaxes.ToArray<float>();
			_globalMax = (float)Num.amax(_motionEnergyFeatures);

			NDArray columnMeans = Num.mean(_motionEnergyFeatures, axis: 0);
			NDArray columnStds = Num.std(_motionEnergyFeatures, axis: 0);
			_perFilterPercentile = (columnMeans + 2.326 * columnStds).ToArray<float>();
		}

		/// <summary>
		/// Sets the filter batch size to the total number of filters in the pyramid,
		/// building the pyramid first if it has not yet been built.
		/// </summary>
		private async Task SetAllFilters()
		{
			if (motionEnergyFeatures.FilterCount < 1)
				await ComputePyramid(false);
			FilterBatchSize = motionEnergyFeatures.FilterCount;
		}

		/// <summary>
		/// Builds the motion-energy pyramid filter parameters on a background thread, updating
		/// the status text and, when it owns the progress bar, the progress bar while it runs.
		/// </summary>
		/// <param name="updateDisplay">Whether to rebuild the overlay from the new filter parameters once the pyramid is built.</param>
		/// <param name="manageProgressBar">Whether this method shows and hides the progress bar itself. Pass false when the pyramid build is one step of a larger operation that owns the progress bar, so the bar is not hidden before that operation finishes.</param>
		private async Task ComputePyramid(bool updateDisplay = true, bool manageProgressBar = true)
		{
			StatusText = "Building pyramid...";
			if (manageProgressBar) IsProgressBarVisible = true;
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
				if (updateDisplay) _ = UpdateOverlay();
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Error building pyramid: {0}", exception.Message);
			}
			finally
			{
				if (manageProgressBar) IsProgressBarVisible = false;
			}
		}

		/// <summary>
		/// Returns the zero-based row indices in the feature array for which no valid gaze
		/// data exists (i.e., gaze X or Y is NaN at the corresponding eyetracking sample).
		/// Uses the current gaze locations, data start frame, and FPS settings.
		/// </summary>
		/// <param name="startFrame">Video frame number corresponding to feature array row zero.</param>
		/// <param name="frameCount">Total number of rows in the feature array.</param>
		/// <returns>A list of row indices with missing gaze data.</returns>
		private List<int> FindMissingGazeFrameIndices(int startFrame, int frameCount)
		{
			List<int> missingIndices = new List<int>();
			if ((object)_gazeLocations == null) return missingIndices;
			int gazeRowCount = _gazeLocations.Shape[0];
			for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
			{
				int videoFrame = startFrame + frameIndex;
				int dataIndex = Math.Clamp(VideoTimeToDataIndex(videoFrame), 0, gazeRowCount - 1);
				double gazeX = (double)_gazeLocations[dataIndex, 0];
				double gazeY = (double)_gazeLocations[dataIndex, 1];
				if (Double.IsNaN(gazeX) || Double.IsNaN(gazeY))
					missingIndices.Add(frameIndex);
			}
			return missingIndices;
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
				// Phase 1+2: build recentered frames and pyramid in parallel
				StatusText = "Processing frames and building pyramid...";
				IsProgressBarIndeterminate = false;
				ProgressBarValue = 0;
				Recenterer recenterer = new Recenterer(
					_videoReader, _gazeLocations, _dataStartFrame.Value,
					false, _eyetrackingFPS, _gazeSpaceWidth, _gazeSpaceHeight,
					_frameScale, _padValue, false);
				IProgress<double> frameProgress = new Progress<double>(value => ProgressBarValue = value);
				Task<NDArray> framesTask = recenterer.BuildRecenteredFramesAsync(_startFrame, frameProgress, cancellationToken);
				Task pyramidTask = Task.Run(() =>
				{
					PythonEnvironmentManager.Instance.Initialize();
					motionEnergyFeatures.BuildPyramid(_videoFps);
				}, cancellationToken);
				try
				{
					await Task.WhenAll(framesTask, pyramidTask);
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception)
				{
					if (framesTask.IsFaulted)
						StatusText = String.Format("Error processing frames: {0}", framesTask.Exception!.InnerException!.Message);
					else
						StatusText = String.Format("Error building pyramid: {0}", pyramidTask.Exception!.InnerException!.Message);
					return;
				}
				NDArray frames = framesTask.Result;
				this.RaisePropertyChanged("ModelFrameWidth");
				this.RaisePropertyChanged("ModelFrameHeight");
				_ = UpdateOverlay();

				cancellationToken.ThrowIfCancellationRequested();

				// Phase 3: extract features (indeterminate progress)
				motionEnergyFeatures.Backend = SelectedBackendKey;
				motionEnergyFeatures.BatchFilters = batchFilters;
				motionEnergyFeatures.FilterBatchSize = _filterBatchSize;
				motionEnergyFeatures.OutputDtype = SelectedOutputDtype;
				motionEnergyFeatures.FrameBatchSize = batchFrames ? _frameBatchSize : null;
				motionEnergyFeatures.FramesInCPU = _framesInCPU;
				motionEnergyFeatures.ResponsesInCPU = _responsesInCPU;
				StatusText = "Computing motion energy...";
				IsProgressBarIndeterminate = true;
				NDArray features;
				
				IProgress<double> extractProgress = new Progress<double>(_ => { });
				features = await motionEnergyFeatures.ExtractAsync(frames, extractProgress);

				// Apply missing-gaze treatment
				if (SelectedMissingGazeTreatment != MissingGazeTreatment.DoNothing)
				{
					List<int> missingFrameIndices = FindMissingGazeFrameIndices(_startFrame, features.Shape[0]);
					if (missingFrameIndices.Count > 0)
					{
						bool fillWithNaN = SelectedMissingGazeTreatment == MissingGazeTreatment.NaN;
						motionEnergyFeatures.FillMissingFrames(missingFrameIndices, fillWithNaN);
						features[new NDArray(missingFrameIndices.ToArray())] = fillWithNaN ? float.NaN : 0.0f;
					}
				}

				_motionEnergyFeatures = features;
				_filterResponseRowCount = features.Shape[0];
				_filterResponseColumnCount = features.Shape[1];
				_flatFilterResponses = features.ToArray<float>();
				_filterResponsesStartFrame = _startFrame;
				await Task.Run(() => ComputeNormalizationStatistics());
				_hasFilterResponses = true;
				_areFilterResponsesStale = false;
				this.RaisePropertyChanged(nameof(CanShowDynamicOverlay));
				this.RaisePropertyChanged(nameof(IsDynamicOverlayStale));
				UpdateDynamicOpacities();
				IsProgressBarIndeterminate = false;
				IsProgressBarVisible = false;

				// Save to disk
				SaveFileDialog saveDialog = new SaveFileDialog() { Title = "Save motion energy features" };
				saveDialog.Filters.Add(new FileDialogFilter() { Name = "NumPy arrays", Extensions = { "npy" } });
				saveDialog.InitialFileName = System.IO.Path.GetFileNameWithoutExtension(_videoReader.videoFileName) + " motion energy.npy";
				string? savePath = await saveDialog.ShowAsync(MainWindow);
				if (savePath != null)
				{
					await Task.Run(() => motionEnergyFeatures.SaveFeatures(savePath));

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
					textContent.AppendLine(String.Format("  Compute dtype: {0}", SelectedOutputDtype));

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
			catch (Exception exception)
			{
				StatusText = String.Format("Error computing motion energy: {0}", exception.Message);
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

		/// <summary>
		/// Holds the metadata parsed from a saved "... info.txt" file, used to
		/// reconstruct the full motion-energy state when loading saved features.
		/// </summary>
		private class LoadedFeatureMetadata
		{
			public string? VideoPath = null;
			public string? GazePath = null; // null when no gaze file was used
			public int EyetrackingFPS = 60;
			public int DataStartFrame = 0;
			public int GazeSpaceWidth = 1024;
			public int GazeSpaceHeight = 768;
			public GazeFilterSettings GazeFilter = new GazeFilterSettings();
			public double PadPercent = 200;
			public double PadValue = 0.1;
			public double FrameScale = 0.125;
			public double VideoFps = 30;
			public List<double> SpatialFrequencies = new List<double>();
			public List<double> TemporalFrequencies = new List<double>();
			public List<double> Directions = new List<double>();
			public int StartFrame = 0;
			public string OutputDtype = "float32";
		}

		/// <summary>
		/// Parses a saved "... info.txt" file. Each value is read by matching the
		/// known line prefix and taking everything after the first ": ", so values that
		/// themselves contain colons (file paths, timecodes) survive intact. The gaze
		/// filter is treated as enabled only when its parameter lines are present (the
		/// "None" placeholder leaves them absent).
		/// </summary>
		private LoadedFeatureMetadata ParseInfoFile(string path)
		{
			LoadedFeatureMetadata meta = new LoadedFeatureMetadata();
			foreach (string rawLine in File.ReadAllLines(path))
			{
				string line = rawLine.Trim();
				if (TryGetValue(line, "Stimulus video: ", out string value))
					meta.VideoPath = value;
				else if (TryGetValue(line, "Gaze file: ", out value))
					meta.GazePath = value == "none" ? null : value;
				else if (TryGetValue(line, "Eyetracking FPS: ", out value))
					meta.EyetrackingFPS = int.Parse(value);
				else if (TryGetValue(line, "Data start frame: ", out value))
					meta.DataStartFrame = int.Parse(value);
				else if (TryGetValue(line, "Gaze space width: ", out value))
					meta.GazeSpaceWidth = int.Parse(value);
				else if (TryGetValue(line, "Gaze space height: ", out value))
					meta.GazeSpaceHeight = int.Parse(value);
				else if (TryGetValue(line, "Median filter window size: ", out value))
				{
					meta.GazeFilter.IsEnabled = true;
					meta.GazeFilter.MedianFilterWindowSize = int.Parse(value);
				}
				else if (TryGetValue(line, "Filter pupil size: ", out value))
					meta.GazeFilter.FilterPupilSize = bool.Parse(value);
				else if (TryGetValue(line, "Outlier removal enabled: ", out value))
					meta.GazeFilter.EnableOutlierRemoval = bool.Parse(value);
				else if (TryGetValue(line, "Outlier threshold X: ", out value))
					meta.GazeFilter.OutlierThresholdX = double.Parse(value);
				else if (TryGetValue(line, "Outlier threshold Y: ", out value))
					meta.GazeFilter.OutlierThresholdY = double.Parse(value);
				else if (TryGetValue(line, "Outlier threshold radius: ", out value))
					meta.GazeFilter.OutlierThresholdRadius = double.Parse(value);
				else if (TryGetValue(line, "Pad percent: ", out value))
					meta.PadPercent = double.Parse(value);
				else if (TryGetValue(line, "Pad value: ", out value))
					meta.PadValue = double.Parse(value);
				else if (TryGetValue(line, "Frame scale: ", out value))
					meta.FrameScale = double.Parse(value);
				else if (TryGetValue(line, "Video FPS: ", out value))
					meta.VideoFps = double.Parse(value);
				else if (TryGetValue(line, "Spatial frequencies: ", out value))
					meta.SpatialFrequencies = ParseDoubleList(value);
				else if (TryGetValue(line, "Temporal frequencies: ", out value))
					meta.TemporalFrequencies = ParseDoubleList(value);
				else if (TryGetValue(line, "Directions: ", out value))
					meta.Directions = ParseDoubleList(value);
				else if (TryGetValue(line, "Start frame: ", out value))
					meta.StartFrame = int.Parse(value);
				else if (TryGetValue(line, "Compute dtype: ", out value))
					meta.OutputDtype = value;
			}
			return meta;
		}

		// Returns true and the trimmed remainder when the line starts with the prefix.
		private static bool TryGetValue(string line, string prefix, out string value)
		{
			if (line.StartsWith(prefix))
			{
				value = line.Substring(prefix.Length).Trim();
				return true;
			}
			value = "";
			return false;
		}

		// Parses a comma-separated list of doubles (e.g. "0, 2, 4, 8").
		private static List<double> ParseDoubleList(string value)
		{
			List<double> result = new List<double>();
			foreach (string token in value.Split(','))
			{
				string trimmed = token.Trim();
				if (trimmed.Length > 0)
					result.Add(double.Parse(trimmed));
			}
			return result;
		}

		/// <summary>
		/// Resolves a file path recorded in a saved info file so a result folder remains
		/// portable. Returns the recorded path if it exists; otherwise falls back to a file
		/// of the same name in <paramref name="fallbackDirectory"/> (the info file's folder),
		/// which handles the whole folder being copied to another machine; otherwise null.
		/// </summary>
		private static string? ResolveFile(string? recordedPath, string fallbackDirectory)
		{
			if (string.IsNullOrEmpty(recordedPath)) return null;
			if (File.Exists(recordedPath)) return recordedPath;
			string sibling = System.IO.Path.Combine(fallbackDirectory, System.IO.Path.GetFileName(recordedPath));
			if (File.Exists(sibling)) return sibling;
			return null;
		}

		// Shows an open-file dialog with a single filter and returns the chosen path, or null if cancelled.
		private async Task<string?> PromptForFile(string title, string filterName, params string[] extensions)
		{
			OpenFileDialog dialog = new OpenFileDialog() { Title = title };
			FileDialogFilter filter = new FileDialogFilter() { Name = filterName };
			foreach (string extension in extensions)
				filter.Extensions.Add(extension);
			dialog.Filters.Add(filter);
			string[] result = await dialog.ShowAsync(MainWindow);
			return (result == null || result.Length == 0) ? null : result[0];
		}

		/// <summary>
		/// Loads a previously saved motion-energy result. The user selects the saved
		/// "... info.txt" file; the sibling ".npy" feature array is derived from its name.
		/// All parameters, the stimulus video, the gaze data and its mapping are
		/// reconstructed from the info file. If the gaze was filtered, the same filter is
		/// reapplied to the loaded gaze. The user is prompted to relocate the video, gaze,
		/// or feature file if any has moved.
		/// </summary>
		public async void LoadSavedFeatures()
		{
			string? infoPath = await PromptForFile("Load saved motion energy (select the info text file)", "Motion energy info", "txt");
			if (infoPath == null) return;

			if (IsVideoPlaying) PlayPause();
			IsProgressBarVisible = true;
			IsProgressBarIndeterminate = true;
			StatusText = "Loading saved motion energy...";
			try
			{
				LoadedFeatureMetadata meta = ParseInfoFile(infoPath);

				// Derive the features .npy path from the info file name ("X info.txt" -> "X.npy").
				string directory = System.IO.Path.GetDirectoryName(infoPath);
				string infoFileName = System.IO.Path.GetFileName(infoPath);
				string baseName = infoFileName.EndsWith(" info.txt")
					? infoFileName.Substring(0, infoFileName.Length - " info.txt".Length)
					: System.IO.Path.GetFileNameWithoutExtension(infoPath);
				string featuresPath = System.IO.Path.Combine(directory, baseName + ".npy");
				if (!File.Exists(featuresPath))
				{
					featuresPath = await PromptForFile("Locate the motion energy .npy file", "NumPy arrays", "npy");
					if (featuresPath == null) { StatusText = "Load cancelled: no feature file selected."; return; }
				}

				// Open the stimulus video, falling back to the info file's folder, then prompting.
				string? videoPath = ResolveFile(meta.VideoPath, directory);
				if (videoPath == null)
				{
					videoPath = await PromptForFile("Locate the stimulus video", "Videos", "avi", "mkv", "mp4", "m4v");
					if (videoPath == null) { StatusText = "Load cancelled: no stimulus video selected."; return; }
				}
				VideoReader videoReader = new VideoReader(videoPath);
				videoReader.ReadFrame();

				// Load the gaze data, or synthesize centered gaze when none was used.
				// Resolve relative to the info file's folder before prompting, for portability.
				string? gazeFileName = ResolveFile(meta.GazePath, directory);
				if (meta.GazePath != null && gazeFileName == null)
					gazeFileName = await PromptForFile("Locate the gaze file", "Gaze files", "npy", "csv", "txt", "asc", "edf");

				NDArray gazeLocations;
				if (gazeFileName != null && File.Exists(gazeFileName))
				{
					string gazePathToLoad = gazeFileName;
					gazeLocations = await Task.Run(() => GazeLoader.Load(gazePathToLoad, out int _));
					// Reapply the same gaze filter that was used when the features were computed.
					if (meta.GazeFilter.IsEnabled)
					{
						GazeFilterSettings gazeFilter = meta.GazeFilter;
						NDArray rawGaze = gazeLocations;
						gazeLocations = await Task.Run(() => GazeFilter.Filter(
							rawGaze, gazeFilter.MedianFilterWindowSize, gazeFilter.FilterPupilSize,
							gazeFilter.EnableOutlierRemoval, gazeFilter.OutlierThresholdX,
							gazeFilter.OutlierThresholdY, gazeFilter.OutlierThresholdRadius));
					}
				}
				else
				{
					gazeFileName = null;
					gazeLocations = new NDArray(NPTypeCode.Double, Shape.Matrix(videoReader.frameCount, 2));
					for (int frameIndex = 0; frameIndex < videoReader.frameCount; frameIndex++)
					{
						gazeLocations[frameIndex, 0] = meta.GazeSpaceWidth / 2.0;
						gazeLocations[frameIndex, 1] = meta.GazeSpaceHeight / 2.0;
					}
				}
				
				int dtypeIndex = OutputDtypeNames.IndexOf(meta.OutputDtype);
				// Load the feature array
				// dtype == 0 is float16, which numsharp does not support, so that has to go through python
				// the other kinds can be loaded from numsharp
				NDArray features = dtypeIndex == 0 ? await Task.Run(() =>
				{
					PythonEnvironmentManager.Instance.Initialize();
					return motionEnergyFeatures.LoadFeatures(featuresPath);
				}) : Num.load(featuresPath).astype(NPTypeCode.Single);

				// Apply parsed parameters via the public setters so the UI updates.
				ResetDynamicState();
				PadPercent = meta.PadPercent;
				PadValue = meta.PadValue;
				FrameScale = meta.FrameScale;
				VideoFps = meta.VideoFps;
				StartFrame = meta.StartFrame;
				if (dtypeIndex >= 0) SelectedOutputDtypeIndex = dtypeIndex;

				SpatialFrequencies.Clear();
				foreach (double frequency in meta.SpatialFrequencies) SpatialFrequencies.Add(frequency);
				motionEnergyFeatures.SpatialFrequencies = new List<double>(SpatialFrequencies);
				TemporalFrequencies.Clear();
				foreach (double frequency in meta.TemporalFrequencies) TemporalFrequencies.Add(frequency);
				motionEnergyFeatures.TemporalFrequencies = new List<double>(TemporalFrequencies);
				Directions.Clear();
				foreach (double direction in meta.Directions) Directions.Add(direction);
				motionEnergyFeatures.Directions = new List<double>(Directions);

				// Mirror LoadFromRecentering for the video/gaze state.
				_gazeLocations = gazeLocations;
				_gazeFileName = gazeFileName;
				_gazeFilterSettings = meta.GazeFilter;
				_dataStartFrame = meta.DataStartFrame;
				_eyetrackingFPS = meta.EyetrackingFPS;
				_gazeSpaceWidth = meta.GazeSpaceWidth;
				_gazeSpaceHeight = meta.GazeSpaceHeight;
				VideoWidth = videoReader.width;
				VideoHeight = videoReader.height;
				_videoPlaybackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / (double)videoReader.fps);
				TotalVideoFrames = videoReader.frameCount;
				IsLoadedFromRecentering = true;
				_updateDisplayDelegate = _isPreview ? UpdateDisplayRecenteredPreview : UpdateDisplayRecentered;

				// Rebuild the pyramid so the filter parameters exist for the overlays.
				// Pass manageProgressBar false so the pyramid build does not hide the progress
				// bar while the rest of the load (normalization statistics, display) is still running.
				await ComputePyramid(false, false);

				bool filterCountMismatch = motionEnergyFeatures.FilterCount > 0
					&& features.Shape.NDim == 2
					&& features.Shape[1] != motionEnergyFeatures.FilterCount;

				_motionEnergyFeatures = features;
				_filterResponsesStartFrame = meta.StartFrame;
				_hasFilterResponses = true;
				_areFilterResponsesStale = false;
				_filterResponseRowCount = features.Shape[0];
				_filterResponseColumnCount = features.Shape[1];
				_flatFilterResponses = features.ToArray<float>();
				this.RaisePropertyChanged(nameof(CanShowDynamicOverlay));
				this.RaisePropertyChanged(nameof(IsDynamicOverlayStale));
				await Task.Run(() => ComputeNormalizationStatistics());

				if (filterCountMismatch)
					StatusText = String.Format(
						"Loaded {0} frames x {1} features, but the pyramid has {2} filters. The overlay may not match.",
						features.Shape[0], features.Shape[1], motionEnergyFeatures.FilterCount);
				else
					StatusText = String.Format("Loaded motion energy: {0} frames x {1} features",
						features.Shape[0], features.Shape[1]);
				
				_videoReader = videoReader;
				this.RaisePropertyChanged("CanPlayVideo");
				UpdateTimecodeDisplay();
				UpdateDisplay();
			}
			catch (Exception exception)
			{
				StatusText = String.Format("Error loading saved motion energy: {0}", exception.Message);
			}
			finally
			{
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

		private Action _updateDisplayDelegate;

		// draws recentered image with UI controls
		private void UpdateDisplayRecentered()
		{
			VideoFrame = _videoReader.GetFrameForDisplay();
			int dataIndex = Math.Clamp(VideoTimeToDataIndex(CurrentVideoFrame), 0, _gazeLocations.Shape[0] - 1);
			double gazeXValue = (double)_gazeLocations[dataIndex, 0];
			double gazeYValue = (double)_gazeLocations[dataIndex, 1];
			if (!Double.IsNaN(gazeXValue) && !Double.IsNaN(gazeYValue))
			{
				GazeX = gazeXValue;
				GazeY = gazeYValue;
			}
		}
		
		// draws the actual frame pixels that would be sent to motion-energy
		private void UpdateDisplayRecenteredPreview()
		{
			int dataIndex = Math.Clamp(VideoTimeToDataIndex(CurrentVideoFrame), 0, _gazeLocations.Shape[0] - 1);
			double gazeXValue = (double)_gazeLocations[dataIndex, 0];
			double gazeYValue = (double)_gazeLocations[dataIndex, 1];
			if (Double.IsNaN(gazeXValue)) gazeXValue = _gazeSpaceWidth  / 2.0;
			if (Double.IsNaN(gazeYValue)) gazeYValue = _gazeSpaceHeight / 2.0;

			int scaledWidth  = Math.Max(1, (int)(_videoWidth  * _frameScale));
			int scaledHeight = Math.Max(1, (int)(_videoHeight * _frameScale));
			using Mat translationMatrix = Recenterer.GetTranslationMatrix(gazeXValue, gazeYValue, _gazeSpaceWidth, _gazeSpaceHeight, _frameScale);
			using Mat processedFrame = Recenterer.ProcessFrame(_videoReader.cvFrame,
				scaledWidth, scaledHeight, scaledWidth * 2, scaledHeight * 2, _padValue * 255.0, translationMatrix);
			MemoryStream imageStream = processedFrame.ToMemoryStream(".bmp");
			imageStream.Seek(0, SeekOrigin.Begin);
			VideoFrame = new Bitmap(imageStream);
		}
		
		// displays raw video that fills the canvas
		private void UpdateDisplayRaw()
		{
			VideoFrame = _videoReader.GetFrameForDisplay();
		}

		public void UpdateDisplay()
		{
			_updateDisplayDelegate?.Invoke();
			CurrentVideoFrame = _videoReader.CurrentFrameNumber;
			CurrentVideoTime = _timeFormatter(_videoReader.CurrentFrameNumber);
			UpdateDynamicOpacities();
		}
	}
}
