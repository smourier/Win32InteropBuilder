using System;

namespace Win32InteropBuilder.Model
{
    public class ParameterDef
    {
        public virtual ParameterDirection? Direction { get; set; }
        public virtual ParameterMarshalAs? MarshalAs { get; set; }
        public virtual ParameterMarshalUsing? MarshalUsing { get; set; }
        public virtual string? TypeName { get; set; }
        public virtual string? Comments { get; set; }
        public virtual bool? IsIn { get; set; }
        public virtual bool? IsOut { get; set; }
        public virtual bool IsNullDirection { get; set; } // only for patches
        public bool IsArrayTypeName => TypeName?.EndsWith("[]") == true;

        public virtual void PatchFrom(ParameterDef patch)
        {
            ArgumentNullException.ThrowIfNull(patch);
            if (patch.Direction.HasValue)
            {
                Direction = patch.Direction.Value;
            }
            else if (patch.IsNullDirection)
            {
                Direction = null;
            }

            if (patch.MarshalAs != null)
            {
                MarshalAs ??= patch.MarshalAs;
                MarshalAs.PatchFrom(patch.MarshalAs);
            }

            if (patch.MarshalUsing != null)
            {
                MarshalUsing ??= patch.MarshalUsing;
                MarshalUsing.PatchFrom(patch.MarshalUsing);
            }

            if (patch.TypeName != null)
            {
                TypeName = patch.TypeName;
            }

            if (patch.Comments != null)
            {
                Comments = patch.Comments;
            }

            if (patch.IsIn.HasValue)
            {
                IsIn = patch.IsIn.Value;
            }

            if (patch.IsOut.HasValue)
            {
                IsOut = patch.IsOut.Value;
            }
        }

        public override string ToString() => $"{MarshalAs} {MarshalUsing} {Direction} {TypeName} {Comments}";
    }
}
