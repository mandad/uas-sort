// tests/UasSort.Review.Tests/LightroomSettingsTests.cs
namespace UasSort.Review.Tests;

public sealed class LightroomSettingsTests
{
    private sealed class Rig
    {
        public FakeUiDispatcher Ui { get; } = new();
        public FakeTimeProvider Time { get; } = new();
        public FakeSettingsStore Store { get; } = new(new SettingsLoad(TestPlans.Settings(), false, null, null));

        public SettingsPageVm Vm(Settings? s = null) => new(s ?? TestPlans.Settings(), Store, _ => Fake.Ledger(), FakeLayout.NewFileSystem(),
            new FakeShellLauncher(), new FakeDialogService(), new FakeFreeSpace(), Time, Ui, () => TestPlans.Ledger(), r => @"C:\AppData\backup");

        public void Flush()
        {
            Time.Advance(SettingsPageVm.SaveDelay);
            Ui.RunAll();
        }
    }

    [Fact]
    public void ChangeLightroomFolder_AValidFolder_IsShownAndSaved()
    {
        var rig = new Rig();
        using var vm = rig.Vm();
        Assert.Equal("Not set", vm.LightroomText);
        vm.ChangeLightroomFolder(@"X:\Photos\Lightroom");
        Assert.Equal((@"X:\Photos\Lightroom", (string?)null), (vm.LightroomFolder, vm.LightroomError));
        Assert.Equal(@"X:\Photos\Lightroom", vm.LightroomText);
        rig.Flush();
        Assert.Equal(@"X:\Photos\Lightroom", rig.Store.Saved[^1].LightroomFolder);
    }

    [Fact]
    public void ChangeLightroomFolder_InsideThePhotoFolder_IsRefusedWithTheReason_AndNotSaved()
    {
        var rig = new Rig();
        using var vm = rig.Vm();
        vm.ChangeLightroomFolder(TestPlans.PhotoRoot + @"\LR");
        Assert.Equal("That folder is inside the photo folder (Picture Offload)", vm.LightroomError);
        Assert.Null(vm.Current.LightroomFolder);
        rig.Flush();
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public void ClearLightroom_RemovesTheFolder()
    {
        var rig = new Rig();
        using var vm = rig.Vm(TestPlans.Settings() with { LightroomFolder = @"X:\Photos\Lightroom" });
        Assert.Equal(@"X:\Photos\Lightroom", vm.LightroomFolder);
        vm.ClearLightroomCommand.Execute(null);
        Assert.Null(vm.Current.LightroomFolder);
        rig.Flush();
        Assert.Null(rig.Store.Saved[^1].LightroomFolder);
    }

    [Fact]
    public void ChangePhotoRoot_IntoTheLightroomFolder_ClearsItWithTheReason()
    {
        var rig = new Rig();
        using var vm = rig.Vm(TestPlans.Settings() with { LightroomFolder = @"X:\Photos\Lightroom" });
        vm.ChangePhotoRoot(@"X:\Photos\Lightroom\Offload");
        Assert.Null(vm.LightroomFolder);
        Assert.Equal("Lightroom folder cleared: That folder contains the photo folder (Picture Offload)", vm.LightroomError);
        rig.Flush();
        Assert.Equal((@"X:\Photos\Lightroom\Offload", (string?)null), (rig.Store.Saved[^1].PhotoRoot, rig.Store.Saved[^1].LightroomFolder));
    }
}
