using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Media;
using NumSharp;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	/// <summary>
	/// Base view model for the tabs that draw a gaze marker over the stimulus video
	/// (Stimulus &amp; Gaze and Recentering). On top of the video transport it owns the
	/// gaze marker, the fading gaze trail, the TTL indicator, and the mapping between
	/// video frames and gaze-data indices. Subclasses supply the gaze data through
	/// <see cref="GazeData"/>; the marker and trail are driven from that single source.
	/// </summary>
	public abstract class GazeVideoPlayerViewModelBase : VideoPlayerViewModelBase
	{
		public ReactiveCommand<Unit, Unit> JumpToFirstTTLCommand { get; }

		/// <summary>
		/// The gaze locations currently being displayed. Stimulus returns its raw or
		/// filtered locations depending on the filter toggle; Recentering returns the
		/// single loaded array.
		/// </summary>
		protected abstract NDArray? GazeData { get; }

		private bool _isLoadingGaze = false;
		public bool IsLoadingGaze
		{
			get => _isLoadingGaze;
			set => this.RaiseAndSetIfChanged(ref _isLoadingGaze, value);
		}

		private bool _isGazeLoaded = false;
		public bool IsGazeLoaded
		{
			get => _isGazeLoaded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isGazeLoaded, value);
				this.RaisePropertyChanged("IsGazeEllipseVisible");
				this.RaisePropertyChanged("HasTTLData");
				OnGazeLoadedChanged();
			}
		}

		/// <summary>Hook for subclasses to raise additional properties that depend on IsGazeLoaded.</summary>
		protected virtual void OnGazeLoadedChanged() { }

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

		public bool HasTTLData => IsGazeLoaded && Recenterer.FindFirstTTLGazeIndex(GazeData) != null;

		// == TTL scrubber markers ==
		// Cached gaze-data indices that carry a TTL pulse. These index into the gaze
		// array and are independent of the gaze-to-video alignment, so they are rebuilt
		// only when the gaze data itself changes, not when the alignment is shifted.
		private List<int> ttlGazeIndices = new List<int>();

		private IReadOnlyList<double> _ttlMarkerPositions = Array.Empty<double>();

		/// <summary>
		/// Positions of the TTL pulses along the video scrubber, as fractions in [0, 1]
		/// of the total video duration under the current alignment. Recomputed whenever
		/// the display updates so the markers track alignment shifts.
		/// </summary>
		public IReadOnlyList<double> TTLMarkerPositions
		{
			get => _ttlMarkerPositions;
			private set => this.RaiseAndSetIfChanged(ref _ttlMarkerPositions, value);
		}

		private double _dataExtentStartFraction = 0.0;

		/// <summary>
		/// Start of the eyetracking data along the video scrubber, as a fraction in [0, 1]
		/// of the total video duration under the current alignment.
		/// </summary>
		public double DataExtentStartFraction
		{
			get => _dataExtentStartFraction;
			private set => this.RaiseAndSetIfChanged(ref _dataExtentStartFraction, value);
		}

		private double _dataExtentEndFraction = 0.0;

		/// <summary>
		/// End of the eyetracking data along the video scrubber, as a fraction in [0, 1] of
		/// the total video duration under the current alignment. Equals the start fraction
		/// when there is no data to mark.
		/// </summary>
		public double DataExtentEndFraction
		{
			get => _dataExtentEndFraction;
			private set => this.RaiseAndSetIfChanged(ref _dataExtentEndFraction, value);
		}

		// == Gaze marker ==
		private double _gazeX = 0;
		public double GazeX
		{
			get => _gazeX;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeX, value);
				this.RaisePropertyChanged("GazeCircleLeft");
				OnGazeXChanged();
			}
		}

		private double _gazeY = 0;
		public double GazeY
		{
			get => _gazeY;
			set
			{
				this.RaiseAndSetIfChanged(ref _gazeY, value);
				this.RaisePropertyChanged("GazeCircleTop");
				OnGazeYChanged();
			}
		}

		/// <summary>Hook for subclasses to raise additional layout properties that depend on GazeX.</summary>
		protected virtual void OnGazeXChanged() { }
		/// <summary>Hook for subclasses to raise additional layout properties that depend on GazeY.</summary>
		protected virtual void OnGazeYChanged() { }

		public double GazeCircleLeft => _gazeX - GazeRadius;
		public double GazeCircleTop => _gazeY - GazeRadius;

		private double _gazeDiameter = 204;
		public double GazeRadius => _gazeDiameter / 2;
		public double GazeDiameter
		{
			get => _gazeDiameter;
			set
			{
				// gaze diameter is bounded
				_gazeDiameter = value;
				this.RaisePropertyChanged("GazeDiameter");
				this.RaisePropertyChanged("GazeRadius");
				// because the circle is set by its left/top corner
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

		// == Frame <-> data-index mapping ==
		protected int? dataStartFrame = null;

		protected int? dataFrame // used to index into the gaze matrix
		{
			get
			{
				if (dataStartFrame == null) return null;
				return VideoTimeToDataIndex(CurrentVideoFrame);
			}
		}

		protected GazeVideoPlayerViewModelBase()
		{
			JumpToFirstTTLCommand = ReactiveCommand.Create(JumpToFirstTTL);
			TrailPoints = new ObservableCollection<TrailGazePoint>(
				Enumerable.Range(0, 10).Select(_ => new TrailGazePoint()));
		}

		/// <summary>
		/// Given a video frame index, gets the first corresponding index in the gaze locations.
		/// </summary>
		protected int VideoTimeToDataIndex(int videoFrame)
		{
			int videoFramesElapsed = videoFrame - dataStartFrame.Value;
			if (videoFramesElapsed < 0)
				return 0;
			double videoElapsedTime = (double)videoFramesElapsed / videoReader.fps;
			return (int)(videoElapsedTime * EyetrackingFPS);
		}

		/// <summary>
		/// For a given index in the gaze data, gets the corresponding video frame under
		/// the current alignment (data start frame and eyetracking sample rate). This is
		/// the inverse of <see cref="VideoTimeToDataIndex"/>.
		/// </summary>
		/// <param name="dataIndex">index in eyetracking data</param>
		/// <returns>video frame number</returns>
		protected int DataIndexToVideoTime(int dataIndex)
		{
			double dataElapsedTime = (double)dataIndex / EyetrackingFPS; // in seconds
			int dataElapsedFrames = (int)Math.Round(dataElapsedTime * videoReader.fps);
			return dataStartFrame.Value + dataElapsedFrames;
		}

		/// <summary>
		/// Rescans the current gaze data for TTL pulses and caches their gaze-data
		/// indices, then refreshes the scrubber marker positions. Call this whenever the
		/// array returned by <see cref="GazeData"/> is replaced (gaze load or filter
		/// swap); the cache is alignment-independent, so shifting the alignment does not
		/// require a rescan.
		/// </summary>
		protected void RebuildTTLMarkerCache()
		{
			ttlGazeIndices = Recenterer.FindAllTTLGazeIndices(GazeData);
			UpdateTTLMarkerPositions();
		}

		/// <summary>
		/// Recomputes the scrubber marker positions from the cached TTL gaze indices under
		/// the current alignment. Markers that fall outside the video are dropped. Sets an
		/// empty list when there is no alignment or video loaded.
		/// </summary>
		private void UpdateTTLMarkerPositions()
		{
			if (dataStartFrame == null || videoReader == null || TotalVideoFrames <= 0)
			{
				TTLMarkerPositions = Array.Empty<double>();
				DataExtentStartFraction = 0.0;
				DataExtentEndFraction = 0.0;
				return;
			}
			List<double> positions = new List<double>(ttlGazeIndices.Count);
			foreach (int ttlGazeIndex in ttlGazeIndices)
			{
				double fraction = (double)DataIndexToVideoTime(ttlGazeIndex) / TotalVideoFrames;
				if (fraction >= 0.0 && fraction <= 1.0)
					positions.Add(fraction);
			}
			TTLMarkerPositions = positions;

			DataExtentStartFraction = Math.Clamp((double)dataStartFrame.Value / TotalVideoFrames, 0.0, 1.0);
			int dataEndVideoFrame = (object)GazeData != null
				? DataIndexToVideoTime(GazeData.Shape[0])
				: dataStartFrame.Value;
			DataExtentEndFraction = Math.Clamp((double)dataEndVideoFrame / TotalVideoFrames, 0.0, 1.0);
		}

		protected bool CheckTTLInVideoFrame(int videoFrame)
		{
			if ((object)GazeData == null || GazeData.Shape[1] < 4) return false;
			int startIndex = VideoTimeToDataIndex(videoFrame);
			int endIndex = VideoTimeToDataIndex(videoFrame + 1);
			for (int i = startIndex; i < endIndex && i < GazeData.Shape[0]; i++)
				if ((double)GazeData[i, 3] != 0.0)
					return true;
			return false;
		}

		public override void UpdateDisplay()
		{
			VideoFrame = videoReader.GetFrameForDisplay();
			CurrentVideoFrame = videoReader.CurrentFrameNumber;
			CurrentVideoTime = _timeFormatter(videoReader.CurrentFrameNumber);
			if (dataStartFrame != null)
			{
				if ((object)GazeData == null || dataFrame >= GazeData.Shape[0])
				{
					IsGazeAtNaN = true;
				}
				else
				{
					double gazeXValue = (double)GazeData[dataFrame, 0];
					double gazeYValue = (double)GazeData[dataFrame, 1];
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
			UpdateTTLMarkerPositions();
		}

		protected void UpdateTrailPoints()
		{
			const int trailCircleCount = 10;
			double stepSeconds = TrailLength / trailCircleCount;
			int stepVideoFrames = Math.Max(1, (int)(stepSeconds * videoReader.fps));
			// snap to multiples of the step so circles stay fixed between step intervals
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
				if (trailDataIndex >= GazeData.Shape[0])
				{
					TrailPoints[i].IsVisible = false;
					continue;
				}
				double trailX = (double)GazeData[trailDataIndex, 0];
				double trailY = (double)GazeData[trailDataIndex, 1];
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

		public void JumpToFirstTTL()
		{
			int? firstTTLGazeIndex = Recenterer.FindFirstTTLGazeIndex(GazeData);
			if (firstTTLGazeIndex == null || videoReader == null || dataStartFrame == null) return;
			int videoFrame = DataIndexToVideoTime(firstTTLGazeIndex.Value);
			videoFrame = Math.Clamp(videoFrame, 0, videoReader.frameCount - 1);
			ShowFrame(videoFrame);
		}
	}
}
