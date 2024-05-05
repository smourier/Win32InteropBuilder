using System;
using System.Collections.Generic;

namespace Win32InteropBuilder.Utilities
{
    public static class DictionaryExtensions
    {
        public const char DefaultSeparator = ';';
        public const char DefaultAssignment = '=';
        public static StringComparer DefaultStringComparer { get; } = StringComparer.OrdinalIgnoreCase;

        public static string? Serialize<T>(this IDictionary<string, T?>? dictionary, StringComparer? comparer = null, char separator = DefaultSeparator, char assignment = DefaultAssignment) => DictionarySerializer<T>.Serialize(dictionary, comparer, separator, assignment);
        public static IDictionary<string, T?> Deserialize<T>(this string? text, StringComparer? comparer = null, char separator = DefaultSeparator, char assignment = DefaultAssignment) => DictionarySerializer<T>.Deserialize(text, comparer, separator, assignment);

        public static string? GetNullifiedString(this IDictionary<string, string?>? dictionary, string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (dictionary == null)
                return null;

            if (!dictionary.TryGetValue(name, out var str))
                return null;

            return str.Nullify();
        }

        public static string? GetNullifiedString(this IDictionary<string, object?>? dictionary, string name, IFormatProvider? provider = null)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (dictionary == null)
                return null;

            if (!dictionary.TryGetValue(name, out var obj) || obj == null)
                return null;

            return string.Format(provider, "{0}", obj).Nullify();
        }

        public static T? GetValue<T>(this IDictionary<string, string?>? dictionary, string name, IFormatProvider? provider = null, T? defaultValue = default)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (dictionary == null || !dictionary.TryGetValue(name, out var str))
                return defaultValue;

            return Conversions.ChangeType(str, defaultValue, provider);
        }

        public static bool TryGetValue<T>(this IDictionary<string, string?>? dictionary, string name, out T? value) => TryGetValue(dictionary, name, null, out value);
        public static bool TryGetValue<T>(this IDictionary<string, string?>? dictionary, string name, IFormatProvider? provider, out T? value)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (dictionary == null || !dictionary.TryGetValue(name, out var str))
            {
                value = default;
                return false;
            }

            return Conversions.TryChangeType(str, provider, out value);
        }

        public static T? GetValue<T>(this IDictionary<string, object?>? dictionary, string name, IFormatProvider? provider = null, T? defaultValue = default)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (dictionary == null || !dictionary.TryGetValue(name, out var obj))
                return defaultValue;

            return Conversions.ChangeType(obj, defaultValue, provider);
        }

        public static bool TryGetValue<T>(this IDictionary<string, object?>? dictionary, string name, out T? value) => TryGetValue(dictionary, name, null, out value);
        public static bool TryGetValue<T>(this IDictionary<string, object?>? dictionary, string name, IFormatProvider? provider, out T? value)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (dictionary == null || !dictionary.TryGetValue(name, out var obj))
            {
                value = default;
                return false;
            }

            return Conversions.TryChangeType(obj, provider, out value);
        }
    }
}
