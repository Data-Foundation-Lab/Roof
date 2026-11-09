using Roof.Engine.Managers;

namespace Roof.Engine.Tools;

internal static class SafePath
{
    public static string CanonicalizeUnder(string path, params string[] allowedRoots)
    {
        if (!SecurityManager.IsValidText(path, 4096))
            throw new ArgumentException("Invalid filesystem path.");
        if (!Path.IsPathFullyQualified(path) || path.Split('/', '\\').Contains(".."))
            throw new ArgumentException("Path must be absolute and cannot contain parent traversal.", nameof(path));

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        foreach (var root in allowedRoots)
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (fullPath.Equals(fullRoot, StringComparison.Ordinal) ||
                fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                RejectSymbolicLinks(fullPath);
                return fullPath;
            }
        }
        throw new UnauthorizedAccessException("Path is outside the allowed root.");
    }

    // Check every ancestor, including the allowed root and dangling final links. Fail closed on
    // access errors. This is a preflight check; callers must not assume it locks the filesystem.
    private static void RejectSymbolicLinks(string fullPath)
    {
        string current = Path.GetPathRoot(fullPath)!;
        foreach (var component in fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Symbolic links are not permitted in protected paths.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static string ShellQuote(string value)
    {
        if (!SecurityManager.IsValidText(value, 16384))
            throw new ArgumentException("Invalid shell argument.");
        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }
}
