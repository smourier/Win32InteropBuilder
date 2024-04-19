using System;
using System.Collections.Generic;
using System.Linq;
using Win32InteropBuilder.Model;

namespace Win32InteropBuilder.Languages
{
    public class CSharpLanguageConfiguration
    {
        public virtual bool GenerateTypeKeywords { get; set; } = true;
        public virtual ISet<BuilderFullNameInput> SupportedConstantTypes { get; set; } = new HashSet<BuilderFullNameInput>();

        public virtual bool IsSupportedAsConstant(FullName fullName)
        {
            ArgumentNullException.ThrowIfNull(fullName);
            if (SupportedConstantTypes.Count == 0)
                return true;

            return SupportedConstantTypes.Any(t => !t.IsReverse && t.Matches(fullName));
        }
    }
}
