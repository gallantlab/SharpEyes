using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public class MotionEnergyOverlayControl : Control
	{
		public static readonly StyledProperty<ObservableCollection<PyramidCircleOverlay>> CirclesProperty =
			AvaloniaProperty.Register<MotionEnergyOverlayControl, ObservableCollection<PyramidCircleOverlay>>(
				nameof(Circles));

		public static readonly StyledProperty<ObservableCollection<PyramidArrowOverlay>> ArrowsProperty =
			AvaloniaProperty.Register<MotionEnergyOverlayControl, ObservableCollection<PyramidArrowOverlay>>(
				nameof(Arrows));

		public static readonly StyledProperty<int> RenderTriggerProperty =
			AvaloniaProperty.Register<MotionEnergyOverlayControl, int>(
				nameof(RenderTrigger));

		public ObservableCollection<PyramidCircleOverlay> Circles
		{
			get => GetValue(CirclesProperty);
			set => SetValue(CirclesProperty, value);
		}

		public ObservableCollection<PyramidArrowOverlay> Arrows
		{
			get => GetValue(ArrowsProperty);
			set => SetValue(ArrowsProperty, value);
		}

		public int RenderTrigger
		{
			get => GetValue(RenderTriggerProperty);
			set => SetValue(RenderTriggerProperty, value);
		}

		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
		{
			base.OnPropertyChanged(change);

			if (change.Property == CirclesProperty)
			{
				ObservableCollection<PyramidCircleOverlay> oldValue =
					change.OldValue.GetValueOrDefault<ObservableCollection<PyramidCircleOverlay>>();
				ObservableCollection<PyramidCircleOverlay> newValue =
					change.NewValue.GetValueOrDefault<ObservableCollection<PyramidCircleOverlay>>();
				if (oldValue != null) oldValue.CollectionChanged -= OnCollectionChanged;
				if (newValue != null) newValue.CollectionChanged += OnCollectionChanged;
				InvalidateVisual();
			}
			else if (change.Property == ArrowsProperty)
			{
				ObservableCollection<PyramidArrowOverlay> oldValue =
					change.OldValue.GetValueOrDefault<ObservableCollection<PyramidArrowOverlay>>();
				ObservableCollection<PyramidArrowOverlay> newValue =
					change.NewValue.GetValueOrDefault<ObservableCollection<PyramidArrowOverlay>>();
				if (oldValue != null) oldValue.CollectionChanged -= OnCollectionChanged;
				if (newValue != null) newValue.CollectionChanged += OnCollectionChanged;
				InvalidateVisual();
			}
			else if (change.Property == RenderTriggerProperty)
			{
				InvalidateVisual();
			}
		}

		private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			InvalidateVisual();
		}

		public override void Render(DrawingContext context)
		{
			base.Render(context);

			ObservableCollection<PyramidCircleOverlay> circles = Circles;
			ObservableCollection<PyramidArrowOverlay> arrows = Arrows;

			if (circles != null)
			{
				foreach (PyramidCircleOverlay circle in circles)
				{
					if (circle.Pen == null) continue;
					double radius = circle.Diameter / 2.0;
					context.DrawEllipse(
						null,
						circle.Pen,
						new Point(circle.Left + radius, circle.Top + radius),
						radius,
						radius);
				}
			}

			if (arrows != null)
			{
				foreach (PyramidArrowOverlay arrow in arrows)
				{
					if (arrow.Pen == null) continue;
					using (IDisposable translateState = context.PushPreTransform(
						Matrix.CreateTranslation(arrow.CanvasLeft, arrow.CanvasTop)))
					{
						context.DrawGeometry(null, arrow.Pen, arrow.Geometry);
					}
				}
			}
		}
	}
}
