using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace SharpEyes.Models
{
	/// <summary>
	/// Serializable record of one manual gaze keyframe. Stores both the absolute
	/// corrected gaze position (for display) and the delta from the originally
	/// loaded gaze at that sample (used to reconstruct the edits on load).
	/// </summary>
	public class StimulusKeyFrameRecord
	{
		/// <summary>Video time in frames.</summary>
		public int VideoFrame { get; set; }

		/// <summary>Index into the gaze data array.</summary>
		public int DataIndex { get; set; }

		/// <summary>Human-readable timecode of the video frame.</summary>
		public string VideoTimeStamp { get; set; } = string.Empty;

		/// <summary>Corrected gaze X in screen space.</summary>
		public double GazeX { get; set; }

		/// <summary>Corrected gaze Y in screen space.</summary>
		public double GazeY { get; set; }

		/// <summary>Correction applied at this keyframe relative to the original gaze (X).</summary>
		public double DeltaX { get; set; }

		/// <summary>Correction applied at this keyframe relative to the original gaze (Y).</summary>
		public double DeltaY { get; set; }
	}

	/// <summary>
	/// Serializable set of manual stimulus-gaze edits: the keyframes and the deltas
	/// they apply to the gaze positions, plus the temporal-alignment metadata needed
	/// to reapply them. Written to / read from an XML file so a correction session can
	/// be saved and resumed.
	/// </summary>
	[XmlRoot("StimulusGazeEdits")]
	public class StimulusGazeEdits
	{
		/// <summary>Source gaze file the edits were made against, for reference.</summary>
		public string GazeFileName { get; set; } = string.Empty;

		/// <summary>Source stimulus video the edits were made against, for reference.</summary>
		public string VideoFileName { get; set; } = string.Empty;

		/// <summary>Video frame at which the eyetracking data starts.</summary>
		public int DataStartFrame { get; set; }

		/// <summary>Eyetracking sample rate the edits were made at.</summary>
		public int EyetrackingFPS { get; set; }

		[XmlArray("KeyFrames")]
		[XmlArrayItem("KeyFrame")]
		public List<StimulusKeyFrameRecord> KeyFrames { get; set; } = new List<StimulusKeyFrameRecord>();

		public void Save(string filePath)
		{
			using FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
			XmlSerializer serializer = new XmlSerializer(typeof(StimulusGazeEdits));
			serializer.Serialize(fileStream, this);
		}

		public static StimulusGazeEdits Load(string filePath)
		{
			using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
			XmlSerializer serializer = new XmlSerializer(typeof(StimulusGazeEdits));
			return (StimulusGazeEdits)serializer.Deserialize(fileStream)!;
		}
	}
}
