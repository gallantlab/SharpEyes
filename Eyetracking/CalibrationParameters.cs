using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Eyetracking
{
	/// <summary>
	/// Convenience index with an int property for easier data binding
	/// </summary>
	public struct CalibrationIndex
	{
		public int Index { get; set; }

		public CalibrationIndex(int index)
		{
			Index = index;
		}
	}

	/// <summary>
	/// A struct that stores information about the eyetracking calibration sequence
	/// </summary>
	public class CalibrationParameters
	{
		// video information

		/// <summary>
		/// List of calibration points in screen space
		/// </summary>
		public List<Point> calibrationPoints { get; set; }

		/// <summary>
		/// List of the points in the order that they are presented. Identified by index in <see cref="calibrationPoints"/>
		/// </summary>
		public List<CalibrationIndex> calibrationSequence { get; set; }

		/// <summary>
		/// How long is the fixation at each point
		/// </summary>
		public double calibrationDuration { get; set; } = 2.0;

		/// <summary>
		/// Seconds between the start of the calibration sequence and the presentation of the first point
		/// Because in driving, the first TTL starts the sequence, and the first point is presented on the 2nd TTL
		/// </summary>
		public double calibrationStartDelay { get; set; } = 2.0;

		/// <summary>
		/// Convenience property to convert the start delay to a frame count
		/// </summary>
		public int calibrationStartDelayFrames => (int) (calibrationStartDelay * eyetrackingFPS);

		/// <summary>
		/// DPI Scaling factor. See Unreal Engine
		/// </summary>
		public double DPIUnscaleFactor { get; set; } = 1.0;

		/// <summary>
		/// Default amount of time to discard from beginning of each point
		/// to account for saccade time
		/// </summary>
		public double calibrationPointStartDelaySeconds { get; set; } = 1 / 6.0;

		/// <summary>
		/// Framerate of eyetracking videos
		/// </summary>
		public int eyetrackingFPS { get; set; } = 60;

		/// <summary>
		/// Conversion for delay in seconds to frames
		/// </summary>
		public int calibrationPointStartDelayFrames
		{
			get
			{
				return (int)(calibrationPointStartDelaySeconds * eyetrackingFPS);
			}
		}

		/// <summary>
		/// Conversion for fixation duration from seconds to frames 
		/// </summary>
		public int calibrationDurationFrames
		{
			get
			{
				return (int) (calibrationDuration * eyetrackingFPS);
			}
		}

		// Calibration hyperparameters

		/// <summary>
		/// Minimum base radius for RBF
		/// </summary>
		public double minBaseRadius { get; set; } = 10.0;

		public double maxBaseRadius { get; set; } = 100.0;

		public Tuple<double, double> baseRadiusRange => new Tuple<double, double>(minBaseRadius, maxBaseRadius);

		public int numBaseRadii { get; set; } = 4;

		public int minNumLayers { get; set; } = 1;

		public int maxNumlayers { get; set; } = 3;

		public Tuple<int, int> numLayersRange => new Tuple<int, int>(minNumLayers, maxNumlayers);

		public double minRegularizer { get; set; } = 0;

		public double maxRegularizer { get; set; } = 10;

		public Tuple<double, double> regularizerRange => new Tuple<double, double>(minRegularizer, maxRegularizer);

		public int numRegularizers { get; set; } = 10;

		public bool logSpaceRegularizers { get; set; } = true;

		public CalibrationParameters()
		{
			calibrationPoints = new List<Point>();
			calibrationSequence = new List<CalibrationIndex>();
		}

		public CalibrationParameters(List<Point> calibrationPoints, List<CalibrationIndex> calibrationSequence)
		{
			this.calibrationPoints = calibrationPoints;
			this.calibrationSequence = calibrationSequence;
		}

		public CalibrationParameters(List<Point> calibrationPoints, List<int> calibrationSequence)
		{
			this.calibrationPoints = calibrationPoints;
			this.calibrationSequence = new List<CalibrationIndex>();
			for (int i = 0; i < calibrationSequence.Count; i++)
				this.calibrationSequence.Add(new CalibrationIndex(calibrationSequence[i]));
		}

		public static CalibrationParameters GetDefault35PointCalibrationParameters()
		{
			return new CalibrationParameters(GeneratePoints(1024, 768, 7, 5, 1.0), CalibrationSequence35);
		}

		public static List<Point> GeneratePoints(int width, int height, int numHorizontal, int numVertical, double DPIUnscaleFactor)
		{
			List<Point> points = new List<Point>();

			int ySpace = (int)(height / numVertical / DPIUnscaleFactor);
			int xSpace = (int)(width / numHorizontal / DPIUnscaleFactor);

			int x, y;
			for (int i = -numHorizontal / 2; i < numHorizontal / 2 + 1; i++)
			{
				x = width / 2 + i * xSpace;
				for (int j = -numVertical / 2; j < numVertical / 2 + 1; j++)
				{
					y = height / 2 + j * ySpace;
					points.Add(new Point(x, y));
				}
			}

			return points;
		}

		public static readonly List<int> CalibrationSequence35 = new List<int>(new int[]{13, 30, 17, 16, 2, 27, 1, 28, 25, 10, 26, 9, 14, 5, 34, 32,
																						 31, 12, 8, 33, 18, 19, 3, 23, 29, 20, 7, 0, 4, 24, 22, 11, 15,
																						 21, 6});
	}
}
