using System;
using System.Collections.Generic;

namespace Win32InteropBuilder.Model
{
    public class BuilderMember : IDocumentable, ISupportable, INameable, IExtensible
    {
        private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);
        IDictionary<string, object?> IExtensible.Properties => _properties;

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
