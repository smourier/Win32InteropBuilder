using System;
using System.Reflection;
using Win32InteropBuilder;
using Win32InteropBuilder.Utilities;

namespace InteropBuilder.Cli
{
    internal class Program
    {
        static void Main()
        {
            Console.WriteLine("Win32 Interop Builder - Copyright (C) 2017-" + DateTime.Now.Year + " Simon Mourier. All rights reserved.");
            Console.WriteLine();
            var configurationPath = CommandLine.Current.GetNullifiedArgument(0);
            if (CommandLine.Current.HelpRequested || configurationPath == null)
            {
                Help();
                return;
            }

            Builder.Run(configurationPath, Win32Metadata.WinMdPath);
        }

        static void Help()
        {
            Console.WriteLine(Assembly.GetEntryAssembly()!.GetName().Name!.ToUpperInvariant() + " <config.json> [outputpath]");
            Console.WriteLine();
            Console.WriteLine("Description:");
            Console.WriteLine("    This tool is used to generate Win32 interop .cs files from Microsoft.Windows.SDK.Win32Metadata and configuration directives.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine();
            Console.WriteLine("    " + Assembly.GetEntryAssembly()!.GetName().Name!.ToUpperInvariant() + @" c:\mypath\myproject\myprojectInteropBuilder.dll");
            Console.WriteLine();
        }
    }
}
