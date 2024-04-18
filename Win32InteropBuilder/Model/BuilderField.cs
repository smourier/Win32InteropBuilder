using System;
using System.Reflection;
using System.Reflection.Metadata;

namespace Win32InteropBuilder.Model
{
    public class BuilderField(string name) : BuilderMember(name), IComparable, IComparable<BuilderField>
    {
        private object? _defaultValue;

        public virtual BuilderType? Type { get; set; }
        public virtual FieldDefinitionHandle? Handle { get; set; }
        public virtual FieldAttributes Attributes { get; set; }
        public virtual bool IsFlexibleArray { get; set; }
        public virtual int? Offset { get; set; }
        public virtual byte[]? DefaultValueAsBytes { get; set; }
        public object? DefaultValue { get => _defaultValue ?? Type?.GetValue(DefaultValueAsBytes); set => _defaultValue = value; }

        int IComparable.CompareTo(object? obj) => CompareTo(obj as BuilderField);
        public int CompareTo(BuilderField? other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return Name.CompareTo(other.Name);
        }

        public override string ToString() => Name;
    }
}
