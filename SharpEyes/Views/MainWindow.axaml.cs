using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using DynamicData.Kernel;
using MessageBox.Avalonia.BaseWindows.Base;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Enums;
using Sentry;

namespace SharpEyes.Views
{
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
			//ExtendClientAreaToDecorationsHint = true;
			ExtendClientAreaTitleBarHeightHint = -1;

			TransparencyLevelHint = WindowTransparencyLevel.AcrylicBlur;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				CheckLinuxLibraries();
		}

		public void GlobalExceptionHandler(Exception e)
		{
			IMsBoxWindow<MessageBox.Avalonia.Enums.ButtonResult> messageBox =
				MessageBoxManager.GetMessageBoxStandardWindow("Internal error",
					"SharpEyes has encountered an internal error and is exiting.\nAn error report will be sent",
					ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error);
			SentryId eventID = SentrySdk.CaptureException(e);
			ProcessStartInfo startInfo = new ProcessStartInfo()
			{
				FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "CrashReporter.exe" : "CrashReporter",
				Arguments = eventID.ToString(),
				UseShellExecute = true
			};
			Process.Start(startInfo);
		}

		private void Window_OnClosing(object? sender, CancelEventArgs e)
		{
			PupilFindingUserControl.OnClosing();
		}

		public void CheckLinuxLibraries()
		{
			string[] libraries =
			{
				"libtesseract.so",
				"libgtk-x11-2.0.so",
				"libgdk-x11-2.0.so",
				"libcairo.so",
				"libgdk_pixbuf-2.0.so",
				"libgobject-2.0.so",
				"libglib-2.0.so",
				"libdc1394.so",
				"libavcodec.so",
				"libavformat.so",
				"libavutil.so",
				"libswscale.so",
				"libjpeg.so",
				"libpng16.so",
				"libtiff.so",
				"libz.so",
				"libdl.so",
				"libpthread.so",
				"librt.so",
				"libstdc++.so",
				"libm.so",
				"libgcc_s.so",
				"libc.so",
				"libgmodule-2.0.so",
				"libpangocairo-1.0.so",
				"libX11.so",
				"libXfixes.so",
				"libatk-1.0.so",
				"libgio-2.0.so",
				"libpangoft2-1.0.so",
				"libpango-1.0.so",
				"libfontconfig.so",
				"libXrender.so",
				"libXinerama.so",
				"libXi.so",
				"libXrandr.so",
				"libXcursor.so",
				"libXcomposite.so",
				"libXdamage.so",
				"libXext.so",
				"libpixman-1.so",
				"libfreetype.so",
				"libxcb-shm.so",
				"libxcb.so",
				"libxcb-render.so",
				"libffi.so",
				"libpcre.so",
				"libraw1394.so",
				"libusb-1.0.so",
				"libswresample.so",
				"libwebp.so",
				"libva.so",
				"libzvbi.so",
				"libxvidcore.so",
				"libx265.so",
				"libx264.so",
				"libwebpmux.so",
				"libwavpack.so",
				"libvpx.so",
				"libvorbisenc.so",
				"libvorbis.so",
				"libtwolame.so",
				"libtheoraenc.so",
				"libtheoradec.so",
				"libspeex.so",
				"libsnappy.so",
				"libshine.so",
				"librsvg-2.so",
				"libopus.so",
				"libopenjp2.so",
				"libmp3lame.so",
				"libgsm.so",
				"liblzma.so",
				"libssh-gcrypt.so",
				"libopenmpt.so",
				"libbluray.so",
				"libgnutls.so",
				"libxml2.so",
				"libgme.so",
				"libchromaprint.so",
				"libbz2.so",
				"libdrm.so",
				"libvdpau.so",
				"libva-x11.so",
				"libva-drm.so",
				"libjbig.so",
				"libselinux.so",
				"libresolv.so",
				"libmount.so",
				"libharfbuzz.so",
				"libthai.so",
				"libexpat.so",
				"libXau.so",
				"libXdmcp.so",
				"libudev.so",
				"libsoxr.so",
				"libnuma.so",
				"libogg.so",
				"libgcrypt.so",
				"libgssapi_krb5.so",
				"libmpg123.so",
				"libvorbisfile.so",
				"libp11-kit.so",
				"libidn2.so",
				"libunistring.so",
				"libtasn1.so",
				"libnettle.so",
				"libhogweed.so",
				"libgmp.so",
				"libicuuc.so",
				"libblkid.so",
				"libgraphite2.so",
				"libdatrie.so",
				"libbsd.so",
				"libgomp.so",
				"libgpg-error.so",
				"libkrb5.so",
				"libk5crypto.so",
				"libcom_err.so",
				"libkrb5support.so",
				"libicudata.so",
				"libuuid.so",
				"libkeyutils.so"
			};
			List<string> missing = new List<string>();
			Process ldconfig = new Process()
			{
				StartInfo = new ProcessStartInfo()
				{
					FileName = "/sbin/ldconfig",
					Arguments = "-p",
					UseShellExecute = false,
					RedirectStandardOutput = true,
				}
			};
			ldconfig.Start();
			string all = ldconfig.StandardOutput.ReadToEnd();
			foreach (string library in libraries)
			{
				if (!all.Contains(library))
					missing.Add(library);
			}

			if (missing.Count > 0)
			{
				mainGrid.Children.Clear();
				StackPanel panel = new StackPanel();
				panel.Children.Add(new TextBlock(){Text = "The following libraries are missing and need to be installed:"});
				foreach (string library in missing)
					panel.Children.Add(new TextBlock(){Text = library});
				mainGrid.Children.Add(panel);
			}
		}
	}
}
