using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder.Model
{
    public class BuilderTypeExtension(BuilderType rootType)
    {
        public const string DefaultPostfix = "Extensions";

        private string? _fileName;

        public BuilderType RootType => rootType;

        public virtual HashSet<BuilderType> Types { get; } = [];
        public virtual bool IsGenerated { get; set; } = true;
        public virtual string FileName { get => _fileName ?? RootType.FileName + DefaultPostfix; set => _fileName = value; }

        public override string ToString() => RootType.ToString();

        public virtual string GetGeneratedName(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return RootType.GetGeneratedName(context) + DefaultPostfix;
        }

        public virtual string Generate(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.ExtensionsOutputDirectoryPath);
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
