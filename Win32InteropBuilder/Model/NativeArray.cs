namespace Win32InteropBuilder.Model
{
    public class NativeArray
    {
        public virtual int? CountConst { get; set; }
        public virtual short? CountParamIndex { get; set; }
        public virtual string? CountFieldName { get; set; }

        // computed
        public virtual BuilderParameter? CountParameter { get; set; }
    }
}
