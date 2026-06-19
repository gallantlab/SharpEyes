using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Eyetracking;
using MessageBox.Avalonia;
using MessageBox.Avalonia.BaseWindows.Base;
using MessageBox.Avalonia.Enums;
using OpenCvSharp;

namespace SharpEyes.Models
{
	public class VideoReader
	{
		protected VideoCapture videoSource = null;
		protected int framesPerHour = 0;
		protected int framesPerMinute = 0;
		protected int _currentFrameNumber = -1;
		protected Bitmap bitmapFrame = null;

		public static void ShowMessageBox(string title, string body, ButtonEnum buttons = ButtonEnum.Ok, Icon icon = Icon.None)
		{
			IMsBoxWindow<MessageBox.Avalonia.Enums.ButtonResult> messageBox =
				MessageBoxManager.GetMessageBoxStandardWindow(title, body, buttons, icon);
			messageBox.Show();
		}

		public string videoFileName { get; private set; }
		public int width { get; private set; } = -1;
		public int height { get; private set; } = -1;
		public int fps { get; private set; } = -1;
		public int frameCount { get; private set; } = -1;
		public double duration { get; private set; } = -1.0;

		/// <summary>
		/// The current frame number that has been read in. When set, will read that frame!
		/// Use <see cref="Seek"/> if we do not want to do the frame reading.
		/// </summary>
		public int CurrentFrameNumber
		{
			get { return _currentFrameNumber; }
			set
			{
				int desired = value;
				if (desired < 0)
				{
					desired = 0;
				}
				else if (desired > frameCount - 1)
				{
					desired = frameCount - 1;
				}

				_currentFrameNumber = desired - 1;
				videoSource.Set(VideoCaptureProperties.PosFrames, desired);
				OnCurrentFrameNumberSet();
			}
		}

		protected virtual void OnCurrentFrameNumberSet()
		{
			ReadFrame();
		}
		public Mat cvFrame { get; protected set; } = null;

		public VideoReader(string videoFileName)
		{
			this.videoFileName = videoFileName;
			videoSource = new VideoCapture(videoFileName);

			width = (int)videoSource.Get(VideoCaptureProperties.FrameWidth);
			height = (int)videoSource.Get(VideoCaptureProperties.FrameHeight);
			fps = (int)videoSource.Get(VideoCaptureProperties.Fps);
			frameCount = (int)videoSource.Get(VideoCaptureProperties.FrameCount);
			duration = (double)frameCount / fps;
			framesPerMinute = fps * 60;
			framesPerHour = framesPerMinute * 60;

			cvFrame = new Mat();
		}

		

		/// <summary>
		/// Read the next frame and increment the internal counter
		/// </summary>
		/// <returns></returns>
		public virtual bool ReadFrame()
		{
			try
			{
				bool success = videoSource.Read(cvFrame);
				if (!success)
				{
					return success;
				}

				_currentFrameNumber++;
				return success;
			}
			catch (AccessViolationException e)
			{
				Sentry.SentrySdk.CaptureException(e);
				return false;
			}
		}

		/// <summary>
		/// Gets the current frame that has been read in for display
		/// </summary>
		/// <param name="filtered">get the filtered frame instead of the RGB frame.</param>
		/// <returns></returns>
		public virtual Bitmap GetFrameForDisplay(bool filtered = false)
		{
			if (cvFrame == null || cvFrame.Empty())
			{
				return bitmapFrame;
			}

			MemoryStream imageStream = cvFrame.ToMemoryStream(".bmp");

			imageStream.Seek(0, SeekOrigin.Begin);
			bitmapFrame = new Bitmap(imageStream);
			return bitmapFrame;
		}

		/// <summary>
		/// Writes the current frame into a WriteableBitmap buffer, which should be faster
		/// </summary>
		/// <param name="buffer"></param>
		public virtual void WriteToBitmapBuffer(WriteableBitmap buffer)
		{
			unsafe
			{
				using ILockedFramebuffer frameBuffer = buffer.Lock();

				int bytesPerPixel = frameBuffer.RowBytes / frameBuffer.Size.Width;

				for (int y = 0; y < frameBuffer.Size.Height; y++)
				{
					byte* row = (byte*)frameBuffer.Address + frameBuffer.RowBytes * y;
					for (int x = 0; x < frameBuffer.Size.Width; x++)
					{

					}
				}
			}
			// TODO: copy from cvFrame memory to the WriteableBitmap memory
		}

		/// <summary>
		/// Seek to a frame such that when <see cref="ReadFrame"/> or <see cref="PupilFinder.ReadGrayscaleFrame"/> is called,
		/// this frame is read in.
		/// </summary>
		/// <param name="frame">frame to go to</param>
		public void Seek(int frame)
		{
			if (frame < 0)
			{
				frame = 0;
			}
			else if (frame > frameCount - 1)
			{
				frame = frameCount - 1;
			}

			_currentFrameNumber = frame - 1;
			videoSource.Set(VideoCaptureProperties.PosFrames, frame);
		}


		public string FramesToTimecode(int frames)
		{
			int hours = frames / framesPerHour;
			frames -= hours * framesPerHour;
			int minutes = frames / framesPerMinute;
			frames -= minutes * framesPerMinute;
			int seconds = frames / fps;
			frames -= seconds * fps;
			return String.Format("{0:00}:{1:00}:{2:00};{3:#00}", hours, minutes, seconds, frames + 1);
		}
	}
}
