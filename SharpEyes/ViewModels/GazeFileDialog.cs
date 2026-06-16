using System.Threading.Tasks;
using Avalonia.Controls;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	/// <summary>
	/// Builds the shared "Load gaze locations" open-file dialog used by the
	/// Stimulus &amp; Gaze and Recentering tabs, so the supported-format filter list
	/// (and the optional EDF entry) is defined in exactly one place.
	/// </summary>
	public static class GazeFileDialog
	{
		/// <summary>
		/// Shows the gaze open-file dialog and returns the chosen path, or null if the
		/// user cancelled.
		/// </summary>
		public static async Task<string?> ShowAsync(Window? owner)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load gaze locations"
			};
			FileDialogFilter allFilesFilter = new FileDialogFilter()
			{
				Name = "All supported files",
				Extensions = { "npy", "csv", "txt", "asc" }
			};
			if (GazeLoader.IsEDFSupported)
				allFilesFilter.Extensions.Add("edf");
			openFileDialog.Filters.Add(allFilesFilter);
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Numpy file",
				Extensions = { "npy" }
			});
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Comma-separated values",
				Extensions = { "csv" }
			});
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Eyelink text file",
				Extensions = { "txt", "asc" }
			});
			if (GazeLoader.IsEDFSupported)
				openFileDialog.Filters.Add(new FileDialogFilter()
				{
					Name = "Eyelink EDF file",
					Extensions = { "edf" }
				});

			string[] fileName = await openFileDialog.ShowAsync(owner);
			return (fileName == null || fileName.Length == 0) ? null : fileName[0];
		}
	}
}
