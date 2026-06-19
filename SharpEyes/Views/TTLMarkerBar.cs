using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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

		/// <summary>
		/// Invalidates the rendered visual when any property that affects the drawn lines
		/// changes.
		/// </summary>
		/// <param name="change">the property change notification</param>
		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == PositionsProperty ||
			    change.Property == LineBrushProperty ||
			    change.Property == LineThicknessProperty ||
			    change.Property == DataExtentStartProperty ||
			    change.Property == DataExtentEndProperty)
				InvalidateVisual();
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

			double extentStartX = DataExtentStart * width;
			double extentEndX = DataExtentEnd * width;
			if (extentEndX > extentStartX)
			{
				double centerY = height / 2.0;
				context.DrawLine(pen, new Point(extentStartX, centerY), new Point(extentEndX, centerY));
			}

			IReadOnlyList<double> positions = Positions;
			if (positions == null) return;
			foreach (double fraction in positions)
			{
				double x = fraction * width;
				context.DrawLine(pen, new Point(x, 0), new Point(x, height));
			}
		}
	}
}
