using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Eyetracking;
using OpenCvSharp.Flann;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;

namespace SharpEyes.ViewModels
{
	public class TemplatePupilFinderConfigUserControlViewModel : ViewModelBase
	{
		// =========
		// UI things
		// =========

		// Commands
		public ReactiveCommand<Unit, Unit>? AddCurrentAsTemplateCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? RemoveCurrentTemplateCommand { get; private set; } = null;
		public ReactiveCommand<int, Unit>? ChangeTemplatePreviewIndexCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? LoadTemplatesCommand { get; private set; } = null;
		public ReactiveCommand<Unit, Unit>? ResetTemplatesCommand { get; private set; } = null;
		public ReactiveCommand<Unit, Unit>? AddCurrentAsAntiTemplateCommand { get; } = null;
		public ReactiveCommand<Unit, Unit>? RemoveCurrentAntiTemplateCommand { get; private set; } = null;
		public ReactiveCommand<int, Unit>? ChangeAntiTemplatePreviewIndexCommand { get; } = null;

		// templates section
		public bool AutoAddNewTemplate { get; set; } = true;
		private int _currentTemplateIndex = 0;
		public int CurrentTemplateIndex
		{
			get => _currentTemplateIndex;
			set
			{
				this.RaiseAndSetIfChanged(ref _currentTemplateIndex, value);
				this.RaisePropertyChanged("TemplateIndexText");
			}
		}
		public int TotalTemplateCount => TemplatePupilFinder != null ? TemplatePupilFinder.NumTemplates : 0;
		public string TemplateIndexText => String.Format("{0}/{1}", CurrentTemplateIndex + 1, TotalTemplateCount);
		private Bitmap? templatePreviewImage = null;
		public Bitmap? TemplatePreviewImage
		{
			get => templatePreviewImage;
			set => this.RaiseAndSetIfChanged(ref templatePreviewImage, value);
		}

		// anti-templates section
		public int CurrentAntiTemplateIndex { get; set; } = 0;
		public int TotalAntiTemplateCount => TemplatePupilFinder != null ? TemplatePupilFinder.NumAntiTemplates : 0;
		public string AntiTemplateIndexText => String.Format("{0}/{1}", CurrentAntiTemplateIndex + 1, TotalAntiTemplateCount);
		private Bitmap? antiTemplatePreviewImage = null;
		public Bitmap? AntiTemplatePreviewImage
		{
			get => antiTemplatePreviewImage;
			set => this.RaiseAndSetIfChanged(ref antiTemplatePreviewImage, value);
		}

		// match options
		public int SelectedMetricIndex { get; set; } = 3;
		/// <summary>
		/// Use all templates? If false, use only recent templates
		/// </summary>
		public bool UseAllTemplates { get; set; } = true;
		public int NumTemplatesToUse { get; set; } = 128;
		/// <summary>
		/// Use every template? If false, use a random subset of templates
		/// </summary>
		public bool UseEveryTemplate { get; set; } = true;
		public int FractionOfTemplatesToUse { get; set; } = 75;
		public int NumTemplatesToMatch { get; set; } = 2;


		// ============
		// Logic stuffs
		// ============

		public TemplatePupilFinder? TemplatePupilFinder
		{
			get
			{
				if (Parent.pupilFinder is TemplatePupilFinder templateFinder)
					return templateFinder;
				return null;
			}
		}

		// view model hierarchy
		public PupilFindingUserControlViewModel? Parent { get; private set; } = null;
		
		private bool _isTemplatesExpanded = SharpEyes.Models.Settings.Current.TemplatesExpanded;
		public bool IsTemplatesExpanded
		{
			get => _isTemplatesExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isTemplatesExpanded, value);
				SharpEyes.Models.Settings.Current.TemplatesExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		}

		private bool _isAntiTemplatesExpanded = SharpEyes.Models.Settings.Current.AntiTemplatesExpanded;
		public bool IsAntiTemplatesExpanded
		{
			get => _isAntiTemplatesExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isAntiTemplatesExpanded, value);
				SharpEyes.Models.Settings.Current.AntiTemplatesExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		}

		private bool _isMatchingOptionsExpanded = SharpEyes.Models.Settings.Current.MatchingOptionsExpanded;
		public bool IsMatchingOptionsExpanded
		{
			get => _isMatchingOptionsExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isMatchingOptionsExpanded, value);
				SharpEyes.Models.Settings.Current.MatchingOptionsExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		}

		public TemplatePupilFinderConfigUserControlViewModel()
			:this(null)
		{
		}

		public TemplatePupilFinderConfigUserControlViewModel(PupilFindingUserControlViewModel parent)
		{
			AddCurrentAsTemplateCommand = ReactiveCommand.Create(AddCurrentAsTemplate);
			RemoveCurrentTemplateCommand = ReactiveCommand.Create(RemoveCurrentTemplate);
			ChangeTemplatePreviewIndexCommand = ReactiveCommand.Create<int>(ChangeTemplatePreviewIndex);
			ResetTemplatesCommand = ReactiveCommand.Create(ResetTemplates);
			LoadTemplatesCommand = ReactiveCommand.Create(LoadTemplates);
			AddCurrentAsAntiTemplateCommand = ReactiveCommand.Create(AddCurrentAsAntiTemplate);
			RemoveCurrentAntiTemplateCommand = ReactiveCommand.Create(RemoveCurrentAntiTemplate);
			ChangeAntiTemplatePreviewIndexCommand = ReactiveCommand.Create<int>(ChangeAntiTemplatePreviewIndex);
			this.Parent = parent;
		}

		// Command implementations

		public void AddCurrentAsTemplate()
		{
			if (TemplatePupilFinder != null)
			{
				int top = (int)(Parent.PupilY - Parent.PupilRadius * 1.5);
				int bottom = (int)(Parent.PupilY + Parent.PupilRadius * 1.5 + 2);
				int left = (int)(Parent.PupilX - Parent.PupilRadius * 1.5);
				int right = (int)(Parent.PupilX + Parent.PupilRadius * 1.5 + 2);
				TemplatePupilFinder.AddImageSegmentAsTemplate(top, bottom, left, right, Parent.PupilRadius);
				SetTemplatePreviewIndex(TemplatePupilFinder.NumTemplates);

				/* TODO: enabling buttons
				if (!saveTemplatesMenuItem.IsEnabled)
				{
					saveTemplatesMenuItem.IsEnabled = true;
				*/
				ResetTemplatesCommand ??= ReactiveCommand.Create(ResetTemplates);


				if (TemplatePupilFinder.NumTemplates > 1 && RemoveCurrentTemplateCommand == null)
					RemoveCurrentTemplateCommand = ReactiveCommand.Create(RemoveCurrentTemplate);

				Parent.IsDataDirty = true;
			}
		}

		public void RemoveCurrentTemplate()
		{
			if (TemplatePupilFinder != null)
			{
				TemplatePupilFinder.RemoveTemplate(CurrentTemplateIndex);
				SetTemplatePreviewIndex(CurrentTemplateIndex - 1);
				if (TemplatePupilFinder.NumTemplates < 2)
					RemoveCurrentTemplateCommand = null;
				Parent.IsDataDirty = true;
			}
		}

		/// <summary>
		/// Changes the template displayed
		/// </summary>
		/// <param name="delta">-1 to show previous, +1 to show next</param>
		public void ChangeTemplatePreviewIndex(int delta)
		{
			SetTemplatePreviewIndex(CurrentTemplateIndex + delta);
		}

		public async void LoadTemplates()
		{
			if (TemplatePupilFinder == null)
				return;

			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load saved templates"
			};
			openFileDialog.Filters.Add(new FileDialogFilter()
			{
				Name = "Data file",
				Extensions = { "dat" }
			});
			string[] fileName = await openFileDialog.ShowAsync(Parent.MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;

			TemplatePupilFinder.LoadTemplates(fileName[0]);
			SetTemplatePreviewIndex(0);
			SetAntiTemplatePreviewIndex(0);
		}

		public void ResetTemplates()
		{
			if (TemplatePupilFinder != null)
			{
				TemplatePupilFinder.MakeTemplates();
				SetTemplatePreviewIndex(0);
				ResetTemplatesCommand = null;
				RemoveCurrentTemplateCommand = null;
				RemoveCurrentAntiTemplateCommand = null;
			}
		}

		public void AddCurrentAsAntiTemplate()
		{
			if (TemplatePupilFinder != null)
			{
				int top = (int)(Parent.PupilY - Parent.PupilRadius * 1.5);
				int bottom = (int)(Parent.PupilY + Parent.PupilRadius * 1.5 + 2);
				int left = (int)(Parent.PupilX - Parent.PupilRadius * 1.5);
				int right = (int)(Parent.PupilX + Parent.PupilRadius * 1.5 + 2);
				TemplatePupilFinder.AddImageSegmentAsAntiTemplate(top, bottom, left, right);
				SetAntiTemplatePreviewIndex(TemplatePupilFinder.NumAntiTemplates);

				/* TODO: enabling buttons
				if (!saveTemplatesMenuItem.IsEnabled)
				{
					saveTemplatesMenuItem.IsEnabled = true;
				*/
				ResetTemplatesCommand ??= ReactiveCommand.Create(ResetTemplates);

				if (TemplatePupilFinder.NumAntiTemplates > 0 && RemoveCurrentAntiTemplateCommand == null)
					RemoveCurrentAntiTemplateCommand = ReactiveCommand.Create(RemoveCurrentAntiTemplate);

				Parent.IsDataDirty = true;
			}
		}

		public void RemoveCurrentAntiTemplate()
		{
			if (TemplatePupilFinder != null)
			{
				TemplatePupilFinder.RemoveAntiTemplate(CurrentAntiTemplateIndex);
				SetTemplatePreviewIndex(CurrentAntiTemplateIndex - 1);
				if (TemplatePupilFinder.NumAntiTemplates < 1)
					RemoveCurrentAntiTemplateCommand = null;
				Parent.IsDataDirty = true;
			}
		}

		public void ChangeAntiTemplatePreviewIndex(int delta)
		{
			SetAntiTemplatePreviewIndex(CurrentAntiTemplateIndex + delta);
		}
		

		private void SetTemplatePreviewIndex(int index)
		{
			if (TemplatePupilFinder != null)
			{
				if (index < 0) index = 0;
				else if (index >= TemplatePupilFinder.NumTemplates) index = TemplatePupilFinder.NumTemplates - 1;

				CurrentTemplateIndex = index;
				TemplatePreviewImage = TemplatePupilFinder.GetTemplateImage(index);
			}
		}

		private void SetAntiTemplatePreviewIndex(int index)
		{
			if (TemplatePupilFinder != null)
			{
				if (index < 0) index = 0;
				else if (index >= TemplatePupilFinder.NumAntiTemplates) index = TemplatePupilFinder.NumAntiTemplates - 1;

				CurrentAntiTemplateIndex = index;
				AntiTemplatePreviewImage = TemplatePupilFinder.GetAntiTemplateImage(index);
			}
		}
	}
}
