using System.Text.Json;

namespace Roof.Engine.Tools
{
    public static class ExtensionMethods
    {
        public static bool IsStringJson(this string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            try
            {
                using var jsonDoc = JsonDocument.Parse(source);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}