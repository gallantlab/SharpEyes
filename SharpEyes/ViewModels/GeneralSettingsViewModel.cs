using System;
using System.Reactive;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	public class GeneralSettingsViewModel : ViewModelBase
	{
		private readonly Action _refreshActiveTab;

		public bool ShowFrameNumber
		{
			get => Settings.Current.ShowFrameNumber;
			set
			{
				Settings.Current.ShowFrameNumber = value;
				Settings.Current.Save();
				this.RaisePropertyChanged(nameof(ShowFrameNumber));
				_refreshActiveTab();
			}
		}

		public bool IsEyelinkSectionVisible { get; } = !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

		private string _eyelinkLibraryPath;
		public string EyelinkLibraryPath
		{
			get => _eyelinkLibraryPath;
			set
			{
				this.RaiseAndSetIfChanged(ref _eyelinkLibraryPath, value);
				Settings.Current.EyelinkLibraryPath = value;
				Settings.Current.Save();
			}
		}

		public ReactiveCommand<Unit, Unit> BrowseEyelinkLibraryCommand { get; }

		public GeneralSettingsViewModel(Action refreshActiveTab)
		{
			_refreshActiveTab = refreshActiveTab;
			_eyelinkLibraryPath = Settings.Current.EyelinkLibraryPath;
			BrowseEyelinkLibraryCommand = ReactiveCommand.CreateFromTask(async () =>
			{
				OpenFileDialog dialog = new OpenFileDialog { Title = "Select Eyelink library" };
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					dialog.Filters.Add(new FileDialogFilter { Name = "DLL", Extensions = { "dll" } });
				else
					dialog.Filters.Add(new FileDialogFilter { Name = "Shared library", Extensions = { "so" } });
				string[] result = await dialog.ShowAsync(MainWindow);
				if (result != null && result.Length > 0)
					EyelinkLibraryPath = result[0];
			});
		}
	}
}
