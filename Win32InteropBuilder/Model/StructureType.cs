namespace Win32InteropBuilder.Model
{
    public class StructureType(FullName fullName) : BuilderType(fullName)
    {
        public override bool IsValueType => true;
        public virtual int? PackingSize { get; set; }

        protected override void CopyTo(BuilderType copy)
        {
            base.CopyTo(copy);
            if (copy is StructureType typed)
            {
                typed.PackingSize = PackingSize;
            }
        }
    }
}
