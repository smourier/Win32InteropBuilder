using System;
using System.Reflection.Metadata;

namespace Win32InteropBuilder
{
    // a definition handle only means something to the reader of the winmd it comes from.
    public sealed class BuilderTypeDefinition(MetadataReader reader, TypeDefinition definition)
    {
        public MetadataReader Reader { get; } = reader ?? throw new ArgumentNullException(nameof(reader));
        public TypeDefinition Definition { get; } = definition;
    }
}
