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

namespace Win32InteropBuilder.Generators
{
    public class CSharpGenerator : IGenerator
    {
        public string Name => "CSharp";
        public string FileExtension => ".cs";
        public CSharpGeneratorConfiguration Configuration { get; private set; } = new();
        public IReadOnlyCollection<FullName> ConstableTypes => _constableTypes;

        private const string IntPtrTypeName = "nint";
        private const string UIntPtrTypeName = "nuint";
        private const string VoidTypeName = "void";
        private const string VTablePtr = "VTablePtr";

        public override string ToString() => Name;
        public virtual void Configure(JsonElement element)
        {
            Configuration = element.Deserialize<CSharpGeneratorConfiguration>(Builder.JsonSerializerOptions) ?? new();

            // supported constants types
            if (Configuration.SupportedConstantTypes.Any(b => b.MatchesEverything))
            {
                Configuration.SupportedConstantTypes.Clear();
            }
            else
            {
                // add all c# const
                foreach (var fn in new CSharpGenerator().ConstableTypes)
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
            context.CurrentWriter.WriteLine("#nullable enable");

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
                    if (field.TypeFullName == null)
                        throw new InvalidOperationException();

                    if (!Configuration.IsSupportedAsConstant(field.TypeFullName))
                        continue;

                    if (field.Documentation != null)
                    {
                        context.CurrentWriter.WriteLine("// " + field.Documentation);
                    }

                    var mapped = context.MapType(field.TypeFullName);
                    if (mapped.UnmanagedType.HasValue)
                    {
                        context.CurrentWriter.WriteLine($"[MarshalAs(UnmanagedType.{mapped.UnmanagedType.Value})]");
                    }

                    var constText = field.Attributes.HasFlag(FieldAttributes.Literal) && context.IsConstableType(field.TypeFullName) ? "const" : "static readonly";
                    var vas = GetValueAsString(context, context.AllTypes[field.TypeFullName], field.GetDefaultValue(context));
                    context.CurrentWriter.WriteLine($"public {constText} {GetTypeReferenceName(mapped.GetGeneratedName(context))} {GetIdentifier(field.Name)} = {vas};");

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

            if (type.Guid.HasValue)
            {
                // note: this shouldn't happen when there are fields
                // when there are no fields, it should have been translated into a guid constant
                context.CurrentWriter.WriteLine($"[Guid(\"{type.Guid.GetValueOrDefault()}\")]");
            }

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
                string? pack = null;
                if (type.PackingSize.HasValue)
                {
                    pack = $", Pack = {type.PackingSize.Value}";
                }

                if (pack != null || lk != LayoutKind.Sequential)
                {
                    context.CurrentWriter.WriteLine($"[StructLayout(LayoutKind.{lk}{pack})]");
                }
            }

            var generateEquatable = context.GeneratesEquatable(type);
            var generateNull = context.GeneratesNullMember(type);

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

                string? derives = null;
                if (generateEquatable)
                {
                    derives = $" : IEquatable<{strucTypeName}>";
                }

                context.CurrentWriter.Write($"public partial struct {strucTypeName}{derives}");
            }

            context.CurrentWriter.WriteLine();
            context.CurrentWriter.WithParens(() =>
            {
                if (generateNull)
                {
                    context.CurrentWriter.WriteLine($"public static readonly {strucTypeName} Null = new();");
                    context.CurrentWriter.WriteLine();
                }

                for (var i = 0; i < type.NestedTypes.Count; i++)
                {
                    var nt = context.AllTypes[type.NestedTypes[i]];
                    GenerateTypeCode(context, nt);
                    if (i <= type.NestedTypes.Count || type.Fields.Count > 0)
                    {
                        context.CurrentWriter.WriteLine();
                    }
                }

                for (var i = 0; i < type.Fields.Count; i++)
                {
                    var field = type.Fields[i];
                    if (field.TypeFullName == null)
                        throw new InvalidOperationException();

                    var mapped = context.MapType(field.TypeFullName);

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
                        if (mapped is PointerType)
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

                if (generateEquatable)
                {
                    var fieldName = GetIdentifier(type.Fields[0].Name);
                    var fieldMapped = context.MapType(type.Fields[0].TypeFullName!);
                    var fieldTypeName = GetTypeReferenceName(fieldMapped.GetGeneratedName(context));
                    if (fieldTypeName == VoidTypeName)
                    {
                        fieldTypeName = IntPtrTypeName;
                    }

                    context.CurrentWriter.WriteLine();
                    context.CurrentWriter.WriteLine($"public {strucTypeName}({fieldTypeName} value) => this.{fieldName} = value;");

                    var generateToString = context.GeneratesToString(type);
                    if (generateToString)
                    {
                        context.CurrentWriter.WriteLine($"public override string ToString() => $\"0x{{{fieldName}:x}}\";");
                    }

                    context.CurrentWriter.WriteLine();
                    context.CurrentWriter.WriteLine($"public override readonly bool Equals(object? obj) => obj is {strucTypeName} value && Equals(value);");
                    context.CurrentWriter.WriteLine($"public readonly bool Equals({strucTypeName} other) => other.{fieldName} == {fieldName};");
                    context.CurrentWriter.WriteLine($"public override readonly int GetHashCode() => {fieldName}.GetHashCode();");
                    context.CurrentWriter.WriteLine($"public static bool operator ==({strucTypeName} left, {strucTypeName} right) => left.Equals(right);");
                    context.CurrentWriter.WriteLine($"public static bool operator !=({strucTypeName} left, {strucTypeName} right) => !left.Equals(right);");
                    context.CurrentWriter.WriteLine($"public static implicit operator {fieldTypeName}({strucTypeName} value) => value.{fieldName};");
                    context.CurrentWriter.WriteLine($"public static implicit operator {strucTypeName}({fieldTypeName} value) => new(value);");
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
                if (method.ReturnTypeFullName != null)
                {
                    var mapped = context.MapType(method.ReturnTypeFullName);

                    if (mapped is InterfaceType)
                    {
                        typeName = IntPtrTypeName;
                    }
                    else
                    {
                        typeName = mapped.GetGeneratedName(context);
                    }

                    if (mapped.UnmanagedType.HasValue)
                    {
                        context.CurrentWriter.WriteLine($"[return: MarshalAs(UnmanagedType.{mapped.UnmanagedType.Value})]");
                    }
                }
                else
                {
                    typeName = VoidTypeName;
                }

                context.CurrentWriter.Write($"public delegate {GetTypeReferenceName(typeName)} {GetIdentifier(type.GetGeneratedName(context))}(");
                for (var j = 0; j < method.Parameters.Count; j++)
                {
                    var parameter = method.Parameters[j];

                    if (parameter.Attributes.HasFlag(ParameterAttributes.Out))
                    {
                        parameter.TypeFullName = WellKnownTypes.SystemIntPtr.FullName;
                        parameter.Attributes &= ~ParameterAttributes.Out;
                    }

                    var methodPatch = patch?.Methods?.FirstOrDefault(m => m.Matches(method));
                    GenerateCode(context, type, method, methodPatch, parameter, j, CSharpGeneratorParameterOptions.None);
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

            var typeName = GetIdentifier(type.GetGeneratedName(context));
            context.CurrentWriter.WriteLine($"[InlineArray({typeName}.Length)]");
            context.CurrentWriter.WriteLine($"public partial struct {typeName}");
            context.CurrentWriter.WithParens(() =>
            {
                var elementName = type.ElementName ?? "Data";
                var elementTypeName = GetTypeReferenceName(type.ElementType.GetGeneratedName(context));

                context.CurrentWriter.WriteLine($"public const int Length = {type.Size};");
                context.CurrentWriter.WriteLine();
                context.CurrentWriter.WriteLine($"public {elementTypeName} {elementName};");

                if (elementTypeName == "char")
                {
                    context.CurrentWriter.WriteLine();
                    context.CurrentWriter.WriteLine($"public override readonly string ToString() => ((ReadOnlySpan<char>)this).ToString().TrimEnd('\\0');");
                    context.CurrentWriter.WriteLine($"public void CopyFrom(string? str) => DirectNExtensions.CopyFrom<{typeName}>(str, this, Length);");
                    context.CurrentWriter.WriteLine($"public static implicit operator {typeName}(string? str) {{ var n = new {typeName}(); n.CopyFrom(str); return n; }}");
                }
            });
        }

        protected virtual void GenerateInterfaceMethods(BuilderContext context, InterfaceType type, int startSlot)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);

            var identifier = GetIdentifier(type.GetGeneratedName(context));
            var patch = context.Configuration.GetTypePatch(type);

            for (var i = 0; i < type.Methods.Count; i++)
            {
                var method = type.Methods[i];
                var methodReturn = GenerateCode(context, type, patch, method,
                    CSharpGeneratorMethodOptions.ForImplementation |
                    CSharpGeneratorMethodOptions.Public |
                    CSharpGeneratorMethodOptions.OutAsRef | // because it's more simple otherwise we'd have to copy structs variables
                    CSharpGeneratorMethodOptions.ComOutPtrAsIntPtr | // because it's more simple
                    CSharpGeneratorMethodOptions.NoLineReturn |
                    CSharpGeneratorMethodOptions.Unsafe);

                context.CurrentWriter.WriteLine(" =>");
                context.CurrentWriter.Indent++;
                string? argumentsTypes = null;
                string? arguments = null;
                if (methodReturn.Parameters.Count > 0)
                {
                    argumentsTypes = "," + string.Join(",", methodReturn.Parameters.Select(p => p.ToArgumentDeclaration()));
                    arguments = ", " + string.Join(", ", methodReturn.Parameters.Select(p => p.ToArgument()));
                }

                var slot = startSlot + i;
                context.CurrentWriter.WriteLine($"((delegate* unmanaged<{identifier}*{argumentsTypes}, {methodReturn.ReturnTypeName}>)(((void**)*((void**){VTablePtr}))[{slot}]))(({identifier}*){VTablePtr}{arguments});");
                context.CurrentWriter.Indent--;

                if (i != type.Methods.Count - 1)
                {
                    context.CurrentWriter.WriteLine();
                }
            }
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

            if (!type.IsIUnknownDerived)
            {
                // there are some structs called "interfaces" that are not COM (not IUnknown/IDispatch derived), ex: ID3D12ShaderReflectionConstantBuffer
                var identifier = GetIdentifier(type.GetGeneratedName(context));
                context.CurrentWriter.WriteLine($"public partial struct {identifier}");
                context.CurrentWriter.WithParens(() =>
                {
                    context.CurrentWriter.WriteLine($"public static readonly {identifier} Null = new();");
                    context.CurrentWriter.WriteLine();

                    context.CurrentWriter.WriteLine($"public nint {VTablePtr};");
                    context.CurrentWriter.WriteLine();
                    if (type.Interfaces.Count > 1)
                        throw new NotSupportedException();

                    var slot = 0;
                    if (type.Interfaces.Count == 1)
                    {
                        var baseInterface = context.AllTypes[type.Interfaces[0]] as InterfaceType;
                        if (baseInterface != null)
                        {
                            context.CurrentWriter.WriteLine($"// {baseInterface.Name} methods");
                            GenerateInterfaceMethods(context, baseInterface, slot);
                            slot += baseInterface.Methods.Count;
                            context.CurrentWriter.WriteLine();
                            context.CurrentWriter.WriteLine($"// {type.Name} methods");
                        }
                    }

                    GenerateInterfaceMethods(context, type, slot);
                });
                return;
            }

            context.CurrentWriter.WriteLine($"[GeneratedComInterface, Guid(\"{type.Guid.GetValueOrDefault()}\")]");
            context.CurrentWriter.Write($"public partial interface {GetIdentifier(type.GetGeneratedName(context))}");

            if (type.Interfaces.Count > 0)
            {
                context.CurrentWriter.Write($" : {string.Join(", ", type.Interfaces.Select(i => GetIdentifier(context.AllTypes[i].GetGeneratedName(context))))}");
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
            if (type.UnderlyingTypeFullName != null)
            {
                var ut = context.AllTypes[type.UnderlyingTypeFullName];
                var typeName = GetTypeReferenceName(ut.GetGeneratedName(context));
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

                    var def = field.GetDefaultValue(context);
                    if (def != null)
                    {
                        context.CurrentWriter.Write(" = ");
                        var ut = context.AllTypes[type.UnderlyingTypeFullName ?? WellKnownTypes.SystemInt32.FullName];
                        context.CurrentWriter.Write(GetValueAsString(context, ut, def));
                    }
                    context.CurrentWriter.WriteLine(',');
                }
            });
        }

        protected virtual CSharpGeneratorMethod GenerateCode(BuilderContext context, BuilderType type, BuilderPatchType? patch, BuilderMethod method, CSharpGeneratorMethodOptions options = CSharpGeneratorMethodOptions.None)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(method);

            if (method.Documentation != null)
            {
                context.CurrentWriter.WriteLine("// " + method.Documentation);
            }

            // patch from type
            string? returnTypeName = null;
            string? methodName = null;

            // consider last error only for functions
            var setLastError = false;
            if (type.GetType() == typeof(BuilderType))
            {
                setLastError = context.HasSetLastError(method);
            }

            var methodPatch = context.Configuration.Patches?.Methods?.FirstOrDefault(m => m.Matches(method));
            methodPatch ??= patch?.Methods?.FirstOrDefault(m => m.Matches(method));
            if (methodPatch != null)
            {
                if (methodPatch.SetLastError.HasValue)
                {
                    setLastError = methodPatch.SetLastError.Value;
                }

                if (!string.IsNullOrEmpty(methodPatch.TypeName))
                {
                    returnTypeName = methodPatch.TypeName;
                }

                if (!string.IsNullOrEmpty(methodPatch.NewName))
                {
                    methodName = methodPatch.NewName;
                }
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

                if (setLastError)
                {
                    context.CurrentWriter.Write(", SetLastError = true");
                }

                context.CurrentWriter.WriteLine(")]");
            }

            if (method.SupportedOSPlatform != null)
            {
                context.CurrentWriter.WriteLine($"[SupportedOSPlatform(\"{method.SupportedOSPlatform}\")]");
            }

            if (!options.HasFlag(CSharpGeneratorMethodOptions.ForImplementation))
            {
                context.CurrentWriter.WriteLine("[PreserveSig]");

                var cc = context.GetCallingConventionType(type, method);
                if (cc != null)
                {
                    context.CurrentWriter.WriteLine($"[UnmanagedCallConv(CallConvs = [typeof({cc.Name})])]");
                }
            }

            if (returnTypeName == null)
            {
                if (method.ReturnTypeFullName != null && method.ReturnTypeFullName != WellKnownTypes.SystemVoid.FullName)
                {
                    var mapped = context.MapType(method.ReturnTypeFullName);
                    var iface = mapped as InterfaceType;
                    if (iface != null && mapped is not PointerType && iface.IsIUnknownDerived)
                    {
                        context.CurrentWriter.WriteLine($"[return: MarshalUsing(typeof(UniqueComInterfaceMarshaller<{iface.Name}>))]");
                    }

                    if (mapped is PointerType || (iface != null && !iface.IsIUnknownDerived))
                    {
                        returnTypeName = IntPtrTypeName;
                    }
                    else
                    {
                        var um = mapped.UnmanagedType;
                        if (Configuration.MarshalAsError(mapped.FullName))
                        {
                            um = UnmanagedType.Error;
                        }

                        if (um.HasValue && (!method.Attributes.HasFlag(MethodAttributes.Static) || mapped == WellKnownTypes.SystemBoolean))
                        {
                            context.CurrentWriter.WriteLine($"[return: MarshalAs(UnmanagedType.{um.Value})]");
                        }
                        returnTypeName = GetTypeReferenceName(mapped.GetGeneratedName(context));
                    }
                }
                else
                {
                    var rt = context.AllTypes[method.ReturnTypeFullName!];
                    if (rt is PointerType)
                    {
                        returnTypeName = IntPtrTypeName;
                    }
                    else
                    {
                        returnTypeName = VoidTypeName;
                    }
                }
            }

            string? staticText = null;
            if (method.Attributes.HasFlag(MethodAttributes.Static))
            {
                staticText = "static partial ";
            }

            methodName ??= GetIdentifier(method.Name);
            string? comments = null;
            foreach (var ifaceTypeName in type.GetAllInterfaces(context))
            {
                var iface = context.AllTypes[ifaceTypeName];
                var existing = iface.Methods.FirstOrDefault(m => HaveSameSignature(context, type, m, method));
                if (existing != null)
                {
                    methodName = type.Name + "_" + methodName;
                    comments = " // renamed, see https://github.com/dotnet/runtime/issues/101240";
                }
            }

            string? publicText = null;
            if (type is not InterfaceType || options.HasFlag(CSharpGeneratorMethodOptions.Public))
            {
                publicText = "public ";
            }

            string? unsafeCode = null;
            if (options.HasFlag(CSharpGeneratorMethodOptions.Unsafe))
            {
                unsafeCode = "unsafe ";
            }

            string? newCode = null;
            if (options.HasFlag(CSharpGeneratorMethodOptions.ForImplementation) &&
                (method.Name == nameof(GetType) || method.Name == nameof(ToString) || method.Name == nameof(Equals) || method.Name == nameof(GetHashCode)))
            {
                newCode = "new ";
            }

            string? readOnly = null;
            if (options.HasFlag(CSharpGeneratorMethodOptions.ReadOnly))
            {
                readOnly = "readonly ";
            }

            returnTypeName = GetTypeReferenceName(returnTypeName);
            method.SetExtendedValue(nameof(returnTypeName), returnTypeName);
            method.SetExtendedValue(nameof(methodName), methodName);

            var methodReturn = new CSharpGeneratorMethod(returnTypeName);
            context.CurrentWriter.Write($"{publicText}{readOnly}{unsafeCode}{newCode}{staticText}{returnTypeName} {methodName}(");
            for (var j = 0; j < method.Parameters.Count; j++)
            {
                var parameter = method.Parameters[j];

                var parameterOptions = CSharpGeneratorParameterOptions.None;
                if (options.HasFlag(CSharpGeneratorMethodOptions.OutAsRef))
                {
                    parameterOptions |= CSharpGeneratorParameterOptions.OutAsRef;
                }

                if (options.HasFlag(CSharpGeneratorMethodOptions.ComOutPtrAsIntPtr))
                {
                    parameterOptions |= CSharpGeneratorParameterOptions.ComOutPtrAsIntPtr;
                }

                var parameterCode = GenerateCode(context, type, method, methodPatch, parameter, j, parameterOptions);
                methodReturn.Parameters.Add(parameterCode);
                if (j != method.Parameters.Count - 1)
                {
                    context.CurrentWriter.Write(", ");
                }
            }

            var decl = options.HasFlag(CSharpGeneratorMethodOptions.ForImplementation) ? null : ";";
            context.CurrentWriter.Write($"){decl}{comments}");
            if (!options.HasFlag(CSharpGeneratorMethodOptions.NoLineReturn))
            {
                context.CurrentWriter.WriteLine();
            }
            return methodReturn;
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
                var def1 = GetParameterDef(context, type, method1, method1.Parameters[i], CSharpGeneratorParameterOptions.None);
                var def2 = GetParameterDef(context, type, method2, method2.Parameters[i], CSharpGeneratorParameterOptions.None);
                if (def1.TypeName != def2.TypeName || def1.Direction != def2.Direction)
                    return false;
            }
            return true;
        }

        public virtual bool IsUnknownComOutPtr(BuilderParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            if (parameter.TypeFullName == null)
                throw new InvalidOperationException();

            var np = parameter.TypeFullName.NoPointerFullName;
            return parameter.IsComOutPtr && (np == WellKnownTypes.SystemVoid.FullName || np == WellKnownTypes.SystemObject.FullName);
        }

        protected virtual ParameterDef GetUnknownComOutPtr(BuilderContext context, BuilderParameter parameter, ParameterDef def, CSharpGeneratorParameterOptions options)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(def);
            if (!IsUnknownComOutPtr(parameter))
                throw new ArgumentException(null, nameof(parameter));

            var copTarget = context.Configuration.Generation.UnknownComOutPtrTarget;
            if (options.HasFlag(CSharpGeneratorParameterOptions.ComOutPtrAsIntPtr))
            {
                copTarget = ComOutPtrTarget.IntPtr;
            }

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
                        Direction = ParameterDirection.Out,
                        TypeName = "object",
                        MarshalUsing = new ParameterMarshalUsing { TypeName = "UniqueComInterfaceMarshaller<object>" },
                        Comments = $" /* {def.TypeName} */"
                    });

                // case ComOutPtrTarget.IntPtr
                default:
                    return context.GetParameterDef(parameter, new ParameterDef
                    {
                        Direction = ParameterDirection.Out,
                        TypeName = IntPtrTypeName,
                        Comments = $" /* {def.TypeName} */"
                    });
            }
        }

        protected virtual ParameterDef GetParameterDef(BuilderContext context, BuilderType type, BuilderMethod method, BuilderParameter parameter, CSharpGeneratorParameterOptions options)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(type);
            if (parameter.TypeFullName == null)
                throw new InvalidOperationException();

            var parameterType = context.AllTypes[parameter.TypeFullName];
            var pt = parameterType as PointerType;
            var def = new ParameterDef();
            var mapped = context.MapType(parameter.TypeFullName);
            def.TypeName = GetTypeReferenceName(mapped.GetGeneratedName(context));
            if (IsUnknownComOutPtr(parameter))
                return GetUnknownComOutPtr(context, parameter, def, options);

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

            if (!def.Direction.HasValue &&
                mapped is PointerType &&
                def.TypeName != IntPtrTypeName &&
                def.TypeName != UIntPtrTypeName)
            {
                def.Direction = ParameterDirection.In;
            }

            if (parameterType is InterfaceType ifaceType && !ifaceType.IsIUnknownDerived)
            {
                var rt = context.AllTypes[parameter.TypeFullName];
            }

            if (parameter.Attributes.HasFlag(ParameterAttributes.Optional) && pt != null)
            {
                def.Comments = $" /* optional {def.TypeName}{new string('*', pt.Indirections)} */";
                def.TypeName = IntPtrTypeName;
                def.MarshalAs = null;
                def.Direction = null;
                return context.GetParameterDef(parameter, def);
            }

            if (def.TypeName == VoidTypeName && mapped is PointerType)
            {
                def.TypeName = IntPtrTypeName;
                def.MarshalAs = null;
                if (mapped is PointerType pt1 && pt1.Indirections == 1)
                {
                    def.Direction = null;
                }
                else
                {
                    def.Direction = ParameterDirection.Out;
                }
                return context.GetParameterDef(parameter, def);
            }

            if (def.TypeName == "byte" && pt != null)
            {
                def.TypeName = IntPtrTypeName;
                def.Comments = " /* byte array */";
                def.MarshalAs = null;
                if (pt.Indirections == 1)
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
                if (def.Direction.HasValue || parameterType is InterfaceType || parameterType is DelegateType)
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

                var implicitArray = IsArrayType(mapped) && pt == null;
                if (!implicitArray)
                {
                    def.TypeName += "[]";
                }

                if (parameter.NativeArray.CountParameter != null)
                {
                    def.MarshalUsing = new ParameterMarshalUsing { CountElementName = parameter.NativeArray.CountParameter.Name };
                    if (!def.Direction.HasValue)
                    {
                        def.IsOut = true;
                        def.IsIn = true;
                    }
                    else if (!parameter.NativeArray.CountParameter.Attributes.HasFlag(ParameterAttributes.Out) &&
                        (pt != null || implicitArray))
                    {
                        def.Direction = null;
                        if (parameter.Attributes.HasFlag(ParameterAttributes.Out))
                        {
                            def.IsOut = true;
                        }
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
                    else if ((pt != null || implicitArray) && parameter.Attributes.HasFlag(ParameterAttributes.Out))
                    {
                        def.Direction = null;
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

                if (implicitArray)
                {
                    def.IsIn = null;
                    def.IsOut = null;
                }

                if (def.Direction == ParameterDirection.In && def.IsArrayTypeName)
                {
                    def.Direction = null;
                    def.IsIn = true;
                }
                return context.GetParameterDef(parameter, def);
            }

            if (def.Direction == ParameterDirection.Out && def.TypeName == "object" && def.MarshalUsing == null && def.MarshalAs == null)
            {
                def.TypeName = IntPtrTypeName;
                return context.GetParameterDef(parameter, def);
            }

            if (def.Direction == ParameterDirection.Out && pt != null && pt.Indirections > 1)
            {
                def.TypeName = IntPtrTypeName;
                return context.GetParameterDef(parameter, def);
            }

            var isOptional = parameter.Attributes.HasFlag(ParameterAttributes.Optional);

            // typically the case of P(W)STR passed as pointers to memory, must not be marked as "out"
            //if (isOptional && def.Direction == ParameterDirection.Out && parameter.BytesParamIndex.HasValue)
            if (def.Direction == ParameterDirection.Out && parameter.BytesParamIndex.HasValue)
            {
                def.Direction = null;
                if (mapped is PointerType)
                {
                    def.TypeName = IntPtrTypeName;
                }
            }

            // don't set ? on value type as this creates a Nullable<struct> which is managed/non-blittable
            if (isOptional &&
                def.TypeName != IntPtrTypeName &&
                !parameterType.IsValueType &&
                (parameterType is not InterfaceType itype || itype.IsIUnknownDerived)) // non-IUnknown are generated as structs
            {
                def.TypeName += "?";
            }

            if (pt != null && context.AllTypes[pt.FullName.NoPointerFullName] is InterfaceType && def.Direction == ParameterDirection.Out)
            {
                if (options.HasFlag(CSharpGeneratorParameterOptions.ComOutPtrAsIntPtr))
                    return context.GetParameterDef(parameter, new ParameterDef
                    {
                        TypeName = IntPtrTypeName,
                        Direction = ParameterDirection.Out,
                    });

                var copTarget = context.Configuration.Generation.ComOutPtrTarget;
                if (copTarget == ComOutPtrTarget.UniqueObject)
                    return context.GetParameterDef(parameter, new ParameterDef
                    {
                        TypeName = def.TypeName,
                        Direction = ParameterDirection.Out,
                        MarshalUsing = new ParameterMarshalUsing { TypeName = $"UniqueComInterfaceMarshaller<{def.TypeName}>" },
                    });
            }

            return context.GetParameterDef(parameter, def);
        }

        protected virtual bool IsArrayType(BuilderType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.FullName == FullName.PWSTR || type.FullName == FullName.PSTR || type.FullName == FullName.BSTR;
        }

        protected virtual CSharpGeneratorParameter GenerateCode(
            BuilderContext context,
            BuilderType type,
            BuilderMethod method,
            BuilderPatchMember? methodPatch,
            BuilderParameter parameter,
            int parameterIndex,
            CSharpGeneratorParameterOptions options)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(parameter);
            if (parameter.TypeFullName == null)
                throw new InvalidOperationException();

            var def = GetParameterDef(context, type, method, parameter, options);

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

            if (def.TypeName == IntPtrTypeName)
            {
                marshalUsing = null;
            }

            string? direction = null;
            if (def.Direction != null)
            {
                if (options.HasFlag(CSharpGeneratorParameterOptions.OutAsRef) && def.Direction == ParameterDirection.Out)
                {
                    direction = "ref ";
                }
                else
                {
                    direction = def.Direction.Value.ToString().ToLowerInvariant() + " ";
                }
            }

            var identifier = GetIdentifier(parameter.Name);
            var parameterReturn = new CSharpGeneratorParameter(identifier, def.TypeName!, def.Direction);
            context.CurrentWriter.Write($"{inAtt}{outAtt}{marshalAs}{marshalUsing}{direction}{def.TypeName}{def.Comments} {identifier}");
            return parameterReturn;
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
