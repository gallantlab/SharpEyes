using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NumSharp;

namespace Eyetracking
{
	/// <summary>
	/// Maps pupil positions in eye video space to gaze positions in screen space with a radial basis function
	/// fitted on calibration points. Preprocessing and aggregation of the calibration nodes happen here; the
	/// fitting and mapping are done by <see cref="RBFMapping"/>.
	/// </summary>
	public class GazeCalibrator
	{
		private RBFMappingModel? fittedModel = null;

		/// <summary>
		/// Parameters of the most recent fit together with the settings recorded by the caller. Null before any fit or load.
		/// </summary>
		public GazeCalibrationParameters? Parameters { get; private set; } = null;

		public bool IsFitted => fittedModel != null;

		// == Preprocessing ==

		/// <summary>
		/// Applies the gaze filter when enabled and sets the position and radius of frames whose confidence is
		/// below the blink threshold to NaN. The input is not modified.
		/// </summary>
		/// <param name="pupils">An (nFrames x columns) pupil array with X, Y, radius, and confidence columns.</param>
		/// <param name="blinkConfidenceThreshold">Frames with confidence below this are treated as blinks.</param>
		/// <param name="filterSettings">Median filter and outlier removal settings.</param>
		/// <returns>A new double array of the same shape.</returns>
		public static NDArray PreprocessPupils(NDArray pupils, double blinkConfidenceThreshold, GazeFilterSettings filterSettings)
		{
			NDArray processed = filterSettings.IsEnabled
				? GazeFilter.Filter(pupils, filterSettings.MedianFilterWindowSize, filterSettings.FilterPupilSize,
					filterSettings.EnableOutlierRemoval, filterSettings.OutlierThresholdX, filterSettings.OutlierThresholdY,
					filterSettings.OutlierThresholdRadius)
				: CopyAsDouble(pupils);
			int columnCount = processed.Shape[1];
			if (columnCount < 4) return processed;
			int frameCount = processed.Shape[0];
			int columnsToMask = Math.Min(3, columnCount);
			for (int row = 0; row < frameCount; row++)
			{
				if ((double)processed[row, 3] < blinkConfidenceThreshold)
					for (int column = 0; column < columnsToMask; column++)
						processed[row, column] = Double.NaN;
			}
			return processed;
		}

		/// <summary>
		/// Aggregates the median pupil position and its standard deviation over each calibration point's fixation
		/// window, skipping frames with a NaN position.
		/// </summary>
		/// <param name="pupils">Preprocessed (nFrames x columns) pupil array.</param>
		/// <param name="screenPoints">(nPoints x 2) screen positions in presentation order.</param>
		/// <param name="onsetFrame">Frame index at which the calibration sequence started.</param>
		/// <param name="delaySeconds">Seconds from the onset to the first point.</param>
		/// <param name="durationSeconds">Seconds each point is shown.</param>
		/// <param name="pointDelaySeconds">Seconds skipped at the start of each point for the saccade.</param>
		/// <param name="endSkipSeconds">Seconds skipped at the end of each point.</param>
		/// <param name="fps">Frame rate of the pupil array.</param>
		/// <returns>One node per screen point, with NaN pupil positions where no frame was valid.</returns>
		public static CalibrationNode[] AggregateCalibrationNodes(NDArray pupils, double[,] screenPoints, int onsetFrame, double delaySeconds,
			double durationSeconds, double pointDelaySeconds, double endSkipSeconds, double fps)
		{
			ExtractPositions(pupils, out double[] pupilX, out double[] pupilY);
			return AggregateCalibrationNodes(pupilX, pupilY, screenPoints, onsetFrame, delaySeconds, durationSeconds, pointDelaySeconds, endSkipSeconds, fps);
		}

		/// <summary>
		/// Aggregates the median pupil position and its standard deviation over each calibration point's fixation
		/// window from separate X and Y columns, skipping frames with a NaN position.
		/// </summary>
		/// <param name="pupilX">Pupil X position per frame.</param>
		/// <param name="pupilY">Pupil Y position per frame.</param>
		/// <param name="screenPoints">(nPoints x 2) screen positions in presentation order.</param>
		/// <param name="onsetFrame">Frame index at which the calibration sequence started.</param>
		/// <param name="delaySeconds">Seconds from the onset to the first point.</param>
		/// <param name="durationSeconds">Seconds each point is shown.</param>
		/// <param name="pointDelaySeconds">Seconds skipped at the start of each point for the saccade.</param>
		/// <param name="endSkipSeconds">Seconds skipped at the end of each point.</param>
		/// <param name="fps">Frame rate of the pupil positions.</param>
		/// <returns>One node per screen point, with NaN pupil positions where no frame was valid.</returns>
		public static CalibrationNode[] AggregateCalibrationNodes(double[] pupilX, double[] pupilY, double[,] screenPoints, int onsetFrame,
			double delaySeconds, double durationSeconds, double pointDelaySeconds, double endSkipSeconds, double fps)
		{
			int frameCount = pupilX.Length;
			int pointCount = screenPoints.GetLength(0);
			int firstFrame = onsetFrame + (int)(delaySeconds * fps);
			int startOffset = (int)(fps * pointDelaySeconds);
			int endOffset = (int)(fps * endSkipSeconds);
			CalibrationNode[] nodes = new CalibrationNode[pointCount];
			for (int point = 0; point < pointCount; point++)
			{
				int start = Math.Max(0, (int)(point * durationSeconds * fps + firstFrame + startOffset));
				int end = Math.Min(frameCount, (int)((point + 1) * durationSeconds * fps + firstFrame - endOffset));
				List<double> xPositions = new List<double>();
				List<double> yPositions = new List<double>();
				for (int frame = start; frame < end; frame++)
				{
					double x = pupilX[frame];
					double y = pupilY[frame];
					if (Double.IsNaN(x) || Double.IsNaN(y)) continue;
					xPositions.Add(x);
					yPositions.Add(y);
				}
				CalibrationNode node = new CalibrationNode
				{
					Index = point,
					ScreenX = screenPoints[point, 0],
					ScreenY = screenPoints[point, 1]
				};
				if (xPositions.Count > 0)
				{
					node.PupilStandardDeviationX = StandardDeviation(xPositions);
					node.PupilStandardDeviationY = StandardDeviation(yPositions);
					node.PupilX = PupilInfo.Median(xPositions);
					node.PupilY = PupilInfo.Median(yPositions);
				}
				nodes[point] = node;
			}
			return nodes;
		}

		/// <summary>
		/// Invalidates nodes whose pupil standard deviation on either axis is above the given percentile of the
		/// standard deviations across the valid nodes.
		/// </summary>
		/// <param name="nodes">Nodes to filter in place.</param>
		/// <param name="percentile">Percentile in the range 0 to 100.</param>
		public static void ApplyVarianceThreshold(CalibrationNode[] nodes, double percentile)
		{
			double[] xDeviations = nodes.Where(node => node.IsValid).Select(node => node.PupilStandardDeviationX).ToArray();
			double[] yDeviations = nodes.Where(node => node.IsValid).Select(node => node.PupilStandardDeviationY).ToArray();
			if (xDeviations.Length == 0) return;
			double xLimit = Percentile(xDeviations, percentile);
			double yLimit = Percentile(yDeviations, percentile);
			foreach (CalibrationNode node in nodes)
			{
				if (!node.IsValid) continue;
				if (node.PupilStandardDeviationX > xLimit || node.PupilStandardDeviationY > yLimit)
				{
					node.PupilX = Double.NaN;
					node.PupilY = Double.NaN;
				}
			}
		}

		/// <summary>
		/// Aggregates the nodes for every delay and duration combination into one NaN-padded stack, with the
		/// combination index equal to delayIndex * durations.Length + durationIndex.
		/// </summary>
		/// <param name="pupils">Preprocessed (nFrames x columns) pupil array.</param>
		/// <param name="screenPoints">(nPoints x 2) screen positions in presentation order.</param>
		/// <param name="onsetFrame">Frame index at which the calibration sequence started.</param>
		/// <param name="delays">Delays in seconds to try.</param>
		/// <param name="durations">Durations in seconds to try.</param>
		/// <param name="pointDelaySeconds">Seconds skipped at the start of each point.</param>
		/// <param name="endSkipSeconds">Seconds skipped at the end of each point.</param>
		/// <param name="fps">Frame rate of the pupil array.</param>
		/// <param name="variancePercentile">Variance threshold percentile, or null to apply none.</param>
		/// <returns>An (nCombinations x nPoints x 2) stack of pupil positions.</returns>
		public static double[,,] BuildSearchStack(NDArray pupils, double[,] screenPoints, int onsetFrame, double[] delays, double[] durations,
			double pointDelaySeconds, double endSkipSeconds, double fps, double? variancePercentile)
		{
			int pointCount = screenPoints.GetLength(0);
			double[,,] stack = new double[delays.Length * durations.Length, pointCount, 2];
			ExtractPositions(pupils, out double[] pupilX, out double[] pupilY);
			Parallel.For(0, delays.Length * durations.Length, combination =>
			{
				int delayIndex = combination / durations.Length;
				int durationIndex = combination % durations.Length;
				CalibrationNode[] nodes = AggregateCalibrationNodes(pupilX, pupilY, screenPoints, onsetFrame, delays[delayIndex], durations[durationIndex],
					pointDelaySeconds, endSkipSeconds, fps);
				if (variancePercentile.HasValue)
					ApplyVarianceThreshold(nodes, variancePercentile.Value);
				for (int point = 0; point < pointCount; point++)
				{
					stack[combination, point, 0] = nodes[point].PupilX;
					stack[combination, point, 1] = nodes[point].PupilY;
				}
			});
			return stack;
		}

		// == Fitting and mapping ==

		/// <summary>
		/// Fits the mapping on the valid nodes, keeps the model for mapping, and records the fitted parameters,
		/// updating <see cref="Parameters"/> in place when it exists.
		/// </summary>
		/// <param name="nodes">Calibration nodes; invalid ones are left out of the fit.</param>
		/// <param name="kernel">The kernel.</param>
		/// <param name="smoothness">Smoothing parameter.</param>
		/// <param name="useLegacyConvention">Whether to use the legacy Rbf convention rather than RBFInterpolator's.</param>
		/// <param name="epsilon">Shape parameter, or null for the convention's default.</param>
		public void Fit(CalibrationNode[] nodes, RBFKernel kernel, double smoothness, bool useLegacyConvention, double? epsilon)
		{
			int nodeCount = nodes.Length;
			double[,] pupilPositions = new double[nodeCount, 2];
			double[,] screenPositions = new double[nodeCount, 2];
			for (int node = 0; node < nodeCount; node++)
			{
				pupilPositions[node, 0] = nodes[node].PupilX;
				pupilPositions[node, 1] = nodes[node].PupilY;
				screenPositions[node, 0] = nodes[node].ScreenX;
				screenPositions[node, 1] = nodes[node].ScreenY;
			}
			RBFMappingModel model = RBFMapping.Fit(pupilPositions, screenPositions, kernel, smoothness, useLegacyConvention, epsilon);
			fittedModel = model;

			GazeCalibrationParameters parameters = Parameters ?? new GazeCalibrationParameters();
			parameters.UseLegacyConvention = useLegacyConvention;
			parameters.Kernel = kernel;
			parameters.Smoothness = smoothness;
			parameters.Epsilon = model.Epsilon;
			parameters.RMSError = model.RMSError;
			parameters.Nodes = new List<CalibrationNode>();
			int validIndex = 0;
			foreach (CalibrationNode node in nodes)
			{
				CalibrationNode recorded = node.Clone();
				if (recorded.IsValid)
				{
					recorded.ResidualX = model.Residuals[validIndex, 0];
					recorded.ResidualY = model.Residuals[validIndex, 1];
					validIndex++;
				}
				parameters.Nodes.Add(recorded);
			}
			parameters.CoefficientsX = new List<double>();
			parameters.CoefficientsY = new List<double>();
			for (int coefficient = 0; coefficient < model.Coefficients.GetLength(0); coefficient++)
			{
				parameters.CoefficientsX.Add(model.Coefficients[coefficient, 0]);
				parameters.CoefficientsY.Add(model.Coefficients[coefficient, 1]);
			}
			parameters.PolynomialPowers = new List<MonomialPowers>();
			for (int monomial = 0; monomial < model.PolynomialPowers.GetLength(0); monomial++)
				parameters.PolynomialPowers.Add(new MonomialPowers { PowerX = model.PolynomialPowers[monomial, 0], PowerY = model.PolynomialPowers[monomial, 1] });
			parameters.ShiftX = model.Shift[0];
			parameters.ShiftY = model.Shift[1];
			parameters.ScaleX = model.Scale[0];
			parameters.ScaleY = model.Scale[1];
			Parameters = parameters;
		}

		/// <summary>
		/// Maps pupil positions to screen positions with the fitted model. Rows with a NaN position map to NaN,
		/// and any columns beyond X and Y, such as radius and confidence, are copied through.
		/// </summary>
		/// <param name="pupils">An (nFrames x columns) pupil array with X and Y in the first two columns.</param>
		/// <returns>An (nFrames x columns) double array with screen X and Y in the first two columns.</returns>
		public NDArray MapToScreen(NDArray pupils)
		{
			if (fittedModel == null)
				throw new InvalidOperationException("No mapping has been fitted.");
			int frameCount = pupils.Shape[0];
			int columnCount = pupils.Shape[1];
			double[,] positions = new double[frameCount, 2];
			for (int frame = 0; frame < frameCount; frame++)
			{
				positions[frame, 0] = (double)pupils[frame, 0];
				positions[frame, 1] = (double)pupils[frame, 1];
			}
			double[,] mapped = fittedModel.MapToScreen(positions);
			NDArray result = new NDArray(NPTypeCode.Double, Shape.Matrix(frameCount, columnCount));
			for (int frame = 0; frame < frameCount; frame++)
			{
				result[frame, 0] = mapped[frame, 0];
				result[frame, 1] = mapped[frame, 1];
				for (int column = 2; column < columnCount; column++)
					result[frame, column] = (double)pupils[frame, column];
			}
			return result;
		}

		// == Persistence ==

		/// <summary>
		/// Writes the recorded parameters to an XML file.
		/// </summary>
		/// <param name="path">Destination file path.</param>
		public void SaveParameters(string path)
		{
			if (Parameters == null)
				throw new InvalidOperationException("No mapping has been fitted.");
			Parameters.Save(path);
		}

		/// <summary>
		/// Reads parameters from an XML file and refits the mapping from the stored nodes and hyperparameters,
		/// which reproduces the stored fit.
		/// </summary>
		/// <param name="path">Source file path.</param>
		public void LoadParameters(string path)
		{
			Parameters = GazeCalibrationParameters.Load(path);
			Fit(Parameters.Nodes.ToArray(), Parameters.Kernel, Parameters.Smoothness, Parameters.UseLegacyConvention, Parameters.Epsilon);
		}

		// == Helpers ==

		/// <summary>
		/// Copies the X and Y columns of a pupil array into managed arrays.
		/// </summary>
		/// <param name="pupils">An (nFrames x columns) pupil array with X and Y in the first two columns.</param>
		/// <param name="pupilX">Receives the X column.</param>
		/// <param name="pupilY">Receives the Y column.</param>
		private static void ExtractPositions(NDArray pupils, out double[] pupilX, out double[] pupilY)
		{
			int frameCount = pupils.Shape[0];
			pupilX = new double[frameCount];
			pupilY = new double[frameCount];
			for (int frame = 0; frame < frameCount; frame++)
			{
				pupilX[frame] = (double)pupils[frame, 0];
				pupilY[frame] = (double)pupils[frame, 1];
			}
		}

		/// <summary>
		/// Copies a two-dimensional array into a new double array.
		/// </summary>
		/// <param name="source">The array to copy.</param>
		/// <returns>A double array with the same values.</returns>
		private static NDArray CopyAsDouble(NDArray source)
		{
			int rowCount = source.Shape[0];
			int columnCount = source.Shape[1];
			NDArray copy = new NDArray(NPTypeCode.Double, Shape.Matrix(rowCount, columnCount));
			for (int row = 0; row < rowCount; row++)
				for (int column = 0; column < columnCount; column++)
					copy[row, column] = (double)source[row, column];
			return copy;
		}

		/// <summary>
		/// Computes the population standard deviation.
		/// </summary>
		/// <param name="values">The values.</param>
		/// <returns>The standard deviation.</returns>
		private static double StandardDeviation(List<double> values)
		{
			double mean = values.Average();
			double sumSquared = 0;
			foreach (double value in values)
				sumSquared += (value - mean) * (value - mean);
			return Math.Sqrt(sumSquared / values.Count);
		}

		/// <summary>
		/// Computes a percentile with linear interpolation between order statistics.
		/// </summary>
		/// <param name="values">The values.</param>
		/// <param name="percentile">Percentile in the range 0 to 100.</param>
		/// <returns>The interpolated percentile value.</returns>
		private static double Percentile(double[] values, double percentile)
		{
			double[] sorted = values.OrderBy(value => value).ToArray();
			double rank = (sorted.Length - 1) * percentile / 100.0;
			int lower = (int)Math.Floor(rank);
			int upper = Math.Min(lower + 1, sorted.Length - 1);
			return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
		}
	}
}
