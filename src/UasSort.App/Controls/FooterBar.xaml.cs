// src/UasSort.App/Controls/FooterBar.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class FooterBar : UserControl
{
    private ReviewVm? _review;
    public FooterBar() => InitializeComponent();
    public ReviewVm? Review { get => _review; set { _review = value; Bindings.Update(); } }

    /// <summary>An issue row goes to its anchor (selects the card and the clip, or the photo's day; Ref §9.10).</summary>
    private void OnIssueClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is IssueVm { Anchor: { } anchor } && _review is not null)
        {
            IssuesFlyout.Hide();
            _review.GoTo(anchor);
        }
    }
}
