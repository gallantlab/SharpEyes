using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Eyetracking
{
	/// <summary>
	/// Radial basis function kernels. The legacy convention supports all but <see cref="InverseQuadratic"/>.
	/// </summary>
	public enum RBFKernel
	{
		ThinPlate,
		Multiquadric,
		Linear,
		Cubic,
		Quintic,
		InverseMultiquadric,
		InverseQuadratic,
		Gaussian,
	}

	/// <summary>
	/// A fitted radial basis function mapping from pupil space to screen space: the nodes, the coefficients of the
	/// kernel and polynomial terms, and the leave-one-out residuals of the fit.
	/// </summary>
	public class RBFMappingModel
	{
		public bool UseLegacyConvention { get; set; } = true;
		public RBFKernel Kernel { get; set; } = RBFKernel.ThinPlate;
		public double Smoothness { get; set; } = 0;
		public double Epsilon { get; set; } = 1;

		/// <summary>(nNodes x 2) pupil positions used as nodes.</summary>
		public double[,] Nodes { get; set; } = new double[0, 2];

		/// <summary>(nNodes x 2) screen positions at the nodes.</summary>
		public double[,] ScreenPoints { get; set; } = new double[0, 2];

		/// <summary>(nNodes + nMonomials x 2) coefficients for the horizontal and vertical outputs.</summary>
		public double[,] Coefficients { get; set; } = new double[0, 2];

		/// <summary>(nMonomials x 2) powers of the polynomial term's monomials.</summary>
		public int[,] PolynomialPowers { get; set; } = new int[0, 2];

		/// <summary>Shift subtracted from positions before evaluating the polynomial term.</summary>
		public double[] Shift { get; set; } = new double[] { 0, 0 };

		/// <summary>Scale dividing shifted positions before evaluating the polynomial term.</summary>
		public double[] Scale { get; set; } = new double[] { 1, 1 };

		/// <summary>(nNodes x 2) leave-one-out residuals at the nodes.</summary>
		public double[,] Residuals { get; set; } = new double[0, 2];

		public double RMSError { get; set; } = Double.NaN;

		/// <summary>
		/// Maps pupil positions to screen positions. Rows with a non-finite coordinate map to NaN.
		/// </summary>
		/// <param name="positions">(nSamples x 2) pupil positions.</param>
		/// <returns>(nSamples x 2) screen positions.</returns>
		public double[,] MapToScreen(double[,] positions)
		{
			int sampleCount = positions.GetLength(0);
			int nodeCount = Nodes.GetLength(0);
			int monomialCount = PolynomialPowers.GetLength(0);
			double[,] output = new double[sampleCount, 2];
			Parallel.For(0, sampleCount, sample =>
			{
				double x = positions[sample, 0];
				double y = positions[sample, 1];
				if (!Double.IsFinite(x) || !Double.IsFinite(y))
				{
					output[sample, 0] = Double.NaN;
					output[sample, 1] = Double.NaN;
					return;
				}
				double outputX = 0;
				double outputY = 0;
				for (int node = 0; node < nodeCount; node++)
				{
					double distance = RBFMapping.Distance(x, y, Nodes[node, 0], Nodes[node, 1]);
					double basis = RBFMapping.EvaluateKernel(distance, Kernel, Epsilon, UseLegacyConvention);
					outputX += Coefficients[node, 0] * basis;
					outputY += Coefficients[node, 1] * basis;
				}
				for (int monomial = 0; monomial < monomialCount; monomial++)
				{
					double basis = RBFMapping.EvaluateMonomial(x, y, PolynomialPowers, monomial, Shift, Scale);
					outputX += Coefficients[nodeCount + monomial, 0] * basis;
					outputY += Coefficients[nodeCount + monomial, 1] * basis;
				}
				output[sample, 0] = outputX;
				output[sample, 1] = outputY;
			});
			return output;
		}
	}

	/// <summary>
	/// Radial basis function fitting for mapping pupil positions to screen positions, mirroring the conventions of
	/// scipy's legacy Rbf class and its RBFInterpolator class. Hyperparameters are selected by leave-one-out error
	/// computed in closed form (Rippa 1999): for the system B c = y, the residual at node k of the model fitted
	/// without node k equals c_k divided by the k-th diagonal entry of the inverse of B. The closed form holds for
	/// a fixed kernel, so under the legacy convention, whose default shape parameter is computed from the node set,
	/// the residuals of the multiquadric, inverse, and gaussian kernels correspond to a leave-one-out with the full
	/// set's shape parameter rather than one recomputed for each fold.
	/// </summary>
	public static class RBFMapping
	{
		public static readonly RBFKernel[] LegacyKernels =
		{
			RBFKernel.ThinPlate, RBFKernel.Multiquadric, RBFKernel.Linear, RBFKernel.Cubic, RBFKernel.Quintic,
			RBFKernel.InverseMultiquadric, RBFKernel.Gaussian,
		};

		public static readonly RBFKernel[] NewKernels =
		{
			RBFKernel.ThinPlate, RBFKernel.Multiquadric, RBFKernel.Linear, RBFKernel.Cubic, RBFKernel.Quintic,
			RBFKernel.InverseMultiquadric, RBFKernel.InverseQuadratic, RBFKernel.Gaussian,
		};

		private static readonly Dictionary<RBFKernel, int> NewKernelMinimumDegrees = new Dictionary<RBFKernel, int>
		{
			{ RBFKernel.Multiquadric, 0 },
			{ RBFKernel.Linear, 0 },
			{ RBFKernel.ThinPlate, 1 },
			{ RBFKernel.Cubic, 1 },
			{ RBFKernel.Quintic, 2 },
		};

		// == Kernels and polynomial terms ==

		/// <summary>
		/// Computes the Euclidean distance between two positions.
		/// </summary>
		/// <param name="x1">First X.</param>
		/// <param name="y1">First Y.</param>
		/// <param name="x2">Second X.</param>
		/// <param name="y2">Second Y.</param>
		/// <returns>The distance.</returns>
		public static double Distance(double x1, double y1, double x2, double y2)
		{
			double deltaX = x1 - x2;
			double deltaY = y1 - y2;
			return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
		}

		/// <summary>
		/// Evaluates the thin plate kernel r^2 log(r), which is zero at zero distance.
		/// </summary>
		/// <param name="distance">The distance.</param>
		/// <returns>The kernel value.</returns>
		private static double ThinPlate(double distance)
		{
			return distance == 0 ? 0 : distance * distance * Math.Log(distance);
		}

		/// <summary>
		/// Evaluates a kernel under the selected convention.
		/// </summary>
		/// <param name="distance">The distance between two positions.</param>
		/// <param name="kernel">The kernel.</param>
		/// <param name="epsilon">Shape parameter, dividing the distance under the legacy convention and multiplying it otherwise.</param>
		/// <param name="useLegacyConvention">Whether to use the legacy convention.</param>
		/// <returns>The kernel value.</returns>
		public static double EvaluateKernel(double distance, RBFKernel kernel, double epsilon, bool useLegacyConvention)
		{
			if (useLegacyConvention)
			{
				double scaled = distance / epsilon;
				switch (kernel)
				{
					case RBFKernel.Multiquadric: return Math.Sqrt(scaled * scaled + 1);
					case RBFKernel.InverseMultiquadric: return 1.0 / Math.Sqrt(scaled * scaled + 1);
					case RBFKernel.Gaussian: return Math.Exp(-scaled * scaled);
					case RBFKernel.Linear: return distance;
					case RBFKernel.Cubic: return distance * distance * distance;
					case RBFKernel.Quintic: return distance * distance * distance * distance * distance;
					case RBFKernel.ThinPlate: return ThinPlate(distance);
					default: throw new ArgumentException(String.Format("Kernel not available under the legacy convention: {0}", kernel));
				}
			}
			else
			{
				double scaled = distance * epsilon;
				switch (kernel)
				{
					case RBFKernel.Linear: return -scaled;
					case RBFKernel.ThinPlate: return ThinPlate(scaled);
					case RBFKernel.Cubic: return scaled * scaled * scaled;
					case RBFKernel.Quintic: return -scaled * scaled * scaled * scaled * scaled;
					case RBFKernel.Multiquadric: return -Math.Sqrt(scaled * scaled + 1);
					case RBFKernel.InverseMultiquadric: return 1.0 / Math.Sqrt(scaled * scaled + 1);
					case RBFKernel.InverseQuadratic: return 1.0 / (scaled * scaled + 1);
					case RBFKernel.Gaussian: return Math.Exp(-scaled * scaled);
					default: throw new ArgumentException(String.Format("Unknown kernel: {0}", kernel));
				}
			}
		}

		/// <summary>
		/// Lists the powers of the monomials in the polynomial term, which is empty under the legacy convention and has
		/// the minimum degree for the kernel under the RBFInterpolator convention, in that class's ordering.
		/// </summary>
		/// <param name="kernel">The kernel.</param>
		/// <param name="useLegacyConvention">Whether the legacy convention applies.</param>
		/// <returns>(nMonomials x 2) powers of X and Y.</returns>
		public static int[,] ComputeMonomialPowers(RBFKernel kernel, bool useLegacyConvention)
		{
			int degree = -1;
			if (!useLegacyConvention)
				degree = Math.Max(NewKernelMinimumDegrees.TryGetValue(kernel, out int minimumDegree) ? minimumDegree : -1, 0);
			List<int[]> powers = new List<int[]>();
			for (int totalDegree = 0; totalDegree <= degree; totalDegree++)
				for (int powerY = 0; powerY <= totalDegree; powerY++)
					powers.Add(new int[] { totalDegree - powerY, powerY });
			int[,] result = new int[powers.Count, 2];
			for (int monomial = 0; monomial < powers.Count; monomial++)
			{
				result[monomial, 0] = powers[monomial][0];
				result[monomial, 1] = powers[monomial][1];
			}
			return result;
		}

		/// <summary>
		/// Evaluates one monomial at a shifted and scaled position.
		/// </summary>
		/// <param name="x">Position X.</param>
		/// <param name="y">Position Y.</param>
		/// <param name="powers">(nMonomials x 2) monomial powers.</param>
		/// <param name="monomial">Index of the monomial to evaluate.</param>
		/// <param name="shift">Shift subtracted from the position.</param>
		/// <param name="scale">Scale dividing the shifted position.</param>
		/// <returns>The monomial value.</returns>
		public static double EvaluateMonomial(double x, double y, int[,] powers, int monomial, double[] shift, double[] scale)
		{
			double scaledX = (x - shift[0]) / scale[0];
			double scaledY = (y - shift[1]) / scale[1];
			return Math.Pow(scaledX, powers[monomial, 0]) * Math.Pow(scaledY, powers[monomial, 1]);
		}

		/// <summary>
		/// Computes the default shape parameter of the legacy convention, which is the product of the bounding box
		/// edges of the valid nodes divided by their count, raised to one over the number of dimensions.
		/// </summary>
		/// <param name="nodes">(nNodes x 2) node coordinates.</param>
		/// <param name="valid">Validity of each node.</param>
		/// <returns>The shape parameter.</returns>
		private static double ComputeLegacyEpsilon(double[,] nodes, bool[] valid)
		{
			ComputeBoundingBox(nodes, valid, out double minX, out double maxX, out double minY, out double maxY);
			double edgeX = maxX - minX == 0 ? 1.0 : maxX - minX;
			double edgeY = maxY - minY == 0 ? 1.0 : maxY - minY;
			int numValid = Math.Max(valid.Count(isValid => isValid), 1);
			return Math.Sqrt(edgeX * edgeY / numValid);
		}

		/// <summary>
		/// Computes the shift and scale that map the valid nodes' bounding box to the unit square, as RBFInterpolator does.
		/// </summary>
		/// <param name="nodes">(nNodes x 2) node coordinates.</param>
		/// <param name="valid">Validity of each node.</param>
		/// <param name="shift">Receives the shift.</param>
		/// <param name="scale">Receives the scale, with zero extents replaced by one.</param>
		private static void ComputePolynomialDomain(double[,] nodes, bool[] valid, out double[] shift, out double[] scale)
		{
			ComputeBoundingBox(nodes, valid, out double minX, out double maxX, out double minY, out double maxY);
			shift = new double[] { (maxX + minX) / 2, (maxY + minY) / 2 };
			scale = new double[] { (maxX - minX) / 2, (maxY - minY) / 2 };
			if (scale[0] == 0) scale[0] = 1;
			if (scale[1] == 0) scale[1] = 1;
		}

		/// <summary>
		/// Computes the bounding box of the valid nodes.
		/// </summary>
		/// <param name="nodes">(nNodes x 2) node coordinates.</param>
		/// <param name="valid">Validity of each node.</param>
		/// <param name="minX">Receives the smallest X.</param>
		/// <param name="maxX">Receives the largest X.</param>
		/// <param name="minY">Receives the smallest Y.</param>
		/// <param name="maxY">Receives the largest Y.</param>
		private static void ComputeBoundingBox(double[,] nodes, bool[] valid, out double minX, out double maxX, out double minY, out double maxY)
		{
			minX = Double.PositiveInfinity;
			maxX = Double.NegativeInfinity;
			minY = Double.PositiveInfinity;
			maxY = Double.NegativeInfinity;
			for (int node = 0; node < valid.Length; node++)
			{
				if (!valid[node]) continue;
				minX = Math.Min(minX, nodes[node, 0]);
				maxX = Math.Max(maxX, nodes[node, 0]);
				minY = Math.Min(minY, nodes[node, 1]);
				maxY = Math.Max(maxY, nodes[node, 1]);
			}
			if (Double.IsInfinity(minX))
			{
				minX = maxX = 0;
				minY = maxY = 0;
			}
		}

		// == Systems ==

		/// <summary>
		/// Builds the system matrix without smoothing. Invalid nodes get a zero row and column with a one on the
		/// diagonal, which decouples them so the remaining block is exactly the system for the valid nodes alone.
		/// </summary>
		/// <param name="nodes">(nNodes x 2) node coordinates.</param>
		/// <param name="valid">Validity of each node.</param>
		/// <param name="kernel">The kernel.</param>
		/// <param name="useLegacyConvention">Whether to use the legacy convention.</param>
		/// <param name="epsilon">Shape parameter, or null for the convention's default.</param>
		/// <param name="epsilonUsed">Receives the shape parameter used.</param>
		/// <param name="powers">Receives the (nMonomials x 2) monomial powers.</param>
		/// <param name="shift">Receives the polynomial domain shift.</param>
		/// <param name="scale">Receives the polynomial domain scale.</param>
		/// <returns>The (nNodes + nMonomials) square system matrix.</returns>
		private static double[,] BuildBaseSystem(double[,] nodes, bool[] valid, RBFKernel kernel, bool useLegacyConvention, double? epsilon,
			out double epsilonUsed, out int[,] powers, out double[] shift, out double[] scale)
		{
			int nodeCount = valid.Length;
			if (useLegacyConvention)
			{
				epsilonUsed = epsilon ?? ComputeLegacyEpsilon(nodes, valid);
				shift = new double[] { 0, 0 };
				scale = new double[] { 1, 1 };
			}
			else
			{
				epsilonUsed = epsilon ?? 1.0;
				ComputePolynomialDomain(nodes, valid, out shift, out scale);
			}
			powers = ComputeMonomialPowers(kernel, useLegacyConvention);
			int monomialCount = powers.GetLength(0);
			int size = nodeCount + monomialCount;
			double[,] system = new double[size, size];
			for (int i = 0; i < nodeCount; i++)
			{
				if (!valid[i])
				{
					system[i, i] = 1;
					continue;
				}
				for (int j = 0; j < nodeCount; j++)
				{
					if (!valid[j]) continue;
					double distance = Distance(nodes[i, 0], nodes[i, 1], nodes[j, 0], nodes[j, 1]);
					system[i, j] = EvaluateKernel(distance, kernel, epsilonUsed, useLegacyConvention);
				}
				for (int monomial = 0; monomial < monomialCount; monomial++)
				{
					double value = EvaluateMonomial(nodes[i, 0], nodes[i, 1], powers, monomial, shift, scale);
					system[i, nodeCount + monomial] = value;
					system[nodeCount + monomial, i] = value;
				}
			}
			return system;
		}

		/// <summary>
		/// Adds the smoothing term to the diagonal entries of the valid nodes, subtracting under the legacy convention
		/// and adding under the RBFInterpolator convention.
		/// </summary>
		/// <param name="baseSystem">The unsmoothed system matrix.</param>
		/// <param name="valid">Validity of each node.</param>
		/// <param name="smoothness">Smoothing parameter.</param>
		/// <param name="useLegacyConvention">Whether the legacy convention applies.</param>
		/// <returns>A smoothed copy of the system matrix.</returns>
		private static double[,] AddSmoothing(double[,] baseSystem, bool[] valid, double smoothness, bool useLegacyConvention)
		{
			double[,] system = (double[,])baseSystem.Clone();
			double sign = useLegacyConvention ? -1.0 : 1.0;
			for (int node = 0; node < valid.Length; node++)
				if (valid[node])
					system[node, node] += sign * smoothness;
			return system;
		}

		/// <summary>
		/// Solves the smoothed system for both outputs and computes the leave-one-out residuals, target minus the
		/// prediction of the model fitted without that node, from one inversion.
		/// </summary>
		/// <param name="system">The smoothed system matrix.</param>
		/// <param name="valid">Validity of each node.</param>
		/// <param name="screenPoints">(nNodes x 2) screen positions at the nodes.</param>
		/// <param name="coefficients">Receives the (size x 2) coefficients.</param>
		/// <param name="residuals">Receives the (nNodes x 2) residuals, NaN for invalid nodes.</param>
		/// <returns>False when the system is singular.</returns>
		private static bool TrySolve(double[,] system, bool[] valid, double[,] screenPoints, out double[,] coefficients, out double[,] residuals)
		{
			int nodeCount = valid.Length;
			int size = system.GetLength(0);
			coefficients = new double[size, 2];
			residuals = new double[nodeCount, 2];
			if (!DenseMatrix.TryInvert(system, out double[,] inverse))
				return false;
			for (int row = 0; row < size; row++)
			{
				double sumX = 0;
				double sumY = 0;
				for (int node = 0; node < nodeCount; node++)
				{
					if (!valid[node]) continue;
					sumX += inverse[row, node] * screenPoints[node, 0];
					sumY += inverse[row, node] * screenPoints[node, 1];
				}
				coefficients[row, 0] = sumX;
				coefficients[row, 1] = sumY;
			}
			for (int node = 0; node < nodeCount; node++)
			{
				if (!valid[node])
				{
					residuals[node, 0] = Double.NaN;
					residuals[node, 1] = Double.NaN;
					continue;
				}
				residuals[node, 0] = coefficients[node, 0] / inverse[node, node];
				residuals[node, 1] = coefficients[node, 1] / inverse[node, node];
			}
			return true;
		}

		/// <summary>
		/// Marks nodes with finite pupil and screen coordinates as valid.
		/// </summary>
		/// <param name="nodes">(nNodes x 2) node coordinates.</param>
		/// <param name="screenPoints">(nNodes x 2) screen positions.</param>
		/// <returns>Validity of each node.</returns>
		private static bool[] FindValidNodes(double[,] nodes, double[,] screenPoints)
		{
			int nodeCount = nodes.GetLength(0);
			bool[] valid = new bool[nodeCount];
			for (int node = 0; node < nodeCount; node++)
				valid[node] = Double.IsFinite(nodes[node, 0]) && Double.IsFinite(nodes[node, 1])
					&& Double.IsFinite(screenPoints[node, 0]) && Double.IsFinite(screenPoints[node, 1]);
			return valid;
		}

		// == Fitting ==

		/// <summary>
		/// Computes the leave-one-out root mean square error over both axes for every node set, kernel, and smoothness,
		/// in parallel over node sets and kernels.
		/// </summary>
		/// <param name="nodeStack">(nSets x nNodes x 2) pupil positions, NaN marking invalid nodes.</param>
		/// <param name="screenPoints">(nNodes x 2) screen positions at the nodes.</param>
		/// <param name="kernels">Kernels to evaluate.</param>
		/// <param name="smoothnesses">Smoothing parameters to evaluate.</param>
		/// <param name="useLegacyConvention">Whether to use the legacy convention.</param>
		/// <param name="epsilon">Shape parameter, or null for the convention's default.</param>
		/// <param name="minimumValidNodes">Node sets with fewer valid nodes get an infinite error.</param>
		/// <returns>An (nSets x nKernels x nSmoothnesses) array of errors, infinite where no fit was possible.</returns>
		public static double[,,] EvaluateHyperparameterGrid(double[,,] nodeStack, double[,] screenPoints, IList<RBFKernel> kernels,
			double[] smoothnesses, bool useLegacyConvention, double? epsilon, int minimumValidNodes = 3)
		{
			int setCount = nodeStack.GetLength(0);
			int nodeCount = nodeStack.GetLength(1);
			int kernelCount = kernels.Count;
			double[,,] errors = new double[setCount, kernelCount, smoothnesses.Length];
			for (int set = 0; set < setCount; set++)
				for (int kernel = 0; kernel < kernelCount; kernel++)
					for (int smoothness = 0; smoothness < smoothnesses.Length; smoothness++)
						errors[set, kernel, smoothness] = Double.PositiveInfinity;

			Parallel.For(0, setCount * kernelCount, index =>
			{
				int set = index / kernelCount;
				int kernelIndex = index % kernelCount;
				double[,] nodes = new double[nodeCount, 2];
				for (int node = 0; node < nodeCount; node++)
				{
					nodes[node, 0] = nodeStack[set, node, 0];
					nodes[node, 1] = nodeStack[set, node, 1];
				}
				bool[] valid = FindValidNodes(nodes, screenPoints);
				int numValid = valid.Count(isValid => isValid);
				double[,] baseSystem = BuildBaseSystem(nodes, valid, kernels[kernelIndex], useLegacyConvention, epsilon,
					out double epsilonUsed, out int[,] powers, out double[] shift, out double[] scale);
				if (numValid < Math.Max(minimumValidNodes, powers.GetLength(0) + 1))
					return;
				for (int smoothnessIndex = 0; smoothnessIndex < smoothnesses.Length; smoothnessIndex++)
				{
					double[,] system = AddSmoothing(baseSystem, valid, smoothnesses[smoothnessIndex], useLegacyConvention);
					if (!TrySolve(system, valid, screenPoints, out double[,] coefficients, out double[,] residuals))
						continue;
					double sumSquared = 0;
					for (int node = 0; node < nodeCount; node++)
					{
						if (!valid[node]) continue;
						sumSquared += residuals[node, 0] * residuals[node, 0] + residuals[node, 1] * residuals[node, 1];
					}
					double rms = Math.Sqrt(sumSquared / (2 * numValid));
					if (Double.IsFinite(rms))
						errors[set, kernelIndex, smoothnessIndex] = rms;
				}
			});
			return errors;
		}

		/// <summary>
		/// Fits the mapping on the valid nodes by solving the smoothed system and computes its leave-one-out residuals.
		/// </summary>
		/// <param name="nodes">(nNodes x 2) pupil positions, NaN marking nodes to leave out.</param>
		/// <param name="screenPoints">(nNodes x 2) screen positions at the nodes.</param>
		/// <param name="kernel">The kernel.</param>
		/// <param name="smoothness">Smoothing parameter.</param>
		/// <param name="useLegacyConvention">Whether to use the legacy convention.</param>
		/// <param name="epsilon">Shape parameter, or null for the convention's default.</param>
		/// <returns>The fitted model.</returns>
		public static RBFMappingModel Fit(double[,] nodes, double[,] screenPoints, RBFKernel kernel, double smoothness, bool useLegacyConvention, double? epsilon)
		{
			bool[] valid = FindValidNodes(nodes, screenPoints);
			int validCount = valid.Count(isValid => isValid);
			double[,] validNodes = new double[validCount, 2];
			double[,] validScreen = new double[validCount, 2];
			int validIndex = 0;
			for (int node = 0; node < valid.Length; node++)
			{
				if (!valid[node]) continue;
				validNodes[validIndex, 0] = nodes[node, 0];
				validNodes[validIndex, 1] = nodes[node, 1];
				validScreen[validIndex, 0] = screenPoints[node, 0];
				validScreen[validIndex, 1] = screenPoints[node, 1];
				validIndex++;
			}
			bool[] allValid = Enumerable.Repeat(true, validCount).ToArray();
			double[,] baseSystem = BuildBaseSystem(validNodes, allValid, kernel, useLegacyConvention, epsilon,
				out double epsilonUsed, out int[,] powers, out double[] shift, out double[] scale);
			double[,] system = AddSmoothing(baseSystem, allValid, smoothness, useLegacyConvention);
			if (!TrySolve(system, allValid, validScreen, out double[,] coefficients, out double[,] residuals))
				throw new InvalidOperationException("The calibration system is singular");
			double sumSquared = 0;
			for (int node = 0; node < validCount; node++)
				sumSquared += residuals[node, 0] * residuals[node, 0] + residuals[node, 1] * residuals[node, 1];
			return new RBFMappingModel
			{
				UseLegacyConvention = useLegacyConvention,
				Kernel = kernel,
				Smoothness = smoothness,
				Epsilon = epsilonUsed,
				Nodes = validNodes,
				ScreenPoints = validScreen,
				Coefficients = coefficients,
				PolynomialPowers = powers,
				Shift = shift,
				Scale = scale,
				Residuals = residuals,
				RMSError = Math.Sqrt(sumSquared / (2.0 * validCount)),
			};
		}
	}

	/// <summary>
	/// Dense matrix inversion for the small calibration systems.
	/// </summary>
	internal static class DenseMatrix
	{
		/// <summary>
		/// Inverts a square matrix by Gauss-Jordan elimination with partial pivoting.
		/// </summary>
		/// <param name="matrix">The matrix to invert; it is not modified.</param>
		/// <param name="inverse">Receives the inverse.</param>
		/// <returns>False when a pivot is too small relative to the matrix's largest entry.</returns>
		public static bool TryInvert(double[,] matrix, out double[,] inverse)
		{
			int size = matrix.GetLength(0);
			double[,] work = (double[,])matrix.Clone();
			inverse = new double[size, size];
			double largestEntry = 0;
			for (int row = 0; row < size; row++)
			{
				inverse[row, row] = 1;
				for (int column = 0; column < size; column++)
					largestEntry = Math.Max(largestEntry, Math.Abs(work[row, column]));
			}
			double pivotTolerance = 1e-14 * (largestEntry == 0 ? 1 : largestEntry);

			for (int column = 0; column < size; column++)
			{
				int pivotRow = column;
				double pivotMagnitude = Math.Abs(work[column, column]);
				for (int row = column + 1; row < size; row++)
				{
					if (Math.Abs(work[row, column]) > pivotMagnitude)
					{
						pivotRow = row;
						pivotMagnitude = Math.Abs(work[row, column]);
					}
				}
				if (!(pivotMagnitude > pivotTolerance))
					return false;
				if (pivotRow != column)
				{
					SwapRows(work, pivotRow, column);
					SwapRows(inverse, pivotRow, column);
				}
				double pivot = work[column, column];
				for (int entry = 0; entry < size; entry++)
				{
					work[column, entry] /= pivot;
					inverse[column, entry] /= pivot;
				}
				for (int row = 0; row < size; row++)
				{
					if (row == column) continue;
					double factor = work[row, column];
					if (factor == 0) continue;
					for (int entry = 0; entry < size; entry++)
					{
						work[row, entry] -= factor * work[column, entry];
						inverse[row, entry] -= factor * inverse[column, entry];
					}
				}
			}
			return true;
		}

		/// <summary>
		/// Swaps two rows of a matrix in place.
		/// </summary>
		/// <param name="matrix">The matrix.</param>
		/// <param name="firstRow">Index of one row.</param>
		/// <param name="secondRow">Index of the other row.</param>
		private static void SwapRows(double[,] matrix, int firstRow, int secondRow)
		{
			int size = matrix.GetLength(1);
			for (int column = 0; column < size; column++)
			{
				double value = matrix[firstRow, column];
				matrix[firstRow, column] = matrix[secondRow, column];
				matrix[secondRow, column] = value;
			}
		}
	}
}
