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

using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using System.Text;

namespace UeBlueprintDumper
{
	/// <summary>
	/// Utlity for dumping the data associated with a blueprint asset to text files
	/// </summary>
	internal static class BlueprintDumper
	{
		private const string SectionDivider = "================================================================================";

		private static readonly ISet<char> sInvalidFilenameChars = new HashSet<char>(Path.GetInvalidFileNameChars());

		/// <summary>
		/// Dumps blueprint data to a directory
		/// </summary>
		/// <param name="assetPath">The path to the asset</param>
		/// <param name="provider">The provider from which to read the asset</param>
		/// <param name="outDir">The output directory for the dump. Will create a subdirectory using the name of the asset.</param>
		/// <param name="logger">For logging any issues</param>
		public static void DumpBlueprintData(string assetPath, IFileProvider provider, string outDir, Logger logger)
		{
			string assetName = Path.GetFileNameWithoutExtension(assetPath);

			GameFile file = provider.Files[assetPath];
			if (!file.IsUePackage)
			{
				return;
			}
			
			outDir = Path.Combine(outDir, assetName);
			Directory.CreateDirectory(outDir);

			provider.ReadScriptData = true;

			AbstractUePackage package = (AbstractUePackage)provider.LoadPackage(file);
			package.Summary.FileVersionUE = provider.Versions.Ver;

			foreach (Lazy<UObject> export in package.ExportsLazy)
			{
				UObject exportObject = export.Value;
				try
				{
					if (exportObject is UFunction function)
					{
						DumpBlueprintFunction(function, assetName, package, outDir, logger);
					}
					else if (exportObject is UBlueprintGeneratedClass genClass)
					{
						DumpBlueprintClass(genClass, outDir, logger);
					}
				}
				catch (Exception ex)
				{
					logger.LogError($"Error processing export \"{exportObject.Name}\" from asset \"{package.Name}\". Output may be missing or incomplete.\n[{ex.GetType().FullName}] {ex.Message}");
				}
			}

			provider.ReadScriptData = false;
		}

		private static void DumpBlueprintClass(UBlueprintGeneratedClass clss, string outDir, Logger logger)
		{
			List<FFieldInfo> classProperties = new(clss.ChildProperties.Select(p => new FFieldInfo(p)));
			List<KeyValuePair<string, string?>> superOverrides = new();

			UObject? defaults = clss.ClassDefaultObject.ResolvedObject?.Object?.Value;
			if (defaults is not null)
			{
				Dictionary<string, FFieldInfo> classPropertyMap = classProperties.ToDictionary(p => p.Name, p => p);
				foreach (FPropertyTag prop in defaults.Properties)
				{
					string? valueStr = GetDefaultValueString(prop);
					if (classPropertyMap.TryGetValue(prop.Name.Text, out FFieldInfo? propInfo))
					{
						propInfo.DefaultValue = valueStr;
					}
					else
					{
						superOverrides.Add(new(prop.Name.Text, valueStr));
					}
				}
			}

			for (int i = classProperties.Count - 1; i >= 0; --i)
			{
				if (classProperties[i].Type.Equals("PointerToUberGraphFrame"))
				{
					classProperties.RemoveAt(i);
				}
			}

			string outPath = Path.Combine(outDir, $"Class_{clss.Name[..^2]}.txt");
			using (FileStream file = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(file))
			{
				writer.WriteLine($"Class: {clss.Name}");
				writer.WriteLine($"Parent: {clss.SuperStruct.Name}");
				writer.WriteLine($"Flags: {FormatClassFlags(clss.ClassFlags)}");
				writer.WriteLine($"Config: {clss.ClassConfigName.Text}");

				WriteFields(writer, "Properties", classProperties);

				WriteHeader(writer, "Parent property overrides");
				foreach (var pair in superOverrides)
				{
					writer.WriteLine($"{pair.Key} = {pair.Value}");
				}
			}
		}

		private static void DumpBlueprintFunction(UFunction function, string assetName, IPackage package, string outDir, Logger logger)
		{
			List<FFieldInfo> inParams = new();
			List<FFieldInfo> outParams = new();
			List<FFieldInfo> localVars = new();
			for (int i = 0; i < function.ChildProperties.Length; ++i)
			{
				FFieldInfo param = new(function.ChildProperties[i]);

				if ((param.PropertyFlags & EPropertyFlags.OutParm) != EPropertyFlags.None)
				{
					outParams.Add(param);
				}
				else if ((param.PropertyFlags & EPropertyFlags.Parm) != EPropertyFlags.None)
				{
					inParams.Add(param);
				}
				else
				{
					localVars.Add(param);
				}
			}

			string assembly = FunctionDumper.Process(package, function);

			string funcName = function.Name;
			string funcType = "Function";
			if ((function.FunctionFlags & EFunctionFlags.FUNC_Delegate) != EFunctionFlags.FUNC_None)
			{
				funcName = funcName[0..funcName.LastIndexOf("__")];
				funcType = "Delegate";
			}
			if ((function.FunctionFlags & EFunctionFlags.FUNC_UbergraphFunction) != EFunctionFlags.FUNC_None)
			{
				funcName = funcName.Replace($"_{assetName}", string.Empty);
				funcType = "Graph";
			}

			// Replace invalid file name characters
			funcName = new(funcName.Select(c => sInvalidFilenameChars.Contains(c) ? '_' : c).ToArray());

			string outPath = Path.Combine(outDir, $"{funcType}_{funcName}.txt");

			using (FileStream file = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(file))
			{
				writer.WriteLine($"Function: {function.Name}");
				writer.WriteLine($"Flags: {FormatFunctionFlags(function.FunctionFlags)}");

				WriteFields(writer, "Inputs", inParams);
				WriteFields(writer, "Outputs", outParams);
				WriteFields(writer, "Locals", localVars);

				WriteHeader(writer, "Code");
				writer.WriteLine(assembly);
			}
		}

		private static void WriteHeader(TextWriter writer, string header)
		{
			writer.WriteLine();
			writer.WriteLine(SectionDivider);
			writer.WriteLine(header);
			writer.WriteLine(SectionDivider);
		}

		private static void WriteFields(TextWriter writer, string header, IReadOnlyList<FFieldInfo> fields)
		{
			WriteHeader(writer, header);

			for (int i = 0; i < fields.Count; ++i)
			{
				WriteField(writer, fields[i]);
				if (i < fields.Count - 1)
				{
					writer.WriteLine();
				}
			}
		}

		private static void WriteField(TextWriter writer, FFieldInfo field)
		{
			writer.WriteLine($"Name: {field.Name}");
			writer.WriteLine($"Type: {field.Type}");
			if (field.DefaultValue is not null)
			{
				writer.WriteLine($"Default: {field.DefaultValue}");
			}
			if (field.PropertyFlags != EPropertyFlags.None)
			{
				writer.WriteLine($"Flags: {field.PropertyFlags}");
			}
		}

		private static string? GetDefaultValueString(FPropertyTag prop)
		{
			return GetDefaultValueString(prop.Tag);
		}

		private static string? GetDefaultValueString(FPropertyTagType? prop, string indent = "")
		{
			object? value = prop?.GenericValue;
			string nextIndent = indent + "  ";
			if (value is FScriptStruct ss)
			{
				if (ss.StructType is FStructFallback sfb)
				{
					if (sfb.Properties.Count == 0)
					{
						return "{ }";
					}

					StringBuilder builder = new($"{{{Environment.NewLine}");
					for (int i = 0; i < sfb.Properties.Count; ++i)
					{
						builder.Append($"{nextIndent}{GetDefaultValueString(sfb.Properties[i].Tag, nextIndent)}");
						if (i < sfb.Properties.Count - 1)
						{
							builder.Append(",");
						}
						builder.AppendLine();
					}
					builder.Append($"{indent}}}");
					return builder.ToString();
				}
				return $"{indent}{{ {ss.StructType} }}";
			}
			else if (value is UScriptArray || value is UScriptSet)
			{
				List<FPropertyTagType> properties = value is UScriptArray arr ? arr.Properties : ((UScriptSet)value).Properties;

				if (properties.Count == 0)
				{
					return "{ }";
				}

				StringBuilder builder = new($"{{{Environment.NewLine}");
				for (int i = 0; i < properties.Count; ++i)
				{
					builder.Append($"{nextIndent}{GetDefaultValueString(properties[i], nextIndent)}");
					if (i < properties.Count - 1)
					{
						builder.Append(",");
					}
					builder.AppendLine();
				}
				builder.Append($"{indent}}}");
				return builder.ToString();
			}
			else if (value is UScriptMap map)
			{
				if (map.Properties.Count == 0)
				{
					return "{ }";
				}

				StringBuilder builder = new($"{{{Environment.NewLine}");
				int i = 0;
				foreach (var pair in map.Properties)
				{
					builder.Append($"{nextIndent}{GetDefaultValueString(pair.Key, nextIndent)} = {GetDefaultValueString(pair.Value, nextIndent)}");
					if (i < map.Properties.Count - 1)
					{
						builder.Append(",");
					}
					builder.AppendLine();
					++i;
				}
				builder.Append($"{indent}}}");
				return builder.ToString();
			}
			return value?.ToString();
		}

		private static string FormatClassFlags(EClassFlags flags)
		{
			string result = flags.ToString();
			result = result.Replace("CLASS_Optional", "CLASS_Parsed"); // CUE4Parse changed Parsed to Optional in their enum
			result = result.Replace("CLASS_", string.Empty);
			return result;
		}

		private static string FormatFunctionFlags(EFunctionFlags flags)
		{
			string result = flags.ToString();
			result = result.Replace("FUNC_", string.Empty);
			return result;
		}

		private class FFieldInfo
		{
			public string Name { get; }

			public string Type { get; }

			public EPropertyFlags Flags { get; }

			public EPropertyFlags PropertyFlags { get; }

			public string? DefaultValue { get; set; }

			public FFieldInfo(FField field)
			{
				Name = field.Name.Text;
				Flags = (EPropertyFlags)field.Flags;

				if (field is FProperty prop)
				{
					Type = GetPropertyType(prop);
					PropertyFlags = (EPropertyFlags)prop.PropertyFlags;
				}
				else
				{
					Type = GetUnknownFieldType(field);
					PropertyFlags = EPropertyFlags.None;
				}
			}

			private static string GetUnknownFieldType(FField field)
			{
				string typeName = field.GetType().Name;
				int suffixIndex = typeName.IndexOf("Property");
				if (suffixIndex < 0)
				{
					return typeName;
				}
				return typeName[1..suffixIndex];
			}

			private static string GetPropertyType(FProperty? property)
			{
				if (property is null) return "None";

				if (property is FArrayProperty array)
				{
					string itemType = GetPropertyType(array.Inner);
					return $"Array<{itemType}>";
				}
				else if (property is FByteProperty bt)
				{
					return bt.Enum.ResolvedObject?.Name.Text ?? "Byte";
				}
				else if (property is FDelegateProperty dlgt)
				{
					return $"{dlgt.SignatureFunction.Name} (Delegate)";
				}
				else if (property is FEnumProperty enm)
				{
					return enm.Enum.Name;
				}
				else if (property is FFieldPathProperty fieldPath)
				{
					return $"{fieldPath.PropertyClass.Text} field path";
				}
				else if (property is FInterfaceProperty intrfc)
				{
					return $"{intrfc.InterfaceClass.Name} interface";
				}
				else if (property is FMapProperty map)
				{
					string keyType = GetPropertyType(map.KeyProp);
					string valueType = GetPropertyType(map.ValueProp);
					return $"Map<{keyType}, {valueType}>";
				}
				else if (property is FMulticastDelegateProperty mdlgt)
				{
					return $"{mdlgt.SignatureFunction.Name} (Multicast Delegate)";
				}
				else if (property is FMulticastInlineDelegateProperty midlgt)
				{
					return $"{midlgt.SignatureFunction.Name} (Multicast Inline Delegate)";
				}
				else if (property is FObjectProperty objct)
				{
					if (property is FClassProperty clss)
					{
						return $"{clss.MetaClass.Name} Class";
					}
					else if (property is FSoftClassProperty softClass)
					{
						return $"{softClass.MetaClass.Name} Class (soft)";
					}
					else
					{
						return objct.PropertyClass.Name;
					}
				}
				else if (property is FSetProperty set)
				{
					string itemType = GetPropertyType(set.ElementProp);
					return $"Set<{itemType}>";
				}
				else if (property is FStructProperty strct)
				{
					return strct.Struct.ResolvedObject?.Name.Text ?? "Struct";
				}

				return GetUnknownFieldType(property);
			}
		}
	}
}
