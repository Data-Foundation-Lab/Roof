using System.Reflection;

[assembly: AssemblyDescription("Roof.Client")]
[assembly: AssemblyCompany("DFLab")]
[assembly: AssemblyCopyright("© 2025 DFLab")]
[assembly: AssemblyVersion("0.0.0.0")]
[assembly: AssemblyFileVersion("0.0.0.0")]
#if DEBUG
[assembly: AssemblyInformationalVersion("0.0.0.0-debug")]
#elif IA 
[assembly: AssemblyInformationalVersion("1.0.0.0-ia")]
#elif BETA
[assembly: AssemblyInformationalVersion("1.0.0.0-beta")]
#elif RELEASE
[assembly: AssemblyInformationalVersion("0.0.0.0-release")]
#endif
[assembly: AssemblyProduct("Roof.Client")]