using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;

namespace Win32InteropBuilder.Model
{
    public static class WellKnownTypes
    {
        public static BuilderType SystemBoolean { get; } = new(typeof(bool)) { IsGenerated = false, UnmanagedType = UnmanagedType.U4, PrimitiveTypeCode = PrimitiveTypeCode.Boolean, IsValueType = true };
        public static BuilderType SystemByte { get; } = new(typeof(byte)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.Byte, IsValueType = true };
        public static BuilderType SystemChar { get; } = new(typeof(char)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.Char, IsValueType = true };
        public static BuilderType SystemDouble { get; } = new(typeof(double)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.Double, IsValueType = true };
        public static BuilderType SystemDecimal { get; } = new(typeof(decimal)) { IsGenerated = false, IsValueType = true };
        public static BuilderType SystemEnum { get; } = new(typeof(Enum)) { IsGenerated = false, IsValueType = true };
        public static BuilderType SystemGuid { get; } = new(typeof(Guid)) { IsGenerated = false, IsValueType = true };
        public static BuilderType SystemInt16 { get; } = new(typeof(short)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.Int16, IsValueType = true };
        public static BuilderType SystemInt32 { get; } = new(typeof(int)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.Int32, IsValueType = true };
        public static BuilderType SystemInt64 { get; } = new(typeof(long)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.Int64, IsValueType = true };
        public static BuilderType SystemIntPtr { get; } = new(typeof(nint)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.IntPtr, IsValueType = true };
        public static BuilderType SystemMulticastDelegate { get; } = new(typeof(MulticastDelegate)) { IsGenerated = false };
        public static BuilderType SystemObject { get; } = new(typeof(object)) { IsGenerated = false };
        public static BuilderType SystemSByte { get; } = new(typeof(sbyte)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.SByte, IsValueType = true };
        public static BuilderType SystemSingle { get; } = new(typeof(float)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.Single, IsValueType = true };
        public static BuilderType SystemString { get; } = new(typeof(string)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.String };
        public static BuilderType SystemUInt16 { get; } = new(typeof(ushort)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.UInt16, IsValueType = true };
        public static BuilderType SystemUInt32 { get; } = new(typeof(uint)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.UInt32, IsValueType = true };
        public static BuilderType SystemUInt64 { get; } = new(typeof(ulong)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.UInt64, IsValueType = true };
        public static BuilderType SystemUIntPtr { get; } = new(typeof(nuint)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.UIntPtr, IsValueType = true };
        public static BuilderType SystemValueType { get; } = new(typeof(ValueType)) { IsGenerated = false };
        public static BuilderType SystemVoid { get; } = new(typeof(void)) { IsGenerated = false, PrimitiveTypeCode = PrimitiveTypeCode.IntPtr };
        public static BuilderType SystemAttribute { get; } = new(typeof(Attribute)) { IsGenerated = false };

        public static BuilderType CallingConvention { get; } = new EnumType(typeof(CallingConvention)) { IsGenerated = false };

        // *warning* this must come *after* definitions of static BuilderType above
        public static IDictionary<FullName, BuilderType> All => _all.Value;
        private static readonly Lazy<IDictionary<FullName, BuilderType>> _all = new(LoadAll);
        private static IDictionary<FullName, BuilderType> LoadAll
        {
            get
            {
                var dic = new ConcurrentDictionary<FullName, BuilderType>();
                foreach (var prop in typeof(WellKnownTypes).GetProperties(BindingFlags.Static | BindingFlags.Public).Where(p => p.PropertyType == typeof(BuilderType)))
                {
                    var type = (BuilderType)prop.GetValue(null)!;
                    dic[type.FullName] = type;
                }
                return dic;
            }
        }
    }
}
