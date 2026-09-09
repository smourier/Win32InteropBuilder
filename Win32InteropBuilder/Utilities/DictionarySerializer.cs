using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Win32InteropBuilder.Utilities
{
    public class DictionarySerializer<T>
    {
        public virtual StringComparer StringComparer { get; set; } = DictionaryExtensions.DefaultStringComparer;
        public virtual char Separator { get; set; } = DictionaryExtensions.DefaultSeparator;
        public virtual char Assignment { get; set; } = DictionaryExtensions.DefaultAssignment;
        public virtual bool Unquote { get; set; } = true;

        public virtual IDictionary<string, T?> DeserializeDictionary(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return new Dictionary<string, T?>(StringComparer);

            using var reader = new StringReader(text);
            return DeserializeDictionary(reader);
        }

        public virtual void DeserializeDictionary(string? text, IDictionary<string, T?> dictionary)
        {
            ArgumentNullException.ThrowIfNull(dictionary);
            if (string.IsNullOrEmpty(text))
                return;

            using var reader = new StringReader(text);
            DeserializeDictionary(reader, dictionary);
        }

        public static string? UnquoteText(string? text)
        {
            if (text == null || text.Length < 2)
                return text;

            if ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\''))
                return text[1..^1];

            return text;
        }

        private enum ParseState
        {
            Key,
            Value,
        }

        public virtual IDictionary<string, T?> DeserializeDictionary(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            var dic = new Dictionary<string, T?>(StringComparer);
            DeserializeDictionary(reader, dic);
            return dic;
        }

        public virtual void DeserializeDictionary(TextReader reader, IDictionary<string, T?> dictionary)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(dictionary);
            var state = ParseState.Key;
            var key = new StringBuilder();
            var value = new StringBuilder();
            var eatNext = false;
            do
            {
                var i = reader.Read();
                var c = (char)i;
                i = reader.Peek();
                var next = i < 0 ? '\uFFFF' : (char)i;

                if (eatNext)
                {
                    eatNext = false;
                    continue;
                }

                switch (state)
                {
                    case ParseState.Key:
                        if (i < 0 || c == Separator)
                        {
                            if (c != Separator && c != Assignment)
                            {
                                key.Append(c);
                            }

                            var name = key.ToString();
                            if (Unquote)
                            {
                                name = UnquoteText(name);
                            }

                            if (!string.IsNullOrEmpty(name))
                            {
                                var t = default(T);
                                dictionary[name] = t;
                            }
                            key.Length = 0;
                            break;
                        }

                        if (c == '\\' && next == Assignment)
                        {
                            eatNext = true;
                            key.Append(Assignment);
                            break;
                        }

                        if (c == Assignment)
                        {
                            state = ParseState.Value;
                            break;
                        }

                        key.Append(c);
                        break;

                    case ParseState.Value:
                        if (c == '\\' && next == Separator)
                        {
                            eatNext = true;
                            value.Append(Separator);
                            break;
                        }

                        if (i < 0 || c == Separator)
                        {
                            if (c != Separator)
                            {
                                value.Append(c);
                            }
                            state = ParseState.Key;

                            var name = key.ToString();
                            if (Unquote)
                            {
                                name = UnquoteText(name);
                            }

                            if (!string.IsNullOrEmpty(name))
                            {
                                if (value.Length == 0)
                                {
                                    dictionary[name] = default;
                                }
                                else
                                {
                                    var valueText = value.ToString();
                                    if (Unquote)
                                    {
                                        valueText = UnquoteText(valueText);
                                    }
                                    dictionary[name] = Conversions.ChangeType(valueText, default(T), CultureInfo.InvariantCulture);
                                }
                            }

                            key.Length = 0;
                            value.Length = 0;
                            break;
                        }
                        value.Append(c);
                        break;
                }
                if (i < 0)
                    break;
            }
            while (true);
        }

        public static void Deserialize(string? text, IDictionary<string, T?> dictionary, StringComparer? comparer = null, char separator = DictionaryExtensions.DefaultSeparator, char assignment = DictionaryExtensions.DefaultAssignment)
        {
            ArgumentNullException.ThrowIfNull(dictionary);
            var serializer = new DictionarySerializer<T>
            {
                StringComparer = comparer ?? DictionaryExtensions.DefaultStringComparer,
                Separator = separator,
                Assignment = assignment
            };
            serializer.DeserializeDictionary(text, dictionary);
        }

        public static IDictionary<string, T?> Deserialize(string? text, StringComparer? comparer = null, char separator = DictionaryExtensions.DefaultSeparator, char assignment = DictionaryExtensions.DefaultAssignment) => new DictionarySerializer<T>
        {
            StringComparer = comparer ?? DictionaryExtensions.DefaultStringComparer,
            Separator = separator,
            Assignment = assignment
        }.DeserializeDictionary(text);

        public static string? Serialize(IDictionary<string, T?>? dictionary, StringComparer? comparer = null, char separator = DictionaryExtensions.DefaultSeparator, char assignment = DictionaryExtensions.DefaultAssignment) => new DictionarySerializer<T>
        {
            StringComparer = comparer ?? DictionaryExtensions.DefaultStringComparer,
            Separator = separator,
            Assignment = assignment
        }.SerializeDictionary(dictionary);

        public virtual string? SerializeDictionary(IDictionary<string, T?>? dictionary)
        {
            if (dictionary == null || dictionary.Count == 0)
                return null;

            using var writer = new StringWriter();
            SerializeDictionary(writer, dictionary);
            return writer.ToString();
        }

        public virtual string? SerializeDictionary(IDictionary? dictionary)
        {
            if (dictionary == null || dictionary.Count == 0)
                return null;

            using var writer = new StringWriter();
            SerializeDictionary(writer, dictionary);
            return writer.ToString();
        }

        public virtual void SerializeDictionary(TextWriter writer, IDictionary<string, T?>? dictionary)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (dictionary == null || dictionary.Count == 0)
                return;

            var needSeparator = false;
            foreach (var kv in dictionary)
            {
                var key = kv.Key;
                if (string.IsNullOrEmpty(key))
                    continue;

                key = key.Replace(Assignment.ToString(), @"\" + Assignment);
                if (needSeparator)
                {
                    writer.Write(Separator);
                }
                else
                {
                    needSeparator = true;
                }

                writer.Write(key);
                writer.Write(Assignment);
                SerializeValue(writer, kv.Value);
            }
        }

        public virtual void SerializeDictionary(TextWriter writer, IDictionary? dictionary)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (dictionary == null || dictionary.Count == 0)
                return;

            var needSeparator = false;
            foreach (DictionaryEntry kv in dictionary)
            {
                var key = string.Format(CultureInfo.InvariantCulture, "{0}", kv.Key);
                if (string.IsNullOrEmpty(key))
                    continue;

                key = key.Replace(Assignment.ToString(), @"\" + Assignment);
                if (needSeparator)
                {
                    writer.Write(Separator);
                }
                else
                {
                    needSeparator = true;
                }

                writer.Write(key);
                writer.Write(Assignment);
                SerializeValue(writer, kv.Value);
            }
        }

        protected virtual void SerializeValue(TextWriter writer, object? obj)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (obj == null)
                return;

            string value;
            if (obj is DateTime dt)
            {
                value = dt.ToString("O");
            }
            else if (obj is DateTimeOffset dto)
            {
                value = dto.ToString("O");
            }
            else
            {
                value = string.Format(CultureInfo.InvariantCulture, "{0}", obj);
            }

            value = value.Replace(Separator.ToString(), @"\" + Separator);
            writer.Write(value);
        }
    }
}
