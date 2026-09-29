// tests/UasSort.Platform.Tests/XamlLintTests.cs  (Xunit comes from the csproj <Using>, UasSort.Testing from GlobalUsings.cs)
using System.Text.RegularExpressions;

namespace UasSort.Platform.Tests;

/// <summary>Ref §2.4: x:Bind only, no property paths, images only from ms-appx:/// or the Thumb.Key attached property.</summary>
public static partial class XamlLint
{
    private static readonly string[] Forbidden = ["{Binding", "DisplayMemberPath", "TextMemberPath", "SelectedValuePath"];

    // An image element and the attributes that name its source.
    [GeneratedRegex(@"<(?<el>Image|BitmapImage|ImageIconSource|ImageBrush|SvgImageSource|BitmapIcon)\b(?<attrs>[^>]*)>",
                    RegexOptions.Singleline)]
    private static partial Regex ImageElement();

    [GeneratedRegex(@"\b(?<name>Source|UriSource|ImageSource)\s*=\s*""(?<value>[^""]*)""")]
    private static partial Regex SourceAttribute();

    // Property-element form: <Image.Source> ... </Image.Source>
    [GeneratedRegex(@"<(Image|ImageBrush|ImageIconSource|BitmapIcon)\.(Source|ImageSource|UriSource)\b")]
    private static partial Regex SourcePropertyElement();

    public static IReadOnlyList<string> Check(string fileName, string xaml)
    {
        var problems = new List<string>();
        var lines = xaml.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            foreach (var f in Forbidden)
                if (lines[i].Contains(f, StringComparison.Ordinal))
                    problems.Add($"{fileName}:{i + 1}: forbidden '{f}' (x:Bind only, Ref §2.4)");

        foreach (Match m in ImageElement().Matches(xaml))
        {
            int line = xaml[..m.Index].Count(c => c == '\n') + 1;
            foreach (Match a in SourceAttribute().Matches(m.Groups["attrs"].Value))
                if (!a.Groups["value"].Value.StartsWith("ms-appx:///", StringComparison.Ordinal))
                    problems.Add($"{fileName}:{line}: {m.Groups["el"].Value}.{a.Groups["name"].Value}=\"{a.Groups["value"].Value}\" " +
                                 "is not ms-appx:/// (use ms-appx:/// or local:Thumb.Key)");
        }
        foreach (Match m in SourcePropertyElement().Matches(xaml))
        {
            int line = xaml[..m.Index].Count(c => c == '\n') + 1;
            problems.Add($"{fileName}:{line}: image source set through a property element; use ms-appx:/// or local:Thumb.Key");
        }
        return problems;
    }
}

public sealed class XamlLintTests
{
    [Theory]
    [InlineData("""<TextBlock Text="{Binding Name}"/>""")]
    [InlineData("""<ComboBox DisplayMemberPath="Name"/>""")]
    [InlineData("""<AutoSuggestBox TextMemberPath="Text"/>""")]
    [InlineData("""<ComboBox SelectedValuePath="Id"/>""")]
    [InlineData("""<Image Source="C:\x.png"/>""")]
    [InlineData("""<Image Source="{x:Bind Path}"/>""")]
    [InlineData("""<BitmapImage UriSource="https://example.org/a.png"/>""")]
    [InlineData("""<ImageIconSource ImageSource="Assets/AppIcon.ico"/>""")]
    [InlineData("<Image>\n<Image.Source><BitmapImage/></Image.Source></Image>")]
    public void Lint_FlagsForbiddenConstructs(string xaml) =>
        Assert.NotEmpty(XamlLint.Check("sample.xaml", xaml));

    [Theory]
    [InlineData("""<TextBlock Text="{x:Bind Vm.Name, Mode=OneWay}"/>""")]
    [InlineData("""<Image Source="ms-appx:///Assets/AppIcon.png"/>""")]
    [InlineData("""<ImageIconSource ImageSource="ms-appx:///Assets/AppIcon.ico"/>""")]
    [InlineData("""<Image ctl:Thumb.Key="{x:Bind ThumbKey}" Width="96"/>""")]
    public void Lint_AcceptsAllowedConstructs(string xaml) =>
        Assert.Empty(XamlLint.Check("sample.xaml", xaml));

    [Fact]
    public void AppXaml_IsClean()
    {
        // RepoPaths.EnumerateFiles skips bin, obj and the other build folders (Part 01 Task 01.2).
        var files = RepoPaths.EnumerateFiles("*.xaml", "src/UasSort.App").ToList();
        Assert.NotEmpty(files);
        var problems = files.SelectMany(f => XamlLint.Check(RepoPaths.Relative(f), File.ReadAllText(f))).ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
