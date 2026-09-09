using System;
using Win32InteropBuilder.Model;

namespace Win32InteropBuilder
{
    public abstract class BuilderNameableInput<T>(string input) : BuilderInput<T>(input) where T : INameable
    {
        public override bool Matches(T nameable)
        {
            ArgumentNullException.ThrowIfNull(nameable);
            if (string.IsNullOrEmpty(Input) && IsWildcard)
                return true;

            if (nameable.Name == Input)
                return true;

            if (IsWildcard)
            {
                if (nameable.Name.StartsWith(Input, StringComparison.CurrentCultureIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
