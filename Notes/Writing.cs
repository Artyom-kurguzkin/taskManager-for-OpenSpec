using OpenSpecStore;
using Notes.PublicDTOs;
using WorkItems;
using WorkItems.PublicDTOs;

namespace Notes;

/// <summary>How a note is composed when a caller gives only text: its kind, its title, its heading, its links, and the signature on the note and the item.</summary>
internal static class Writing
{
    /// <summary>Why a draft cannot be written (null when it can), checked before anything is written.</summary>
    internal static string? Check(NoteDraft d)
    {
        if (string.IsNullOrWhiteSpace(d.Body)) return "A note needs a body: say what happened, in plain words.";
        string? kind = KindOf(d.Kind);
        if (kind is not null && !NoteKinds.All.Contains(kind)) return $"Unknown note kind \"{kind}\": the kinds are {string.Join(", ", NoteKinds.All.Select(k => $"\"{k}\""))}; leave `kind` out for an ordinary entry.";
        foreach (string url in d.Attachments ?? [])
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return $"Attachment \"{url}\" is not a full link: use http:// or https:// URLs. Files go in the note body, not as attachments.";
        return null;
    }

    private static string? KindOf(string? kind) => string.IsNullOrWhiteSpace(kind) ? null : kind.Trim().ToLowerInvariant();

    /// <summary>The title of an untitled note: its first non-empty line without leading # and spaces, at most 80 characters.</summary>
    internal static string TitleFrom(string body)
    {
        string line = body.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        line = line.TrimStart('#', ' ');
        return line.Length == 0 ? "Note" : line.Length > 80 ? line[..80].TrimEnd() : line;
    }

    /// <summary>Writes the draft as a note of the item by <paramref name="who"/>, signs the note and the item. <paramref name="knownCommits"/> says which of the draft's commit hashes the repository's history has.</summary>
    internal static WrittenNote Write(int id, string who, NoteDraft d, Func<IReadOnlyList<string>, IReadOnlyList<string>>? knownCommits)
    {
        WrittenNote Fail(string message, int noteId = 0) => new(false, message, noteId, id, "", [], [], "", null);
        if (Check(d) is string problem) return Fail(problem);
        if (WorkItemsApi.Get(id) is null) return Fail($"There is no work item #wi{id}. Use board_list to find the id.");
        string? kind = KindOf(d.Kind);
        string body = d.Body;
        bool titled = !string.IsNullOrWhiteSpace(d.Title);
        string title = titled ? d.Title!.Trim() : kind == NoteKinds.Decision ? "Decision" : TitleFrom(body);
        (bool ok, string message, int noteId) = Commands.Create(id, who, DateTime.Now, kind);
        if (!ok) return Fail($"Could not create the note: {message}");
        string? failure = null;
        void Step(string name, (bool Success, string Message) r) { if (failure is null && !r.Success) failure = $"Note #n{noteId} was created but {name} failed: {r.Message}"; }
        string text = body.Replace("\r\n", "\n").Trim();
        // the first line became the title: the text under the heading is the rest, so the entry does not say it twice
        if (!titled && !text.StartsWith('#') && text.Split('\n')[0].Trim() == title) text = text[text.Split('\n')[0].Length..].TrimStart();
        Step("setting the title", Commands.SetTitle(noteId, title));
        bool heading = text.StartsWith("# ", StringComparison.Ordinal) || (!titled && text.StartsWith('#'));   // a body that opens with its own heading is not given a second one
        if (failure is null) Step("writing the body", Commands.SaveBody(noteId, heading ? text + "\n" : $"# {title}\n\n{text}\n"));
        foreach (string link in d.Links ?? []) if (failure is null) Step($"adding the link {link}", Commands.AddManualLink(noteId, link));
        List<string> warnings = [];
        if (d.Commits is { Count: > 0 } && failure is null)
        {
            Step("linking the commits", Commands.SetLinkedCommits(noteId, d.Commits));
            IReadOnlyList<string> known = knownCommits?.Invoke(d.Commits) ?? [];
            foreach (string c in d.Commits.Where(c => !known.Any(k => k.StartsWith(c, StringComparison.OrdinalIgnoreCase) || c.StartsWith(k, StringComparison.OrdinalIgnoreCase)))) warnings.Add($"Commit {c} is not in this repository's history (yet).");
        }
        foreach (string url in d.Attachments ?? []) if (failure is null) Step($"attaching {url}", Commands.AttachLink(noteId, url));
        if (failure is not null) return Fail(failure, noteId);
        (bool promoted, string promoteMessage, IReadOnlyList<string> subtasks) = Commands.PromoteSubtasks(noteId);
        if (!promoted) warnings.Add($"Could not turn the note's checkboxes into subtasks: {promoteMessage}");
        Step("signing the note", Commands.StampAgent(noteId, who));
        Step("signing the item", WorkItemsApi.StampAgent(id, who));
        if (failure is not null) return Fail(failure, noteId);
        WorkItem item = WorkItemsApi.Get(id)!;
        return new(true, "", noteId, id, Path.GetRelativePath(OpenSpecStoreApi.RepoRoot!, Queries.GetAll().FirstOrDefault(n => n.Id == noteId)?.Path ?? item.Path).Replace('\\', '/'), warnings, subtasks, item.Revision, kind);
    }

    /// <summary>
    /// An item ends without being done: hidden (its status stays), the resolution logged, and a decision note titled by it with <paramref name="why"/> as its body
    /// (the resolution's own title when none is given).
    /// </summary>
    internal static ResolvedItem Resolve(int id, Resolution resolution, string? why, string who)
    {
        (bool hidden, string message) = WorkItemsApi.Hide(id);
        if (!hidden) return new(false, message, null);
        WorkItemsApi.LogEvent(id, "resolution", resolution.Event);
        return new(true, "", Write(id, who, new NoteDraft(string.IsNullOrWhiteSpace(why) ? $"{resolution.Title}." : why, resolution.Title, Kind: NoteKinds.Decision), null));
    }
}
