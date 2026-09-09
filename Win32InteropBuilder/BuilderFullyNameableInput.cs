using System;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    public abstract class BuilderFullyNameableInput<T>(string input) : BuilderInput<T>(input) where T : IFullyNameable
    {
        public override bool Matches(T item) => Matches(this, item);
        public static bool Matches(BuilderInput<T> input, IFullyNameable nameable)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(nameable);
            if (string.IsNullOrEmpty(input.Input) && input.IsWildcard)
                return true;

            var fn = nameable.FullName;
            var fns = fn.ToString();
            if (input.Input == fns)
                return true;

            if (fn.Namespace.EqualsIgnoreCase(input.Input))
                return true;

            if (fn.Name.EqualsIgnoreCase(input.Input))
                return true;

            if (input.IsWildcard)
            {
                if (fn.Namespace.StartsWith(input.Input + ".", StringComparison.CurrentCultureIgnoreCase))
                    return true;

                if (fn.Name.StartsWith(input.Input, StringComparison.CurrentCultureIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
