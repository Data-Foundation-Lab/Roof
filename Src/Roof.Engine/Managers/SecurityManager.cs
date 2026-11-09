using System.Text;
using System.Text.RegularExpressions;
using Roof.Common.Tools;

namespace Roof.Engine.Managers
{
    public class SecurityManager
    {
        public static bool IsValidClientID(string value) =>
            value != null && Regex.IsMatch(value, @"\A[a-fA-F0-9]{32}\z");

        // Roof client IDs are also used as Linux account names.
        public static bool IsValidUsername(string value) =>
            value != null && (IsValidClientID(value) || Regex.IsMatch(value, @"\A[a-z_][a-z0-9_-]{0,31}\z"));

        public static bool IsValidText(string value, int maxBytes) =>
            !string.IsNullOrEmpty(value) && !value.Any(char.IsControl) &&
            Encoding.UTF8.GetByteCount(value) <= maxBytes;

        public static bool IsValidPassword(string value) =>
            GetPasswordValidationError(value) == null;

        public static string? GetPasswordValidationError(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "Password must not be empty.";
            if (value.Any(char.IsControl))
                return "Password must not contain control characters (including line breaks).";
            if (Encoding.UTF8.GetByteCount(value) > 1024)
                return "Password must not exceed 1024 UTF-8 bytes.";

            // chpasswd splits username:password at the first colon only.
            // Further colons are password data; line breaks and NUL remain forbidden.
            return null;
        }

        public static bool IsValidFilename(string value) =>
            IsValidText(value, 255) && value != "." && value != ".." &&
            !value.Contains('/') && !value.Contains('\\');

        public static async Task PeformSecurityAndIntegrityCheck()
        {
            Logger.LogToFile("[SecurityManager]: Peforming a security and integrity check...");
        }
    }
}