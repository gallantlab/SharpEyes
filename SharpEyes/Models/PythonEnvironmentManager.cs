using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Python.Runtime;

namespace SharpEyes.Models
{
	public enum PackageStatus { Installed, Missing }

	public class DependencyCheckResult
	{
		public PackageStatus NumPy { get; set; } = PackageStatus.Missing;
		public PackageStatus Pillow { get; set; } = PackageStatus.Missing;
		public PackageStatus Moten { get; set; } = PackageStatus.Missing;
		public PackageStatus Torch { get; set; } = PackageStatus.Missing;
	}

	public class CondaEnvironmentInfo
	{
		public string Name { get; set; } = String.Empty;
		public string Path { get; set; } = String.Empty;
	}

	public class PythonEnvironmentManager
	{
		private static PythonEnvironmentManager? _instance;
		public static PythonEnvironmentManager Instance => _instance ??= new PythonEnvironmentManager();

		public Settings Settings { get; private set; } = new Settings();
		public bool IsInitialized { get; private set; } = false;

		private static readonly string AppDataDirectory =
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SharpEyes");

		public static string VenvPath => Path.Combine(AppDataDirectory, "system-venv");
		public static string BundledPythonPath => Path.Combine(AppDataDirectory, "python-dist");

		private const string BundledPythonVersion = "3.12.13";
		private const string BundledPythonTag = "20260510";
		private const string PymotenGitURL = "git+https://github.com/gallantlab/pymoten.git";
		private const string PymotenInstallSpec = PymotenGitURL + "[torch]";

		private static string BundledPythonDownloadURL
		{
			get
			{
				string platform;
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					platform = "x86_64-pc-windows-msvc";
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
					platform = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
						? "aarch64-apple-darwin"
						: "x86_64-apple-darwin";
				else
					platform = "x86_64-unknown-linux-gnu";
				return String.Format(
					"https://github.com/indygreg/python-build-standalone/releases/download/{0}/cpython-{1}+{0}-{2}-install_only_stripped.tar.gz",
					BundledPythonTag, BundledPythonVersion, platform);
			}
		}

		private PythonEnvironmentManager() { }

		public void LoadSettings()
		{
			Settings = Settings.Load();
		}

		public void SaveSettings()
		{
			Settings.Save();
		}

		public void Shutdown()
		{
			if (!IsInitialized) return;
			PythonEngine.Shutdown();
			IsInitialized = false;
		}

		// Attempts to initialize pythonnet. Can only be called once per process.
		public void Initialize()
		{
			if (IsInitialized) return;

			string pythonHome = GetPythonHome();
			string? pythonDLL = FindPythonDLL(pythonHome);
			if (pythonDLL == null)
				throw new InvalidOperationException(
					String.Format("Could not find Python shared library in: {0}", pythonHome));

			Runtime.PythonDLL = pythonDLL;
			PythonEngine.PythonHome = pythonHome;
			PythonEngine.Initialize();
			IsInitialized = true;

			if (Settings.PythonSourceMode == PythonSourceMode.System)
			{
				// Add venv site-packages to sys.path
				string sitePackagesPath = GetVenvSitePackagesPath();
				using (Py.GIL())
				{
					dynamic sys = Py.Import("sys");
					sys.path.append(sitePackagesPath);
				}
			}

			// Release the GIL from the initializing thread so any thread can acquire it
			PythonEngine.BeginAllowThreads();
		}

		private string GetPythonHome()
		{
			switch (Settings.PythonSourceMode)
			{
				case PythonSourceMode.System:
					// Use sys.base_prefix from the configured Python executable
					string basePrefix = RunPythonCommand(
						Settings.SystemPythonExecutablePath, "-c \"import sys; print(sys.base_prefix)\"");
					return basePrefix.Trim();

				case PythonSourceMode.Conda:
					return Settings.CondaEnvironmentPath;

				case PythonSourceMode.Bundled:
				default:
					return Path.Combine(BundledPythonPath, "python");
			}
		}

		private static string? FindPythonDLL(string pythonHome)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				string libDir = Path.Combine(pythonHome, "lib");
				if (Directory.Exists(libDir))
				{
					string[] candidates = Directory.GetFiles(libDir, "libpython3.*.so.1.0");
					if (candidates.Length > 0) return candidates[0];
					candidates = Directory.GetFiles(libDir, "libpython3.*.so*");
					if (candidates.Length > 0) return candidates[0];
				}
				// Fallback: search common system library directories
				string[] systemLibDirs = { "/usr/lib/x86_64-linux-gnu", "/usr/lib", "/usr/local/lib" };
				foreach (string dir in systemLibDirs)
				{
					if (!Directory.Exists(dir)) continue;
					string[] candidates = Directory.GetFiles(dir, "libpython3.*.so.1.0");
					if (candidates.Length > 0) return candidates[0];
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				string libDir = Path.Combine(pythonHome, "lib");
				if (Directory.Exists(libDir))
				{
					string[] candidates = Directory.GetFiles(libDir, "libpython3.*.dylib");
					if (candidates.Length > 0) return candidates[0];
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string[] candidates = Directory.GetFiles(pythonHome, "python3*.dll");
				if (candidates.Length > 0) return candidates[0];
			}
			return null;
		}

		private static string GetVenvSitePackagesPath()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return Path.Combine(VenvPath, "Lib", "site-packages");
			// Python version directory name varies; find it
			string libPath = Path.Combine(VenvPath, "lib");
			if (Directory.Exists(libPath))
			{
				string[] versionDirs = Directory.GetDirectories(libPath, "python3.*");
				if (versionDirs.Length > 0)
					return Path.Combine(versionDirs[0], "site-packages");
			}
			return Path.Combine(VenvPath, "lib", "python3.12", "site-packages");
		}

		private static string GetPythonExecutableForMode(Settings settings)
		{
			switch (settings.PythonSourceMode)
			{
				case PythonSourceMode.System:
					return settings.SystemPythonExecutablePath;

				case PythonSourceMode.Conda:
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
						return Path.Combine(settings.CondaEnvironmentPath, "python.exe");
					else
						return Path.Combine(settings.CondaEnvironmentPath, "bin", "python3");

				case PythonSourceMode.Bundled:
				default:
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
						return Path.Combine(BundledPythonPath, "python", "python.exe");
					else
						return Path.Combine(BundledPythonPath, "python", "bin", "python3");
			}
		}

		private static string GetPipExecutable(Settings settings)
		{
			switch (settings.PythonSourceMode)
			{
				case PythonSourceMode.System:
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
						return Path.Combine(VenvPath, "Scripts", "pip.exe");
					else
						return Path.Combine(VenvPath, "bin", "pip3");

				case PythonSourceMode.Conda:
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
						return Path.Combine(settings.CondaEnvironmentPath, "Scripts", "pip.exe");
					else
						return Path.Combine(settings.CondaEnvironmentPath, "bin", "pip3");

				case PythonSourceMode.Bundled:
				default:
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
						return Path.Combine(BundledPythonPath, "python", "Scripts", "pip.exe");
					else
						return Path.Combine(BundledPythonPath, "python", "bin", "pip3");
			}
		}

		// Runs before pythonnet initialization to check if pymoten and its deps are importable.
		public DependencyCheckResult CheckDependencies()
		{
			DependencyCheckResult result = new DependencyCheckResult();
			string pythonExe = GetPythonExecutableForMode(Settings);
			if (!File.Exists(pythonExe)) return result;

			string checkScript =
				"import sys\n" +
				"for pkg, label in [('numpy','numpy'),('PIL','pillow'),('moten','moten'),('torch','torch')]:\n" +
				"    try:\n" +
				"        __import__(pkg)\n" +
				"        print(label + ':installed')\n" +
				"    except ImportError:\n" +
				"        print(label + ':missing')\n";

			string output = RunPythonScript(pythonExe, checkScript);
			foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				string trimmed = line.Trim();
				if (trimmed.StartsWith("numpy:"))
					result.NumPy = trimmed.EndsWith("installed") ? PackageStatus.Installed : PackageStatus.Missing;
				else if (trimmed.StartsWith("pillow:"))
					result.Pillow = trimmed.EndsWith("installed") ? PackageStatus.Installed : PackageStatus.Missing;
				else if (trimmed.StartsWith("moten:"))
					result.Moten = trimmed.EndsWith("installed") ? PackageStatus.Installed : PackageStatus.Missing;
				else if (trimmed.StartsWith("torch:"))
					result.Torch = trimmed.EndsWith("installed") ? PackageStatus.Installed : PackageStatus.Missing;
			}
			return result;
		}

		// Creates the venv (System mode only) and installs pymoten and deps.
		public async Task SetupSystemPython(IProgress<string>? statusProgress = null)
		{
			statusProgress?.Report("Creating virtual environment...");
			string pythonExe = Settings.SystemPythonExecutablePath;
			await RunProcessAsync(pythonExe, String.Format("-m venv \"{0}\"", VenvPath));

			statusProgress?.Report("Installing pymoten...");
			string pipExe = GetPipExecutable(Settings);
			await RunProcessAsync(pipExe, String.Format("install numpy Pillow {0}", PymotenInstallSpec));
			statusProgress?.Report("Done.");
		}

		// Installs pymoten into the currently configured environment.
		public async Task InstallPymoten(IProgress<string>? statusProgress = null)
		{
			if (Settings.PythonSourceMode == PythonSourceMode.Conda)
			{
				string? condaExe = DetectConda();
				if (condaExe == null)
					throw new InvalidOperationException("conda executable not found.");
				statusProgress?.Report("Installing numpy and Pillow via conda...");
				await RunProcessAsync(condaExe, String.Format(
					"install -p \"{0}\" numpy blas=*=mkl pillow -y",
					Settings.CondaEnvironmentPath));
				statusProgress?.Report("Installing pytorch via conda...");
				await RunProcessAsync(condaExe, String.Format(
					"install -p \"{0}\" pytorch pytorch-cuda -c pytorch -c nvidia -y",
					Settings.CondaEnvironmentPath));
				statusProgress?.Report("Installing pymoten...");
				string pipExe = GetPipExecutable(Settings);
				await RunProcessAsync(pipExe, String.Format("install {0}", PymotenGitURL));
			}
			else
			{
				statusProgress?.Report("Installing pymoten...");
				string pipExe = GetPipExecutable(Settings);
				await RunProcessAsync(pipExe, String.Format("install numpy Pillow {0}", PymotenInstallSpec));
			}
			statusProgress?.Report("Done.");
		}

		// Installs only the packages flagged as missing in checkResult into the current environment.
		public async Task InstallMissingPackages(DependencyCheckResult checkResult, IProgress<string>? statusProgress = null)
		{
			if (Settings.PythonSourceMode == PythonSourceMode.Conda)
			{
				string? condaExe = DetectConda();
				if (condaExe == null)
					throw new InvalidOperationException("conda executable not found.");
				string envPath = Settings.CondaEnvironmentPath;

				List<string> defaultsPackages = new List<string>();
				List<string> torchPackages = new List<string>();
				bool installPymoten = false;

				if (checkResult.NumPy == PackageStatus.Missing)
				{
					defaultsPackages.Add("numpy");
					defaultsPackages.Add("blas=*=mkl");
				}
				if (checkResult.Pillow == PackageStatus.Missing)
					defaultsPackages.Add("pillow");
				if (checkResult.Moten == PackageStatus.Missing)
				{
					torchPackages.Add("pytorch");
					torchPackages.Add("pytorch-cuda");
					installPymoten = true;
				}
				else if (checkResult.Torch == PackageStatus.Missing)
				{
					torchPackages.Add("pytorch");
					torchPackages.Add("pytorch-cuda");
				}

				if (defaultsPackages.Count == 0 && torchPackages.Count == 0 && !installPymoten)
				{
					statusProgress?.Report("All packages already installed.");
					return;
				}

				if (defaultsPackages.Count > 0)
				{
					statusProgress?.Report("Installing missing packages via conda...");
					await RunProcessAsync(condaExe, String.Format(
						"install -p \"{0}\" {1} -y", envPath, String.Join(" ", defaultsPackages)));
				}
				if (torchPackages.Count > 0)
				{
					statusProgress?.Report("Installing pytorch via conda...");
					await RunProcessAsync(condaExe, String.Format(
						"install -p \"{0}\" {1} -c pytorch -c nvidia -y", envPath, String.Join(" ", torchPackages)));
				}
				if (installPymoten)
				{
					statusProgress?.Report("Installing pymoten...");
					string pipExe = GetPipExecutable(Settings);
					await RunProcessAsync(pipExe, String.Format("install {0}", PymotenGitURL));
				}
			}
			else
			{
				string pipExe = GetPipExecutable(Settings);
				List<string> packagesToInstall = new List<string>();

				if (checkResult.NumPy == PackageStatus.Missing)
					packagesToInstall.Add("numpy");
				if (checkResult.Pillow == PackageStatus.Missing)
					packagesToInstall.Add("Pillow");
				if (checkResult.Moten == PackageStatus.Missing)
					packagesToInstall.Add(PymotenInstallSpec);  // also pulls in torch via [torch] extra
				else if (checkResult.Torch == PackageStatus.Missing)
					packagesToInstall.Add("torch");  // moten present but torch missing

				if (packagesToInstall.Count == 0)
				{
					statusProgress?.Report("All packages already installed.");
					return;
				}

				statusProgress?.Report("Installing missing packages...");
				await RunProcessAsync(pipExe, String.Format("install {0}", String.Join(" ", packagesToInstall)));
			}
			statusProgress?.Report("Done.");
		}

		// Probes the configured Python environment for which pymoten backends are usable.
		// Returns a subset of: "numpy", "torch", "torch_cuda", "torch_mps"
		public List<string> ProbeAvailableBackends()
		{
			List<string> availableBackends = new List<string>();
			string pythonExe = GetPythonExecutableForMode(Settings);
			if (!File.Exists(pythonExe)) return availableBackends;

			string probeScript =
				"try:\n" +
				"    import numpy\n" +
				"    print('numpy')\n" +
				"except ImportError:\n" +
				"    pass\n" +
				"try:\n" +
				"    import torch\n" +
				"    print('torch')\n" +
				"    if torch.cuda.is_available():\n" +
				"        print('torch_cuda')\n" +
				"    if torch.backends.mps.is_available():\n" +
				"        print('torch_mps')\n" +
				"except ImportError:\n" +
				"    pass\n";

			string output = RunPythonScript(pythonExe, probeScript);
			foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				string trimmed = line.Trim();
				if (trimmed == "numpy" || trimmed == "torch" || trimmed == "torch_cuda" || trimmed == "torch_mps")
					availableBackends.Add(trimmed);
			}
			return availableBackends;
		}

		// Returns the path to the conda executable, or null if not found.
		public static string? DetectConda()
		{
			// Check PATH first
			string condaName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "conda.exe" : "conda";
			foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
			{
				string candidate = Path.Combine(dir, condaName);
				if (File.Exists(candidate)) return candidate;
			}

			// Check common install locations
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			List<string> searchPaths;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				searchPaths = new List<string>
				{
					Path.Combine(home, "miniconda3", "Scripts", "conda.exe"),
					Path.Combine(home, "Anaconda3", "Scripts", "conda.exe"),
					Path.Combine(home, "anaconda3", "Scripts", "conda.exe"),
					@"C:\ProgramData\miniconda3\Scripts\conda.exe",
					@"C:\ProgramData\Anaconda3\Scripts\conda.exe",
				};
			}
			else
			{
				searchPaths = new List<string>
				{
					Path.Combine(home, "miniconda3", "bin", "conda"),
					Path.Combine(home, "anaconda3", "bin", "conda"),
					Path.Combine(home, "Miniconda3", "bin", "conda"),
					"/opt/miniconda3/bin/conda",
					"/opt/anaconda3/bin/conda",
					"/opt/conda/bin/conda",
				};
			}

			foreach (string path in searchPaths)
			{
				if (File.Exists(path)) return path;
			}
			return null;
		}

		// Returns a list of all conda environments.
		public static List<CondaEnvironmentInfo> GetCondaEnvironments()
		{
			List<CondaEnvironmentInfo> environments = new List<CondaEnvironmentInfo>();
			string? condaExe = DetectConda();
			if (condaExe == null) return environments;

			string output = RunProcess(condaExe, "env list --json");
			try
			{
				JsonDocument doc = JsonDocument.Parse(output);
				JsonElement envsArray = doc.RootElement.GetProperty("envs");
				bool isFirst = true;
				foreach (JsonElement envElement in envsArray.EnumerateArray())
				{
					string envPath = envElement.GetString() ?? String.Empty;
					string name = isFirst ? "base" : Path.GetFileName(envPath);
					environments.Add(new CondaEnvironmentInfo { Name = name, Path = envPath });
					isFirst = false;
				}
			}
			catch { }
			return environments;
		}

		// Creates a new conda environment with Python 3.12 and installs pymoten.
		public async Task CreateCondaEnvironment(string environmentName, IProgress<string>? statusProgress = null)
		{
			string? condaExe = DetectConda();
			if (condaExe == null)
				throw new InvalidOperationException("conda executable not found.");

			statusProgress?.Report(String.Format("Creating conda environment '{0}'...", environmentName));
			await RunProcessAsync(condaExe, String.Format("create -n {0} python=3.12 -y", environmentName));

			// Find the newly created environment path
			List<CondaEnvironmentInfo> environments = GetCondaEnvironments();
			CondaEnvironmentInfo? newEnv = environments.Find(e => e.Name == environmentName);
			if (newEnv != null)
				Settings.CondaEnvironmentPath = newEnv.Path;

			statusProgress?.Report("Installing numpy and Pillow via conda...");
			await RunProcessAsync(condaExe, String.Format(
				"install -n {0} numpy blas=*=mkl pillow -y", environmentName));
			statusProgress?.Report("Installing pytorch via conda...");
			await RunProcessAsync(condaExe, String.Format(
				"install -n {0} pytorch pytorch-cuda -c pytorch -c nvidia -y", environmentName));
			statusProgress?.Report("Installing pymoten...");
			string pipExe = GetPipExecutable(Settings);
			await RunProcessAsync(pipExe, String.Format("install {0}", PymotenGitURL));
			statusProgress?.Report("Done.");
		}

		// Downloads and extracts python-build-standalone, then installs pymoten.
		public async Task DownloadBundledPython(IProgress<double> downloadProgress, IProgress<string>? statusProgress = null)
		{
			string downloadURL = BundledPythonDownloadURL;
			string tarPath = Path.Combine(AppDataDirectory, "python-dist.tar.gz");
			Directory.CreateDirectory(AppDataDirectory);

			statusProgress?.Report("Downloading Python...");
			using (HttpClient httpClient = new HttpClient())
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "SharpEyes");
				using HttpResponseMessage response = await httpClient.GetAsync(
					downloadURL, HttpCompletionOption.ResponseHeadersRead);
				response.EnsureSuccessStatusCode();
				long? totalBytes = response.Content.Headers.ContentLength;
				using Stream contentStream = await response.Content.ReadAsStreamAsync();
				using FileStream fileStream = new FileStream(tarPath, FileMode.Create, FileAccess.Write);
				byte[] buffer = new byte[81920];
				long bytesRead = 0;
				int read;
				while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
				{
					await fileStream.WriteAsync(buffer, 0, read);
					bytesRead += read;
					if (totalBytes.HasValue)
						downloadProgress.Report((double)bytesRead / totalBytes.Value);
				}
			}

			statusProgress?.Report("Extracting...");
			string extractDir = Path.Combine(AppDataDirectory, "python-dist");
			if (Directory.Exists(extractDir))
				Directory.Delete(extractDir, true);
			Directory.CreateDirectory(extractDir);
			await RunProcessAsync("tar", String.Format("-xzf \"{0}\" -C \"{1}\"", tarPath, extractDir));
			File.Delete(tarPath);

			statusProgress?.Report("Installing pymoten...");
			string pipExe = GetPipExecutable(Settings);
			await RunProcessAsync(pipExe, String.Format("install numpy Pillow {0}", PymotenInstallSpec));
			statusProgress?.Report("Done.");
			downloadProgress.Report(1.0);
		}

		public bool IsBundledPythonDownloaded()
		{
			string pythonExe = GetPythonExecutableForMode(
				new Settings { PythonSourceMode = PythonSourceMode.Bundled });
			return File.Exists(pythonExe);
		}

		// == Subprocess helpers ==

		private static string RunPythonCommand(string pythonExe, string arguments)
		{
			return RunProcess(pythonExe, arguments);
		}

		private static string RunPythonScript(string pythonExe, string script)
		{
			string tempFile = Path.GetTempFileName() + ".py";
			File.WriteAllText(tempFile, script);
			try
			{
				return RunProcess(pythonExe, String.Format("\"{0}\"", tempFile));
			}
			finally
			{
				File.Delete(tempFile);
			}
		}

		private static string RunProcess(string executable, string arguments)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = executable,
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using Process process = new Process { StartInfo = startInfo };
			process.Start();
			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();
			return output;
		}

		private static async Task RunProcessAsync(string executable, string arguments)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = executable,
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using Process process = new Process { StartInfo = startInfo };
			process.Start();
			await process.WaitForExitAsync();
		}
	}
}
