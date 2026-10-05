// src/UasSort.App/CompositionRoot.cs — hand-written, no DI container (Ref §2.1). Every ShellDeps factory is here.
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace UasSort.App;

public sealed record AppServices(PlatformServices Platform, ShellVm Shell, ThumbnailCache Thumbnails, DialogService Dialogs,
                                 WinUiDispatcher Ui, string WebView2DataDir, SelfTestSandbox? Sandbox);

public static class CompositionRoot
{
    public static AppServices Build(DispatcherQueue ui, Func<XamlRoot?> xamlRoot, SelfTestSandbox? sandbox)
    {
        var clock = TimeProvider.System;
        var appData = sandbox?.AppDataDir ?? KnownFolders.AppDataDir();
        var platform = PlatformServices.Create(appData, clock);
        if (sandbox is not null) platform.Settings.Save(SelfTestSettings(sandbox));   // before the shell reads settings
        platform.Log.Prune();

        var dispatcher = new WinUiDispatcher(ui, App.ReportFault);
        var dialogs = new DialogService(xamlRoot, ui);
        var wiring = new Wiring(platform, dispatcher, dialogs, selfTest: sandbox is not null);
        var thumbs = new ThumbnailCache(wiring.Thumbnails, ui);
        var webView2 = sandbox?.WebView2Folder ?? Path.Join(appData, "WebView2");
        return new AppServices(platform, wiring.Shell, thumbs, dialogs, dispatcher, webView2, sandbox);
    }

    /// <summary>The selftest's isolated settings (Ref §13): roots in the sandbox, confirmed, the Ref §11 defaults otherwise,
    /// drone clock US Eastern like the fixture's RC 2.</summary>
    public static Settings SelfTestSettings(SelfTestSandbox s) => new(
        Schema: 1, VideoRoot: s.VideoRoot, PhotoRoot: s.PhotoRoot, PreviousPhotoRoots: [],
        RadiusMiles: 50, GapDays: 1, DroneClockMode: StoredClockMode.Zone, DroneClockZone: "America/New_York", CopyJpgTwin: true,
        Map: SettingsDefaults.Map() with { Base = "streets" }, Layout: SettingsDefaults.Layout(), RootsConfirmed: true,
        LightroomFolder: Path.Join(s.Root, "lightroom"));

    /// <summary>The ShellDeps factories (registry, Part 11 item 9) and the state they share: the open card reader and its thumbnails.</summary>
    private sealed class Wiring
    {
        private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly PlatformServices _p;
        private readonly WinUiDispatcher _ui;
        private readonly DialogService _dialogs;
        private readonly bool _selfTest;
        private readonly DeferredPlaceIndex _places;
        private readonly Planner _planner;
        private readonly IFreeSpace _space;
        private readonly IReviewLog _log;
        private readonly string _appVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        private ICardReader? _reader;

        public Wiring(PlatformServices p, WinUiDispatcher ui, DialogService dialogs, bool selfTest)
        {
            _p = p;
            _ui = ui;
            _dialogs = dialogs;
            _selfTest = selfTest;
            _places = new DeferredPlaceIndex(PlaceIndex.LoadAsync(p.Assets));        // background load (Ref §4.2)
            _planner = new Planner(new GeoTimeZoneResolver(), _places, TimeZoneInfo.Local, p.Clock);
            _space = new VolumeFreeSpace(p.Volumes);
            _log = new FileReviewLog(p.Log);
            Shell = new ShellVm(new ShellDeps(
                p.Settings.Load(),
                load => new SetupVm(load, p.Settings, p.LedgerFor, _space),
                CreateCard,
                CreateScan,
                CreateReview,
                CreatePreflight,
                preflight => new CopyVm(preflight, _dialogs, _ui, _log),
                CreateVerdict,
                (origin, offload) => CreateCleanup(offload),
                CreateSettings,
                review => Audit(review, null),
                CardPresent,
                VolumeRefusal,
                p.Settings.Save) { Log = _log, CreatePhotoCleanup = CreatePhotoCleanup, FolderExists = FolderFacts.Exists });
            Shell.Faulted += e => p.Log.Error("scan failed", e);      // already shown on the Card stage (Ref §12 log, keep running)
        }

        public ShellVm Shell { get; }

        public CardThumbnails Thumbnails { get; } = new();

        private Settings Current => Shell.Settings;

        private ICardReader Reader => _reader ?? throw new InvalidOperationException("No card is open");

        // Selftest: the Card stage never enumerates real volumes, so no real card is ever touched (Ref §13 Isolation).
        private CardStageVm CreateCard() => new(
            _selfTest ? NoVolumes.Instance : _p.Volumes,
            volumes => CardDetector.Detect(volumes, _p.Lister, Current),
            (path, detected) => _p.Validator.Validate(path, detected, Current, _p.Lister, _p.PathFacts, _p.AppDataDir));

        private ScanStageVm CreateScan(Settings s) => new(
            (source, progress, ct) => new ScanService(s, _p.Lister, _p.LedgerFor(s.VideoRoot), _p.Volumes, new GeoTimeZoneResolver(),
                                                      TimeZoneInfo.Local, _p.Clock).ScanAsync(source, _p.Readers, progress, ct),
            _planner.Prepare, s, _ui);

        private ReviewVm CreateReview(CardSource source, PlanBase b)
        {
            _reader = _p.Readers.Open(source, IdentityOf(source));
            Thumbnails.Use(_reader, b.Scan.Raw);
            var s = b.Scan.Settings;
            var services = new ReviewServices(_ui, _dialogs, _p.Shell, Thumbnails, _p.Drafts, _space, _p.Clock, _log);
            var decisions = new LedgerDecisionService(_p.LedgerFor(s.VideoRoot), _p.Clock, _p.Machine);
            return new ReviewVm(new PlanSession(b, _planner, new Tuning(s.RadiusMiles, s.GapDays), _p.Clock), services, decisions,
                                DraftOffers.Find(b, _p.Drafts, _planner));
        }

        private PreflightVm CreatePreflight(ReviewVm review)
        {
            var plan = review.Plan;
            var s = plan.Base.Scan.Settings;
            var env = new CommitEnvironment(Reader, _p.Lister, _p.LedgerFor(s.VideoRoot), _p.OffloadLock, _p.Power, Thumbnails,
                                            _p.Reports, _p.Volumes, dirs => _p.FileOpsFor(s, dirs), _p.Clock, _p.Machine, _appVersion);
            return new PreflightVm(plan, plan.Base.Scan.Inventory.Source, p => CommitSession.Begin(p, env),
                                   new CommitPorts(_p.Drafts, _dialogs, _ui));
        }

        private VerdictVm CreateVerdict(ReviewVm review, CommitResult? result)
        {
            var s = review.Plan.Base.Scan.Settings;
            var ports = new VerdictPorts(_p.LedgerFor(s.VideoRoot), _p.Machine, _p.Clock, _dialogs, _p.Shell, _p.Eject,
                                         () => Audit(review, result));
            return new VerdictVm(result?.Verdict ?? Audit(review, null), review.Plan, result?.Offload, result?.ReportPath, ports);
        }

        /// <summary>AuditNow and the verdict's re-audit: relist the card, load the ledger, CardAudit (Ref §10.4).</summary>
        private FormatVerdict Audit(ReviewVm review, CommitResult? result)
        {
            var plan = review.Plan;
            CardIdentity? now;
            ListingResult relisted;
            try
            {
                now = Reader.CurrentIdentity();
                relisted = Reader.Relist();
            }
            catch (IOException)
            {
                now = null;                                     // the card is gone: the audit says NotSafe
                relisted = new ListingResult([], []);
            }
            var ledger = _p.LedgerFor(plan.Base.Scan.Settings.VideoRoot).Load();
            return CardAudit.Audit(plan.Base.Scan.Inventory, relisted, now, plan, result?.Offload, ledger, result?.Batch.Card);
        }

        private CleanupVm CreateCleanup(OffloadResult? offload)
        {
            var review = Shell.Review ?? throw new InvalidOperationException("Cleanup needs a scanned card");
            var source = review.Plan.Base.Scan.Inventory.Source;
            var s = Current;
            var env = new CleanupEnvironment(source, IdentityOf(source), Reader, Thumbnails, _p.Erasers, _p.Lister,
                                             _p.LedgerFor(s.VideoRoot), _p.OffloadLock, _p.Power, _p.Clock, s);
            var engine = new CleanupEngine(
                () => PrepareCleanup(review, offload),
                (confirmed, progress, ct) => CleanupExecutor.RunAsync(confirmed, env, progress, ct),
                Shell.RescanForCleanupAsync,
                _p.Reports.Save,
                _p.Eject);
            return new CleanupVm(engine, _dialogs, _ui, _p.Clock);
        }

        /// <summary>Picture Offload cleanup (spec 2026-10-04): the canonical photo root (so plan paths match the recycler's guard) and the
        /// canonical Lightroom folder (so the index lists, reads and is guarded in one form — a junction, subst or mapped drive would
        /// otherwise make every Lightroom read "outside the Lightroom folder"), the guarded reader and its thumbnails, the drone clock the
        /// scan uses, and the executor's environment.</summary>
        private PhotoCleanupVm CreatePhotoCleanup(Settings settings)
        {
            var s = settings with { LightroomFolder = settings.LightroomFolder is { } lr ? _p.PathFacts.Canonical(lr) : null };
            var root = _p.PathFacts.Canonical(s.PhotoRoot);
            var reader = _p.PhotoReaderFor(s);
            var thumbnails = new PhotoRootThumbnails(reader);
            Thumbnails.PhotoRoot = thumbnails;
            var ledger = _p.LedgerFor(s.VideoRoot);
            var clock = PhotoCaptureClock.For(s, new GeoTimeZoneResolver(), TimeZoneInfo.Local);      // as the Planner's scan resolves stills
            var env = new PhotoCleanupEnvironment(_p.PhotoRecyclers, _p.Lister, ledger, _p.OffloadLock, _p.Power, _p.Clock, _p.Machine);
            var engine = PhotoCleanupEngines.Create(new PhotoCleanupPorts(s, root, _p.Lister, reader, ledger, clock, env, FolderFacts.Exists,
                                                                          _p.Reports, _p.Shell)
            {
                Thumbnails = thumbnails,
            });
            return new PhotoCleanupVm(engine, _dialogs, _ui, _p.Clock);
        }

        /// <summary>Ref §10.6 Preparation steps 1–4: identity, re-list + CardAudit, fresh listings + ledger, Space().</summary>
        private CleanupPreparation PrepareCleanup(ReviewVm review, OffloadResult? offload)
        {
            var plan = review.Plan;
            var inventory = plan.Base.Scan.Inventory;
            var source = inventory.Source;
            var s = Current;
            CardIdentity now;
            ListingResult relisted;
            CardSpace space;
            try
            {
                now = Reader.CurrentIdentity();
                relisted = Reader.Relist();
                space = Reader.Space();
            }
            catch (IOException)
            {
                return new CleanupPreparation(null, $"The card in {Fmt.Drive(source.Root)} can't be read; insert it and rescan", true);
            }
            if (source.Identity is { } pinned && now != pinned)
                return new CleanupPreparation(null, $"A different card is in {Fmt.Drive(source.Root)}; rescan", true);
            var volume = _p.Volumes.GetVolumes().FirstOrDefault(v => PathRules.Equal(v.Root, source.Root));
            if (volume is null)
                return new CleanupPreparation(null, $"{Fmt.Drive(source.Root)} is not available; rescan", true);

            var (ledger, refused) = CleanupLedgerGate.Load(_p.LedgerFor(s.VideoRoot), s);   // Check() first: Blocking up front (Ref §7.5)
            if (refused is not null) return refused;
            var audit = CardAudit.Audit(inventory, relisted, now, plan, offload, ledger!);
            var listings = new LibraryListings(ListRoot(s.VideoRoot, DestRoot.Video, false), ListRoot(s.PhotoRoot, DestRoot.Photo, false),
                                               [.. s.PreviousPhotoRoots.Select(r => ListRoot(r, DestRoot.Photo, true))]);
            return new CleanupPreparation(new CleanupInputs(inventory, plan, audit, offload, space, listings, ledger!, volume, _places, s),
                                          null, false);
        }

        private RootListing ListRoot(string root, DestRoot kind, bool previous)
        {
            var listing = _p.Lister.Enumerate(root, true, ScanService.LibraryExcludes);
            var missing = listing.Errors.Any(e => PathRules.Equal(e.Path, root) && e.Win32Error is 2 or 3);   // not found
            return new RootListing(root, kind, previous, !missing, listing);
        }

        private SettingsPageVm CreateSettings(Settings s) => new(
            s, _p.Settings, _p.LedgerFor, _p.Lister, _p.Shell, _dialogs, _space, _p.Clock, _ui,
            () => CurrentLedger(s.VideoRoot),                                                     // the current (old) root's ledger
            root => LedgerPaths.BackupDir(_p.AppDataDir, _p.PathFacts.Canonical(root)));

        /// <summary>The ledger a video-root [Copy] copies (Ref §9.14): the old root's union; when the old root is gone, its latest
        /// local snapshot ∪ mirror (LedgerStore.LoadFromBackup).</summary>
        private LedgerSnapshot CurrentLedger(string oldRoot)
        {
            var old = _p.LedgerFor(oldRoot);
            if (old.Check().State == LedgerFolderState.VideoRootMissing && old is LedgerStore store) return store.LoadFromBackup();
            return Shell.Review?.Plan.Base.Scan.Ledger ?? old.Load();
        }

        private bool CardPresent(CardSource s) => s.Identity is { } id
            ? _p.Volumes.GetVolumes().Any(v => PathRules.Equal(v.Root, s.Root) && v.Identity == id)
            : _p.Lister.Enumerate(s.Root, false, NoExcludes).Errors.IsEmpty;

        /// <summary>CleanupVolumeCheck on the card's volume (the root's and MISC's children, so the drone index is seen), with the
        /// detail naming the failing rule (Task U4); browsed folders are refused by CleanupAvailability.</summary>
        private (string? Refusal, string? Detail) VolumeRefusal(CardSource s)
        {
            if (s.IsBrowsedFolder) return default;
            var volume = _p.Volumes.GetVolumes().FirstOrDefault(v => PathRules.Equal(v.Root, s.Root));
            return volume is null
                ? (CleanupVolumeCheck.NotACard, $"rule 2: {s.Root} is not a mounted volume root")
                : CleanupVolumeCheck.Evaluate(volume, CleanupVolumeCheck.CardListing(_p.Lister, s.Root), Current, _p.AppDataDir);
        }

        /// <summary>The identity of a detected card, or of the volume holding a browsed folder (as ScanService does).</summary>
        private CardIdentity IdentityOf(CardSource source) =>
            source.Identity
            ?? _p.Volumes.GetVolumes().FirstOrDefault(v => PathRules.Equal(v.Root, Path.GetPathRoot(source.Root) ?? source.Root))?.Identity
            ?? throw new InvalidOperationException("No volume found for " + source.Root);
    }
}
