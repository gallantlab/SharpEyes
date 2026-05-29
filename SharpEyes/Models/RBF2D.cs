using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Avalonia;
using Num = NumSharp.np;

// Note: try not to use NumSharp data structures in these classes
// Because they're internally contained

namespace Eyetracking
{
	/// <summary>
	/// Object-oriented wrapper to alglib's hierarchical RBF functions.
	/// Specifically maps a 2D values to a scalar
	/// </summary>
	public class RBF2D
	{
		/// <summary>
		/// Performs leave-one-out selection of best hyperparameter values for the RBF
		/// </summary>
		/// <param name="x">data X</param>
		/// <param name="y">data Y</param>
		/// <param name="value">data values at (X, Y)</param>
		/// <param name="baseRadiusRange">range of values for the base radius, must > 0</param>
		/// <param name="numBaseRadii">number of values for the base radius</param>
		/// <param name="numLayersRange">number of layers, must be > 0</param>
		/// <param name="regularizerRange">range of regularizers to use, must be >= 0</param>
		/// <param name="numRegularizers">number of regularizers</param>
		/// <param name="logSpaceRegularizers">log space instead of linearly space regularizers?</param>
		/// <returns>RBF fit on all of the data using the parameter that generated the lowest sum of squares loss</returns>
		public static RBF2D SearchHyperparameters(double[] x, double[] y, double[] value, Tuple<double, double> baseRadiusRange, int numBaseRadii,
												  Tuple<int, int> numLayersRange, Tuple<double, double> regularizerRange, int numRegularizers, bool logSpaceRegularizers)
		{
			RBF2D best = null;
			double bestError = Double.PositiveInfinity;
			List<double> regularizers = new List<double>
			{
				regularizerRange.Item1
			};
			double start = regularizerRange.Item1;
			double end = regularizerRange.Item2;
			if (logSpaceRegularizers)
			{
				start = start == 0 ? -6 : Math.Log(start);
				end = Math.Log(end);
			}
			foreach (double regularizer in Num.linspace(start, end, regularizerRange.Item1 == 0 ? numRegularizers - 1 : numRegularizers))
			{
				regularizers.Add(logSpaceRegularizers ? Math.Pow(10, regularizer) : regularizer);
			}

			object parallelLock = new object();
			foreach (double baseRadius in Num.linspace(baseRadiusRange.Item1, baseRadiusRange.Item2, numBaseRadii))
				for (int numLayers = numLayersRange.Item1; numLayers < numLayersRange.Item2 + 1; numLayers++)
					Parallel.ForEach(regularizers, (double regularizer) => 
					{
						RBF2D RBF = new RBF2D(x, y, value)
						{
							baseRadius = baseRadius,
							numLayers = numLayers,
							regularizer = regularizer
						};
						double error = RBF.CrossValidate();
						lock (parallelLock)
						{
							if (error < bestError)
							{
								best = RBF;
								bestError = error;
							}
						}
					});
					
			return best;
		}

		public static async Task<RBF2D> SearchHyperparametersAsync(double[] x, double[] y, double[] value, Tuple<double, double> baseRadiusRange, int numBaseRadii,
			Tuple<int, int> numLayersRange, Tuple<double, double> regularizerRange, int numRegularizers, bool logSpaceRegularizers)
		{
			return await Task.Run(() => SearchHyperparameters(x, y, value, baseRadiusRange, numBaseRadii, numLayersRange,
				regularizerRange,
				numRegularizers, logSpaceRegularizers));
		}

		private alglib.rbfmodel RBFModel;
		private alglib.rbfreport RBFReport;

		private double[] dataX;
		private double[] dataY;
		private double[] dataValue;

		public Nullable<double> baseRadius = null;
		public int numLayers = 5;
		public double regularizer = 0;

		public double RMSError { get; private set; } = 10000;

		public RBF2D()
		{
			alglib.rbfcreate(2, 1, out RBFModel);
		}

		public RBF2D(double[] x, double[] y, double[] value)
		{
			alglib.rbfcreate(2, 1, out RBFModel);
			dataX = x;
			dataY = y;
			dataValue = value;
		}

		public void Fit()
		{
			Fit(dataX, dataY, dataValue);
		}

		public void Fit(double[] x, double[] y, double[] value)
		{
			dataX = x;
			dataY = y;
			dataValue = value;

			// alglib expects a 2D array in [sample, x-y-value]
			double[,] formattedData = new double[dataX.Length, 3];
			for (int i = 0; i < dataX.Length; i++)
			{
				formattedData[i, 0] = dataX[i];
				formattedData[i, 1] = dataY[i];
				formattedData[i, 2] = dataValue[i];
			}
			alglib.rbfsetpoints(RBFModel, formattedData);

			// this call sets the algorithms and have hyperparameters that need tuning
			alglib.rbfsetalgohierarchical(RBFModel, baseRadius ?? 1.0, numLayers, regularizer);

			alglib.rbfbuildmodel(RBFModel, out RBFReport);
		}

		/// <summary>
		/// Does leave-one-out cross validation and stores the mean error
		/// </summary>
		public double CrossValidate()
		{
			double sumErrorSquared = 0;
			object parallelLock = new object();
			Parallel.For(0, dataX.Length, i =>
			{
				alglib.rbfcreate(2, 1, out alglib.rbfmodel thisModel);

				double[,] formattedData = new double[dataX.Length - 1, 3];
				int index = 0;
				for (int j = 0; j < dataX.Length; j++)
				{
					if (j == i) continue;
					formattedData[index, 0] = dataX[j];
					formattedData[index, 1] = dataY[j];
					formattedData[index++, 2] = dataValue[j];
				}
				alglib.rbfsetpoints(thisModel, formattedData);
				alglib.rbfsetalgohierarchical(thisModel, baseRadius ?? 1.0, numLayers, regularizer);
				alglib.rbfbuildmodel(thisModel, out alglib.rbfreport thisReport);

				double error = alglib.rbfcalc2(thisModel, dataX[i], dataY[i]) - dataValue[i];
				lock (parallelLock)
				{
					sumErrorSquared += error * error;
				}
			});
			RMSError = Math.Sqrt(sumErrorSquared / dataX.Length);
			return RMSError;
		}

		public double Evaluate(double x, double y)
		{
			return alglib.rbfcalc2(RBFModel, x, y);
		}
	}


	/// <summary>
	/// Encapsulated two RBF2D objects to map between two 2D coordinate systems.
	/// Exists because we need to optimize RMS errors in 2D
	/// </summary>
	public class RBF2DTo2D
	{
		RBF2D xInterpolator = null, yInterpolator = null;

		private double[] dataX;
		private double[] dataY;
		private double[] dataValueX;
		private double[] dataValueY;

		private Nullable<double> _baseRadius = null;
		public Nullable<double> baseRadius
		{
			get { return _baseRadius; }
			set
			{
				xInterpolator.baseRadius = yInterpolator.baseRadius = value;
				_baseRadius = value;
			}
		}
		private int _numLayers = 5;
		public int numLayers
		{
			get { return _numLayers; }
			set
			{
				xInterpolator.numLayers = yInterpolator.numLayers = value;
				_numLayers = value;
			}
		}

		private double _regularizer = 0;
		public double regularizer
		{
			get { return _regularizer; }
			set
			{
				xInterpolator.regularizer = yInterpolator.regularizer = value;
				_regularizer = value;
			}
		}

		public double RMSError { get; private set; }

		/// <summary>
		/// Performs leave-one-out selection of best hyperparameter values for the RBF
		/// </summary>
		/// <param name="x">data X</param>
		/// <param name="y">data Y</param>
		/// <param name="valueX">data X values at (X, Y)</param>
		/// <param name="valueY">data Y values at (X, Y)</param>
		/// <param name="baseRadiusRange">range of values for the base radius, must > 0</param>
		/// <param name="numBaseRadii">number of values for the base radius</param>
		/// <param name="numLayersRange">number of layers, must be > 0</param>
		/// <param name="regularizerRange">range of regularizers to use, must be >= 0</param>
		/// <param name="numRegularizers">number of regularizers</param>
		/// <param name="logSpaceRegularizers">log space instead of linearly space regularizers?</param>
		/// <returns>RBF fit on all of the data using the parameter that generated the lowest sum of squares loss</returns>
		public static RBF2DTo2D SearchHyperparameters(double[] x, double[] y, double[] valueX, double[] valueY, Tuple<double, double> baseRadiusRange, int numBaseRadii,
													  Tuple<int, int> numLayersRange, Tuple<double, double> regularizerRange, int numRegularizers, bool logSpaceRegularizers)
		{
			RBF2DTo2D best = null;
			double bestError = Double.PositiveInfinity;
			List<double> regularizers = new List<double>
			{
				regularizerRange.Item1
			};
			double start = regularizerRange.Item1;
			double end = regularizerRange.Item2;
			if (logSpaceRegularizers)
			{
				start = start == 0 ? -6 : Math.Log(start);
				end = Math.Log(end);
			}
			foreach (double regularizer in Num.linspace(start, end, regularizerRange.Item1 == 0 ? numRegularizers - 1 : numRegularizers))
			{
				regularizers.Add(logSpaceRegularizers ? Math.Pow(10, regularizer) : regularizer);
			}

			foreach (double baseRadius in Num.linspace(baseRadiusRange.Item1, baseRadiusRange.Item2, numBaseRadii))
				for (int numLayers = numLayersRange.Item1; numLayers < numLayersRange.Item2 + 1; numLayers++)
					foreach (double regularizer in regularizers)
					{
						RBF2DTo2D RBF = new RBF2DTo2D(x, y, valueX, valueY);
						RBF.baseRadius = baseRadius;
						RBF.numLayers = numLayers;
						RBF.regularizer = regularizer;
						double error = RBF.CrossValidate();
						if (error < bestError)
						{
							best = RBF;
							bestError = error;
						}
					}
			return best;
		}

		public RBF2DTo2D()
		{
			xInterpolator = new RBF2D();
			yInterpolator = new RBF2D();
		}

		public RBF2DTo2D(double[] x, double[] y, double[] valueX, double[] valueY)
		{
			xInterpolator = new RBF2D(x, y, valueX);
			yInterpolator = new RBF2D(x, y, valueY);

			dataX = x;
			dataY = y;
			dataValueX = valueX;
			dataValueY = valueY;
		}

		public void Fit()
		{
			Fit(dataX, dataY, dataValueX, dataValueY);
		}

		public void Fit(double[] x, double[] y, double[] valueX, double[] valueY)
		{
			dataX = x;
			dataY = y;
			dataValueX = valueX;
			dataValueY = valueY;

			xInterpolator.Fit(x, y, valueX);
			yInterpolator.Fit(x, y, valueY);
		}

		public double CrossValidate()
		{
			double sumErrorSquared = 0;
			object parallelLock = new object();
			Parallel.For(0, dataX.Length, i =>
			{
				alglib.rbfcreate(2, 1, out alglib.rbfmodel thisXModel);
				alglib.rbfcreate(2, 1, out alglib.rbfmodel thisYModel);

				double[,] formattedDataX = new double[dataX.Length - 1, 3];
				double[,] formattedDataY = new double[dataX.Length - 1, 3];
				int index = 0;
				for (int j = 0; j < dataX.Length; j++)
				{
					if (j == i) continue;
					formattedDataX[index, 0] = formattedDataY[index, 0] = dataX[j];
					formattedDataX[index, 1] = formattedDataY[index, 1] = dataY[j];
					formattedDataX[index, 2] = dataValueX[j];
					formattedDataY[index++, 2] = dataValueY[j];
				}
				alglib.rbfsetpoints(thisXModel, formattedDataX);
				alglib.rbfsetpoints(thisYModel, formattedDataY);
				alglib.rbfsetalgohierarchical(thisXModel, baseRadius ?? 1.0, numLayers, regularizer);
				alglib.rbfsetalgohierarchical(thisYModel, baseRadius ?? 1.0, numLayers, regularizer);
				alglib.rbfbuildmodel(thisXModel, out alglib.rbfreport thisXReport);
				alglib.rbfbuildmodel(thisYModel, out alglib.rbfreport thisYReport);

				double errorX = alglib.rbfcalc2(thisXModel, dataX[i], dataY[i]) - dataValueX[i];
				double errorY = alglib.rbfcalc2(thisYModel, dataX[i], dataY[i]) - dataValueY[i];
				lock (parallelLock)
				{
					sumErrorSquared += errorX * errorX + errorY * errorY;
				}
			});
			RMSError = Math.Sqrt(sumErrorSquared / dataX.Length);
			return RMSError;
		}

		public Point Evaluate(Point point)
		{
			return Evaluate(point.X, point.Y);
		}

		public Point Evaluate(double x, double y)
		{
			return new Point(xInterpolator.Evaluate(x, y),
							 yInterpolator.Evaluate(x, y));
		}
	}
}
