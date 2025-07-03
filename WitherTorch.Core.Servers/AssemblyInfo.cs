using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("WitherTorch.Core.Server")]
[assembly: AssemblyDescription("WitherTorch.Core 的預設伺服器實現集")]

[assembly: AssemblyCompany("WitherTorch 製作組")]
[assembly: AssemblyProduct("WitherTorch")]
[assembly: AssemblyCopyright("Copyright © WitherTorch 製作組 2025")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("1.8.4.0")]
[assembly: AssemblyFileVersion("1.8.4.10")]

//Used for NGen pre-linking
[assembly: Dependency("System.Text.Json", LoadHint.Always)]
[assembly: Dependency("YamlDotNet", LoadHint.Always)]
[assembly: Dependency("WitherTorch.Core", LoadHint.Always)]