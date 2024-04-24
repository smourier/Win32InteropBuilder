namespace Win32InteropBuilder.Model
{
    public class ParameterDef
    {
        public virtual ParameterDirection? Direction { get; set; }
        public virtual ParameterMarshalAs? MarshalAs { get; set; }
        public virtual ParameterMarshalUsing? MarshalUsing { get; set; }
        public virtual string? TypeName { get; set; }
        public virtual string? Comments { get; set; }

        public override string ToString() => $"{MarshalAs} {MarshalUsing} {Direction} {TypeName} {Comments}";
    }
}
