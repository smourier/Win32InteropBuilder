﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    public class BuilderConfiguration
    {
        public virtual string? BuilderTypeFilePath { get; set; }
        public virtual string? BuilderTypeName { get; set; }
        public virtual string? WinMdPath { get; set; }
        public virtual string? OutputDirectoryPath { get; set; }
        public virtual string? PatchesFilePath { get; set; } = "Patches.json";
        public virtual BuilderPatches? Patches { get; set; }
        public virtual bool DeleteOutputDirectory { get; set; }
        public virtual bool GenerateFiles { get; set; } = true;
        public virtual Encoding? OutputEncoding { get; set; }
        public virtual EncodingDetectorMode EncodingDetectorMode { get; set; }
        public virtual bool RemoveNonGeneratedFiles { get; set; } = true;
        public virtual IList<BuilderTypeInput> TypeInputs { get; set; } = [];
        public virtual IList<BuilderMemberInput> MemberInputs { get; set; } = [];
        public virtual Encoding FinalOutputEncoding => OutputEncoding ?? Encoding.UTF8;
        public virtual BuilderGeneration Generation { get; set; } = new();
        public virtual GeneratorConfiguration Generator { get; set; } = new();
        public virtual Architecture Architecture { get; set; } = GetCurrentArchitecture();

        private static Architecture GetCurrentArchitecture()
        {
            switch (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture)
            {
                case System.Runtime.InteropServices.Architecture.X64:
                    return Architecture.X64;

                case System.Runtime.InteropServices.Architecture.Arm64:
                    return Architecture.Arm64;

                case System.Runtime.InteropServices.Architecture.X86:
                    return Architecture.X86;

                default:
                    throw new NotSupportedException();
            }
        }

        public class BuilderGeneration
        {
            public virtual ComOutPtrTarget UnknownComOutPtrTarget { get; set; } = ComOutPtrTarget.IntPtr;
            public virtual ComOutPtrTarget ComOutPtrTarget { get; set; } = ComOutPtrTarget.UniqueObject;
            public virtual bool AddNullableEnable { get; set; } = true;
            public virtual UnifiedGeneration Unified { get; set; } = new();
            public virtual SetLastErrorMode SetLastErrorMode { get; set; }

            public class UnifiedGeneration
            {
                public const string DefaultFunctionsFileName = "Functions";
                public const string DefaultConstantsFileName = "Constants";

                public virtual string? Namespace { get; set; }
                public virtual string? FunctionsFileName { get; set; } = DefaultFunctionsFileName;
                public virtual string? ConstantsFileName { get; set; } = DefaultConstantsFileName;
            }
        }

        public enum SetLastErrorMode
        {
            Auto,
            Attributed,
        }

        public class GeneratorConfiguration
        {
            public virtual string? TypeFilePath { get; set; }
            public virtual string? TypeName { get; set; }
        }

        internal BuilderPatchType? GetTypePatch(BuilderType type)
        {
            if (Patches == null)
                return null;

            return Patches.Types?.FirstOrDefault(t => t.Matches(type));
        }

        internal BuilderGeneration.UnifiedGeneration? GetUnifiedGeneration()
        {
            var ns = Generation?.Unified?.Namespace.Nullify();
            if (ns == null)
                return null;

            Generation!.Unified.Namespace = ns;
            Generation.Unified.FunctionsFileName = Generation.Unified.FunctionsFileName.Nullify();
            Generation.Unified.ConstantsFileName = Generation.Unified.ConstantsFileName.Nullify();
            return Generation.Unified;
        }
    }
}
