using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	public class PythonSettingsViewModel : ViewModelBase
	{
		private readonly PythonEnvironmentManager _manager = PythonEnvironmentManager.Instance;

		// == Source mode ==

		private int _selectedSourceModeIndex;
		public int SelectedSourceModeIndex
		{
			get => _selectedSourceModeIndex;
			set
			{
				this.RaiseAndSetIfChanged(ref _selectedSourceModeIndex, value);
				this.RaisePropertyChanged("IsSystemModeSelected");
				this.RaisePropertyChanged("IsCondaModeSelected");
				this.RaisePropertyChanged("IsBundledModeSelected");
				_manager.Settings.PythonSourceMode = (PythonSourceMode)value;
				CheckRestartRequired();
			}
		}

		public bool IsSystemModeSelected => _selectedSourceModeIndex == 0;
		public bool IsCondaModeSelected => _selectedSourceModeIndex == 1;
		public bool IsBundledModeSelected => _selectedSourceModeIndex == 2;

		// == System Python ==

		private string _systemPythonExecutablePath = String.Empty;
		public string SystemPythonExecutablePath
		{
			get => _systemPythonExecutablePath;
			set
			{
				this.RaiseAndSetIfChanged(ref _systemPythonExecutablePath, value);
				_manager.Settings.SystemPythonExecutablePath = value;
				CheckRestartRequired();
			}
		}

		public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
		public ReactiveCommand<Unit, Unit> SetupSystemPythonCommand { get; }

		// == Conda ==

		private bool _isCondaAvailable = false;
		public bool IsCondaAvailable
		{
			get => _isCondaAvailable;
			private set => this.RaiseAndSetIfChanged(ref _isCondaAvailable, value);
		}

		public ObservableCollection<CondaEnvironmentInfo> CondaEnvironments { get; } =
			new ObservableCollection<CondaEnvironmentInfo>();

		private int _selectedCondaEnvironmentIndex = -1;
		public int SelectedCondaEnvironmentIndex
		{
			get => _selectedCondaEnvironmentIndex;
			set
			{
				this.RaiseAndSetIfChanged(ref _selectedCondaEnvironmentIndex, value);
				if (value >= 0 && value < CondaEnvironments.Count)
				{
					_manager.Settings.CondaEnvironmentPath = CondaEnvironments[value].Path;
					CheckRestartRequired();
				}
			}
		}

		private string _newCondaEnvironmentName = String.Empty;
		public string NewCondaEnvironmentName
		{
			get => _newCondaEnvironmentName;
			set => this.RaiseAndSetIfChanged(ref _newCondaEnvironmentName, value);
		}

		public ReactiveCommand<Unit, Unit> RefreshCondaEnvironmentsCommand { get; }
		public ReactiveCommand<Unit, Unit> CreateCondaEnvironmentCommand { get; }
		public ReactiveCommand<Unit, Unit> InstallPymotenIntoCondaCommand { get; }

		// == Bundled Python ==

		public ReactiveCommand<Unit, Unit> DownloadBundledPythonCommand { get; }

		private double _downloadProgress = 0;
		public double DownloadProgress
		{
			get => _downloadProgress;
			set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
		}

		private bool _isDownloadProgressVisible = false;
		public bool IsDownloadProgressVisible
		{
			get => _isDownloadProgressVisible;
			set => this.RaiseAndSetIfChanged(ref _isDownloadProgressVisible, value);
		}

		// == Shared status ==

		private string _statusText = String.Empty;
		public string StatusText
		{
			get => _statusText;
			set => this.RaiseAndSetIfChanged(ref _statusText, value);
		}

		// == Dependency status ==

		private string _numPyStatusText = "unknown";
		public string NumPyStatusText
		{
			get => _numPyStatusText;
			set => this.RaiseAndSetIfChanged(ref _numPyStatusText, value);
		}

		private string _pillowStatusText = "unknown";
		public string PillowStatusText
		{
			get => _pillowStatusText;
			set => this.RaiseAndSetIfChanged(ref _pillowStatusText, value);
		}

		private string _motenStatusText = "unknown";
		public string MotenStatusText
		{
			get => _motenStatusText;
			set => this.RaiseAndSetIfChanged(ref _motenStatusText, value);
		}

		public ReactiveCommand<Unit, Unit> CheckDependenciesCommand { get; }

		// == Restart warning ==

		private bool _restartRequiredVisible = false;
		public bool RestartRequiredVisible
		{
			get => _restartRequiredVisible;
			set => this.RaiseAndSetIfChanged(ref _restartRequiredVisible, value);
		}

		public PythonSettingsViewModel()
		{
			AppSettings settings = _manager.Settings;
			_selectedSourceModeIndex = (int)settings.PythonSourceMode;
			_systemPythonExecutablePath = settings.SystemPythonExecutablePath;

			BrowseCommand = ReactiveCommand.CreateFromTask(Browse);
			SetupSystemPythonCommand = ReactiveCommand.CreateFromTask(SetupSystemPython);
			RefreshCondaEnvironmentsCommand = ReactiveCommand.Create(RefreshCondaEnvironments);
			CreateCondaEnvironmentCommand = ReactiveCommand.CreateFromTask(CreateCondaEnvironment);
			InstallPymotenIntoCondaCommand = ReactiveCommand.CreateFromTask(InstallPymotenIntoConda);
			DownloadBundledPythonCommand = ReactiveCommand.CreateFromTask(DownloadBundledPython);
			CheckDependenciesCommand = ReactiveCommand.Create(CheckDependencies);

			IsCondaAvailable = PythonEnvironmentManager.DetectConda() != null;
			if (IsCondaAvailable)
				RefreshCondaEnvironments();

			// Pre-select the saved conda environment if present
			if (!String.IsNullOrEmpty(settings.CondaEnvironmentPath))
			{
				for (int index = 0; index < CondaEnvironments.Count; index++)
				{
					if (CondaEnvironments[index].Path == settings.CondaEnvironmentPath)
					{
						_selectedCondaEnvironmentIndex = index;
						break;
					}
				}
			}
		}

		private async Task Browse()
		{
			OpenFileDialog dialog = new OpenFileDialog { Title = "Select Python executable" };
			if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
				System.Runtime.InteropServices.OSPlatform.Windows))
			{
				dialog.Filters.Add(new FileDialogFilter
				{
					Name = "Executable",
					Extensions = { "exe" }
				});
			}
			string[] result = await dialog.ShowAsync(MainWindow);
			if (result != null && result.Length > 0)
				SystemPythonExecutablePath = result[0];
		}

		private async Task SetupSystemPython()
		{
			IProgress<string> statusProgress = new Progress<string>(text => StatusText = text);
			await _manager.SetupSystemPython(statusProgress);
			CheckDependencies();
			_manager.SaveSettings();
		}

		private void RefreshCondaEnvironments()
		{
			CondaEnvironments.Clear();
			foreach (CondaEnvironmentInfo environment in PythonEnvironmentManager.GetCondaEnvironments())
				CondaEnvironments.Add(environment);
		}

		private async Task CreateCondaEnvironment()
		{
			if (String.IsNullOrWhiteSpace(NewCondaEnvironmentName)) return;
			string createdName = NewCondaEnvironmentName;
			IProgress<string> statusProgress = new Progress<string>(text => StatusText = text);
			await _manager.CreateCondaEnvironment(createdName, statusProgress);
			RefreshCondaEnvironments();
			for (int index = 0; index < CondaEnvironments.Count; index++)
			{
				if (CondaEnvironments[index].Name == createdName)
				{
					SelectedCondaEnvironmentIndex = index;
					break;
				}
			}
			CheckDependencies();
			_manager.SaveSettings();
		}

		private async Task InstallPymotenIntoConda()
		{
			IProgress<string> statusProgress = new Progress<string>(text => StatusText = text);
			await _manager.InstallPymoten(statusProgress);
			CheckDependencies();
		}

		private async Task DownloadBundledPython()
		{
			IsDownloadProgressVisible = true;
			IProgress<double> downloadProgress = new Progress<double>(value => DownloadProgress = value * 100);
			IProgress<string> statusProgress = new Progress<string>(text => StatusText = text);
			await _manager.DownloadBundledPython(downloadProgress, statusProgress);
			IsDownloadProgressVisible = false;
			CheckDependencies();
			CheckRestartRequired();
			_manager.SaveSettings();
		}

		private void CheckDependencies()
		{
			DependencyCheckResult result = _manager.CheckDependencies();
			NumPyStatusText = result.NumPy == PackageStatus.Installed ? "installed" : "missing";
			PillowStatusText = result.Pillow == PackageStatus.Installed ? "installed" : "missing";
			MotenStatusText = result.Moten == PackageStatus.Installed ? "installed" : "missing";
		}

		private void CheckRestartRequired()
		{
			if (_manager.IsInitialized)
				RestartRequiredVisible = true;
		}
	}
}
