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

		// == Filter batching ==

		// Batch process filter repsonses?
		public bool BatchFilters { get; set; } = false;
		public int FilterBatchSize { get; set; } = 128;

		// Frame batch size, if null, process all in one go
		public int? FrameBatchSize { get; set; } = null;

		// Precision of pymoten's response accumulators (e.g. "float16",
		// "float32", "float64"). Lower precision uses less memory. Features are
		// saved to disk at this dtype; C# reads them back as float32 for
		// visualization.
		public string OutputDtype { get; set; } = "float32";

		// When true, only each batch's frames are copied to the GPU rather than
		// moving the full stimulus tensor up front.
		public bool FramesInCPU { get; set; } = false;
		// When true, per-batch filter responses are copied back to CPU immediately
		// rather than accumulating the full (nimages x nfilters) tensor on the GPU.
		public bool ResponsesInCPU { get; set; } = false;

		// == Pyramid state (runtime, not persisted) ==

		private PyObject? _pyramidObject = null;

		// Most recently computed features retained at the compute dtype so they
		// can be saved to disk at that dtype. C# reads the features back as
		// float32 separately for visualization. Released on the next ExtractAsync.
		private PyObject? _lastFeatures = null;

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
		public async Task ExtractAsync(NDArray frames, IProgress<double> progress)
		{
			int nFrames = frames.Shape[0];
			int height = frames.Shape[1];
			int width = frames.Shape[2];

			await Task.Run(() =>
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

					try
					{
						// Batching or not batching we call the same method just to make logic easier
						dynamic projection = ((dynamic)_pyramidObject).project_stimulus_batched(npArray, batch_size: new PyInt(BatchFilters ? FilterBatchSize : 1),
																								dtype: new PyString(OutputDtype),
																								stimulus_batch_size: FrameBatchSize.HasValue? new PyInt(FrameBatchSize.Value) : null,
																								frames_in_cpu: FramesInCPU.ToPython(),
																								responses_in_cpu: ResponsesInCPU.ToPython());
						// if the responses were not in CPU we have to explicitly moved it back
						PyObject computedFeatures = ResponsesInCPU ? projection : projection.cpu().numpy();
						_lastFeatures?.Dispose();
						_lastFeatures = computedFeatures;
					}
					finally
					{
						// Free Python and GPU memory even when the projection throws
						// (e.g. CUDA out-of-memory) so a failed run does not leave VRAM
						// allocated.
						dynamic gc = Py.Import("gc");
						gc.collect();
						if (Backend == "torch_cuda")
						{
							dynamic torch = Py.Import("torch");
							torch.cuda.empty_cache();
						}
					}
				}
				progress.Report(1.0);
			});
		}

		/// <summary>
		/// Saves the most recently computed motion-energy features to a NumPy .npy
		/// file at the compute dtype they were produced in. Does nothing if no
		/// features have been computed. Must be called after ExtractAsync.
		/// </summary>
		/// <param name="path">Destination .npy file path.</param>
		public void SaveFeatures(string path)
		{
			if (_lastFeatures == null) return;
			using (Py.GIL())
			{
				dynamic np = Py.Import("numpy");
				np.save(path, _lastFeatures);
			}
		}

		/// <summary>
		/// Loads a previously saved motion-energy feature array from a NumPy .npy file
		/// and returns it as a float32 NDArray for visualization. The file is loaded and
		/// cast through Python (numpy) rather than NumSharp so that arrays saved at a
		/// lower compute dtype (e.g. float16) read back correctly. Must be called after
		/// PythonEnvironmentManager.Initialize().
		/// </summary>
		/// <param name="path">Source .npy file path.</param>
		/// <returns>An (nFrames x nFeatures) float32 array.</returns>
		public NDArray LoadFeatures(string path)
		{
			float[] resultData;
			int nFrames;
			int nFeatures;
			using (Py.GIL())
			{
				dynamic np = Py.Import("numpy");
				dynamic ctypes = Py.Import("ctypes");
				dynamic npCtypeslib = Py.Import("numpy.ctypeslib");

				dynamic loaded = np.load(path);
				// Ascontiguousarray guarantees the reshape(-1) below is a contiguous view
				dynamic result = np.ascontiguousarray(loaded.astype(np.float32));
				nFrames = (int)result.shape[0];
				nFeatures = (int)result.shape[1];

				resultData = new float[nFrames * nFeatures];
				GCHandle outputHandle = GCHandle.Alloc(resultData, GCHandleType.Pinned);
				try
				{
					long outputPtr = outputHandle.AddrOfPinnedObject().ToInt64();
					dynamic outputCArrayType = ctypes.c_float * resultData.Length;
					dynamic outputCArray = outputCArrayType.from_address(outputPtr);
					dynamic outputNpArray = npCtypeslib.as_array(outputCArray);
					np.copyto(outputNpArray, result.reshape(-1));
				}
				finally
				{
					outputHandle.Free();
				}
			}
			return new NDArray(resultData).reshape(nFrames, nFeatures);
		}

		/// <summary>
		/// Sets entire rows of the most recently computed feature array to zero or NaN
		/// for the specified frame indices, modifying the retained Python array in place
		/// using NumPy fancy indexing. Must be called after ExtractAsync and before
		/// SaveFeatures.
		/// </summary>
		/// <param name="frameIndices">Zero-based row indices into the feature array for frames to fill.</param>
		/// <param name="fillWithNaN">True to fill each row with NaN; false to fill with zero.</param>
		public void FillMissingFrames(List<int> frameIndices, bool fillWithNaN)
		{
			if (_lastFeatures == null || frameIndices.Count == 0) return;
			using (Py.GIL())
			{
				dynamic np = Py.Import("numpy");
				dynamic features = (dynamic)_lastFeatures;
				dynamic indices = np.array(frameIndices.ToArray());
				if (fillWithNaN)
					features[indices] = np.nan;
				else
					features[indices] = new PyInt(0);
			}
		}

		public NDArray? filterResponses
		{
			get
			{
				if (_lastFeatures == null) return null;
				using (Py.GIL())
				{
					dynamic motenBackend = Py.Import("moten.backend");
					motenBackend.set_backend(Backend);

					dynamic np = Py.Import("numpy");
					dynamic ctypes = Py.Import("ctypes");
					dynamic npCtypeslib = Py.Import("numpy.ctypeslib");
					// C# reads the features back as float32 for visualization only.
					dynamic result = ((dynamic)_lastFeatures).astype(np.float32);
					int nFeatures = (int)result.shape[1];
					int nFrames = (int)result.shape[0];

					// Pin the C# float array and copy numpy result directly into it,
					// avoiding the intermediate Python bytes object and cross-boundary marshal
					var resultData = new float[nFrames * nFeatures];
					GCHandle outputHandle = GCHandle.Alloc(resultData, GCHandleType.Pinned);
					try
					{
						long outputPtr = outputHandle.AddrOfPinnedObject().ToInt64();
						dynamic outputCArrayType = ctypes.c_float * resultData.Length;
						dynamic outputCArray = outputCArrayType.from_address(outputPtr);
						dynamic outputNpArray = npCtypeslib.as_array(outputCArray);
						np.copyto(outputNpArray, result.reshape(-1));
					}
					finally
					{
						outputHandle.Free();
					}
					return new NDArray(resultData).reshape(nFrames, nFeatures);
				}
			}
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
