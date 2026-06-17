using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ReactiveUI;
using Eyetracking;
using SharpEyes.Views;
using Avalonia.Controls;
using Num = NumSharp.np;

namespace SharpEyes.ViewModels
{
	public class CalibrationViewModel : ViewModelBase
	{
		public ReactiveCommand<Unit, Unit> LoadCalibrationPupilsCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ImportCalibrationPupilsCommand { get; set; }

		public ReactiveCommand<Unit, Unit> LoadPupilsToConvertCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ConvertPupilFinderCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ComputeMappingCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ForceRedrawCommand { get; set; }

		public int StimulusWidth
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1024;

		public int StimulusHeight
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 768;

		public string CalibrationRMSError
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = "Mapping not yet computed";

		public int CalibrationStartFrame
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1;

		public string CalibrationStartTimeStamp
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = "0:00:00.000";

		public double CalibrationDuration
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 2.0;

		public double CalibrationDelay
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 2.0;

		public double PointDelay
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0.167;

		public int EyetrackingFPS
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 60;

		public double DPIUnscaleFactor
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1.0;

		public ObservableCollection<Point> CalibrationPoints
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new ObservableCollection<Point>();

		// objects drawn on the screen to visualize things

		public ObservableCollection<Shape> ShapesToDraw
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new ObservableCollection<Shape>();

		public PupilInfo PupilInfo
		{
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = null;

		public bool IsCalibrationParametersExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationParametersExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationParametersExpanded;

		public bool IsCalibrationPointsExpanded
		{
			get;
			set
			{
				this.RaiseAndSetIfChanged(ref field, value);
				SharpEyes.Models.Settings.Current.CalibrationPointsExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		} = SharpEyes.Models.Settings.Current.CalibrationPointsExpanded;

		public CalibrationViewModel()
		{
			LoadCalibrationPupilsCommand = ReactiveCommand.Create(LoadCalibrationPupils);
			ImportCalibrationPupilsCommand = ReactiveCommand.Create(ImportCalibrationPupils);
			LoadPupilsToConvertCommand = ReactiveCommand.Create(LoadPupilsToConvert);
			ComputeMappingCommand = ReactiveCommand.Create(ComputeMapping);
			ConvertPupilFinderCommand = ReactiveCommand.Create(ConvertPupilFinderData);
			ForceRedrawCommand = ReactiveCommand.Create(ForceRedraw);

			CalibrationParameters defaults = CalibrationParameters.GetDefault35PointCalibrationParameters();
			foreach (CalibrationIndex index in defaults.calibrationSequence)
				CalibrationPoints.Add(defaults.calibrationPoints[index.Index]);

			ShapesToDraw.Add(new Rectangle()
			{
				Width = StimulusWidth, Height = StimulusHeight,
				StrokeThickness = 4, Stroke = new SolidColorBrush(Colors.DodgerBlue)
			});
		}

		public async void LoadCalibrationPupils()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load Pupils...",
				Filters = { new FileDialogFilter() { Name = "Numpy File (*.npy)", Extensions = { "npy" } } }
			};
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;
			string pupilsFile = fileName[0];

			openFileDialog.Title = "Load Timestamps...";
			fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;
			string timestampsFile = fileName[0];

			PupilInfo = new PupilInfo(Num.load(pupilsFile), Num.load(timestampsFile));
		}

		public void ImportCalibrationPupils()
		{

		}

		public void LoadPupilsToConvert()
		{

		}

		public void ComputeMapping()
		{

		}

		public void ConvertPupilFinderData()
		{

		}

		public void ForceRedraw()
		{
			ShapesToDraw.Clear();
			ShapesToDraw.Add(new Rectangle()
			{
				Width = StimulusWidth,
				Height = StimulusHeight,
				StrokeThickness = 4,
				Stroke = new SolidColorBrush(Colors.DodgerBlue)
			});
			foreach (Point p in CalibrationPoints)
			{
				
			}
		}
	}
}