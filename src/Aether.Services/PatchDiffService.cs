using System.Collections.Generic;
using System.Linq;

namespace Aether.Services;

public enum DiffType { Unchanged, Added, Removed, Modified }

public record DiffLine(int? OldLineNumber, int? NewLineNumber, string OldText, string NewText, DiffType Type);

public interface IPatchDiffService
{
    IReadOnlyList<DiffLine> ComputeLineDiffs(string oldText, string newText);
}

public class PatchDiffService : IPatchDiffService
{
    public PatchDiffService() { }
    // Very small line-based diff: uses a longest common subsequence (LCS) approach
    public IReadOnlyList<DiffLine> ComputeLineDiffs(string oldText, string newText)
    {
        var oldLines = SplitLines(oldText).ToArray();
        var newLines = SplitLines(newText).ToArray();

        int m = oldLines.Length, n = newLines.Length;
        var lcs = new int[m + 1, n + 1];
        for (int i = m - 1; i >= 0; i--)
        for (int j = n - 1; j >= 0; j--)
            lcs[i, j] = oldLines[i] == newLines[j] ? 1 + lcs[i + 1, j + 1] : System.Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<DiffLine>();
        int oi = 0, ni = 0;
        while (oi < m || ni < n)
        {
            if (oi < m && ni < n && oldLines[oi] == newLines[ni])
            {
                result.Add(new DiffLine(oi + 1, ni + 1, oldLines[oi], newLines[ni], DiffType.Unchanged));
                oi++; ni++;
            }
            else if (ni < n && (oi == m || lcs[oi, ni + 1] >= lcs[oi + 1, ni]))
            {
                // added in new
                result.Add(new DiffLine(null, ni + 1, string.Empty, newLines[ni], DiffType.Added));
                ni++;
            }
            else if (oi < m && (ni == n || lcs[oi, ni + 1] < lcs[oi + 1, ni]))
            {
                // removed from old
                result.Add(new DiffLine(oi + 1, null, oldLines[oi], string.Empty, DiffType.Removed));
                oi++;
            }
            else
            {
                // fallback
                if (oi < m && ni < n)
                {
                    result.Add(new DiffLine(oi + 1, ni + 1, oldLines[oi], newLines[ni], DiffType.Modified));
                    oi++; ni++;
                }
                else if (oi < m)
                {
                    result.Add(new DiffLine(oi + 1, null, oldLines[oi], string.Empty, DiffType.Removed));
                    oi++;
                }
                else if (ni < n)
                {
                    result.Add(new DiffLine(null, ni + 1, string.Empty, newLines[ni], DiffType.Added));
                    ni++;
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> SplitLines(string s)
    {
        if (s == null) yield break;
        using var sr = new System.IO.StringReader(s);
        string? line;
        while ((line = sr.ReadLine()) != null)
            yield return line;
        // preserve trailing newline as empty line? Not necessary for now
    }
}
