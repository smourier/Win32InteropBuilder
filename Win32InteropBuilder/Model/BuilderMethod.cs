using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;

namespace Win32InteropBuilder.Model
{
    public class BuilderMethod(string name) : BuilderMember(name), IComparable, IComparable<BuilderMethod>
    {
        private readonly List<BuilderParameter> _parameters = [];

        public virtual MethodDefinitionHandle? Handle { get; set; }
        public virtual MethodAttributes Attributes { get; set; }
        public virtual MethodImplAttributes ImplAttributes { get; set; }
        public virtual bool IsAnsi { get; set; }
        public virtual bool IsUnicode { get; set; }
        public virtual BuilderType? ReturnType { get; set; }
        public virtual IList<BuilderParameter> Parameters => _parameters;
        public virtual MethodImportAttributes ImportAttributes { get; set; }
        public virtual string? ImportEntryPoint { get; set; }
        public virtual string? ImportModuleName { get; set; }

        public virtual void SortAndResolveParameters()
        {
            _parameters.Sort();
            foreach (var parameter in _parameters)
            {
                if (parameter.NativeArray?.CountParamIndex >= 0)
                {
                    parameter.NativeArray.CountParameter = _parameters[(int)parameter.NativeArray?.CountParamIndex.Value!];
                }
            }
        }

        int IComparable.CompareTo(object? obj) => CompareTo(obj as BuilderMethod);
        public int CompareTo(BuilderMethod? other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return Name.CompareTo(other.Name);
        }

        public override string ToString() => Name;
    }
}
