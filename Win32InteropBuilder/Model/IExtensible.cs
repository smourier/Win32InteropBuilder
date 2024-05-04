using System.Collections.Generic;

namespace Win32InteropBuilder.Model
{
    public interface IExtensible
    {
        IDictionary<string, object?> Properties { get; }
    }
}
