using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Win32InteropBuilder.Utilities
{
    public class JsonStringConverter<T> : JsonConverter<T> where T : ICreatableFromString<T>
    {
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => throw new NotImplementedException();
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return T.Create(reader.GetString()!);

            throw new NotImplementedException();
        }
    }
}
