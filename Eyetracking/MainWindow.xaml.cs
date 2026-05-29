using Microsoft.Win32;
using Sentry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpenCvSharp;
using Point = System.Windows.Point;

namespace Eyetracking
{
	public enum EditingState
	{
		None,
		DrawingWindow,
		MovingPupil
	}

	public class TimestampRule : ValidationRule
	{
		public TimestampRule()
		{
		}

		public override ValidationResult Validate(object value, CultureInfo cultureInfo)
		{
			try
			{
				string timestamp = (string)value;
				if (!Regex.Match(timestamp, "[0-9]{1,2}:[0-9]{1,2}:[0-9]{1,2}.[0-9]{1,3}").Success)
					return new ValidationResult(false, "Invalid timestamp");
				return ValidationResult.ValidResult;
			}
			catch (Exception e)
			{
				return new ValidationResult(false, $"{e.Message}");
			}
		}
	}

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : System.Windows.Window
	{
		// == video play related stuff ==
		private PupilFinder pupilFinder = null;
		private bool isPlaying = false;
		private DispatcherTimer timer;
		public double videoScaleFactor { get; private set; }    // scaling fractor from video size to display size
		private int framesPerHour, framesPerMinute;


		// == eyetracking setup related stuff ==
		private bool isEditingStarted = false;  // generic flag for indicating whether the window/pupil edit has begun with a mouseclick
		private System.Windows.Point mouseMoveStartPoint;
		private bool isMovingSearchWindow = false;

		// data related stuff
		private EditingState _editingState = EditingState.None;
		private EditingState editingState
		{
			get { return _editingState; }
			set
			{
				_editingState = value;
			}
		}
		private bool isPupilManuallySetOnThisFrame = false;

		// Pupil X and Y set the _center_ of the ellipse
		// The outward-facing values, both returned and in the text labels,
		// Reflect the position in video frame space
		// PupilEllipse's values reflect values in screen space
		private double PupilX
		{
			get => Canvas.GetLeft(PupilEllipse) / videoScaleFactor + PupilRadius;
			set
			{
				Canvas.SetLeft(PupilEllipse, value * videoScaleFactor - PupilEllipse.Width / 2);
				XPositionText.Text = string.Format("X: {0:####.#}", value);
				if (value < 0 && PupilEllipse.IsVisible)
					PupilEllipse.Visibility = Visibility.Hidden;
				else if (value > 0 && !PupilEllipse.IsVisible)
					PupilEllipse.Visibility = Visibility.Visible;
			}
		}
		private double PupilY
		{
			get => Canvas.GetTop(PupilEllipse) / videoScaleFactor + PupilRadius; 
			set
			{
				Canvas.SetTop(PupilEllipse, value * videoScaleFactor - PupilEllipse.Height / 2);
				YPositionText.Text = string.Format("Y: {0:####.#}", value);
			}
		}
		private double PupilRadius
		{
			get => PupilEllipse.Height / 2 / videoScaleFactor; 
			set
			{
				if (value < pupilFinder.minRadius) // pupil hasn't been found yet so we just break
				{
					return;
				}

				double X = PupilX;
				double Y = PupilY;
				PupilEllipse.Width = PupilEllipse.Height = value * 2 * videoScaleFactor;
				PupilX = X;
				PupilY = Y;
				RadiusText.Text = string.Format("Radius: {0:####.#}", value);
			}
		}
		private double _pupilConfidence = 1;
		private double PupilConfidence
		{
			get => _pupilConfidence; 
			set
			{
				_pupilConfidence = value;
				ConfidenceText.Text = String.Format("Confidence: {0:#.####}", value);
				double transparency = value;
				if (transparency < 0.2)
				{
					transparency = 0.2;
				}
				else if (transparency > 1)
				{
					transparency = 1;
				}
				// during moving pupil, we deep the drawn pupil at 50% transparency
				if (editingState != EditingState.MovingPupil)
					PupilEllipse.Stroke.Opacity = ShowPupilOpacityCheckBox.IsChecked.Value ? transparency * 0.75 : 0.75;
			}
		}

		public bool IsPlaying
		{
			get => isPlaying; 
			set
			{
				isPlaying = value;
				if (value)
				{
					PlayPauseButton.Content = "Pause";
					timer.Start();
				}
				else
				{
					PlayPauseButton.Content = "Play";
					timer.Stop();
				}
			}
		}

		public double MillisecondsPerFrame { get; private set; } = 0;
		private TimeSpan timePerFrame;

		public bool HasFile
		{
			get => pupilFinder.videoFileName != null;
		}

		// == mapping from eyetracking video space to stimulus video space ==
		private Calibrator calibrator = null;
		public string CalibrationStartTime { get; set; } = "00:00:00.000";
		public string GazeStartTime { get; set; } = "00:00:00.000";

		public MainWindow()
		{
			InitializeComponent();

			Canvas.SetLeft(SearchWindowRectangle, 0);
			Canvas.SetTop(SearchWindowRectangle, 0);

			// update visibility
			TemplateCountComboBox_SelectionChanged(null, null);
		}

		public void SetStatus(string status = null)
		{
			StatusText.Text = status ?? "Idle";
		}

		private void LoadFile(string videoFileName)
		{
			SetStatus("Loading");
			if (TemplatePupilFindingRadioButton.IsChecked.Value)
				pupilFinder = new TemplatePupilFinder(videoFileName, progressBar, taskbarItemInfo, SetStatus,
					UpdateDisplays, OnFramesProcessed)
				{
					NumMatches = NumMatchesPicker.Value.Value,
					TemplateMatchMode = (TemplateMatchModes) MatchModeComboBox.SelectedIndex
				};
			else
				pupilFinder = new HoughPupilFinder(videoFileName, progressBar, taskbarItemInfo, SetStatus, UpdateDisplays, OnFramesProcessed);
			RadiusPickerValuesChanged(null, null);
			isPupilManuallySetOnThisFrame = false;

			VideoNameStatus.Text = videoFileName;
			VideoDurationStatus.Text = FramesToDurationString(pupilFinder.frameCount, pupilFinder.fps);
			VideoSizeStatus.Text = string.Format("{0}×{1}", pupilFinder.width, pupilFinder.height);
			FPSStatus.Text = string.Format("{0:##} fps", pupilFinder.fps);

			videoScaleFactor = VideoFrameImage.Width / pupilFinder.width;

			MillisecondsPerFrame = 1000.0 / pupilFinder.fps;
			timePerFrame = TimeSpan.FromMilliseconds(MillisecondsPerFrame);
			framesPerHour = pupilFinder.fps * 60 * 60;
			framesPerMinute = pupilFinder.fps * 60;

			VideoSlider.Maximum = pupilFinder.frameCount - 1;

			timer = new DispatcherTimer(DispatcherPriority.Render);
			timer.Interval = timePerFrame;
			timer.Tick += VideoPlayTimerTick;

			isPlaying = false;
			FindPupilsButton.IsEnabled = false;
			ReadTimestampButton.IsEnabled = true;
			LoadSavedTimeStampsButton.IsEnabled = true;
			saveEyetrackingMenuItem.IsEnabled = true;
			AutoAddCustomTemplateCheckBox.IsChecked = false;

			// force update displays because
			PupilX = PupilX;
			PupilY = PupilY;
			PupilRadius = PupilRadius;

			// enable scrubbing buttongs
			PreviousFrameButton.IsEnabled = true;
			PlayPauseButton.IsEnabled = true;
			NextFrameButton.IsEnabled = true;
			StepBackButton.IsEnabled = true;

			// if the pupil finder found auto save files
			// we immediately enable pupil finding
			List<string> autoloads = new List<string>(); ;
			if (pupilFinder.isTimestampParsed)
			{
				FindPupilsButton.IsEnabled = true;
				autoloads.Add("timestamps");
			}

			if (pupilFinder is TemplatePupilFinder templatePupilFinder)
			{
				TemplatePreviewIndex = 0;
				loadSavedTemplatesMenuItem.IsEnabled = true;
				if (templatePupilFinder.IsUsingCustomTemplates)
				{
					saveTemplatesMenuItem.IsEnabled = true;
					ResetTemplatesButton.IsEnabled = true;
					autoloads.Add("templates");

					if (templatePupilFinder.NumTemplates > 1)
						DeleteTemplateButton.Visibility = Visibility.Visible;
				}
			}

			if (pupilFinder.isAnyFrameProcessed)
			{
				autoloads.Add("pupils");
			}

			switch (autoloads.Count)
			{
				case 1:
					SetStatus(String.Format("Autoloaded {0}", autoloads[0]));
					break;
				case 2:
					SetStatus(String.Format("Autoloaded {0} & {1}", autoloads[0], autoloads[1]));
					break;
				case 3:
					SetStatus("Autoloaded timestamps, templates, & pupils");
					break;
				default:
					SetStatus();
					break;
			}

			pupilFinder.ReadGrayscaleFrame();
			UpdateDisplays();
			pupilFinder.Seek(0);
			saveAllMenuItem.IsEnabled = true;

			CalibrateButton.IsEnabled = false;
			MapPupilToGazeButton.IsEnabled = false;

			OpenVideoForGazeButton.IsEnabled = false;
			StimulusViewWithGazeMenuitem.IsEnabled = false;

			UpdateFramesProcessedPreviewImage();
		}

		/// <summary>
		/// Aggregate handle for the multitude of UI controls that scrub through the video
		/// </summary>
		/// <param name="frame">Frame to set the video to</param>
		private void UpdateVideoTime(int frame)
		{
			pupilFinder.CurrentFrameNumber = frame;
			UpdateDisplays();
		}

		private void VideoPlayTimerTick(object sender, EventArgs e)
		{
			if (pupilFinder.CurrentFrameNumber >= pupilFinder.frameCount - 1)
			{
				// stop if we reached the end
				PlayPauseButton_Click(null, null);
			}
			pupilFinder.ReadGrayscaleFrame();
			UpdateDisplays();
		}

		/// <summary>
		/// Updates the displays pupil and video information
		/// </summary>
		private void UpdateDisplays()
		{
			try
			{
				int frames = pupilFinder.CurrentFrameNumber;
				VideoSlider.Value = frames;

				int hours = frames / framesPerHour;
				frames -= hours * framesPerHour;
				int minutes = frames / framesPerMinute;
				frames -= minutes * framesPerMinute;
				int seconds = frames / pupilFinder.fps;
				frames -= seconds * pupilFinder.fps;
				VideoTimeLabel.Content = String.Format("{0:00}:{1:00}:{2:00};{3:#00}", hours, minutes, seconds, frames + 1);
				VideoFrameImage.Source = pupilFinder.GetFrameForDisplay(ShowFilteredVideoButton.IsChecked.Value);

				PupilX = pupilFinder.pupilLocations[pupilFinder.CurrentFrameNumber, 0];
				PupilY = pupilFinder.pupilLocations[pupilFinder.CurrentFrameNumber, 1];
				PupilRadius = pupilFinder.pupilLocations[pupilFinder.CurrentFrameNumber, 2];
				PupilConfidence = pupilFinder.pupilLocations[pupilFinder.CurrentFrameNumber, 3];

				// at some point check if this could be moved into an arg and passed in from the pupil finders,
				// which would need some change in the delegate signature
				isPupilManuallySetOnThisFrame = false;
			}
			catch (Exception e)
			{
				MessageBox.Show(e.ToString(), "Exception", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void DrawWindowButton_Click(object sender, RoutedEventArgs e)
		{
			if (editingState == EditingState.DrawingWindow)
			{
				editingState = EditingState.None;
				movePupilEllipseButton.IsChecked = false;
				drawWindowButton.IsChecked = false;
				isEditingStarted = false;
			}
			else
			{
				editingState = EditingState.DrawingWindow;
				movePupilEllipseButton.IsChecked = false;
				drawWindowButton.IsChecked = true;
			}
		}

		private void MovePupilEllipseButton_Click(object sender, RoutedEventArgs e)
		{
			if (editingState == EditingState.MovingPupil)
			{
				editingState = EditingState.None;
				movePupilEllipseButton.IsChecked = false;
				drawWindowButton.IsChecked = false;
				isEditingStarted = false;
			}
			else
			{
				editingState = EditingState.MovingPupil;
				movePupilEllipseButton.IsChecked = true;
				drawWindowButton.IsChecked = false;
			}
		}

		private void OpenVideoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			OpenFileMenuItem_Click(sender, e);
		}

		private void OpenEyetrackingDataCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			LoadSavedDataMenuItem_Click(null, null);
		}

		private void NewFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{

		}

		private void SaveFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{

		}

		private void SaveFileAsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{

		}

		private void ReadTimestampButton_Click(object sender, RoutedEventArgs e)
		{
			if (pupilFinder != null)
			{
				if (pupilFinder.isTimestampParsed && MessageBox.Show("Timestamps are already parsed. Redo?", "Existing timestamps", MessageBoxButton.OKCancel) == MessageBoxResult.Cancel)
					return;


				ReadTimestampButton.IsEnabled = false;
				LoadSavedTimeStampsButton.IsEnabled = false;
				pupilFinder.OnTimeStampsFound += delegate (bool warn, string message, bool stepback)
												{
													ReadTimestampButton.IsEnabled = true;
													LoadSavedTimeStampsButton.IsEnabled = true;
													if (AutoSaveCheckBox.IsChecked.Value)
														pupilFinder.SaveTimestamps();
													if (warn)
														MessageBox.Show(message, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
												};
				pupilFinder.Seek(0);
				pupilFinder.ParseTimeStamps();
				FindPupilsButton.IsEnabled = true;
				saveTimestampsMenuItem.IsEnabled = true;
			}
		}

		private void FindPupils(int frames, object sender)
		{
			// disable pupil editing if needed
			if (editingState == EditingState.MovingPupil)
			{
				editingState = EditingState.None;
				movePupilEllipseButton.IsChecked = false;
				drawWindowButton.IsChecked = false;
				isEditingStarted = false;
			}

			if (pupilFinder is TemplatePupilFinder templatePupilFinder)
			{
				templatePupilFinder.fractionToUse = PercentTemplatesToUsePicker.Value.Value;
				templatePupilFinder.pupilBrightnessThreshold = PupilStdevThresholdPicker.Value.Value;
				templatePupilFinder.windowBrightnessThreshold = BlinkStdevThresholdPicker.Value.Value;
			}

			if (AutoAddCustomTemplateCheckBox.IsChecked.Value && isPupilManuallySetOnThisFrame)
			{
				UseImageAsTemplateButton_Click(null, null);
			}

			double l = Canvas.GetLeft(SearchWindowRectangle);
			pupilFinder.left = (int)(Canvas.GetLeft(SearchWindowRectangle) / canvas.Width * pupilFinder.width);
			pupilFinder.right = (int)(SearchWindowRectangle.Width / canvas.Width * pupilFinder.width) + pupilFinder.left;
			pupilFinder.top = (int)(Canvas.GetTop(SearchWindowRectangle) / canvas.Height * pupilFinder.height);
			pupilFinder.bottom = (int)(SearchWindowRectangle.Height / canvas.Height * pupilFinder.height) + pupilFinder.top;

			UpdatePupilFindingButtons(true);
			
			if (frames + pupilFinder.CurrentFrameNumber >= pupilFinder.frameCount)
			{
				frames = pupilFinder.frameCount - pupilFinder.CurrentFrameNumber - 1;
			}

			pupilFinder.FindPupils(frames, AutoPausePupilFindingCheckBox.IsChecked.Value ? ConfidenceThresholdPicker.Value.Value : 0,
								   AutoPausePupilFindingCheckBox.IsChecked.Value ? ConfidenceThresholdFramesPicker.Value.Value : 0,
								   AutoPausePupilFindingCheckBox.IsChecked.Value ? DoNotStopForBlinksButton.IsChecked.Value : false);
		}

		private void LoadTimestamps()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Numpy file (*.npy)|*.npy",
				Title = "Load timestamps..."
			};
			if (openFileDialog.ShowDialog() == true)
			{
				pupilFinder.LoadTimestamps(openFileDialog.FileName);
				FindPupilsButton.IsEnabled = true;
			}
		}

		private void SaveTimestamps()
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "Numpy file (*.npy)|*.npy",
				Title = "Save timestamps...",
				FileName = Path.GetFileNameWithoutExtension(pupilFinder.autoTimestampFileName)
			};
			if (saveFileDialog.ShowDialog() == true)
			{
				pupilFinder.SaveTimestamps(saveFileDialog.FileName);
			}
		}

		private void ImageFilterValuesChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
		{
			if (pupilFinder == null)
			{
				return;
			}

			pupilFinder.bilateralBlurSize = BilateralBlurSizePicker.Value.Value;
			pupilFinder.bilateralSigmaColor = BilateralSigmaColorPicker.Value.Value;
			pupilFinder.bilateralSigmaSpace = BilateralSigmaSpacePicker.Value.Value;
			pupilFinder.medianBlurSize = MedianBlurSizePicker.Value.Value;
			pupilFinder.FilterCurrentFrame();

			VideoFrameImage.Source = pupilFinder.GetFrameForDisplay(true);
		}

		private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (SettingsTabs.SelectedIndex == 1)
			{
				ShowFilteredVideoButton.IsChecked = true;
				ShowFilteredVideoButton.IsEnabled = false;
				ImageFilterValuesChanged(null, null);
			}
			else
			{
				try
				{
					ShowFilteredVideoButton.IsChecked = false;
					ShowFilteredVideoButton.IsEnabled = true;
					VideoFrameImage.Source = pupilFinder.GetFrameForDisplay(false);
				}
				catch (Exception) { }
			}; // null reference during initialization
		}

		/// <summary>
		/// Update found pupil positions after the values are manually adjusted
		/// </summary>
		private void UpdatePupilPositionData()
		{
			if (pupilFinder != null)
			{
				PupilConfidence = 1;
				int frameDecay;
				ManualUpdateMode mode;
				if (LinearDecayRadioButton.IsChecked.Value)
				{
					frameDecay = LinearFadeFramesPicker.Value.Value;
					mode = ManualUpdateMode.Linear;
				}
				else
				{
					frameDecay = ExponentialFadeFramePicker.Value.Value;
					mode = ManualUpdateMode.Exponential;
				}
				pupilFinder.ManuallyUpdatePupilLocations(pupilFinder.CurrentFrameNumber, PupilX, PupilY, PupilRadius, frameDecay, mode);
			}
			isPupilManuallySetOnThisFrame = true;
		}

		private void SetTemplatePreviewImage()
		{
			if (pupilFinder is TemplatePupilFinder templatePupilFinder)
			{
				TemplatePreviewImage.Source = templatePupilFinder.GetTemplateImage();
			}
		}

		/// <summary>
		/// Called when a chunk of frames is processed
		/// </summary>
		public void OnFramesProcessed(bool warn, string message, bool stepBack)
		{
			UpdatePupilFindingButtons(false);
			UpdateFramesProcessedPreviewImage();
			if (AutoSaveCheckBox.IsChecked.Value)
			{
				pupilFinder.SavePupilLocations();
				pupilFinder.SaveTimestamps();
				if (pupilFinder is TemplatePupilFinder templatePupilFinder)
					templatePupilFinder.SaveTemplates();
			}
			if (warn)
				MessageBox.Show(message, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
			if (stepBack)
				UpdateVideoTime(pupilFinder.CurrentFrameNumber - ConfidenceThresholdFramesPicker.Value.Value / 2); // don't fully step back because we want the bad frames in saccades
		}

		private void ExponentialFadeFramePicker_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
		{
			if (pupilFinder == null)
			{
				return;
			}

			double oldX = pupilFinder.pupilLocations[pupilFinder.CurrentFrameNumber, 0];
			double oldY = pupilFinder.pupilLocations[pupilFinder.CurrentFrameNumber, 1];
			double dD = Math.Sqrt((oldX - PupilX) * (oldX - PupilX) + (oldY - PupilY) * (oldY - PupilY));
			if (dD < 1)
			{
				ExponentialTotalFadeFrameLabel.Content = String.Format("Position unchanged");
			}
			else
			{
				int frames = (int)(ExponentialFadeFramePicker.Value.Value * Math.Log(dD));
				ExponentialTotalFadeFrameLabel.Content = String.Format("{0} frames total", frames);
			}
		}

		private void TabControl_SelectionChanged_1(object sender, SelectionChangedEventArgs e)
		{
			if (ManualAdjustmentDecayOptionsTabs.SelectedIndex == 1)
			{
				ExponentialFadeFramePicker_ValueChanged(null, null);
			}
		}

		private void AdjustmentModeSelected(object sender, RoutedEventArgs e)
		{
			if (ManualAdjustmentDecayOptionsTabs == null)
			{
				return; // preconstruction references not set
			}

			if (LinearDecayRadioButton.IsChecked.Value)
			{
				ManualAdjustmentDecayOptionsTabs.SelectedIndex = 0;
			}
			else
			{
				ManualAdjustmentDecayOptionsTabs.SelectedIndex = 1;
			}
		}

		/// <summary>
		/// Changes the appearance of Find Pupils/Find Pupils All Frames depending on state
		/// </summary>
		/// <param name="isPupilFinding">Are we entering pupil finding mode?</param>
		private void UpdatePupilFindingButtons(bool isPupilFinding)
		{
			FindPupilsButton.Content = isPupilFinding ? "Cancel" : "Find Pupils";
			if (isPupilFinding)
			{
				FindPupilsButton.Click -= FindPupilsButton_Click;
				FindPupilsButton.Click += CancelPupilFindingButton_Click;
			}
			else
			{
				FindPupilsButton.Click += FindPupilsButton_Click;
				FindPupilsButton.Click -= CancelPupilFindingButton_Click;
				if (AutoStartPupilEditModeCheckBox.IsChecked.Value)
				{
					editingState = EditingState.MovingPupil;
					movePupilEllipseButton.IsChecked = true;
				}
			}
			StepBackButton.IsEnabled = !isPupilFinding;
		}

		private void NextFrameCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			NextFrameButton_Click(null, null);
		}

		private void PrevFrameCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			PreviousFrameButton_Click(null, null);
		}

		private void PlayPauseCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
            FindPupilsButton_Click(null, null);
		}

		private void SaveAllMenuItem_Click(object sender, RoutedEventArgs e)
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "Directory | directory",
				Title = "Save all into folder...",
			};
			if (saveFileDialog.ShowDialog() == true)
			{
				pupilFinder.SaveTimestamps(Path.Combine(Path.GetDirectoryName(saveFileDialog.FileName),
														String.Format("{0} timestamps.npy", Path.GetFileNameWithoutExtension(pupilFinder.videoFileName))));
				pupilFinder.SavePupilLocations(Path.Combine(Path.GetDirectoryName(saveFileDialog.FileName),
															String.Format("{0} pupils.npy", Path.GetFileNameWithoutExtension(pupilFinder.videoFileName))));
				if (pupilFinder is TemplatePupilFinder templatePupilFinder)
				{
					templatePupilFinder.SaveTemplates(Path.Combine(Path.GetDirectoryName(saveFileDialog.FileName),
																   String.Format("{0} templates.dat", Path.GetFileNameWithoutExtension(pupilFinder.videoFileName))));
				}
			}
		}

		private void MovePupilUp(object sender, ExecutedRoutedEventArgs e)
		{
			if (PupilY >= 1)
			{
				PupilY--;
				UpdatePupilPositionData();
			}
		}

		private void MovePupilDown(object sender, ExecutedRoutedEventArgs e)
		{
			if (PupilY <= pupilFinder.height - 2)
			{
				PupilY++;
				UpdatePupilPositionData();
			}
		}

		private void MovePupilLeft(object sender, ExecutedRoutedEventArgs e)
		{
			if (PupilX >= 1)
			{
				PupilX--;
				UpdatePupilPositionData();
			}
		}

		private void MovePupilRight(object sender, ExecutedRoutedEventArgs e)
		{
			if (PupilX <= pupilFinder.width - 2)
			{
				PupilX++;
				UpdatePupilPositionData();
			}
		}

		private void IncreasePupilSizeCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			if (editingState == EditingState.MovingPupil)
			{
				double newSize = PupilRadius + 1;
				if (newSize > pupilFinder.maxRadius)
				{
					return;
				}

				PupilRadius = newSize;
				UpdatePupilPositionData();
			}
		}

		private void DecreasePupilSizeCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			if (editingState == EditingState.MovingPupil)
			{
				double newSize = PupilRadius - 1;
				if (newSize < pupilFinder.minRadius)
				{
					return;
				}

				PupilRadius = newSize;
				UpdatePupilPositionData();
			}
		}

		private void ShowFilteredVideoButton_Checked(object sender, RoutedEventArgs e)
		{
			UpdateDisplays();
		}

		private void DrawWindowCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			DrawWindowButton_Click(null, null);
		}

		private void MovePupilCommand_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			MovePupilEllipseButton_Click(null, null);
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (AutoSaveOnExitMenuItem.IsChecked && pupilFinder != null)
			{
				pupilFinder.SaveTimestamps();
				pupilFinder.SavePupilLocations();
				if (pupilFinder is TemplatePupilFinder templatePupilFinder)
					templatePupilFinder.SaveTemplates();
			}
		}

		private void StimulusViewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            StimulusGazeViewer stimulusGazeViewer = new StimulusGazeViewer();
            stimulusGazeViewer.Show();
        }

		private void BackConfidenceFrames_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			GoBackNumThresholdFramesButton_Click(null, null);
		}

		private void BackFindPupilFrames_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			StepBackButton_Click(null, null);
		}

		private void ForwardConfidenceFrames_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			UpdateVideoTime(pupilFinder.CurrentFrameNumber + ConfidenceThresholdFramesPicker.Value.Value);
		}

		private void ForwardFindPupilFrames_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			UpdateVideoTime(pupilFinder.CurrentFrameNumber + FramesToProcessPicker.Value.Value);
		}

		private void NumMatchesPicker_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
		{
			try
			{
				TemplatesLabel.Content = NumMatchesPicker.Value > 1 ? "templates" : "template";
				if (pupilFinder is TemplatePupilFinder templatePupilFinder)
					templatePupilFinder.NumMatches = NumMatchesPicker.Value.Value;
			}
			catch (NullReferenceException)	// initialization time gets this
			{
			}
		}
	

		private void MatchModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (pupilFinder is TemplatePupilFinder templatePupilFinder)
				templatePupilFinder.TemplateMatchMode = (TemplateMatchModes)MatchModeComboBox.SelectedIndex;
		}

		private void AddTemplate_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			UseImageAsTemplateButton_Click(null, null);
		}

		private void StimulusViewWithGazeMenuitem_Click(object sender, RoutedEventArgs e)
		{
			OpenStimulusVideoWithGaze();
		}

		private void UpdateFramesProcessedPreviewImage()
		{
			FramesProcessedPreviewImage.Source = (pupilFinder == null) ? null : pupilFinder.GetFramesProcessedPreviewImage((int)PreviewImageGrid.ActualWidth);
		}
	}
}
