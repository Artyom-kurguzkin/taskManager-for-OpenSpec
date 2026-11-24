namespace TaskManager.AppState;

/// <summary>One pane of a window: an address (base-relative: "", "item/5", "note/3", "settings", "doc/...") shown by its own page.</summary>
public sealed class Pane
{
    internal Pane(int id, string path) { Id = id; Path = path; }
    public int Id { get; }
    public string Path { get; internal set; }
}

/// <summary>The panes of one window, side by side. One is active: the window's address and its side menu follow it. At most <see cref="Max"/>; never fewer than one.</summary>
public sealed class Panes
{
    public const int Max = 4;
    private readonly List<Pane> all;
    private int nextId = 1;

    public Panes()
    {
        all = [new Pane(nextId++, "")];
        Active = all[0];
    }

    public IReadOnlyList<Pane> All => all;
    public Pane Active { get; private set; }

    /// <summary>Something changed: the panes, which one is active, or an address.</summary>
    public event Action? Changed;

    /// <summary>Adds a pane to the right of the active one, showing the same page, and makes it active. A fifth is refused (null).</summary>
    public Pane? Split()
    {
        if (all.Count >= Max) return null;
        Pane added = new(nextId++, Active.Path);
        all.Insert(all.IndexOf(Active) + 1, added);
        Active = added;
        Changed?.Invoke();
        return added;
    }

    /// <summary>Closes a pane; the last one stays. The neighbour (the one that moves into its place, else the one before) becomes active when it was the active one.</summary>
    public bool Close(Pane pane)
    {
        int at = all.IndexOf(pane);
        if (at < 0 || all.Count == 1) return false;
        all.RemoveAt(at);
        if (Active == pane) Active = all[Math.Min(at, all.Count - 1)];
        Changed?.Invoke();
        return true;
    }

    public void Activate(Pane pane)
    {
        if (Active == pane || !all.Contains(pane)) return;
        Active = pane;
        Changed?.Invoke();
    }

    public void Navigate(Pane pane, string path)
    {
        if (pane.Path == path || !all.Contains(pane)) return;
        pane.Path = path;
        Changed?.Invoke();
    }

    /// <summary>The pane an item opened from a board or list in <paramref name="from"/> goes to: that pane alone, else its right neighbour, or the left one from the last pane.</summary>
    public Pane ItemTarget(Pane from)
    {
        int at = all.IndexOf(from);
        if (all.Count == 1 || at < 0) return from;
        return at + 1 < all.Count ? all[at + 1] : all[at - 1];
    }

    /// <summary>The pane's place, counting from 1.</summary>
    public int Number(Pane pane) => all.IndexOf(pane) + 1;
}
