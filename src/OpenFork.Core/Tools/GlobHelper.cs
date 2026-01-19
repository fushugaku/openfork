using System.Text.RegularExpressions;

namespace OpenFork.Core.Tools;

/// <summary>
/// Shared glob pattern to regex conversion utilities.
/// </summary>
public static class GlobHelper
{
    /// <summary>
    /// Converts a glob pattern to a regex for full path matching.
    /// Supports **, *, ?, {a,b,c}, and path separators.
    /// </summary>
    public static Regex GlobToRegex(string pattern)
    {
        var regexPattern = "^";
        var i = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    if (i + 2 < pattern.Length && (pattern[i + 2] == '/' || pattern[i + 2] == '\\'))
                    {
                        // **/ matches zero or more directory segments
                        regexPattern += "(?:.*[\\\\/])?";
                        i += 3;
                    }
                    else
                    {
                        // ** at end or before non-separator matches anything
                        regexPattern += ".*";
                        i += 2;
                    }
                }
                else
                {
                    // * matches any character except path separator
                    regexPattern += "[^\\\\/]*";
                    i++;
                }
            }
            else if (c == '?')
            {
                // ? matches any single character except path separator
                regexPattern += "[^\\\\/]";
                i++;
            }
            else if (c == '{')
            {
                // {a,b,c} matches alternatives
                var end = pattern.IndexOf('}', i);
                if (end > i)
                {
                    var options = pattern[(i + 1)..end].Split(',');
                    regexPattern += "(" + string.Join("|", options.Select(Regex.Escape)) + ")";
                    i = end + 1;
                }
                else
                {
                    regexPattern += Regex.Escape(c.ToString());
                    i++;
                }
            }
            else if (c == '/' || c == '\\')
            {
                // Normalize path separators
                regexPattern += "[\\\\/]";
                i++;
            }
            else
            {
                regexPattern += Regex.Escape(c.ToString());
                i++;
            }
        }

        regexPattern += "$";
        return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>
    /// Converts a simple glob pattern (for filename matching only) to regex.
    /// Simpler version that handles *, ?, and {a,b,c}.
    /// </summary>
    public static Regex SimpleGlobToRegex(string pattern)
    {
        var regexPattern = "^";
        var i = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '*')
            {
                regexPattern += ".*";
                i++;
            }
            else if (c == '?')
            {
                regexPattern += ".";
                i++;
            }
            else if (c == '{')
            {
                var end = pattern.IndexOf('}', i);
                if (end > i)
                {
                    var options = pattern[(i + 1)..end].Split(',');
                    regexPattern += "(" + string.Join("|", options.Select(Regex.Escape)) + ")";
                    i = end + 1;
                }
                else
                {
                    regexPattern += Regex.Escape(c.ToString());
                    i++;
                }
            }
            else
            {
                regexPattern += Regex.Escape(c.ToString());
                i++;
            }
        }

        regexPattern += "$";
        return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
