using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Win32InteropBuilder.Model
{
    public class BuilderParameter : IComparable, IComparable<BuilderParameter>, IDocumentable, IExtensible
    {
        private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);
        IDictionary<string, object?> IExtensible.Properties => _properties;

        public BuilderParameter(string name, int sequenceNumber)
        {
            ArgumentNullException.ThrowIfNull(name);
            Name = name;
            SequenceNumber = sequenceNumber;
        }

        public string Name { get; }
        public int SequenceNumber { get; }
        public virtual ParameterAttributes Attributes { get; set; }
        public virtual BuilderType? Type { get; set; }
        public virtual bool IsComOutPtr { get; set; }
        public virtual NativeArray? NativeArray { get; set; }
        public virtual bool IsConst { get; set; }
        public virtual string? Documentation { get; set; }
        public virtual UnmanagedType? UnmanagedType { get; set; }

        int IComparable.CompareTo(object? obj) => CompareTo(obj as BuilderParameter);
        public int CompareTo(BuilderParameter? other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return SequenceNumber.CompareTo(other.SequenceNumber);
        }

        public override string ToString() => $"{Type} {Name}";
    }
}
