using System.Text.Json.Serialization;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    [JsonConverter(typeof(JsonStringConverter<BuilderTypeInput>))]
    public class BuilderTypeInput(string input) : BuilderFullyNameableInput<BuilderType>(input), ICreatableFromString<BuilderTypeInput>
    {
        public static BuilderTypeInput Create(string input) => new(input);
    }
}
