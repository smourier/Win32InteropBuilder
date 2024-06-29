using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    public class BuilderContext
    {
        public BuilderContext(BuilderConfiguration configuration, IGenerator generator)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(generator);
            Configuration = configuration;
            Generator = generator;
            SignatureTypeProvider = new SignatureTypeProvider(this);
            CustomAttributeTypeProvider = new CustomAttributeTypeProvider(this);
            ImplicitNamespaces.Add("System");
            ImplicitNamespaces.Add(BuilderType.GeneratedInteropNamespace);
        }

        public BuilderConfiguration Configuration { get; }
        public IGenerator Generator { get; }
        public virtual ILogger? Logger { get; set; }
        public virtual SignatureTypeProvider SignatureTypeProvider { get; }
        public virtual CustomAttributeTypeProvider CustomAttributeTypeProvider { get; }
        public virtual MetadataReader? MetadataReader { get; set; }
        public virtual HashSet<FullName> TypesToBuild { get; } = [];
        public virtual IDictionary<FullName, BuilderType> AllTypes { get; } = new Dictionary<FullName, BuilderType>();
        public virtual IDictionary<FullName, TypeDefinition> TypeDefinitions { get; } = new Dictionary<FullName, TypeDefinition>();
        public virtual IDictionary<FullName, BuilderType> MappedTypes { get; } = new Dictionary<FullName, BuilderType>();
        public virtual ISet<string> ImplicitNamespaces { get; } = new HashSet<string>();
        public virtual ISet<BuilderType> TypesWithFunctions { get; } = new HashSet<BuilderType>();
        public virtual ISet<BuilderType> TypesWithConstants { get; } = new HashSet<BuilderType>();
        public virtual IDictionary<string, object> Constants { get; } = new Dictionary<string, object>();

        // changing properties
        public virtual IndentedTextWriter? CurrentWriter { get; set; }
        public virtual string? CurrentNamespace { get; set; }
        public virtual Stack<BuilderType> CurrentTypes { get; } = [];

        public virtual BuilderType CreateBuilderType(FullName fullName) => new(fullName);
        public virtual InterfaceType CreateInterfaceType(FullName fullName) => new(fullName);
        public virtual StructureType CreateStructureType(FullName fullName) => new(fullName);
        public virtual EnumType CreateEnumType(FullName fullName) => new(fullName);
        public virtual DelegateType CreateDelegateType(FullName fullName) => new(fullName);
        public virtual PointerType CreatePointerType(FullName fullName, int indirections) => new(fullName, indirections);
        public virtual ArrayType CreateArrayType(FullName fullName, ArrayShape shape) => new(fullName, shape);
        public virtual BuilderMethod CreateBuilderMethod(string name) => new(name);
        public virtual BuilderParameter CreateBuilderParameter(string name, int sequenceNumber) => new(name, sequenceNumber);
        public virtual BuilderField CreateBuilderField(string name) => new(name);
        public virtual BuilderType CreateInlineArrayType(BuilderType elementType, int size, FullName? fullName = null) => new InlineArrayType(elementType, size, fullName);

        public virtual FullName GetFullName(TypeDefinition typeDef)
        {
            if (MetadataReader == null)
                throw new InvalidOperationException();

            var fn = MetadataReader.GetFullName(typeDef);
            if (fn.Namespace == string.Empty)
            {
                var declaringTypeHandle = typeDef.GetDeclaringType();
                if (!declaringTypeHandle.IsNil)
                {
                    // nested classes
                    var declaringTypeDef = MetadataReader.GetTypeDefinition(declaringTypeHandle);
                    var declaringTypeFn = GetFullName(declaringTypeDef);
                    var nestedFn = new FullName(declaringTypeFn.Namespace, declaringTypeFn.Name + FullName.NestedTypesSeparator + fn.Name);
                    return nestedFn;
                }
            }
            return fn;
        }

        public virtual BuilderType? CreateBuilderType(TypeDefinition typeDef)
        {
            if (Configuration == null)
                throw new InvalidOperationException();

            // skip non corresponding architectures
            var arch = this.GetSupportedArchitecture(typeDef.GetCustomAttributes());
            if (arch != null && (arch & Configuration.Architecture) != Configuration.Architecture)
                return null;

            var type = CreateBuilderTypeCore(typeDef);
            return type;
        }

        protected virtual BuilderType CreateBuilderTypeCore(TypeDefinition typeDef)
        {
            if (MetadataReader == null)
                throw new InvalidOperationException();

            var fn = GetFullName(typeDef);
            if (typeDef.Attributes.HasFlag(TypeAttributes.Interface))
                return CreateInterfaceType(fn);

            var isStructure = MetadataReader.IsValueType(typeDef);
            if (isStructure)
                return CreateStructureType(fn);

            var isEnum = MetadataReader.IsEnum(typeDef);
            if (isEnum)
                return CreateEnumType(fn);

            var isDelegate = MetadataReader.IsDelegate(typeDef);
            if (isDelegate)
                return CreateDelegateType(fn);

            return CreateBuilderType(fn);
        }

        public virtual void AddDependencies(FullName typeFullName)
        {
            ArgumentNullException.ThrowIfNull(typeFullName);
            typeFullName = typeFullName.NoPointerFullName;
            if (MetadataReader == null)
                throw new InvalidOperationException();

            if (!TypeDefinitions.TryGetValue(typeFullName, out var typeDef))
                return;

            if (TypesToBuild.Contains(typeFullName))
                return;

            AllTypes[typeFullName].ResolveType(this, typeDef);
        }

        public virtual BuilderType MapType(FullName typeFullName)
        {
            ArgumentNullException.ThrowIfNull(typeFullName);
            if (!MappedTypes.TryGetValue(typeFullName, out var mapped))
            {
                var nofn = typeFullName.NoPointerFullName;
                if (!MappedTypes.TryGetValue(nofn, out mapped))
                    return AllTypes[typeFullName];

                var pt = CreatePointerType(mapped.FullName, typeFullName.Indirections);
                if (!AllTypes.TryGetValue(pt.FullName, out mapped))
                {
                    AllTypes[pt.FullName] = pt;
                    mapped = pt;
                }
            }

            return mapped;
        }

        public virtual FullName MapGeneratedFullName(FullName fullName)
        {
            ArgumentNullException.ThrowIfNull(fullName);
            ArgumentNullException.ThrowIfNull(Configuration);
            ArgumentNullException.ThrowIfNull(Configuration.Generation);
            var un = Configuration.GetUnifiedGeneration();
            if (un != null)
                return new FullName(un.Namespace!, FullName.HRESULT.Name);

            return fullName;
        }

        public virtual bool IsConstableType(FullName typeFullName)
        {
            ArgumentNullException.ThrowIfNull(typeFullName);
            if (AllTypes[typeFullName] is EnumType)
                return true;

            return Generator.ConstableTypes.Contains(typeFullName);
        }

        public virtual string GetValueAsString(BuilderType type, object? value, string defaultValueAsString)
        {
            ArgumentNullException.ThrowIfNull(type);
            return defaultValueAsString;
        }

        public virtual string GetConstantValue(BuilderType type, Model.Constant constant)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(constant);
            return constant.ValueAsText;
        }

        public virtual ParameterDef GetParameterDef(BuilderParameter parameter, ParameterDef defaultDef)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(defaultDef);
            return defaultDef;
        }

        public virtual BuilderType? GetTypeFromValue(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var valueType = value.GetType();
            foreach (var kv in WellKnownTypes.All)
            {
                if (kv.Value.ClrType != null && kv.Value.ClrType == valueType)
                    return kv.Value;
            }
            return null;
        }

        public virtual bool IsHandleType(BuilderType type, TypeDefinition typeDef)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(MetadataReader);
            if (type is not StructureType)
                return false;

            if (!MetadataReader.IsHandle(typeDef, SignatureTypeProvider))
                return false;

            if (type.FullName == FullName.LRESULT)
                return false;

            return true;
        }

        // last error is globally not well defined in Win32metadata
        // so the logic is to set it when it's not defined even if it's not always ok
        public virtual bool HasSetLastError(BuilderMethod method)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(Configuration);
            ArgumentNullException.ThrowIfNull(Configuration.Generation);

            if (method.ImportAttributes.HasFlag(MethodImportAttributes.SetLastError))
                return true;

            if (Configuration.Generation.SetLastErrorMode != BuilderConfiguration.SetLastErrorMode.Auto || method.ReturnTypeFullName == null)
                return false;

            // void is rarely last error
            if (method.ReturnTypeFullName == WellKnownTypes.SystemVoid.FullName)
                return false;

            // HRESULT is never last error
            if (method.ReturnTypeFullName == FullName.HRESULT)
                return false;

            // bool is often last error
            if (method.ReturnTypeFullName == WellKnownTypes.SystemBoolean.FullName ||
                method.ReturnTypeFullName == FullName.BOOL)
                return true;

            // handle often set last errrors
            if (method.ReturnTypeFullName == WellKnownTypes.SystemIntPtr.FullName ||
                method.ReturnTypeFullName == WellKnownTypes.SystemUIntPtr.FullName)
                return true;

            var rt = AllTypes[method.ReturnTypeFullName];
            if (rt.IsHandle)
                return true;

            return false;
        }

        public void LogInfo(object? message = null, [CallerMemberName] string? methodName = null) => Log(TraceLevel.Info, message, methodName);
        public void LogWarning(object? message = null, [CallerMemberName] string? methodName = null) => Log(TraceLevel.Warning, message, methodName);
        public void LogError(object? message = null, [CallerMemberName] string? methodName = null) => Log(TraceLevel.Error, message, methodName);
        public void LogVerbose(object? message = null, [CallerMemberName] string? methodName = null) => Log(TraceLevel.Verbose, message, methodName);
        public virtual void Log(TraceLevel level, object? message = null, [CallerMemberName] string? methodName = null) => Logger?.Log(level, message, methodName);
    }
}
