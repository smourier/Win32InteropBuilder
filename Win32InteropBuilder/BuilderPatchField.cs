using System;
using Win32InteropBuilder.Model;

namespace Win32InteropBuilder
{
    public class BuilderPatchField
    {
        private readonly Lazy<BuilderMemberInput> _matcher;

        public BuilderPatchField()
        {
            _matcher = new Lazy<BuilderMemberInput>(() => new BuilderMemberInput(Name ?? string.Empty));
        }

        public virtual string? Name { get; set; }
        public virtual string? TypeName { get; set; }
        public virtual string? NewName { get; set; }
        public virtual string? Value { get; set; }

        public virtual bool Matches(BuilderMember member) => !_matcher.Value.IsReverse && _matcher.Value.Matches(member);

        public override string ToString() => $"{TypeName} {Name}";
    }
}
