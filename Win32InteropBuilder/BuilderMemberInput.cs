using System.Text.Json.Serialization;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    [JsonConverter(typeof(JsonStringConverter<BuilderMemberInput>))]
    public class BuilderMemberInput(string input) : BuilderNameableInput<BuilderMember>(input), ICreatableFromString<BuilderMemberInput>
    {
        public static BuilderMemberInput Create(string input) => new(input);
    }
}
