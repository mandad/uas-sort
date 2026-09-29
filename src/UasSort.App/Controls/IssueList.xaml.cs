// src/UasSort.App/Controls/IssueList.xaml.cs
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class IssueList : UserControl
{
    public IssueList() => InitializeComponent();

    /// <summary>Replaces the bars: one closed-for-good (not closable) InfoBar per line, all with this severity.</summary>
    public void Show(IReadOnlyList<string> lines, InfoBarSeverity severity)
    {
        Host.Children.Clear();
        foreach (var line in lines)
            Host.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = severity, Message = line });
    }
}
