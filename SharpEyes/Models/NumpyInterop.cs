using System;
using System.Linq;
using System.Runtime.InteropServices;
using Python.Runtime;

namespace SharpEyes.Models
{
	/// <summary>
	/// Copies arrays between managed memory and numpy through pinned ctypes views, so that no element-wise
	/// marshalling crosses the interop boundary. Every method must be called with the GIL held.
	/// </summary>
	public static class NumpyInterop
	{
		/// <summary>
		/// Copies a managed array into a new numpy array of the given shape.
		/// </summary>
		/// <param name="source">Managed array of a blittable element type to copy from.</param>
		/// <param name="ctypesTypeName">Name of the ctypes type matching the element type, such as c_uint8 or c_double.</param>
		/// <param name="shape">Shape of the numpy array; its element count must equal the source length.</param>
		/// <returns>A numpy array owning a copy of the data.</returns>
		public static dynamic CopyToNumpy(Array source, string ctypesTypeName, params int[] shape)
		{
			dynamic ctypes = Py.Import("ctypes");
			dynamic npCtypeslib = Py.Import("numpy.ctypeslib");
			if (source.Length == 0)
			{
				dynamic np = Py.Import("numpy");
				return np.zeros(ToPyTuple(shape));
			}
			GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
			try
			{
				long pointer = handle.AddrOfPinnedObject().ToInt64();
				dynamic elementType = ((PyObject)ctypes).GetAttr(ctypesTypeName);
				dynamic cArrayType = elementType * source.Length;
				dynamic cArray = cArrayType.from_address(pointer);
				dynamic copy = npCtypeslib.as_array(cArray).copy();
				return copy.reshape(ToPyTuple(shape));
			}
			finally
			{
				handle.Free();
			}
		}

		/// <summary>
		/// Copies the values of a numpy array, cast to the destination element type, into a managed array in row-major order.
		/// </summary>
		/// <param name="numpyArray">Numpy array whose element count equals the destination length.</param>
		/// <param name="destination">Managed array of a blittable element type to fill.</param>
		/// <param name="ctypesTypeName">Name of the ctypes type matching the destination element type.</param>
		/// <param name="numpyDtypeName">Name of the numpy dtype matching the destination element type, such as float32.</param>
		public static void CopyToManaged(dynamic numpyArray, Array destination, string ctypesTypeName, string numpyDtypeName)
		{
			if (destination.Length == 0) return;
			dynamic np = Py.Import("numpy");
			dynamic ctypes = Py.Import("ctypes");
			dynamic npCtypeslib = Py.Import("numpy.ctypeslib");
			dynamic dtype = ((PyObject)np).GetAttr(numpyDtypeName);
			// Ascontiguousarray guarantees the reshape(-1) below is a contiguous view
			dynamic flattened = np.ascontiguousarray(numpyArray.astype(dtype)).reshape(-1);
			GCHandle handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
			try
			{
				long pointer = handle.AddrOfPinnedObject().ToInt64();
				dynamic elementType = ((PyObject)ctypes).GetAttr(ctypesTypeName);
				dynamic cArrayType = elementType * destination.Length;
				dynamic cArray = cArrayType.from_address(pointer);
				dynamic view = npCtypeslib.as_array(cArray);
				np.copyto(view, flattened);
			}
			finally
			{
				handle.Free();
			}
		}

		/// <summary>
		/// Converts a shape into a Python tuple of integers.
		/// </summary>
		/// <param name="shape">Dimensions of the shape.</param>
		/// <returns>A Python tuple holding the dimensions.</returns>
		private static PyTuple ToPyTuple(int[] shape)
		{
			return new PyTuple(shape.Select(dimension => (PyObject)new PyInt(dimension)).ToArray());
		}
	}
}
