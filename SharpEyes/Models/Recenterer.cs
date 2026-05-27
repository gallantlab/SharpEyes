using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NumSharp;
using OpenCvSharp;
using Num = NumSharp.np;

namespace SharpEyes.Models
{
	public class Recenterer
	{
		private readonly VideoReader videoReader;
		private readonly NDArray gazeLocations;
		private readonly int dataStartFrame;
		private readonly bool startFromFirstTTL;
		private readonly int eyetrackingFPS;
		private readonly int gazeSpaceWidth;
		private readonly int gazeSpaceHeight;
		private readonly double frameScale;
		private readonly double padValue;
		private readonly bool flipColors;

		public Recenterer(
			VideoReader videoReader,
			NDArray gazeLocations,
			int dataStartFrame,
			bool startFromFirstTTL,
			int eyetrackingFPS,
			int gazeSpaceWidth,
			int gazeSpaceHeight,
			double frameScale,
			double padValue,
			bool flipColors)
		{
			this.videoReader = videoReader;
			this.gazeLocations = gazeLocations;
			this.dataStartFrame = dataStartFrame;
			this.startFromFirstTTL = startFromFirstTTL;
			this.eyetrackingFPS = eyetrackingFPS;
			this.gazeSpaceWidth = gazeSpaceWidth;
			this.gazeSpaceHeight = gazeSpaceHeight;
			this.frameScale = frameScale;
			this.padValue = padValue;
			this.flipColors = flipColors;
		}

		public static int? FindFirstTTLGazeIndex(NDArray gazeLocations)
		{
			if ((object)gazeLocations == null || gazeLocations.Shape[1] < 4)
				return null;
			for (int i = 0; i < gazeLocations.Shape[0]; i++)
				if ((double)gazeLocations[i, 3] != 0.0)
					return i;
			return null;
		}

		public async Task ExportAsync(string destinationPath, bool isPng, bool includeRaw, IProgress<double> progress)
		{
			await Task.Run(() => RunExport(destinationPath, isPng, includeRaw, progress));
		}

		private void RunExport(string destinationPath, bool isPng, bool includeRaw, IProgress<double> progress)
		{
			int scaledWidth  = Math.Max(1, (int)(videoReader.width  * frameScale));
			int scaledHeight = Math.Max(1, (int)(videoReader.height * frameScale));
			int outputWidth  = scaledWidth  * 2;
			int outputHeight = scaledHeight * 2;

			double frameIndexingFactor = (double)eyetrackingFPS / videoReader.fps;

			int gazeStartIndex = 0;
			int videoStartFrame = dataStartFrame;
			if (startFromFirstTTL)
			{
				int? firstTTLGazeIndex = FindFirstTTLGazeIndex(gazeLocations);
				if (firstTTLGazeIndex != null)
				{
					gazeStartIndex = firstTTLGazeIndex.Value;
					double gazeElapsedTime = (double)gazeStartIndex / eyetrackingFPS;
					videoStartFrame = dataStartFrame + (int)(gazeElapsedTime * videoReader.fps);
				}
			}

			int videoFramesAvailable = videoReader.frameCount - videoStartFrame;
			int maxFrameFromGaze = (int)Math.Floor((gazeLocations.Shape[0] - gazeStartIndex) / frameIndexingFactor);
			int nFramesToExport = Math.Min(videoFramesAvailable, maxFrameFromGaze);

			double padBGR = padValue * 255.0;

			// Create output directories for PNG
			if (isPng)
			{
				if (includeRaw)
				{
					Directory.CreateDirectory(Path.Combine(destinationPath, "recentered"));
					Directory.CreateDirectory(Path.Combine(destinationPath, "raw"));
				}
				else
				{
					Directory.CreateDirectory(destinationPath);
				}
			}

			// For numpy: pre-allocate flat byte arrays, fill during loop, build NDArray after
			byte[] allRecenterredGray = isPng ? null : new byte[nFramesToExport * outputHeight * outputWidth];
			byte[] allRawGray = (isPng || !includeRaw) ? null : new byte[nFramesToExport * outputHeight * outputWidth];

			// Translation matrix: [[1, 0, tx], [0, 1, ty]] in CV_64F
			Mat translationMatrix = new Mat(2, 3, MatType.CV_64F, Scalar.All(0.0));
			translationMatrix.At<double>(0, 0) = 1.0;
			translationMatrix.At<double>(1, 1) = 1.0;

			videoReader.Seek(videoStartFrame);
			int actualFrameCount = 0;

			for (int frame = 0; frame < nFramesToExport; frame++)
			{
				if (!videoReader.ReadFrame()) break;

				int eyetrackingIndex = gazeStartIndex + (int)(frame * frameIndexingFactor);
				if (eyetrackingIndex >= gazeLocations.Shape[0]) break;

				double gazeXValue = (double)gazeLocations[eyetrackingIndex, 0];
				double gazeYValue = (double)gazeLocations[eyetrackingIndex, 1];
				// Replace NaN gaze with center of gaze space
				if (Double.IsNaN(gazeXValue)) gazeXValue = gazeSpaceWidth  / 2.0;
				if (Double.IsNaN(gazeYValue)) gazeYValue = gazeSpaceHeight / 2.0;

				double dx = gazeSpaceWidth  / 2.0 - gazeXValue;
				double dy = gazeSpaceHeight / 2.0 - gazeYValue;
				double tx = dx * frameScale + scaledWidth  / 2.0;
				double ty = dy * frameScale + scaledHeight / 2.0;

				// Downscale frame
				Mat scaledFrame = new Mat();
				Cv2.Resize(videoReader.cvFrame, scaledFrame, new OpenCvSharp.Size(scaledWidth, scaledHeight));

				// Apply recentered warp (inverse mapping via warpAffine)
				translationMatrix.At<double>(0, 2) = tx;
				translationMatrix.At<double>(1, 2) = ty;
				Mat recenterredFrame = new Mat();
				Cv2.WarpAffine(scaledFrame, recenterredFrame, translationMatrix,
					new OpenCvSharp.Size(outputWidth, outputHeight),
					borderMode: BorderTypes.Constant,
					borderValue: new Scalar(padBGR, padBGR, padBGR));

				if (isPng)
				{
					Mat frameToSave = recenterredFrame;
					Mat flipped = null;
					if (flipColors)
					{
						flipped = new Mat();
						Cv2.CvtColor(recenterredFrame, flipped, ColorConversionCodes.BGR2RGB);
						frameToSave = flipped;
					}
					string pngPath = includeRaw
						? Path.Combine(destinationPath, "recentered", String.Format("frame-{0:000000}.png", frame))
						: Path.Combine(destinationPath, String.Format("frame-{0:000000}.png", frame));
					Cv2.ImWrite(pngPath, frameToSave);
					flipped?.Dispose();
				}
				else
				{
					// Convert to grayscale and copy row-by-row into pre-allocated flat array
					Mat grayFrame = new Mat();
					Cv2.CvtColor(recenterredFrame, grayFrame, ColorConversionCodes.BGR2GRAY);
					int offset = actualFrameCount * outputHeight * outputWidth;
					int step = (int)grayFrame.Step();
					for (int y = 0; y < outputHeight; y++)
						Marshal.Copy(IntPtr.Add(grayFrame.Data, y * step), allRecenterredGray, offset + y * outputWidth, outputWidth);
					grayFrame.Dispose();
				}

				if (includeRaw)
				{
					// Centered warp: frame is always placed at the center of the doubled canvas
					translationMatrix.At<double>(0, 2) = scaledWidth  / 2.0;
					translationMatrix.At<double>(1, 2) = scaledHeight / 2.0;
					Mat rawFrame = new Mat();
					Cv2.WarpAffine(scaledFrame, rawFrame, translationMatrix,
						new OpenCvSharp.Size(outputWidth, outputHeight),
						borderMode: BorderTypes.Constant,
						borderValue: new Scalar(padBGR, padBGR, padBGR));

					if (isPng)
					{
						Mat frameToSave = rawFrame;
						Mat flipped = null;
						if (flipColors)
						{
							flipped = new Mat();
							Cv2.CvtColor(rawFrame, flipped, ColorConversionCodes.BGR2RGB);
							frameToSave = flipped;
						}
						string rawPngPath = Path.Combine(destinationPath, "raw", String.Format("frame-{0:000000}.png", frame));
						Cv2.ImWrite(rawPngPath, frameToSave);
						flipped?.Dispose();
					}
					else
					{
						Mat grayRaw = new Mat();
						Cv2.CvtColor(rawFrame, grayRaw, ColorConversionCodes.BGR2GRAY);
						int offset = actualFrameCount * outputHeight * outputWidth;
						int step = (int)grayRaw.Step();
						for (int y = 0; y < outputHeight; y++)
							Marshal.Copy(IntPtr.Add(grayRaw.Data, y * step), allRawGray, offset + y * outputWidth, outputWidth);
						grayRaw.Dispose();
					}

					rawFrame.Dispose();
				}

				scaledFrame.Dispose();
				recenterredFrame.Dispose();
				actualFrameCount++;

				progress.Report((double)actualFrameCount / nFramesToExport * 100.0);
			}

			translationMatrix.Dispose();

			// Save numpy arrays
			if (!isPng)
			{
				NDArray recenterredArray = new NDArray(NPTypeCode.Byte, new Shape(actualFrameCount, outputHeight, outputWidth));
				for (int f = 0; f < actualFrameCount; f++)
					for (int y = 0; y < outputHeight; y++)
						for (int x = 0; x < outputWidth; x++)
							recenterredArray[f, y, x] = allRecenterredGray[f * outputHeight * outputWidth + y * outputWidth + x];
				Num.save(destinationPath, recenterredArray);

				if (includeRaw)
				{
					NDArray rawArray = new NDArray(NPTypeCode.Byte, new Shape(actualFrameCount, outputHeight, outputWidth));
					for (int f = 0; f < actualFrameCount; f++)
						for (int y = 0; y < outputHeight; y++)
							for (int x = 0; x < outputWidth; x++)
								rawArray[f, y, x] = allRawGray[f * outputHeight * outputWidth + y * outputWidth + x];

					string rawNpyPath = Path.GetFileNameWithoutExtension(destinationPath) + " raw.npy";
					rawNpyPath = Path.Combine(Path.GetDirectoryName(destinationPath)!, rawNpyPath);
					Num.save(rawNpyPath, rawArray);
				}
			}
		}
	}
}
