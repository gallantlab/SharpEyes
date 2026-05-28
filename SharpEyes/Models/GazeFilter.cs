using System;
using System.Linq;
using NumSharp;

namespace Eyetracking
{
	public class GazeFilterSettings
	{
		public bool IsEnabled { get; set; }
		public int MedianFilterWindowSize { get; set; }
		public bool FilterPupilSize { get; set; }
		public bool EnableOutlierRemoval { get; set; }
		public double OutlierThresholdX { get; set; }
		public double OutlierThresholdY { get; set; }
		public double OutlierThresholdRadius { get; set; }
	}

	public static class GazeFilter
	{
		/// <summary>
		/// Applies bidirectional median filtering and optional percentile outlier removal to gaze data.
		/// Mirrors the logic from PupilFinder.FilterPupils in the Python eyetracking library.
		/// </summary>
		public static NDArray Filter(NDArray gazeLocations, int windowSize, bool filterPupilSize,
			bool enableOutlierRemoval, double outlierThresholdX, double outlierThresholdY, double outlierThresholdRadius)
		{
			int frameCount = gazeLocations.Shape[0];
			int columnCount = gazeLocations.Shape[1];

			NDArray filtered = new NDArray(NPTypeCode.Double, Shape.Matrix(frameCount, columnCount));
			for (int column = 0; column < columnCount; column++)
				WriteColumn(filtered, column, ExtractColumn(gazeLocations, column, frameCount));

			int columnsToMedianFilter = (filterPupilSize && columnCount > 2) ? 3 : 2;
			for (int column = 0; column < columnsToMedianFilter && column < columnCount; column++)
				WriteColumn(filtered, column, BidirectionalMedianFilter(ExtractColumn(filtered, column, frameCount), windowSize));

			if (enableOutlierRemoval)
			{
				double[] thresholds = new double[] { outlierThresholdX, outlierThresholdY, outlierThresholdRadius };
				for (int column = 0; column < Math.Min(3, columnCount); column++)
					WriteColumn(filtered, column, RemoveOutliers(ExtractColumn(filtered, column, frameCount), thresholds[column]));

				// if any of the first columns is NaN, set the entire row to NaN
				for (int row = 0; row < frameCount; row++)
				{
					bool hasNaN = false;
					for (int column = 0; column < Math.Min(3, columnCount); column++)
					{
						if (Double.IsNaN((double)filtered[row, column]))
						{
							hasNaN = true;
							break;
						}
					}
					if (hasNaN)
						for (int column = 0; column < Math.Min(3, columnCount); column++)
							filtered[row, column] = Double.NaN;
				}
			}

			return filtered;
		}

		private static double[] ExtractColumn(NDArray array, int column, int rowCount)
		{
			double[] result = new double[rowCount];
			for (int row = 0; row < rowCount; row++)
				result[row] = (double)array[row, column];
			return result;
		}

		private static void WriteColumn(NDArray array, int column, double[] values)
		{
			for (int row = 0; row < values.Length; row++)
				array[row, column] = values[row];
		}

		private static double[] BidirectionalMedianFilter(double[] data, int windowSize)
		{
			double[] forward = ApplyMedianFilterOnce(data, windowSize);
			double[] reversed = (double[])data.Clone();
			Array.Reverse(reversed);
			double[] backwardReversed = ApplyMedianFilterOnce(reversed, windowSize);
			Array.Reverse(backwardReversed);
			double[] result = new double[data.Length];
			for (int i = 0; i < data.Length; i++)
				result[i] = (forward[i] + backwardReversed[i]) / 2.0;
			return result;
		}

		private static double[] ApplyMedianFilterOnce(double[] data, int windowSize)
		{
			int halfWindow = windowSize / 2;
			double[] result = new double[data.Length];
			for (int i = 0; i < data.Length; i++)
			{
				int start = Math.Max(0, i - halfWindow);
				int end = Math.Min(data.Length - 1, i + halfWindow);
				double[] window = new double[end - start + 1];
				Array.Copy(data, start, window, 0, window.Length);
				Array.Sort(window);
				result[i] = window[window.Length / 2];
			}
			return result;
		}

		private static double[] RemoveOutliers(double[] data, double percentile)
		{
			double[] validValues = data.Where(d => !Double.IsNaN(d)).OrderBy(d => d).ToArray();
			if (validValues.Length == 0) return data;
			int thresholdIndex = Math.Min((int)(validValues.Length * percentile / 100.0), validValues.Length - 1);
			double threshold = validValues[thresholdIndex];
			double[] result = new double[data.Length];
			for (int i = 0; i < data.Length; i++)
				result[i] = data[i] > threshold ? Double.NaN : data[i];
			return result;
		}
	}
}
