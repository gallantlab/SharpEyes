using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SharpEyes.Converters
{
	/// <summary>
	/// Converts an output data type name into the foreground brush used to display it in the
	/// compute data type dropdown. The half precision "float16" entry is shown in orange to
	/// warn that it is prone to floating point overflows; every other entry falls back to the
	/// inherited theme foreground.
	/// </summary>
	public class OutputDtypeForegroundConverter : IValueConverter
	{
		/// <summary>
		/// Returns the orange warning brush when the data type name is "float16" and the unset
		/// value otherwise so that the displayed text keeps its inherited theme foreground.
		/// </summary>
		/// <param name="value">The data type name being displayed, expected to be a string.</param>
		/// <param name="targetType">The type the binding target expects, which is a brush.</param>
		/// <param name="parameter">The converter parameter, which is not used.</param>
		/// <param name="culture">The culture to use for the conversion, which is not used.</param>
		/// <returns>An orange brush for "float16"; otherwise the unset value.</returns>
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is string dataTypeName && dataTypeName == "float16")
				return new SolidColorBrush(Color.FromRgb(255, 165, 0));
			return AvaloniaProperty.UnsetValue;
		}

		/// <summary>
		/// Not supported; the foreground brush is never converted back to a data type name.
		/// </summary>
		/// <param name="value">The value produced by the binding target, which is not used.</param>
		/// <param name="targetType">The type to convert back to, which is not used.</param>
		/// <param name="parameter">The converter parameter, which is not used.</param>
		/// <param name="culture">The culture to use for the conversion, which is not used.</param>
		/// <returns>This method always throws.</returns>
		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>
	/// Converts an output data type name into the warning tooltip shown for it in the compute
	/// data type dropdown. The half precision "float16" entry warns that it is prone to
	/// floating point overflows; every other entry has no tooltip.
	/// </summary>
	public class OutputDtypeWarningConverter : IValueConverter
	{
		/// <summary>
		/// Returns the floating point overflow warning when the data type name is "float16" and
		/// null otherwise so that no tooltip is shown for the other data types.
		/// </summary>
		/// <param name="value">The data type name being displayed, expected to be a string.</param>
		/// <param name="targetType">The type the binding target expects, which is a string.</param>
		/// <param name="parameter">The converter parameter, which is not used.</param>
		/// <param name="culture">The culture to use for the conversion, which is not used.</param>
		/// <returns>The overflow warning text for "float16"; otherwise null.</returns>
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is string dataTypeName && dataTypeName == "float16")
				return "Half precision is prone to floating point overflows.";
			return null;
		}

		/// <summary>
		/// Not supported; the tooltip text is never converted back to a data type name.
		/// </summary>
		/// <param name="value">The value produced by the binding target, which is not used.</param>
		/// <param name="targetType">The type to convert back to, which is not used.</param>
		/// <param name="parameter">The converter parameter, which is not used.</param>
		/// <param name="culture">The culture to use for the conversion, which is not used.</param>
		/// <returns>This method always throws.</returns>
		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
