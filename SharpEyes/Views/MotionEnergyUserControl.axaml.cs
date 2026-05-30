using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SharpEyes.Models;
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

		private void SpatialFrequenciesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (viewModel == null) return;
			ListBox listBox = (ListBox)sender;
			List<int> selectedIndices = new List<int>();
			foreach (object selectedItem in listBox.SelectedItems)
			{
				int index = viewModel.SpatialFrequencies.IndexOf((double)selectedItem);
				if (index >= 0 && !selectedIndices.Contains(index))
					selectedIndices.Add(index);
			}
			viewModel.SelectedSpatialFrequencyIndices = selectedIndices;
		}

		private void TemporalFrequenciesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (viewModel == null) return;
			ListBox listBox = (ListBox)sender;
			List<int> selectedIndices = new List<int>();
			foreach (object selectedItem in listBox.SelectedItems)
			{
				int index = viewModel.TemporalFrequencies.IndexOf((double)selectedItem);
				if (index >= 0 && !selectedIndices.Contains(index))
					selectedIndices.Add(index);
			}
			viewModel.SelectedTemporalFrequencyIndices = selectedIndices;
		}

		private void DirectionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (viewModel == null) return;
			ListBox listBox = (ListBox)sender;
			List<int> selectedIndices = new List<int>();
			foreach (object selectedItem in listBox.SelectedItems)
			{
				int index = viewModel.Directions.IndexOf((double)selectedItem);
				if (index >= 0 && !selectedIndices.Contains(index))
					selectedIndices.Add(index);
			}
			viewModel.SelectedDirectionIndices = selectedIndices;
		}

		// Commits a filter-parameter control when it loses focus.
		private void FilterParameterControl_LostFocus(object sender, RoutedEventArgs e)
		{
			CommitFilterParameter(sender);
		}

		// Commits a filter-parameter control when its value is accepted with Enter.
		private void FilterParameterControl_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter || e.Key == Key.Return)
				CommitFilterParameter(sender);
		}

		// Tells the view model that a filter-parameter change is committed. Controls
		// tagged "NoPyramidRebuild" (pad value) skip the pyramid rebuild.
		private void CommitFilterParameter(object sender)
		{
			if (viewModel == null) return;
			Control control = (Control)sender;
			bool rebuildPyramid = !object.Equals(control.Tag, "NoPyramidRebuild");
			viewModel.CommitFilterParameterChange(rebuildPyramid);
		}

		private void ChangeTimecodeDisplay(object sender, RoutedEventArgs e)
		{
			Settings.Current.ShowFrameNumber = !Settings.Current.ShowFrameNumber;
			Settings.Current.Save();
			viewModel?.UpdateTimecodeDisplay();
		}
	}
}
