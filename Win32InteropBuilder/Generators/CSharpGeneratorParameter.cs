using Win32InteropBuilder.Model;

namespace Win32InteropBuilder.Generators
{
    public class CSharpGeneratorParameter(string name, string typeName, ParameterDirection? direction)
    {
        public string Name => name;
        public string TypeName => typeName;
        public ParameterDirection? Direction => direction;

        public string ToArgumentDeclaration()
        {
            if (direction == ParameterDirection.Out || direction == ParameterDirection.Ref)
                return $"{TypeName}*";

            return TypeName;
        }

        public string ToArgument()
        {
            if (direction == ParameterDirection.Out || direction == ParameterDirection.Ref)
                return $"({TypeName}*)Unsafe.AsPointer(ref {Name})";

            return Name;
        }
    }
}
