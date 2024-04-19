using System;

namespace Win32InteropBuilder.Model
{
    [Flags]
    public enum BuilderTypeAttributes
    {
        None = 0x0,
        IsUnifiedConstants = 0x1,
        IsUnifiedFunctions = 0x2,
    }
}
