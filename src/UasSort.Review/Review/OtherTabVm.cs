// src/UasSort.Review/Review/OtherTabVm.cs
namespace UasSort.Review;

public sealed class OtherRowVm(string text, string? detail, IAsyncRelayCommand? undismissCommand)
{
    public string Text { get; } = text;
    public string? Detail { get; } = detail;
    public IAsyncRelayCommand? UndismissCommand { get; } = undismissCommand;
    public override string ToString() => Text;
}

public sealed class OtherSectionVm(string title, string? note, IReadOnlyList<OtherRowVm> rows, bool isCollapsed)
{
    public string Title { get; } = title;
    public string? Note { get; } = note;
    public IReadOnlyList<OtherRowVm> Rows { get; } = rows;
    public bool IsCollapsed { get; } = isCollapsed;
    public override string ToString() => Title;
}

/// <summary>The Other tab: unknown files, rule skips, probe errors, scan warnings, dismissed items (Ref §9.9).</summary>
public sealed partial class OtherTabVm(Func<IReadOnlyList<Item>, Task> undismiss) : ObservableObject
{
    public ObservableCollection<OtherSectionVm> Sections { get; } = [];

    [ObservableProperty] public partial string Header { get; private set; } = "Other";

    internal void Update(PlanIndex ix)
    {
        var scan = ix.Plan.Base.Scan;
        var sections = new List<OtherSectionVm>();
        var count = 0;

        var unknown = scan.Inventory.Entries.Where(e => e.Class == EntryClass.Unknown).ToList();
        if (unknown.Count > 0)
            sections.Add(new("Unknown files", "Not copied by uas-sort; copy manually if needed",
                             [.. unknown.Select(e => new OtherRowVm(e.RelPath, Fmt.Size(e.Size), null))], false));
        count += unknown.Count;

        var skipped = scan.Inventory.Entries.Where(e => e.Class == EntryClass.Skip).GroupBy(e => e.Rule ?? "Other", StringComparer.Ordinal).ToList();
        if (skipped.Count > 0)
            sections.Add(new("Skipped by rule", null,
                             [.. skipped.Select(g => new OtherRowVm($"{g.Key} · {Fmt.Count(g.Count(), "file", "files")}", null, null))], true));
        count += skipped.Sum(g => g.Count());

        var probe = ix.Plan.Base.Items.Where(i => i.Raw.ProbeError is not null).ToList();
        if (probe.Count > 0)
            sections.Add(new("Probe errors", null, [.. probe.Select(i => new OtherRowVm(i.Raw.Name, i.Raw.ProbeError, null))], false));
        count += probe.Count;

        if (scan.Warnings.Length > 0)
            sections.Add(new("Scan warnings", "Parts of the card couldn't be read; the verdict will be Don't format yet",
                             [.. scan.Warnings.Select(w => new OtherRowVm(w.Message, w.RelPath, null))], false));
        count += scan.Warnings.Length;

        var dismissed = ix.Plan.Base.Items.Where(i => i.Newness is Decided { Kind: DecisionKind.Dismissed }).ToList();
        if (dismissed.Count > 0)
            sections.Add(new("Dismissed by you", null,
                             [.. dismissed.Select(i => new OtherRowVm(i.Raw.Name, ClipRowVm.Status(i).Tooltip,
                                                                      new AsyncRelayCommand(() => undismiss([i]))))], false));
        count += dismissed.Count;

        Sections.Clear();
        foreach (var s in sections) Sections.Add(s);
        Header = "Other · " + count.ToString(CultureInfo.InvariantCulture);
    }
}
