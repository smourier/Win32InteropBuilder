using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Win32InteropBuilder.Utilities;

namespace Win32InteropBuilder.Model
{
    [JsonConverter(typeof(JsonStringConverter<FullName>))]
    public class FullName : IEquatable<FullName>, IComparable<FullName>, IComparable, IFullyNameable, ICreatableFromString<FullName>
    {
        public const char NestedTypesSeparator = '+';
        public const string FoundationNamespace = "Windows.Win32.Foundation";
        public const string ComNamespace = "Windows.Win32.System.Com";
        public const string MetadataNamespace = FoundationNamespace + ".Metadata";

        public static FullName SystemIntPtr { get; } = new(typeof(nint));
        public static FullName SystemValueType { get; } = new(typeof(ValueType));
        public static FullName SystemEnum { get; } = new(typeof(Enum));
        public static FullName IUnknown { get; } = new(ComNamespace + ".IUnknown");
        public static FullName IUnknownPtr { get; } = new(ComNamespace + ".IUnknown*");
        public static FullName IDispatch { get; } = new(ComNamespace + ".IDispatch");
        public static FullName HRESULT { get; } = new(FoundationNamespace + ".HRESULT");
        public static FullName LRESULT { get; } = new(FoundationNamespace + ".LRESULT");
        public static FullName BOOL { get; } = new(FoundationNamespace + ".BOOL");
        public static FullName DECIMAL { get; } = new(FoundationNamespace + ".DECIMAL");
        public static FullName FARPROC { get; } = new(FoundationNamespace + ".FARPROC");
        public static FullName PWSTR { get; } = new(FoundationNamespace + ".PWSTR");
        public static FullName PSTR { get; } = new(FoundationNamespace + ".PSTR");
        public static FullName BSTR { get; } = new(FoundationNamespace + ".BSTR");
        public static FullName NativeTypedefAttribute { get; } = new(MetadataNamespace + ".NativeTypedefAttribute");
        public static FullName MemorySizeAttribute { get; } = new(MetadataNamespace + ".MemorySizeAttribute");
        public static FullName DocumentationAttribute { get; } = new(MetadataNamespace + ".DocumentationAttribute");
        public static FullName ComOutPtrAttribute { get; } = new(MetadataNamespace + ".ComOutPtrAttribute");
        public static FullName FlexibleArrayAttribute { get; } = new(MetadataNamespace + ".FlexibleArrayAttribute");
        public static FullName ConstAttribute { get; } = new(MetadataNamespace + ".ConstAttribute");
        public static FullName SupportedOSPlatformAttribute { get; } = new(MetadataNamespace + ".SupportedOSPlatformAttribute");
        public static FullName SupportedArchitectureAttribute { get; } = new(MetadataNamespace + ".SupportedArchitectureAttribute");
        public static FullName ConstantAttribute { get; } = new(MetadataNamespace + ".ConstantAttribute");
        public static FullName GuidAttribute { get; } = new(MetadataNamespace + ".GuidAttribute");
        public static FullName AnsiAttribute { get; } = new(MetadataNamespace + ".AnsiAttribute");
        public static FullName UnicodeAttribute { get; } = new(MetadataNamespace + ".UnicodeAttribute");
        public static FullName NativeArrayInfoAttribute { get; } = new(MetadataNamespace + ".NativeArrayInfoAttribute");
        public static FullName UnmanagedFunctionPointerAttribute { get; } = new(typeof(UnmanagedFunctionPointerAttribute));
        public static FullName MulticastDelegate { get; } = new(typeof(MulticastDelegate));
        public static FullName FlagsAttribute { get; } = new(typeof(FlagsAttribute));

        public FullName(string @namespace, string name)
        {
            ArgumentNullException.ThrowIfNull(@namespace);
            ArgumentNullException.ThrowIfNull(name);
            Namespace = @namespace;
            Name = name;
            SetNoPointerFullName();
        }

        public FullName(string fullName)
        {
            ArgumentNullException.ThrowIfNull(fullName);
            var pos = fullName.LastIndexOf('.');
            if (pos < 0)
            {
                Name = fullName;
                Namespace = string.Empty;
            }
            else
            {
                Name = fullName[(pos + 1)..];
                Namespace = fullName[..pos];
            }
            SetNoPointerFullName();
        }

        public FullName(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type.Name == null)
                throw new ArgumentException(null, nameof(type));

            if (type.Namespace == null)
                throw new ArgumentException(null, nameof(type));

            Namespace = type.Namespace;
            Name = type.Name;
            SetNoPointerFullName();
        }

        FullName IFullyNameable.FullName => this;
        public FullName NoPointerFullName { get; private set; }
        public int Indirections { get; private set; }
        public string Namespace { get; }
        public string Name { get; }
        public string? NestedName
        {
            get
            {
                var pos = Name.LastIndexOf(NestedTypesSeparator);
                if (pos < 0)
                    return null;

                return Name[(pos + 1)..];
            }
        }

        [MemberNotNull(nameof(NoPointerFullName))]
        private void SetNoPointerFullName()
        {
            if (Name.EndsWith('*'))
            {
                Indirections = Name.Count(c => c == '*');
                NoPointerFullName = new FullName(Namespace, Name.Replace("*", string.Empty));
            }
            else
            {
                NoPointerFullName = this;
            }
        }

        public override string ToString() => $"{Namespace}.{Name}";
        public bool Equals(FullName? other) => other != null && other.Namespace == Namespace && other.Name == Name;
        public override bool Equals(object? obj) => Equals(obj as FullName);
        public override int GetHashCode() => Namespace.GetHashCode() ^ Name.GetHashCode();
        int IComparable.CompareTo(object? obj) => CompareTo(obj as FullName);
        public int CompareTo(FullName? other) { ArgumentNullException.ThrowIfNull(other); return ToString().CompareTo(other.ToString()); }

        public static FullName Create(string input) => new(input);
        public static bool operator !=(FullName? obj1, FullName? obj2) => !(obj1 == obj2);
        public static bool operator ==(FullName? obj1, FullName? obj2)
        {
            if (ReferenceEquals(obj1, obj2))
                return true;

            if (obj1 is null)
                return false;

            if (obj2 is null)
                return false;

            return obj1.Equals(obj2);
        }
    }
}
