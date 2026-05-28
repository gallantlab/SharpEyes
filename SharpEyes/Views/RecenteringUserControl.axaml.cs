using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SharpEyes.Models;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public partial class RecenteringUserControl : UserControl
	{
		private bool areThumbEventsAttached = false;
		private bool isDraggingVideoSlider = false;
		private RecenteringViewModel? viewModel => (RecenteringViewModel)this.DataContext;
		public RecenteringUserControl()
		{
			InitializeComponent();
			this.GotFocus += (sender, args) => { AttachThumbEvents(); };
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

		private void ChangeTimecodeDisplay(object sender, RoutedEventArgs e)
		{
			Settings.Current.ShowFrameNumber = !Settings.Current.ShowFrameNumber;
			Settings.Current.Save();
			viewModel?.UpdateTimecodeDisplay();
		}
	}
}
