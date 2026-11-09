using System.Reflection;

namespace Roof.Engine
{
    internal class Version
    {
        internal static string Get()
        {
            var assembly = Assembly.GetEntryAssembly();
            var infoVersionAttribute = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string version = infoVersionAttribute?.InformationalVersion ?? "Version not found";
            return version;
        }

        internal static string GetCompileFlag()
        {
#if DEBUG
            return "DEBUG";
#elif IA 
            return "IA";
#elif BETA
            return "BETA";
#elif RELEASE
            return "RELEASE";
#endif
        }
    }
}