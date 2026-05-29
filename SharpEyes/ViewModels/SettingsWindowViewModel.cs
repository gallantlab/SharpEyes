using System.Collections.ObjectModel;
using ReactiveUI;

namespace SharpEyes.ViewModels
{
	public class SettingsWindowViewModel : ViewModelBase
	{
		public ObservableCollection<string> Categories { get; } =
			new ObservableCollection<string> { "General", "Python" };

		private int _selectedCategoryIndex = 0;
		public int SelectedCategoryIndex
		{
			get => _selectedCategoryIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedCategoryIndex, value);
		}
	}
}
