using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Eyetracking
{
	/// <summary>
	/// One calibration fixation: the aggregated pupil position for a presented calibration point, the screen position
	/// of that point, and the fitted mapping's leave-one-out residual there.
	/// </summary>
	public class CalibrationFixation
	{
		[XmlAttribute]
		public int Index { get; set; } = 0;

		public double PupilX { get; set; } = Double.NaN;
		public double PupilY { get; set; } = Double.NaN;
		public double PupilStandardDeviationX { get; set; } = Double.NaN;
		public double PupilStandardDeviationY { get; set; } = Double.NaN;
		public double ScreenX { get; set; } = 0;
		public double ScreenY { get; set; } = 0;
		public double ResidualX { get; set; } = Double.NaN;
		public double ResidualY { get; set; } = Double.NaN;

		/// <summary>
		/// Whether the fixation has a pupil position and takes part in the fit.
		/// </summary>
		[XmlIgnore]
		public bool IsValid => !Double.IsNaN(PupilX) && !Double.IsNaN(PupilY);

		/// <summary>
		/// Euclidean length of the leave-one-out residual in screen pixels.
		/// </summary>
		[XmlIgnore]
		public double Error => Math.Sqrt(ResidualX * ResidualX + ResidualY * ResidualY);

		/// <summary>
		/// Copies the fixation.
		/// </summary>
		/// <returns>A fixation with the same values.</returns>
		public CalibrationFixation Clone()
		{
			return (CalibrationFixation)MemberwiseClone();
		}
	}

	/// <summary>
	/// Powers of one monomial of the polynomial term of the mapping.
	/// </summary>
	public class MonomialPowers
	{
		[XmlAttribute]
		public int PowerX { get; set; } = 0;

		[XmlAttribute]
		public int PowerY { get; set; } = 0;
	}

	/// <summary>
	/// Everything that defines a fitted pupil-to-screen mapping and the settings that produced its fixations,
	/// serialized to XML so a calibration can be saved and refitted later.
	/// </summary>
	[XmlRoot("GazeCalibration")]
	public class GazeCalibrationParameters
	{
		// == Timing used to aggregate the fixations ==
		public bool UseTimestampForStart { get; set; } = false;
		public int CalibrationStartFrame { get; set; } = 0;
		public string CalibrationStartTimeStamp { get; set; } = "0:00:00.000";
		public double CalibrationDelay { get; set; } = 2.0;
		public double CalibrationDuration { get; set; } = 2.0;
		public double PointDelay { get; set; } = 1 / 6.0;
		public double EndSkip { get; set; } = 0;
		public int EyetrackingFPS { get; set; } = 60;
		public int VideoWidth { get; set; } = 0;
		public int VideoHeight { get; set; } = 0;

		// == Preprocessing ==
		public double BlinkConfidenceThreshold { get; set; } = 0.6;
		public bool FilterEnabled { get; set; } = true;
		public int FilterWindowSize { get; set; } = 15;
		public bool FilterPupilSize { get; set; } = true;
		public bool EnableOutlierRemoval { get; set; } = false;
		public double OutlierThresholdX { get; set; } = 95;
		public double OutlierThresholdY { get; set; } = 95;
		public double OutlierThresholdRadius { get; set; } = 95;
		public bool UseVarianceThreshold { get; set; } = false;
		public double VarianceThresholdPercentile { get; set; } = 90;

		// == Fitted mapping ==
		public bool UseLegacyConvention { get; set; } = true;
		public RBFKernel Kernel { get; set; } = RBFKernel.ThinPlate;
		public double Smoothness { get; set; } = 0;
		public double Epsilon { get; set; } = 1;
		public double RMSError { get; set; } = Double.NaN;

		[XmlArray("Fixations")]
		[XmlArrayItem("Fixation")]
		public List<CalibrationFixation> Fixations { get; set; } = new List<CalibrationFixation>();

		[XmlArray("CoefficientsX")]
		[XmlArrayItem("double")]
		public List<double> CoefficientsX { get; set; } = new List<double>();

		[XmlArray("CoefficientsY")]
		[XmlArrayItem("double")]
		public List<double> CoefficientsY { get; set; } = new List<double>();

		[XmlArray("PolynomialPowers")]
		[XmlArrayItem("Monomial")]
		public List<MonomialPowers> PolynomialPowers { get; set; } = new List<MonomialPowers>();

		public double ShiftX { get; set; } = 0;
		public double ShiftY { get; set; } = 0;
		public double ScaleX { get; set; } = 1;
		public double ScaleY { get; set; } = 1;

		/// <summary>
		/// Writes the parameters to an XML file.
		/// </summary>
		/// <param name="path">Destination file path.</param>
		public void Save(string path)
		{
			using FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
			XmlSerializer serializer = new XmlSerializer(typeof(GazeCalibrationParameters));
			serializer.Serialize(fileStream, this);
		}

		/// <summary>
		/// Reads parameters from an XML file written by <see cref="Save"/>.
		/// </summary>
		/// <param name="path">Source file path.</param>
		/// <returns>The deserialized parameters.</returns>
		public static GazeCalibrationParameters Load(string path)
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
			XmlSerializer serializer = new XmlSerializer(typeof(GazeCalibrationParameters));
			return (GazeCalibrationParameters)serializer.Deserialize(fileStream)!;
		}
	}
}
