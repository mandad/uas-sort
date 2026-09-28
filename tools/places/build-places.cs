#:project ../../src/UasSort.Core/UasSort.Core.csproj
#pragma warning disable RS0030 // tool: reads the GeoNames dump and writes places.bin.gz; never shipped, never run by the app

using System.Globalization;
using System.IO.Compression;
using UasSort.Core;       // a file-based app gets no GlobalUsings.Core.cs, so the Core namespaces it uses are imported here
using UasSort.Core.Geo;

// Builds src/UasSort.App/places.bin.gz (Ref §8.7) from GeoNames US.zip (populated places + the listed feature codes)
// and cities5000.zip (populated places worldwide). GeoNames data: CC-BY 4.0 (credited in About).
if (args.Length != 6 || args[0] != "--us" || args[2] != "--cities" || args[4] != "--out")
{
    Console.Error.WriteLine("usage: dotnet run --file tools/places/build-places.cs -- --us <US.zip> --cities <cities5000.zip> --out <places.bin.gz>");
    return 2;
}

var records = PlacesFormat.Build(ReadLines(args[1], "US.txt"), ReadLines(args[3], "cities5000.txt"));
var populated = records.Count(r => r.Class == PlaceClass.Populated);
var zones = records.Select(r => r.TzId).Distinct(StringComparer.Ordinal).Count();
var tmp = args[5] + ".tmp";
using (var output = File.Create(tmp))
    PlacesFormat.Write(output, records);
File.Move(tmp, args[5], overwrite: true);
var size = new FileInfo(args[5]).Length;
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"places: {records.Length:N0} records ({populated:N0} populated, {records.Length - populated:N0} features), {zones} zones -> {args[5]} ({size / 1e6:0.0} MB)"));
return 0;

static IEnumerable<string> ReadLines(string zipPath, string entryName)
{
    using var zip = ZipFile.OpenRead(zipPath);
    var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"{zipPath} has no {entryName}");
    using var reader = new StreamReader(entry.Open());
    while (reader.ReadLine() is { } line)
        yield return line;
}
