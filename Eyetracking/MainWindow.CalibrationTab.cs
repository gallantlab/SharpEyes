using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NumSharp;
using Num = NumSharp.np;
using MessageBox = System.Windows.MessageBox;

namespace Eyetracking
{
	partial class MainWindow
	{
		/// <summary>
		/// Frame number at which the calibration sequence starts
		/// </summary>
		private int? calibrationStartFrame = null;
		
		/// <summary>
		/// Frame number at which the calibration
		/// </summary>
		private int calibrationEndFrame = 0;

		private NDArray gazePosition = null;

		private void OpenCalibrationParametersButton_Click(object sender, RoutedEventArgs e)
		{
			if (calibrator == null)
				calibrator = new Calibrator()
				{
					OnCalibrationFinished = this.OnCalibrationFinished
				};

			CalibrationParametersWindow calibrationParametersWindow =
				new CalibrationParametersWindow(calibrator.calibrationParameters);

			calibrationParametersWindow.ShowDialog();
		}

		private void CalibrationStartTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
		{
			e.Handled = Regex.Match(e.Text, "[0-9]{1,2}:[0-9]{1,2}:[0-9]{1,2}.[0-9]{1,3}").Success;
			CalibrateButton.IsEnabled = e.Handled;
		}

		private void CalibrateButton_Click(object sender, RoutedEventArgs e)
		{
			if (calibrator == null)
				calibrator = new Calibrator()
				{
					OnCalibrationFinished = this.OnCalibrationFinished
				};

			CalibrateButton.IsEnabled = false;

			// TODO: Convert progress bar thing be not indeterminate
			progressBar.Visibility = Visibility.Visible;
			progressBar.IsIndeterminate = true;
			taskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Indeterminate;
			calibrator.Calibrate(GetEyetrackingCalibrationPositions(calibrator.calibrationParameters));
		}

		private List<Point> GetEyetrackingCalibrationPositions(CalibrationParameters parameters)
		{
			List<Point> gazePositions = new List<Point>();

			if (calibrationStartFrame == null)
				calibrationStartFrame = pupilFinder.TimeStampToFrameNumber(CalibrationStartTime);
			
			for (int i = 0; i < parameters.calibrationPoints.Count; i++)
			{
				int startFrame = calibrationStartFrame.Value + i * parameters.calibrationDurationFrames + parameters.calibrationStartDelayFrames;
				int endFrame = startFrame + parameters.calibrationDurationFrames;
				startFrame += parameters.calibrationPointStartDelayFrames;

				gazePositions.Add(pupilFinder.GetMedianPupilLocation(startFrame, endFrame));
			}

			return gazePositions;
		}

		private void OnCalibrationFinished()
		{
			CalibrateButton.IsEnabled = true;
			CalibrationErrorTextBlock.Text = String.Format("Min RMS error {0}", calibrator.MinRMSError);

			taskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
			progressBar.Visibility = Visibility.Collapsed;
			progressBar.IsIndeterminate = false;
			MapPupilToGazeButton.IsEnabled = true;
		}

		/// <summary>
		/// Set the current frame as the start of the calibration sequence
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void MarkInButton_Click(object sender, RoutedEventArgs e)
		{
			calibrationStartFrame = pupilFinder.CurrentFrameNumber;
			Tuple<int, int, int, int> timestamp = pupilFinder.GetTimestampForFrame(calibrationStartFrame.Value);
			CalibrationStartTime = String.Format("{0:00}:{1:00}:{2:00}.{3:000}",
														 timestamp.Item1,
														 timestamp.Item2,
														 timestamp.Item3,
														 timestamp.Item4);
			CalibrationStartTextBox.Text = CalibrationStartTime;
			GazeStartTextBox.Text = CalibrationStartTextBox.Text;
			CalibrateButton.IsEnabled = true;
		}

		/// <summary>
		/// Set the current frame as the end of the calibration sequence
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void MarkOutButton_Click(object sender, RoutedEventArgs e)
		{
			calibrationEndFrame = pupilFinder.CurrentFrameNumber;
		}

		private async void MapPupilToGazeButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				int startFrame = pupilFinder.TimeStampToFrameNumber(GazeStartTime);
				gazePosition = await Task.Run(() => calibrator.MapPupilPositionToGazePosition(pupilFinder.pupilLocations[string.Format("{0}:, :2", startFrame)]));
				saveGazeTraceMenuItem.IsEnabled = true;
				OpenVideoForGazeButton.IsEnabled = true;
				StimulusViewWithGazeMenuitem.IsEnabled = true;
			}
			catch (InvalidOperationException)
			{
				MessageBox.Show("Timestamps not parsed");
			}
			catch (ArgumentException)
			{
				MessageBox.Show("Invalid timestamp format");
			}
		}

		/// <summary>
		/// Parses the eyetracking history file to get a list of first TRs
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void ReadHistoryFileButton_Click(object sender, RoutedEventArgs e)
		{
			
		}

		private void SaveGazeTrace()
		{
			if (gazePosition == null)
			{
				MessageBox.Show("Gaze not parsed yet");
				return;
			}
			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "Numpy file (*.npy)|*.npy",
				Title = "Save gaze...",
				FileName = Path.GetFileNameWithoutExtension(Path.Combine(Path.GetDirectoryName(pupilFinder.videoFileName),
																		 String.Format("{0} gaze.npy", Path.GetFileNameWithoutExtension(pupilFinder.videoFileName))))
			};
			if (saveFileDialog.ShowDialog() == true)
			{
				Num.save(saveFileDialog.FileName, gazePosition);
			}
		}

		private void OpenVideoForGazeButton_Click(object sender, RoutedEventArgs e)
		{
			OpenStimulusVideoWithGaze();
		}

		private void OpenStimulusVideoWithGaze()
		{
			if (gazePosition == null)
			{
				MessageBox.Show("Gaze not parsed yet");
				return;
			}
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				DefaultExt = ".mkv",
				Filter = "Stimulus videos (.avi, .mp4, .mkv)|*.avi;*.mp4;*.mkv",
				Title = "Open stimulus video..."
			};
			if (openFileDialog.ShowDialog() == true)
			{
				StimulusGazeViewer stimulusGazeViewer = new StimulusGazeViewer(openFileDialog.FileName, gazePosition);
				stimulusGazeViewer.Show();
			}
		}


		private void CalibrationStartTextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			try
			{
				GazeStartTextBox.Text = CalibrationStartTextBox.Text;
			}
			catch (NullReferenceException) { }

			CalibrateButton.IsEnabled =
				Regex.IsMatch(CalibrationStartTextBox.Text, "[0-9]{1,2}:[0-9]{1,2}:[0-9]{1,2}.[0-9]{1,3}");

		}

	}
}
