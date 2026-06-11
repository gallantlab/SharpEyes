using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace SharpEyes.Models
{
	public enum PythonSourceMode { System, Conda, Bundled }

	[XmlRoot("Settings")]
	public class Settings
	{
		public PythonSourceMode PythonSourceMode { get; set; } = PythonSourceMode.Bundled;
		public string SystemPythonExecutablePath { get; set; } = String.Empty;
		public string CondaEnvironmentPath { get; set; } = String.Empty;

		[XmlArray("BackendPreference")]
		[XmlArrayItem("string")]
		public List<string> BackendPreference { get; set; } = null;

		public string EyelinkLibraryPath { get; set; } = String.Empty;

		public bool ShowFrameNumber { get; set; } = false;

		public int LastOpenTabIndex { get; set; } = 2;

		public int MotionEnergyFrameHeight { get; set; } = 270;
		public int MotionEnergyFrameWidth { get; set; } = 480;

		public double MotionEnergyPadPercent { get; set; } = 200;
		public double MotionEnergyPadValue { get; set; } = 0.1;
		public double MotionEnergyFrameScale { get; set; } = 0.125;

		[XmlArray("MotionEnergySpatialFrequencies")]
		[XmlArrayItem("double")]
		public List<double> MotionEnergySpatialFrequencies { get; set; } = null;

		[XmlArray("MotionEnergyTemporalFrequencies")]
		[XmlArrayItem("double")]
		public List<double> MotionEnergyTemporalFrequencies { get; set; } = null;

		[XmlArray("MotionEnergyDirections")]
		[XmlArrayItem("double")]
		public List<double> MotionEnergyDirections { get; set; } = null;

		// Filter batching: when enabled, ExtractAsync calls pymoten's
		// project_stimulus_batched, which processes MotionEnergyFilterBatchSize
		// gabor filters at a time. Batching is over filters.
		public bool MotionEnergyUseFilterBatching { get; set; } = false;
		public int MotionEnergyFilterBatchSize { get; set; } = 128;
		
		// Batch over stimulus frames?
		public bool MotionEnergyBatchFrames { get; set; } = true;
		public int MotionEnergyFrameBatchSize { get; set; } = 1000;

		// Precision of pymoten's response accumulators. Lower precision uses
		// less memory. Features are saved to disk at this dtype; the in-memory
		// features used for visualization are float32.
		public string MotionEnergyOutputDtype { get; set; } = "float32";

		// Expander open/close states - CalibrationUserControl
		public bool CalibrationParametersExpanded { get; set; } = true;
		public bool CalibrationPointsExpanded { get; set; } = true;

		// Expander open/close states - MotionEnergyUserControl
		public bool MotionEnergyFrameParametersExpanded { get; set; } = true;
		public bool MotionEnergyPyramidExpanded { get; set; } = true;
		public bool MotionEnergySpatialFrequenciesExpanded { get; set; } = false;
		public bool MotionEnergyTemporalFrequenciesExpanded { get; set; } = false;
		public bool MotionEnergyDirectionsExpanded { get; set; } = false;
		public bool MotionEnergyComputeFeaturesExpanded { get; set; } = true;

		// Expander open/close states - PupilFindingUserControl
		public bool PupilSizeExpanded { get; set; } = false;
		public bool ConfidenceOptionsExpanded { get; set; } = false;
		public bool TimestampParsingExpanded { get; set; } = false;
		public bool ImagePreFilteringExpanded { get; set; } = false;
		public bool ManualAdjustOptionsExpanded { get; set; } = false;

		// Expander open/close states - RecenteringUserControl
		public bool RecenteringGazeInfoExpanded { get; set; } = true;
		public bool RecenteringExportExpanded { get; set; } = true;

		// Expander open/close states - TemplatePupilFinderConfigUserControl
		public bool TemplatesExpanded { get; set; } = false;
		public bool AntiTemplatesExpanded { get; set; } = false;
		public bool MatchingOptionsExpanded { get; set; } = false;

		// Expander open/close states - StimulusGazeUserControl
		public bool StimulusTemporalAlignmentExpanded { get; set; } = true;
		public bool StimulusGazeInfoExpanded { get; set; } = true;
		public bool StimulusGazeFilteringExpanded { get; set; } = true;
		public bool StimulusKeyframesExpanded { get; set; } = true;

		// Gaze filter options - StimulusGazeUserControl
		public bool GazeFilterEnabled { get; set; } = false;
		public int GazeFilterWindowSize { get; set; } = 15;
		public bool GazeFilterPupilSize { get; set; } = true;
		public bool GazeFilterEnableOutlierRemoval { get; set; } = false;
		public double GazeFilterOutlierThresholdX { get; set; } = 95;
		public double GazeFilterOutlierThresholdY { get; set; } = 95;
		public double GazeFilterOutlierThresholdRadius { get; set; } = 95;

		public static Settings Current { get; private set; }

		public static string SettingsFilePath =>
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"SharpEyes", "settings.xml");

		public static Settings Load()
		{
			string filePath = SettingsFilePath;
			Settings result;
			if (!File.Exists(filePath))
				result = new Settings();
			else
			{
				try
				{
					using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
					XmlSerializer serializer = new XmlSerializer(typeof(Settings));
					result = (Settings)serializer.Deserialize(fileStream)!;
				}
				catch
				{
					result = new Settings();
				}
			}
			if (result.MotionEnergySpatialFrequencies == null)
				result.MotionEnergySpatialFrequencies = new List<double> { 0, 2, 4, 8, 16, 32 };
			if (result.MotionEnergyTemporalFrequencies == null)
				result.MotionEnergyTemporalFrequencies = new List<double> { 0, 2, 4, 8, 16 };
			if (result.MotionEnergyDirections == null)
				result.MotionEnergyDirections = new List<double> { 0, 45, 90, 135, 180, 225, 270, 315 };
			if (result.BackendPreference == null)
				result.BackendPreference = new List<string> { "torch_cuda", "torch_mps", "torch", "numpy" };
			Current = result;
			return result;
		}

		public void Save()
		{
			string filePath = SettingsFilePath;
			Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
			using FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
			XmlSerializer serializer = new XmlSerializer(typeof(Settings));
			serializer.Serialize(fileStream, this);
		}
	}
}
