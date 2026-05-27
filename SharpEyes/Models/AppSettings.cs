using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace SharpEyes.Models
{
	public enum PythonSourceMode { System, Conda, Bundled }

	[XmlRoot("AppSettings")]
	public class AppSettings
	{
		public PythonSourceMode PythonSourceMode { get; set; } = PythonSourceMode.Bundled;
		public string SystemPythonExecutablePath { get; set; } = String.Empty;
		public string CondaEnvironmentPath { get; set; } = String.Empty;

		public int MotionEnergyFrameHeight { get; set; } = 270;
		public int MotionEnergyFrameWidth { get; set; } = 480;

		[XmlArray("MotionEnergySpatialFrequencies")]
		[XmlArrayItem("double")]
		public List<double> MotionEnergySpatialFrequencies { get; set; } = new List<double> { 0, 2, 4, 8, 16, 32 };

		[XmlArray("MotionEnergyTemporalFrequencies")]
		[XmlArrayItem("double")]
		public List<double> MotionEnergyTemporalFrequencies { get; set; } = new List<double> { 0, 2, 4, 8, 16 };

		[XmlArray("MotionEnergyDirections")]
		[XmlArrayItem("double")]
		public List<double> MotionEnergyDirections { get; set; } = new List<double> { 0, 45, 90, 135, 180, 225, 270, 315 };

		public static string SettingsFilePath =>
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"SharpEyes", "settings.xml");

		public static AppSettings Load()
		{
			string filePath = SettingsFilePath;
			if (!File.Exists(filePath))
				return new AppSettings();
			try
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
				return (AppSettings)serializer.Deserialize(fileStream)!;
			}
			catch
			{
				return new AppSettings();
			}
		}

		public void Save()
		{
			string filePath = SettingsFilePath;
			Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
			using FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
			XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
			serializer.Serialize(fileStream, this);
		}
	}
}
