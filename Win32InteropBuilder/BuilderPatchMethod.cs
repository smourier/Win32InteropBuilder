using System;
using System.Collections.Generic;
using Win32InteropBuilder.Model;

namespace Win32InteropBuilder
{
    public class BuilderPatchMethod
    {
        private readonly Lazy<BuilderMemberInput> _matcher;

        public BuilderPatchMethod()
        {
            _matcher = new Lazy<BuilderMemberInput>(() => new BuilderMemberInput(Name ?? string.Empty));
        }

        public virtual string? Name { get; set; } // can be a number => index
        public virtual string? TypeName { get; set; }
        public virtual string? NewName { get; set; }
        public virtual bool? SetLastError { get; set; }
        public virtual IList<BuilderPatchParameter> Parameters { get; set; } = [];

        public virtual bool Matches(BuilderMember member) => !_matcher.Value.IsReverse && _matcher.Value.Matches(member);

        public override string ToString() => $"{TypeName} {Name} ({string.Join(", ", Parameters)})";
    }
}
