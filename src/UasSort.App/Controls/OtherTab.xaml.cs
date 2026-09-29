// src/UasSort.App/Controls/OtherTab.xaml.cs
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class OtherTab : UserControl
{
    private OtherTabVm? _vm;
    public OtherTab() => InitializeComponent();
    public OtherTabVm? Vm { get => _vm; set { _vm = value; Bindings.Update(); } }
}
