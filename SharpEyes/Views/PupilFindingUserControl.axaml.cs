using Avalonia.Controls;
using System;
using SharpEyes.ViewModels;
using SharpEyes.Models;
using Avalonia.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;
using Eyetracking;

namespace SharpEyes.Views
{
	public partial class PupilFindingUserControl : UserControl
	{
		// for moving the pupil and window
		private bool isMouseDownOnVideoCanvas = false;
		private double lastPupilStrokeOpacity = -1;
		private Point? windowInitialPoint = null;
		private PupilFindingUserControlViewModel? viewModel => (PupilFindingUserControlViewModel)this.DataContext;

		private bool isDraggingVideoSlider = false;

		private bool areThumbEventsAttached = false;

		public PupilFindingUserControl()
		{
			InitializeComponent();
			this.GotFocus += (sender, args) => { AttachThumbEvents(); };
		}

		private void SetCanvasChildElementPosition(Point point)
		{
			switch (viewModel.EditingState)
			{
				case EditingState.MovePupil:
					if (lastPupilStrokeOpacity < 0)
					{
						lastPupilStrokeOpacity = viewModel.PupilStrokeOpacity;
						viewModel.PupilStrokeOpacity /= 2;
					}

					viewModel.PupilX = point.X;
					viewModel.PupilY = point.Y;
					if (Double.IsNaN(viewModel.PupilDiameter))
						viewModel.PupilDiameter = 36;
					viewModel.IsPupilManuallyEdited = true;
					break;
				case EditingState.DrawWindow:
					if (windowInitialPoint == null)
						windowInitialPoint = point;
					double left, right, top, bottom;
					if (windowInitialPoint.Value.X < point.X)
					{
						left = windowInitialPoint.Value.X;
						right = point.X;
					}
					else
					{
						left = point.X;
						right = windowInitialPoint.Value.X;
					}
					if (windowInitialPoint.Value.Y < point.Y)
					{
						top = windowInitialPoint.Value.Y;
						bottom = point.Y;
					}
					else
					{
						top = point.Y;
						bottom = windowInitialPoint.Value.Y;
					}
					left = left >= 0 ? left : 0;
					top = top >= 0 ? top : 0;
					viewModel.PupilWindowLeft = (int)Math.Round(left);
					viewModel.PupilWindowTop = (int)Math.Round(top);
					viewModel.PupilWindowWidth = right <= VideoCanvas.Width ? (int)Math.Round(right - left) : (int)Math.Round(VideoCanvas.Width - left);
					viewModel.PupilWindowHeight = bottom <= VideoCanvas.Height ? (int)Math.Round(bottom - top) : (int)Math.Round(VideoCanvas.Height - top);

					break;
				default:
					break;
			}
		}

		public void VideoCanvasMouseDown(object sender, PointerPressedEventArgs e)
		{
			isMouseDownOnVideoCanvas = true;
			windowInitialPoint = null;
			SetCanvasChildElementPosition(e.GetPosition(VideoCanvas));
		}

		public void VideoCanvasMouseMove(object sender, PointerEventArgs e)
		{
			if (isMouseDownOnVideoCanvas)
				SetCanvasChildElementPosition(e.GetPosition(VideoCanvas));
		}

		public void VideoCanvasMouseUp(object sender, PointerReleasedEventArgs e)
		{
			isMouseDownOnVideoCanvas = false;
			if (viewModel.EditingState == EditingState.MovePupil)
			{
				viewModel.PupilConfidence = 1;
				int frameDecay;
				ManualUpdateMode mode;
				if (viewModel.UseNoDecay) // this is a dummy
					return;
				if (viewModel.UseLinearDecay)
				{
					frameDecay = viewModel.LinearDecayFrames;
					mode = ManualUpdateMode.Linear;
				}
				else
				{
					frameDecay = viewModel.ExponentialDecayTimeConstant;
					mode = ManualUpdateMode.Exponential;
				}
				viewModel.pupilFinder.ManuallyUpdatePupilLocations(viewModel.pupilFinder.CurrentFrameNumber, 
					viewModel.PupilX, viewModel.PupilY, viewModel.PupilRadius, frameDecay, mode);
				viewModel.IsDataDirty = true;
				viewModel.PupilStrokeOpacity = lastPupilStrokeOpacity;
				lastPupilStrokeOpacity = -1;
			}
		}

		public void VideoCanvasScroll(object sender, PointerWheelEventArgs e)
		{
			// e.Delta.Y encodes the number of clicks of the wheel, with + being up, - being down
			switch (viewModel.EditingState)
			{
				case EditingState.None:
					if (e.Delta.Y > 0)
						viewModel.PreviousFrameCommand?.Execute();
					else
						viewModel.NextFrameCommand?.Execute();
					break;
				case EditingState.DrawWindow:
					break;
				case EditingState.MovePupil:
					viewModel.PupilDiameter += e.Delta.Y;
					viewModel.IsPupilManuallyEdited = true;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
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

		public void OnClosing()
		{
			viewModel?.OnClosing();
		}

		private void ChangeTimecodeDisplay(object sender, RoutedEventArgs e)
		{
			Settings.Current.ShowFrameNumber = !Settings.Current.ShowFrameNumber;
			Settings.Current.Save();
			viewModel?.UpdateTimecodeDisplay();
		}
	}
}
