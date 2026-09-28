using System.Diagnostics;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using GeoTimeZone;

// Stream wrapper that counts bytes actually read (to prove the MP4 reader seeks past mdat)
sealed class CountingStream(Stream inner) : Stream {
    public long BytesRead; public int Seeks;
    public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
    public override long Length => inner.Length; public override long Position { get => inner.Position; set { Seeks++; inner.Position = value; } }
    public override void Flush() {}
    public override int Read(byte[] b, int o, int c) { int n = inner.Read(b, o, c); BytesRead += n; return n; }
    public override int Read(Span<byte> s) { int n = inner.Read(s); BytesRead += n; return n; }
    public override long Seek(long off, SeekOrigin or) { Seeks++; return inner.Seek(off, or); }
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}

static class P {
    static string Root => OperatingSystem.IsWindows() ? @"C:\Users\damia\OneDrive\Pictures\UAS Videos" : "/mnt/c/Users/damia/OneDrive/Pictures/UAS Videos";
    static string J(params string[] parts) => Path.Combine([Root, .. parts]);

    static void Main(string[] args) {
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier})");
        var mode = args.Length > 0 ? args[0] : "all";
        if (mode is "all" or "meta") Meta();
        if (mode is "all" or "tz") Tz();
        if (mode is "all" or "hash") Hash();
        if (mode is "all" or "drives") Drives();
        if (mode is "copy") CopyTest();
        if (mode is "ledger") LedgerTest();
    }

    static void ReadAny(string path) {
        Console.WriteLine($"\n== {path}");
        var fi = new FileInfo(path);
        Console.WriteLine($"   size={fi.Length:N0} attrs=0x{(int)fi.Attributes:X} ({fi.Attributes})");
        if (((int)fi.Attributes & (0x400000 | 0x1000 | 0x40000)) != 0) { Console.WriteLine("   SKIP: cloud placeholder (RecallOnDataAccess/Offline/RecallOnOpen)"); return; }
        var sw = Stopwatch.StartNew();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        var cs = new CountingStream(fs);
        IReadOnlyList<MetadataExtractor.Directory> dirs = ImageMetadataReader.ReadMetadata(cs, Path.GetFileName(path));
        sw.Stop();
        Console.WriteLine($"   parsed in {sw.ElapsedMilliseconds} ms; bytesRead={cs.BytesRead:N0} ({100.0*cs.BytesRead/fi.Length:F4}% of file), seeks={cs.Seeks}");
        Console.WriteLine($"   directories: {string.Join(", ", dirs.Select(d => d.Name))}");
        int i = 0;
        foreach (var sub in dirs.OfType<ExifSubIfdDirectory>()) {
            i++;
            var has = sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dto);
            Console.WriteLine($"   ExifSubIfd#{i}: tags={sub.TagCount} DateTimeOriginal={(has ? dto.ToString("yyyy-MM-dd HH:mm:ss") + " Kind=" + dto.Kind : "<absent>")} OffsetTimeOriginal={sub.GetDescription(ExifDirectoryBase.TagTimeZoneOriginal) ?? "<none>"} NewSubfileType={sub.GetDescription(ExifDirectoryBase.TagNewSubfileType) ?? "-"} Width={sub.GetDescription(ExifDirectoryBase.TagImageWidth) ?? "-"}");
        }
        var any = dirs.OfType<ExifDirectoryBase>().FirstOrDefault(d => d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal));
        Console.WriteLine($"   => DateTimeOriginal found in: {any?.Name ?? "none"} = {any?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal)}; IFD0 DateTime={dirs.OfType<ExifIfd0Directory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagDateTime)}");
        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (ifd0 != null) Console.WriteLine($"   Make={ifd0.GetDescription(ExifDirectoryBase.TagMake)} Model={ifd0.GetDescription(ExifDirectoryBase.TagModel)}");
        var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();
        if (gps != null) {
            Console.WriteLine($"   GPS TryGetGeoLocation: {(gps.TryGetGeoLocation(out var gl) ? $"{gl.Latitude:F6}, {gl.Longitude:F6}" : "n/a")}");
            Console.WriteLine($"   GPS DateStamp={gps.GetDescription(GpsDirectory.TagDateStamp) ?? "<none>"} TimeStamp={gps.GetDescription(GpsDirectory.TagTimeStamp) ?? "<none>"} Alt={gps.GetDescription(GpsDirectory.TagAltitude) ?? "<none>"}");
        }
        foreach (var mvhd in dirs.OfType<QuickTimeMovieHeaderDirectory>()) {
            Console.WriteLine($"   mvhd Created desc='{mvhd.GetDescription(QuickTimeMovieHeaderDirectory.TagCreated)}'");
            if (mvhd.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var c)) Console.WriteLine($"   mvhd Created DateTime = {c:yyyy-MM-ddTHH:mm:ss} Kind={c.Kind}");
            Console.WriteLine($"   mvhd Duration='{mvhd.GetDescription(QuickTimeMovieHeaderDirectory.TagDuration)}'");
        }
        foreach (var d in dirs.Where(d => d.Name.Contains("QuickTime") || d.Name.Contains("Track"))) {
            foreach (var t in d.Tags.Take(40)) Console.WriteLine($"     [{d.Name}] {t.Name} = {t.Description}");
        }
    }

    static void Meta() {
        string[] files = [
            J("Picture Offload", "001_0087", "PANO_0001.DNG"),
            J("2026", "2026-09", "2026-09-27 Zachar Bay", "DJI_20260927140627_0128_D.MP4"),
            J("2022", "2022-03-27 Makaha Valley", "MAX_0061.MP4"),
        ];
        foreach (var f in files) { try { ReadAny(f); } catch (Exception ex) { Console.WriteLine($"   ERROR {ex.GetType().Name}: {ex.Message}"); } }
    }

    static void Tz() {
        Console.WriteLine("\n== GeoTimeZone + TimeZoneInfo");
        var before = GC.GetTotalMemory(true); var ws0 = Environment.WorkingSet;
        var sw = Stopwatch.StartNew();
        var first = TimeZoneLookup.GetTimeZone(57.55044, -153.73897);
        var el = sw.ElapsedMilliseconds;
        Console.WriteLine($"   first lookup (includes data load) {el} ms; managed heap delta={(GC.GetTotalMemory(true)-before)/1048576.0:F1} MB; working set delta={(Environment.WorkingSet-ws0)/1048576.0:F1} MB");
        (string name, double lat, double lon)[] pts = [
            ("Zachar Bay", 57.55044, -153.73897), ("Kodiak PANO", 57.7996, -152.3902), ("Anvil Mtn/Nome", 64.5626762, -165.37),
            ("Newport RI", 41.49, -71.31), ("Makaha HI", 21.47, -158.21), ("Adak (Aleutians)", 51.88, -176.66),
            ("Gulf of Alaska open ocean", 57.0, -148.0), ("Bering Sea off Nome", 64.0, -168.5), ("Hyder AK (border)", 55.9166, -130.0247),
            ("Stewart BC (border)", 55.9364, -129.9906), ("Metlakatla AK", 55.129, -131.572), ("Lake Erie mid", 42.2, -81.2)];
        sw.Restart();
        foreach (var p in pts) {
            var r = TimeZoneLookup.GetTimeZone(p.lat, p.lon);
            string conv;
            try {
                var tzi = TimeZoneInfo.FindSystemTimeZoneById(r.Result);
                var utc = new DateTime(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc);
                var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tzi);
                conv = $"TZI.Id={tzi.Id} HasIana={tzi.HasIanaId} 2026-09-27T18:06:27Z -> {local:yyyy-MM-dd HH:mm} (off {tzi.GetUtcOffset(utc)})";
            } catch (Exception ex) { conv = $"FindSystemTimeZoneById FAILED {ex.GetType().Name}"; }
            Console.WriteLine($"   {p.name,-28} {p.lat,9:F4},{p.lon,10:F4} -> {r.Result,-22} alts=[{string.Join(",", r.AlternativeResults)}]  {conv}");
        }
        Console.WriteLine($"   {pts.Length} lookups+conversions {sw.Elapsed.TotalMilliseconds:F1} ms; heap now {GC.GetTotalMemory(false)/1048576.0:F1} MB");
        Console.WriteLine($"   TryConvertIanaIdToWindowsId(America/Anchorage): {(TimeZoneInfo.TryConvertIanaIdToWindowsId("America/Anchorage", out var w) ? w : "FAILED")}");
        Console.WriteLine($"   Local zone: {TimeZoneInfo.Local.Id} HasIanaId={TimeZoneInfo.Local.HasIanaId} ; TryConvertWindowsIdToIanaId: {(TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var ia) ? ia : "n/a")}");
        Console.WriteLine($"   Eastern (drone clock) America/New_York: {TimeZoneInfo.FindSystemTimeZoneById("America/New_York").GetUtcOffset(new DateTime(2026,9,27,18,0,0,DateTimeKind.Utc))}");
    }

    static void Hash() {
        Console.WriteLine("\n== Hash throughput (in-memory 256 MB buffer, CPU-bound)");
        var buf = new byte[256 * 1024 * 1024]; new Random(1).NextBytes(buf);
        void T(string n, Func<byte[], string> f) { f(buf.AsSpan(0, 1024).ToArray()); var sw = Stopwatch.StartNew(); var h = f(buf); sw.Stop(); Console.WriteLine($"   {n,-10} {buf.Length / 1048576.0 / sw.Elapsed.TotalSeconds,8:F0} MB/s  {h[..16]}"); }
        T("XxHash3", b => Convert.ToHexString(XxHash3.Hash(b)));
        T("XxHash128", b => Convert.ToHexString(XxHash128.Hash(b)));
        T("XxHash64", b => Convert.ToHexString(XxHash64.Hash(b)));
        T("Crc64", b => Convert.ToHexString(Crc64.Hash(b)));
        T("SHA256", b => Convert.ToHexString(SHA256.HashData(b)));
        T("MD5", b => Convert.ToHexString(MD5.HashData(b)));
        // incremental streaming API over a real local file
        var p = J("2026", "2026-09", "2026-09-27 Zachar Bay", "DJI_20260927140627_0128_D.MP4");
        var fi = new FileInfo(p);
        if (((int)fi.Attributes & 0x400000) == 0) {
            var sw = Stopwatch.StartNew();
            var h = new XxHash3();
            using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan)) {
                var chunk = new byte[4 << 20]; int n; while ((n = fs.Read(chunk)) > 0) h.Append(chunk.AsSpan(0, n));
            }
            sw.Stop();
            Console.WriteLine($"   file XxHash3 streaming {fi.Length/1048576.0:F0} MB in {sw.Elapsed.TotalSeconds:F2}s = {fi.Length/1048576.0/sw.Elapsed.TotalSeconds:F0} MB/s -> {Convert.ToHexString(h.GetCurrentHash())}");
        }
    }

    static void Drives() {
        Console.WriteLine("\n== DriveInfo.GetDrives()");
        var swd = Stopwatch.StartNew(); var all = DriveInfo.GetDrives(); Console.WriteLine($"   GetDrives() {swd.ElapsedMilliseconds} ms");
        foreach (var d in all) { swd.Restart();
            string extra = "";
            try { if (d.IsReady) extra = $"fmt={d.DriveFormat} label='{d.VolumeLabel}' free={d.AvailableFreeSpace/1e9:F1}GB total={d.TotalSize/1e9:F1}GB DCIM={System.IO.Directory.Exists(Path.Combine(d.RootDirectory.FullName, "DCIM"))}"; else extra = "not ready"; }
            catch (Exception ex) { extra = ex.GetType().Name; }
            Console.WriteLine($"   {d.Name} type={d.DriveType} {extra} ({swd.ElapsedMilliseconds} ms)");
        }
    }

    static void CopyTest() {
        // Only touches %TEMP%\uas-sort-spike-copy, deleted at the end.
        var dir = Path.Combine(Path.GetTempPath(), "uas-sort-spike-copy");
        Console.WriteLine($"\n== Copy test in {dir}");
        System.IO.Directory.CreateDirectory(dir);
        try {
            var src = Path.Combine(dir, "src.bin"); File.WriteAllBytes(src, new byte[8 << 20]);
            var t0 = new DateTime(2026, 5, 23, 1, 52, 51, DateTimeKind.Utc);
            File.SetCreationTimeUtc(src, t0.AddMinutes(-5)); File.SetLastWriteTimeUtc(src, t0);
            // 1) File.Copy
            var d1 = Path.Combine(dir, "copy1.bin"); File.Copy(src, d1);
            Console.WriteLine($"   File.Copy: src C={File.GetCreationTimeUtc(src):u} W={File.GetLastWriteTimeUtc(src):u} | dst C={File.GetCreationTimeUtc(d1):u} W={File.GetLastWriteTimeUtc(d1):u}");
            // 2) streaming copy with hash to a .tmp, flush to disk, set times, atomic rename (no overwrite)
            var tmp = Path.Combine(dir, "DJI_x.MP4.uas-sort.tmp"); var fin = Path.Combine(dir, "DJI_x.MP4");
            var hs = new XxHash128();
            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            using (var o = new FileStream(tmp, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0, PreallocationSize = i.Length, Options = FileOptions.SequentialScan })) {
                var buf = new byte[4 << 20]; int n; while ((n = i.Read(buf)) > 0) { hs.Append(buf.AsSpan(0, n)); o.Write(buf, 0, n); }
                o.Flush(flushToDisk: true);
            }
            File.SetCreationTimeUtc(tmp, File.GetCreationTimeUtc(src)); File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(src));
            File.Move(tmp, fin, overwrite: false);
            var hd = new XxHash128(); using (var f = File.OpenRead(fin)) hd.Append(f);
            Console.WriteLine($"   stream+rename: dst C={File.GetCreationTimeUtc(fin):u} W={File.GetLastWriteTimeUtc(fin):u} hashMatch={hs.GetCurrentHash().AsSpan().SequenceEqual(hd.GetCurrentHash())}");
            unsafe {
                const FileOptions NoBuffering = (FileOptions)0x20000000;
                using var h = File.OpenHandle(fin, FileMode.Open, FileAccess.Read, FileShare.Read, NoBuffering | FileOptions.SequentialScan);
                nuint sz = 4 << 20; void* mem = System.Runtime.InteropServices.NativeMemory.AlignedAlloc(sz, 4096);
                try {
                    var span = new Span<byte>(mem, (int)sz); var hu = new XxHash128(); long off = 0; int n;
                    long len = RandomAccess.GetLength(h);
                    while ((n = RandomAccess.Read(h, span, off)) > 0) { hu.Append(span[..(int)Math.Min(n, len - off)]); off += n; }
                    Console.WriteLine($"   unbuffered (FILE_FLAG_NO_BUFFERING) re-read OK, hashMatch={hu.GetCurrentHash().AsSpan().SequenceEqual(hs.GetCurrentHash())}");
                } finally { System.Runtime.InteropServices.NativeMemory.AlignedFree(mem); }
            }
            try { File.Move(d1, fin, overwrite: false); Console.WriteLine("   Move onto existing: NO EXCEPTION (bad)"); }
            catch (IOException ex) { Console.WriteLine($"   Move onto existing (overwrite:false) -> {ex.GetType().Name} HResult=0x{ex.HResult:X8} (good: never clobbers)"); }
            var di = new DriveInfo(Path.GetPathRoot(dir)!); Console.WriteLine($"   free space on {di.Name}: {di.AvailableFreeSpace/1e9:F1} GB (AvailableFreeSpace honors quotas)");
        } finally { System.IO.Directory.Delete(dir, recursive: true); Console.WriteLine($"   cleaned up: exists={System.IO.Directory.Exists(dir)}"); }
    }

    record LedgerRow(string CardSerial, string SourcePath, long Size, DateTime CaptureUtc, string Hash, string Dest, DateTime OffloadedUtc);
    static void LedgerTest() {
        Console.WriteLine("\n== Ledger JSON Lines test (10,000 rows, scratch file in %TEMP%)");
        var p = Path.Combine(Path.GetTempPath(), "uas-sort-spike-ledger.jsonl");
        try {
            var sw = Stopwatch.StartNew();
            using (var w = new StreamWriter(p)) for (int i = 0; i < 10000; i++)
                w.WriteLine(System.Text.Json.JsonSerializer.Serialize(new LedgerRow("1A2B-3C4D", $"DCIM/DJI_001/DJI_20260927140627_{i:D4}_D.MP4", 123456789 + i, DateTime.UtcNow, "0123456789ABCDEF0123456789ABCDEF", @"C:\x\y.MP4", DateTime.UtcNow)));
            var wms = sw.ElapsedMilliseconds; sw.Restart();
            var rows = File.ReadLines(p).Select(l => System.Text.Json.JsonSerializer.Deserialize<LedgerRow>(l)!).ToDictionary(r => (r.SourcePath, r.Size));
            Console.WriteLine($"   write {wms} ms, read+index {sw.ElapsedMilliseconds} ms, file {new FileInfo(p).Length/1024} KB, rows {rows.Count}");
        } finally { File.Delete(p); Console.WriteLine($"   cleaned up: exists={File.Exists(p)}"); }
    }
}
