namespace UasSort.Testing;

/// <summary>A fresh %TEMP%\uas-sort-test-&lt;guid&gt; folder, deleted on Dispose (Global Constraints: temp artefacts only there).</summary>
public sealed class TestTempDir : IDisposable
{
    public TestTempDir()
    {
        FullPath = Path.Join(Path.GetTempPath(), "uas-sort-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string Combine(params string[] parts) => Path.Join([FullPath, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must never fail a test; the next run uses a new guid.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
