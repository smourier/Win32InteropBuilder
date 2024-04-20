namespace Win32InteropBuilder.Model
{
    public class ParameterDef
    {
        public virtual string? Direction { get; set; }
        public virtual string? MarshalAs { get; set; }
        public virtual string? MarshalUsing { get; set; }
        public virtual string? TypeName { get; set; }
        public virtual string? Comments { get; set; }
        public virtual bool IsComOutPtr { get; set; }

        public override string ToString() => $"{MarshalAs}{MarshalUsing}{Direction}{TypeName}{Comments}";
    }
}
