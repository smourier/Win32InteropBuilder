using System.Reflection.Metadata;

namespace Win32InteropBuilder.Model
{
    public class ArrayType(FullName fullName, ArrayShape shape) : BuilderType(fullName)
    {
        public ArrayShape ArrayShape { get; private set; } = shape;

        protected override void CopyTo(BuilderType copy)
        {
            base.CopyTo(copy);
            if (copy is ArrayType typed)
            {
                typed.ArrayShape = ArrayShape;
            }
        }
    }
}
