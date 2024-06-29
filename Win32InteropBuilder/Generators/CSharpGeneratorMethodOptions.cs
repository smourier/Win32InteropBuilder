namespace Win32InteropBuilder.Generators
{
    public enum CSharpGeneratorMethodOptions
    {
        None = 0x0,
        ForImplementation = 0x1,
        Unsafe = 0x2,
        Public = 0x4,
        OutAsRef = 0x8,
        ComOutPtrAsIntPtr = 0x10,
    }
}
