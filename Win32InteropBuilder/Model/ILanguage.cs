using System.Collections.Generic;
using System.Text.Json;

namespace Win32InteropBuilder.Model
{
    public interface ILanguage
    {
        string Name { get; }
        string FileExtension { get; }
        IReadOnlyCollection<FullName> ConstableTypes { get; }

        void Configure(JsonElement element);
        void GenerateCode(BuilderContext context, BuilderType type);
    }
}
