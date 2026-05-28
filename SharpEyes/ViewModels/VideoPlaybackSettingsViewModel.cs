using System;
using ReactiveUI;
using SharpEyes.Models;

namespace SharpEyes.ViewModels
{
	public class VideoPlaybackSettingsViewModel : ViewModelBase
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

		public VideoPlaybackSettingsViewModel(Action refreshActiveTab)
		{
			_refreshActiveTab = refreshActiveTab;
		}
	}
}
