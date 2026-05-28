using NumSharp;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using MessageBox.Avalonia;
using MessageBox.Avalonia.BaseWindows.Base;
using MessageBox.Avalonia.Enums;
using SharpEyes.ViewModels;
using Num = NumSharp.np;
using SharpEyes.Models;

namespace Eyetracking
{
	/// <summary>
	/// Delegate for SetStatus from the main window
	/// </summary>
	/// <param name="status"></param>
	public delegate void SetStatusDelegate(string status = null);

	/// <summary>
	/// Delegate for things to do when a the pupil is found on a frame
	/// </summary>
	public delegate void FrameProcessedDelegate();

	/// <summary>
	///  Delegate for things to do when a chunk of frames is processed.
	/// FramesProcessed is called on every frame. This is called only once
	/// per click of the Find Frames button.
	/// </summary>
	/// <param name="error">Did an error stop frame finding?</param>
	/// <param name="message">Any message to display?</param>
	/// <param name="stepBack">Do we need to step back to when confidence was still above the threshold?</param>
	public delegate void FramesProcessedDelegate(bool error = false, string message = null, bool stepBack = false);

	/// <summary>
	/// Delegate to be called for cancelling pupil finding
	/// </summary>
	public delegate void CancelPupilFindingDelegate();

	/// <summary>
	/// How should values be updated using manual adjustments?
	/// </summary>
	public enum ManualUpdateMode
	{
		Linear,         // fade linearly
		Exponential,    // fade exponentially
	}

	public abstract class PupilFinder : VideoReader
	{
		// video information
		public string autoTimestampFileName
		{
			get
			{
				if (videoFileName == null) return null;
				return Path.Combine(Path.GetDirectoryName(videoFileName),
									String.Format("{0} Timestamps.npy", Path.GetFileNameWithoutExtension(videoFileName)));
			}
		}
		public string autoPupilsFileName
		{
			get
			{
				if (videoFileName == null) return null;
				return Path.Combine(Path.GetDirectoryName(videoFileName),
									String.Format("{0} Pupils.npy", Path.GetFileNameWithoutExtension(videoFileName)));
			}
		}

		public PupilFindingUserControlViewModel? ViewModel { get; set; } = null;

		// parsing video stuff		
		// NOTE: IT LOOKS LIKE VideoCaptureProperties.PosFrames is 1-INDEXED!!
		// or rather, like my previews implementation of CurrentFrameNumber, it is the 0-indexed frame number
		// of the upcoming frame.
		protected Mat[] colorChannels = new Mat[3];
		protected Mat red;
		public bool isTimestampParsed { get; private set; } = false;

		/// <summary>
		/// Lazy flag in case for some reason we need to get the same frame twice
		/// </summary>
		private bool isCVFrameConverted = false;
		private bool isBitmapFrameGrayscale = false;

		// pupil finding stuff
		public int left => ViewModel.PupilWindowLeft;
		public int right => left + ViewModel.PupilWindowWidth;
		public int top => ViewModel.PupilWindowTop;
		public int bottom => top + ViewModel.PupilWindowHeight;
		public int minRadius => (int)((double)ViewModel.MinPupilDiameter / 2);               // min/max pupil sizes
		public int maxRadius => (int)((double)ViewModel.MaxPupilDiameter / 2);
		public int nThreads = 1;
		public double WindowBrightness
		{
			get
			{
				if (!filteredFrame.Empty())
					return ((double)filteredFrame.Sum()) / (double)(filteredFrame.Height * filteredFrame.Width);
				else return 0;
			}
		}

		/// <summary>
		/// pupilLocations is a time x 4 array in which the columns are [X, Y, Radius, Confidence.]
		/// Confidence is correlation value from the template finder and whatever that value is
		/// from Hough circle. If set to 2, then this indicates that it is a frame in which a manual
		/// adjustment was made.
		/// </summary>
		public NDArray pupilLocations { get; protected set; } = null;
		private NDArray timeStamps = null;
		protected Mat grayFrame = new Mat();
		protected Mat filteringFrame = new Mat();   // helper for filtering
		protected Mat filteredFrame = new Mat();	// processed frame in which to find Pupils
		protected bool[] isFrameProcessed;          // has each frame been processed?
		public bool AreAllFramesProcessed           // has all frames been processed?
		{
			get
			{
				for (int i = 0; i < frameCount; i++)
				{
					if (!isFrameProcessed[i])
					{
						return false;
					}
				}

				return true;
			}
		}
		public bool isAnyFrameProcessed { get; protected set; } = false;

		public int bilateralBlurSize => ViewModel.UseBilateralBlur ? ViewModel.BilateralBlurSize : 0;
		public int medianBlurSize => ViewModel.UseMedianBlur ? ViewModel.MedianBlurSize : 0;
		public double bilateralSigmaColor => ViewModel.BilateralBlurSigmaColor;
		public double bilateralSigmaSpace => ViewModel.BilateralBlurSigmaSpace;

		// UI delegates/references
		public SetStatusDelegate SetStatusDelegate { get; private set; }
		public FrameProcessedDelegate UpdateFrameDelegate;
		public FramesProcessedDelegate OnFramesPupilsProcessedDelegate; // delegate for when Pupils are found in a chunk of frames
		public CancelPupilFindingDelegate CancelPupilFindingDelegate;	// delegate for interrupting pupil finding

		public PupilFinder(string videoFileName, PupilFindingUserControlViewModel viewModel = null)
			: base(videoFileName)
		{
			ViewModel = viewModel;
			SetStatusDelegate = SetStatus;
			UpdateFrameDelegate = UpdateFrame;
			OnFramesPupilsProcessedDelegate = OnFramesProcessed;

			// try to auto load stuff if they exist
			if (File.Exists(autoTimestampFileName))
			{
				LoadTimestamps(autoTimestampFileName);
				isTimestampParsed = true;
			}
			else
				timeStamps = Num.zeros((frameCount, 4), NPTypeCode.Int32);

			ViewModel.IsTimestampsRead = isTimestampParsed;

			if (File.Exists(autoPupilsFileName))
				LoadPupilLocations(autoPupilsFileName);
			else
			{
				pupilLocations = Num.zeros((frameCount, 4), NPTypeCode.Double);
				pupilLocations *= Num.NaN;	// use NaN to indicate pupil not yet found on this frame
			}

			for (int i = 0; i < 3; i++)
			{
				colorChannels[i] = new Mat();
			}

			red = colorChannels[0];

			isFrameProcessed = new bool[frameCount];
			for (int i = 0; i < frameCount; i++)
			{
				isFrameProcessed[i] = false;
			}

			ViewModel.TotalVideoFrames = frameCount;
			ViewModel.TimeFormatter = Settings.Current.ShowFrameNumber
				? (Func<int, string>)(frame => String.Format("Frame {0}", frame))
				: (Func<int, string>)FramesToTimecode;
			ViewModel.TotalVideoTime = ViewModel.TimeFormatter(frameCount);
			ViewModel.PupilWindowTop = 0;
			ViewModel.PupilWindowLeft = 0;
			ViewModel.PupilWindowWidth = width;
			ViewModel.PupilWindowHeight = height;
			ViewModel.VideoHeight = height;
			ViewModel.VideoWidth = width;
		}

		protected override void OnCurrentFrameNumberSet()
		{
			ReadGrayscaleFrame();
		}

		/// <summary>
		/// Uses the viewmodel to figure out the parameters
		/// </summary>
		public virtual void FindPupils()
		{
			int frames = ViewModel.ProcessAllFrames ? frameCount - CurrentFrameNumber : ViewModel.FramesToProcess;
			FindPupils(frames, ViewModel.StopOnLowConfidence ? ViewModel.LowConfidenceThreshold : 0, 
				ViewModel.LowConfidenceFrameCountThreshold, ViewModel.EnableBlinkRejection);
		}

		/// <summary>
		/// Find Pupils in some set of frames. Must be overridden in child classes.
		/// Will auto-pause if average confidence is below the threshold for the specified number of frames
		/// </summary>
		/// <param name="frames"> number of frames from current to find Pupils for </param>
		/// <param name="threshold"> confidence threshold at which to auto-pause pupil finding</param>
		/// <param name="thresholdFrames"> confidence threshold duration at which to auto pause pupil finding</param>
		/// <param name="doNotStopForBlink"></param>
		public virtual void FindPupils(int frames, double threshold = 0, int thresholdFrames = 0, bool doNotStopForBlink = false)
		{
			if (!isTimestampParsed)
			{
				ParseTimeStamps();
			}
		}


		/// <summary>
		/// Parses Timestamps from the video
		/// </summary>
		public void ParseTimeStamps()
		{
			ViewModel.CanPlayVideo = false;
			Seek(0);
			SetStatusDelegate("Parsing Timestamps 0%");
			DateTime start = DateTime.Now;
			BackgroundWorker worker = new BackgroundWorker
			{
				WorkerReportsProgress = true
			};
			worker.DoWork += delegate (object sender, DoWorkEventArgs args)
			{
				for (int i = 0; i < frameCount; i++)
				{
					if (!videoSource.Read(cvFrame)) break ;
					Cv2.Split(cvFrame, out colorChannels);
					timeStamps[i, 0] = Templates.MatchDigit(colorChannels[2][195, 207, 7, 15]) * 10 + Templates.MatchDigit(colorChannels[2][195, 207, 15, 23]);     // hours
					timeStamps[i, 1] = Templates.MatchDigit(colorChannels[2][195, 207, 35, 43]) * 10 + Templates.MatchDigit(colorChannels[2][195, 207, 43, 51]);    // minutes

					if (Templates.SecondsMarkerMatch(colorChannels[2][195, 207, 103, 111])) // check for seconds marker location
					{
						timeStamps[i, 2] = Templates.MatchDigit(colorChannels[2][195, 207, 67, 75]);            // a single seconds digit
						timeStamps[i, 3] = Templates.MatchDigit(colorChannels[2][195, 207, 79, 87]) * 100 +
										   Templates.MatchDigit(colorChannels[2][195, 207, 87, 95]) * 10 +
										   Templates.MatchDigit(colorChannels[2][195, 207, 95, 103]);
					}
					else
					{
						timeStamps[i, 2] = Templates.MatchDigit(colorChannels[2][195, 207, 67, 75]) * 10 + Templates.MatchDigit(colorChannels[2][195, 207, 75, 83]);
						timeStamps[i, 3] = Templates.MatchDigit(colorChannels[2][195, 207, 87, 95]) * 100 +
										   Templates.MatchDigit(colorChannels[2][195, 207, 95, 103]) * 10 +
										   Templates.MatchDigit(colorChannels[2][195, 207, 103, 111]);
					}
					colorChannels[0].Dispose();
					colorChannels[1].Dispose();
					colorChannels[2].Dispose();

					((BackgroundWorker)sender).ReportProgress((i + 1) * 100 / frameCount);
				}
			};

			worker.ProgressChanged += delegate (object sender, ProgressChangedEventArgs e)
			{
				SetStatusDelegate(string.Format("Parsing Timestamps {0}%", e.ProgressPercentage));
				ViewModel.ProgressBarValue = e.ProgressPercentage;
			};

			worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
			{
				TimeSpan elapsed = DateTime.Now - start;
				SetStatusDelegate(string.Format("Idle. {0} frames processed in {1:c} ({2} fps)", frameCount, elapsed, (int)(frameCount / elapsed.TotalSeconds)));
				ViewModel.IsProgressBarVisible = false;
				// seek to beginning
				CurrentFrameNumber = 0;
				isTimestampParsed = true;
				SaveTimestamps();

				bool warn = false;
				string message = null;
				int i = 0;
				int lastTime = timeStamps[i, 3] + 1000 * (timeStamps[i, 2] + 60 * timeStamps[i, 1] + 3600 * timeStamps[i, 0]);
				int thisTime;
				for (i = 1; i < timeStamps.shape[0]; i++)
				{
					thisTime = timeStamps[i, 3] + 1000 * (timeStamps[i, 2] + 60 * timeStamps[i, 1] + 3600 * timeStamps[i, 0]);
					if (thisTime <= lastTime)   // Timestamps should always increment
					{
						ShowMessageBox("Warning", "Parsed Timestamps are not incrementing", icon: Icon.Warning);
						break;
					}
				}

				ViewModel.IsTimestampsRead = true;
				ViewModel.CanPlayVideo = true;
			};

			ViewModel.IsProgressBarVisible = true;
			worker.RunWorkerAsync();

		}

		/// <summary>
		/// Reads the next frame and makes it grayscale
		/// Also does any needed filtering
		/// </summary>
		/// <returns></returns>
		public bool ReadGrayscaleFrame()
		{
			bool success = ReadFrame();
			if (success)
			{
				Cv2.CvtColor(cvFrame, grayFrame, ColorConversionCodes.RGB2GRAY);
				FilterCurrentFrame();
			}
			return success;
		}

		public void UpdateDisplays()
		{
			ViewModel.CurrentVideoFrame = CurrentFrameNumber;
			ViewModel.CurrentVideoTime = ViewModel.TimeFormatter(CurrentFrameNumber);
			ViewModel.VideoFrame = GetFrameForDisplay(ViewModel.ShowFilteredImage);
			ViewModel.PupilX = pupilLocations[CurrentFrameNumber, 0];
			ViewModel.PupilY = pupilLocations[CurrentFrameNumber, 1];
			ViewModel.PupilDiameter = pupilLocations[CurrentFrameNumber, 2] * 2;
			ViewModel.PupilConfidence = pupilLocations[CurrentFrameNumber, 3];
		}

		public void FilterCurrentFrame()
		{
			grayFrame.CopyTo(filteredFrame);
			if (bilateralBlurSize > 0)
			{
				filteringFrame = filteredFrame.BilateralFilter(bilateralBlurSize, bilateralSigmaColor, bilateralSigmaSpace);
				filteringFrame.CopyTo(filteredFrame);
				filteringFrame = new Mat();
			}
			if (medianBlurSize > 1)
			{
				filteringFrame = filteredFrame.MedianBlur(medianBlurSize);
				filteringFrame.CopyTo(filteredFrame);
				filteringFrame = new Mat();
			}
			isCVFrameConverted = false;
		}

		public void SaveTimestamps(string fileName = null)
		{
			fileName = fileName ?? this.autoTimestampFileName;
			Num.save(fileName, timeStamps);
		}

		public void SavePupilLocations(string fileName = null)
		{
			fileName = fileName ?? this.autoPupilsFileName;
			Num.save(fileName, pupilLocations);
		}

		public void LoadTimestamps(string fileName)
		{
			timeStamps = Num.load(fileName);
			isTimestampParsed = true;
		}

		public void LoadPupilLocations(string fileName)
		{
			pupilLocations = Num.load(fileName);
			isAnyFrameProcessed = true;
		}

		/// <summary>
		/// Uses manual adjusts on the found pupil locations. Can either use a linear fade or an exponential decay.
		/// If linear, then <paramref name="frameDecay"/> specifies the number of frames over which to fade the delta.
		/// If exponential, then <paramref name="frameDecay"/> is the time constant of decay, and the 2D delta will
		/// be added until it is less than 1 pixel, or until the end of the video, whichever is earlier.
		/// </summary>
		/// <param name="startFrame">frame number at which the manual update was made</param>
		/// <param name="X">new X position of pupil</param>
		/// <param name="Y">new Y position of pupil</param>
		/// <param name="radius">new radius of pupil</param>
		/// <param name="frameDecay">number of frames over which to linearly fade the difference, or time constant of exponential decay</param>
		public void ManuallyUpdatePupilLocations(int startFrame, double X, double Y, double radius, int frameDecay, ManualUpdateMode updateMode)
		{
			double dX = X - pupilLocations[startFrame, 0];
			double dY = Y - pupilLocations[startFrame, 1];
			double dR = radius - pupilLocations[startFrame, 2];
			pupilLocations[startFrame, 3] = 2;  // mark manual adjustment
			double fade;
			double dD = Math.Sqrt(dX * dX + dY * dY);
			int numFramesToUpdate = (updateMode == ManualUpdateMode.Linear) ? frameDecay : (int)(frameDecay / Math.Log(dD));
			for (int i = 0; i < numFramesToUpdate; i++)
			{
				if (i + startFrame >= frameCount)
					break;
				if (pupilLocations[i + startFrame, 0] == Num.NaN)
					break;	// don't update auto values if they don't exist
				fade = (updateMode == ManualUpdateMode.Exponential) ? dD * Math.Exp(-1 * (double)i / frameDecay) : (double)(frameDecay - i) / frameDecay;
				pupilLocations[i + startFrame, 0] += fade * dX;
				pupilLocations[i + startFrame, 1] += fade * dY;
				if (double.IsFinite(pupilLocations[i + startFrame, 2]))
					pupilLocations[i + startFrame, 2] += fade * dR;
				else
					pupilLocations[i + startFrame, 2] = radius;
			}
		}

		/// <summary>
		/// Gets a green-red representation of which frames has had FindPupils run over them
		/// </summary>
		/// <param name="width">width of image to get</param>
		/// <param name="height">height of image to get</param>
		/// <returns></returns>
		public Bitmap GetFramesProcessedPreviewImage(int width = 1920, int height = 6)
		{
			if (pupilLocations == null) return null;
			Mat representation = new Mat(height, frameCount, MatType.CV_8UC3);
			MatIndexer<Vec3b> indexer = representation.GetGenericIndexer<Vec3b>();
			for (int i = 0; i < frameCount; i++)
			{
				for (int j = 0; j < height; j++)
					representation.Set(j, i, Double.IsNaN(pupilLocations[i, 0]) ? Scalar.DeepPink.ToVec3b() : Scalar.LimeGreen.ToVec3b());
					//indexer[i, j] = value > 0 ? Scalar.LimeGreen.ToVec3b() : Scalar.DeepPink.ToVec3b();
			}

			MemoryStream imageStream = representation.Resize(new Size(width, height), 0, 0, InterpolationFlags.Nearest)
				.ToMemoryStream(".bmp");
			imageStream.Seek(0, SeekOrigin.Begin);
			bitmapFrame = new Bitmap(imageStream);
			return bitmapFrame;
		}

		public void ResetPupilLocations()
		{
			pupilLocations = Num.zeros((frameCount, 4), NPTypeCode.Double);
			pupilLocations *= Num.NaN;    // use -1 to indicate pupil not yet found on this frame
		}

		// UI interaction code
		// quick default delegates. Holdover from WPF implementation
		// TODO: check if we still actually need the delegate pattern
		public void SetStatus(string status = null)
		{
			if (ViewModel != null)
				ViewModel.StatusText = status != null ? status : "Idle";
		}

		public void OnFramesProcessed(bool error = false, string message = null, bool stepBack = false)
		{
			UpdatePupilFindingButtons(false);
			UpdateFramesProcessedPreviewImage();
			
			// auto save
			SavePupilLocations();
			SaveTimestamps();
			if (this is TemplatePupilFinder templatePupilFinder)
				templatePupilFinder.SaveTemplates();
			
			if (stepBack)
				UpdateVideoTime(CurrentFrameNumber - ViewModel.LowConfidenceFrameCountThreshold / 2); // don't fully step back because we want the bad frames in saccades

		}


		public void UpdateFrame()
		{
			try
			{
				ViewModel.CurrentVideoFrame = CurrentFrameNumber;
				ViewModel.CurrentVideoTime = ViewModel.TimeFormatter(CurrentFrameNumber);
				ViewModel.VideoFrame = GetFrameForDisplay(ViewModel.ShowFilteredImage);

				ViewModel.PupilX = pupilLocations[CurrentFrameNumber, 0];
				ViewModel.PupilY = pupilLocations[CurrentFrameNumber, 1];
				ViewModel.PupilDiameter = pupilLocations[CurrentFrameNumber, 2] * 2;
				ViewModel.PupilConfidence = pupilLocations[CurrentFrameNumber, 3];

				// at some point check if this could be moved into an arg and passed in from the pupil finders,
				// which would need some change in the delegate signature
				// TODO: Move isPupilManuallySetOnThisFrame into the pupil finder
				//isPupilManuallySetOnThisFrame = false;
			}
			catch (Exception e)
			{
				ShowMessageBox("Exception", e.ToString(), ButtonEnum.Ok, Icon.Error);
			}
		}
		public void OnTimestampsFound(bool error = false, string message = null, bool stepBack = false)
		{

		}

		private void UpdateFramesProcessedPreviewImage()
		{
			ViewModel.FramesProcessedPreviewImage = GetFramesProcessedPreviewImage();
		}

		private void UpdatePupilFindingButtons(bool isPupilFinding)
		{

		}

		public override Bitmap GetFrameForDisplay(bool filtered = false)
		{
			if (cvFrame == null)
			{
				return null;
			}

			if ((filtered == isBitmapFrameGrayscale) && isCVFrameConverted)
			{
				return bitmapFrame;
			}

			isBitmapFrameGrayscale = filtered;
			isCVFrameConverted = true;
			MemoryStream imageStream = filtered ? filteredFrame.ToMemoryStream(".bmp") : cvFrame.ToMemoryStream(".bmp");

			imageStream.Seek(0, SeekOrigin.Begin);
			bitmapFrame = new Bitmap(imageStream);
			return bitmapFrame;
		}

		public override bool ReadFrame()
		{
			bool success = base.ReadFrame();
			if (success)
				isCVFrameConverted = false;

			return success;
		}

		protected void UpdateVideoTime(int frame)
		{
			CurrentFrameNumber = frame;
			UpdateFrame();
		}

		public PupilInfo GetPupilInfo()
		{
			return new PupilInfo(pupilLocations, timeStamps, fps);
		}
	}
}
