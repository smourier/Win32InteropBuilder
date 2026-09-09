using System.Collections.Generic;

namespace Win32InteropBuilder.Generators
{
    public class CSharpGeneratorMethod(string returnTypeName)
    {
        public string ReturnTypeName => returnTypeName;
        public IList<CSharpGeneratorParameter> Parameters { get; } = [];
    }
}
