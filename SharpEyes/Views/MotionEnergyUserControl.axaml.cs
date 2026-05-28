using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public partial class MotionEnergyUserControl : UserControl
	{
		private bool areThumbEventsAttached = false;
		private bool isDraggingVideoSlider = false;
		private MotionEnergyViewModel? viewModel => (MotionEnergyViewModel)this.DataContext;

		public MotionEnergyUserControl()
		{
			InitializeComponent();
			this.GotFocus += (sender, args) => { AttachThumbEvents(); };
		}

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
	}
}
