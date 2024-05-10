using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder.Model
{
    public class BuilderTypeExtension
    {
        public const string DefaultPostfix = "Extensions";

        private string? _fileName;
        private readonly List<BuilderTypeExtensionMethod> _methods = [];

        public BuilderTypeExtension(BuilderType rootType)
        {
            ArgumentNullException.ThrowIfNull(rootType);
            RootType = rootType;
        }

        public BuilderType RootType { get; }
        public virtual IList<BuilderTypeExtensionMethod> Methods => _methods;
        public virtual HashSet<BuilderType> Types { get; } = [];
        public virtual bool IsGenerated { get; set; } = true;
        public virtual string FileName { get => _fileName ?? RootType.FileName + DefaultPostfix; set => _fileName = value; }

        public override string ToString() => RootType.ToString();

        public virtual void BuildMethods(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (RootType is not InterfaceType rif)
                return;

            var types = new List<InterfaceType> { rif };
            types.AddRange(Types.OfType<InterfaceType>().OrderBy(t => t, BuilderTypeHierarchyComparer.Instance));

            foreach (var type in types)
            {
                foreach (var method in type.Methods)
                {
                    if (!context.HasExtensions(this, type, method))
                        continue;

                    Methods.AddRange(BuildMethods(context, type, method));
                }
            }
        }

        protected virtual IEnumerable<BuilderTypeExtensionMethod> BuildMethods(BuilderContext context, InterfaceType type, BuilderMethod sourceMethod)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(sourceMethod);

            var raw = BuildRawMethod(context, type, sourceMethod);
            if (raw == null) // all other are based on raw
                yield break;

            yield return raw;

            // don't build if more than one out parameter
            // determine return type
        }

        protected virtual BuilderTypeExtensionMethod? BuildRawMethod(BuilderContext context, InterfaceType type, BuilderMethod sourceMethod)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(sourceMethod);
            if (sourceMethod.ReturnType == null)
                throw new InvalidOperationException();

            var isHResult = sourceMethod.ReturnType.FullName == FullName.HRESULT;

            var outParams = sourceMethod.Parameters.Where(CanBeReturnValue).ToArray();
            if (outParams.Length > 1)
            {
            }
            return null;
        }

        protected virtual bool IsOutParameter(BuilderParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);

            if (parameter.IsComOutPtr)
                return true;

            if (parameter.Attributes.HasFlag(System.Reflection.ParameterAttributes.Out))
                return true;

            return false;
        }

        protected virtual bool CanBeReturnValue(BuilderParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            if (!IsOutParameter(parameter))
                return false;

            if (parameter.IsComOutPtr)
                return true;

            return true;
        }

        public virtual string GetGeneratedName(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return RootType.GetGeneratedName(context) + DefaultPostfix;
        }

        public virtual string? Generate(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.ExtensionsOutputDirectoryPath);

            BuildMethods(context);
            if (Methods.Count == 0)
                return null;

            using var writer = new StringWriter();
            context.CurrentWriter = new IndentedTextWriter(writer);
            try
            {
                GenerateCode(context);
            }
            finally
            {
                context.CurrentWriter.Dispose();
                context.CurrentWriter = null;
            }

            var text = writer.ToString();
            var fileName = FileName + context.Generator.FileExtension;
            var typePath = Path.Combine(context.Configuration.ExtensionsOutputDirectoryPath, fileName);

            if (IOUtilities.PathIsFile(typePath))
            {
                var existingText = EncodingDetector.ReadAllText(typePath, context.Configuration.EncodingDetectorMode, out _);
                if (text == existingText)
                    return typePath;
            }

            IOUtilities.FileEnsureDirectory(typePath);

            context.LogVerbose(RootType.FullName + " extensions => " + typePath);
            File.WriteAllText(typePath, text, context.Configuration.FinalOutputEncoding);
            return typePath;
        }

        public virtual void GenerateCode(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.Generator);
            context.Generator.GenerateExtension(context, this);
        }
    }
}
