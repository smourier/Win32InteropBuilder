using System;

namespace Win32InteropBuilder.Model
{
    public class InlineArrayType : StructureType
    {
        public InlineArrayType(BuilderType elementType, int size, FullName? fullName = null)
            : base(fullName ?? BuildFullName(elementType, size))
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ElementType = elementType;
            Size = size;
        }

        public static FullName BuildFullName(BuilderType elementType, int size, string? elementName = null)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            var ns = elementType.FullName.NestedName;
            if (ns != null)
                return new(GeneratedInteropNamespace + ".InlineArray" + ns + size);

            return new(GeneratedInteropNamespace + ".InlineArray" + (elementName ?? elementType.FullName.Name) + size);
        }

        public virtual BuilderType ElementType { get; protected set; }
        public virtual int Size { get; protected set; }
        public virtual string? ElementName { get; set; }
        public override bool IsGenerated
        {
            get
            {
                // don't generate nested inline array apart
                if (ElementType?.FullName.NestedName != null)
                    return false;

                return base.IsGenerated;
            }
            set => base.IsGenerated = value;
        }

        protected override void CopyTo(BuilderType copy)
        {
            base.CopyTo(copy);
            if (copy is InlineArrayType typed)
            {
                typed.ElementType = ElementType;
                typed.Size = Size;
            }
        }
    }
}
