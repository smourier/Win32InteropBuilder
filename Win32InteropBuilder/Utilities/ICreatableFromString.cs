namespace Win32InteropBuilder.Utilities
{
    public interface ICreatableFromString<T>
    {
        static abstract T Create(string input);
    }
}
