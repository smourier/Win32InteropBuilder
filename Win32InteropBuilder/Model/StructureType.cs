namespace Win32InteropBuilder.Model
{
    public class StructureType(FullName fullName) : BuilderType(fullName)
    {
        public override bool IsValueType { get => true; }
        public virtual int? PackingSize { get; set; }
    }
}
