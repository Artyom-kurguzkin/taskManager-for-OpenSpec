using System.Text.RegularExpressions;
using OpenSpecStore.PublicDTOs;

namespace OpenSpecStore;

/// <summary>
/// The grammar of the board's references, written in notes, descriptions, commit messages and events: <c>#wiN</c> (a work item), <c>#nN</c> (a note) and <c>[[doc]]</c> (a document).
/// One boundary rule: <c>#wiN</c> and <c>#nN</c> are references when the number has at most 9 digits, no letter, digit or underscore follows it, and no letter, digit, underscore or # comes before the #
/// (so #wi123 is not #wi1234, #wi12a, foo#wi12 or ##wi12). <c>[[doc]]</c> needs a closing ]] on the same line. Every reader, writer and renamer of references goes through here.
/// </summary>
internal static partial class References
{
    [GeneratedRegex(@"\[\[(?<doc>[^\]\r\n]+)\]\]|(?<![\w#])(?:#wi(?<wi>\d{1,9})|#n(?<n>\d{1,9}))(?!\w)")]
    private static partial Regex Any();

    [GeneratedRegex(@"^#?(?:wi)?(?<n>\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex Typed();

    [GeneratedRegex(@"^#?wi(?<n>\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex TypedItem();

    internal static IReadOnlyList<Reference> Find(string text)
    {
        List<Reference> found = [];
        foreach (Match m in Any().Matches(text))
        {
            if (m.Groups["doc"].Success)
            {
                string doc = m.Groups["doc"].Value.Trim();
                if (doc.Length > 0) found.Add(new("doc", doc, 0, m.Index, m.Length));
            }
            else if (m.Groups["wi"].Success) found.Add(new("item", m.Groups["wi"].Value, int.Parse(m.Groups["wi"].Value), m.Index, m.Length));
            else found.Add(new("note", m.Groups["n"].Value, int.Parse(m.Groups["n"].Value), m.Index, m.Length));
        }
        return found;
    }

    internal static string Format(string kind, string target) => kind switch
    {
        "item" => "#wi" + target,
        "note" => "#n" + target,
        _ => "[[" + target + "]]",
    };

    /// <summary>The text with each reference replaced by what <paramref name="replace"/> says for it; null keeps it as written.</summary>
    internal static string Rewrite(string text, Func<Reference, string?> replace)
    {
        IReadOnlyList<Reference> found = Find(text);
        if (found.Count == 0) return text;
        System.Text.StringBuilder result = new();
        int at = 0;
        foreach (Reference r in found)
        {
            string? now = replace(r);
            if (now is null) continue;
            result.Append(text, at, r.Start - at).Append(now);
            at = r.Start + r.Length;
        }
        return result.Append(text, at, text.Length - at).ToString();
    }

    internal static string Rename(string text, string kind, int from, int to) =>
        Rewrite(text, r => r.Kind == kind && r.Number == from ? Format(kind, to.ToString()) : null);

    /// <summary>The item number a typed reference names: "#wi12" or "wi12" (and "12", "#12" with <paramref name="bareNumber"/>), whole, in any case; null otherwise.</summary>
    internal static int? ParseItem(string text, bool bareNumber)
    {
        Match m = (bareNumber ? Typed() : TypedItem()).Match(text);
        return m.Success ? int.Parse(m.Groups["n"].Value) : null;
    }

    internal static Func<string, bool> MentionsItem(int id) => text => Find(text).Any(r => r.Kind == "item" && r.Number == id);
}
