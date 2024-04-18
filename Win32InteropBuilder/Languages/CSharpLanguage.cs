using System;
using System.Collections.Concurrent;
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

        public override string ToString() => Name;
        public virtual void Configure(JsonElement element)
        {
            Configuration = element.Deserialize<CSharpLanguageConfiguration>(Builder.JsonSerializerOptions) ?? new();
        }

        public virtual string GetValueAsString(BuilderType type, object? value)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (value == null)
                return "null";

            if (value is Guid guid)
                return $"new(\"{guid}\")";

            if (value is bool b)
                return b ? "true" : "false";

            if (value is string s)
                return $"@\"{s.Replace("\"", "\"\"")}\"";

            if (type.ClrType != null)
            {
                value = Conversions.ChangeType(value, type.ClrType, value, CultureInfo.InvariantCulture);
            }

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
        public virtual string? GetIdentifier(string? name)
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
        public virtual string GetTypeReferenceName(string fullName)
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
                ns = type.FullName.Namespace;
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
                for (var i = 0; i < type.Fields.Count; i++)
                {
                    var field = type.Fields[i];
                    if (field.Type == null)
                        throw new InvalidOperationException();

                    if (field.Documentation != null)
                    {
                        context.CurrentWriter.WriteLine("// " + field.Documentation);
                    }

                    var mapped = context.MapType(field.Type);
                    if (mapped.UnmanagedType.HasValue)
                    {
                        context.CurrentWriter.WriteLine($"[MarshalAs(UnmanagedType.{mapped.UnmanagedType.Value})]");
                    }

                    var constText = field.Attributes.HasFlag(FieldAttributes.Literal) && field.Type.IsConstableType() ? "const" : "static readonly";
                    context.CurrentWriter.WriteLine($"public {constText} {GetTypeReferenceName(mapped.GetGeneratedName(context))} {GetIdentifier(field.Name)} = {GetValueAsString(field.Type, field.DefaultValue)};");

                    if (i != type.Fields.Count - 1 || type.Methods.Count > 0)
                    {
                        context.CurrentWriter.WriteLine();
                    }
                }

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

                    GenerateCode(context, type, method);
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
            if (ns != null)
            {
                context.CurrentWriter.Write($"public struct {GetIdentifier(ns)}");
            }
            else
            {
                context.CurrentWriter.Write($"public partial struct {GetIdentifier(type.GetGeneratedName(context))}");
            }

            context.CurrentWriter.WriteLine();
            context.CurrentWriter.WithParens(() =>
            {
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
                        context.CurrentWriter.Write($"public nint {GetIdentifier(field.Name)};");
                    }
                    else
                    {
                        var typeName = GetTypeReferenceName(mapped.GetGeneratedName(context));
                        if (mapped.Indirections > 0)
                        {
                            typeName = "nint";
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

        public virtual void GenerateTypeCode(BuilderContext context, DelegateType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);

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
                        typeName = "nint";
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

                    GenerateCode(context, type, parameter);
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

        public virtual void GenerateTypeCode(BuilderContext context, InlineArrayType type)
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
                for (var i = 0; i < type.Methods.Count; i++)
                {
                    var method = type.Methods[i];
                    GenerateCode(context, type, method);
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

                    if (field.DefaultValueAsBytes != null)
                    {
                        context.CurrentWriter.Write(" = ");
                        context.CurrentWriter.Write(GetValueAsString(type.UnderlyingType ?? WellKnownTypes.SystemInt32, field.DefaultValue));
                    }
                    context.CurrentWriter.WriteLine(',');
                }
            });
        }

        public virtual void GenerateCode(BuilderContext context, BuilderType type, BuilderMethod method)
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
            string typeName;
            if (method.ReturnType != null && method.ReturnType != WellKnownTypes.SystemVoid)
            {
                var mapped = context.MapType(method.ReturnType);
                if (mapped.UnmanagedType.HasValue)
                {
                    if (!method.Attributes.HasFlag(MethodAttributes.Static) || mapped == WellKnownTypes.SystemBoolean)
                    {
                        context.CurrentWriter.WriteLine($"[return: MarshalAs(UnmanagedType.{mapped.UnmanagedType.Value})]");
                    }
                }
                typeName = GetTypeReferenceName(mapped.GetGeneratedName(context));
            }
            else
            {
                typeName = "void";
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
                var existing = iface.Methods.FirstOrDefault(m => HaveSameSignature(context, m, method));
                if (existing != null)
                {
                    methodName = type.FullName.Name + "_" + methodName;
                    comments = " // renamed, see https://github.com/dotnet/runtime/issues/101240";
                }
            }

            context.CurrentWriter.Write($"public {staticText}{GetTypeReferenceName(typeName)} {methodName}(");
            for (var j = 0; j < method.Parameters.Count; j++)
            {
                var parameter = method.Parameters[j];
                GenerateCode(context, type, parameter);
                if (j != method.Parameters.Count - 1)
                {
                    context.CurrentWriter.Write(", ");
                }
            }
            context.CurrentWriter.WriteLine($");{comments}");
        }

        protected virtual bool HaveSameSignature(BuilderContext context, BuilderMethod method1, BuilderMethod method2)
        {
            ArgumentNullException.ThrowIfNull(context);
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
                // hope context are similar
                var def1 = GetParameterDef(context, method1.Parameters[i]);
                var def2 = GetParameterDef(context, method2.Parameters[i]);
                if (def1.TypeName != def2.TypeName || def1.Direction != def2.Direction)
                    return false;
            }
            return true;
        }

        protected virtual ParameterDef GetParameterDef(BuilderContext context, BuilderParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameter);
            if (parameter.Type == null)
                throw new InvalidOperationException();

            var mapped = context.MapType(parameter.Type);
            var typeName = GetTypeReferenceName(mapped.GetGeneratedName(context));
            string? comments = null;
            string? marshalAs = null;
            if (parameter.UnmanagedType.HasValue)
            {
                marshalAs = parameter.UnmanagedType.Value.ToString();
            }
            else if (mapped.UnmanagedType.HasValue)
            {
                marshalAs = mapped.UnmanagedType.Value.ToString();
            }

            string? direction = null;
            if (typeName == "byte" && parameter.Type.Indirections > 0)
            {
                typeName = "nint";
                comments = " /* byte array */";
                marshalAs = null;
                if (parameter.Type.Indirections > 1)
                {
                    direction = "out";
                }
            }
            else
            {
                var optionalPtr = parameter.Attributes.HasFlag(ParameterAttributes.Optional) & parameter.Type.Indirections > 0;
                if (optionalPtr)
                {
                    typeName = "nint";
                    comments = $"/* {typeName} */";
                    marshalAs = null;
                }
                else
                {
                    if (parameter.Attributes.HasFlag(ParameterAttributes.Out))
                    {
                        if (parameter.Attributes.HasFlag(ParameterAttributes.In))
                        {
                            direction = "ref";
                        }
                        else
                        {
                            direction = "out";
                        }
                    }
                    else if (mapped.Indirections > 0 && typeName != "nint")
                    {
                        direction = "in";
                    }

                    if (typeName == "void" && mapped.Indirections > 0)
                    {
                        typeName = "nint";
                        marshalAs = null;
                        if (mapped.Indirections == 1)
                        {
                            direction = null;
                        }
                        else
                        {
                            direction = "out";
                        }
                    }
                }
            }
            return new ParameterDef { Direction = direction, TypeName = typeName, MarshalAs = marshalAs, Comments = comments };
        }

        protected class ParameterDef
        {
            public virtual string? Direction { get; set; }
            public virtual string? MarshalAs { get; set; }
            public virtual string? TypeName { get; set; }
            public virtual string? Comments { get; set; }

            public override string ToString() => $"{MarshalAs}{Direction}{TypeName}{Comments}";
        }

        public virtual void GenerateCode(BuilderContext context, BuilderType type, BuilderParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.CurrentWriter);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(parameter);
            if (parameter.Type == null)
                throw new InvalidOperationException();

            var def = GetParameterDef(context, parameter);
            if (type is DelegateType)
            {
                // there are currently big limits to delegate marshaling
                if (def.Direction != null || parameter.Type is InterfaceType || parameter.Type is DelegateType)
                {
                    def.Comments = $" /* {def.Direction} {def.TypeName} */";
                    def.Direction = null;
                    def.TypeName = "nint";
                }
            }

            if (def.MarshalAs != null)
            {
                def.MarshalAs = $"[MarshalAs(UnmanagedType.{def.MarshalAs})] ";
            }

            if (def.Direction != null)
            {
                def.Direction += " ";
            }
            context.CurrentWriter.Write($"{def.MarshalAs}{def.Direction}{def.TypeName}{def.Comments} {GetIdentifier(parameter.Name)}");
        }

        private static readonly ConcurrentDictionary<string, string> _typeKeywords = new()
        {
            [WellKnownTypes.SystemBoolean.FullName.ToString()] = "bool",
            [WellKnownTypes.SystemBoolean.FullName.Name] = "bool",
            [WellKnownTypes.SystemByte.FullName.ToString()] = "byte",
            [WellKnownTypes.SystemByte.FullName.Name] = "byte",
            [WellKnownTypes.SystemChar.FullName.ToString()] = "char",
            [WellKnownTypes.SystemChar.FullName.Name] = "char",
            [WellKnownTypes.SystemDouble.FullName.ToString()] = "double",
            [WellKnownTypes.SystemDouble.FullName.Name] = "double",
            [WellKnownTypes.SystemDecimal.FullName.ToString()] = "decimal",
            [WellKnownTypes.SystemDecimal.FullName.Name] = "decimal",
            [WellKnownTypes.SystemInt16.FullName.ToString()] = "short",
            [WellKnownTypes.SystemInt16.FullName.Name] = "short",
            [WellKnownTypes.SystemInt32.FullName.ToString()] = "int",
            [WellKnownTypes.SystemInt32.FullName.Name] = "int",
            [WellKnownTypes.SystemInt64.FullName.ToString()] = "long",
            [WellKnownTypes.SystemInt64.FullName.Name] = "long",
            [WellKnownTypes.SystemIntPtr.FullName.ToString()] = "nint",
            [WellKnownTypes.SystemIntPtr.FullName.Name] = "nint",
            [WellKnownTypes.SystemObject.FullName.ToString()] = "object",
            [WellKnownTypes.SystemObject.FullName.Name] = "object",
            [WellKnownTypes.SystemSByte.FullName.ToString()] = "sbyte",
            [WellKnownTypes.SystemSByte.FullName.Name] = "sbyte",
            [WellKnownTypes.SystemSingle.FullName.ToString()] = "float",
            [WellKnownTypes.SystemSingle.FullName.Name] = "float",
            [WellKnownTypes.SystemString.FullName.ToString()] = "string",
            [WellKnownTypes.SystemString.FullName.Name] = "string",
            [WellKnownTypes.SystemUInt16.FullName.ToString()] = "ushort",
            [WellKnownTypes.SystemUInt16.FullName.Name] = "ushort",
            [WellKnownTypes.SystemUInt32.FullName.ToString()] = "uint",
            [WellKnownTypes.SystemUInt32.FullName.Name] = "uint",
            [WellKnownTypes.SystemUInt64.FullName.ToString()] = "ulong",
            [WellKnownTypes.SystemUInt64.FullName.Name] = "ulong",
            [WellKnownTypes.SystemUIntPtr.FullName.ToString()] = "nuint",
            [WellKnownTypes.SystemUIntPtr.FullName.Name] = "nuint",
            [WellKnownTypes.SystemVoid.FullName.ToString()] = "void",
            [WellKnownTypes.SystemVoid.FullName.Name] = "void",
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
