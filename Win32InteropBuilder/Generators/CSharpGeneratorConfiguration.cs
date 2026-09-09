using System;
using System.Collections.Generic;
using System.Linq;
using Win32InteropBuilder.Model;

namespace Win32InteropBuilder.Generators
{
    public class CSharpGeneratorConfiguration
    {
        public virtual bool GenerateTypeKeywords { get; set; } = true;
        public virtual ISet<BuilderFullNameInput> SupportedConstantTypes { get; set; } = new HashSet<BuilderFullNameInput>();
        public virtual ISet<BuilderFullNameInput> MarshalAsErrorTypes { get; set; } = new HashSet<BuilderFullNameInput>();

        public virtual bool IsSupportedAsConstant(FullName fullName)
        {
            ArgumentNullException.ThrowIfNull(fullName);
            if (SupportedConstantTypes.Count == 0) // special case means all
                return true;

            return SupportedConstantTypes.Any(t => !t.IsReverse && t.Matches(fullName));
        }

        public virtual bool MarshalAsError(FullName fullName)
        {
            ArgumentNullException.ThrowIfNull(fullName);
            return MarshalAsErrorTypes.Any(t => !t.IsReverse && t.Matches(fullName));
        }

        internal void AddSupportedConstantType(FullName fullName) => AddFullName(SupportedConstantTypes, fullName);
        internal void AddMarshalAsErrorType(FullName fullName) => AddFullName(MarshalAsErrorTypes, fullName);
        private static void AddFullName(ISet<BuilderFullNameInput> set, FullName fullName)
        {
            if (set.Any(b => b.IsReverse && b.Matches(fullName)))
                return;

            set.Add(new BuilderFullNameInput(fullName));
        }

        internal void ClearSupportedConstantTypeReverses() => ClearReverse(SupportedConstantTypes);
        internal void ClearMarshalAsErrorTypeReverses() => ClearReverse(MarshalAsErrorTypes);
        private static void ClearReverse(ISet<BuilderFullNameInput> set)
        {
            // now remove all reverse we just keep an inclusion list
            foreach (var type in set.Where(t => t.IsReverse).ToArray())
            {
                set.Remove(type);
            }
        }
    }
}
