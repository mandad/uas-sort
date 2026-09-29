// src/UasSort.Review/Review/IssuesVm.cs
namespace UasSort.Review;

public sealed class IssueVm
{
    public IssueVm(Issue issue, Func<Issue, QuickFix, Task> runFix)
    {
        Model = issue;
        QuickFixes = [.. issue.QuickFixes.Select(f => new QuickFixVm(f.Label, () => runFix(issue, f)))];
    }

    public Issue Model { get; }
    public IssueSeverity Severity => Model.Severity;
    public IssueCode Code => Model.Code;
    public string Message => Model.Message;
    public ItemId? Anchor => Model.Anchor;
    public string Glyph => Model.Severity switch { IssueSeverity.Blocking => "⛔", IssueSeverity.Warning => "⚠", _ => "ⓘ" };
    public IReadOnlyList<QuickFixVm> QuickFixes { get; }
    public override string ToString() => Message;
}

/// <summary>The ⛔/⚠/ⓘ counters and their flyout (Ref §9.10).</summary>
public sealed partial class IssuesVm(Func<Issue, QuickFix, Task> runFix) : ObservableObject
{
    public ObservableCollection<IssueVm> Entries { get; } = [];

    [ObservableProperty] public partial int BlockingCount { get; private set; }
    [ObservableProperty] public partial int WarningCount { get; private set; }
    [ObservableProperty] public partial int InfoCount { get; private set; }
    [ObservableProperty] public partial string? BlockedTooltip { get; private set; }

    public bool HasBlocking => BlockingCount > 0;

    internal void Update(ImmutableArray<Issue> issues)
    {
        Entries.Clear();
        foreach (var i in issues.OrderBy(i => i.Severity)) Entries.Add(new IssueVm(i, runFix));
        BlockingCount = issues.Count(i => i.Severity == IssueSeverity.Blocking);
        WarningCount = issues.Count(i => i.Severity == IssueSeverity.Warning);
        InfoCount = issues.Count(i => i.Severity == IssueSeverity.Info);
        var blocking = issues.Where(i => i.Severity == IssueSeverity.Blocking).Select(i => "• " + i.Message).ToList();
        BlockedTooltip = blocking.Count == 0 ? null : "Fix before offloading:\n" + string.Join("\n", blocking);
        OnPropertyChanged(nameof(HasBlocking));
    }
}
