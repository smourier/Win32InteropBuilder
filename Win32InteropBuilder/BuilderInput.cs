using System;
using System.Collections.Generic;
using Win32InteropBuilder.Model;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder
{
    public abstract class BuilderInput<T> : IExtensible
    {
        private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);
        IDictionary<string, object?> IExtensible.Properties => _properties;

        public BuilderInput(string input)
        {
            ArgumentNullException.ThrowIfNull(input);
            var pos = input.IndexOf('(');
            if (pos >= 0)
            {
                string? text;
                var end = input.IndexOf(')', pos + 1);
                if (end < 0)
                {
                    text = input[(pos + 1)..].Nullify();
                }
                else
                {
                    text = input.Substring(pos + 1, end - pos - 1).Nullify();
                }

                foreach (var kv in DictionarySerializer<object?>.Deserialize(text))
                {
                    _properties[kv.Key] = kv.Value;
                }

                input = input[..pos];
            }

            if (input.StartsWith('-'))
            {
                Exclude = true;
                input = input[1..];
            }

            if (input.StartsWith('!'))
            {
                IsReverse = true;
                input = input[1..];
            }

            if (input.EndsWith('*'))
            {
                IsWildcard = true;
                input = input[..^1];
            }

            Input = input;
            PostParse(input);
        }

        public string Input { get; }
        public bool IsWildcard { get; }
        public bool IsReverse { get; }
        public bool Exclude { get; }
        public int MatchesCount { get; set; }

        public bool MatchesEverything => IsWildcard && Input == string.Empty; // "*"
        public bool ReversesEverything => IsReverse && Input == string.Empty; // "!"

        protected virtual void PostParse(string input) => ArgumentNullException.ThrowIfNull(input);

        public override string ToString() => $"{(Exclude ? "-" : null)}{(IsReverse ? "!" : null)}{Input}{(IsWildcard ? "*" : null)}";

        // not "Equals" to avoid confusion
        protected virtual bool EqualsTo(BuilderInput<T> other) => other?.Input == Input && other.IsReverse == IsReverse && other.IsWildcard == IsWildcard;
        public abstract bool Matches(T type);
    }
}
