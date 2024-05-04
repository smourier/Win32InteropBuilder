using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder.Languages
{
    public class CSharpLanguage : ILanguage
    {
        public string Name => "CSharp";
        public string FileExtension => ".cs";
        public CSharpLanguageConfiguration Configuration { get; private set; } = new();
        public IReadOnlyCollection<FullName> ConstableTypes => _constableTypes;

        private const string IntPtrTypeName = "nint";
        private const string UIntPtrTypeName = "nuint";

        public override string ToString() => Name;
        public virtual void Configure(JsonElement element)
        {
            Configuration = element.Deserialize<CSharpLanguageConfiguration>(Builder.JsonSerializerOptions) ?? new();

            // supported constants types
            if (Configuration.SupportedConstantTypes.Any(b => b.MatchesEverything))
            {
                Configuration.SupportedConstantTypes.Clear();
            }
            else
            {
                // add all c# const
                foreach (var fn in new CSharpLanguage().ConstableTypes)
                {
                    Configuration.AddSupportedConstantType(fn);
                }
                Configuration.AddSupportedConstantType(WellKnownTypes.SystemIntPtr.FullName);
                Configuration.AddSupportedConstantType(WellKnownTypes.SystemUIntPtr.FullName);
                Configuration.AddSupportedConstantType(WellKnownTypes.SystemGuid.FullName);

                // now remove all reverse we just keep an inclusion list
                Configuration.ClearSupportedConstantTypeReverses();
            }

            // marshal as error types
            Configuration.AddMarshalAsErrorType(FullName.HRESULT);
            Configuration.ClearMarshalAsErrorTypeReverses();
        }

        protected virtual string GetValueAsString(BuilderContext context, BuilderType type, object? value)
        {
            var valueAsString = GetDefaultValueAsString(context, type, value);
            return context.GetValueAsString(type, value, valueAsString);
        }

        protected virtual string GetDefaultValueAsString(BuilderContext context, BuilderType type, object? value)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(type);
            if (value == null)
                return "null";

            if (value is Guid guid)
                return $"new(\"{guid}\")";

            if (value is bool b)
                return b ? "true" : "false";

            if (value is string s)
                return $"@\"{s.Replace("\"", "\"\"")}\"";

            if (value is char c)
                return $"'\\u{(ushort)c:x4}'";

            if (value is byte[] bytes)
                return $"[{string.Join(", ", bytes)}]";

            if (value is Constant ct)
                return context.GetConstantValue(type, ct);

            if (type.ClrType != null)
            {
                value = Conversions.ChangeType(value, type.ClrType, value, CultureInfo.InvariantCulture);
            }

            if (value is sbyte sb)
            {
                if (sb == sbyte.MinValue)
                    return "sbyte.MinValue";

                if (sb == sbyte.MaxValue)
                    return "sbyte.MaxValue";
            }

            if (value is short sh)
            {
                if (sh == short.MinValue)
                    return "short.MinValue";

                if (sh == short.MaxValue)
                    return "short.MaxValue";
            }

            if (value is int i)
            {
                if (i == int.MinValue)
                    return "int.MinValue";

                if (i == int.MaxValue)
                    return "int.MaxValue";
            }

            if (value is long l)
            {
                if (l == long.MinValue)
                    return "long.MinValue";

                if (l == long.MaxValue)
                    return "long.MaxValue";
            }

            if (value is float f)
            {
                if (f == float.MinValue)
                    return "float.MinValue";

                if (f == float.MaxValue)
                    return "float.MaxValue";
            }

            if (value is double d)
            {
                if (d == double.MinValue)
                    return "double.MinValue";

                if (d == float.MaxValue)
                    return "double.MaxValue";
            }

            if (value is byte by && by == byte.MaxValue)
                return "byte.MaxValue";

            if (value is ushort u && u == ushort.MaxValue)
                return "ushort.MaxValue";

            if (value is uint ui && ui == uint.MaxValue)
                return "uint.MaxValue";

            if (value is ulong ul && ul == ulong.MaxValue)
                return "ulong.MaxValue";

            var str = string.Format(CultureInfo.InvariantCulture, "{0}", value);
            if (value is float)
                return str + "f";

            if (value is double)
                return str + "d";

            if (value is decimal)
                return str + "m";
            return str;
        }

        [return: NotNullIfNotNull(nameof(name))]
        protected virtual string? GetIdentifier(string? name)
        {
            if (name == null)
                return null;

            if (name.Contains('.'))
            {
                var split = name.Split('.');
                return string.Join('.', split.Select(s => GetIdentifier(s)));
            }

            if (_keywords.Contains(name))
                return "@" + name;

            return name;
        }

        [return: NotNullIfNotNull(nameof(fullName))]
        protected virtual string GetTypeReferenceName(string fullName)
        {
            ArgumentNullException.ThrowIfNull(fullName);
            if (Configuration.GenerateTypeKeywords && _typeKeywords.TryGetValue(fullName, out var keyword))
                return keyword;

            return fullName;
        }

        public virtual void GenerateCode(BuilderContext context, BuilderType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.Generation);
            ArgumentNullException.ThrowIfNull(type);

            string ns;
            if (GetType() == typeof(BuilderType))
            {
                // it's not structure, interface or enum
                // so it's not unified functions or constants
                // so, don't map namespace
                ns = type.Namespace;
            }
            else
            {
                ns = context.MapGeneratedFullName(type.FullName).Namespace;
            }

            ns = GetIdentifier(ns);
            context.CurrentWriter.WriteLine($"namespace {ns};");
            context.CurrentWriter.WriteLine();
            context.CurrentNamespace = ns;

            if (type.Documentation != null)
            {
                context.CurrentWriter.WriteLine("// " + type.Documentation);
            }

            GenerateTypeCode(context, type);
            context.CurrentNamespace = null;
        }

        protected virtual void GenerateTypeCode(BuilderContext context, BuilderType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);

            if (type is EnumType enumType)
            {
                GenerateTypeCode(context, enumType);
                return;
            }

            if (type is InterfaceType interfaceType)
            {
                GenerateTypeCode(context, interfaceType);
                return;
            }

            if (type is DelegateType delegateType)
            {
                GenerateTypeCode(context, delegateType);
                return;
            }

            if (type is InlineArrayType inlineArrayType)
            {
                GenerateTypeCode(context, inlineArrayType);
                return;
            }

            if (type is StructureType structureType)
            {
                GenerateTypeCode(context, structureType);
                return;
            }

            var un = context.Configuration.GetUnifiedGeneration();

            string? staticText = null;
            if (type.TypeAttributes.HasFlag(TypeAttributes.Sealed) && type.TypeAttributes.HasFlag(TypeAttributes.Abstract))
            {
                staticText = "static ";
            }

            context.CurrentWriter.Write($"public {staticText}partial class {GetIdentifier(type.GetGeneratedName(context))}");
            context.CurrentWriter.WriteLine();
            context.CurrentWriter.WithParens(() =>
            {
                // we're here mostly for constants.cs (if configured for unified namespace)
                for (var i = 0; i < type.Fields.Count; i++)
                {
                    var field = type.Fields[i];
                    if (field.Type == null)
                        throw new InvalidOperationException();

                    if (!Configuration.IsSupportedAsConstant(field.Type.FullName))
                        continue;

                    if (field.Documentation != null)
                    {
                        context.CurrentWriter.WriteLine("// " + field.Documentation);
                    }

                    var mapped = context.MapType(field.Type);
                    if (mapped.UnmanagedType.HasValue)
                    {
                        context.CurrentWriter.WriteLine($"[MarshalAs(UnmanagedType.{mapped.UnmanagedType.Value})]");
                    }

                    var constText = field.Attributes.HasFlag(FieldAttributes.Literal) && context.IsConstableType(field.Type) ? "const" : "static readonly";
                    context.CurrentWriter.WriteLine($"public {constText} {GetTypeReferenceName(mapped.GetGeneratedName(context))} {GetIdentifier(field.Name)} = {GetValueAsString(context, field.Type, field.DefaultValue)};");

                    if (i != type.Fields.Count - 1 || type.Methods.Count > 0)
                    {
                        context.CurrentWriter.WriteLine();
                    }
                }

                var patch = context.Configuration.GetTypePatch(type);

                // we're here mostly for functions.cs (if configured for unified namespace)
                for (var i = 0; i < type.Methods.Count; i++)
                {
                    var method = type.Methods[i];
                    if (method.Handle.HasValue)
                    {
                        if (type.ExcludedMethods.Contains(method.Handle.Value))
                            continue;

                        if (type.IncludedMethods.Count > 0 && !type.IncludedMethods.Contains(method.Handle.Value))
                            continue;
                    }

                    GenerateCode(context, type, patch, method);
                    if (i != type.Methods.Count - 1)
                    {
                        context.CurrentWriter.WriteLine();
                    }
                }
            });
        }

        protected virtual void GenerateTypeCode(BuilderContext context, StructureType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);

            if (type.SupportedOSPlatform != null)
            {
                context.CurrentWriter.WriteLine($"[SupportedOSPlatform(\"{type.SupportedOSPlatform}\")]");
            }

            LayoutKind? lk;
            if (type.TypeAttributes.HasFlag(TypeAttributes.SequentialLayout))
            {
                lk = LayoutKind.Sequential;
            }
            else if (type.TypeAttributes.HasFlag(TypeAttributes.ExplicitLayout))
            {
                lk = LayoutKind.Explicit;
            }
            else
            {
                lk = null;
            }

            if (lk.HasValue)
            {
                context.CurrentWriter.WriteLine($"[StructLayout(LayoutKind.{lk})]");
            }

            var ns = type.FullName.NestedName;
            string strucTypeName;
            if (ns != null)
            {
                strucTypeName = GetIdentifier(ns);
                context.CurrentWriter.Write($"public struct {strucTypeName}");
            }
            else
            {
                strucTypeName = GetIdentifier(type.GetGeneratedName(context));
                context.CurrentWriter.Write($"public partial struct {strucTypeName}");
            }

            context.CurrentWriter.WriteLine();
            context.CurrentWriter.WithParens(() =>
            {
                if (context.Configuration.Generation.AddNullToIntPtrValueTypes &&
                    type.Fields.Count == 1 &&
                    (type.Fields[0].Type == WellKnownTypes.SystemIntPtr || type.Fields[0].Type == WellKnownTypes.SystemUIntPtr))
                {
                    context.CurrentWriter.WriteLine($"public static readonly {strucTypeName} Null = new();");
                    context.CurrentWriter.WriteLine();
                }

                for (var i = 0; i < type.NestedTypes.Count; i++)
                {
                    var nt = type.NestedTypes[i];
                    GenerateTypeCode(context, nt);
                    if (i <= type.NestedTypes.Count || type.Fields.Count > 0)
                    {
                        context.CurrentWriter.WriteLine();
                    }
                }

                for (var i = 0; i < type.Fields.Count; i++)
                {
                    var field = type.Fields[i];
                    if (field.Type == null)
                        throw new InvalidOperationException();

                    var mapped = context.MapType(field.Type);

                    var addSep = mapped.UnmanagedType.HasValue || field.Offset.HasValue;
                    if (addSep && i > 0)
                    {
                        context.CurrentWriter.WriteLine();
                    }

                    if (mapped.UnmanagedType.HasValue)
                    {
                        context.CurrentWriter.WriteLine($"[MarshalAs(UnmanagedType.{mapped.UnmanagedType.Value})]");
                    }

                    if (field.Offset.HasValue)
                    {
                        context.CurrentWriter.WriteLine($"[FieldOffset({field.Offset.Value})]");
                    }

                    if (mapped is InterfaceType || mapped is DelegateType)
                    {
                        context.CurrentWriter.Write($"public {IntPtrTypeName} {GetIdentifier(field.Name)};");
                    }
                    else
                    {
                        var typeName = GetTypeReferenceName(mapped.GetGeneratedName(context));
                        if (mapped.Indirections > 0)
                        {
                            typeName = IntPtrTypeName;
                        }
                        context.CurrentWriter.Write($"public {typeName} {GetIdentifier(field.Name)};");
                    }

                    if (field.IsFlexibleArray)
                    {
                        context.CurrentWriter.Write(" // variable-length array placeholder");
                    }
                    context.CurrentWriter.WriteLine();
                }
            });
        }

        protected virtual void GenerateTypeCode(BuilderContext context, DelegateType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);

            var patch = context.Configuration.GetTypePatch(type);

            // should be only 2 methods: ctor & invoke
            for (var i = 0; i < type.Methods.Count; i++)
            {
                var method = type.Methods[i];
                if (method.Attributes.HasFlag(MethodAttributes.SpecialName))
                    continue;

                var cc = type.CallingConvention ?? CallingConvention.Winapi;
                context.CurrentWriter.WriteLine($"[UnmanagedFunctionPointer(CallingConvention.{cc})]");

                string typeName;
                if (method.ReturnType != null)
                {
                    var mapped = context.MapType(method.ReturnType);

                    if (mapped is InterfaceType)
                    {
                        typeName = IntPtrTypeName;
                    }
                    else
                    {
                        typeName = mapped.GetGeneratedName(context);
                    }
                }
                else
                {
                    typeName = "void";
                }

                context.CurrentWriter.Write($"public delegate {GetTypeReferenceName(typeName)} {GetIdentifier(type.GetGeneratedName(context))}(");
                for (var j = 0; j < method.Parameters.Count; j++)
                {
                    var parameter = method.Parameters[j];

                    if (parameter.Attributes.HasFlag(ParameterAttributes.Out))
                    {
                        parameter.Type = WellKnownTypes.SystemIntPtr;
                        parameter.Attributes &= ~ParameterAttributes.Out;
                    }

                    var methodPatch = patch?.Methods?.FirstOrDefault(m => m.Matches(method));
                    GenerateCode(context, type, method, methodPatch, parameter, j);
                    if (j != method.Parameters.Count - 1)
                    {
                        context.CurrentWriter.Write(", ");
                    }
                }
                context.CurrentWriter.WriteLine(");");

                if (i != type.Methods.Count - 1)
                {
                    context.CurrentWriter.WriteLine();
                }
            }
        }

        protected virtual void GenerateTypeCode(BuilderContext context, InlineArrayType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);

            context.CurrentWriter.WriteLine($"[InlineArray({type.Size})]");
            context.CurrentWriter.WriteLine($"public partial struct {GetIdentifier(type.GetGeneratedName(context))}");
            context.CurrentWriter.WithParens(() =>
            {
                var elementName = type.ElementName ?? "Data";
                var typeName = GetTypeReferenceName(type.ElementType.GetGeneratedName(context));
                context.CurrentWriter.WriteLine($"public {typeName} {elementName};");

                if (typeName == "char")
                {
                    context.CurrentWriter.WriteLine();
                    context.CurrentWriter.WriteLine($"public override readonly string ToString() => ((ReadOnlySpan<char>)this).ToString();");
                }
            });
        }

        protected virtual void GenerateTypeCode(BuilderContext context, InterfaceType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);

            if (type.SupportedOSPlatform != null)
            {
                context.CurrentWriter.WriteLine($"[SupportedOSPlatform(\"{type.SupportedOSPlatform}\")]");
            }

            context.CurrentWriter.WriteLine($"[GeneratedComInterface, Guid(\"{type.Guid.GetValueOrDefault()}\")]");
            context.CurrentWriter.Write($"public partial interface {GetIdentifier(type.GetGeneratedName(context))}");

            if (type.Interfaces.Count > 0)
            {
                context.CurrentWriter.Write($" : {string.Join(", ", type.Interfaces.Select(i => GetIdentifier(i.GetGeneratedName(context))))}");
            }

            context.CurrentWriter.WriteLine();
            context.CurrentWriter.WithParens(() =>
            {
                var patch = context.Configuration.GetTypePatch(type);

                for (var i = 0; i < type.Methods.Count; i++)
                {
                    var method = type.Methods[i];
                    GenerateCode(context, type, patch, method);
                    if (i != type.Methods.Count - 1)
                    {
                        context.CurrentWriter.WriteLine();
                    }
                }
            });
        }

        protected virtual void GenerateTypeCode(BuilderContext context, EnumType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);

            if (type.SupportedOSPlatform != null)
            {
                context.CurrentWriter.WriteLine($"[SupportedOSPlatform(\"{type.SupportedOSPlatform}\")]");
            }

            if (type.IsFlags)
            {
                context.CurrentWriter.WriteLine("[Flags]");
            }

            context.CurrentWriter.Write($"public enum {GetIdentifier(type.GetGeneratedName(context))}");
            if (type.UnderlyingType != null)
            {
                var typeName = GetTypeReferenceName(type.UnderlyingType.GetGeneratedName(context));
                if (typeName != "int")
                {
                    context.CurrentWriter.Write($" : {typeName}");
                }
            }

            context.CurrentWriter.WriteLine();
            context.CurrentWriter.WithParens(() =>
            {
                for (var i = 0; i < type.Fields.Count; i++)
                {
                    var field = type.Fields[i];
                    context.CurrentWriter.Write(GetIdentifier(field.Name));

                    if (field.DefaultValue != null)
                    {
                        context.CurrentWriter.Write(" = ");
                        context.CurrentWriter.Write(GetValueAsString(context, type.UnderlyingType ?? WellKnownTypes.SystemInt32, field.DefaultValue));
                    }
                    context.CurrentWriter.WriteLine(',');
                }
            });
        }

        protected virtual void GenerateCode(BuilderContext context, BuilderType type, BuilderPatchType? patch, BuilderMethod method)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(method);

            if (method.Documentation != null)
            {
                context.CurrentWriter.WriteLine("// " + method.Documentation);
            }

            if (method.ImportModuleName != null)
            {
                var module = method.ImportModuleName;
                const string dll = ".dll";
                if (module.EndsWith(dll, StringComparison.InvariantCultureIgnoreCase))
                {
                    module = module[..^dll.Length];
                }

                context.CurrentWriter.Write($"[LibraryImport(\"{module}\"");
                if (method.IsUnicode)
                {
                    context.CurrentWriter.Write(", StringMarshalling = StringMarshalling.Utf16");
                }
                context.CurrentWriter.WriteLine(")]");
            }

            if (method.SupportedOSPlatform != null)
            {
                context.CurrentWriter.WriteLine($"[SupportedOSPlatform(\"{method.SupportedOSPlatform}\")]");
            }

            context.CurrentWriter.WriteLine("[PreserveSig]");
            string returnTypeName;
            if (method.ReturnType != null && method.ReturnType != WellKnownTypes.SystemVoid)
            {
                var mapped = context.MapType(method.ReturnType);
                var um = mapped.UnmanagedType;
                if (Configuration.MarshalAsError(mapped.FullName))
                {
                    um = UnmanagedType.Error;
                }

                if (um.HasValue &&
                    (!method.Attributes.HasFlag(MethodAttributes.Static) || mapped == WellKnownTypes.SystemBoolean))
                {
                    context.CurrentWriter.WriteLine($"[return: MarshalAs(UnmanagedType.{um.Value})]");
                }
                returnTypeName = GetTypeReferenceName(mapped.GetGeneratedName(context));
            }
            else
            {
                returnTypeName = "void";
            }

            string? staticText = null;
            if (method.Attributes.HasFlag(MethodAttributes.Static))
            {
                staticText = "static partial ";
            }

            var methodName = GetIdentifier(method.Name);
            string? comments = null;
            foreach (var iface in type.AllInterfaces)
            {
                var existing = iface.Methods.FirstOrDefault(m => HaveSameSignature(context, type, m, method));
                if (existing != null)
                {
                    methodName = type.Name + "_" + methodName;
                    comments = " // renamed, see https://github.com/dotnet/runtime/issues/101240";
                }
            }

            string? publicText = null;
            if (type is not InterfaceType)
            {
                publicText = "public ";
            }

            returnTypeName = GetTypeReferenceName(returnTypeName);

            // patch
            var methodPatch = patch?.Methods?.FirstOrDefault(m => m.Matches(method));
            if (methodPatch != null)
            {
                if (!string.IsNullOrEmpty(methodPatch.TypeName))
                {
                    returnTypeName = methodPatch.TypeName;
                }

                if (!string.IsNullOrEmpty(methodPatch.NewName))
                {
                    methodName = methodPatch.NewName;
                }
            }

            method.SetExtendedValue(nameof(returnTypeName), returnTypeName);
            method.SetExtendedValue(nameof(methodName), methodName);

            context.CurrentWriter.Write($"{publicText}{staticText}{returnTypeName} {methodName}(");
            for (var j = 0; j < method.Parameters.Count; j++)
            {
                var parameter = method.Parameters[j];
                GenerateCode(context, type, method, methodPatch, parameter, j);
                if (j != method.Parameters.Count - 1)
                {
                    context.CurrentWriter.Write(", ");
                }
            }
            context.CurrentWriter.WriteLine($");{comments}");
        }

        protected virtual bool HaveSameSignature(BuilderContext context, BuilderType type, BuilderMethod method1, BuilderMethod method2)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(method1);
            ArgumentNullException.ThrowIfNull(method2);
            if (method1 == method2)
                return true;

            if (method1.Name != method2.Name)
                return false;

            if (method1.Parameters.Count != method2.Parameters.Count)
                return false;

            for (var i = 0; i < method1.Parameters.Count; i++)
            {
                // hopefully context are similar
                var def1 = GetParameterDef(context, type, method1.Parameters[i]);
                var def2 = GetParameterDef(context, type, method2.Parameters[i]);
                if (def1.TypeName != def2.TypeName || def1.Direction != def2.Direction)
                    return false;
            }
            return true;
        }

        protected virtual ParameterDef GetParameterDef(BuilderContext context, BuilderType type, BuilderParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(type);
            if (parameter.Type == null)
                throw new InvalidOperationException();

            var def = new ParameterDef();
            var mapped = context.MapType(parameter.Type);
            def.TypeName = GetTypeReferenceName(mapped.GetGeneratedName(context));
            var isUnknownComOutPtr = parameter.IsComOutPtr && (parameter.Type == WellKnownTypes.SystemVoid || parameter.Type == WellKnownTypes.SystemObject);
            if (isUnknownComOutPtr)
            {
                var copTarget = context.Configuration.Generation.ComOutPtrTarget;
                switch (copTarget)
                {
                    case ComOutPtrTarget.Object:
                        return context.GetParameterDef(parameter, new ParameterDef
                        {
                            Direction = ParameterDirection.Out,
                            TypeName = "object",
                            MarshalAs = new ParameterMarshalAs { UnmanagedType = UnmanagedType.Interface },
                            Comments = $" /* {def.TypeName} */"
                        });

                    case ComOutPtrTarget.UniqueObject:
                        return context.GetParameterDef(parameter, new ParameterDef
                        {
                            Direction =
                            ParameterDirection.Out,
                            TypeName = "object",
                            MarshalUsing = new ParameterMarshalUsing { TypeName = "UniqueComInterfaceMarshaller<object>" },
                            Comments = $" /* {def.TypeName} */"
                        });

                    // case ComOutPtrTarget.IntPtr
                    default:
                        return context.GetParameterDef(parameter, def);
                }
            }

            if (parameter.Attributes.HasFlag(ParameterAttributes.Out))
            {
                if (parameter.Attributes.HasFlag(ParameterAttributes.In))
                {
                    def.Direction = ParameterDirection.Ref;
                }
                else
                {
                    def.Direction = ParameterDirection.Out;
                }
            }

            if (parameter.UnmanagedType.HasValue)
            {
                def.MarshalAs = new ParameterMarshalAs { UnmanagedType = parameter.UnmanagedType.Value };
            }
            else if (mapped.UnmanagedType.HasValue)
            {
                def.MarshalAs = new ParameterMarshalAs { UnmanagedType = mapped.UnmanagedType.Value };
            }

            if (!def.Direction.HasValue && mapped.Indirections > 0 && def.TypeName != IntPtrTypeName && def.TypeName != UIntPtrTypeName)
            {
                def.Direction = ParameterDirection.In;
            }

            var optionalPtr = parameter.Attributes.HasFlag(ParameterAttributes.Optional) & parameter.Type.Indirections > 0;
            if (optionalPtr)
            {
                def.Comments = $" /* optional {def.TypeName}{new string('*', parameter.Type.Indirections)} */";
                def.TypeName = IntPtrTypeName;
                def.MarshalAs = null;
                def.Direction = null;
                return context.GetParameterDef(parameter, def);
            }

            if (def.TypeName == "void" && mapped.Indirections > 0)
            {
                def.TypeName = IntPtrTypeName;
                def.MarshalAs = null;
                if (mapped.Indirections == 1)
                {
                    def.Direction = null;
                }
                else
                {
                    def.Direction = ParameterDirection.Out;
                }
                return context.GetParameterDef(parameter, def);
            }

            if (def.TypeName == "byte" && parameter.Type.Indirections > 0)
            {
                def.TypeName = IntPtrTypeName;
                def.Comments = " /* byte array */";
                def.MarshalAs = null;
                if (parameter.Type.Indirections == 1)
                {
                    def.Direction = null;
                }
                else
                {
                    def.Direction = ParameterDirection.Out;
                }
                return context.GetParameterDef(parameter, def);
            }

            // there are currently big limits to delegate marshaling
            if (type is DelegateType)
            {
                if (def.Direction.HasValue || parameter.Type is InterfaceType || parameter.Type is DelegateType)
                {
                    if (def.Direction.HasValue)
                    {
                        def.Comments = $" /* {def.Direction.Value.ToString().ToLowerInvariant()} {def.TypeName} */";
                    }
                    else
                    {
                        def.Comments = $" /* {def.TypeName} */";
                    }
                    def.Direction = null;
                    def.TypeName = IntPtrTypeName;
                }
                return context.GetParameterDef(parameter, def);
            }

            if (parameter.NativeArray != null)
            {
                // see https://github.com/dotnet/runtime/discussions/101494
                if (def.TypeName == "bool")
                {
                    def.MarshalAs = new ParameterMarshalAs { UnmanagedType = UnmanagedType.LPArray, ArraySubType = UnmanagedType.U4 };
                }
                else
                {
                    def.MarshalAs = null;
                }

                def.TypeName += "[]";
                if (parameter.NativeArray.CountParameter != null)
                {
                    def.MarshalUsing = new ParameterMarshalUsing { CountElementName = parameter.NativeArray.CountParameter.Name };
                    if (!def.Direction.HasValue)
                    {
                        def.IsOut = true;
                        def.IsIn = true;
                    }
                }
                else if (parameter.NativeArray.CountConst.HasValue)
                {
                    def.MarshalUsing = new ParameterMarshalUsing { ConstantElementCount = parameter.NativeArray.CountConst.Value };
                    if (!def.Direction.HasValue)
                    {
                        def.IsOut = true;
                        def.IsIn = true;
                    }
                }
                else
                {
                    if (def.Direction == ParameterDirection.Out)
                    {
                        def.TypeName = IntPtrTypeName;
                    }
                    else
                        throw new NotSupportedException();
                }
                return context.GetParameterDef(parameter, def);
            }

            if (def.Direction == ParameterDirection.Out && def.TypeName == "object" && def.MarshalUsing == null && def.MarshalAs == null)
            {
                def.TypeName = IntPtrTypeName;
                return context.GetParameterDef(parameter, def);
            }

            if (def.Direction == ParameterDirection.Out && parameter.Type.Indirections > 1)
            {
                def.TypeName = IntPtrTypeName;
                return context.GetParameterDef(parameter, def);
            }

            var isOptional = parameter.Attributes.HasFlag(ParameterAttributes.Optional);
            if (isOptional && def.TypeName != IntPtrTypeName)
            {
                def.TypeName += "?";
            }

            return context.GetParameterDef(parameter, def);
        }

        protected virtual void GenerateCode(BuilderContext context, BuilderType type, BuilderMethod method, BuilderPatchMember? methodPatch, BuilderParameter parameter, int parameterIndex)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(parameter);
            if (parameter.Type == null)
                throw new InvalidOperationException();

            var def = GetParameterDef(context, type, parameter);

            var defPatch = methodPatch?.Parameters?.FirstOrDefault(p => p.Matches(parameter, parameterIndex))?.Def;
            if (defPatch != null)
            {
                def.PatchFrom(defPatch);
            }

            var inAtt = def.IsIn == true ? "[In]" : null;
            var outAtt = def.IsOut == true ? "[Out]" : null;
            string? marshalAs = null;
            string? marshalUsing = null;
            if (def.MarshalAs != null)
            {
                marshalAs = $"[MarshalAs(UnmanagedType.{def.MarshalAs.UnmanagedType}";
                if (def.MarshalAs.ArraySubType.HasValue)
                {
                    marshalAs += $", ArraySubType = UnmanagedType.{def.MarshalAs.ArraySubType}";
                }
                marshalAs += ")] ";
            }
            else if (def.MarshalUsing != null)
            {
                var dic = new Dictionary<string, object>();
                if (def.MarshalUsing.TypeName != null)
                {
                    dic.Add(string.Empty, $"typeof({def.MarshalUsing.TypeName})");
                }

                if (def.MarshalUsing.CountElementName != null)
                {
                    dic.Add(nameof(def.MarshalUsing.CountElementName), $"nameof({def.MarshalUsing.CountElementName})");
                }

                if (def.MarshalUsing.ConstantElementCount.HasValue)
                {
                    dic.Add(nameof(def.MarshalUsing.ConstantElementCount), def.MarshalUsing.ConstantElementCount.Value);
                }

                marshalUsing = $"[MarshalUsing({string.Join(", ", dic.Select(kv => (kv.Key.Length > 0) ? (kv.Key + " = " + kv.Value) : kv.Value))})] ";
            }

            string? direction = null;
            if (def.Direction != null)
            {
                direction = def.Direction.Value.ToString().ToLowerInvariant() + " ";
            }

            context.CurrentWriter.Write($"{inAtt}{outAtt}{marshalAs}{marshalUsing}{direction}{def.TypeName}{def.Comments} {GetIdentifier(parameter.Name)}");
        }

        public virtual void GenerateExtension(BuilderContext context, BuilderTypeExtension extension)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.Generation);
            ArgumentNullException.ThrowIfNull(extension);
            if (extension.RootType is not InterfaceType)
                return;

            var ns = context.MapGeneratedFullName(extension.RootType.FullName).Namespace;
            ns = GetIdentifier(ns);
            context.CurrentWriter.WriteLine($"namespace {ns};");
            context.CurrentWriter.WriteLine();
            context.CurrentNamespace = ns;

            if (extension.RootType is InterfaceType interfaceType)
            {
                GenerateExtension(context, extension, interfaceType);
            }

            context.CurrentNamespace = null;
        }

        protected virtual void GenerateExtension(BuilderContext context, BuilderTypeExtension extension, InterfaceType interfaceType)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.Generation);
            ArgumentNullException.ThrowIfNull(extension);
            ArgumentNullException.ThrowIfNull(interfaceType);

            //if (type.SupportedOSPlatform != null)
            //{
            //    context.CurrentWriter.WriteLine($"[SupportedOSPlatform(\"{type.SupportedOSPlatform}\")]");
            //}

            context.CurrentWriter.Write($"public static partial class {GetIdentifier(extension.GetGeneratedName(context))}");
            context.CurrentWriter.WriteLine();
            context.CurrentWriter.WithParens(() =>
            {
                var types = new List<InterfaceType>
                {
                    (InterfaceType)extension.RootType
                };

                types.AddRange(extension.Types.Cast<InterfaceType>().OrderBy(t => t, BuilderTypeHierarchyComparer.Instance));
                foreach (var type in types)
                {
                    context.CurrentWriter.WriteLine("// " + type.Name);
                    for (var i = 0; i < type.Methods.Count; i++)
                    {
                        var method = type.Methods[i];
                        GenerateExtension(context, extension, type, method);
                        if (i != type.Methods.Count - 1)
                        {
                            context.CurrentWriter.WriteLine();
                        }
                    }
                }
            });
        }

        protected virtual void GenerateExtension(BuilderContext context, BuilderTypeExtension extension, InterfaceType type, BuilderMethod method)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.Generation);
            ArgumentNullException.ThrowIfNull(extension);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(method);

            string returnTypeName;
            returnTypeName = method.GetExtendedValue<string>(nameof(returnTypeName))!;

            string methodName;
            methodName = method.GetExtendedValue<string>(nameof(methodName))!;
            context.CurrentWriter.Write($"public static {returnTypeName} {methodName}(");
            for (var j = 0; j < method.Parameters.Count; j++)
            {
                var parameter = method.Parameters[j];
                //GenerateCode(context, type, method, methodPatch, parameter, j);
                if (j != method.Parameters.Count - 1)
                {
                    context.CurrentWriter.Write(", ");
                }
            }
            context.CurrentWriter.WriteLine($")");
            context.CurrentWriter.WithParens(() =>
            {
            });
        }

        // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#154-constants
        private static readonly ConcurrentBag<FullName> _constableTypes =
        [
            WellKnownTypes.SystemSByte.FullName,
            WellKnownTypes.SystemByte.FullName,
            WellKnownTypes.SystemInt16.FullName,
            WellKnownTypes.SystemUInt16.FullName,
            WellKnownTypes.SystemInt32.FullName,
            WellKnownTypes.SystemUInt32.FullName,
            WellKnownTypes.SystemInt64.FullName,
            WellKnownTypes.SystemUInt64.FullName,
            WellKnownTypes.SystemChar.FullName,
            WellKnownTypes.SystemSingle.FullName,
            WellKnownTypes.SystemDouble.FullName,
            WellKnownTypes.SystemDecimal.FullName,
            WellKnownTypes.SystemBoolean.FullName,
            WellKnownTypes.SystemString.FullName,
        ];

        private static readonly ConcurrentDictionary<string, string> _typeKeywords = new()
        {
            [WellKnownTypes.SystemBoolean.FullName.ToString()] = "bool",
            [WellKnownTypes.SystemBoolean.Name] = "bool",
            [WellKnownTypes.SystemByte.FullName.ToString()] = "byte",
            [WellKnownTypes.SystemByte.Name] = "byte",
            [WellKnownTypes.SystemChar.FullName.ToString()] = "char",
            [WellKnownTypes.SystemChar.Name] = "char",
            [WellKnownTypes.SystemDouble.FullName.ToString()] = "double",
            [WellKnownTypes.SystemDouble.Name] = "double",
            [WellKnownTypes.SystemDecimal.FullName.ToString()] = "decimal",
            [WellKnownTypes.SystemDecimal.Name] = "decimal",
            [WellKnownTypes.SystemInt16.FullName.ToString()] = "short",
            [WellKnownTypes.SystemInt16.Name] = "short",
            [WellKnownTypes.SystemInt32.FullName.ToString()] = "int",
            [WellKnownTypes.SystemInt32.Name] = "int",
            [WellKnownTypes.SystemInt64.FullName.ToString()] = "long",
            [WellKnownTypes.SystemInt64.Name] = "long",
            [WellKnownTypes.SystemIntPtr.FullName.ToString()] = "nint",
            [WellKnownTypes.SystemIntPtr.Name] = "nint",
            [WellKnownTypes.SystemObject.FullName.ToString()] = "object",
            [WellKnownTypes.SystemObject.Name] = "object",
            [WellKnownTypes.SystemSByte.FullName.ToString()] = "sbyte",
            [WellKnownTypes.SystemSByte.Name] = "sbyte",
            [WellKnownTypes.SystemSingle.FullName.ToString()] = "float",
            [WellKnownTypes.SystemSingle.Name] = "float",
            [WellKnownTypes.SystemString.FullName.ToString()] = "string",
            [WellKnownTypes.SystemString.Name] = "string",
            [WellKnownTypes.SystemUInt16.FullName.ToString()] = "ushort",
            [WellKnownTypes.SystemUInt16.Name] = "ushort",
            [WellKnownTypes.SystemUInt32.FullName.ToString()] = "uint",
            [WellKnownTypes.SystemUInt32.Name] = "uint",
            [WellKnownTypes.SystemUInt64.FullName.ToString()] = "ulong",
            [WellKnownTypes.SystemUInt64.Name] = "ulong",
            [WellKnownTypes.SystemUIntPtr.FullName.ToString()] = "nuint",
            [WellKnownTypes.SystemUIntPtr.Name] = "nuint",
            [WellKnownTypes.SystemVoid.FullName.ToString()] = "void",
            [WellKnownTypes.SystemVoid.Name] = "void",
        };

        private static readonly ConcurrentBag<string> _keywords =
        [
            "abstract",
            "as",
            "base",
            "bool",
            "break",
            "byte",
            "case",
            "catch",
            "char",
            "checked",
            "class",
            "const",
            "continue",
            "decimal",
            "default",
            "delegate",
            "do",
            "double",
            "else",
            "enum",
            "event",
            "explicit",
            "extern",
            "false",
            "finally",
            "fixed",
            "float",
            "for",
            "foreach",
            "goto",
            "if",
            "implicit",
            "in",
            "int",
            "interface",
            "internal",
            "is",
            "lock",
            "long",
            "namespace",
            "new",
            "null",
            "object",
            "operator",
            "out",
            "override",
            "params",
            "private",
            "protected",
            "public",
            "readonly",
            "ref",
            "return",
            "sbyte",
            "sealed",
            "short",
            "sizeof",
            "stackalloc",
            "static",
            "string",
            "struct",
            "switch",
            "this",
            "throw",
            "true",
            "try",
            "typeof",
            "uint",
            "ulong",
            "unchecked",
            "unsafe",
            "ushort",
            "virtual",
            "void",
            "volatile",
            "while",
        ];
    }
}
