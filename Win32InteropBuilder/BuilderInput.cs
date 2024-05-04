using System;

namespace Win32InteropBuilder
{
    public abstract class BuilderInput<T>
    {
        public BuilderInput(string input)
        {
            ArgumentNullException.ThrowIfNull(input);
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
        }

        public string Input { get; }
        public bool IsWildcard { get; }
        public bool IsReverse { get; }
        public bool Exclude { get; }

        public bool MatchesEverything => IsWildcard && Input == string.Empty; // "*"
        public bool ReversesEverything => IsReverse && Input == string.Empty; // "!"

        public override string ToString() => $"{(Exclude ? "-" : null)}{(IsReverse ? "!" : null)}{Input}{(IsWildcard ? "*" : null)}";

        // not "Equals" to avoid confusion
        protected virtual bool EqualsTo(BuilderInput<T> other) => other?.Input == Input && other.IsReverse == IsReverse && other.IsWildcard == IsWildcard;
        public abstract bool Matches(T type);
    }
}
