using System;
using System.Text.Json.Serialization;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    [JsonConverter(typeof(JsonStringConverter<BuilderFullNameInput>))]
    public class BuilderFullNameInput : BuilderFullyNameableInput<FullName>, ICreatableFromString<BuilderFullNameInput>, IEquatable<BuilderFullNameInput>
    {
        public BuilderFullNameInput(string input)
            : base(input)
        {
        }

        public BuilderFullNameInput(FullName fullName)
            : base(fullName?.ToString()!) // let it throw below
        {
        }

        public static BuilderFullNameInput Create(string input) => new(input);

        public override int GetHashCode() => ToString().GetHashCode();
        public override bool Equals(object? obj) => Equals(obj as BuilderFullNameInput);
        public bool Equals(BuilderFullNameInput? other) => EqualsTo(this);
    }
}
