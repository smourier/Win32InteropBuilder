using System;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder.Model
{
    public class BuilderMember : IDocumentable, ISupportable, INameable
    {
        public BuilderMember(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            Name = name;
        }

        public virtual string? Documentation { get; set; }
        public virtual string? SupportedOSPlatform { get; set; }

        public string Name { get; }
        public override int GetHashCode() => Name.GetHashCode();
    }
}
