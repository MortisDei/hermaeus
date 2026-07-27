using System.Text;
using System.Text.RegularExpressions;

namespace Hermaeus.Core.Services;

/// <summary>
/// The one glob engine in this codebase (r23 3.4, r24 doc 03 "one glob
/// engine"). Originally the Agent's <c>glob_files</c>/workspace-policy
/// matcher; lives in Core so both Hermaeus.Agent (glob_files, workspace
/// policy) and Hermaeus.Rag (watched sources, doc 03) call the exact same
/// matcher. A second implementation could diverge from what
/// <c>glob_files</c> itself matches, and that divergence would be a
/// security bug for workspace policy and a correctness bug for watched
/// sources.
/// </summary>
public static class GlobMatcher
{
    public static Regex ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        var normalized = pattern.Replace('\\', '/');
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (c == '*')
            {
                if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                {
                    // "**/" matches zero or more whole path segments (so
                    // "src/**/*.cs" also matches "src/Foo.cs" directly, not
                    // just files at least one directory deeper); a bare "**"
                    // with no following separator matches anything.
                    if (i + 2 < normalized.Length && normalized[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        sb.Append(".*");
                        i++;
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
    }

    public static bool IsMatch(string pattern, string relativePath)
    {
        try { return ToRegex(pattern).IsMatch(relativePath); }
        catch { return false; }
    }
}
