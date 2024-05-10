using System;

namespace Win32InteropBuilder.Model
{
    public class BuilderTypeExtensionMethod : BuilderMethod
    {
        public BuilderTypeExtensionMethod(BuilderMethod method)
            : base(method?.Name!)
        {
            ArgumentNullException.ThrowIfNull(method);
            SourceMethod = method;
        }

        public BuilderMethod SourceMethod { get; }
    }
}
