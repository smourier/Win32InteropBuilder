using System;

namespace Win32InteropBuilder.Model
{
    public class PointerType(FullName fullName, int indirections) : BuilderType(BuildFullName(fullName, indirections))
    {
        public int Indirections { get; private set; } = indirections;
        public override bool IsGenerated { get => false; set => base.IsGenerated = value; }

        protected override void CopyTo(BuilderType copy)
        {
            base.CopyTo(copy);
            if (copy is PointerType typed)
            {
                typed.Indirections = Indirections;
            }
        }

        public override string GetGeneratedName(BuilderContext context) => base.GetGeneratedName(context).Replace("*", string.Empty);

        private static FullName BuildFullName(FullName fullName, int indirections)
        {
            ArgumentNullException.ThrowIfNull(fullName);
            var name = fullName.Name.Replace("*", string.Empty);
            return new FullName(fullName.Namespace, name + new string('*', indirections));
        }
    }
}
