using System;

namespace Win32InteropBuilder.Model;

public class UniqueFieldType(BuilderType containerType, BuilderField field, BuilderType fieldType)
{
    public BuilderType ContainerType { get; } = containerType ?? throw new ArgumentNullException(nameof(containerType));
    public BuilderField Field { get; } = field ?? throw new ArgumentNullException(nameof(field));
    public BuilderType FieldType { get; } = fieldType ?? throw new ArgumentNullException(nameof(fieldType));
}
