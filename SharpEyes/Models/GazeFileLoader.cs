using System.Collections.Generic;
using System.IO;
using NumSharp;

namespace Eyetracking
{
	/// <summary>
	/// Loads gaze locations from the file formats the application accepts: NumPy arrays,
	/// Eyelink text/ASC files, Eyelink EDF files, and comma-separated values. Centralizes
	/// the parsing so the recentering and motion-energy load paths read files identically.
	/// </summary>
	public static class GazeFileLoader
	{
		/// <summary>
		/// Loads gaze locations from a file, dispatching on the file extension.
		/// </summary>
		/// <param name="path">Path to the gaze file.</param>
		/// <param name="sampleRate">Set to the parsed sample rate for Eyelink formats that record one, otherwise zero.</param>
		/// <returns>An (nSamples x columns) array of gaze locations.</returns>
		public static NDArray Load(string path, out int sampleRate)
		{
			sampleRate = 0;
			string extension = Path.GetExtension(path).ToLowerInvariant();
			if (extension == ".npy")
				return np.load(path);
			if (extension == ".txt" || extension == ".asc")
				return EyelinkParser.ParseTextFile(path, out sampleRate);
			if (extension == ".edf")
				return EyelinkParser.ParseEDFFile(path, out sampleRate);
			return ParseCsv(path);
		}

		private static NDArray ParseCsv(string path)
		{
			using StreamReader csvFile = new StreamReader(path);
			string line = csvFile.ReadLine();
			List<double[]> values = new List<double[]>();
			bool isFirstLine = true;
			while (line != null)
			{
				try
				{
					string[] tokens = line.Split(',');
					double x = double.Parse(tokens[0]);
					double y = double.Parse(tokens[1]);
					values.Add(new double[] { x, y });
				}
				catch
				{
					// tolerate a header on the first line; re-raise a parse error anywhere else
					if (!isFirstLine)
						throw;
				}
				isFirstLine = false;
				line = csvFile.ReadLine();
			}

			NDArray gazeLocations = new NDArray(NPTypeCode.Double, Shape.Matrix(values.Count, 2));
			for (int i = 0; i < values.Count; i++)
			{
				gazeLocations[i, 0] = values[i][0];
				gazeLocations[i, 1] = values[i][1];
			}
			return gazeLocations;
		}
	}
}
