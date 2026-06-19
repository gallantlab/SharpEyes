using ReactiveUI;

namespace SharpEyes.ViewModels
{
	/// <summary>
	/// One faded circle in the gaze trail drawn behind the current gaze marker.
	/// </summary>
	public class TrailGazePoint : ReactiveObject
	{
		private double _left;
		public double Left
		{
			get => _left;
			set => this.RaiseAndSetIfChanged(ref _left, value);
		}

		private double _top;
		public double Top
		{
			get => _top;
			set => this.RaiseAndSetIfChanged(ref _top, value);
		}

		private double _opacity;
		public double Opacity
		{
			get => _opacity;
			set => this.RaiseAndSetIfChanged(ref _opacity, value);
		}

		private bool _isVisible = false;
		public bool IsVisible
		{
			get => _isVisible;
			set => this.RaiseAndSetIfChanged(ref _isVisible, value);
		}
	}
}
