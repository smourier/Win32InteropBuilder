using System;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    public class BuilderPatchParameter
    {
        public virtual string? Name { get; set; } // can be a number => index
        public virtual ParameterDef? Def { get; set; }

        public virtual bool Matches(BuilderParameter parameter, int parameterIndex)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentOutOfRangeException.ThrowIfNegative(parameterIndex);
            if (string.IsNullOrEmpty(Name))
                return false;

            if (parameter.Name.EqualsIgnoreCase(Name))
                return true;

            if (Name.EndsWith('*'))
            {
                var name = Name[..^1];
                if (parameter.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (int.TryParse(Name, out var index) && index == parameterIndex)
                return true;

            return false;
        }

        public override string ToString() => $"{Name} {Def}";
    }
}
