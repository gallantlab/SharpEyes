using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	public class ViewModelBase : ReactiveObject
	{
		// == window reference. needed for showing dialogs ==
		public Window? MainWindow =>
			Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
				? desktop.MainWindow
				: null;

		// == Progress bar ==
		private string _statusText = "Idle";
		public string StatusText
		{
			get => _statusText;
			set => this.RaiseAndSetIfChanged(ref _statusText, value);
		}

		private bool _isProgressBarVisible = false;
		public bool IsProgressBarVisible
		{
			get => _isProgressBarVisible;
			set => this.RaiseAndSetIfChanged(ref _isProgressBarVisible, value);
		}

		private bool _isProgressBarIndeterminate = false;
		public bool IsProgressBarIndeterminate
		{
			get => _isProgressBarIndeterminate;
			set => this.RaiseAndSetIfChanged(ref _isProgressBarIndeterminate, value);
		}

		private double _progressBarValue = 0;
		public double ProgressBarValue
		{
			get => _progressBarValue;
			set => this.RaiseAndSetIfChanged(ref _progressBarValue, value);
		}

		// == Video dimensions ==
		// Backing fields are protected so subclasses can read them in their layout
		// computations; the setters are virtual so subclasses can raise their own
		// derived layout properties when the size changes.
		protected int _videoWidth = 1024;
		public virtual int VideoWidth
		{
			get => _videoWidth;
			set => this.RaiseAndSetIfChanged(ref _videoWidth, value);
		}

		protected int _videoHeight = 768;
		public virtual int VideoHeight
		{
			get => _videoHeight;
			set => this.RaiseAndSetIfChanged(ref _videoHeight, value);
		}

		/// <summary>
		/// Initializes the embedded Python interpreter if it has not been initialized, showing progress in the status bar.
		/// </summary>
		/// <returns>True when the interpreter is initialized.</returns>
		public async Task<bool> InitializePython()
		{
			if (PythonEnvironmentManager.Instance.IsInitialized) return true;
			IsProgressBarVisible = true;
			try
			{
				return await PythonEnvironmentManager.Instance.EnsureInitialized(message => StatusText = message);
			}
			finally
			{
				IsProgressBarVisible = false;
			}
		}
	}
}
