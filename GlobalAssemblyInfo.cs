﻿using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyCompany("WitherTorch 製作組")]
[assembly: AssemblyProduct("WitherTorch")]
[assembly: AssemblyCopyright("Copyright © WitherTorch 製作組 2023")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("1.8.3.0")]
[assembly: AssemblyFileVersion("1.8.3.6")]

//Used for NGen pre-linking
[assembly: Dependency("System.IO.Compression", LoadHint.Always)]
[assembly: Dependency("System.Text.Json", LoadHint.Always)]
[assembly: Dependency("YamlDotNet", LoadHint.Always)]
[assembly: Dependency("WitherTorch.Core", LoadHint.Always)]