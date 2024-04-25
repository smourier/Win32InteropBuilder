using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Win32InteropBuilder.Languages;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    public class Builder
    {
        public static JsonSerializerOptions JsonSerializerOptions { get; } = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new EncodingConverter(), new JsonStringEnumConverter() }
        };

        public static void Run(string configurationPath, string winMdPath, string outputDirectoryPath)
        {
            ArgumentNullException.ThrowIfNull(configurationPath);
            ArgumentNullException.ThrowIfNull(winMdPath);
            ArgumentNullException.ThrowIfNull(outputDirectoryPath);

            BuilderConfiguration? configuration;
            try
            {
                using var stream = File.OpenRead(configurationPath);
                configuration = JsonSerializer.Deserialize<BuilderConfiguration>(stream, JsonSerializerOptions);
                EnumBasedException<Win32InteropBuilderExceptionCode>.ThrowIfNull(Win32InteropBuilderExceptionCode.InvalidConfiguration, configuration);
            }
            catch (Exception ex)
            {
                throw new EnumBasedException<Win32InteropBuilderExceptionCode>(Win32InteropBuilderExceptionCode.InvalidConfiguration, ex);
            }

            if (configuration.BuilderTypeFilePath != null)
            {
                Assembly.LoadFrom(configuration.BuilderTypeFilePath);
            }

            if (configuration.Language?.TypeFilePath != null)
            {
                Assembly.LoadFrom(configuration.Language.TypeFilePath);
            }

            configuration.Language ??= new BuilderConfiguration.LanguageConfiguration();
            configuration.Language.TypeName ??= typeof(CSharpLanguage).AssemblyQualifiedName!;
            configuration.BuilderTypeName ??= typeof(Builder).AssemblyQualifiedName!;
            configuration.WinMdPath ??= winMdPath;
            configuration.OutputDirectoryPath ??= outputDirectoryPath;

            var builderType = Type.GetType(configuration.BuilderTypeName, true)!;
            var builder = (Builder)Activator.CreateInstance(builderType)!;

            var languageType = Type.GetType(configuration.Language.TypeName, true)!;
            var language = (ILanguage)Activator.CreateInstance(languageType)!;

            if (!string.IsNullOrEmpty(configuration.PatchesFilePath))
            {
                var patchesPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configurationPath)!, configuration.PatchesFilePath));
                if (IOUtilities.PathIsFile(patchesPath))
                {
                    try
                    {
                        using var stream = File.OpenRead(patchesPath);
                        configuration.Patches = JsonSerializer.Deserialize<BuilderPatches>(stream, JsonSerializerOptions);
                    }
                    catch (Exception ex)
                    {
                        throw new EnumBasedException<Win32InteropBuilderExceptionCode>(Win32InteropBuilderExceptionCode.InvalidPatchesConfiguration, ex);
                    }

                }
            }

            // reread file to configure per language
            using var stream2 = File.OpenRead(configurationPath);
            var configurationDic = JsonSerializer.Deserialize<Dictionary<string, object>>(stream2, JsonSerializerOptions);
            if (configurationDic?.TryGetValue(nameof(configuration.Language), out var languageConfig) == true &&
                languageConfig is JsonElement element &&
                element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(language.Name, out var perLangConfig))
            {
                language.Configure(perLangConfig);
            }

            var context = builder.CreateBuilderContext(configuration, language);
            context.Logger = new ConsoleLogger();
            context.LogInfo($"Builder type name: {builder.GetType().FullName}");
            context.LogInfo($"WinMd path: {configuration.WinMdPath}");
            context.LogInfo($"Architecture: {configuration.Architecture}");
            context.LogInfo($"Language: {language.Name}");
            context.LogInfo($"Output path: {configuration.OutputDirectoryPath}");
            context.LogInfo($"Output encoding: {configuration.FinalOutputEncoding.WebName}");
            context.LogInfo($"Running {builderType!.FullName} builder...");
            context.LogInfo();
            builder.Build(context);
        }

        public virtual BuilderContext CreateBuilderContext(BuilderConfiguration configuration, ILanguage language) => new(configuration, language);

        public virtual void Build(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Configuration.WinMdPath == null)
                throw new ArgumentException(null, nameof(context));

            var arch = context.Configuration.Architecture;
            if (arch != Model.Architecture.X86 & arch != Model.Architecture.X64 && arch != Model.Architecture.Arm64)
                throw new EnumBasedException<Win32InteropBuilderExceptionCode>(Win32InteropBuilderExceptionCode.UnsupportedArchitecture, $"Architecture: {arch}");

            using var stream = File.OpenRead(context.Configuration.WinMdPath);
            using var pe = new PEReader(stream);
            context.MetadataReader = pe.GetMetadataReader();
            GatherTypes(context);
            if (context.Configuration.GenerateFiles)
            {
                GenerateTypes(context);
            }
        }

        protected virtual void GatherTypes(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.MetadataReader);

            AddWellKnownTypes(context);
            var includes = new HashSet<BuilderType>();
            var excludes = new HashSet<BuilderType>();
            var includedByMembers = new HashSet<BuilderType>();

            foreach (var typeDef in context.MetadataReader.TypeDefinitions.Select(context.MetadataReader.GetTypeDefinition))
            {
                var type = context.CreateBuilderType(typeDef);
                if (type == null)
                    continue;

                type.IsNested = typeDef.IsNested;
                context.CurrentTypes.Push(type);
                try
                {
                    context.AllTypes[type.FullName] = type;
                    context.TypeDefinitions[type.FullName] = typeDef;

                    if (context.Configuration.MemberInputs.Count > 0)
                    {
                        foreach (var methodHandle in typeDef.GetMethods())
                        {
                            var methodDef = context.MetadataReader.GetMethodDefinition(methodHandle);
                            var method = context.CreateBuilderMethod(context.MetadataReader.GetString(methodDef.Name));
                            method.Handle = methodHandle;
                            foreach (var match in context.Configuration.MemberInputs.Where(x => x.Matches(method)))
                            {
                                includedByMembers.Add(type);
                                if (match.IsReverse)
                                {
                                    type.ExcludedMethods.Add(methodHandle);
                                }
                                else
                                {
                                    type.IncludedMethods.Add(methodHandle);
                                }
                            }
                        }

                        foreach (var fieldHandle in typeDef.GetFields())
                        {
                            var fieldDef = context.MetadataReader.GetFieldDefinition(fieldHandle);
                            var field = context.CreateBuilderField(context.MetadataReader.GetString(fieldDef.Name));
                            field.Handle = fieldHandle;
                            foreach (var match in context.Configuration.MemberInputs.Where(x => x.Matches(field)))
                            {
                                includedByMembers.Add(type);
                                if (match.IsReverse)
                                {
                                    type.ExcludedFields.Add(fieldHandle);
                                }
                                else
                                {
                                    type.IncludedFields.Add(fieldHandle);
                                }
                            }
                        }
                    }

                    foreach (var match in context.Configuration.TypeInputs.Where(x => x.Matches(type)))
                    {
                        includedByMembers.Remove(type);
                        type.IncludedFields.Clear();
                        type.IncludedMethods.Clear();
                        if (!match.IsReverse)
                        {
                            includes.Add(type);
                            excludes.Remove(type);
                        }
                        else
                        {
                            excludes.Add(type);
                            includes.Remove(type);
                        }
                    }
                }
                finally
                {
                    if (context.CurrentTypes.Pop() != type)
                        throw new InvalidOperationException();
                }
            }

            foreach (var match in excludes.ToArray())
            {
                includes.Remove(match);
            }

            foreach (var type in includedByMembers)
            {
                // only really include if it has included methods or fields
                if (type.IncludedMethods.Count > 0 || type.IncludedFields.Count > 0)
                {
                    includes.Add(type);
                }
            }

            foreach (var type in includes)
            {
                context.AddDependencies(type);
            }

            RemoveNonGeneratedTypes(context);
        }

        protected virtual void RemoveNonGeneratedTypes(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.Generation);

            context.TypesToBuild.Remove(new BuilderType(FullName.IUnknown));

            if (context.Configuration.Generation.HandleToIntPtr)
            {
                RemoveHandleTypes(context);
                throw new NotSupportedException(); // yet
            }
        }

        protected virtual void RemoveHandleTypes(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.MetadataReader);
            foreach (var type in context.TypesToBuild.ToArray())
            {
                if (context.TypeDefinitions.TryGetValue(type.FullName, out var typeDef) &&
                    context.MetadataReader.IsHandle(typeDef, context.SignatureTypeProvider))
                {
                    context.TypesToBuild.Remove(type);
                }
            }
        }

        protected virtual void AddWellKnownTypes(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            foreach (var kv in WellKnownTypes.All)
            {
                context.AllTypes[kv.Key] = kv.Value;
            }
        }

        protected virtual void AddMappedTypes(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.MappedTypes[FullName.BOOL] = WellKnownTypes.SystemBoolean;
            context.MappedTypes[FullName.IUnknown] = WellKnownTypes.SystemIntPtr;
        }

        protected virtual void GenerateTypes(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.MetadataReader);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.Generation);
            ArgumentNullException.ThrowIfNull(context.Configuration.OutputDirectoryPath);

            if (context.Configuration.DeleteOutputDirectory)
            {
                IOUtilities.DirectoryDelete(context.Configuration.OutputDirectoryPath, true);
            }

            if (context.Configuration.RemoveNonGeneratedFiles && IOUtilities.PathIsDirectory(context.Configuration.OutputDirectoryPath))
            {
                context.ExistingFiles.AddRange(Directory.EnumerateFileSystemEntries(context.Configuration.OutputDirectoryPath, "*.cs", SearchOption.AllDirectories));
            }

            AddMappedTypes(context);

            // first pass to compute duplicate file names if unified namespaces
            var un = context.Configuration.GetUnifiedGeneration();
            if (un != null)
            {
                var duplicateFiles = new Dictionary<string, List<BuilderType>>(StringComparer.OrdinalIgnoreCase);
                foreach (var type in context.TypesToBuild.OrderBy(t => t.FullName))
                {
                    var finalType = type;
                    if (context.MappedTypes.TryGetValue(type.FullName, out var mappedType))
                    {
                        finalType = mappedType;
                    }
                    if (!finalType.IsGenerated)
                        continue;

                    if (finalType.GetType() == typeof(BuilderType))
                    {
                        // base type handling only (including constants & functions)
                        var ico = isConstants();
                        var ifu = isFunctions();
                        if (!ico && !ifu)
                        {
                            if (un.ConstantsFileName != null)
                            {
                                context.TypesWithConstants.Add(finalType);
                            }

                            if (un.FunctionsFileName != null)
                            {
                                context.TypesWithFunctions.Add(finalType);
                            }

                            // nothing to do here?
                            if (un.ConstantsFileName != null && un.FunctionsFileName != null)
                            {
                                finalType.IsGenerated = false;
                                continue;
                            }
                        }

                        bool isConstants() => finalType.FullName.Namespace == un.Namespace && finalType.FullName.Name == un.ConstantsFileName;
                        bool isFunctions() => finalType.FullName.Namespace == un.Namespace && finalType.FullName.Name == un.FunctionsFileName;
                    }

                    var ns = context.MapGeneratedFullName(finalType.FullName).Namespace.Replace('.', Path.DirectorySeparatorChar);
                    var fileName = finalType.FileName + context.Language.FileExtension;
                    var typePath = Path.Combine(context.Configuration.OutputDirectoryPath, ns, fileName);

                    if (!duplicateFiles.TryGetValue(typePath, out var list))
                    {
                        list = [];
                        duplicateFiles[typePath] = list;
                    }

                    duplicateFiles[typePath].Add(finalType);
                }

                // try to come up with unique names for duplicate files
                foreach (var kv in duplicateFiles.Where(k => k.Value.Count > 1))
                {
                    var list = kv.Value;
                    var names = new string[list.Count];
                    var indices = new int[list.Count];
                    var fns = new string[list.Count];
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i] is InlineArrayType inlineArray)
                        {
                            fns[i] = inlineArray.ElementType.FullName.ToString();
                        }
                        else
                        {
                            fns[i] = list[i].FullName.ToString();
                        }
                        indices[i] = fns[i].Length - 1;
                    }

                    var dedup = false;
                    do
                    {
                        for (var i = 0; i < list.Count; i++)
                        {
                            var pos = fns[i].LastIndexOf('.', indices[i]);
                            if (pos < 0)
                            {
                                indices[i] = 0;
                                names[i] = fns[i];
                            }
                            else
                            {
                                indices[i] = pos - 1;
                                names[i] = fns[i][(pos + 1)..];
                            }
                        }

                        var uniques = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                        if (uniques.Count == names.Length &&
                            uniques.All(s => !duplicateFiles.TryGetValue(s, out var dups) || dups.Count == 1))
                        {
                            for (var i = 0; i < names.Length; i++)
                            {
                                FullName fn;
                                if (list[i] is InlineArrayType inlineArray)
                                {
                                    fn = InlineArrayType.BuildFullName(inlineArray.ElementType, inlineArray.Size, names[i].Replace(".", string.Empty));
                                }
                                else
                                {
                                    fn = new FullName(list[i].FullName.Namespace, names[i].Replace(".", string.Empty));
                                }

                                var clone = list[i].Clone(context, fn);
                                context.MappedTypes[list[i].FullName] = clone;
                            }

                            dedup = true;
                            break;
                        }

                        if (indices.All(i => i == 0))
                        {
                            // we've done our best but they apparently reside in same namespace,
                            // happens in rare cases at least on:
                            //
                            // Windows.Win32.Media.DirectShow.AVIStreamHeader
                            // Windows.Win32.Media.DirectShow.AVISTREAMHEADER
                            for (var i = 0; i < list.Count; i++)
                            {
                                list[i].FileName += "_" + i;
                            }

                            dedup = true;
                        }
                    }
                    while (!dedup);
                }
            }

            foreach (var type in context.TypesToBuild.OrderBy(t => t.FullName))
            {
                var finalType = type;
                if (context.MappedTypes.TryGetValue(type.FullName, out var mappedType))
                {
                    finalType = mappedType;
                }
                if (!finalType.IsGenerated)
                    continue;

                finalType.Generate(context);
            }

            if (un != null)
            {
                // build pseudo-types
                if (un.ConstantsFileName != null)
                {
                    var fields = context.TypesWithConstants.SelectMany(t => t.GeneratedFields).ToHashSet();
                    var constantsType = context.CreateBuilderType(new FullName(un.Namespace!, un.ConstantsFileName));
                    constantsType.Attributes |= BuilderTypeAttributes.IsUnifiedConstants;
                    constantsType.TypeAttributes |= TypeAttributes.Abstract | TypeAttributes.Sealed; // static
                    constantsType.Fields.AddRange(fields);

                    if (constantsType.IsGenerated)
                    {
                        constantsType.Generate(context);
                    }
                }

                if (un.FunctionsFileName != null)
                {
                    var functions = context.TypesWithFunctions.SelectMany(t => t.GeneratedMethods).ToHashSet();
                    var functionsType = context.CreateBuilderType(new FullName(un.Namespace!, un.FunctionsFileName));
                    functionsType.Attributes |= BuilderTypeAttributes.IsUnifiedFunctions;
                    functionsType.TypeAttributes |= TypeAttributes.Abstract | TypeAttributes.Sealed; // static
                    functionsType.Methods.AddRange(functions);

                    if (functionsType.IsGenerated)
                    {
                        functionsType.Generate(context);
                    }
                }
            }

            if (context.Configuration.RemoveNonGeneratedFiles)
            {
                foreach (var filePath in context.ExistingFiles)
                {
                    IOUtilities.FileDelete(filePath);
                }
                IOUtilities.DirectoryDeleteEmptySubDirectories(context.Configuration.OutputDirectoryPath);
            }
        }
    }
}
