// Copyright 2024 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Versions;
using System.Reflection;

namespace UeBlueprintDumper
{
	/// <summary>
	/// The main program
	/// </summary>
	internal class Program : IDisposable
	{
		private readonly Options mOptions;
		private readonly Logger mLogger;

		private readonly DefaultFileProvider mProvider;

		public Program(Options options, Logger logger)
		{
			mOptions = options;
			mLogger = logger;

			mProvider = new(new DirectoryInfo(options.GameDirectory!), SearchOption.AllDirectories, null, null);
		}

		/// <summary>
		/// Entry point
		/// </summary>
		private static int Main(string[] args)
		{
			Logger logger = new ConsoleLogger();
			if (args.Length == 0)
			{
				Options.PrintUsage(logger);
				return OnExit(0);
			}

			Options? options;
			if (!Options.TryParseCommandLine(args, logger, out options))
			{
				logger.LogEmptyLine(LogLevel.Information);
				Options.PrintUsage(logger);
				return OnExit(1);
			}

			string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
			ZlibHelper.Initialize(Path.Combine(assemblyDir, ZlibHelper.DLL_NAME));
			OodleHelper.Initialize(Path.Combine(assemblyDir, OodleHelper.OODLE_DLL_NAME));

			options.PrintConfiguration(logger, LogLevel.Information);
			logger.LogEmptyLine(LogLevel.Information);

			// Allow exception to escape in debug mode for easier debugging
#if !DEBUG
			try
			{
#endif
			using Program program = new(options, logger);
			program.Initialize();
			program.Run();
#if !DEBUG
		}
			catch (Exception ex)
			{
				logger.LogError("Failed. Error:");
				ExceptionHelper.PrintException(ex, logger);
				return OnExit(1);
			}
#endif

			return OnExit(0);
		}

		/// <summary>
		/// Initialize the program
		/// </summary>
		public void Initialize()
		{
			mProvider.Versions.Game = mOptions.EngineVersion;
			mProvider.Versions.Ver = mOptions.EngineVersion.GetVersion();

			mProvider.Initialize();

			if (mOptions.MappingsPath is not null)
			{
				mProvider.MappingsContainer = new FileUsmapTypeMappingsProvider(mOptions.MappingsPath);
			}

			FAesKey encryptionKey;
			if (mOptions.EncryptionKey is null)
			{
				encryptionKey = new(new byte[32]);
			}
			else
			{
				encryptionKey = new(mOptions.EncryptionKey);
			}
			foreach (var vfsReader in mProvider.UnloadedVfs)
			{
				mProvider.SubmitKey(vfsReader.EncryptionKeyGuid, encryptionKey);
			}

			mProvider.PostMount();

			mProvider.ChangeCulture(mProvider.GetLanguageCode(ELanguage.English));
		}

		/// <summary>
		/// Run the program
		/// </summary>
		public void Run()
		{
			if (mOptions.OperatingModes.HasFlag(OperatingModes.ListBlueprints))
			{
				string outPath = Path.Combine(mOptions.OutputDirectory, "AssetList.txt");
				mLogger.Log(LogLevel.Important, $"Outputting all asset paths containing the text \"{mOptions.AssetMatch}\" to \"{outPath}\"...");

				using FileStream outFile = File.Create(outPath);
				using StreamWriter writer = new(outFile);
				foreach (string path in mProvider.Files.Keys)
				{
					if (path.Contains(mOptions.AssetMatch, StringComparison.OrdinalIgnoreCase))
					{
						mLogger.Log(LogLevel.Information, path);
						writer.WriteLine(path);
					}
				}
			}

			if (mOptions.OperatingModes.HasFlag(OperatingModes.DumpBlueprints))
			{
				if (mOptions.OperatingModes.HasFlag(OperatingModes.ListBlueprints))
				{
					mLogger.LogEmptyLine(LogLevel.Important);
				}
				mLogger.Log(LogLevel.Important, $"Dumping all assets with paths containing the text \"{mOptions.AssetMatch}\" to \"{mOptions.OutputDirectory}\"...");

				foreach (string path in mProvider.Files.Keys)
				{
					if (path.Contains(mOptions.AssetMatch, StringComparison.OrdinalIgnoreCase))
					{
						if (path.EndsWith(".uasset"))
						{
							mLogger.Log(LogLevel.Information, path);
							BlueprintDumper.DumpBlueprintData(path, mProvider, mOptions.OutputDirectory, mLogger);
						}
					}
				}
			}

			mLogger.LogEmptyLine(LogLevel.Important);
			mLogger.Log(LogLevel.Important, "Completed.");
		}

		/// <summary>
		/// Cleanup the program
		/// </summary>
		public void Dispose()
		{
			mProvider.Dispose();
		}

		private static int OnExit(int code)
		{
			if (System.Diagnostics.Debugger.IsAttached)
			{
				Console.Out.WriteLine("Press a key to exit");
				Console.ReadKey();
			}
			return code;
		}
	}
}
