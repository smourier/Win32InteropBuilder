using System;
using System.Collections.Generic;
using Win32InteropBuilder.Model;

namespace Win32InteropBuilder
{
    public class BuilderPatchType
    {
        private readonly Lazy<BuilderInput<BuilderType>> _matcher;

        public BuilderPatchType()
        {
            _matcher = new Lazy<BuilderInput<BuilderType>>(() => new BuilderTypeInput(TypeName ?? string.Empty));
        }

        public virtual string? TypeName { get; set; }
        public virtual IList<BuilderPatchMethod> Methods { get; set; } = [];
        public virtual IList<BuilderPatchField> Fields { get; set; } = [];

        public virtual bool Matches(BuilderType type) => !_matcher.Value.IsReverse && _matcher.Value.Matches(type);

        public override string ToString() => $"{TypeName} ({string.Join(Environment.NewLine, Methods)}) ({string.Join(Environment.NewLine, Fields)})";
    }
}
