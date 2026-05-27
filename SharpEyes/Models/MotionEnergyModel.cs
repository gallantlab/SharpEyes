using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NumSharp;
using Python.Runtime;

namespace SharpEyes.Models
{
	public class MotionEnergyModel
	{
		// == Parameters (user-adjustable, persisted) ==

		private int _frameHeight = 270;
		public int FrameHeight
		{
			get => _frameHeight;
			set { _frameHeight = value; RebuildRequired = true; }
		}

		private int _frameWidth = 480;
		public int FrameWidth
		{
			get => _frameWidth;
			set { _frameWidth = value; RebuildRequired = true; }
		}

		private double? _fpsOverride = null;
		public double? FpsOverride
		{
			get => _fpsOverride;
			set { _fpsOverride = value; RebuildRequired = true; }
		}

		private List<double> _spatialFrequencies = new List<double> { 0, 2, 4, 8, 16, 32 };
		public List<double> SpatialFrequencies
		{
			get => _spatialFrequencies;
			set { _spatialFrequencies = value; RebuildRequired = true; }
		}

		private List<double> _temporalFrequencies = new List<double> { 0, 2, 4, 8, 16 };
		public List<double> TemporalFrequencies
		{
			get => _temporalFrequencies;
			set { _temporalFrequencies = value; RebuildRequired = true; }
		}

		private List<double> _directions = new List<double> { 0, 45, 90, 135, 180, 225, 270, 315 };
		public List<double> Directions
		{
			get => _directions;
			set { _directions = value; RebuildRequired = true; }
		}

		// == Pyramid state (runtime, not persisted) ==

		private PyObject? _pyramidObject = null;

		public int FilterCount { get; private set; } = 0;
		public int FeatureCount { get; private set; } = 0;
		public bool IsPyramidBuilt => _pyramidObject != null;
		public bool RebuildRequired { get; private set; } = true;

		// Constructs the moten.MotionEnergyPyramid from current parameters.
		// Must be called after PythonEnvironmentManager.Initialize().
		// videoFps is the source video frame rate; FpsOverride takes precedence if set.
		public void BuildPyramid(double videoFps)
		{
			double effectiveFps = FpsOverride ?? videoFps;

			using (Py.GIL())
			{
				dynamic moten = Py.Import("moten.pyramids");
				dynamic np = Py.Import("numpy");

				dynamic sfList = np.array(_spatialFrequencies.ToArray());
				dynamic tfList = np.array(_temporalFrequencies.ToArray());
				dynamic dirList = np.array(_directions.ToArray());

				_pyramidObject = moten.MotionEnergyPyramid(
					vhsize: new PyTuple(new PyObject[] { new PyInt(FrameHeight), new PyInt(FrameWidth) }),
					fps: new PyFloat(effectiveFps),
					sf_cycles_s: sfList,
					tf_Hz: tfList,
					directiondeg: dirList
				);

				dynamic pyramid = _pyramidObject;
				try { FilterCount = (int)pyramid.nfilters; } catch { FilterCount = 0; }
				try { FeatureCount = (int)pyramid.nfeatures; } catch { FeatureCount = 0; }
			}

			RebuildRequired = false;
		}

		// Converts frames (nFrames x height x width, uint8) to motion-energy features
		// (nFrames x nFeatures, float32).
		// Must be called after BuildPyramid(). Frames must match FrameHeight x FrameWidth.
		public async Task<NDArray> ExtractAsync(NDArray frames, IProgress<double> progress)
		{
			int nFrames = frames.Shape[0];
			int height = frames.Shape[1];
			int width = frames.Shape[2];

			return await Task.Run(() =>
			{
				float[] resultData;
				int nFeatures;

				using (Py.GIL())
				{
					dynamic np = Py.Import("numpy");
					dynamic ctypes = Py.Import("ctypes");

					// Pin the C# array and create a numpy view into it (zero-copy),
					// then immediately copy into a new numpy array before releasing the pin
					byte[] rawData = frames.Data<byte>().ToArray();
					GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);
					dynamic npArray;
					try
					{
						long ptr = handle.AddrOfPinnedObject().ToInt64();
						dynamic cArrayType = ctypes.c_uint8 * rawData.Length;
						dynamic cArray = cArrayType.from_address(ptr);
						npArray = np.ctypeslib.as_array(cArray).copy()
							.reshape(nFrames, height, width)
							.astype(np.float32).__truediv__(255.0f);
					}
					finally
					{
						handle.Free();
					}

					dynamic result = ((dynamic)_pyramidObject).project_stimulus(npArray);
					nFeatures = (int)result.shape[1];

					// Convert float32 numpy result back to C# float[]
					byte[] resultBytes = (byte[])result.astype(np.float32).tobytes();
					resultData = new float[nFrames * nFeatures];
					Buffer.BlockCopy(resultBytes, 0, resultData, 0, resultBytes.Length);
				}

				NDArray output = new NDArray(resultData).reshape(nFrames, nFeatures);
				progress.Report(1.0);
				return output;
			});
		}

		// Copies parameter values from AppSettings into this model.
		public void LoadFromSettings(AppSettings settings)
		{
			FrameHeight = settings.MotionEnergyFrameHeight;
			FrameWidth = settings.MotionEnergyFrameWidth;
			FpsOverride = settings.MotionEnergyFpsOverride;
			SpatialFrequencies = new List<double>(settings.MotionEnergySpatialFrequencies);
			TemporalFrequencies = new List<double>(settings.MotionEnergyTemporalFrequencies);
			Directions = new List<double>(settings.MotionEnergyDirections);
		}

		// Copies parameter values from this model into AppSettings.
		public void SaveToSettings(AppSettings settings)
		{
			settings.MotionEnergyFrameHeight = FrameHeight;
			settings.MotionEnergyFrameWidth = FrameWidth;
			settings.MotionEnergyFpsOverride = FpsOverride;
			settings.MotionEnergySpatialFrequencies = new List<double>(SpatialFrequencies);
			settings.MotionEnergyTemporalFrequencies = new List<double>(TemporalFrequencies);
			settings.MotionEnergyDirections = new List<double>(Directions);
		}
	}
}
