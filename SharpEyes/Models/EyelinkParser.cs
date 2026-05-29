using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NumSharp;
using SharpEyes.Models;

namespace Eyetracking
{
	/// <summary>
	/// Parses gaze data from Eyelink EDF binary files or converted text files.
	/// Returns an NDArray of shape (N, 7) matching the Python EyelinkParser output:
	/// gazeX, gazeY, pupilSize, isTTL, isSaccade, isFixation, eyelinkTimestamp.
	/// Missing or invalid gaze samples are NaN.
	/// </summary>
	public static class EyelinkParser
	{
		private const string EDF_API_DLL = "edfapi";
		private const float EyelinkNaN = 1e8f;

		public static bool IsEDFSupported { get; private set; }

		static EyelinkParser()
		{
			NativeLibrary.SetDllImportResolver(
				typeof(EyelinkParser).Assembly,
				(libraryName, assembly, searchPath) =>
				{
					if (libraryName != EDF_API_DLL)
						return IntPtr.Zero;
					string settingsPath = Settings.Current?.EyelinkLibraryPath ?? String.Empty;
					if (!String.IsNullOrEmpty(settingsPath))
					{
						IntPtr handle;
						if (NativeLibrary.TryLoad(settingsPath, out handle))
							return handle;
					}
					IntPtr defaultHandle;
					NativeLibrary.TryLoad(libraryName, assembly, searchPath, out defaultHandle);
					return defaultHandle;
				});
			IsEDFSupported = CheckEDFLibraryAvailable();
		}

		private static bool CheckEDFLibraryAvailable()
		{
			string settingsPath = Settings.Current?.EyelinkLibraryPath ?? String.Empty;
			if (!String.IsNullOrEmpty(settingsPath))
			{
				try { return NativeLibrary.TryLoad(settingsPath, out IntPtr _); }
				catch { return false; }
			}
			try { return NativeLibrary.TryLoad(EDF_API_DLL, out IntPtr _); }
			catch { return false; }
		}

		private const int ENDSACC = 6;
		private const int ENDFIX = 8;
		private const int MESSAGEEVENT = 24;
		private const int SAMPLE_TYPE = 200;
		private const int RECORDING_INFO = 30;
		private const int NO_PENDING_ITEMS = 0;

		private const int SAMPLE_LEFT = 0x8000;
		private const int SAMPLE_RIGHT = 0x4000;
		private const int SAMPLE_GAZEXY = 0x0400;
		private const int EDF_LOAD_EVENTS = 1;
		private const int EDF_LOAD_SAMPLE = 1;

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct FEVENT
		{
			public uint Time;
			public short Type;
			public ushort Read;
			public uint StartTime;
			public uint EndTime;
			public float Hstx;
			public float Hsty;
			public float Gstx;
			public float Gsty;
			public float Sta;
			public float Henx;
			public float Heny;
			public float Genx;
			public float Geny;
			public float Ena;
			public float Havx;
			public float Havy;
			public float Gavx;
			public float Gavy;
			public float Ava;
			public float Avel;
			public float Pvel;
			public float Svel;
			public float Evel;
			public float SupdX;
			public float EupdX;
			public float SupdY;
			public float EupdY;
			public short Eye;
			public ushort Status;
			public ushort Flags;
			public ushort Input;
			public ushort Buttons;
			public ushort Parsedby;
			public IntPtr MessagePtr;
		}

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
			public short Htype;
			public ushort Errors;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct RECORDINGS
		{
			public uint Time;
			public float SampleRate;
			public ushort Eflags;
			public ushort Sflags;
			public byte State;
			public byte RecordType;
			public byte PupilType;
			public byte RecordingMode;
			public byte FilterType;
			public byte PosType;
			public byte Eye;
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
		/// Returns an NDArray of shape (N, 7): gazeX, gazeY, pupilSize, isTTL, isSaccade, isFixation, eyelinkTimestamp.
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
				List<double> pupilSizeList = new List<double>();
				List<uint> timestampList = new List<uint>();
				List<uint> ttlTimestampList = new List<uint>();
				List<(uint startTime, uint endTime)> saccadeRanges = new List<(uint, uint)>();
				List<(uint startTime, uint endTime)> fixationRanges = new List<(uint, uint)>();

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

					if (dataType == ENDSACC || dataType == ENDFIX || dataType == MESSAGEEVENT)
					{
						IntPtr dataPtr = edf_get_float_data(edfFile);
						FEVENT fevent = Marshal.PtrToStructure<FEVENT>(dataPtr);
						if (dataType == ENDSACC)
							saccadeRanges.Add((fevent.StartTime, fevent.EndTime));
						else if (dataType == ENDFIX)
							fixationRanges.Add((fevent.StartTime, fevent.EndTime));
						else
						{
							string messageText = ReadLString(fevent.MessagePtr);
							string[] messageParts = messageText.Split(new char[]{' ', '\t'},
								StringSplitOptions.RemoveEmptyEntries);
							if (messageParts.Length >= 1 && messageParts[0] == "TTL")
								ttlTimestampList.Add(fevent.StartTime);
						}
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
					bool hasLeft = (recording.Eye == 1 || recording.Eye == 3) && (sample.Flags & SAMPLE_LEFT) != 0;
					bool hasRight = (recording.Eye == 2 || recording.Eye == 3) && (sample.Flags & SAMPLE_RIGHT) != 0;

					// prefer right eye over left, matching the Python parser
					float gazeX = EyelinkNaN;
					float gazeY = EyelinkNaN;
					float pupilSize = EyelinkNaN;
					if (hasLeft) { gazeX = sample.Gx[0]; gazeY = sample.Gy[0]; pupilSize = sample.Pa[0]; }
					if (hasRight) { gazeX = sample.Gx[1]; gazeY = sample.Gy[1]; pupilSize = sample.Pa[1]; }

					gazeXList.Add(gazeX == EyelinkNaN ? double.NaN : gazeX);
					gazeYList.Add(gazeY == EyelinkNaN ? double.NaN : gazeY);
					pupilSizeList.Add(pupilSize == EyelinkNaN ? double.NaN : pupilSize);
					timestampList.Add(sample.Time);
				}

				return BuildDataArray(gazeXList, gazeYList, pupilSizeList, timestampList,
					ttlTimestampList, saccadeRanges, fixationRanges);
			}
			finally
			{
				edf_close_file(edfFile);
			}
		}

		/// <summary>
		/// Parses an Eyelink text file (converted from EDF via edf2text or EDFParser).
		/// Returns an NDArray of shape (N, 7): gazeX, gazeY, pupilSize, isTTL, isSaccade, isFixation, eyelinkTimestamp.
		/// sampleRate is set to the recording sample rate in Hz, or 0 if not found.
		/// </summary>
		public static NDArray ParseTextFile(string filePath, out int sampleRate)
		{
			bool hasEyelinkHeader = false;
			using (StreamReader headerReader = new StreamReader(filePath, Encoding.UTF8))
			{
				string headerLine;
				while ((headerLine = headerReader.ReadLine()) != null)
				{
					if (!headerLine.StartsWith("**"))
						break;
					if (headerLine.StartsWith("** TYPE: EDF_FILE"))
					{
						hasEyelinkHeader = true;
						break;
					}
				}
			}

			if (!hasEyelinkHeader)
				throw new InvalidDataException("File does not appear to be an EyeLink text file.");

			List<double> gazeXList = new List<double>();
			List<double> gazeYList = new List<double>();
			List<double> pupilSizeList = new List<double>();
			List<uint> timestampList = new List<uint>();
			List<uint> ttlTimestampList = new List<uint>();
			List<(uint startTime, uint endTime)> saccadeRanges = new List<(uint, uint)>();
			List<(uint startTime, uint endTime)> fixationRanges = new List<(uint, uint)>();

			bool recordingHasLeft = false;
			bool recordingHasRight = false;
			sampleRate = 0;

			using (StreamReader fileHandle = new StreamReader(filePath, Encoding.UTF8))
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
						recordingHasLeft = Array.IndexOf(headerParts, "LEFT") >= 0;
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

					if (line.StartsWith("MSG"))
					{
						string[] parts = line.Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
						if (parts.Length >= 4 && parts[2] == "TTL")
						{
							uint ttlTime;
							if (uint.TryParse(parts[1], out ttlTime))
								ttlTimestampList.Add(ttlTime);
						}
						continue;
					}

					if (line.StartsWith("ESACC"))
					{
						string[] parts = line.Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
						if (parts.Length >= 4)
						{
							uint startTime, endTime;
							if (uint.TryParse(parts[2], out startTime) && uint.TryParse(parts[3], out endTime))
								saccadeRanges.Add((startTime, endTime));
						}
						continue;
					}

					if (line.StartsWith("EFIX"))
					{
						string[] parts = line.Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
						if (parts.Length >= 4)
						{
							uint startTime, endTime;
							if (uint.TryParse(parts[2], out startTime) && uint.TryParse(parts[3], out endTime))
								fixationRanges.Add((startTime, endTime));
						}
						continue;
					}

					if (line.StartsWith("SBLINK") || line.StartsWith("SSACC") || line.StartsWith("SFIX")
						|| line.StartsWith("EBLINK") || line.StartsWith("INPUT") || line.StartsWith("BUTTON"))
						continue;

					if (!char.IsDigit(line[0]))
						continue;

					string[] sampleParts = line.Split(new char[]{'\t', ' '}, StringSplitOptions.RemoveEmptyEntries);
					int columnIndex = 1;
					double x = double.NaN;
					double y = double.NaN;
					double pupilSize = double.NaN;

					if (recordingHasLeft)
					{
						x = columnIndex < sampleParts.Length ? ParseNumericField(sampleParts[columnIndex]) : double.NaN;
						y = columnIndex + 1 < sampleParts.Length ? ParseNumericField(sampleParts[columnIndex + 1]) : double.NaN;
						pupilSize = columnIndex + 2 < sampleParts.Length ? ParseNumericField(sampleParts[columnIndex + 2]) : double.NaN;
						columnIndex += 3;
					}
					// right eye overwrites left when both are present, matching the Python parser
					if (recordingHasRight)
					{
						x = columnIndex < sampleParts.Length ? ParseNumericField(sampleParts[columnIndex]) : double.NaN;
						y = columnIndex + 1 < sampleParts.Length ? ParseNumericField(sampleParts[columnIndex + 1]) : double.NaN;
						pupilSize = columnIndex + 2 < sampleParts.Length ? ParseNumericField(sampleParts[columnIndex + 2]) : double.NaN;
					}

					uint timestamp;
					if (!uint.TryParse(sampleParts[0], out timestamp))
						continue;

					gazeXList.Add(x);
					gazeYList.Add(y);
					pupilSizeList.Add(pupilSize);
					timestampList.Add(timestamp);
				}
			}

			return BuildDataArray(gazeXList, gazeYList, pupilSizeList, timestampList,
				ttlTimestampList, saccadeRanges, fixationRanges);
		}

		private static string ReadLString(IntPtr ptr)
		{
			if (ptr == IntPtr.Zero) return string.Empty;
			short length = Marshal.ReadInt16(ptr);
			if (length <= 0) return string.Empty;
			byte[] bytes = new byte[length];
			Marshal.Copy(ptr + 2, bytes, 0, length);
			int end = Array.IndexOf(bytes, (byte)0);
			if (end < 0) end = length;
			while (end > 0 && (bytes[end - 1] == '\n' || bytes[end - 1] == '\r' || bytes[end - 1] == 0))
				end--;
			return Encoding.Latin1.GetString(bytes, 0, end);
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

		private static NDArray BuildDataArray(
			List<double> gazeXList, List<double> gazeYList, List<double> pupilSizeList,
			List<uint> timestampList, List<uint> ttlTimestampList,
			List<(uint startTime, uint endTime)> saccadeRanges,
			List<(uint startTime, uint endTime)> fixationRanges)
		{
			int count = timestampList.Count;
			uint[] timestamps = timestampList.ToArray();

			double[] isTTL = new double[count];
			double[] isSaccade = new double[count];
			double[] isFixation = new double[count];

			foreach (uint ttlTime in ttlTimestampList)
				isTTL[FindNearestIndex(timestamps, ttlTime)] = 1.0;

			foreach ((uint startTime, uint endTime) range in saccadeRanges)
			{
				int startIndex = Array.BinarySearch(timestamps, range.startTime);
				if (startIndex < 0) startIndex = ~startIndex;
				for (int i = startIndex; i < count && timestamps[i] <= range.endTime; i++)
					isSaccade[i] = 1.0;
			}

			foreach ((uint startTime, uint endTime) range in fixationRanges)
			{
				int startIndex = Array.BinarySearch(timestamps, range.startTime);
				if (startIndex < 0) startIndex = ~startIndex;
				for (int i = startIndex; i < count && timestamps[i] <= range.endTime; i++)
					isFixation[i] = 1.0;
			}

			NDArray data = new NDArray(NPTypeCode.Double, Shape.Matrix(count, 7));
			for (int i = 0; i < count; i++)
			{
				data[i, 0] = gazeXList[i];
				data[i, 1] = gazeYList[i];
				data[i, 2] = pupilSizeList[i];
				data[i, 3] = isTTL[i];
				data[i, 4] = isSaccade[i];
				data[i, 5] = isFixation[i];
				data[i, 6] = (double)timestampList[i];
			}
			return data;
		}

		private static int FindNearestIndex(uint[] sortedTimestamps, uint targetTime)
		{
			int index = Array.BinarySearch(sortedTimestamps, targetTime);
			if (index < 0)
				index = Math.Clamp(~index, 0, sortedTimestamps.Length - 1);
			return index;
		}
	}
}
