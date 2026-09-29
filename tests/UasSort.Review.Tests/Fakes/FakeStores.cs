// tests/UasSort.Review.Tests/Fakes/FakeStores.cs
namespace UasSort.Review.Tests;

internal sealed class FakeSettingsStore(SettingsLoad load) : ISettingsStore
{
    public List<Settings> Saved { get; } = [];
    public List<string> Log { get; } = [];
    public SettingsLoad Load(bool readOnly = false) => load;
    public void Save(Settings s) { Saved.Add(s); Log.Add("Save:" + s.VideoRoot); }
}
