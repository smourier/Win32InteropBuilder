using System;

namespace Win32InteropBuilder.Model
{
    public class ParameterMarshalUsing
    {
        public virtual string? TypeName { get; set; }
        public virtual string? CountElementName { get; set; }
        public virtual int? ConstantElementCount { get; set; }

        public virtual void PatchFrom(ParameterMarshalUsing @using)
        {
            ArgumentNullException.ThrowIfNull(@using);
            if (@using.TypeName != null)
            {
                TypeName = @using.TypeName;
            }

            if (@using.CountElementName != null)
            {
                CountElementName = @using.CountElementName;
            }

            if (@using.ConstantElementCount.HasValue)
            {
                ConstantElementCount = @using.ConstantElementCount.Value;
            }
        }
    }
}
