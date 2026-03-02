# Win32InteropBuilder
A tool to generate .NET AOT-friendly Win32 interop code from `Microsoft.Windows.SDK.Win32Metadata` or any `.winmd` package "related" to it.

Examples of this:

* [DirectNAot](https://github.com/smourier/DirectNAot) which is an AOT-friendly version of [DirectN](https://github.com/smourier/DirectN) : interop Code for .NET Framework, .NET Core and .NET 5+ : DXGI, WIC, DirectX 9 to 12, Direct2D, Direct Write, Direct Composition, Media Foundation, WASAPI, CodecAPI, GDI, Spatial Audio, DVD, Windows Media Player, UWP DXInterop, WinUI3, etc)
* [WebView2Aot](https://github.com/smourier/WebView2Aot) which is an AOT-compatible bindings dll for [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2?form=MA13LH) independent from WinForms or WPF.
* [ShellN](https://github.com/smourier/ShellBat/tree/main/ShellN) interop code for the Windows Shell (IShellItem, etc.). Ships with [ShellBat](https://github.com/smourier/ShellBat) a .NET AOT one-exe file modern Windows file explorer with file viewers, multi-instance workflows, terminal integration, search capabilities, and deep Windows Shell interoperability.

The key points that drive how code is generated and built:

* modern code exclusively based on .NET 8+ newer source-generated LibraryImport, source-generated ComWrappers, etc. Note the result is the a .dll size is significantly bigger than what it was before.
* unsafe usage is limited.
* raw pointers usage is not exposed (we don't want to code in C# like it's another C/C++), only interface types, `object` types, or `nint` depending on the situation.
* doing interop is inherently unsafe but we want to keep a.NET-like programming whenever possible. The generated code serves a similar purpose to the CsWin32 project, but the generated code and how we program it are quite different.
