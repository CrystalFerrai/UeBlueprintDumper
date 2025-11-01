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
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace UeBlueprintDumper
{
	/// <summary>
	/// Dumps a human readable text version of a disassembled UFunction to a string.
	/// </summary>
	internal class FunctionDumper : IDisposable
	{
		private const string BlockSeparator = "--------------------------------------------------------------------------------";

		private readonly IPackage mPackage;
		private readonly UFunction mFunction;
		private readonly bool mLargeWorldCoordinates;

		private readonly StringWriter mWriter;

		private int mIndentLevel;

		private EX_LocalFinalFunction? mFinalFunction;

		private FunctionDumper(IPackage package, UFunction function)
		{
			mPackage = package;
			mFunction = function;

			mWriter = new StringWriter();

			mIndentLevel = -1;

			mLargeWorldCoordinates = package.Summary.FileVersionUE >= EUnrealEngineObjectUE5Version.LARGE_WORLD_COORDINATES;
		}

		public void Dispose()
		{
			mWriter.Dispose();
		}

		/// <summary>
		/// Process a fucntion and return human readable disassembly
		/// </summary>
		/// <param name="package">The package that owns the function</param>
		/// <param name="function">The function to process</param>
		/// <param name="finalFunction">If this is an event function that is implemented elsewhere, will contain the location of the implementation</param>
		/// <exception cref="ArgumentNullException">A parameter is null</exception>
		/// <exception cref="ArgumentException">Function's script bytecode is not loaded</exception>
		public static string Process(IPackage package, UFunction function, out LocalFinalFunctionData? finalFunction)
		{
			if (package == null) throw new ArgumentNullException(nameof(package));
			if (function == null) throw new ArgumentNullException(nameof(function));
			if (function.ScriptBytecode == null) throw new ArgumentException($"Function script is not loaded. Set {nameof(IFileProvider.ReadScriptData)}=true on the {nameof(IFileProvider)} used to load the function.", nameof(function));

			using FunctionDumper instance = new(package, function);
			string assembly = instance.Process(function.ScriptBytecode, null);

			finalFunction = instance.mFinalFunction is not null ? new() { Name = function.Name, Location = instance.mFinalFunction } : null;
			return assembly;
		}

		/// <summary>
		/// Process a fucntion and return human readable disassembly
		/// </summary>
		/// <param name="package">The package that owns the function</param>
		/// <param name="function">The function to process</param>
		/// <param name="finalFunctions">Event functions implemented by this graph</param>
		/// <exception cref="ArgumentNullException">A parameter is null</exception>
		/// <exception cref="ArgumentException">Function's script bytecode is not loaded</exception>
		public static string ProcessGraph(IPackage package, UFunction function, IReadOnlyList<LocalFinalFunctionData>? finalFunctions)
		{
			if (package == null) throw new ArgumentNullException(nameof(package));
			if (function == null) throw new ArgumentNullException(nameof(function));
			if (function.ScriptBytecode == null) throw new ArgumentException($"Function script is not loaded. Set {nameof(IFileProvider.ReadScriptData)}=true on the {nameof(IFileProvider)} used to load the function.", nameof(function));

			Dictionary<int, LocalFinalFunctionData>? finalFunctionMap = finalFunctions?.ToDictionary(f => ((EX_IntConst)f.Location.Parameters[0]).Value, f => f);

			using FunctionDumper instance = new(package, function);
			string assembly = instance.Process(function.ScriptBytecode, finalFunctionMap);

			return assembly;
		}

		private string Process(IList<KismetExpression> expressions, IReadOnlyDictionary<int, LocalFinalFunctionData>? finalFunctionMap)
		{
			if (finalFunctionMap is null) finalFunctionMap = new Dictionary<int, LocalFinalFunctionData>();

			int offset = 0;
			foreach (KismetExpression expression in expressions)
			{
				ProcessExpr(expression, finalFunctionMap, ref offset);
			}
			return mWriter.ToString();
		}

		// Based on ProcessCommon from ScriptDisassembler.cpp. Adapted to read CUE4Parse script data.
		// Offset calculation is based on deserialized data. Sizes referenced from ScriptSerialization.h
		// which is inlined into UStruct::Serialize in Class.cpp.
		private EExprToken ProcessExpr(KismetExpression expression, IReadOnlyDictionary<int, LocalFinalFunctionData> finalFunctionMap, ref int offset)
		{
			int opOffset = offset;
			AdvanceByte(ref offset);
			++mIndentLevel;

			if (finalFunctionMap.TryGetValue(opOffset, out LocalFinalFunctionData finalFunctionData))
			{
				Log($"Event: {finalFunctionData.Name}");
			}

			switch (expression.Token)
			{
				case EExprToken.EX_Cast:
					{
						EX_Cast op = (EX_Cast)expression;

						AdvanceByte(ref offset); // conversion type

						// CUE4Parse is not using the proper token name or cast type enum for this
						ECastToken castType = (ECastToken)op.ConversionType;
						Log(opOffset, expression.Token, "PrimitiveCast", $"Type {castType}");

						Log("Argument:");
						ProcessExpr(op.Target, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_SetSet:
					{
						EX_SetSet op = (EX_SetSet)expression;

						Log(opOffset, expression.Token);

						ProcessExpr(op.SetProperty, finalFunctionMap, ref offset);
						AdvanceInt32(ref offset);

						foreach (KismetExpression element in op.Elements)
						{
							ProcessExpr(element, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndSet

						break;
					}
				case EExprToken.EX_SetConst:
					{
						EX_SetConst op = (EX_SetConst)expression;

						AdvanceField(op.InnerProperty, ref offset);
						AdvanceInt32(ref offset);

						Log(opOffset, expression.Token, $"Element count: {op.Elements.Length}, inner property: {GetPath(op.InnerProperty)}");

						foreach (KismetExpression element in op.Elements)
						{
							ProcessExpr(element, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndSetConst

						break;
					}
				case EExprToken.EX_SetMap:
					{
						EX_SetMap op = (EX_SetMap)expression;

						ProcessExpr(op.MapProperty, finalFunctionMap, ref offset);

						AdvanceInt32(ref offset);

						Log(opOffset, expression.Token, $"Element count: {op.Elements.Length}");

						foreach (KismetExpression element in op.Elements)
						{
							ProcessExpr(element, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndMap

						break;
					}
				case EExprToken.EX_MapConst:
					{
						EX_MapConst op = (EX_MapConst)expression;

						AdvanceField(op.KeyProperty, ref offset);
						AdvanceField(op.ValueProperty, ref offset);
						AdvanceInt32(ref offset);

						Log(opOffset, expression.Token, $"Element count: {op.Elements.Length}, key property: {GetPath(op.KeyProperty)}, value property: {GetPath(op.ValueProperty)}");

						foreach (KismetExpression element in op.Elements)
						{
							ProcessExpr(element, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndMapConst

						break;
					}
				case EExprToken.EX_ObjToInterfaceCast:
				case EExprToken.EX_CrossInterfaceCast:
				case EExprToken.EX_InterfaceToObjCast:
					{
						EX_CastBase op = (EX_CastBase)expression;

						AdvanceObject(ref offset);

						Log(opOffset, expression.Token, $"Cast to {op.ClassPtr.Name}");

						ProcessExpr(op.Target, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_Let:
					{
						EX_Let op = (EX_Let)expression;

						Log(opOffset, expression.Token, "Variable = Expression");

						AdvanceField(op.Property, ref offset);

						++mIndentLevel;

						Log("Variable:");
						ProcessExpr(op.Variable, finalFunctionMap, ref offset);

						Log("Expression:");
						ProcessExpr(op.Assignment, finalFunctionMap, ref offset);

						--mIndentLevel;

						break;
					}
				case EExprToken.EX_LetObj:
				case EExprToken.EX_LetWeakObjPtr:
				case EExprToken.EX_LetBool:
				case EExprToken.EX_LetDelegate:
				case EExprToken.EX_LetMulticastDelegate:
					{
						EX_LetBase op = (EX_LetBase)expression;

						Log(opOffset, expression.Token, "Variable = Expression");

						++mIndentLevel;

						// Variable expr.
						Log("Variable:");
						ProcessExpr(op.Variable, finalFunctionMap, ref offset);

						// Assignment expr.
						Log("Expression:");
						ProcessExpr(op.Assignment, finalFunctionMap, ref offset);

						--mIndentLevel;

						break;
					}
				case EExprToken.EX_LetValueOnPersistentFrame:
					{
						EX_LetValueOnPersistentFrame op = (EX_LetValueOnPersistentFrame)expression;

						Log(opOffset, expression.Token);

						++mIndentLevel;

						AdvanceField(op.DestinationProperty, ref offset);
						Log($"Destination variable: {GetPath(op.DestinationProperty)}");

						Log("Expression:");
						ProcessExpr(op.AssignmentExpression, finalFunctionMap, ref offset);

						--mIndentLevel;

						break;
					}
				case EExprToken.EX_StructMemberContext:
					{
						EX_StructMemberContext op = (EX_StructMemberContext)expression;

						Log(opOffset, expression.Token);

						++mIndentLevel;

						AdvanceField(op.Property, ref offset);
						Log($"Member name: {GetPath(op.Property)}");

						Log("Expression to struct:");
						ProcessExpr(op.StructExpression, finalFunctionMap, ref offset);

						--mIndentLevel;

						break;
					}
				case EExprToken.EX_LocalVirtualFunction:
				case EExprToken.EX_VirtualFunction:
					{
						EX_VirtualFunction op = (EX_VirtualFunction)expression;

						AdvanceName(ref offset);
						Log(opOffset, expression.Token, op.VirtualFunctionName.Text);

						foreach (KismetExpression param in op.Parameters)
						{
							ProcessExpr(param, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndFunctionParms

						break;
					}
				case EExprToken.EX_LocalFinalFunction:
				case EExprToken.EX_FinalFunction:
				case EExprToken.EX_CallMath:
					{
						EX_FinalFunction op = (EX_FinalFunction)expression;

						AdvanceObject(ref offset);

						Log(opOffset, expression.Token, GetPath(op.StackNode));

						foreach (KismetExpression param in op.Parameters)
						{
							ProcessExpr(param, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndFunctionParms

						if (mFunction.FunctionFlags.HasFlag(EFunctionFlags.FUNC_Event) &&
							op is EX_LocalFinalFunction finalFunction &&
							finalFunction.Parameters.Length == 1 &&
							finalFunction.Parameters[0] is EX_IntConst)
						{
							// Function is implemented in an event graph
							mFinalFunction = finalFunction;
						}

						break;
					}
				case EExprToken.EX_ComputedJump:
					{
						EX_ComputedJump op = (EX_ComputedJump)expression;

						Log(opOffset, expression.Token, "Offset specified by expression:");

						++mIndentLevel;

						ProcessExpr(op.CodeOffsetExpression, finalFunctionMap, ref offset);

						--mIndentLevel;

						if (mIndentLevel == 0)
						{
							mWriter.WriteLine(BlockSeparator);
						}

						break;
					}

				case EExprToken.EX_Jump:
					{
						EX_Jump op = (EX_Jump)expression;

						AdvanceInt32(ref offset);
						Log(opOffset, expression.Token, $"Offset = 0x{op.CodeOffset:X}");

						if (mIndentLevel == 0)
						{
							mWriter.WriteLine(BlockSeparator);
						}
						break;
					}
				case EExprToken.EX_LocalVariable:
				case EExprToken.EX_DefaultVariable:
				case EExprToken.EX_InstanceVariable:
				case EExprToken.EX_LocalOutVariable:
				case EExprToken.EX_ClassSparseDataVariable:
					{
						EX_VariableBase op = (EX_VariableBase)expression;

						AdvanceField(op.Variable, ref offset);
						Log(opOffset, expression.Token, GetPath(op.Variable));

						break;
					}
				case EExprToken.EX_InterfaceContext:
					{
						EX_InterfaceContext op = (EX_InterfaceContext)expression;

						Log(opOffset, expression.Token);

						ProcessExpr(op.InterfaceValue, finalFunctionMap, ref offset);
					}
					break;
				case EExprToken.EX_Return:
					{
						EX_Return op = (EX_Return)expression;

						Log(opOffset, expression.Token);

						ProcessExpr(op.ReturnExpression, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_DeprecatedOp4A:
					{
						Log(opOffset, expression.Token, "This opcode has been removed and does nothing.");
						break;
					}
				case EExprToken.EX_Nothing:
				case EExprToken.EX_EndOfScript:
				case EExprToken.EX_IntZero:
				case EExprToken.EX_IntOne:
				case EExprToken.EX_True:
				case EExprToken.EX_False:
				case EExprToken.EX_NoObject:
				case EExprToken.EX_NoInterface:
				case EExprToken.EX_Self:
				case EExprToken.EX_EndParmValue:
					{
						Log(opOffset, expression.Token);
						break;
					}
				case EExprToken.EX_EndArray:
				case EExprToken.EX_EndArrayConst:
				case EExprToken.EX_EndFunctionParms:
				case EExprToken.EX_EndMap:
				case EExprToken.EX_EndMapConst:
				case EExprToken.EX_EndSet:
				case EExprToken.EX_EndSetConst:
				case EExprToken.EX_EndStructConst:
					{
						// The version of CUE4Parse we are using consumes these without including them in the
						// deserialized assembly. If we encounter this, we probably have a bug.
						throw new ApplicationException($"Unexpected {expression.Token}");
					}
				case EExprToken.EX_CallMulticastDelegate:
					{
						EX_CallMulticastDelegate op = (EX_CallMulticastDelegate)expression;

						AdvanceObject(ref offset);
						Log(opOffset, expression.Token, GetPath(op.StackNode));

						ProcessExpr(op.Delegate, finalFunctionMap, ref offset);

						++mIndentLevel;

						Log("Params:");
						foreach (KismetExpression param in op.Parameters)
						{
							ProcessExpr(param, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndFunctionParms

						--mIndentLevel;

						break;
					}
				case EExprToken.EX_ClassContext:
				case EExprToken.EX_Context:
				case EExprToken.EX_Context_FailSilent:
					{
						EX_Context op = (EX_Context)expression;

						Log(opOffset, expression.Token);

						++mIndentLevel;

						Log("ObjectExpression:");
						ProcessExpr(op.ObjectExpression, finalFunctionMap, ref offset);

						bool canFailSilently = expression.Token == EExprToken.EX_Context_FailSilent;
						if (canFailSilently)
						{
							Log("Can fail silently on access none");
						}

						// Code offset for NULL expressions.
						AdvanceInt32(ref offset);
						Log($"Skip {op.Offset} bytes");

						AdvanceField(op.RValuePointer, ref offset);
						Log($"R-Value Property: {GetPath(op.RValuePointer)}");

						Log("ContextExpression:");
						ProcessExpr(op.ContextExpression, finalFunctionMap, ref offset);

						--mIndentLevel;

						break;
					}
				case EExprToken.EX_IntConst:
					{
						EX_IntConst op = (EX_IntConst)expression;

						AdvanceInt32(ref offset);
						Log(opOffset, expression.Token, op.Value.ToString());

						break;
					}
				case EExprToken.EX_Int64Const:
					{
						EX_Int64Const op = (EX_Int64Const)expression;

						AdvanceInt64(ref offset);
						Log(opOffset, expression.Token, op.Value.ToString());

						break;
					}
				case EExprToken.EX_UInt64Const:
					{
						EX_UInt64Const op = (EX_UInt64Const)expression;

						AdvanceUInt64(ref offset);
						Log(opOffset, expression.Token, op.Value.ToString());

						break;
					}
				case EExprToken.EX_SkipOffsetConst:
					{
						EX_SkipOffsetConst op = (EX_SkipOffsetConst)expression;

						AdvanceInt32(ref offset);
						Log(opOffset, expression.Token, $"0x{op.Value:X}");

						break;
					}
				case EExprToken.EX_FloatConst:
					{
						EX_FloatConst op = (EX_FloatConst)expression;

						AdvanceSingle(ref offset);
						Log(opOffset, expression.Token, op.Value.ToString("0.0###"));

						break;
					}
				case EExprToken.EX_DoubleConst:
					{
						EX_DoubleConst op = (EX_DoubleConst)expression;

						AdvanceDouble(ref offset);
						Log(opOffset, expression.Token, op.Value.ToString("0.0###"));

						break;
					}
				case EExprToken.EX_StringConst:
					{
						EX_StringConst op = (EX_StringConst)expression;

						AdvanceAsciiString(op.Value, ref offset);
						Log(opOffset, expression.Token, op.Value);

						break;
					}
				case EExprToken.EX_UnicodeStringConst:
					{
						EX_UnicodeStringConst op = (EX_UnicodeStringConst)expression;

						AdvanceUnicodeString(op.Value, ref offset);
						Log(opOffset, expression.Token, op.Value);

						break;
					}
				case EExprToken.EX_TextConst:
					{
						EX_TextConst op = (EX_TextConst)expression;
						FScriptText text = op.Value;

						AdvanceByte(ref offset); // TextLiteralType

						List<string> values = new();
						switch (text.TextLiteralType)
						{
							case EBlueprintTextLiteralType.Empty:
								{
									Log(opOffset, expression.Token, "Empty");
								}
								break;

							case EBlueprintTextLiteralType.LocalizedText:
								{
									Log(opOffset, expression.Token, "Localized {");
									++mIndentLevel;

									Log("namespace: ");
									++mIndentLevel;
									ProcessExpr(text.Namespace!, finalFunctionMap, ref offset);
									--mIndentLevel;

									Log("key: ");
									++mIndentLevel;
									ProcessExpr(text.KeyString!, finalFunctionMap, ref offset);
									--mIndentLevel;

									Log("source: ");
									++mIndentLevel;
									ProcessExpr(text.SourceString!, finalFunctionMap, ref offset);
									--mIndentLevel;

									--mIndentLevel;
									Log("}");
								}
								break;

							case EBlueprintTextLiteralType.InvariantText:
								{
									Log(opOffset, expression.Token, "Invariant {");

									++mIndentLevel;
									ProcessExpr(text.SourceString!, finalFunctionMap, ref offset);
									--mIndentLevel;

									Log("}");
								}
								break;

							case EBlueprintTextLiteralType.LiteralString:
								{
									Log(opOffset, expression.Token, "Literal {");

									++mIndentLevel;
									ProcessExpr(text.SourceString!, finalFunctionMap, ref offset);
									--mIndentLevel;

									Log("}");
								}
								break;

							case EBlueprintTextLiteralType.StringTableEntry:
								{
									AdvanceObject(ref offset); // String table asset

									Log(opOffset, expression.Token, "String table entry {");
									++mIndentLevel;

									Log("tableid: ");
									++mIndentLevel;
									ProcessExpr(text.TableIdString!, finalFunctionMap, ref offset);
									--mIndentLevel;

									Log("key: ");
									++mIndentLevel;
									ProcessExpr(text.KeyString!, finalFunctionMap, ref offset);
									--mIndentLevel;

									--mIndentLevel;
									Log("}");
								}
								break;

							default:
								throw new FormatException($"Unexpected EBlueprintTextLiteralType value {text.TextLiteralType}");
						}

						break;
					}
				case EExprToken.EX_PropertyConst:
					{
						EX_PropertyConst op = (EX_PropertyConst)expression;

						AdvanceField(op.Property, ref offset);
						Log(opOffset, expression.Token, GetPath(op.Property));

						break;
					}
				case EExprToken.EX_ObjectConst:
					{
						EX_ObjectConst op = (EX_ObjectConst)expression;

						AdvanceObject(ref offset);
						Log(opOffset, expression.Token, op.Value.Name);

						break;
					}
				case EExprToken.EX_SoftObjectConst:
				case EExprToken.EX_FieldPathConst:
					{
						KismetExpression<KismetExpression> op = (KismetExpression<KismetExpression>)expression;

						Log(opOffset, expression.Token);
						ProcessExpr(op.Value, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_NameConst:
					{
						EX_NameConst op = (EX_NameConst)expression;

						AdvanceName(ref offset);
						Log(opOffset, expression.Token, op.Value.Text);

						break;
					}
				case EExprToken.EX_RotationConst:
					{
						EX_RotationConst op = (EX_RotationConst)expression;
						FRotator value = op.Value;

						AdvanceCoordinate(ref offset); // pitch
						AdvanceCoordinate(ref offset); // yaw
						AdvanceCoordinate(ref offset); // roll

						Log(opOffset, expression.Token, $"Pitch={value.Pitch:0.0###}, Yaw={value.Yaw:0.0###}, Roll={value.Roll:0.0###}");

						break;
					}
				case EExprToken.EX_VectorConst:
					{
						EX_VectorConst op = (EX_VectorConst)expression;
						FVector value = op.Value;

						AdvanceCoordinate(ref offset); // x
						AdvanceCoordinate(ref offset); // y
						AdvanceCoordinate(ref offset); // z

						Log(opOffset, expression.Token, $"X={value.X:0.0###}, Y={value.Y:0.0###}, Z={value.Z:0.0###}");

						break;
					}
				case EExprToken.EX_TransformConst:
					{
						EX_TransformConst op = (EX_TransformConst)expression;

						AdvanceCoordinate(ref offset); // rx
						AdvanceCoordinate(ref offset); // ry
						AdvanceCoordinate(ref offset); // rz
						AdvanceCoordinate(ref offset); // rw

						AdvanceCoordinate(ref offset); // x
						AdvanceCoordinate(ref offset); // y
						AdvanceCoordinate(ref offset); // z

						AdvanceCoordinate(ref offset); // sx
						AdvanceCoordinate(ref offset); // sy
						AdvanceCoordinate(ref offset); // sz

						Log(opOffset, expression.Token, op.Value.ToString());

						break;
					}
				case EExprToken.EX_StructConst:
					{
						EX_StructConst op = (EX_StructConst)expression;

						AdvanceObject(ref offset);
						AdvanceInt32(ref offset);

						Log(opOffset, expression.Token, $"{op.Struct.Name} (serialized size: {op.StructSize})");

						foreach (KismetExpression property in op.Properties)
						{
							ProcessExpr(property, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndStructConst

						break;
					}
				case EExprToken.EX_SetArray:
					{
						EX_SetArray op = (EX_SetArray)expression;

						Log(opOffset, expression.Token);

						if (mPackage.Summary.FileVersionUE >= EUnrealEngineObjectUE4Version.CHANGE_SETARRAY_BYTECODE)
						{
							ProcessExpr(op.AssigningProperty!, finalFunctionMap, ref offset);
						}
						else
						{
							Log(op.ArrayInnerProp!.Name);
						}

						foreach (KismetExpression element in op.Elements)
						{
							ProcessExpr(element, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndArray

						break;
					}
				case EExprToken.EX_ArrayConst:
					{
						EX_ArrayConst op = (EX_ArrayConst)expression;

						AdvanceField(op.InnerProperty, ref offset);
						AdvanceInt32(ref offset);

						Log(opOffset, expression.Token, $"Type: {GetPath(op.InnerProperty)}, Count: {op.Elements.Length}");

						foreach (KismetExpression element in op.Elements)
						{
							ProcessExpr(element, finalFunctionMap, ref offset);
						}
						AdvanceByte(ref offset); // EndArrayConst

						break;
					}
				case EExprToken.EX_ByteConst:
				case EExprToken.EX_IntConstByte:
					{
						KismetExpression<byte> op = (KismetExpression<byte>)expression;

						AdvanceByte(ref offset);
						Log(opOffset, expression.Token, op.Value.ToString());

						break;
					}
				case EExprToken.EX_MetaCast:
				case EExprToken.EX_DynamicCast:
					{
						EX_CastBase op = (EX_CastBase)expression;

						AdvanceObject(ref offset);
						Log(opOffset, expression.Token, $"Cast to {op.ClassPtr.Name} of expr:");

						ProcessExpr(op.Target, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_JumpIfNot:
					{
						EX_JumpIfNot op = (EX_JumpIfNot)expression;

						// Code offset.
						AdvanceInt32(ref offset);

						Log(opOffset, expression.Token, $"Offset: 0x{op.CodeOffset:X}, Condition:");

						// Boolean expr.
						ProcessExpr(op.BooleanExpression, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_Assert:
					{
						EX_Assert op = (EX_Assert)expression;

						AdvanceUInt16(ref offset); // line number
						AdvanceByte(ref offset); // debug mode

						Log(opOffset, expression.Token, $"Line {op.LineNumber}, in debug mode = {op.DebugMode} with expr:");

						ProcessExpr(op.AssertExpression, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_Skip:
					{
						EX_Skip op = (EX_Skip)expression;

						AdvanceInt32(ref offset);
						Log(opOffset, expression.Token, $"Possibly skip {op.CodeOffset} bytes of expr:");

						ProcessExpr(op.SkipExpression, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_InstanceDelegate:
					{
						EX_InstanceDelegate op = (EX_InstanceDelegate)expression;

						AdvanceName(ref offset);

						Log(opOffset, expression.Token, op.FunctionName.Text);

						break;
					}
				case EExprToken.EX_AddMulticastDelegate:
					{
						EX_AddMulticastDelegate op = (EX_AddMulticastDelegate)expression;

						Log(opOffset, expression.Token);

						ProcessExpr(op.Delegate, finalFunctionMap, ref offset);
						ProcessExpr(op.DelegateToAdd, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_RemoveMulticastDelegate:
					{
						EX_RemoveMulticastDelegate op = (EX_RemoveMulticastDelegate)expression;

						Log(opOffset, expression.Token);

						ProcessExpr(op.Delegate, finalFunctionMap, ref offset);
						ProcessExpr(op.DelegateToAdd, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_ClearMulticastDelegate:
					{
						EX_ClearMulticastDelegate op = (EX_ClearMulticastDelegate)expression;

						Log(opOffset, expression.Token);

						ProcessExpr(op.DelegateToClear, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_BindDelegate:
					{
						EX_BindDelegate op = (EX_BindDelegate)expression;

						AdvanceName(ref offset);

						Log(opOffset, expression.Token, op.FunctionName.Text);

						++mIndentLevel;

						Log("Delegate:");
						ProcessExpr(op.Delegate, finalFunctionMap, ref offset);

						Log("Object:");
						ProcessExpr(op.ObjectTerm, finalFunctionMap, ref offset);

						--mIndentLevel;

						break;
					}
				case EExprToken.EX_PushExecutionFlow:
					{
						EX_PushExecutionFlow op = (EX_PushExecutionFlow)expression;

						AdvanceInt32(ref offset);
						Log(opOffset, expression.Token, $"FlowStack.Push(0x{op.PushingAddress:X})");

						break;
					}
				case EExprToken.EX_PopExecutionFlow:
					{
						EX_PopExecutionFlow op = (EX_PopExecutionFlow)expression;

						Log(opOffset, expression.Token, "Jump to statement at FlowStack.Pop()");

						if (mIndentLevel == 0)
						{
							mWriter.WriteLine(BlockSeparator);
						}
						break;
					}
				case EExprToken.EX_PopExecutionFlowIfNot:
					{
						EX_PopExecutionFlowIfNot op = (EX_PopExecutionFlowIfNot)expression;

						Log(opOffset, expression.Token, "Jump to statement at FlowStack.Pop(). Condition:");

						ProcessExpr(op.BooleanExpression, finalFunctionMap, ref offset);

						break;
					}
				case EExprToken.EX_Breakpoint:
				case EExprToken.EX_WireTracepoint:
				case EExprToken.EX_Tracepoint:
					{
						Log(opOffset, expression.Token);
						break;
					}
				case EExprToken.EX_InstrumentationEvent:
					{
						EX_InstrumentationEvent op = (EX_InstrumentationEvent)expression;

						AdvanceByte(ref offset);
						if (op.EventType == EScriptInstrumentationType.InlineEvent)
						{
							AdvanceName(ref offset); // event name, not serialized
						}
						++offset; // There is always a byte skipped here when executing this code

						Log(opOffset, expression.Token, op.EventType.ToString());

						break;
					}
				case EExprToken.EX_SwitchValue:
					{
						EX_SwitchValue op = (EX_SwitchValue)expression;

						AdvanceUInt16(ref offset); // num cases
						AdvanceInt32(ref offset); // after offset

						Log(opOffset, expression.Token, $"{op.Cases.Length} cases, end at 0x{op.EndGotoOffset:X}");

						++mIndentLevel;

						Log("Index:");
						ProcessExpr(op.IndexTerm, finalFunctionMap, ref offset);

						for (ushort caseIndex = 0; caseIndex < op.Cases.Length; ++caseIndex)
						{
							Log($"Case {caseIndex}:");
							ProcessExpr(op.Cases[caseIndex].CaseIndexValueTerm, finalFunctionMap, ref offset);

							++mIndentLevel;

							AdvanceInt32(ref offset);
							Log($"Offset of next case: 0x{op.Cases[caseIndex].NextOffset}");

							Log("Case Term:");
							ProcessExpr(op.Cases[caseIndex].CaseTerm, finalFunctionMap, ref offset); // case term

							--mIndentLevel;
						}

						Log($"Default term:");
						ProcessExpr(op.DefaultTerm, finalFunctionMap, ref offset);

						--mIndentLevel;

						break;
					}
				case EExprToken.EX_ArrayGetByRef:
					{
						EX_ArrayGetByRef op = (EX_ArrayGetByRef)expression;

						Log(opOffset, expression.Token);

						++mIndentLevel;

						ProcessExpr(op.ArrayVariable, finalFunctionMap, ref offset);
						ProcessExpr(op.ArrayIndex, finalFunctionMap, ref offset);

						--mIndentLevel;

						break;
					}
				default:
					throw new NotImplementedException($"Op code {expression.Token} not implemented in disassembler");
			}

			--mIndentLevel;
			return expression.Token;
		}

		private void Log(int offset, EExprToken opcode)
		{
			mWriter.WriteLine($"{offset:X8}  {new string(' ', mIndentLevel * 2)}[{(int)opcode:X2}] {TrimEnum(opcode)}");
		}

		private void Log(string? message)
		{
			mWriter.WriteLine($"          {new string(' ', mIndentLevel * 2)}{message}");
		}

		private void Log(int offset, EExprToken opcode, string? message)
		{
			mWriter.WriteLine($"{offset:X8}  {new string(' ', mIndentLevel * 2)}[{(int)opcode:X2}] {TrimEnum(opcode)}: {message}");
		}

		private void Log(int offset, EExprToken opcode, string opCodeName, string? message)
		{
			mWriter.WriteLine($"{offset:X8}  {new string(' ', mIndentLevel * 2)}[{(int)opcode:X2}] {opCodeName}: {message}");
		}

		private string? GetPath(FKismetPropertyPointer property)
		{
			if (property.New is null) return null;
			return string.Join('.', property.New.Path.Select(n => n.Text));
		}

		private string GetPath(FPackageIndex packageIndex)
		{
			if (mPackage is Package package)
			{
				FObjectResource resource;
				if (packageIndex.Index < 0) resource = package.ImportMap[~packageIndex.Index];
				else resource = package.ExportMap[packageIndex.Index - 1];

				string path1 = resource.ObjectName.Text;
				if (resource.OuterIndex is not null)
				{
					path1 = $"{resource.OuterIndex.Name}::{path1}";
				}
				return path1;
			}

			string path = packageIndex.ResolvedObject!.Name.Text;

			if (packageIndex.ResolvedObject?.Outer is not null)
			{
				path = $"{packageIndex.ResolvedObject.Outer.Name.Text}::{path}";
			}
			return path;
		}

		private void AdvanceByte(ref int offset)
		{
			++offset;
		}

		private void AdvanceUInt16(ref int offset)
		{
			offset += 2;
		}

		private void AdvanceInt32(ref int offset)
		{
			offset += 4;
		}

		private void AdvanceInt64(ref int offset)
		{
			offset += 8;
		}

		private void AdvanceUInt64(ref int offset)
		{
			offset += 8;
		}

		private void AdvanceSingle(ref int offset)
		{
			offset += 4;
		}

		private void AdvanceDouble(ref int offset)
		{
			offset += 8;
		}

		private void AdvanceCoordinate(ref int offset)
		{
			offset += mLargeWorldCoordinates ? 8 : 4;
		}

		private void AdvanceField(FKismetPropertyPointer field, ref int offset)
		{
			offset += 8;

			// There are names here, but they do not affect the script offset.
			// Keeping this here commented out for informational purposes.
			//for (int i = 0; i < (field.New?.Path.Count ?? 0); ++i)
			//{
			//	AdvanceName(ref offset);
			//}
		}

		private void AdvanceName(ref int offset)
		{
			offset += 12;
		}

		private void AdvanceAsciiString(string value, ref int offset)
		{
			offset += value.Length + 1;
		}

		private void AdvanceUnicodeString(string value, ref int offset)
		{
			offset += (value.Length + 1) * 2;
		}

		private void AdvanceObject(ref int offset)
		{
			offset += 8;
		}

		private static string TrimEnum(Enum value)
		{
			string raw = value.ToString();
			int index = raw.IndexOf('_') + 1;
			return raw[index..];
		}
	}

	internal struct LocalFinalFunctionData
	{
		public string Name;
		public EX_LocalFinalFunction Location;
	}

	// From Script.h
	internal enum ECastToken
	{
		ObjectToInterface = 0x46,
		ObjectToBool = 0x47,
		InterfaceToBool = 0x49,
		Max = 0xFF,
	};
}
