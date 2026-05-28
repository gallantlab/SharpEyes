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

		public bool ShowFrameNumber { get; set; } = false;

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
