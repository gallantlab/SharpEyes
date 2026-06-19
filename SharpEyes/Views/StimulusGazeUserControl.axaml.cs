using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SharpEyes.Models;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public partial class StimulusGazeUserControl : UserControl
	{
		private bool areThumbEventsAttached = false;
		private bool isDraggingVideoSlider = false;
		private bool isMouseDownOnVideoCanvas = false;
		private StimulusGazeViewModel? viewModel => (StimulusGazeViewModel)this.DataContext;
		public StimulusGazeUserControl()
		{
			InitializeComponent();
			this.GotFocus += (sender, args) => { AttachThumbEvents(); };
		}

		private void SetGazeLocation(Point point)
		{
			if (viewModel.IsMovingGaze)
			{
				viewModel.GazeX = point.X;
				viewModel.GazeY = point.Y;
			}
		}

		private void VideoCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			isMouseDownOnVideoCanvas = true;
			if (viewModel.IsMovingGaze && viewModel.IsVideoPlaying)
			{
				viewModel.PlayPause();
				SetGazeLocation(e.GetPosition(VideoCanvas));
			}
		}

		private void VideoCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
		{
			if (isMouseDownOnVideoCanvas)
				SetGazeLocation(e.GetPosition(VideoCanvas));
		}

		private void VideoCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			isMouseDownOnVideoCanvas = false;
			viewModel.UpdateGaze();
		}

		private void VideoCanvas_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
		{
			if (viewModel.IsVideoPlaying)
				viewModel.PlayPause();
			if (e.Delta.Y > 0)
				viewModel.ShowFrame(viewModel.CurrentVideoFrame - 1);
			else
				viewModel.ShowFrame(viewModel.CurrentVideoFrame + 1);
		}

		/// <summary>
		/// Attaches event handlers on to the thumb in the video slider, because Avalonia XAML
		/// does not expose those properties
		/// </summary>
		private void AttachThumbEvents()
		{
			if (!areThumbEventsAttached)
			{
				Thumb thumb = VideoTimeSlider.FindDescendantOfType<Thumb>();
				thumb.DragStarted += VideoTimeSlider_DragStarted;
				thumb.DragDelta += VideoTimeSlider_Drag;
				thumb.DragCompleted += VideoTimeSlider_DragFinished;
				areThumbEventsAttached = true;
			}
		}

		private void VideoTimeSlider_DragStarted(object sender, VectorEventArgs e)
		{
			if (viewModel.IsVideoPlaying)
				viewModel.PlayPause();
			isDraggingVideoSlider = true;
		}

		private void VideoTimeSlider_Drag(object sender, VectorEventArgs e)
		{
			if (isDraggingVideoSlider)
				viewModel.ShowFrame();
		}

		private void VideoTimeSlider_DragFinished(object sender, VectorEventArgs e)
		{
			isDraggingVideoSlider = false;
		}

		private void FilterNumericUpDown_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			viewModel.ReapplyFilterIfEnabled();
		}

		private void ChangeTimecodeDisplay(object sender, RoutedEventArgs e)
		{
			Settings.Current.ShowFrameNumber = !Settings.Current.ShowFrameNumber;
			Settings.Current.Save();
			viewModel?.UpdateTimecodeDisplay();
		}
	}
}
