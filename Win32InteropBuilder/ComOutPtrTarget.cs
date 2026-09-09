namespace Win32InteropBuilder
{
    public enum ComOutPtrTarget
    {
        IntPtr, // out IntPtr object
        Object, // [MarshalAs(UnmanagedType.Interface)] out object 
        UniqueObject, // [MarshalUsing(typeof(UniqueComInterfaceMarshaller<object>))] out object
    }
}
