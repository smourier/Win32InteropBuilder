﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Win32InteropBuilder.Generators;
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

        public static void Run(string configurationPath, string winMdPath)
        {
            ArgumentNullException.ThrowIfNull(configurationPath);
            ArgumentNullException.ThrowIfNull(winMdPath);

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

            if (configuration.Generator?.TypeFilePath != null)
            {
                Assembly.LoadFrom(configuration.Generator.TypeFilePath);
            }

            configuration.Generator ??= new BuilderConfiguration.GeneratorConfiguration();
            configuration.Generator.TypeName ??= typeof(CSharpGenerator).AssemblyQualifiedName!;
            configuration.BuilderTypeName ??= typeof(Builder).AssemblyQualifiedName!;
            configuration.WinMdPath ??= winMdPath;
            configuration.OutputDirectoryPath ??= Path.Combine(Path.GetDirectoryName(configurationPath).Nullify() ?? Path.GetFullPath("."), Path.GetFileNameWithoutExtension(configurationPath));

            var builderType = Type.GetType(configuration.BuilderTypeName, true)!;
            var builder = (Builder)Activator.CreateInstance(builderType)!;

            var generatorType = Type.GetType(configuration.Generator.TypeName, true)!;
            var generator = (IGenerator)Activator.CreateInstance(generatorType)!;

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

            // reread file to configure per generator
            using var stream2 = File.OpenRead(configurationPath);
            var configurationDic = JsonSerializer.Deserialize<Dictionary<string, object>>(stream2, JsonSerializerOptions);
            if (configurationDic?.TryGetValue(nameof(configuration.Generator), out var generatorConfig) == true &&
                generatorConfig is JsonElement element &&
                element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(generator.Name, out var perLangConfig))
            {
                generator.Configure(perLangConfig);
            }

            var context = builder.CreateBuilderContext(configuration, generator);
            context.Logger = new ConsoleLogger();
            context.LogInfo($"Builder type name: {builder.GetType().FullName}");
            context.LogInfo($"WinMd path: {configuration.WinMdPath}");
            context.LogInfo($"Architecture: {configuration.Architecture}");
            context.LogInfo($"Generator: {generator.Name}");
            context.LogInfo($"Output path: {configuration.OutputDirectoryPath}");
            context.LogInfo($"Output encoding: {configuration.FinalOutputEncoding.WebName}");
            context.LogInfo($"Running {builderType!.FullName} builder...");
            builder.Build(context);
            context.LogInfo("Builder has finished successfully.");
        }

        public virtual BuilderContext CreateBuilderContext(BuilderConfiguration configuration, IGenerator generator) => new(configuration, generator);

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
            RunGlobalPatches(context);
            if (context.Configuration.GenerateFiles)
            {
                GenerateTypes(context);
            }
        }

        protected virtual void RunGlobalPatches(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // meant to work around this: https://github.com/microsoft/win32metadata/issues/2086
            RunOptionalArgumentPatches(context);
        }

        protected virtual void RunOptionalArgumentPatches(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.Configuration);

            if (context.Configuration.Patches?.OptionalArguments == null || context.Configuration.Patches.OptionalArguments.Count == 0)
                return;

            var dic = new Dictionary<FullName, List<PatchMethod>>();
            foreach (var patch in context.Configuration.Patches.OptionalArguments)
            {
                if (patch == null)
                    continue;

                var split = patch.Split("::");
                if (split.Length != 3)
                    continue;

                var fn = new FullName(split[0]);
                if (!dic.TryGetValue(fn, out var methods))
                {
                    methods = [];
                    dic[fn] = methods;
                }

                var method = methods.Find(m => m.Name.EqualsIgnoreCase(split[1]));
                if (method == null)
                {
                    method = new PatchMethod(split[1]);
                    methods.Add(method);
                }

                if (!method.Arguments.Contains(split[2]))
                {
                    method.Arguments.Add(split[2]);
                }
            }

            foreach (var kv in dic)
            {
                var finalType = context.AllTypes[kv.Key];
                foreach (var method in kv.Value)
                {
                    var existing = finalType.Methods.FirstOrDefault(m => m.Name.EqualsIgnoreCase(method.Name));
                    if (existing == null)
                    {
                        context.LogWarning($"Cannot find method '{method.Name}' of '{kv.Key}' type for setting an optional argument.");
                        continue;
                    }

                    foreach (var arg in method.Arguments)
                    {
                        var existingArg = existing.Parameters.FirstOrDefault(p => p.Name.EqualsIgnoreCase(arg));
                        if (existingArg == null)
                        {
                            context.LogWarning($"Cannot find argument '{arg}' of method '{method.Name}' of '{kv.Key}' type.");
                            continue;
                        }

                        existingArg.Attributes |= ParameterAttributes.Optional;
                    }
                }
            }
        }

        private sealed class PatchMethod(string name)
        {
            public string Name { get; } = name;
            public List<string> Arguments { get; } = [];
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
            var includedByTypes = new HashSet<BuilderType>();
            var excludedByTypes = new HashSet<BuilderType>();

            foreach (var typeDef in context.MetadataReader.TypeDefinitions.Select(context.MetadataReader.GetTypeDefinition))
            {
                var type = context.CreateBuilderType(typeDef);
                if (type == null)
                    continue;

                IncludeTypeFromMetadata(context, type);
                type.IsNested = typeDef.IsNested;
                context.CurrentTypes.Push(type);

                try
                {
                    context.AllTypes[type.FullName] = type;
                    context.TypeDefinitions[type.FullName] = typeDef;

                    // we consider members add/remove only for non-enum non-structure non-interface non-delegate types
                    if (type.GetType() == typeof(BuilderType) && context.Configuration.MemberInputs.Count > 0)
                    {
                        foreach (var methodHandle in typeDef.GetMethods())
                        {
                            var methodDef = context.MetadataReader.GetMethodDefinition(methodHandle);
                            var method = context.CreateBuilderMethod(context.MetadataReader.GetString(methodDef.Name));
                            if (method == null)
                                continue;

                            method.Handle = methodHandle;
                            foreach (var match in context.Configuration.MemberInputs.Where(x => x.Matches(method)))
                            {
                                match.MatchesCount++;
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
                            if (field == null)
                                continue;

                            field.Handle = fieldHandle;
                            foreach (var match in context.Configuration.MemberInputs.Where(x => x.Matches(field)))
                            {
                                match.MatchesCount++;
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
                        if (match.IsReverse)
                        {
                            excludes.Add(type);
                            includes.Remove(type);
                            excludedByTypes.Add(type);
                        }
                        else
                        {
                            includes.Add(type);
                            excludes.Remove(type);
                            includedByTypes.Add(type);
                        }

                        if (match.Exclude)
                        {
                            type.IsGenerated = false;
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

            foreach (var type in includedByTypes)
            {
                if (type.FullName.ToString() == "Windows.Win32.Graphics.Gdi.Apis")
                {
                }
                if (excludedByTypes.Contains(type))
                    continue;

                type.IncludedFields.Clear();
                type.IncludedMethods.Clear();
            }

            foreach (var type in includes)
            {
                context.AddDependencies(type.FullName);
            }

            ExcludeTypesFromBuild(context);

            foreach (var input in context.Configuration.MemberInputs.Where(m => m.MatchesCount == 0))
            {
                context.LogWarning("No match for member input " + input);
            }
        }

        protected virtual void IncludeTypeFromMetadata(BuilderContext context, BuilderType type)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(type);
        }

        protected virtual void ExcludeTypesFromBuild(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.Configuration);
            ArgumentNullException.ThrowIfNull(context.Configuration.Generation);

            context.TypesToBuild.Remove(FullName.IUnknown);
        }

        // unsed today
        protected virtual void RemoveHandleTypes(BuilderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.MetadataReader);
            foreach (var type in context.TypesToBuild.ToArray().Where(t => context.AllTypes[t].IsHandle))
            {
                context.TypesToBuild.Remove(type);
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
            context.MappedTypes[FullName.DECIMAL] = WellKnownTypes.SystemDecimal;
            context.MappedTypes[FullName.IUnknown] = WellKnownTypes.SystemIntPtr;
            context.MappedTypes[FullName.IUnknownPtr] = WellKnownTypes.SystemIntPtr;
            context.MappedTypes[FullName.FARPROC] = WellKnownTypes.SystemIntPtr;
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

            var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (context.Configuration.RemoveNonGeneratedFiles && IOUtilities.PathIsDirectory(context.Configuration.OutputDirectoryPath))
            {
                existingFiles.AddRange(Directory.EnumerateFileSystemEntries(context.Configuration.OutputDirectoryPath, "*.cs", SearchOption.AllDirectories));
            }

            AddMappedTypes(context);

            // first pass to compute duplicate file names if unified namespaces
            var un = context.Configuration.GetUnifiedGeneration();
            if (un != null)
            {
                var duplicateFiles = new Dictionary<string, List<BuilderType>>(StringComparer.OrdinalIgnoreCase);
                foreach (var typeFullName in context.TypesToBuild.OrderBy(t => t))
                {
                    var finalType = context.AllTypes[typeFullName];
                    if (context.MappedTypes.TryGetValue(typeFullName, out var mappedType))
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

                        bool isConstants() => finalType.Namespace == un.Namespace && finalType.Name == un.ConstantsFileName;
                        bool isFunctions() => finalType.Namespace == un.Namespace && finalType.Name == un.FunctionsFileName;
                    }

                    var ns = context.MapGeneratedFullName(finalType.FullName).Namespace.Replace('.', Path.DirectorySeparatorChar);
                    var fileName = finalType.FileName + context.Generator.FileExtension;
                    var typePath = Path.Combine(context.Configuration.OutputDirectoryPath, ns, fileName);

                    if (!duplicateFiles.TryGetValue(typePath, out var list))
                    {
                        list = [];
                        duplicateFiles[typePath] = list;
                    }

                    var dups = duplicateFiles[typePath];
                    if (!dups.Contains(finalType))
                    {
                        dups.Add(finalType);
                    }
                    else
                    {
                        context.TypesToBuild.Remove(typeFullName);
                    }
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
                                    fn = new FullName(list[i].Namespace, names[i].Replace(".", string.Empty));
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

            foreach (var typeFullName in context.TypesToBuild.OrderBy(t => t))
            {
                var finalType = context.AllTypes[typeFullName];
                if (context.MappedTypes.TryGetValue(typeFullName, out var mappedType))
                {
                    finalType = mappedType;
                }
                if (!finalType.IsGenerated || finalType.IsNested)
                    continue;

                var typePath = finalType.Generate(context);
                if (typePath != null)
                {
                    existingFiles.Remove(typePath);
                }
            }

            if (un != null)
            {
                // build pseudo-types
                if (un.ConstantsFileName != null)
                {
                    var fields = context.TypesWithConstants.SelectMany(t => t.GeneratedFields).ToHashSet();
                    var constantsType = context.CreateBuilderType(new FullName(un.Namespace!, un.ConstantsFileName));
                    if (constantsType != null)
                    {
                        constantsType.Attributes |= BuilderTypeAttributes.IsUnifiedConstants;
                        constantsType.TypeAttributes |= TypeAttributes.Abstract | TypeAttributes.Sealed; // static
                        constantsType.Fields.AddRange(fields);

                        if (constantsType.IsGenerated)
                        {
                            // add manually defined constants
                            foreach (var kv in context.Constants)
                            {
                                if (constantsType.Fields.Any(f => f.Name == kv.Key))
                                    continue;

                                var field = context.CreateBuilderField(kv.Key);
                                if (field == null)
                                    continue;

                                field.DefaultValue = kv.Value;
                                field.TypeFullName = context.GetTypeFromValue(kv.Value)?.FullName;
                                if (field.TypeFullName == null)
                                    throw new InvalidOperationException();

                                constantsType.Fields.Add(field);
                            }

                            var typePath = constantsType.Generate(context);
                            if (typePath != null)
                            {
                                existingFiles.Remove(typePath);
                            }
                        }
                    }
                }

                if (un.FunctionsFileName != null)
                {
                    var functions = context.TypesWithFunctions.SelectMany(t => t.GeneratedMethods).ToHashSet();
                    var functionsType = context.CreateBuilderType(new FullName(un.Namespace!, un.FunctionsFileName));
                    if (functionsType != null)
                    {
                        functionsType.Attributes |= BuilderTypeAttributes.IsUnifiedFunctions;
                        functionsType.TypeAttributes |= TypeAttributes.Abstract | TypeAttributes.Sealed; // static
                        functionsType.Methods.AddRange(functions);

                        if (functionsType.IsGenerated)
                        {
                            var typePath = functionsType.Generate(context);
                            if (typePath != null)
                            {
                                existingFiles.Remove(typePath);
                            }
                        }
                    }
                }
            }

            if (context.Configuration.RemoveNonGeneratedFiles)
            {
                foreach (var filePath in existingFiles)
                {
                    IOUtilities.FileDelete(filePath);
                }
                IOUtilities.DirectoryDeleteEmptySubDirectories(context.Configuration.OutputDirectoryPath);
            }
        }
    }
}
