using System;
using System.Collections.Generic;

namespace HandWritten_OCR.Helpers;

/// <summary>
/// Fuzzy-corrects the leading word of OCR output to the nearest known month,
/// but ONLY for date/month columns (e.g. "When Born", "When Registered").
///
/// The exact-match cursive table in <c>TrOcrService</c> fixes systematic misreads
/// (e.g. cursive "Sept" → "Sefre"); this catches the long tail of near-misses
/// (e.g. "Septr", "Octr", "Jnue") that an exact table can't enumerate. It is
/// scoped to date fields so generic fields like Name or Street are never altered.
/// </summary>
public static class MonthFieldCorrector
{
    // Canonical output form → accepted spellings as written in these registry
    // books. Spellings are matched case-insensitively against the leading word.
    private static readonly (string Canonical, string[] Forms)[] s_months =
    {
        ("Jany",  new[] { "jany", "jan", "janu", "january" }),
        ("Feb",   new[] { "feb", "febr", "february" }),
        ("March", new[] { "march", "mar" }),
        ("April", new[] { "april", "apr" }),
        ("May",   new[] { "may" }),
        ("June",  new[] { "june", "jun" }),
        ("July",  new[] { "july", "jul" }),
        ("Aug",   new[] { "aug", "august" }),
        ("Sept",  new[] { "sept", "sep", "septr", "september" }),
        ("Oct",   new[] { "oct", "octr", "october" }),
        ("Nov",   new[] { "nov", "novr", "november" }),
        ("Dec",   new[] { "dec", "decr", "december" }),
    };

    // Keywords that mark a field as carrying a date/month value.
    private static readonly string[] s_dateFieldKeywords =
        { "when", "registered", "date", "month", "birth" };

    // Keywords that veto a match — these fields hold places or free text, not
    // dates, even when they share a word with a date column (e.g. "Where Born"
    // is a birthplace, "When was last vote cast and when" is free text).
    private static readonly string[] s_excludeKeywords =
        { "where", "vote", "place" };

    /// <summary>
    /// True when <paramref name="columnName"/> names a date/month field. The
    /// match is a case-insensitive substring test against known keywords, with
    /// place/free-text columns explicitly vetoed.
    /// </summary>
    public static bool IsDateField(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return false;

        foreach (string ex in s_excludeKeywords)
            if (columnName.Contains(ex, StringComparison.OrdinalIgnoreCase))
                return false;

        foreach (string kw in s_dateFieldKeywords)
            if (columnName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Returns <paramref name="text"/> with its leading word snapped to the
    /// nearest month when <paramref name="columnName"/> is a date field and the
    /// word is within edit-distance tolerance of a known spelling. Otherwise the
    /// text is returned unchanged. Digits and punctuation after the word are
    /// preserved exactly (they are reliable).
    /// </summary>
    public static string Apply(string text, string? columnName)
    {
        if (!IsDateField(columnName) || string.IsNullOrEmpty(text)) return text;

        int wordEnd = 0;
        while (wordEnd < text.Length && char.IsLetter(text[wordEnd])) wordEnd++;
        if (wordEnd == 0) return text;

        string firstWord = text[..wordEnd];
        string lower = firstWord.ToLowerInvariant();

        string? bestCanonical = null;
        int bestDistance = int.MaxValue;

        foreach (var (canonical, forms) in s_months)
        {
            foreach (string form in forms)
            {
                int d = Levenshtein(lower, form);
                // Tolerance scales with the candidate length: short forms like
                // "may" must match almost exactly; longer ones allow more slack.
                int tolerance = Math.Max(1, form.Length / 3);
                if (d <= tolerance && d < bestDistance)
                {
                    bestDistance = d;
                    bestCanonical = canonical;
                }
            }
        }

        if (bestCanonical is null) return text;
        return bestCanonical + text[wordEnd..];
    }

    // Classic two-row Levenshtein edit distance.
    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        int[] prev = new int[b.Length + 1];
        int[] curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(prev[j] + 1, curr[j - 1] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
