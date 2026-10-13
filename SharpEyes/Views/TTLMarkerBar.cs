using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SharpEyes.Views
{
	/// <summary>
	/// Draws vertical tick lines over the video scrubber to mark where TTL pulses occur.
	/// Each position is a fraction in [0, 1] of the control width, so the lines rescale
	/// with the control and stay aligned with the slider underneath. The lines are
	/// redrawn whenever the positions, brush, or thickness change.
	/// </summary>
	public class TTLMarkerBar : Control
	{
		public static readonly StyledProperty<IReadOnlyList<double>> PositionsProperty =
			AvaloniaProperty.Register<TTLMarkerBar, IReadOnlyList<double>>(nameof(Positions));

		public static readonly StyledProperty<IBrush> LineBrushProperty =
			AvaloniaProperty.Register<TTLMarkerBar, IBrush>(nameof(LineBrush), Brushes.LimeGreen);

		public static readonly StyledProperty<double> LineThicknessProperty =
			AvaloniaProperty.Register<TTLMarkerBar, double>(nameof(LineThickness), 1.0);

		public static readonly StyledProperty<double> DataExtentStartProperty =
			AvaloniaProperty.Register<TTLMarkerBar, double>(nameof(DataExtentStart));

		public static readonly StyledProperty<double> DataExtentEndProperty =
			AvaloniaProperty.Register<TTLMarkerBar, double>(nameof(DataExtentEnd));

		public static readonly StyledProperty<Slider?> TrackSliderProperty =
			AvaloniaProperty.Register<TTLMarkerBar, Slider?>(nameof(TrackSlider));

		private double trackInset = 0.0;
		private Thumb? trackThumb = null;

		/// <summary>TTL marker positions as fractions in [0, 1] of the control width.</summary>
		public IReadOnlyList<double> Positions
		{
			get => GetValue(PositionsProperty);
			set => SetValue(PositionsProperty, value);
		}

		/// <summary>Start of the eyetracking-data extent line, as a fraction in [0, 1] of the control width.</summary>
		public double DataExtentStart
		{
			get => GetValue(DataExtentStartProperty);
			set => SetValue(DataExtentStartProperty, value);
		}

		/// <summary>End of the eyetracking-data extent line, as a fraction in [0, 1] of the control width.</summary>
		public double DataExtentEnd
		{
			get => GetValue(DataExtentEndProperty);
			set => SetValue(DataExtentEndProperty, value);
		}

		/// <summary>Brush used to stroke the TTL tick lines.</summary>
		public IBrush LineBrush
		{
			get => GetValue(LineBrushProperty);
			set => SetValue(LineBrushProperty, value);
		}

		/// <summary>Stroke thickness of the TTL tick lines.</summary>
		public double LineThickness
		{
			get => GetValue(LineThicknessProperty);
			set => SetValue(LineThicknessProperty, value);
		}

		/// <summary>The slider this bar overlays, whose thumb width sets the marker travel range.</summary>
		public Slider? TrackSlider
		{
			get => GetValue(TrackSliderProperty);
			set => SetValue(TrackSliderProperty, value);
		}

		/// <summary>
		/// Invalidates the rendered visual when a property affecting the drawn lines
		/// changes, and tracks the slider's layout so the thumb width can be measured.
		/// </summary>
		/// <param name="change">the property change notification</param>
		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == TrackSliderProperty)
			{
				Slider? oldSlider = change.OldValue.GetValueOrDefault<Slider?>();
				Slider? newSlider = change.NewValue.GetValueOrDefault<Slider?>();
				if (oldSlider != null) oldSlider.LayoutUpdated -= OnTrackSliderLayoutUpdated;
				if (newSlider != null) newSlider.LayoutUpdated += OnTrackSliderLayoutUpdated;
				trackThumb = null;
				InvalidateVisual();
			}
			else if (change.Property == PositionsProperty ||
			         change.Property == LineBrushProperty ||
			         change.Property == LineThicknessProperty ||
			         change.Property == DataExtentStartProperty ||
			         change.Property == DataExtentEndProperty)
				InvalidateVisual();
		}

		/// <summary>
		/// Re-measures the tracked slider's thumb width after a layout pass and redraws if
		/// it changed. The thumb is located once and cached.
		/// </summary>
		/// <param name="sender">the slider whose layout updated</param>
		/// <param name="e">the layout-updated event arguments</param>
		private void OnTrackSliderLayoutUpdated(object? sender, EventArgs e)
		{
			if (trackThumb == null)
				trackThumb = TrackSlider?.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
			double newInset = trackThumb?.Bounds.Width ?? 0.0;
			if (Math.Abs(newInset - trackInset) > 0.01)
			{
				trackInset = newInset;
				InvalidateVisual();
			}
		}

		/// <summary>
		/// Maps a position fraction to an x coordinate, inset by half the thumb width on
		/// each side so it lines up with the centre of the scrubbing marker.
		/// </summary>
		/// <param name="fraction">position as a fraction in [0, 1]</param>
		/// <param name="width">current control width</param>
		/// <returns>x coordinate for the marker</returns>
		private double MapFractionToX(double fraction, double width)
		{
			double usableWidth = Math.Max(0.0, width - trackInset);
			return trackInset / 2.0 + fraction * usableWidth;
		}

		/// <summary>
		/// Draws the horizontal eyetracking-data extent line and one full-height vertical
		/// line per TTL position, all scaled to the control's current width.
		/// </summary>
		/// <param name="context">the drawing context to render into</param>
		public override void Render(DrawingContext context)
		{
			base.Render(context);
			double width = Bounds.Width;
			double height = Bounds.Height;
			Pen pen = new Pen(LineBrush, LineThickness);

			double extentStartX = MapFractionToX(DataExtentStart, width);
			double extentEndX = MapFractionToX(DataExtentEnd, width);
			if (extentEndX > extentStartX)
			{
				double centerY = height / 2.0;
				context.DrawLine(pen, new Point(extentStartX, centerY), new Point(extentEndX, centerY));
			}

			IReadOnlyList<double> positions = Positions;
			if (positions == null) return;
			foreach (double fraction in positions)
			{
				double x = MapFractionToX(fraction, width);
				context.DrawLine(pen, new Point(x, 0), new Point(x, height));
			}
		}
	}
}
