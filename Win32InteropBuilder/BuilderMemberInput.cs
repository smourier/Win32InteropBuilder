using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Win32InteropBuilder.Model;

namespace Win32InteropBuilder
{
    [JsonConverter(typeof(Converter))]
    public class BuilderMemberInput(string input) : BuilderInput<BuilderMember>(input)
    {
        public override bool Matches(BuilderMember member)
        {
            ArgumentNullException.ThrowIfNull(member);
            if (string.IsNullOrEmpty(Input) && IsWildcard)
                return true;

            if (member.Name == Input)
                return true;

            if (IsWildcard)
            {
                if (member.Name.StartsWith(Input, StringComparison.CurrentCultureIgnoreCase))
                    return true;
            }
            return false;
        }

        private sealed class Converter : JsonConverter<BuilderMemberInput>
        {
            public override void Write(Utf8JsonWriter writer, BuilderMemberInput value, JsonSerializerOptions options) => throw new NotSupportedException();
            public override BuilderMemberInput? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                    return new BuilderMemberInput(reader.GetString()!);

                throw new NotImplementedException();
            }
        }
    }
}
