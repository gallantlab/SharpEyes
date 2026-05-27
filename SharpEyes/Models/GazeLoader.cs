using System;
using System.Collections.Generic;
using System.IO;
using Eyetracking;
using NumSharp;
using Num = NumSharp.np;

namespace SharpEyes.Models
{
	public static class GazeLoader
	{
		public static bool IsEDFSupported => EyelinkParser.IsEDFSupported;

		public static NDArray Load(string filePath, out int sampleRate)
		{
			sampleRate = -1;
			string extension = Path.GetExtension(filePath);
			if (extension == ".npy")
				return Num.load(filePath);
			else if (extension == ".txt")
				return EyelinkParser.ParseTextFile(filePath, out sampleRate);
			else if (extension == ".edf")
				return EyelinkParser.ParseEDFFile(filePath, out sampleRate);
			else
				return LoadCSV(filePath);
		}

		public static void Save(string filePath, NDArray gazeData)
		{
			Num.save(filePath, gazeData);
		}

		private static NDArray LoadCSV(string filePath)
		{
			using StreamReader csvFile = new StreamReader(filePath);
			string line = csvFile.ReadLine();
			List<double[]> values = new List<double[]>();
			bool isFirstLine = true;
			while (line != null)
			{
				try
				{
					string[] tokens = line.Split(',');
					double x = Double.Parse(tokens[0]);
					double y = Double.Parse(tokens[1]);
					values.Add(new double[]{x, y});
					isFirstLine = false;
					line = csvFile.ReadLine();
				}
				catch (Exception e)
				{	// so if the first line is a header, we throw it away,
					// but if there's a parsing error anywhere else we raise it
					if (!isFirstLine)
						throw;
				}
			}

			NDArray gazeData = new NDArray(NPTypeCode.Double, Shape.Matrix(values.Count, 2));
			for (int i = 0; i < values.Count; i++)
			{
				gazeData[i, 0] = values[i][0];
				gazeData[i, 1] = values[i][1];
			}
			return gazeData;
		}
	}
}
