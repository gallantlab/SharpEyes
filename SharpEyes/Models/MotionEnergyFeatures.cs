using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NumSharp;
using Python.Runtime;

namespace SharpEyes.Models
{
	public class MotionEnergyFilterParameters
	{
		public double CenterVertical { get; set; }
		public double CenterHorizontal { get; set; }
		public double Direction { get; set; }
		public double SpatialFrequency { get; set; }
		public double SpatialEnvelope { get; set; }
		public double TemporalFrequency { get; set; }
		public double TemporalEnvelope { get; set; }
		public int FilterTemporalWidth { get; set; }
		public double SpatialPhaseOffset { get; set; }
	}

	public class MotionEnergyFeatures
	{
		// == Parameters (user-adjustable, persisted) ==

		private int _frameHeight = 360;
		public int FrameHeight
		{
			get => _frameHeight;
			set => _frameHeight = value;
		}

		private int _frameWidth = 480;
		public int FrameWidth
		{
			get => _frameWidth;
			set => _frameWidth = value;
		}

		private List<double> _spatialFrequencies = new List<double> { 0, 2, 4, 8, 16, 32 };
		public List<double> SpatialFrequencies
		{
			get => _spatialFrequencies;
			set => _spatialFrequencies = value;
		}

		private List<double> _temporalFrequencies = new List<double> { 0, 2, 4, 8, 16 };
		public List<double> TemporalFrequencies
		{
			get => _temporalFrequencies;
			set => _temporalFrequencies = value;
		}

		private List<double> _directions = new List<double> { 0, 45, 90, 135, 180, 225, 270, 315 };
		public List<double> Directions
		{
			get => _directions;
			set => _directions = value;
		}

		// == Compute backend ==

		public string Backend { get; set; } = "numpy";

		// == Pyramid state (runtime, not persisted) ==

		private PyObject? _pyramidObject = null;

		public int FilterCount { get; private set; } = 0;
		public List<MotionEnergyFilterParameters> FilterParameters { get; private set; } = new List<MotionEnergyFilterParameters>();
		public bool IsPyramidBuilt => _pyramidObject != null;

		// Constructs the moten.MotionEnergyPyramid from current parameters.
		// Must be called after PythonEnvironmentManager.Initialize().
		public void BuildPyramid(double fps)
		{
			using (Py.GIL())
			{
				dynamic moten = Py.Import("moten.pyramids");
				dynamic np = Py.Import("numpy");

				dynamic sfList = np.array(_spatialFrequencies.ToArray());
				dynamic tfList = np.array(_temporalFrequencies.ToArray());
				dynamic dirList = np.array(_directions.ToArray());

				_pyramidObject = moten.MotionEnergyPyramid(
					stimulus_vhsize: new PyTuple(new PyObject[] { new PyInt(FrameHeight), new PyInt(FrameWidth) }),
					stimulus_fps: new PyInt((int)fps),
					spatial_frequencies: sfList,
					temporal_frequencies: tfList,
					spatial_directions: dirList
				);

				dynamic pyramid = _pyramidObject;
				try
				{
					FilterCount = (int)pyramid.nfilters;
					List<MotionEnergyFilterParameters> parameters = new List<MotionEnergyFilterParameters>();
					dynamic filtersList = pyramid.filters;
					for (int filterIndex = 0; filterIndex < FilterCount; filterIndex++)
					{
						dynamic filterDict = filtersList[filterIndex];
						parameters.Add(new MotionEnergyFilterParameters
						{
							CenterVertical       = (double)filterDict["centerv"],
							CenterHorizontal     = (double)filterDict["centerh"],
							Direction            = (double)filterDict["direction"],
							SpatialFrequency     = (double)filterDict["spatial_freq"],
							SpatialEnvelope      = (double)filterDict["spatial_env"],
							TemporalFrequency    = (double)filterDict["temporal_freq"],
							TemporalEnvelope     = (double)filterDict["temporal_env"],
							FilterTemporalWidth  = (int)filterDict["filter_temporal_width"],
							SpatialPhaseOffset   = (double)filterDict["spatial_phase_offset"],
						});
					}
					FilterParameters = parameters;
				}
				catch
				{
					FilterCount = 0;
					FilterParameters = new List<MotionEnergyFilterParameters>();
				}
			}

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
					dynamic motenBackend = Py.Import("moten.backend");
					motenBackend.set_backend(Backend);

					dynamic np = Py.Import("numpy");
					dynamic ctypes = Py.Import("ctypes");
					dynamic npCtypeslib = Py.Import("numpy.ctypeslib");

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
						npArray = npCtypeslib.as_array(cArray).copy()
							.reshape(nFrames, height, width).__truediv__(255.0f);
					}
					finally
					{
						handle.Free();
					}

					dynamic result = ((dynamic)_pyramidObject).project_stimulus(npArray).cpu().numpy(); // this is a float32
					nFeatures = (int)result.shape[1];

					// Convert numpy result back to C#
					byte[] resultBytes = (byte[])result.tobytes();
					resultData = new float[nFrames * nFeatures];
					Buffer.BlockCopy(resultBytes, 0, resultData, 0, resultBytes.Length);

					dynamic gc = Py.Import("gc");
					gc.collect();
					if (Backend == "torch_cuda")
					{
						dynamic torch = Py.Import("torch");
						torch.cuda.empty_cache();
					}
				}

				NDArray output = new NDArray(resultData).reshape(nFrames, nFeatures);
				progress.Report(1.0);
				return output;
			});
		}

		// Copies parameter values from Settings into this model.
		public void LoadFromSettings(Settings settings)
		{
			FrameHeight = settings.MotionEnergyFrameHeight;
			FrameWidth = settings.MotionEnergyFrameWidth;
			SpatialFrequencies = new List<double>(settings.MotionEnergySpatialFrequencies);
			TemporalFrequencies = new List<double>(settings.MotionEnergyTemporalFrequencies);
			Directions = new List<double>(settings.MotionEnergyDirections);
		}

		// Copies parameter values from this model into Settings.
		public void SaveToSettings(Settings settings)
		{
			settings.MotionEnergyFrameHeight = FrameHeight;
			settings.MotionEnergyFrameWidth = FrameWidth;
			settings.MotionEnergySpatialFrequencies = new List<double>(SpatialFrequencies);
			settings.MotionEnergyTemporalFrequencies = new List<double>(TemporalFrequencies);
			settings.MotionEnergyDirections = new List<double>(Directions);
		}
	}
}
