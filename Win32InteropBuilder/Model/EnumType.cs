using System;
using System.Reflection;
using System.Reflection.Metadata;

namespace Win32InteropBuilder.Model
{
    public class EnumType : BuilderType
    {
        public EnumType(FullName fullName)
            : base(fullName)
        {
        }

        public EnumType(Type type)
            : base(type)
        {
        }

        public virtual bool IsFlags { get; set; }
        public virtual FullName? UnderlyingTypeFullName { get; set; }
        public override bool IsValueType => true;

        protected override internal void ResolveType(BuilderContext context, TypeDefinition typeDef)
        {
            base.ResolveType(context, typeDef);
            IsFlags = context.MetadataReader!.IsEnumFlags(typeDef.GetCustomAttributes());
        }

        protected override void CopyTo(BuilderType copy)
        {
            base.CopyTo(copy);
            if (copy is EnumType typed)
            {
                typed.IsFlags = IsFlags;
                typed.UnderlyingTypeFullName = UnderlyingTypeFullName;
            }
        }

        protected override void ResolveFields(BuilderContext context, TypeDefinition typeDef)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.MetadataReader);

            foreach (var handle in typeDef.GetFields())
            {
                var fieldDef = context.MetadataReader.GetFieldDefinition(handle);
                var field = context.CreateBuilderField(context.MetadataReader.GetString(fieldDef.Name));
                if (field == null)
                    continue;

                field.Handle = handle;
                var type = fieldDef.DecodeSignature(context.SignatureTypeProvider, null);
                field.TypeFullName = type.FullName;
                if (fieldDef.Attributes.HasFlag(FieldAttributes.RTSpecialName))
                {
                    UnderlyingTypeFullName = field.TypeFullName;
                    continue;
                }

                Fields.Add(field);
                field.DefaultValueAsBytes = context.MetadataReader.GetConstantBytes(fieldDef.GetDefaultValue());
            }
        }
    }
}
