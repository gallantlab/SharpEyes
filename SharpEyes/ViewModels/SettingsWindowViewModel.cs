using System.Collections.ObjectModel;
using ReactiveUI;

namespace SharpEyes.ViewModels
{
	public class SettingsWindowViewModel : ViewModelBase
	{
		public ObservableCollection<string> Categories { get; } =
			new ObservableCollection<string> { "General", "Python" };

		public int SelectedCategoryIndex
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0;
	}
}
