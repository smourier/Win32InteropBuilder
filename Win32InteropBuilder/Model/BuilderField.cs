using System;
using System.Reflection;
using System.Reflection.Metadata;

namespace Win32InteropBuilder.Model
{
    public class BuilderField : BuilderMember, IComparable, IComparable<BuilderField>
    {
        public BuilderField(string name)
            : base(name)
        {
        }

        public virtual BuilderType? Type { get; set; }
        public virtual FieldDefinitionHandle? Handle { get; set; }
        public virtual FieldAttributes Attributes { get; set; }
        public virtual int? Offset { get; set; }
        public virtual byte[]? DefaultValueAsBytes { get; set; }
        public object? DefaultValue => Type?.GetValue(DefaultValueAsBytes);

        int IComparable.CompareTo(object? obj) => CompareTo(obj as BuilderField);
        public int CompareTo(BuilderField? other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return Name.CompareTo(other.Name);
        }

        public override string ToString() => Name;
    }
}
