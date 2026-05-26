using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using NumSharp;

namespace Eyetracking
{
	/// <summary>
	/// Parses gaze data from Eyelink EDF binary files or converted text files.
	/// Returns an NDArray of shape (N, 2) with gaze X and Y pixel coordinates.
	/// Missing or invalid samples are NaN.
	/// </summary>
	public static class EyelinkParser
	{
		private const string EDF_API_DLL = "edfapi";
		private const float EyelinkNaN = 1e8f;

		public static bool IsEDFSupported { get; } = CheckEDFLibraryAvailable();

		private static bool CheckEDFLibraryAvailable()
		{
			try
			{
				return NativeLibrary.TryLoad(EDF_API_DLL, out IntPtr _);
			}
			catch
			{
				return false;
			}
		}

		private const int SAMPLE_TYPE      = 200;
		private const int RECORDING_INFO   = 30;
		private const int NO_PENDING_ITEMS = 0;
		private const int SAMPLE_LEFT      = 0x8000;
		private const int SAMPLE_RIGHT     = 0x4000;
		private const int SAMPLE_GAZEXY    = 0x0400;
		private const int EDF_LOAD_EVENTS  = 1;
		private const int EDF_LOAD_SAMPLE  = 1;

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct FSAMPLE
		{
			public uint Time;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Px;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Py;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Hx;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Hy;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Pa;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Gx;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Gy;
			public float Rx;
			public float Ry;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Gxvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Gyvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Hxvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Hyvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Rxvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Ryvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Fgxvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Fgyvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Fhxvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Fhyvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Frxvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] Fryvel;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
			public short[] Hdata;
			public ushort Flags;
			public ushort Input;
			public ushort Buttons;
			public short  Htype;
			public ushort Errors;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct RECORDINGS
		{
			public uint   Time;
			public float  SampleRate;
			public ushort Eflags;
			public ushort Sflags;
			public byte   State;
			public byte   RecordType;
			public byte   PupilType;
			public byte   RecordingMode;
			public byte   FilterType;
			public byte   PosType;
			public byte   Eye;
		}

		[DllImport(EDF_API_DLL, CallingConvention = CallingConvention.StdCall)]
		private static extern IntPtr edf_open_file(
			[MarshalAs(UnmanagedType.LPStr)] string fileName,
			int consistency,
			int loadEvents,
			int loadSamples,
			out int errorValue);

		[DllImport(EDF_API_DLL, CallingConvention = CallingConvention.StdCall)]
		private static extern int edf_close_file(IntPtr edfFile);

		[DllImport(EDF_API_DLL, CallingConvention = CallingConvention.StdCall)]
		private static extern int edf_get_next_data(IntPtr edfFile);

		[DllImport(EDF_API_DLL, CallingConvention = CallingConvention.StdCall)]
		private static extern IntPtr edf_get_float_data(IntPtr edfFile);

		[DllImport(EDF_API_DLL, CallingConvention = CallingConvention.StdCall)]
		private static extern int edf_get_trial_count(IntPtr edfFile);

		/// <summary>
		/// Parses an Eyelink EDF binary file directly using the edfapi library.
		/// Returns an NDArray of shape (N, 2) with gaze X and Y pixel coordinates.
		/// sampleRate is set to the recording sample rate in Hz, or 0 if not found.
		/// </summary>
		public static NDArray ParseEDFFile(string filePath, out int sampleRate)
		{
			IntPtr edfFile = edf_open_file(filePath, 2, EDF_LOAD_EVENTS, EDF_LOAD_SAMPLE, out int errorValue);
			if (edfFile == IntPtr.Zero)
				throw new IOException(String.Format("Failed to open EDF file: {0} (error {1})", filePath, errorValue));

			try
			{
				edf_get_trial_count(edfFile);

				List<double> gazeXList = new List<double>();
				List<double> gazeYList = new List<double>();
				RECORDINGS? currentRecording = null;
				sampleRate = 0;

				while (true)
				{
					int dataType = edf_get_next_data(edfFile);
					if (dataType == NO_PENDING_ITEMS)
						break;

					if (dataType == RECORDING_INFO)
					{
						IntPtr dataPtr = edf_get_float_data(edfFile);
						RECORDINGS rec = Marshal.PtrToStructure<RECORDINGS>(dataPtr);
						currentRecording = rec.State == 0 ? (RECORDINGS?)null : rec;
						if (rec.State != 0 && sampleRate == 0)
							sampleRate = (int)Math.Round(rec.SampleRate);
						continue;
					}

					if (dataType != SAMPLE_TYPE)
					{
						edf_get_float_data(edfFile);
						continue;
					}

					IntPtr samplePtr = edf_get_float_data(edfFile);
					FSAMPLE sample = Marshal.PtrToStructure<FSAMPLE>(samplePtr);

					if (currentRecording == null || (sample.Flags & SAMPLE_GAZEXY) == 0)
						continue;

					RECORDINGS recording = currentRecording.Value;
					bool hasLeft  = (recording.Eye == 1 || recording.Eye == 3) && (sample.Flags & SAMPLE_LEFT)  != 0;
					bool hasRight = (recording.Eye == 2 || recording.Eye == 3) && (sample.Flags & SAMPLE_RIGHT) != 0;

					// prefer right eye over left, matching the behavior of the Python parser
					float gazeX = EyelinkNaN;
					float gazeY = EyelinkNaN;
					if (hasLeft)  { gazeX = sample.Gx[0]; gazeY = sample.Gy[0]; }
					if (hasRight) { gazeX = sample.Gx[1]; gazeY = sample.Gy[1]; }

					gazeXList.Add(gazeX == EyelinkNaN ? double.NaN : gazeX);
					gazeYList.Add(gazeY == EyelinkNaN ? double.NaN : gazeY);
				}

				return BuildGazeArray(gazeXList, gazeYList);
			}
			finally
			{
				edf_close_file(edfFile);
			}
		}

		/// <summary>
		/// Parses an Eyelink text file (converted from EDF via edf2text or EDFParser).
		/// Returns an NDArray of shape (N, 2) with gaze X and Y pixel coordinates.
		/// sampleRate is set to the recording sample rate in Hz, or 0 if not found.
		/// </summary>
		public static NDArray ParseTextFile(string filePath, out int sampleRate)
		{
			List<double> gazeXList = new List<double>();
			List<double> gazeYList = new List<double>();

			bool recordingHasLeft  = false;
			bool recordingHasRight = false;
			sampleRate = 0;

			using (StreamReader fileHandle = new StreamReader(filePath, System.Text.Encoding.UTF8))
			{
				string rawLine;
				while ((rawLine = fileHandle.ReadLine()) != null)
				{
					string line = rawLine.TrimEnd('\r', '\n');
					if (line.Length == 0)
						continue;

					if (line.StartsWith("**") || line.StartsWith("START") || line.StartsWith("PRESCALER")
						|| line.StartsWith("VPRESCALER") || line.StartsWith("PUPIL")
						|| line.StartsWith("EVENTS\t") || line.StartsWith("END\t"))
						continue;

					if (line.StartsWith("SAMPLES"))
					{
						string[] headerParts = line.Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
						recordingHasLeft  = Array.IndexOf(headerParts, "LEFT")  >= 0;
						recordingHasRight = Array.IndexOf(headerParts, "RIGHT") >= 0;
						int rateIndex = Array.IndexOf(headerParts, "RATE");
						if (rateIndex >= 0 && rateIndex + 1 < headerParts.Length && sampleRate == 0)
						{
							double parsedRate;
							if (double.TryParse(headerParts[rateIndex + 1], NumberStyles.Any,
								CultureInfo.InvariantCulture, out parsedRate))
								sampleRate = (int)Math.Round(parsedRate);
						}
						continue;
					}

					if (line.StartsWith("MSG")    || line.StartsWith("SBLINK") || line.StartsWith("SSACC")
						|| line.StartsWith("SFIX") || line.StartsWith("EBLINK") || line.StartsWith("ESACC")
						|| line.StartsWith("EFIX") || line.StartsWith("INPUT")  || line.StartsWith("BUTTON"))
						continue;

					if (!char.IsDigit(line[0]))
						continue;

					string[] parts = line.Split(new char[]{'\t', ' '}, StringSplitOptions.RemoveEmptyEntries);
					int columnIndex = 1;
					double x = double.NaN;
					double y = double.NaN;

					if (recordingHasLeft)
					{
						x = columnIndex     < parts.Length ? ParseNumericField(parts[columnIndex])     : double.NaN;
						y = columnIndex + 1 < parts.Length ? ParseNumericField(parts[columnIndex + 1]) : double.NaN;
						columnIndex += 3;
					}
					// right eye overwrites left when both are present, matching the Python parser
					if (recordingHasRight)
					{
						x = columnIndex     < parts.Length ? ParseNumericField(parts[columnIndex])     : double.NaN;
						y = columnIndex + 1 < parts.Length ? ParseNumericField(parts[columnIndex + 1]) : double.NaN;
					}

					gazeXList.Add(x);
					gazeYList.Add(y);
				}
			}

			return BuildGazeArray(gazeXList, gazeYList);
		}

		private static double ParseNumericField(string fieldString)
		{
			string stripped = fieldString.Trim();
			if (stripped == ".")
				return double.NaN;
			double result;
			return double.TryParse(stripped, NumberStyles.Any, CultureInfo.InvariantCulture, out result)
				? result
				: double.NaN;
		}

		private static NDArray BuildGazeArray(List<double> gazeXList, List<double> gazeYList)
		{
			int count = gazeXList.Count;
			NDArray gazeLocations = new NDArray(NPTypeCode.Double, Shape.Matrix(count, 2));
			for (int i = 0; i < count; i++)
			{
				gazeLocations[i, 0] = gazeXList[i];
				gazeLocations[i, 1] = gazeYList[i];
			}
			return gazeLocations;
		}
	}
}
