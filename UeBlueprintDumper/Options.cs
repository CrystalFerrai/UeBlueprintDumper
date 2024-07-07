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

using CUE4Parse.UE4.Versions;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace UeBlueprintDumper
{
	/// <summary>
	/// Program options
	/// </summary>
	internal class Options
	{
		/// <summary>
		/// The directory containing the pak files for the game
		/// </summary>
		public string GameDirectory { get; set; }

		/// <summary>
		/// The engine version of the game
		/// </summary>
		public EGame EngineVersion { get; set; }

		/// <summary>
		/// The operatin mode of the program
		/// </summary>
		public OperatingModes OperatingModes { get; set; }

		/// <summary>
		/// Dump all assets containing this string in their path
		/// </summary>
		public string AssetMatch { get; set; }

		/// <summary>
		/// The output directory for the dump
		/// </summary>
		public string OutputDirectory { get; set; }

		/// <summary>
		/// Path to a mappings file for the game. Needed for UE5 games
		/// </summary>
		public string? MappingsPath { get; set; }

		/// <summary>
		/// An AES encryption key for the game's data, if necessary
		/// </summary>
		public string? EncryptionKey { get; set; }

		private Options()
		{
			GameDirectory = null!;
			OperatingModes = OperatingModes.Invalid;
			AssetMatch = null!;
			OutputDirectory = null!;
			MappingsPath = null;
			EncryptionKey = null;
		}

		/// <summary>
		/// Create an Options instance from command line arguments
		/// </summary>
		/// <param name="args">The command line arguments to parse</param>
		/// <param name="logger">For logging parse errors</param>
		/// <param name="options">Outputs the options if parsing is successful</param>
		/// <returns>Whether parsing was successful</returns>
		public static bool TryParseCommandLine(string[] args, Logger logger, [NotNullWhen(true)] out Options? result)
		{
			if (args.Length == 0)
			{
				result = null;
				return false;
			}

			Options instance = new();

			int positionalArgIndex = 0;

			for (int i = 0; i < args.Length; ++i)
			{
				if (args[i].StartsWith("--"))
				{
					// Explicit arg
					string argValue = args[i][2..];
					switch (argValue)
					{
						case "list":
							instance.OperatingModes |= OperatingModes.ListBlueprints;
							break;
						case "dump":
							instance.OperatingModes |= OperatingModes.DumpBlueprints;
							break;
						case "mappings":
							if (i < args.Length - 1 && !args[i + 1].StartsWith("--"))
							{
								instance.MappingsPath = args[i + 1];
								++i;
							}
							else
							{
								logger.LogError("Missing parameter for --mappings argument");
								result = null;
								return false;
							}
							break;
						case "key":
							if (i < args.Length - 1 && !args[i + 1].StartsWith("--"))
							{
								instance.EncryptionKey = args[i + 1];
								++i;
							}
							else
							{
								logger.LogError("Missing parameter for --key argument");
								result = null;
								return false;
							}
							break;
						default:
							logger.LogError($"Unrecognized argument '{args[i]}'");
							result = null;
							return false;
					}
				}
				else
				{
					// Positional arg
					switch (positionalArgIndex)
					{
						case 0:
							instance.GameDirectory = Path.GetFullPath(args[i]);
							break;
						case 1:
							{
								string value = args[i];
								if (!value.StartsWith("GAME_", StringComparison.OrdinalIgnoreCase))
								{
									value = "GAME_" + value;
								}
								if (Enum.TryParse<EGame>(value, true, out EGame version))
								{
									instance.EngineVersion = version;
								}
								else
								{
									logger.LogError($"{args[i]} is not a valid engine version.");
									result = null;
									return false;
								}
							}
							break;
						case 2:
							instance.AssetMatch = args[i];
							break;
						case 3:
							instance.OutputDirectory = Path.GetFullPath(args[i]);
							break;
						default:
							logger.LogError("Too many positional arguments.");
							result = null;
							return false;
					}
					++positionalArgIndex;
				}
			}

			if (positionalArgIndex < 4)
			{
				logger.LogError($"Not enough positional arguments");
				result = null;
				return false;
			}

			if (!Directory.Exists(instance.GameDirectory))
			{
				logger.LogError($"The specified game directory \"{instance.GameDirectory}\" does not exist or is inaccessible");
				result = null;
				return false;
			}

			if (instance.MappingsPath is not null && !File.Exists(instance.MappingsPath))
			{
				logger.LogError($"The specified mappings path \"{instance.MappingsPath}\" does not exist or is inacessible");
				result = null;
				return false;
			}

			if (instance.OperatingModes == OperatingModes.Invalid)
			{
				instance.OperatingModes = OperatingModes.DumpBlueprints;
			}

			result = instance;
			return true;
		}

		/// <summary>
		/// Prints how to use the program, including all possible command line arguments
		/// </summary>
		/// <param name="logger">Where the message will be printed</param>
		/// <param name="indent">Every line of the output will be prefixed with this</param>
		public static void PrintUsage(Logger logger, string indent = "")
		{
			string? programName = Assembly.GetExecutingAssembly().GetName().Name;
			logger.Log(LogLevel.Important, $"{indent}Usage: {programName} [[options]] [game directory] [asset match] [output directory]");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  game directory    The directory of the game from which you want to dump blueprint data.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  engine version    The engine version the game was built with. See list below for values.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  asset match       A string matching one or more assets paths to dump. All assets with");
			logger.Log(LogLevel.Important, $"{indent}                    paths containing this text will be processed.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  output directory  A file or directory that will receive the dumped blueprints.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}Options");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  --dump      Dump blueprints starting with the passed in path to the output diretory.");
			logger.Log(LogLevel.Important, $"{indent}              This is the default behavior.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  --list      Output the names of all assets starting with the passed in path to a");
			logger.Log(LogLevel.Important, $"{indent}              file named AssetList.txt in the output directory instead of dumping");
			logger.Log(LogLevel.Important, $"{indent}              blueprint data. This includes assets which are not blueprints. Combine");
			logger.Log(LogLevel.Important, $"{indent}              with --dump if you want to do both operations.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  --mappings  The path to a usmap file for the game. This is necessary if the game contains");
			logger.Log(LogLevel.Important, $"{indent}              unversioned data, such as a UE5 game. See readme for more information.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  --key       The AES encryption key for the game's data if the data is encrypted.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}Game engine versions");
			logger.Log(LogLevel.Important, $"{indent}  Pass in the engine version that best matches the game being dumped. If the game has a");
			logger.Log(LogLevel.Important, $"{indent}  specialized version, pass that in. Otherwise, pass in the engine version the game was");
			logger.Log(LogLevel.Important, $"{indent}  built with, which can be found in the properties of the game's exe.");
			logger.LogEmptyLine(LogLevel.Important);
			logger.Log(LogLevel.Important, $"{indent}  Following is a list of all possible engine version values.");
			logger.LogEmptyLine(LogLevel.Important);

			foreach (EGame version in Enum.GetValues<EGame>().ToHashSet())
			{
				string versionStr;
				switch (version)
				{
					case EGame.GAME_UE4_LATEST:
						versionStr = "UE4_LATEST";
						break;
					case EGame.GAME_UE5_LATEST:
						versionStr = "UE5_LATEST";
						break;
					default:
						versionStr = version.ToString()[5..]; // Trim GAME_
						break;
				}
				logger.Log(LogLevel.Important, $"  {versionStr}");
			}
		}

		/// <summary>
		/// Prints the current configuration of options
		/// </summary>
		/// <param name="logger">Where the message will be printed</param>
		/// <param name="logLevel">The log level for the message</param>
		/// <param name="indent">Every line of the output will be prefixed with this</param>
		public void PrintConfiguration(Logger logger, LogLevel logLevel, string indent = "")
		{
			string programName = Assembly.GetExecutingAssembly().GetName().Name ?? "Ue4Export";
			logger.Log(logLevel, $"{indent}{programName}");
			logger.Log(logLevel, $"{indent}  Game directory    {GameDirectory}");
			logger.Log(logLevel, $"{indent}  Engine version    {EngineVersion.ToString()[5..]}");
			logger.Log(logLevel, $"{indent}  Asset match       {AssetMatch}");
			logger.Log(logLevel, $"{indent}  Output directory  {OutputDirectory}");
			logger.Log(logLevel, $"{indent}  Operations        {OperatingModes}");
			logger.Log(logLevel, $"{indent}  Mappings path     {MappingsPath ?? "[None]"}");
			logger.Log(logLevel, $"{indent}  AES key           {(EncryptionKey is null ? "No" : "Yes")}");
		}
	}

	[Flags]
	internal enum OperatingModes
	{
		Invalid = 0x00,
		ListBlueprints = 0x01,
		DumpBlueprints = 0x02
	}
}
