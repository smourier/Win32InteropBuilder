# Win32InteropBuilder
A tool to generate .NET AOT-friendly Win32 interop code from Microsoft.Windows.SDK.Win32Metadata package.

An example of this is [DirectNAot](https://github.com/smourier/DirectNAot) which is an AOT-friendly version of [DirectN](https://github.com/smourier/DirectN).

The key points that drive how code is generated and built:

* modern code exclusively based on .NET 8 newer source-generated LibraryImport, source-generated ComWrappers, etc. Note the result is the a .dll size is significantly bigger than what it was before.
* unsafe usage is limited.
* raw pointers usage is not exposed, only interface types, object types, or nint depending on the situation.
* doing interop is inherently unsafe but we want to keep a.NET-like programming whenever possible. The generated code serves a similar purpose to the CsWin32 project, but the generated code and how we program it are quite different.
