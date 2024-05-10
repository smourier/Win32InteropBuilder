﻿using System;
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
        public virtual BuilderType? UnderlyingType { get; set; }
        public override bool IsValueType { get => true; }

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
                typed.UnderlyingType = UnderlyingType;
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
                field.Type = fieldDef.DecodeSignature(context.SignatureTypeProvider, null);
                if (fieldDef.Attributes.HasFlag(FieldAttributes.RTSpecialName))
                {
                    UnderlyingType = field.Type;
                    continue;
                }

                Fields.Add(field);
                field.DefaultValueAsBytes = context.MetadataReader.GetConstantBytes(fieldDef.GetDefaultValue());
            }
        }
    }
}
