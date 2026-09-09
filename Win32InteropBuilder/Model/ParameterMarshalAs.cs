using System;
using System.Runtime.InteropServices;

namespace Win32InteropBuilder.Model
{
    public class ParameterMarshalAs
    {
        public virtual UnmanagedType UnmanagedType { get; set; }
        public virtual UnmanagedType? ArraySubType { get; set; }

        public virtual void PatchFrom(ParameterMarshalAs patch)
        {
            ArgumentNullException.ThrowIfNull(patch);
            UnmanagedType = patch.UnmanagedType;

            if (patch.ArraySubType.HasValue)
            {
                ArraySubType = patch.ArraySubType.Value;
            }
        }
    }
}
