using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	public class PymotenBackend
	{
		public string Key { get; }
		public string DisplayName { get; }

		public PymotenBackend(string key)
		{
			Key = key;
			DisplayName = key switch
			{
				"torch"      => "torch (CPU)",
				"torch_cuda" => "torch (CUDA)",
				"torch_mps"  => "torch (MPS)",
				_            => key
			};
		}
	}

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
				_manager.SaveSettings();
				_ = Task.Run(CheckDependencies);
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
					_manager.SaveSettings();
					_ = Task.Run(CheckDependencies);
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

		private string _torchStatusText = "unknown";
		public string TorchStatusText
		{
			get => _torchStatusText;
			set => this.RaiseAndSetIfChanged(ref _torchStatusText, value);
		}

		public ReactiveCommand<Unit, Unit> CheckDependenciesCommand { get; }
		public ReactiveCommand<Unit, Unit> InstallMissingPackagesCommand { get; }

		// == Backend preference ==

		private DependencyCheckResult _lastCheckResult = new DependencyCheckResult();

		public ObservableCollection<PymotenBackend> AvailableBackends { get; } = new ObservableCollection<PymotenBackend>();

		private int _selectedBackendIndex = -1;
		public int SelectedBackendIndex
		{
			get => _selectedBackendIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedBackendIndex, value);
		}

		public ReactiveCommand<Unit, Unit> ProbeBackendsCommand { get; }
		public ReactiveCommand<Unit, Unit> MoveBackendUpCommand { get; }
		public ReactiveCommand<Unit, Unit> MoveBackendDownCommand { get; }

		public PythonSettingsViewModel()
		{
			Settings settings = _manager.Settings;
			_selectedSourceModeIndex = (int)settings.PythonSourceMode;
			_systemPythonExecutablePath = settings.SystemPythonExecutablePath;

			BrowseCommand = ReactiveCommand.CreateFromTask(Browse);
			SetupSystemPythonCommand = ReactiveCommand.CreateFromTask(SetupSystemPython);
			RefreshCondaEnvironmentsCommand = ReactiveCommand.Create(RefreshCondaEnvironments);
			CreateCondaEnvironmentCommand = ReactiveCommand.CreateFromTask(CreateCondaEnvironment);
			InstallPymotenIntoCondaCommand = ReactiveCommand.CreateFromTask(InstallPymotenIntoConda);
			DownloadBundledPythonCommand = ReactiveCommand.CreateFromTask(DownloadBundledPython);
			CheckDependenciesCommand = ReactiveCommand.Create(CheckDependencies);
			InstallMissingPackagesCommand = ReactiveCommand.CreateFromTask(InstallMissingPackages);
			ProbeBackendsCommand = ReactiveCommand.Create(ProbeBackends);
			MoveBackendUpCommand = ReactiveCommand.Create(MoveBackendUp);
			MoveBackendDownCommand = ReactiveCommand.Create(MoveBackendDown);

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

			_ = Task.Run(CheckDependencies);
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
			_manager.SaveSettings();
		}

		private void CheckDependencies()
		{
			DependencyCheckResult result = _manager.CheckDependencies();
			_lastCheckResult = result;
			NumPyStatusText = result.NumPy == PackageStatus.Installed ? "installed" : "missing";
			PillowStatusText = result.Pillow == PackageStatus.Installed ? "installed" : "missing";
			MotenStatusText = result.Moten == PackageStatus.Installed ? "installed" : "missing";
			TorchStatusText = result.Torch == PackageStatus.Installed ? "installed" : "missing";
			ProbeBackends();
		}

		private void ProbeBackends()
		{
			List<string> probeResult = _manager.ProbeAvailableBackends();
			List<string> savedPreference = _manager.Settings.BackendPreference;

			List<string> orderedResult = new List<string>();
			foreach (string backend in savedPreference)
				if (probeResult.Contains(backend))
					orderedResult.Add(backend);
			foreach (string backend in probeResult)
				if (!orderedResult.Contains(backend))
					orderedResult.Add(backend);

			Dispatcher.UIThread.Post(() =>
			{
				AvailableBackends.Clear();
				foreach (string backend in orderedResult)
					AvailableBackends.Add(new PymotenBackend(backend));
			});
		}

		private void MoveBackendUp()
		{
			int index = SelectedBackendIndex;
			if (index <= 0 || index >= AvailableBackends.Count) return;
			AvailableBackends.Move(index, index - 1);
			SelectedBackendIndex = index - 1;
			List<string> keyList = new List<string>();
			foreach (PymotenBackend item in AvailableBackends)
				keyList.Add(item.Key);
			_manager.Settings.BackendPreference = keyList;
			_manager.SaveSettings();
		}

		private void MoveBackendDown()
		{
			int index = SelectedBackendIndex;
			if (index < 0 || index >= AvailableBackends.Count - 1) return;
			AvailableBackends.Move(index, index + 1);
			SelectedBackendIndex = index + 1;
			List<string> keyList = new List<string>();
			foreach (PymotenBackend item in AvailableBackends)
				keyList.Add(item.Key);
			_manager.Settings.BackendPreference = keyList;
			_manager.SaveSettings();
		}

		private async Task InstallMissingPackages()
		{
			IProgress<string> statusProgress = new Progress<string>(text => StatusText = text);
			await _manager.InstallMissingPackages(_lastCheckResult, statusProgress);
			CheckDependencies();
		}

	}
}
