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
		private readonly bool flipBGR;

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
			bool flipBgr)
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
			this.flipBGR = flipBgr;
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

		private (int scaledWidth, int scaledHeight, int outputWidth, int outputHeight,
		         double frameIndexingFactor, int gazeStartIndex, int nFramesToExport, double padGray)
		ComputeExportParameters()
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

			double padGray = padValue * 255.0;
			videoReader.Seek(videoStartFrame);

			return (scaledWidth, scaledHeight, outputWidth, outputHeight,
			        frameIndexingFactor, gazeStartIndex, nFramesToExport, padGray);
		}

		private void RunExport(string destinationPath, bool isPng, bool includeRaw, IProgress<double> progress)
		{
			(int scaledWidth, int scaledHeight, int outputWidth, int outputHeight,
			 double frameIndexingFactor, int gazeStartIndex, int nFramesToExport, double padGray) = ComputeExportParameters();

			if (isPng)
			{
				ExportPngs(destinationPath, includeRaw, nFramesToExport, gazeStartIndex, frameIndexingFactor,
					scaledWidth, scaledHeight, outputWidth, outputHeight, padGray, progress);
			}
			else
			{
				(byte[] recenteredFrames, byte[] rawFrames, int actualFrameCount) = BuildNumpyArrays(
					includeRaw, nFramesToExport, gazeStartIndex, frameIndexingFactor,
					scaledWidth, scaledHeight, outputWidth, outputHeight, padGray, progress);

				SaveNumpyArray(recenteredFrames, actualFrameCount, outputHeight, outputWidth, destinationPath);

				if (includeRaw)
				{
					string rawNpyPath = Path.GetFileNameWithoutExtension(destinationPath) + " raw.npy";
					rawNpyPath = Path.Combine(Path.GetDirectoryName(destinationPath)!, rawNpyPath);
					SaveNumpyArray(rawFrames, actualFrameCount, outputHeight, outputWidth, rawNpyPath);
				}
			}
		}

		// Scales videoFrame and applies an affine warp with the given (tx, ty). Caller must dispose the returned Mat.
		private Mat ProcessFrame(Mat videoFrame, double tx, double ty, int scaledWidth, int scaledHeight, 
								 int outputWidth, int outputHeight, double padValue, Mat translationMatrix)
		{
			Mat scaledFrame = new Mat();
			Cv2.Resize(videoFrame, scaledFrame, new OpenCvSharp.Size(scaledWidth, scaledHeight));

			translationMatrix.At<double>(0, 2) = tx;
			translationMatrix.At<double>(1, 2) = ty;
			Mat warpedFrame = new Mat();
			Cv2.WarpAffine(scaledFrame, warpedFrame, translationMatrix,
				new OpenCvSharp.Size(outputWidth, outputHeight),
				borderMode: BorderTypes.Constant,
				borderValue: new Scalar(padValue, padValue, padValue));

			scaledFrame.Dispose();
			return warpedFrame;
		}

		// Iterates all frames, calling processRecenteredFrame and (if includeRaw) processRawFrame for each.
		// The Mat passed to each callback is disposed by this method after the callback returns.
		// The int argument is the sequential index of the successfully processed frame (0-based).
		// Returns the total number of successfully processed frames.
		private int IterateFrames(
			int nFramesToExport, int gazeStartIndex, double frameIndexingFactor,
			int scaledWidth, int scaledHeight, int outputWidth, int outputHeight, double padGray,
			bool includeRaw, IProgress<double> progress,
			Action<Mat, int> processRecenteredFrame,
			Action<Mat, int>? processRawFrame)
		{
			// Translation matrix: [[1, 0, tx], [0, 1, ty]] in CV_64F
			Mat translationMatrix = new Mat(2, 3, MatType.CV_64F, Scalar.All(0.0));
			translationMatrix.At<double>(0, 0) = 1.0;
			translationMatrix.At<double>(1, 1) = 1.0;

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

				Mat recenteredFrame = ProcessFrame(videoReader.cvFrame, tx, ty,
					scaledWidth, scaledHeight, outputWidth, outputHeight, padGray, translationMatrix);
				processRecenteredFrame(recenteredFrame, actualFrameCount);
				recenteredFrame.Dispose();

				if (includeRaw && processRawFrame != null)
				{
					// Centered warp: frame is always placed at the center of the doubled canvas
					Mat rawFrame = ProcessFrame(videoReader.cvFrame, scaledWidth / 2.0, scaledHeight / 2.0,
						scaledWidth, scaledHeight, outputWidth, outputHeight, padGray, translationMatrix);
					processRawFrame(rawFrame, actualFrameCount);
					rawFrame.Dispose();
				}

				actualFrameCount++;
				progress.Report((double)actualFrameCount / nFramesToExport * 100.0);
			}

			translationMatrix.Dispose();
			return actualFrameCount;
		}

		private void ExportPngs(string destinationPath, bool includeRaw, int nFramesToExport, int gazeStartIndex,
								double frameIndexingFactor, int scaledWidth, int scaledHeight, int outputWidth,
								int outputHeight, double padGray, IProgress<double> progress)
		{
			// Create output directories
			if (includeRaw)
			{
				Directory.CreateDirectory(Path.Combine(destinationPath, "recentered"));
				Directory.CreateDirectory(Path.Combine(destinationPath, "raw"));
			}
			else
			{
				Directory.CreateDirectory(destinationPath);
			}

			IterateFrames(nFramesToExport, gazeStartIndex, frameIndexingFactor,
				scaledWidth, scaledHeight, outputWidth, outputHeight, padGray, includeRaw, progress,
				(frame, frameIndex) =>
				{
					Mat frameToSave = frame;
					Mat flipped = null;
					if (flipBGR)
					{
						flipped = new Mat();
						Cv2.CvtColor(frame, flipped, ColorConversionCodes.BGR2RGB);
						frameToSave = flipped;
					}
					string pngPath = includeRaw
						? Path.Combine(destinationPath, "recentered", String.Format("frame-{0:000000}.png", frameIndex))
						: Path.Combine(destinationPath, String.Format("frame-{0:000000}.png", frameIndex));
					Cv2.ImWrite(pngPath, frameToSave);
					flipped?.Dispose();
				},
				includeRaw ? (Action<Mat, int>)((frame, frameIndex) =>
				{
					Mat rawToSave = frame;
					Mat rawFlipped = null;
					if (flipBGR)
					{
						rawFlipped = new Mat();
						Cv2.CvtColor(frame, rawFlipped, ColorConversionCodes.BGR2RGB);
						rawToSave = rawFlipped;
					}
					string rawPngPath = Path.Combine(destinationPath, "raw", String.Format("frame-{0:000000}.png", frameIndex));
					Cv2.ImWrite(rawPngPath, rawToSave);
					rawFlipped?.Dispose();
				}) : null);
		}

		private (byte[] recenteredFrames, byte[] paddedRawFrames, int actualFrameCount) BuildNumpyArrays(bool includeRaw,
			int nFramesToExport, int gazeStartIndex, double frameIndexingFactor, int scaledWidth, int scaledHeight,
			int outputWidth, int outputHeight, double padGray, IProgress<double> progress)
		{
			// Pre-allocate flat byte arrays, fill during loop, build NDArray after
			byte[] recenteredFrames = new byte[nFramesToExport * outputHeight * outputWidth];
			byte[] paddedRawFrames = includeRaw ? new byte[nFramesToExport * outputHeight * outputWidth] : null;

			int actualFrameCount = IterateFrames(nFramesToExport, gazeStartIndex, frameIndexingFactor,
				scaledWidth, scaledHeight, outputWidth, outputHeight, padGray, includeRaw, progress,
				(frame, frameIndex) =>
				{
					Mat grayscale = new Mat();
					Cv2.CvtColor(frame, grayscale, ColorConversionCodes.BGR2GRAY);
					int offset = frameIndex * outputHeight * outputWidth;
					int step = (int)grayscale.Step();
					for (int y = 0; y < outputHeight; y++)
						Marshal.Copy(IntPtr.Add(grayscale.Data, y * step), recenteredFrames, offset + y * outputWidth, outputWidth);
					grayscale.Dispose();
				},
				includeRaw ? (Action<Mat, int>)((frame, frameIndex) =>
				{
					Mat padded = new Mat();
					Cv2.CvtColor(frame, padded, ColorConversionCodes.BGR2GRAY);
					int offset = frameIndex * outputHeight * outputWidth;
					int rawStep = (int)padded.Step();
					for (int y = 0; y < outputHeight; y++)
						Marshal.Copy(IntPtr.Add(padded.Data, y * rawStep), paddedRawFrames, offset + y * outputWidth, outputWidth);
					padded.Dispose();
				}) : null);

			return (recenteredFrames, paddedRawFrames, actualFrameCount);
		}

		public async Task<NDArray> BuildRecenteredFramesAsync(IProgress<double> progress)
		{
			return await Task.Run(() =>
			{
				(int scaledWidth, int scaledHeight, int outputWidth, int outputHeight,
				 double frameIndexingFactor, int gazeStartIndex, int nFramesToExport, double padGray) = ComputeExportParameters();

				(byte[] recenteredGray, byte[] _, int actualFrameCount) = BuildNumpyArrays(
					false, nFramesToExport, gazeStartIndex, frameIndexingFactor,
					scaledWidth, scaledHeight, outputWidth, outputHeight, padGray, progress);

				return BuildNDArray(recenteredGray, actualFrameCount, outputHeight, outputWidth);
			});
		}

		private NDArray BuildNDArray(byte[] flatArray, int frameCount, int height, int width)
		{
			NDArray array = new NDArray(NPTypeCode.Byte, new Shape(frameCount, height, width));
			for (int f = 0; f < frameCount; f++)
				for (int y = 0; y < height; y++)
					for (int x = 0; x < width; x++)
						array[f, y, x] = flatArray[f * height * width + y * width + x];
			return array;
		}

		private void SaveNumpyArray(byte[] flatArray, int frameCount, int height, int width, string path)
		{
			Num.save(path, BuildNDArray(flatArray, frameCount, height, width));
		}
	}
}
