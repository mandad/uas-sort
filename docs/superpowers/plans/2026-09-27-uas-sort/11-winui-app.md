# Part 11 — WinUI App

**Goal:** build the WinUI 3 application on top of the finished Core, Platform and Review layers: `Program.Main` with single-instance activation, the window chrome (TitleBar, Mica, minimum size in physical pixels, InfoBars), every stage as a `Page` in a `Frame` (Setup, Card, Scan, Review, Preflight, Copy, Verdict, Cleanup, Settings), the Review screen (timeline `ItemsView` with group cards and chip headers, clip list with day-split banners and `Thumb.Key` thumbnails, Photos and Other tabs, tuning sliders, footer and issues flyout, undo/redo, scoped keyboard handling, queued dialogs), the WebView2 + MapLibre map pane (the `.mjs` MIME check first, then the bridge, sync, live R/G and offline canvas), the Commit and Cleanup pages, device-arrival refresh, the hand-written composition root, the XAML lint and the full `--selftest`.

**Ref sections:** §9 in full (§9.1–9.14, §9.6 map pane in full), §10.2 sheet, §10.5 Verdict page, §10.6 Cleanup page and entry points, §2.4 XAML lint, §2.5 App csproj, §2.7 pitfalls 3–7, 10, 11, §4.2 App row, §4.4 Launch, §11 Selftest/WebView2 rows, §12 (UI rows), §13 UI smoke test, §14 step 11. Main spec §6, §7.4, §7.5, §10 (`--selftest` row).

**Depends on:** Parts 01–10 — Part 01 (App project `src/UasSort.App` with `AssemblyName=uas-sort`, `DISABLE_XAML_GENERATED_MAIN`, `MapAssets\**` as Content, `SelfTest\*` embedded as `UasSort.App.SelfTest.<file>`, `Program.Main`, `LaunchOptions`, `SingleInstanceGate`, the probe page, the minimal selftest and its `SelfTestCheck`/`SelfTestResult`/`SelfTestJsonContext`, `tools/fixtures/make-selftest-assets.cs`, and the Platform helpers `NamedMutexLock`, `SingleInstance`, `ForegroundWindow`, `PlaceholderMode`, `SelfTestSandbox`, the optional WSL wrapper `tools/r.sh`); Parts 02–08 (Core model, ports, probes, time/geo incl. `src/UasSort.App/places.bin.gz` from Part 04, library, planning, offload, cleanup); Part 09 (every Platform implementation); Part 10 (every Review view model, `MapBridge`, `CollectionSync`). Part 01's types are extended in place here, never redeclared. Part 13 consumes this part's `--selftest --result <path> [--only …]` contract (defined in Part 13; honoured here through Part 01's `LaunchOptions`). Names, namespaces, signatures and owners follow `00-interfaces.md`, which wins over this part's text.

**Test commands used in this part** (run natively on Windows from the repo root `C:\dev\uas-sort` in PowerShell 7 or Claude Code's Bash tool (Git Bash); only if driving the build from WSL (optional), prefix with `tools/r.sh` as Part 01 defines, then `dotnet build-server shutdown`):

```powershell
dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*XamlLint*"          # XAML lint
dotnet test --project tests\UasSort.Review.Tests -- --filter-class "*LruCache*"             # thumbnail LRU
dotnet build src\UasSort.App -c Debug -r win-x64                                             # x:Bind is compiled: a wrong path fails here
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only <check>[,<check>...]  # Debug build + one selftest run (defined in Task 11.4)
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1                             # every selftest check
```

## Contract C11 — what this part consumes from Parts 01–10

Part 11 binds only the Part 09/10 members listed in `00-interfaces.md` (Review VMs table); the C11 → Part 10 mapping below is applied in every page, control and selftest check of this part. Part 10's view-model members are canonical, including the four members Part 10 adds for this part (`ReviewVm.MapBase`, `ReviewVm.GoTo`, `ReviewVm.MoveTargets`, `ReviewVm.MoveSelectedToGroupAsync`) and the shell additions (`ShellDeps.SaveSettings`, `ShellVm.UpdateLayout`). Compiled `x:Bind` makes any mismatch a build error, so nothing silently diverges. Every other type used here is in the registry (exact names), in the Ref, or marked "(defined here)".

Namespaces: every Core namespace comes from the fixed `GlobalUsings.Core.cs` (Part 02); the App's own `src/UasSort.App/GlobalUsings.cs` (Task 11.4) adds exactly the registry list (`UasSort.App.*`, `UasSort.Platform` and its sub-namespaces, `UasSort.Review`). There is no `UasSort.Core.Ports` namespace and no `UasSort.Review.*` sub-namespace; every Review type is in `UasSort.Review`, so XAML uses one prefix `xmlns:rv="using:UasSort.Review"`.

C11 → Part 10 mapping ("none" = removed from the App):

| C11 item | Part 10 canonical |
|---|---|
| `ReviewDependencies`, `ShellVm(ReviewDependencies)` | `ShellDeps`, `ShellVm(ShellDeps)` (built by `CompositionRoot`, Task 11.4) |
| `ShellVm.Thumbnails` | App `CardThumbnails` (Task 11.4) |
| `ShellStage` | `Stage` |
| `ShellVm.Setup`, `.Scan`, `.Settings` (SettingsPageVm) | `ShellVm.Current` cast to `SetupVm`/`ScanStageVm`/`SettingsPageVm` (`ShellVm.Settings` is the `Settings` record) |
| `ShellVm.Card`, `.Review`, `.Preflight`, `.Copy`, `.Verdict`, `.Cleanup` | same names |
| `ShellVm.CardChipText`, `CanRescan`, `CanOpenSettings`, `RescanCommand` | same names (`RescanCommand` is `IAsyncRelayCommand`) |
| `ShellVm.HasCard` | `CardChipText is not null` (via `UiFormat`) |
| `ShellVm.CanUndo`, `CanRedo`, `UndoCommand`, `RedoCommand` | `ShellVm.CanUndoRedo` + `Review.CanUndo`/`CanRedo`/`UndoCommand`/`RedoCommand` |
| `ShellVm.CanCleanUp`, `CleanUpTooltip`, `OpenCleanupCommand`, `OpenSettingsCommand` | `CleanupEnabled`, `CleanupTooltip`, `CleanupCommand`, `SettingsCommand` |
| `ShellVm.DeviceRefreshEnabled`, `OnDevicesChangedAsync()` | Stage check + `DeviceChanged()` (Task 11.17) |
| `ShellVm.InfoBars` | none (stage pages show their own: `ReviewVm.InfoBars`, `SetupVm.RecoveryText`, `CleanupVm.BlockingText`) |
| `InfoBarVm.Key`, `Message`, `IsClosable` | same |
| `InfoBarVm.Severity` (IssueSeverity) | `InfoSeverity` |
| `InfoBarVm.Title`, `IsOpen` | none (closing calls `ReviewVm.CloseInfoBar(Key)`) |
| `InfoBarVm.ActionLabel`, `HasAction`, `ActionCommand`, `DismissCommand` | `Actions` (`QuickFixVm` list: `Label`, `Command`); dismiss = `CloseInfoBar` |
| `SetupVm.VideoRootFree`, `PhotoRootFree`, `LedgerStatusText` | `VideoFreeText`, `PhotoFreeText`, `LedgerStatus` |
| `SetupVm.VideoRootError`, `PhotoRootError` | none (use `CanConfirm`) |
| `SetupVm.LedgerFolder` | `LedgerPaths.For(VideoRoot)` via `UiFormat` |
| `SetupVm.LedgerBlocking` | `LedgerSeverity == InfoSeverity.Error` |
| `SetupVm.SetVideoRootAsync`, `SetPhotoRootAsync` | `SetVideoRoot`, `SetPhotoRoot` (sync) |
| `SetupVm.ConfirmCommand`, `KeepOnDeviceCommand` | same (`IRelayCommand`); plus `CanKeepOnDevice`, `RecoveryText`, `PhotoInsideVideoNote` |
| `CardStageVm.Volumes` / `VolumeRowVm` | `Rows` / `CardRowVm` (`Text`, `KindText`, `IsDjiCard`, `IsWriteProtected`, `UseCommand`) |
| `VolumeRowVm.Letter`, `Label`, `FileSystem`, `MediaCountText` | composed in `CardRowVm.Text` |
| `CardStageVm.IsEmpty`, `EmptyText`, `RefusalMessage` | `Rows.Count == 0`, `StatusText`, `Message` |
| `CardStageVm.ScanVolumeCommand` | `CardRowVm.UseCommand` |
| `CardStageVm.CanBrowse`, `BrowseResultAsync(path)` | `ShellVm.CanBrowse`, `Browse(string?)` |
| `CardStageVm.RescanCommand` (async) | `RescanCommand` (`IRelayCommand`) |
| `ScanStageVm.PhaseText`, `Progress`, `IsIndeterminate`, `CancelCommand` | same (+ `ErrorText`, `IsRunning`) |
| `ReviewVm.Map` (non-null) | `Map` (`MapBridge?`, set by the App, Task 11.10) |
| `ReviewVm.SelectedTabIndex` | `SelectedTab` |
| `ReviewVm.VideosTabText`, `PhotosTabText`, `OtherTabText` | `Videos.Header`, `Photos.Header`, `Other.Header` |
| `ReviewVm.FooterTotalsText`, `OffloadTooltip` | `FooterText`, `OffloadDisabledReason` |
| `ReviewVm.OffloadCommand` (async) | `OffloadCommand` (`IRelayCommand`) |
| `ReviewVm.TimelineWidth`, `MapHeightRatio`, `UpdateLayout` | `ShellVm.Settings.Layout` + `ShellVm.UpdateLayout` (added) |
| `ReviewVm.MapBaseIndex` | `ReviewVm.MapBase` (added; `"streets"`/`"satellite"`/`"none"`) |
| `VideosTabVm.Timeline` (`TimelineItemVm`) | `Timeline` (`TimelineEntryVm`) |
| `VideosTabVm.SelectedItem` | `SelectedEntry` |
| `VideosTabVm.SelectedClips`, `SetSelectedClips(rows)` | `SelectedClipIds`, `SetSelectedClips(IReadOnlyList<ItemId>)` |
| `VideosTabVm.ClipSelectionChangedByMap` | `ClipSelectionChanged` (`Action<IReadOnlyList<ItemId>>`) |
| `VideosTabVm.MergeWithNextCommand`, `MoveSelectedToNewGroupCommand` | `ReviewVm.MergeSelectedWithNextAsync()`, `ReviewVm.MoveSelectedToNewGroupAsync()` |
| `VideosTabVm.MoveTargets()`, `MoveSelectedToGroupAsync(target)` | `ReviewVm.MoveTargets()`, `ReviewVm.MoveSelectedToGroupAsync(target)` (added) |
| `VideosTabVm.SetSelectedIncludedAsync(bool)` | `ReviewVm.SetIncludedAsync(Videos.SelectedClipIds, bool)` |
| `VideosTabVm.ToggleIncludedAsync(row)`, `SplitBeforeAsync(row)` | `row.ToggleIncludedCommand`, `row.SplitBeforeCommand` |
| `VideosTabVm.OpenInPlayer(row)`, `CardPathOf(row)` | `ReviewVm.OpenClip(row.Id)`, `PathRules.Join(review.Plan.Base.Scan.Inventory.Source.Root, row.Id.CardRelPath.Replace('/', '\\'))` |
| `TimelineItemVm.HasChip`, `ChipText`, `ChipHasAction`, `ChipActionLabel`, `ChipTooltip`, `ChipCommand` | `GroupCardVm.Chip` (`BoundaryChipVm?`): `Chip is not null`, `Text`, `CanMerge`, `ButtonText`, `MergeTooltip`, `MergeCommand` |
| `GroupCardVm.SwatchColor`, `DescriptionIsSuggested`, `BadgeText` | `Swatch`, `DescriptionIsSuggestion`, `Badge` |
| `GroupCardVm.IsDescriptionEditable` | `!IsDescriptionReadOnly` |
| `GroupCardVm.FilterSuggestions(text)` | none (show `Suggestions`, at most 6) |
| `GroupCardVm.CommitRenameAsync(text)` | set `Description`, then `CommitDescriptionCommand` |
| `GroupCardVm.IsAppend`, `AppendHint` | `ReadOnlyHint is not null`, `ReadOnlyHint` |
| `GroupCardVm.HasConfidence`, `ConfidenceText` | `ConfidenceText is not null`, `ConfidenceText` |
| `GroupCardVm.BuildTargetMenu()` / `TargetMenuEntryVm` / `TargetMenuKind` | `RetargetOptions()` / `RetargetOptionVm` / `RetargetKind` (separators added by the App; click → `ReviewVm.RetargetAsync(card, option)`) |
| `GroupCardVm.RetargetToBrowsedFolderAsync(path)`, `VideoRootForPicker` | `ReviewVm.BrowseRetargetAsync(card, path)`, `ReviewVm.Index.VideoRoot` |
| `GroupCardVm.ShowPhotosCommand` | App sets `ReviewVm.SelectedTab = 1` |
| `GroupCardVm.Thumbs` (ObservableCollection), `HasMoreThumbs` | `Thumbs` (`IReadOnlyList<ThumbVm>`, replaced whole), `MoreThumbsText is not null` |
| `GroupCardVm.DateRangeText`, `ZoneBadges`, `LocationText`, `VideoCountsText`, `PhotoCountsText`, `MoreThumbsText`, `Chips`, `Suggestions`, `NewFolderInsteadCommand`, `TargetPath`, `Description` | same |
| `FoldedRunVm.ToggleExpandCommand` | `VideosTabVm.ToggleFold(run)` (+ `Text`, `IsExpanded`, `ClipCount`) |
| `ThumbVm.ThumbKey` | `ThumbVm.Key` |
| `ChipVm.HasAction`, `ActionLabel`, `ActionCommand` | `Actions.Count > 0`, `Actions[0].Label`, `Actions[0].Command` |
| `ClipRowVm.Included`, `CanToggleInclude`, `TimeSourceGlyph`, `HasDayBanner` | `IsIncluded`, `!IsReadOnly`, `TimeGlyph`, `Banner is not null` |
| `DaySplitBannerVm.Emphasised`, `SplitHereCommand` | `IsEmphasised`, `SplitCommand` (+ `EmphasisText`) |
| `TuningVm.GapDays` (double) | `GapDays` (int) |
| `TuningVm.BeginPointerDrag()`, `EndPointerDragAsync()` | `BeginDrag()`, `EndDragAsync()` |
| `IssuesVm.Items` / `IssueRowVm` | `Entries` / `IssueVm` |
| `IssueRowVm.CodeText`, `Fixes`, `GoToCommand` | `Code` (formatted by `UiFormat`), `QuickFixes`, `ReviewVm.GoTo(issue.Anchor)` (added) |
| `PhotosTabVm.ToggleIncludedAsync(tile)` | `tile.ToggleCommand` |
| `PhotoDayVm.Title`, `ReasonText` | `DayText`, `Reason` |
| `PhotoTileVm.Included`, `Name`, `IsConfirmedByYou` | `IsIncluded`, `Text`, `CanUndo` |
| `PhotoTileVm.IsPaired`, `IsSet`, `SetText`, `ClashText` | `PairText is not null`; set/clash text is part of `StatusText` |
| `OtherSectionVm.IsExpanded`, `Rows` (ObservableCollection) | `!IsCollapsed`, `Rows` (`IReadOnlyList<OtherRowVm>`) (+ `Note`) |
| `OtherRowVm.Path`, `CanUndismiss` | `Text`, `UndismissCommand is not null` |
| `MapBridge.Outgoing`, `OnIncoming`, `Start`, `SetTheme`, `SetOnline`, `Ping`, `PongReceived`, `ReadyReceived`, `ClickHandled`, `ContextMenuRequested`, `OpenInBrowserUri()` | ctor `post`, `Dispatch`, `Send(MapProjection.Init(...))`, `Send(new MapSetTheme(...))`, re-send init, `Send(new MapPing(n))`, `ReviewVm.MapStatus`, `ReviewVm.MapStatus`, none, `ReviewVm.MapContextMenuRequested`, App-side (Task 11.8) |
| `PreflightVm.IsChecking` | none (`Open()` is synchronous) |
| `PreflightVm.Blocking`, `Warnings`, `Infos` (`IssueRowVm`) | `IReadOnlyList<string>` |
| `PreflightVm.FoldersToCreate`, `FoldersAppended`, `VolumeLines` (ObservableCollection) | `IReadOnlyList<string>` |
| `PreflightVm.StartCommand` | `ShellVm.StartCopyAsync()` |
| `PreflightVm.Acks`, `CanStart`, `BackCommand`; `AckVm.Text`, `IsChecked` | same |
| `CopyVm.CurrentFileText`, `Progress` | `CurrentText`, `Fraction` |
| `VerdictVm.HasSafeRemovalNote` | `SafeRemovalNote is not null` |
| `VerdictVm.Ejects`, `Groups` (`CommandRowVm`) | `Ejects` (`EjectVm`: `Text`, `EjectCommand`, `ResultText`), `Groups` (`VerdictGroupRowVm`: `Text`, `Path`, `OpenFolderCommand`) |
| `VerdictVm.Categories` (`CategoryVm`) | `CategoryLines` |
| `VerdictVm.RecordAsImportedCommand`, `CanUndo` | `RecordImportedCommand`, `UndoCommand.CanExecute` |
| `VerdictVm.CanCleanUp`, `CleanUpTooltip`, `CleanUpCommand` | `CanCleanup`, `CleanupTooltip`, `CleanupCommand` |
| `VerdictVm.DoneCommand` (async) | `DoneCommand` (`IRelayCommand`) |
| `NotCopiedRowVm.IsDayHeader`, `CanSelect` | `NotCopiedDays` (`NotCopiedDayVm`: `Date`, `Text`, `SelectDayCommand`); rows always selectable via `ToggleCommand` |
| `CleanupStep { Choose, Deleting, Result }` | `CleanupStep { Choose, Review, Confirm, Deleting, Result }` |
| `CleanupVm.ModeIndex`, `FreeSpaceKindIndex`, `FreeSpaceGb` | `Mode` (`CleanupMode`), `FreeKind` (`FreeSpaceKind`), `GbValue` |
| `CleanupVm.CardSpaceText`, `CardLine` | `CardSummary` / `FreeNowText` |
| `CleanupVm.ShowReviewList`, `ReviewRows`, `ScrollToRowRequested` | `Step == CleanupStep.Review`, `Rows`, `FirstUndecided` changes |
| `CleanupVm.CutoffLine`, `CountsLine`, `EvidenceLine`, `NeverCopiedLine`, `HasNeverCopiedLine`, `NeverTouchedLine` | `CutoffText`, `CountsText`, `EvidenceText`, `NeverCopiedText`, `NeverCopiedText is not null`, `NeverTouchedText` |
| `CleanupVm.FlightContinuesLine`, `HasFlightContinuesLine` | part of `CutoffText` (`CleanupTexts.CutoffLine`) |
| `CleanupVm.KeptLines` | `Kept` (`KeptGroupVm`: `Reason`, `Lines`) |
| `CleanupVm.HasShortfall`, `ShortfallOffersInclude` | `ShortfallText is not null`, `CanIncludeNotInLibraryFix` |
| `CleanupVm.AckCantRecover`, `ShowAckNotInLibrary`, `AckNotInLibraryText` | `AckCantBeRecovered`, `ShowNotInLibraryAck`, `NotInLibraryAckText` |
| `CleanupVm.InfoBars` | `BlockingText` + `CanRescan`/`RescanCommand` |
| `CleanupVm.Progress` | indeterminate bar + `ProgressText` |
| `CleanupVm.BackCommand` (async) | `BackCommand` (`IRelayCommand`); other commands same names |
| `CleanupRowVm.WhenText`, `TickedForOffload`/`IsNewInRange`, `DecisionIndex` | `DateText`, `Badge`, `Decision` (`RowDecision`) + `KeepCommand`/`DeleteCommand` |
| `CleanupResultVm.Headline`, `SkippedLines`, `StillListedLines`, `RemovalNote`, `EjectLabel`, `EjectCommand`, `DoneCommand` | `HeadlineText`, `Problems`, `StillListed`, `SafeRemovalText`, `Eject.Text`, `Eject.EjectCommand`, `CleanupVm.DoneCommand` |
| `SettingsPageVm.VideoRootFree`, `PhotoRootFree`, `LedgerStatusText` | `VideoFreeText`, `PhotoFreeText`, `LedgerStatus` |
| `SettingsPageVm.PhotoInsideVideo` | `PathRules.IsStrictlyUnder(PhotoRoot, VideoRoot)` via `UiFormat` |
| `SettingsPageVm.OpenLedgerFolderCommand`, `OpenBackupFolderCommand` | `OpenLedgerCommand`, `OpenBackupCommand` |
| `SettingsPageVm.ShowNoHistoryPrompt`, `NoHistoryText` | `NoHistoryPrompt is not null`, `NoHistoryPrompt` |
| `SettingsPageVm.DefaultRadiusMiles`, `DefaultGapDays` (double) | `RadiusMiles`, `GapDays` (int) |
| `SettingsPageVm.ClockModeIndex`, `ClockFollowsSiteLocal` | `IsSiteLocal` |
| `SettingsPageVm.ZoneIds`, `ClockZoneIndex` | `DroneClock.UsZones` (+ current `ClockZone`), `ClockZone` (string) |
| `SettingsPageVm.MapBaseIndex`, `StreetsStyleUrl`, `StreetsDarkStyleUrl` | `MapBase` (string), `StreetsUrl`, `StreetsDarkUrl` |
| `SettingsPageVm.SatellitePresets` (`PresetVm`) | `SatellitePresets` (dictionary; menu item sets `SatelliteUrl`) |
| `SettingsPageVm.SetVideoRootAsync`, `SetPhotoRootAsync`, `CloseCommand` | `ChangeVideoRootAsync`, `ChangePhotoRoot` (sync), `ShellVm.CloseSettings()` |
| `PreviousRootVm.Path`, `ForgetCommand` | same |
| `CoreJson` | `LedgerJsonContext` / `CoreJsonContext` |
| `PlaceholderGuard.ExposePlaceholders()` | `PlaceholderMode.ExposePlaceholders()` |
| `PlatformServices` (Part 09) | Part 11's own record (Task 11.3) |
| `LedgerSamples` (Part 03/05) | Part 11 Task 11.9 (`UasSort.Testing`) |

Other consumed members (registry signatures): `CardDetector.Detect`, `ICardSourceValidator.Validate`, `ScanService`, `Planner` (`Prepare`, `IPlanDeriver`), `PlanSession`, `DraftOffers.Find`, `LedgerDecisionService`, `ReviewServices`, `CommitSession.Begin`, `CommitEnvironment`, `CommitResult`, `CardAudit.Audit`, `CleanupEnvironment`, `CleanupExecutor.RunAsync`, `CleanupInputs`, `CleanupVolumeCheck`, `ThumbnailReader`, `PlaceIndex.LoadAsync`, `GeoTimeZoneResolver`, `StillProbe.Read`, `LedgerCodec`, `LedgerJsonContext`, `CoreJsonContext`, `SettingsDefaults`; Part 01's `LaunchOptions`, `SingleInstanceGate`, `SelfTestCheck`/`SelfTestResult`/`SelfTestJsonContext`, `SelfTestSandbox`, `ForegroundWindow`, `PlaceholderMode`; Part 03's `SyntheticMp4Builder`/`SyntheticDngBuilder` (only in `tools/fixtures/make-selftest-assets.cs`, Task 11.9).

---

### Task 11.1: XAML lint

**Files:**
- Create: `tests/UasSort.Platform.Tests/XamlLintTests.cs`

**Interfaces:**
- Consumes: nothing from `src/` (reads `src/UasSort.App/**/*.xaml` as text); `RepoPaths` (Part 01, `UasSort.Testing`, global in Platform.Tests).
- Produces: `static class XamlLint { static IReadOnlyList<string> Check(string fileName, string xaml); }` (defined here, test project); test class `XamlLintTests`.

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*XamlLintTests"`
Expected: the `Lint_*` cases pass (the lint is in the test file), and `AppXaml_IsClean` FAILS on the Part 01 spike-style icon, e.g. `src\UasSort.App\MainWindow.xaml:NN: ImageIconSource.ImageSource="Assets/AppIcon.ico" is not ms-appx:///`. If Part 01's XAML already uses `ms-appx:///`, the failing case is instead `Lint_FlagsForbiddenConstructs` before the test file compiles — in that case the red step is the compile error `CS0103: The name 'XamlLint' does not exist` from writing the test class first; write both classes as above and continue.

- [ ] **Step 3: Make it pass**

Fix every reported line in Part 01's XAML: image sources become `ms-appx:///…` (for the title-bar icon: `<ImageIconSource ImageSource="ms-appx:///Assets/AppIcon.ico"/>`). Nothing else changes; Task 11.4 replaces these files anyway, and the lint then guards every later task.

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*XamlLintTests"`
Expected: `Passed! - Failed: 0, Passed: 14` (9 + 4 theory rows + 1 fact).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Platform.Tests/XamlLintTests.cs src/UasSort.App
git commit -m "test: XAML lint over src/UasSort.App (x:Bind only, ms-appx images)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 11.2: Thumbnail LRU, `ThumbnailCache` and the `Thumb.Key` attached property

**Files:**
- Create: `src/UasSort.Review/Services/LruCache.cs`
- Create: `tests/UasSort.Review.Tests/LruCacheTests.cs`
- Create: `src/UasSort.App/Controls/ThumbnailCache.cs`
- Create: `src/UasSort.App/Controls/Thumb.cs`

**Interfaces:**
- Consumes: `IThumbnailSource.GetAsync(ItemId, CancellationToken) → ValueTask<ReadOnlyMemory<byte>>` (Ref §4.1); `ItemId(string CardRelPath)` (Ref §3).
- Produces (defined here):
  - `sealed class LruCache<TKey, TValue> where TKey : notnull` (namespace `UasSort.Review`, like every Review folder) — `LruCache(int capacity)`, `int Count`, `int Capacity`, `bool TryGet(TKey key, out TValue value)` (marks most-recent), `void Set(TKey key, TValue value)` (evicts least-recent beyond capacity), `void Clear()`.
  - `sealed class ThumbnailCache` (namespace `UasSort.App.Controls`) — `const int Capacity = 400`, `const int DecodeWidth = 96`, `ThumbnailCache(IThumbnailSource source, DispatcherQueue ui)`, `Task<ImageSource?> GetAsync(ItemId id)`, `void Clear()`, `int LoadsStarted { get; }`.
  - `static class Thumb` (namespace `UasSort.App.Controls`) — attached property `Key` on `Image`: `static readonly DependencyProperty KeyProperty`, `static ItemId GetKey(Image)`, `static void SetKey(Image, ItemId)`, `static ThumbnailCache? Cache { get; set; }`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/LruCacheTests.cs  (UasSort.Review comes from GlobalUsings.cs, Xunit from the csproj)
namespace UasSort.Review.Tests;

public sealed class LruCacheTests
{
    [Fact]
    public void Set_BeyondCapacity_EvictsLeastRecentlyUsed()
    {
        var c = new LruCache<string, int>(2);
        c.Set("a", 1); c.Set("b", 2);
        Assert.True(c.TryGet("a", out _));   // a becomes most recent
        c.Set("c", 3);                       // evicts b
        Assert.False(c.TryGet("b", out _));
        Assert.True(c.TryGet("a", out var a)); Assert.Equal(1, a);
        Assert.True(c.TryGet("c", out var cc)); Assert.Equal(3, cc);
        Assert.Equal(2, c.Count);
    }

    [Fact]
    public void Set_ExistingKey_ReplacesValueWithoutGrowing()
    {
        var c = new LruCache<string, int>(2);
        c.Set("a", 1); c.Set("a", 5);
        Assert.Equal(1, c.Count);
        Assert.True(c.TryGet("a", out var v)); Assert.Equal(5, v);
    }

    [Fact]
    public void Capacity400_HoldsExactly400()
    {
        var c = new LruCache<int, int>(400);
        for (int i = 0; i < 1000; i++) c.Set(i, i);
        Assert.Equal(400, c.Count);
        Assert.False(c.TryGet(599, out _));
        Assert.True(c.TryGet(600, out _));
        Assert.True(c.TryGet(999, out _));
    }

    [Fact]
    public void Clear_EmptiesTheCache()
    {
        var c = new LruCache<int, int>(3);
        c.Set(1, 1); c.Clear();
        Assert.Equal(0, c.Count);
        Assert.False(c.TryGet(1, out _));
    }

    [Fact]
    public void Ctor_RejectsNonPositiveCapacity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<int, int>(0));
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests\UasSort.Review.Tests -- --filter-class "*LruCacheTests"`
Expected: build error `CS0246: The type or namespace name 'LruCache<,>' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Services/LruCache.cs
namespace UasSort.Review;

/// <summary>Least-recently-used cache (not thread-safe; the thumbnail cache uses it on the UI thread only). Ref §9.5: LRU of 400.</summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();   // First = most recent

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    public int Capacity { get; }
    public int Count => _map.Count;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _map.Remove(key);
        }
        var node = _order.AddFirst((key, value));
        _map[key] = node;
        while (_map.Count > Capacity)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
        }
    }

    public void Clear() { _map.Clear(); _order.Clear(); }
}
```

```csharp
// src/UasSort.App/Controls/ThumbnailCache.cs — LruCache is written fully qualified: the App's GlobalUsings.cs arrives in Task 11.4
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace UasSort.App.Controls;

/// <summary>Ref §9.5: bytes from IThumbnailSource, decoded at 96 px, LRU of 400, one load per key in flight.
/// Empty bytes (IThumbnailSource.Pause during Commit/Cleanup, or no thumbnail) are never cached, so the placeholder shows
/// and the next realisation retries.</summary>
public sealed class ThumbnailCache(IThumbnailSource source, DispatcherQueue ui)
{
    public const int Capacity = 400;
    public const int DecodeWidth = 96;

    private readonly UasSort.Review.LruCache<ItemId, BitmapImage> _cache = new(Capacity);
    private readonly Dictionary<ItemId, Task<ImageSource?>> _inFlight = [];

    public int LoadsStarted { get; private set; }

    /// <summary>Call on the UI thread.</summary>
    public Task<ImageSource?> GetAsync(ItemId id)
    {
        if (_cache.TryGet(id, out var hit)) return Task.FromResult<ImageSource?>(hit);
        if (_inFlight.TryGetValue(id, out var running)) return running;
        var task = LoadAsync(id);
        _inFlight[id] = task;
        return task;
    }

    public void Clear() => _cache.Clear();

    private async Task<ImageSource?> LoadAsync(ItemId id)
    {
        LoadsStarted++;
        try
        {
            var bytes = await Task.Run(async () => await source.GetAsync(id, CancellationToken.None)).ConfigureAwait(true);
            if (bytes.IsEmpty) return null;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes.ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = DecodeWidth, DecodePixelType = DecodePixelType.Logical };
            await image.SetSourceAsync(stream);
            _cache.Set(id, image);
            return image;
        }
#pragma warning disable CA1031 // a bad thumbnail (card gone, corrupt JPEG, decoder error) shows the placeholder; never fatal
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
        finally
        {
            _inFlight.Remove(id);
            System.Diagnostics.Debug.Assert(ui.HasThreadAccess);
        }
    }
}
```

```csharp
// src/UasSort.App/Controls/Thumb.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

/// <summary>Ref §9.5: <c>&lt;Image ctl:Thumb.Key="{x:Bind ThumbKey}"/&gt;</c>. The DP stores the key's string (a WinRT-friendly
/// value, safe under AOT); the load assigns Source only if the element still carries the same key when it finishes, so a
/// recycled container never shows another item's thumbnail.</summary>
public static class Thumb
{
    public static ThumbnailCache? Cache { get; set; }

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(Thumb), new PropertyMetadata(null, OnKeyChanged));

    public static ItemId GetKey(Image element) => new((string?)element.GetValue(KeyProperty) ?? "");

    public static void SetKey(Image element, ItemId value) => element.SetValue(KeyProperty, value.CardRelPath);

    private static async void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;
        image.Source = null;                                   // placeholder (the template's Border background) until loaded
        if (e.NewValue is not string rel || rel.Length == 0 || Cache is null) return;
        var requested = new ItemId(rel);
        var source = await Cache.GetAsync(requested);
        if ((string?)image.GetValue(KeyProperty) == rel)       // key re-check: the container may have been recycled meanwhile
            image.Source = source;
    }
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test --project tests\UasSort.Review.Tests -- --filter-class "*LruCacheTests"` then `dotnet build src\UasSort.App -c Debug -r win-x64`
Expected: `Passed! - Failed: 0, Passed: 5`; the App build succeeds with 0 warnings (the key re-check itself is exercised by the selftest check `thumb.keyRecheck` in Task 11.12).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Services/LruCache.cs tests/UasSort.Review.Tests/LruCacheTests.cs src/UasSort.App/Controls/ThumbnailCache.cs src/UasSort.App/Controls/Thumb.cs
git commit -m "feat: thumbnail LRU (400 at 96 px) and Thumb.Key attached property with key re-check

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.3: Platform helpers for the App — window interop, message hook, selftest sandbox roots, `PlatformServices`

The App must not touch Win32 or the disk itself (Ref §2.3–2.4: Platform is the only code touching disk, Win32 or the shell; the App project has no `AllowUnsafeBlocks`). The window subclass for `WM_DEVICECHANGE`, the selftest's `%TEMP%` sandbox roots and the composition record of every Platform port therefore live in Platform. The foreground calls stay in Part 01's `ForegroundWindow` (`AllowSetForeground`, `BringToFront`); the sandbox is Part 01's `SelfTestSandbox`, extended here in place (it is a `partial` class after the Part 01 edits), never redeclared.

**Files:**
- Create: `src/UasSort.Platform/Win32/WindowInterop.cs`
- Create: `src/UasSort.Platform/Win32/WindowMessageHook.cs`
- Create: `src/UasSort.Platform/Stores/SelfTestSandbox.Roots.cs` (the second part of Part 01's `public sealed partial class SelfTestSandbox`)
- Create: `src/UasSort.Platform/PlatformServices.cs`
- Create: `tests/UasSort.Platform.Tests/Win32/WindowMessageHookTests.cs`
- Create: `tests/UasSort.Platform.Tests/PlatformServicesTests.cs`
- Modify: `tests/UasSort.Platform.Tests/Stores/SelfTestSandboxTests.cs` (Part 01's class `UasSort.Platform.Tests.Stores.SelfTestSandboxTests`: add two tests; no second class of that name)

**Interfaces:**
- Consumes: Part 01 `SelfTestSandbox` (`FolderPrefix`, `Create(string tempRoot)`, `Root`, `WebView2Folder`, `WriteResult(string path, ReadOnlySpan<byte> utf8Json)`, `TryDelete()`, `Dispose()`), `TestTempDir`; Part 09's constructors (registry Platform table): `PathFacts`, `KnownFolders.Pictures()`, `WindowsDirectoryLister`, `WindowsVolumeProvider`, `WindowsCardReaderFactory`, `WindowsCardEraserFactory`, `SettingsStore`, `DraftStore`, `ReportStore`, `AppAssets`, `PowerRequest`, `OffloadLock`, `DeviceEject`, `ShellLauncher`, `LedgerStore`, `GuardedFileOps`, `FileLog`; Part 02's `CardSourceValidator`.
- Produces (defined here):
  - `static partial class WindowInterop` (`UasSort.Platform.Win32`) — `nint Send(nint hwnd, uint msg, nint wParam, nint lParam)`, `nint CreateMessageOnlyWindow()`, `void DestroyWindow(nint hwnd)`; no foreground members.
  - `static class WindowMessageHook` (`UasSort.Platform.Win32`) — `IDisposable Attach(nint hwnd, uint message, Action<nint, nint> handler)`; constants `WM_DEVICECHANGE = 0x0219`, `DBT_DEVICEARRIVAL = 0x8000`, `DBT_DEVICEREMOVECOMPLETE = 0x8004`.
  - `SelfTestSandbox` (`UasSort.Platform.Stores`, Part 01's partial class) gains `static SelfTestSandbox Create()` (= `Create(Path.GetTempPath())`, then creates the four roots), `string AppDataDir` (`Root\appdata`), `string VideoRoot` (`Root\video`), `string PhotoRoot` (`Root\photo`), `string CardRoot` (`Root\card`), `void WriteFile(string relativeToRoot, ReadOnlySpan<byte> content)` (refuses a path that leaves `Root`). The WebView2 folder stays Part 01's `WebView2Folder`; the result file stays Part 01's `WriteResult(string, ReadOnlySpan<byte>)`.
  - `sealed record PlatformServices` (`UasSort.Platform`, registry decision 21) — `PlatformServices(TimeProvider Clock, string AppDataDir, string Machine, IVolumeProvider Volumes, IDirectoryLister Lister, IPathFacts PathFacts, ICardSourceValidator Validator, ICardReaderFactory Readers, ICardEraserFactory Erasers, ISettingsStore Settings, IDraftStore Drafts, IReportStore Reports, IAppAssets Assets, IPowerRequest Power, IOffloadLock OffloadLock, IDeviceEject Eject, IShellLauncher Shell, Func<string, ILedgerStore> LedgerFor, Func<Settings, IReadOnlySet<string>, IFileOps> FileOpsFor, FileLog Log)`; `static PlatformServices Create(string appDataDir, TimeProvider clock)` (machine = `Environment.MachineName`; writes nothing on creation).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Platform.Tests/Win32/WindowMessageHookTests.cs  (UasSort.Platform.Win32 is global in Platform.Tests)
namespace UasSort.Platform.Tests.Win32;

public sealed class WindowMessageHookTests
{
    [Fact]
    public void Attach_ReceivesDeviceChange_AndDisposeStopsIt()
    {
        var hwnd = WindowInterop.CreateMessageOnlyWindow();
        try
        {
            var seen = new List<(nint W, nint L)>();
            var hook = WindowMessageHook.Attach(hwnd, WindowMessageHook.WM_DEVICECHANGE, (w, l) => seen.Add((w, l)));
            WindowInterop.Send(hwnd, WindowMessageHook.WM_DEVICECHANGE, WindowMessageHook.DBT_DEVICEARRIVAL, 0);
            WindowInterop.Send(hwnd, 0x0400 /* WM_USER: another message, ignored */, 0, 0);
            Assert.Equal([(WindowMessageHook.DBT_DEVICEARRIVAL, (nint)0)], seen);

            hook.Dispose();
            WindowInterop.Send(hwnd, WindowMessageHook.WM_DEVICECHANGE, WindowMessageHook.DBT_DEVICEREMOVECOMPLETE, 0);
            Assert.Single(seen);
        }
        finally { WindowInterop.DestroyWindow(hwnd); }
    }
}
```

Add these two tests inside Part 01's `SelfTestSandboxTests` class in `tests/UasSort.Platform.Tests/Stores/SelfTestSandboxTests.cs` (its existing tests stay as they are):

```csharp
    [Fact]
    public void Create_WithoutArgument_MakesTheFourRootsUnderTemp()
    {
        using var sandbox = SelfTestSandbox.Create();
        Assert.StartsWith(Path.Join(Path.GetTempPath(), SelfTestSandbox.FolderPrefix), sandbox.Root, StringComparison.OrdinalIgnoreCase);
        foreach (var dir in new[] { sandbox.AppDataDir, sandbox.VideoRoot, sandbox.PhotoRoot, sandbox.CardRoot })
        {
            Assert.True(Directory.Exists(dir), dir);
            Assert.Equal(sandbox.Root, Path.GetDirectoryName(dir));
        }
    }

    [Fact]
    public void WriteFile_WritesUnderTheRoot_AndRefusesAPathThatLeavesIt()
    {
        using var temp = new TestTempDir();
        using var sandbox = SelfTestSandbox.Create(temp.FullPath);
        sandbox.WriteFile(@"card\DCIM\DJI_001\a.bin", "abc"u8);
        Assert.Equal("abc", File.ReadAllText(Path.Join(sandbox.Root, @"card\DCIM\DJI_001\a.bin")));
        Assert.Throws<InvalidOperationException>(() => sandbox.WriteFile(@"..\escape.bin", "x"u8));
        Assert.False(File.Exists(Path.Join(temp.FullPath, "escape.bin")));
    }
```

```csharp
// tests/UasSort.Platform.Tests/PlatformServicesTests.cs
namespace UasSort.Platform.Tests;

public sealed class PlatformServicesTests
{
    [Fact]
    public void Create_ComposesThePlatformClasses_AndWritesNothing()
    {
        using var temp = new TestTempDir();
        var appData = temp.Combine("appdata");                  // does not exist yet
        var p = PlatformServices.Create(appData, TimeProvider.System);

        Assert.Equal(Environment.MachineName, p.Machine);
        Assert.Equal(appData, p.AppDataDir);
        Assert.IsType<WindowsVolumeProvider>(p.Volumes);
        Assert.IsType<WindowsDirectoryLister>(p.Lister);
        Assert.IsType<SettingsStore>(p.Settings);
        var ledger = Assert.IsType<LedgerStore>(p.LedgerFor(temp.Combine("video")));
        Assert.EndsWith(@"\.uas-sort", ledger.Folder, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<GuardedFileOps>(p.FileOpsFor(SettingsDefaults.Derive(temp.FullPath), new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        Assert.False(Directory.Exists(appData));                // no settings, drafts, reports or logs folder was created
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*WindowMessageHookTests" --filter-class "*SelfTestSandboxTests" --filter-class "*PlatformServicesTests"`
Expected: build errors `CS0103: The name 'WindowInterop' does not exist in the current context`, `CS0117: 'SelfTestSandbox' does not contain a definition for 'AppDataDir'` (and `Create()` without arguments, `WriteFile`), `CS0246: The type or namespace name 'PlatformServices' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Platform/Win32/WindowInterop.cs
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

/// <summary>Window-level Win32 calls the App needs besides the foreground ones (those are Part 01's ForegroundWindow).</summary>
public static partial class WindowInterop
{
    private static readonly nint HWND_MESSAGE = -3;

    public static nint Send(nint hwnd, uint msg, nint wParam, nint lParam) => SendMessageW(hwnd, msg, wParam, lParam);

    /// <summary>A message-only STATIC window on the calling thread (tests and the selftest's device-hook check).</summary>
    public static nint CreateMessageOnlyWindow()
    {
        var h = CreateWindowExW(0, "STATIC", "uas-sort-msg", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
        if (h == 0) throw new InvalidOperationException("CreateWindowExW failed: " + Marshal.GetLastPInvokeError());
        return h;
    }

    public static void DestroyWindow(nint hwnd) => DestroyWindowNative(hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
                                                int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindowNative(nint hWnd);
}
```

```csharp
// src/UasSort.Platform/Win32/WindowMessageHook.cs
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

/// <summary>comctl32 window subclassing (SetWindowSubclass) so the App can watch one message on its top-level window
/// (Ref §14 step 11: device-arrival refresh via WM_DEVICECHANGE; UNVERIFIED). The callback is an UnmanagedCallersOnly
/// function pointer, so this is AOT-safe.</summary>
public static unsafe partial class WindowMessageHook
{
    public const uint WM_DEVICECHANGE = 0x0219;
    public const nint DBT_DEVICEARRIVAL = 0x8000;
    public const nint DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const uint WM_NCDESTROY = 0x0082;

    private static readonly ConcurrentDictionary<nuint, (nint Hwnd, uint Message, Action<nint, nint> Handler)> Hooks = new();
    private static long _nextId;

    public static IDisposable Attach(nint hwnd, uint message, Action<nint, nint> handler)
    {
        var id = (nuint)Interlocked.Increment(ref _nextId);
        Hooks[id] = (hwnd, message, handler);
        if (!SetWindowSubclass(hwnd, &SubclassProc, id, 0))
        {
            Hooks.TryRemove(id, out _);
            throw new InvalidOperationException("SetWindowSubclass failed");
        }
        return new Subscription(hwnd, id);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (Hooks.TryGetValue(id, out var h))
        {
            if (msg == h.Message)
            {
#pragma warning disable CA1031 // a handler bug must not tear down the window proc (unmanaged callback)
                try { h.Handler(wParam, lParam); } catch (Exception) { }
#pragma warning restore CA1031
            }
            else if (msg == WM_NCDESTROY)
            {
                RemoveWindowSubclass(hwnd, &SubclassProc, id);
                Hooks.TryRemove(id, out _);
            }
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private sealed class Subscription(nint hwnd, nuint id) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            RemoveWindowSubclass(hwnd, &SubclassProc, id);
            Hooks.TryRemove(id, out _);
        }
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hWnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> pfn,
                                                  nuint uIdSubclass, nuint dwRefData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint hWnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> pfn,
                                                     nuint uIdSubclass);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
}
```

```csharp
// src/UasSort.Platform/Stores/SelfTestSandbox.Roots.cs — Part 11's part of Part 01's partial SelfTestSandbox (Task 01.9)
namespace UasSort.Platform.Stores;

/// <summary>Ref §11 Selftest row / §13: the sandbox also holds the selftest's settings and app data, a temp video root whose
/// .uas-sort\ holds the test ledger, a photo root and the synthetic card (Part 01 already holds WebView2Folder there).</summary>
public sealed partial class SelfTestSandbox
{
    /// <summary>Create(Path.GetTempPath()), then the four roots of the full selftest.</summary>
    public static SelfTestSandbox Create()
    {
        var sandbox = Create(Path.GetTempPath());
#pragma warning disable RS0030 // IO layer: selftest sandbox, only under %TEMP%\uas-sort-selftest-<guid>
        foreach (var dir in new[] { sandbox.AppDataDir, sandbox.VideoRoot, sandbox.PhotoRoot, sandbox.CardRoot })
            Directory.CreateDirectory(dir);
#pragma warning restore RS0030
        return sandbox;
    }

    /// <summary>The App's app-data folder under --selftest (settings, drafts, reports, logs).</summary>
    public string AppDataDir => Path.Join(Root, "appdata");

    public string VideoRoot => Path.Join(Root, "video");

    public string PhotoRoot => Path.Join(Root, "photo");

    public string CardRoot => Path.Join(Root, "card");

    /// <summary>Writes one file below Root (creating its folders); a path that leaves Root throws before any IO.</summary>
    public void WriteFile(string relativeToRoot, ReadOnlySpan<byte> content)
    {
        var full = Path.GetFullPath(Path.Join(Root, relativeToRoot));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selftest write outside the sandbox: " + relativeToRoot);
#pragma warning disable RS0030 // IO layer: selftest sandbox, only under %TEMP%\uas-sort-selftest-<guid>
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
#pragma warning restore RS0030
    }
}
```

```csharp
// src/UasSort.Platform/PlatformServices.cs — composition record of every Platform port (registry decision 21; owner Part 11)
using System.Reflection;

namespace UasSort.Platform;

/// <summary>
/// Every Platform implementation the App composes (Ref §4.1). Creating it writes nothing: the stores, the ledger and the log
/// touch the disk only when used, so the selftest's placeholderVisibility check can create one over the real app-data folder.
/// Inside this record the property `Settings` (the store) hides the Core type of the same name, so the type is written
/// fully qualified.
/// </summary>
public sealed record PlatformServices(
    TimeProvider Clock, string AppDataDir, string Machine,
    IVolumeProvider Volumes, IDirectoryLister Lister, IPathFacts PathFacts, ICardSourceValidator Validator,
    ICardReaderFactory Readers, ICardEraserFactory Erasers, ISettingsStore Settings, IDraftStore Drafts, IReportStore Reports,
    IAppAssets Assets, IPowerRequest Power, IOffloadLock OffloadLock, IDeviceEject Eject, IShellLauncher Shell,
    Func<string, ILedgerStore> LedgerFor, Func<UasSort.Core.Settings, IReadOnlySet<string>, IFileOps> FileOpsFor, FileLog Log)
{
    public static PlatformServices Create(string appDataDir, TimeProvider clock)
    {
        var machine = Environment.MachineName;                  // decision 32: passed explicitly from here
        var facts = new PathFacts();
        var lister = new WindowsDirectoryLister();
        var settings = new SettingsStore(appDataDir, KnownFolders.Pictures(), machine, facts, lister, clock);

        // The guarded Platform classes take Settings at construction. These read the saved settings (read-only, never
        // writing) each time they are used, so a root changed in Setup or Settings is what the next card read, card erase
        // or ledger open is guarded with.
        UasSort.Core.Settings Current() => settings.Load(readOnly: true).Settings;

        return new PlatformServices(
            clock, appDataDir, machine,
            new WindowsVolumeProvider(), lister, facts, new CardSourceValidator(),
            new CurrentReaders(() => new WindowsCardReaderFactory(Current(), appDataDir, machine, facts, lister)),
            new CurrentErasers(() => new WindowsCardEraserFactory(Current(), appDataDir, machine, facts, lister)),
            settings,
            new DraftStore(appDataDir, machine, facts),
            new ReportStore(appDataDir, machine, facts, clock),
            new AppAssets(AppContext.BaseDirectory, Assembly.GetEntryAssembly() ?? typeof(PlatformServices).Assembly),
            new PowerRequest(),
            new OffloadLock(),
            new DeviceEject(),
            new ShellLauncher(),
            videoRoot => new LedgerStore(Current() with { VideoRoot = videoRoot }, appDataDir, machine, facts, lister, clock),
            (s, newFolderDirs) => new GuardedFileOps(s, appDataDir, machine, facts, newFolderDirs),
            new FileLog(appDataDir, machine, facts, clock));
    }

    private sealed class CurrentReaders(Func<ICardReaderFactory> create) : ICardReaderFactory
    {
        public ICardReader Open(CardSource source, CardIdentity identity) => create().Open(source, identity);
    }

    private sealed class CurrentErasers(Func<ICardEraserFactory> create) : ICardEraserFactory
    {
        public ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan) => create().Open(source, pinned, plan);
    }
}
```

- [ ] **Step 4: Run them and watch them pass**

Run: `dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*WindowMessageHookTests" --filter-class "*SelfTestSandboxTests" --filter-class "*PlatformServicesTests"`
Expected: `Passed! - Failed: 0, Passed: 10` (1 hook test, Part 01's 6 sandbox cases + the 2 added, 1 composition test).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform/Win32/WindowInterop.cs src/UasSort.Platform/Win32/WindowMessageHook.cs src/UasSort.Platform/Stores/SelfTestSandbox.Roots.cs src/UasSort.Platform/PlatformServices.cs tests/UasSort.Platform.Tests/Win32/WindowMessageHookTests.cs tests/UasSort.Platform.Tests/Stores/SelfTestSandboxTests.cs tests/UasSort.Platform.Tests/PlatformServicesTests.cs
git commit -m "feat: platform window interop, WM_DEVICECHANGE hook, selftest sandbox roots and PlatformServices

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.4: Shell — `App`, composition root, window chrome, stage frame and the selftest runner

`Program.Main` stays Part 01's (Task 01.10, after the Part 01 edits): process start time → `WinRT.ComWrappersSupport.InitializeComWrappers()` → `PlaceholderMode.ExposePlaceholders()` → `LaunchOptions.Parse(args, processStartUtc)` → `SingleInstanceGate.Claim(options.ForceMutex)` unless `--selftest` → `Application.Start(_ => new App(options, gate))`. `LaunchOptions` already parses the Part 13 contract `--selftest --result <path> [--only a,b]` and `--single-instance-mutex`; the selftest records `SelfTestCheck`/`SelfTestResult`/`SelfTestJsonContext` already have the Part 13 shape. This task reuses those files unchanged, extends `SingleInstanceGate` in place, and replaces only the App's window, application class and the probe page.

**Files:**
- Create: `tools/selftest.ps1`
- Create: `src/UasSort.App/GlobalUsings.cs`, `src/UasSort.App/Namespaces.cs`
- Modify: `src/UasSort.App/UasSort.App.csproj` (`places.bin.gz` as Content), `src/UasSort.App/SingleInstanceGate.cs` (Part 01's class: add `event Action? Activated`)
- Reuse unchanged (Part 01): `src/UasSort.App/Program.cs`, `src/UasSort.App/LaunchOptions.cs`, `src/UasSort.App/SelfTest/SelfTestResult.cs` (`SelfTestCheck`, `SelfTestResult`, `SelfTestJsonContext`)
- Replace: `src/UasSort.App/App.xaml`, `src/UasSort.App/App.xaml.cs`, `src/UasSort.App/MainWindow.xaml`, `src/UasSort.App/MainWindow.xaml.cs`
- Create: `src/UasSort.App/CompositionRoot.cs`
- Create: `src/UasSort.App/Services/WinUiDispatcher.cs`, `Services/DialogService.cs`, `Services/UiFormat.cs`, `Services/VisualTree.cs`, `Services/DeferredPlaceIndex.cs`, `Services/CardThumbnails.cs`, `Services/PlatformAdapters.cs`
- Create: `src/UasSort.App/Pages/ShellPage.xaml`, `Pages/ShellPage.xaml.cs`
- Create: `src/UasSort.App/SelfTest/SelfTestRunner.cs`, `SelfTest/SelfTestChecks.cs`
- Delete (`git rm`): `src/UasSort.App/SelfTest/MinimalSelfTest.cs`, `src/UasSort.App/Pages/StackProbePage.xaml`, `Pages/StackProbePage.xaml.cs`, `Pages/StackProbeVm.cs` (the probe VMs), `Pages/ProbeTemplateSelector.cs`. Their checks (virtual host, JSON round-trip, GeoTimeZone, MetadataExtractor) are re-implemented below and in Tasks 11.7 and 11.9. `MapAssets/probe.html` stays until Task 11.8 removes it.

**Interfaces:**
- Consumes: `PlatformServices.Create` (Task 11.3); Part 01's `LaunchOptions` (`SelfTest`, `ResultPath`, `Only`, `ForceMutex`, `ProcessStartUtc`), `SingleInstanceGate` (`Claim`, `IsMain`, `Mechanism`, `Key`), `SelfTestCheck.Pass/Fail/NotApplicable`, `SelfTestResult`, `SelfTestJsonContext`, `SelfTestSandbox` (`Create()`, `WebView2Folder`, `AppDataDir`, `VideoRoot`, `PhotoRoot`, `WriteResult(string, ReadOnlySpan<byte>)`, `Dispose`), `ForegroundWindow.BringToFront`; `ShellDeps`, `ShellVm` and every stage VM constructor (registry Review VMs table); `CardDetector.Detect`, `ICardSourceValidator.Validate`, `ScanService`, `Planner`, `PlanSession`, `DraftOffers.Find`, `LedgerDecisionService`, `ReviewServices`, `IFreeSpace`, `IReviewLog`, `CommitSession.Begin`, `CommitEnvironment`, `CommitPorts`, `CommitResult`, `VerdictPorts`, `CardAudit.Audit`, `CleanupEnvironment`, `CleanupExecutor.RunAsync`, `CleanupEngine`, `CleanupPreparation`, `CleanupInputs`, `CleanupVolumeCheck`, `ThumbnailReader`, `PlaceIndex.LoadAsync`, `GeoTimeZoneResolver`, `CoreJsonContext`, `SettingsDefaults`, `LedgerPaths`, `PathRules`, `KnownFolders.AppDataDir()`; `ThumbnailCache`, `Thumb` (Task 11.2).
- Produces (defined here):
  - `SingleInstanceGate` (Part 01's class, same file) gains `event Action? Activated` (raised on a thread-pool thread in the main instance when a second launch redirects to it; the redirect itself stays Part 01's worker-thread `RedirectActivationToAsync`).
  - `sealed record AppServices(PlatformServices Platform, ShellVm Shell, ThumbnailCache Thumbnails, DialogService Dialogs, WinUiDispatcher Ui, string WebView2DataDir, SelfTestSandbox? Sandbox)`; `static class CompositionRoot { static AppServices Build(DispatcherQueue ui, Func<XamlRoot?> xamlRoot, SelfTestSandbox? sandbox); static Settings SelfTestSettings(SelfTestSandbox s); }` (builds `ShellDeps`, `CommitEnvironment`, `CleanupEnvironment`; a `NoVolumes` provider for the Card stage under selftest).
  - `sealed class CardThumbnails : IThumbnailSource` (`UasSort.App.Services`) — `void Use(ICardReader reader, IEnumerable<RawItem> items)` (disposes the previous `ThumbnailReader`); the one `IThumbnailSource` given to `ReviewServices.Thumbs`, `CommitEnvironment.Thumbnails`, `CleanupEnvironment.Thumbnails` and the `ThumbnailCache`.
  - `sealed class WinUiDispatcher : IUiDispatcher`; `sealed class DialogService : IDialogService` (queued, one `ContentDialog` at a time); `static class UiFormat` (x:Bind helper functions); `static class VisualTree` (`FindDescendant<T>`, `FindAll<T>`, `FindAncestor<T>`); `sealed class DeferredPlaceIndex : IPlaceIndex` (over `PlaceIndex.LoadAsync`); `VolumeFreeSpace : IFreeSpace`, `FileReviewLog : IReviewLog`, `NoVolumes : IVolumeProvider` (App-only adapters).
  - `MainWindow` (Part 01's class, replaced) keeps `internal Task<double> FirstFrameMs`, `internal void ShowOffScreen()`, `internal void BringToFront()` and adds `AppServices Services`, `nint Hwnd`, `ShellPage? Shell`, `void Start()`, `void ApplyMinimumSize()`, constants `MinWidth = 1100`, `MinHeight = 700` (`DeviceWatcher` follows in Task 11.17); `ProbePage` is gone with the probe page. `App` keeps `internal static MainWindow? MainWindow` and `internal static string WebView2Folder`.
  - `sealed partial class ShellPage : Page` — `Frame StageFrame`, `TitleBar AppTitleBar`, `static readonly Dictionary<Stage, Type> StagePages`, `ShellVm Vm`, `MainWindow Window`; navigates the stage frame whenever `ShellVm.Current` changes; `sealed record StageArgs(object Vm, MainWindow Window)`.
  - Selftest (namespace `UasSort.App.SelfTest`, `internal` like Part 01's records): `sealed class SelfTestContext(MainWindow window, LaunchOptions options)`; `static class SelfTestRunner { static Task RunAsync(SelfTestContext ctx); }`; `static partial class SelfTestChecks { static readonly List<(string Name, Func<SelfTestContext, Task<SelfTestCheck>> Run)> All; }` with checks `shell.render`, `window.minSize`, `probe.timeZone`, `json.planEdit`, `placeholderVisibility`.
  - `tools/selftest.ps1 [-Only a,b] [-Configuration Debug] [-TimeoutSec 60]` — builds the App, runs `uas-sort.exe --selftest --result <tmp> [--only …]`, prints the result JSON and `exit code N`, exits with that code.
  - Command line (Part 13's contract, parsed by Part 01's `LaunchOptions`): `uas-sort.exe --selftest --result <path> [--only name,name]`; result `{ok, firstFrameMs, checks[{name,status,detail}]}` from Part 01's records.

- [ ] **Step 1: Write the check harness first (the failing check)**

```powershell
# tools/selftest.ps1 — build the App and run one --selftest (Ref §13). Used by Part 11 tasks as their UI test step.
param(
    [string]$Only = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$TimeoutSec = 60
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$rid = 'win-x64'   # x64 only (user decision 2026-09-28)
& dotnet build (Join-Path $repo 'src\UasSort.App\UasSort.App.csproj') -c $Configuration -r $rid -tl:off | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit $LASTEXITCODE }

$exe = Get-ChildItem (Join-Path $repo 'src\UasSort.App\bin') -Recurse -Filter 'uas-sort.exe' |
       Where-Object { $_.FullName -like "*\$Configuration\*" -and $_.FullName -like "*\$rid\*" } |
       Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $exe) { Write-Host "uas-sort.exe not found under src\UasSort.App\bin"; exit 2 }

$result = Join-Path ([IO.Path]::GetTempPath()) ("uas-sort-selftest-result-{0}.json" -f [guid]::NewGuid().ToString('N'))
$psi = [Diagnostics.ProcessStartInfo]::new($exe.FullName)
$psi.UseShellExecute = $false
foreach ($a in @('--selftest', '--result', $result)) { $psi.ArgumentList.Add($a) }
if ($Only) { $psi.ArgumentList.Add('--only'); $psi.ArgumentList.Add($Only) }
$p = [Diagnostics.Process]::Start($psi)
if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    & taskkill.exe /T /F /PID $p.Id | Out-Null
    Write-Host "TIMEOUT after $TimeoutSec s"
    exit 124
}
if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result -Raw | Write-Host; Remove-Item -LiteralPath $result }
else { Write-Host "NO RESULT FILE" }
Write-Host "exit code $($p.ExitCode)"
exit $p.ExitCode
```

- [ ] **Step 2: Run it and watch it fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only shell.render,window.minSize,probe.timeZone,json.planEdit,placeholderVisibility`
Expected: the build succeeds and Part 01's minimal selftest answers, but its result lists none of the five requested checks (`"checks":[]`: it runs only its own stack-proof checks and skips every name `--only` asks for that it doesn't know). That is the red state: none of `shell.render`, `window.minSize`, `probe.timeZone`, `json.planEdit`, `placeholderVisibility` exists yet.

- [ ] **Step 3: Implement the shell**

```csharp
// src/UasSort.App/GlobalUsings.cs — exactly the registry list (00-interfaces.md); Core namespaces come from GlobalUsings.Core.cs
global using System.Collections.ObjectModel;
global using UasSort.App.Controls;
global using UasSort.App.MapHost;
global using UasSort.App.Pages;
global using UasSort.App.SelfTest;
global using UasSort.App.Services;
global using UasSort.Platform;
global using UasSort.Platform.Card;
global using UasSort.Platform.Io;
global using UasSort.Platform.Ledger;
global using UasSort.Platform.Logging;
global using UasSort.Platform.Shell;
global using UasSort.Platform.Stores;
global using UasSort.Platform.Win32;
global using UasSort.Review;
```

```csharp
// src/UasSort.App/Namespaces.cs — anchors UasSort.App.MapHost (named in GlobalUsings.cs) until MapPane arrives in Task 11.7
namespace UasSort.App.MapHost { internal static class NamespaceMarker { } }
```

In `src/UasSort.App/UasSort.App.csproj`, add to the `ItemGroup` that holds Part 01's `MapAssets\**` Content item (registry decision 35; Part 04 produced the file, Part 01 did not add it):

```xml
    <Content Include="places.bin.gz" CopyToOutputDirectory="PreserveNewest" />
```

In Part 01's `src/UasSort.App/SingleInstanceGate.cs`, add the event and raise it from the private constructor (everything else in the file — `Claim`, the `Local\uas-sort` fallback through `NamedMutexLock`, `ForegroundWindow.AllowSetForeground` and the redirect on a worker thread — stays as Part 01 wrote it):

```csharp
    /// <summary>Raised on a thread-pool thread in the main instance when a second launch redirects its activation here.</summary>
    public event Action? Activated;
```

```csharp
    private SingleInstanceGate(bool isMain, string mechanism, AppInstance? key, NamedMutexLock? mutex)
    {
        IsMain = isMain;
        Mechanism = mechanism;
        Key = key;
        _mutex = mutex;
        if (isMain && key is not null)
        {
            key.Activated += (_, _) => Activated?.Invoke();
        }
    }
```

```xml
<!-- src/UasSort.App/App.xaml -->
<?xml version="1.0" encoding="utf-8"?>
<Application
    x:Class="UasSort.App.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
            <Style x:Key="ChipBorder" TargetType="Border">
                <Setter Property="CornerRadius" Value="10" />
                <Setter Property="Padding" Value="8,2" />
                <Setter Property="Background" Value="{ThemeResource SubtleFillColorSecondaryBrush}" />
            </Style>
            <!-- Defined here so the title-bar icon buttons don't depend on a style name the WinUI version may not ship -->
            <Style x:Key="SubtleButtonStyle" TargetType="Button" BasedOn="{StaticResource DefaultButtonStyle}">
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="BorderThickness" Value="0" />
                <Setter Property="Padding" Value="8,6" />
            </Style>
            <Style x:Key="CaptionText" TargetType="TextBlock" BasedOn="{StaticResource CaptionTextBlockStyle}">
                <Setter Property="Foreground" Value="{ThemeResource TextFillColorSecondaryBrush}" />
                <Setter Property="TextTrimming" Value="CharacterEllipsis" />
            </Style>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

```csharp
// src/UasSort.App/App.xaml.cs — replaces Part 01's; the constructor signature App(LaunchOptions, SingleInstanceGate?) is Part 01's
using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace UasSort.App;

public partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly SingleInstanceGate? _gate;
    private readonly SelfTestSandbox? _sandbox;
    private MainWindow? _window;

    internal App(LaunchOptions options, SingleInstanceGate? gate)
    {
        _options = options;
        _gate = gate;
        _sandbox = options.SelfTest ? SelfTestSandbox.Create() : null;   // %TEMP%\uas-sort-selftest-<guid>\ with its four roots
        WebView2Folder = _sandbox?.WebView2Folder ?? Path.Join(KnownFolders.AppDataDir(), "WebView2");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _window?.Services.Platform.Log.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    internal static MainWindow? MainWindow { get; private set; }

    internal static string WebView2Folder { get; private set; } = "";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(_options, _gate?.Mechanism ?? "skipped (selftest)", _sandbox);
        MainWindow = window;
        _window = window;
        if (_gate is not null)
        {
            _gate.Activated += () => window.DispatcherQueue.TryEnqueue(window.BringToFront);
        }

        window.Start();
    }

    // Ref §12: log, keep running, tell the user; drafts survive and copied files are in the ledger.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Trace.WriteLine("UNHANDLED " + e.Exception);
        _window?.Services.Platform.Log.Error("unhandled exception", e.Exception);
        if (_options.SelfTest || _window is null) return;           // the selftest records it as a failed check instead
        e.Handled = true;
        _ = _window.Services.Dialogs.ShowAsync(new DialogRequest(
            "Something went wrong",
            "uas-sort hit an unexpected error: " + e.Message + "\n\nYour edits are kept as a draft, and every file this run " +
            "copied is recorded in the history. Restart uas-sort to continue.",
            "OK", null, "Close"));
    }
}
```

```xml
<!-- src/UasSort.App/MainWindow.xaml — the window only hosts a Frame (Ref §2.7 #3); the TitleBar lives in ShellPage -->
<?xml version="1.0" encoding="utf-8"?>
<Window
    x:Class="UasSort.App.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="uas-sort">
    <Frame x:Name="RootFrame" />
</Window>
```

```csharp
// src/UasSort.App/MainWindow.xaml.cs — Ref §9.2 chrome; FirstFrameMs, ShowOffScreen and BringToFront are Part 01's
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace UasSort.App;

public sealed partial class MainWindow : Window
{
    public const int MinWidth = 1100, MinHeight = 700;
    private readonly LaunchOptions _options;
    private readonly TaskCompletionSource<double> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _sized;

    internal MainWindow(LaunchOptions options, string singleInstanceMechanism, SelfTestSandbox? sandbox)
    {
        _options = options;
        InitializeComponent();
        Title = "uas-sort";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(Path.Join(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        Services = CompositionRoot.Build(DispatcherQueue, () => RootFrame.XamlRoot, sandbox);
        Services.Platform.Log.Info("start; single instance: " + singleInstanceMechanism);
        Thumb.Cache = Services.Thumbnails;
        CompositionTarget.Rendering += OnFirstRendering;
        RootFrame.Loaded += (_, _) => { ApplyMinimumSize(); RootFrame.XamlRoot.Changed += (_, _) => ApplyMinimumSize(); };
        RootFrame.Navigate(typeof(ShellPage), this);
        Closed += (_, _) => Services.Sandbox?.Dispose();
    }

    public AppServices Services { get; }
    public ShellPage? Shell => RootFrame.Content as ShellPage;
    public nint Hwnd => Win32Interop.GetWindowFromWindowId(AppWindow.Id);

    /// <summary>Milliseconds from process start to the first CompositionTarget.Rendering (Ref §13 firstFrameMs).</summary>
    internal Task<double> FirstFrameMs => _firstFrame.Task;

    public void Start()
    {
        if (_options.SelfTest)
        {
            ShowOffScreen();
            _ = SelfTestRunner.RunAsync(new SelfTestContext(this, _options));
        }
        else
        {
            Activate();
        }
        _ = Services.Shell.StartAsync();
    }

    /// <summary>--selftest launches off-screen and never activates the window (Ref §13 Isolation).</summary>
    internal void ShowOffScreen()
    {
        AppWindow.Move(new PointInt32(-6000, -6000));
        AppWindow.Show(false);
    }

    internal void BringToFront() => ForegroundWindow.BringToFront(Win32Interop.GetWindowFromWindowId(AppWindow.Id));

    /// <summary>Ref §2.7 #5: AppWindow sizes are physical pixels, so scale by the XAML rasterization scale.</summary>
    public void ApplyMinimumSize()
    {
        var scale = RootFrame.XamlRoot?.RasterizationScale ?? 1.0;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)Math.Ceiling(MinWidth * scale);
            p.PreferredMinimumHeight = (int)Math.Ceiling(MinHeight * scale);
        }
        if (!_sized)
        {
            _sized = true;
            AppWindow.Resize(new SizeInt32((int)Math.Ceiling(1280 * scale), (int)Math.Ceiling(800 * scale)));
        }
    }

    private void OnFirstRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstRendering;
        _firstFrame.TrySetResult((TimeProvider.System.GetUtcNow().UtcDateTime - _options.ProcessStartUtc).TotalMilliseconds);
    }
}
```

```csharp
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

        var dispatcher = new WinUiDispatcher(ui);
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
        Map: SettingsDefaults.Map() with { Base = "streets" }, Layout: SettingsDefaults.Layout(), RootsConfirmed: true);

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
                preflight => new CopyVm(preflight, _dialogs, _ui),
                CreateVerdict,
                (origin, offload) => CreateCleanup(offload),
                CreateSettings,
                review => Audit(review, null),
                CardPresent,
                VolumeRefusal,
                p.Settings.Save));
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

            var ledger = _p.LedgerFor(s.VideoRoot).Load();
            var audit = CardAudit.Audit(inventory, relisted, now, plan, offload, ledger);
            var listings = new LibraryListings(ListRoot(s.VideoRoot, DestRoot.Video, false), ListRoot(s.PhotoRoot, DestRoot.Photo, false),
                                               [.. s.PreviousPhotoRoots.Select(r => ListRoot(r, DestRoot.Photo, true))]);
            return new CleanupPreparation(new CleanupInputs(inventory, plan, audit, offload, space, listings, ledger, volume, _places, s),
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
            () => Shell.Review?.Plan.Base.Scan.Ledger ?? _p.LedgerFor(s.VideoRoot).Load(),        // the current (old) root's ledger
            root => LedgerPaths.BackupDir(_p.AppDataDir, _p.PathFacts.Canonical(root)));

        private bool CardPresent(CardSource s) => s.Identity is { } id
            ? _p.Volumes.GetVolumes().Any(v => PathRules.Equal(v.Root, s.Root) && v.Identity == id)
            : _p.Lister.Enumerate(s.Root, false, NoExcludes).Errors.IsEmpty;

        /// <summary>CleanupVolumeCheck on the card's volume (top-level listing); browsed folders are refused by CleanupAvailability.</summary>
        private string? VolumeRefusal(CardSource s)
        {
            if (s.IsBrowsedFolder) return null;
            var volume = _p.Volumes.GetVolumes().FirstOrDefault(v => PathRules.Equal(v.Root, s.Root));
            return volume is null
                ? CleanupVolumeCheck.NotACard
                : CleanupVolumeCheck.Refusal(volume, _p.Lister.Enumerate(s.Root, false, NoExcludes), Current, _p.AppDataDir);
        }

        /// <summary>The identity of a detected card, or of the volume holding a browsed folder (as ScanService does).</summary>
        private CardIdentity IdentityOf(CardSource source) =>
            source.Identity
            ?? _p.Volumes.GetVolumes().FirstOrDefault(v => PathRules.Equal(v.Root, Path.GetPathRoot(source.Root) ?? source.Root))?.Identity
            ?? throw new InvalidOperationException("No volume found for " + source.Root);
    }
}
```

```csharp
// src/UasSort.App/Services/DeferredPlaceIndex.cs — PlaceIndex loads on a background thread (Ref §4.2, PlaceIndex.LoadAsync)
namespace UasSort.App.Services;

public sealed class DeferredPlaceIndex(Task<PlaceIndex> loading) : IPlaceIndex
{
    public IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max)
    {
        PlaceIndex index;
        try { index = loading.GetAwaiter().GetResult(); }            // called from scan/plan threads, never the UI thread
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return [];                                               // a missing or damaged places.bin.gz only loses suggestions
        }
        return index.Near(p, r, cls, max);
    }
}
```

```csharp
// src/UasSort.App/Services/CardThumbnails.cs — the App's one IThumbnailSource; swaps the per-scan ThumbnailReader (Ref §9.5)
namespace UasSort.App.Services;

/// <summary>Stable facade over the current card's ThumbnailReader. Pause is honoured across a swap: while any pause is held
/// GetAsync returns empty bytes without touching the card (Commit and Cleanup pause thumbnails, Ref §10.3, §10.6).</summary>
public sealed class CardThumbnails : IThumbnailSource
{
    private readonly Lock _gate = new();
    private ThumbnailReader? _current;
    private int _pauses;

    public void Use(ICardReader reader, IEnumerable<RawItem> items)
    {
        var next = new ThumbnailReader(reader, items);
        ThumbnailReader? old;
        lock (_gate)
        {
            old = _current;
            _current = next;
        }
        old?.Dispose();
    }

    public ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct)
    {
        ThumbnailReader? reader;
        lock (_gate) reader = _pauses > 0 ? null : _current;
        return reader?.GetAsync(id, ct) ?? ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
    }

    public IDisposable Pause()
    {
        IDisposable? inner;
        lock (_gate)
        {
            _pauses++;
            inner = _current?.Pause();
        }
        return new Resume(this, inner);
    }

    private sealed class Resume(CardThumbnails owner, IDisposable? inner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            inner?.Dispose();
            lock (owner._gate) owner._pauses--;
        }
    }
}
```

```csharp
// src/UasSort.App/Services/PlatformAdapters.cs — small App-side adapters of Platform ports to Review seams
namespace UasSort.App.Services;

/// <summary>IFreeSpace for the Setup/Settings free-space lines: the free bytes of the volume holding the path.</summary>
public sealed class VolumeFreeSpace(IVolumeProvider volumes) : IFreeSpace
{
    public long? FreeBytes(string anyPathOnVolume)
    {
        var root = Path.GetPathRoot(anyPathOnVolume);
        if (string.IsNullOrEmpty(root)) return null;
        var volume = volumes.GetVolumes().FirstOrDefault(v => PathRules.Equal(v.Root, root));
        return volume is { IsReady: true } ? volume.FreeBytes : null;
    }
}

/// <summary>IReviewLog over Part 09's FileLog.</summary>
public sealed class FileReviewLog(FileLog log) : IReviewLog
{
    public void Warn(string message) => log.Warn(message);
}

/// <summary>The selftest's Card stage volume list: always empty (Ref §13 Isolation).</summary>
public sealed class NoVolumes : IVolumeProvider
{
    public static readonly NoVolumes Instance = new();

    public IReadOnlyList<VolumeInfo> GetVolumes() => [];
}
```

```csharp
// src/UasSort.App/Services/WinUiDispatcher.cs
using Microsoft.UI.Dispatching;

namespace UasSort.App.Services;

public sealed class WinUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;
    public void Post(Action a)
    {
        if (!queue.TryEnqueue(() => a())) throw new InvalidOperationException("The UI dispatcher is shut down.");
    }
}
```

```csharp
// src/UasSort.App/Services/DialogService.cs — Ref §2.7 #11: only one ContentDialog may be open, so dialogs queue
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Services;

public sealed class DialogService(Func<XamlRoot?> xamlRoot, DispatcherQueue ui) : IDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int Shown { get; private set; }

    public Task<DialogResult> ShowAsync(DialogRequest r)
    {
        if (ui.HasThreadAccess) return ShowOnUiAsync(r);
        var tcs = new TaskCompletionSource<DialogResult>();
        ui.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await ShowOnUiAsync(r)); }
#pragma warning disable CA1031 // handed to the awaiting caller through the task, not swallowed
            catch (Exception ex) { tcs.SetException(ex); }
#pragma warning restore CA1031
        });
        return tcs.Task;
    }

    private async Task<DialogResult> ShowOnUiAsync(DialogRequest r)
    {
        await _gate.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot(),
                Title = r.Title,
                Content = new TextBlock { Text = r.Body, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = r.Primary,
                SecondaryButtonText = r.Secondary ?? "",
                CloseButtonText = r.Close,
                DefaultButton = ContentDialogButton.Close,
            };
            Shown++;
            var result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => DialogResult.Primary,
                ContentDialogResult.Secondary => DialogResult.Secondary,
                _ => DialogResult.Close,
            };
        }
        finally { _gate.Release(); }
    }
}
```

```csharp
// src/UasSort.App/Services/UiFormat.cs — functions for x:Bind (no converters, no reflection)
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace UasSort.App.Services;

/// <summary>x:Bind helper functions. Each name is unique (x:Bind functions are not overloaded by type).</summary>
public static class UiFormat
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = [];

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfNone(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfNotNull(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;
    public static Windows.UI.Text.FontStyle Italic(bool value) => value ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
    public static Windows.UI.Text.FontWeight Weight(bool strong) =>
        strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    public static string Count(int value) => value.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public static double ToDouble(int value) => value;

    /// <summary>InfoBarVm, SetupVm and SettingsPageVm severities (Review's InfoSeverity).</summary>
    public static InfoBarSeverity BarSeverity(InfoSeverity s) => s switch
    {
        InfoSeverity.Error => InfoBarSeverity.Error,
        InfoSeverity.Warning => InfoBarSeverity.Warning,
        InfoSeverity.Success => InfoBarSeverity.Success,
        _ => InfoBarSeverity.Informational,
    };

    public static Visibility VisibleIfError(InfoSeverity s) => s == InfoSeverity.Error ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Plan issues (Core's IssueSeverity).</summary>
    public static InfoBarSeverity IssueBarSeverity(IssueSeverity s) => s switch
    {
        IssueSeverity.Blocking => InfoBarSeverity.Error,
        IssueSeverity.Warning => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational,
    };

    /// <summary>IssueVm.Code as shown under the message in the issues flyout.</summary>
    public static string IssueCodeText(IssueCode code) => code.ToString();

    /// <summary>The ledger folder is derived from the video root, never a setting (Ref \u00A79.1 Setup).</summary>
    public static string LedgerFolder(string videoRoot) => LedgerPaths.For(videoRoot);

    /// <summary>"The photo folder is inside the video folder" note (Ref \u00A79.14).</summary>
    public static Visibility VisibleIfInside(string photoRoot, string videoRoot) =>
        photoRoot.Length > 0 && videoRoot.Length > 0 && PathRules.IsStrictlyUnder(photoRoot, videoRoot) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>MapSettings.Base / ReviewVm.MapBase values in the order of the base-map radio buttons.</summary>
    public static readonly IReadOnlyList<string> MapBases = ["streets", "satellite", "none"];

    public static int BaseIndex(string mapBase) => Math.Max(0, MapBases.ToList().IndexOf(mapBase));

    public static string BaseAt(int index) => MapBases[Math.Clamp(index, 0, MapBases.Count - 1)];

    public static int ModeIndex(CleanupMode mode) => mode == CleanupMode.FreeSpace ? 1 : 0;

    public static int FreeKindIndex(FreeSpaceKind kind) => kind == FreeSpaceKind.FreeUp ? 1 : 0;

    public static bool IsKeep(RowDecision d) => d == RowDecision.Keep;

    public static bool IsDelete(RowDecision d) => d == RowDecision.Delete;

    /// <summary>"#RRGGBB" (MapBridge palette, Ref §9.4) → a cached brush.</summary>
    public static SolidColorBrush Brush(string hex)
    {
        if (Brushes.TryGetValue(hex, out var b)) return b;
        var v = hex.StartsWith('#') ? hex[1..] : hex;
        var c = v.Length == 6
            ? Color.FromArgb(255, Convert.ToByte(v[..2], 16), Convert.ToByte(v[2..4], 16), Convert.ToByte(v[4..6], 16))
            : Color.FromArgb(255, 128, 128, 128);
        return Brushes[hex] = new SolidColorBrush(c);
    }

    public static SolidColorBrush VerdictBrush(VerdictLevel level) => level switch
    {
        VerdictLevel.Safe => Brush("#107C10"),
        VerdictLevel.SafeWithAssumptions => Brush("#9D5D00"),
        _ => Brush("#C42B1C"),
    };
}
```

```csharp
// src/UasSort.App/Services/VisualTree.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace UasSort.App.Services;

public static class VisualTree
{
    public static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && (match is null || match(t))) return t;
            if (FindDescendant(child, match) is { } found) return found;
        }
        return null;
    }

    public static List<T> FindAll<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        var list = new List<T>();
        void Walk(DependencyObject d)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is T t && (match is null || match(t))) list.Add(t);
                Walk(c);
            }
        }
        Walk(root);
        return list;
    }

    public static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T t) return t;
        return null;
    }
}
```

```xml
<!-- src/UasSort.App/Pages/ShellPage.xaml — TitleBar (Ref §9.2) and the stage Frame; InfoBars belong to the stage pages -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.ShellPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TitleBar x:Name="AppTitleBar" Title="uas-sort">
            <TitleBar.IconSource>
                <ImageIconSource ImageSource="ms-appx:///Assets/AppIcon.ico" />
            </TitleBar.IconSource>
            <TitleBar.Content>
                <Border x:Name="CardChip" Style="{StaticResource ChipBorder}" VerticalAlignment="Center"
                        Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.CardChipText), Mode=OneWay}">
                    <TextBlock Text="{x:Bind Vm.CardChipText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                </Border>
            </TitleBar.Content>
            <TitleBar.RightHeader>
                <StackPanel Orientation="Horizontal" Spacing="2" Margin="0,0,8,0">
                    <Button x:Name="RescanButton" Command="{x:Bind Vm.RescanCommand}" IsEnabled="{x:Bind Vm.CanRescan, Mode=OneWay}"
                            ToolTipService.ToolTip="Rescan (F5)" Style="{StaticResource SubtleButtonStyle}">
                        <FontIcon Glyph="&#xE72C;" FontSize="14" />
                    </Button>
                    <!-- Undo/Redo act on ShellVm.Review (not observable), so the code-behind enables them (CanUndoRedo + Review.CanUndo/CanRedo) -->
                    <Button x:Name="UndoButton" Click="OnUndo" IsEnabled="False"
                            ToolTipService.ToolTip="Undo (Ctrl+Z)" Style="{StaticResource SubtleButtonStyle}">
                        <FontIcon Glyph="&#xE7A7;" FontSize="14" />
                    </Button>
                    <Button x:Name="RedoButton" Click="OnRedo" IsEnabled="False"
                            ToolTipService.ToolTip="Redo (Ctrl+Y)" Style="{StaticResource SubtleButtonStyle}">
                        <FontIcon Glyph="&#xE7A6;" FontSize="14" />
                    </Button>
                    <Button x:Name="CleanUpButton" Content="Clean up card…" Command="{x:Bind Vm.CleanupCommand}"
                            IsEnabled="{x:Bind Vm.CleanupEnabled, Mode=OneWay}" Style="{StaticResource SubtleButtonStyle}"
                            ToolTipService.ToolTip="{x:Bind Vm.CleanupTooltip, Mode=OneWay}" />
                    <Button x:Name="SettingsButton" Command="{x:Bind Vm.SettingsCommand}" IsEnabled="{x:Bind Vm.CanOpenSettings, Mode=OneWay}"
                            ToolTipService.ToolTip="Settings" Style="{StaticResource SubtleButtonStyle}">
                        <FontIcon Glyph="&#xE713;" FontSize="14" />
                    </Button>
                </StackPanel>
            </TitleBar.RightHeader>
        </TitleBar>

        <Frame x:Name="StageFrame" Grid.Row="1" CacheSize="2" />
    </Grid>
</Page>
```

```csharp
// src/UasSort.App/Pages/ShellPage.xaml.cs — the ShellVm stage machine drives the Frame (Ref §9.1)
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class ShellPage : Page
{
    /// <summary>Stage → page type. Tasks 11.5–11.16 each add their line.</summary>
    public static readonly Dictionary<Stage, Type> StagePages = new()
    {
    };

    private MainWindow _window = null!;
    private object? _shown;
    private ReviewVm? _review;

    public ShellPage() => InitializeComponent();

    public ShellVm Vm { get; private set; } = null!;

    public MainWindow Window => _window;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _window = (MainWindow)e.Parameter;
        Vm = _window.Services.Shell;
        Vm.PropertyChanged += OnShellChanged;
        Loaded += (_, _) => _window.SetTitleBar(AppTitleBar);
        Show();
    }

    // ShellVm.Go sets Stage first and Current second, so the frame follows Current (the pair is consistent then).
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellVm.Current)) Show();
        else if (e.PropertyName == nameof(ShellVm.CanUndoRedo)) UpdateUndoButtons();
    }

    /// <summary>Shows ShellVm.Current in the page registered for ShellVm.Stage; the page casts StageArgs.Vm.</summary>
    private void Show()
    {
        WatchReview(Vm.Review);
        UpdateUndoButtons();
        if (Vm.Current is not { } vm || ReferenceEquals(vm, _shown)) return;
        if (!StagePages.TryGetValue(Vm.Stage, out var page)) return;
        _shown = vm;
        StageFrame.Navigate(page, new StageArgs(vm, _window));
    }

    private void WatchReview(ReviewVm? review)
    {
        if (ReferenceEquals(review, _review)) return;
        if (_review is not null) _review.PropertyChanged -= OnReviewChanged;
        _review = review;
        if (review is null) return;
        review.PropertyChanged += OnReviewChanged;
        _window.Services.Thumbnails.Clear();                    // a new scan: ItemIds may repeat with other bytes
    }

    private void OnReviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewVm.CanUndo) or nameof(ReviewVm.CanRedo) or nameof(ReviewVm.IsReadOnly))
            UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        var review = Vm.CanUndoRedo ? Vm.Review : null;
        UndoButton.IsEnabled = review is { CanUndo: true, IsReadOnly: false };
        RedoButton.IsEnabled = review is { CanRedo: true, IsReadOnly: false };
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (Vm.CanUndoRedo && Vm.Review is { } review) _ = review.UndoCommand.ExecuteAsync(null);
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (Vm.CanUndoRedo && Vm.Review is { } review) _ = review.RedoCommand.ExecuteAsync(null);
    }
}

/// <summary>What every stage page receives (defined here): ShellVm.Current and the window.</summary>
public sealed record StageArgs(object Vm, MainWindow Window);
```

```csharp
// src/UasSort.App/SelfTest/SelfTestRunner.cs — Ref §13 UI smoke test: exits 0/1, writes the result JSON, 60 s budget.
// The records SelfTestCheck/SelfTestResult/SelfTestJsonContext are Part 01's (src/UasSort.App/SelfTest/SelfTestResult.cs).
using System.Text.Json;

namespace UasSort.App.SelfTest;

internal sealed class SelfTestContext(MainWindow window, LaunchOptions options)
{
    public MainWindow Window { get; } = window;
    public LaunchOptions Options { get; } = options;
    public double FirstFrameMs { get; set; } = -1;
    public AppServices Services => Window.Services;
    public SelfTestSandbox Sandbox => Window.Services.Sandbox!;
    public Dictionary<string, object> Shared { get; } = [];              // state handed from one check to the next
}

internal static class SelfTestRunner
{
    public static readonly TimeSpan PerCheck = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(55);  // deploy.ps1 / selftest.ps1 kill at 60 s
    private static int _finished;

    public static async Task RunAsync(SelfTestContext ctx)
    {
        var checks = new List<SelfTestCheck>();
        using var watchdog = new Timer(_ =>
        {
            lock (checks) { checks.Add(SelfTestCheck.Fail("watchdog", "selftest did not finish within 55 s")); Finish(ctx, checks); }
        }, null, Watchdog, Timeout.InfiniteTimeSpan);

        try { ctx.FirstFrameMs = await ctx.Window.FirstFrameMs.WaitAsync(PerCheck); }
        catch (TimeoutException) { lock (checks) checks.Add(SelfTestCheck.Fail("firstFrame", "no frame rendered within 20 s")); }

        var wanted = SelfTestChecks.All.Where(c => ctx.Options.Only is null || ctx.Options.Only.Contains(c.Name)).ToList();
        if (ctx.Options.Only is not null)
            foreach (var name in ctx.Options.Only.Where(n => SelfTestChecks.All.All(c => c.Name != n)))
                lock (checks) checks.Add(SelfTestCheck.Fail(name, "unknown check"));

        foreach (var (name, run) in wanted)
        {
            SelfTestCheck result;
            try
            {
                var task = run(ctx);
                result = await Task.WhenAny(task, Task.Delay(PerCheck)) == task
                    ? await task
                    : SelfTestCheck.Fail(name, $"timed out after {PerCheck.TotalSeconds:0} s");
            }
#pragma warning disable CA1031 // selftest: every failure becomes a failed check in the result file
            catch (Exception ex) { result = SelfTestCheck.Fail(name, ex.GetType().Name + ": " + ex.Message); }
#pragma warning restore CA1031
            lock (checks) checks.Add(result);
        }
        lock (checks) Finish(ctx, checks);
    }

    private static void Finish(SelfTestContext ctx, List<SelfTestCheck> checks)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;         // the watchdog and the normal end never both write
        bool ok = checks.Count > 0 && checks.TrueForAll(c => c.Status != "fail");
        var json = JsonSerializer.SerializeToUtf8Bytes(new SelfTestResult(ok, Math.Round(ctx.FirstFrameMs), [.. checks]),
                                                       SelfTestJsonContext.Default.SelfTestResult);
        try { SelfTestSandbox.WriteResult(ctx.Options.ResultPath, json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ok = false; }
        ctx.Services.Sandbox?.Dispose();
        Environment.Exit(ok ? 0 : 1);
    }
}
```

```csharp
// src/UasSort.App/SelfTest/SelfTestChecks.cs — the registry; later tasks append entries to All
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    public static readonly List<(string Name, Func<SelfTestContext, Task<SelfTestCheck>> Run)> All =
    [
        ("shell.render", ShellRender),
        ("window.minSize", WindowMinSize),
        ("probe.timeZone", ProbeTimeZone),
        ("json.planEdit", JsonPlanEdit),
        ("placeholderVisibility", PlaceholderVisibility),
    ];

    private static Task<SelfTestCheck> ShellRender(SelfTestContext ctx)
    {
        var shell = ctx.Window.Shell;
        if (shell is null) return Task.FromResult(SelfTestCheck.Fail("shell.render", "ShellPage is not the window content"));
        var title = VisualTree.FindDescendant<TitleBar>(shell);
        return Task.FromResult(title?.Title == "uas-sort" && shell.StageFrame is not null
            ? SelfTestCheck.Pass("shell.render", $"TitleBar and stage frame rendered; first frame {ctx.FirstFrameMs:0} ms")
            : SelfTestCheck.Fail("shell.render", "TitleBar 'uas-sort' not found"));
    }

    private static Task<SelfTestCheck> WindowMinSize(SelfTestContext ctx)
    {
        var scale = ctx.Window.Shell?.XamlRoot?.RasterizationScale ?? 1.0;
        var p = (OverlappedPresenter)ctx.Window.AppWindow.Presenter;
        int w = (int)Math.Ceiling(MainWindow.MinWidth * scale), h = (int)Math.Ceiling(MainWindow.MinHeight * scale);
        return Task.FromResult(p.PreferredMinimumWidth == w && p.PreferredMinimumHeight == h
            ? SelfTestCheck.Pass("window.minSize", $"{w}x{h} physical at scale {scale}")
            : SelfTestCheck.Fail("window.minSize", $"expected {w}x{h}, got {p.PreferredMinimumWidth}x{p.PreferredMinimumHeight}"));
    }

    private static Task<SelfTestCheck> ProbeTimeZone(SelfTestContext ctx)
    {
        var zone = new GeoTimeZoneResolver().Resolve(new GeoPoint(57.5504, -153.7390));   // Zachar Bay
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone.IanaId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc), tz);
        return Task.FromResult(zone.IanaId == "America/Anchorage" && local == new DateTime(2026, 9, 27, 10, 6, 27)
            ? SelfTestCheck.Pass("probe.timeZone", "Zachar Bay → America/Anchorage, 18:06:27Z → 10:06:27")
            : SelfTestCheck.Fail("probe.timeZone", $"{zone.IanaId}, {local:O}"));
    }

    private static Task<SelfTestCheck> JsonPlanEdit(SelfTestContext ctx)
    {
        PlanEdit[] edits =
        [
            new Merge(new ItemId("DCIM/DJI_001/a.MP4"), new ItemId("DCIM/DJI_001/b.MP4")),
            new Retarget(new ItemId("DCIM/DJI_001/a.MP4"), new AppendTo(@"C:\v\2026\2026-07\2026-07-25 Council Road"), false, []),
            new SetDayIncluded(new DateOnly(2026, 7, 25), true),
        ];
        foreach (var e in edits)
        {
            var json = JsonSerializer.Serialize(e, CoreJsonContext.Default.PlanEdit);
            var back = JsonSerializer.Deserialize(json, CoreJsonContext.Default.PlanEdit);
            if (back is null || back.GetType() != e.GetType() || JsonSerializer.Serialize(back, CoreJsonContext.Default.PlanEdit) != json)
                return Task.FromResult(SelfTestCheck.Fail("json.planEdit", "round-trip changed " + json));
        }
        return Task.FromResult(SelfTestCheck.Pass("json.planEdit", "Merge, Retarget(AppendTo), SetDayIncluded round-trip"));
    }

    /// <summary>Ref §13: list this PC's configured video root (real settings, read-only; listing only, .uas-sort excluded) and
    /// require an entry with 0x400000 or 0x1000; none → notApplicable (deploy then needs -AllowNoPlaceholders).
    /// PlatformServices.Create writes nothing, so the real app-data folder is only read.</summary>
    private static Task<SelfTestCheck> PlaceholderVisibility(SelfTestContext ctx)
    {
        var real = PlatformServices.Create(KnownFolders.AppDataDir(), TimeProvider.System);
        var settings = real.Settings.Load(readOnly: true).Settings;
        var listing = real.Lister.Enumerate(settings.VideoRoot, recurse: true,
                                            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".uas-sort" });
        if (listing.Entries.IsEmpty && !listing.Errors.IsEmpty)
            return Task.FromResult(SelfTestCheck.NotApplicable("placeholderVisibility", $"video root {settings.VideoRoot} not listable"));
        var hit = listing.Entries.FirstOrDefault(e => (e.RawAttributes & (0x400000u | 0x1000u)) != 0);
        return Task.FromResult(hit is not null
            ? SelfTestCheck.Pass("placeholderVisibility", $"0x{hit.RawAttributes:X} on {hit.RelPath}")
            : SelfTestCheck.NotApplicable("placeholderVisibility", $"no cloud-only files under {settings.VideoRoot}"));
    }
}
```

Then remove Part 01's probe page and minimal selftest (Part 01's `LaunchOptions.cs`, `Program.cs`, `SelfTest/SelfTestResult.cs` and `SelfTest/stack-exif.jpg` stay):

```bash
git rm src/UasSort.App/SelfTest/MinimalSelfTest.cs src/UasSort.App/Pages/StackProbePage.xaml src/UasSort.App/Pages/StackProbePage.xaml.cs src/UasSort.App/Pages/StackProbeVm.cs src/UasSort.App/Pages/ProbeTemplateSelector.cs
```

- [ ] **Step 4: Run it and watch it pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only shell.render,window.minSize,probe.timeZone,json.planEdit,placeholderVisibility`
Expected (values vary; Part 01's `SelfTestJsonContext` writes the file indented, shown here on one line):
```
{"ok":true,"firstFrameMs":640,"checks":[{"name":"shell.render","status":"pass","detail":"TitleBar and stage frame rendered; first frame 640 ms"},{"name":"window.minSize","status":"pass","detail":"1650x1050 physical at scale 1.5"},{"name":"probe.timeZone","status":"pass","detail":"Zachar Bay → America/Anchorage, 18:06:27Z → 10:06:27"},{"name":"json.planEdit","status":"pass","detail":"Merge, Retarget(AppendTo), SetDayIncluded round-trip"},{"name":"placeholderVisibility","status":"pass","detail":"0x5400020 on 2026\\2026-07\\…"}]}
exit code 0
```
(`placeholderVisibility` may be `notApplicable` on a PC without cloud-only files; `ok` stays true.) Also run `dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*XamlLintTests"` → `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add tools/selftest.ps1 src/UasSort.App
git commit -m "feat: app shell (single instance, composition root, TitleBar/Mica/min size, stage frame) and selftest runner

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.5: Setup stage and Settings page

**Files:**
- Create: `src/UasSort.App/Services/FolderPickerService.cs`
- Create: `src/UasSort.App/Pages/SetupPage.xaml`, `Pages/SetupPage.xaml.cs`
- Create: `src/UasSort.App/Pages/SettingsPage.xaml`, `Pages/SettingsPage.xaml.cs`
- Create: `src/UasSort.App/SelfTest/SelfTestChecks.Pages.cs`
- Modify: `src/UasSort.App/Pages/ShellPage.xaml.cs` (two `StagePages` entries), `src/UasSort.App/Services/UiFormat.cs` (`ClockModeIndex`), `src/UasSort.App/SelfTest/SelfTestChecks.cs` (register `page.settings`)

**Interfaces:**
- Consumes (registry Review VMs table): `SetupVm` (`VideoRoot`, `PhotoRoot`, `VideoFreeText`, `PhotoFreeText`, `LedgerStatus`, `LedgerSeverity`, `CanKeepOnDevice`, `CanConfirm`, `RecoveryText`, `PhotoInsideVideoNote`, `ConfirmCommand`, `KeepOnDeviceCommand`, `SetVideoRoot`, `SetPhotoRoot`), `SettingsPageVm` (`VideoRoot`, `PhotoRoot`, `VideoFreeText`, `PhotoFreeText`, `PreviousPhotoRoots`, `LedgerFolder`, `BackupFolder`, `LedgerStatus`, `LedgerSeverity`, `CanKeepOnDevice`, `NoHistoryPrompt`, `RadiusMiles`, `GapDays`, `IsSiteLocal`, `ClockZone`, `ClockLearnedText`, `CopyJpgTwin`, `MapBase`, `StreetsUrl`, `StreetsDarkUrl`, `SatelliteUrl`, `SatellitePresets`, `AboutText`, the commands, `ChangeVideoRootAsync`, `ChangePhotoRoot`), `PreviousRootVm` (`Path`, `ForgetCommand`), `ShellVm.SettingsCommand`/`CloseSettings()`, `DroneClock.UsZones`; `StageArgs`, `UiFormat`, `VisualTree` (Task 11.4); `CommunityToolkit.WinUI.Controls.SettingsCard`/`SettingsExpander` (SettingsControls 8.3.260402-preview2); `Microsoft.Windows.Storage.Pickers.FolderPicker` (Windows App SDK; owner from `AppWindow.Id`; UNVERIFIED unpackaged).
- Produces (defined here): `static class FolderPickerService { static Task<string?> PickFolderAsync(MainWindow window, string? startFolder); }`; `SetupPage`, `SettingsPage` (each `Vm` property + `OnNavigatedTo(StageArgs)`; `SettingsPage.ZoneIds`); `UiFormat.ClockModeIndex(bool)`; `internal static partial class SelfTestChecks { static Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout); static T? CurrentPage<T>(SelfTestContext ctx) where T : Page; }`; selftest check `page.settings`.

- [ ] **Step 1: Register the failing check**

Add to `SelfTestChecks.All` in `src/UasSort.App/SelfTest/SelfTestChecks.cs`:

```csharp
        ("page.settings", PageSettings),
```

and create the check file:

```csharp
// src/UasSort.App/SelfTest/SelfTestChecks.Pages.cs
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var until = TimeProvider.System.GetTimestamp() + (long)(timeout.TotalSeconds * TimeProvider.System.TimestampFrequency);
        while (!condition())
        {
            if (TimeProvider.System.GetTimestamp() > until) return false;
            await Task.Delay(50);
        }
        return true;
    }

    public static T? CurrentPage<T>(SelfTestContext ctx) where T : Page => ctx.Window.Shell?.StageFrame.Content as T;

    private static async Task<SelfTestCheck> PageSettings(SelfTestContext ctx)
    {
        var shell = ctx.Services.Shell;
        var before = shell.Stage;
        shell.SettingsCommand.Execute(null);
        if (!await WaitUntilAsync(() => CurrentPage<SettingsPage>(ctx) is { IsLoaded: true }, TimeSpan.FromSeconds(5)))
            return SelfTestCheck.Fail("page.settings", "SettingsPage did not open");
        var page = CurrentPage<SettingsPage>(ctx)!;
        var video = VisualTree.FindDescendant<SettingsCard>(page, c => (c.Header as string) == "Video folder");
        var rootShown = VisualTree.FindDescendant<TextBlock>(page, t => t.Text == ctx.Sandbox.VideoRoot) is not null;
        var about = VisualTree.FindDescendant<TextBlock>(page, t => t.Text.Contains("GeoNames", StringComparison.Ordinal)) is not null;
        shell.CloseSettings();
        await WaitUntilAsync(() => shell.Stage == before, TimeSpan.FromSeconds(5));
        return video is not null && rootShown && about
            ? SelfTestCheck.Pass("page.settings", "Video folder card shows the sandbox root; About credits GeoNames")
            : SelfTestCheck.Fail("page.settings", $"card={video is not null} root={rootShown} about={about}");
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only page.settings`
Expected: build error `CS0246: The type or namespace name 'SettingsPage' could not be found` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.App/Services/FolderPickerService.cs — Windows App SDK picker; returns a path string (no StorageFolder: banned)
using Microsoft.Windows.Storage.Pickers;

namespace UasSort.App.Services;

public static class FolderPickerService
{
    public static async Task<string?> PickFolderAsync(MainWindow window, string? startFolder)
    {
        var picker = new FolderPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        if (!string.IsNullOrEmpty(startFolder)) picker.SuggestedStartFolder = startFolder;
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }
}
```

```xml
<!-- src/UasSort.App/Pages/SetupPage.xaml — Ref §9.1 Setup: the first three Settings cards, ledger status read-only -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.SetupPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctk="using:CommunityToolkit.WinUI.Controls"
    xmlns:ui="using:UasSort.App.Services">
    <ScrollViewer>
        <StackPanel MaxWidth="820" Padding="24" Spacing="8">
            <TextBlock Text="Set up uas-sort" Style="{StaticResource TitleTextBlockStyle}" />
            <TextBlock Text="Confirm where videos and photos go. The history (ledger) lives in the video folder, in .uas-sort."
                       TextWrapping="Wrap" Style="{StaticResource BodyTextBlockStyle}" />
            <InfoBar IsOpen="True" Severity="Warning" IsClosable="False" Message="{x:Bind Vm.RecoveryText, Mode=OneWay}"
                     Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.RecoveryText), Mode=OneWay}" />
            <ctk:SettingsCard Header="Video folder" Description="{x:Bind Vm.VideoFreeText, Mode=OneWay}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <TextBlock Text="{x:Bind Vm.VideoRoot, Mode=OneWay}" VerticalAlignment="Center" MaxWidth="360" TextTrimming="CharacterEllipsis" />
                    <Button Content="Change…" Click="OnPickVideoRoot" />
                </StackPanel>
            </ctk:SettingsCard>
            <ctk:SettingsCard Header="Photo folder" Description="{x:Bind Vm.PhotoFreeText, Mode=OneWay}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <TextBlock Text="{x:Bind Vm.PhotoRoot, Mode=OneWay}" VerticalAlignment="Center" MaxWidth="360" TextTrimming="CharacterEllipsis" />
                    <Button Content="Change…" Click="OnPickPhotoRoot" />
                </StackPanel>
            </ctk:SettingsCard>
            <TextBlock Text="The photo folder is inside the video folder; that is supported." Style="{StaticResource CaptionText}"
                       Margin="16,0,0,4" Visibility="{x:Bind ui:UiFormat.Visible(Vm.PhotoInsideVideoNote), Mode=OneWay}" />
            <ctk:SettingsCard Header="History (ledger)" Description="{x:Bind ui:UiFormat.LedgerFolder(Vm.VideoRoot), Mode=OneWay}">
                <Button Content="Keep on this device" Command="{x:Bind Vm.KeepOnDeviceCommand}"
                        Visibility="{x:Bind ui:UiFormat.Visible(Vm.CanKeepOnDevice), Mode=OneWay}" />
            </ctk:SettingsCard>
            <InfoBar IsOpen="True" IsClosable="False" Severity="{x:Bind ui:UiFormat.BarSeverity(Vm.LedgerSeverity), Mode=OneWay}"
                     Message="{x:Bind Vm.LedgerStatus, Mode=OneWay}" />
            <!-- ConfirmCommand's CanExecute is CanConfirm (both roots set, no ledger error) -->
            <Button Content="Continue" Style="{StaticResource AccentButtonStyle}" HorizontalAlignment="Right"
                    Command="{x:Bind Vm.ConfirmCommand}" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

```csharp
// src/UasSort.App/Pages/SetupPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class SetupPage : Page
{
    private MainWindow _window = null!;
    public SetupPage() => InitializeComponent();
    public SetupVm Vm { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (SetupVm)args.Vm;                                   // ShellVm.Current on the Setup stage
        _window = args.Window;
    }

    private async void OnPickVideoRoot(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.VideoRoot) is { } path) Vm.SetVideoRoot(path);
    }

    private async void OnPickPhotoRoot(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.PhotoRoot) is { } path) Vm.SetPhotoRoot(path);
    }
}
```

```xml
<!-- src/UasSort.App/Pages/SettingsPage.xaml — Ref §9.14 -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.SettingsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctk="using:CommunityToolkit.WinUI.Controls"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <StackPanel Orientation="Horizontal" Spacing="8" Padding="24,12,24,4">
            <Button Click="OnClose" Style="{StaticResource SubtleButtonStyle}" ToolTipService.ToolTip="Back">
                <FontIcon Glyph="&#xE72B;" FontSize="14" />
            </Button>
            <TextBlock Text="Settings" Style="{StaticResource TitleTextBlockStyle}" />
        </StackPanel>
        <ScrollViewer Grid.Row="1">
            <StackPanel MaxWidth="900" Padding="24,4,24,24" Spacing="4">
                <TextBlock Text="Folders" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="0,8,0,4" />
                <ctk:SettingsCard Header="Video folder" Description="{x:Bind Vm.VideoFreeText, Mode=OneWay}">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="{x:Bind Vm.VideoRoot, Mode=OneWay}" VerticalAlignment="Center" MaxWidth="420" TextTrimming="CharacterEllipsis" />
                        <Button Content="Change…" Click="OnPickVideoRoot" />
                    </StackPanel>
                </ctk:SettingsCard>
                <InfoBar IsOpen="True" Severity="Warning" IsClosable="False" Title="No history in the new video folder"
                         Message="{x:Bind Vm.NoHistoryPrompt, Mode=OneWay}" Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.NoHistoryPrompt), Mode=OneWay}">
                    <InfoBar.Content>
                        <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">
                            <Button Content="Copy" Style="{StaticResource AccentButtonStyle}" Command="{x:Bind Vm.CopyLedgerCommand}" />
                            <Button Content="Start empty" Command="{x:Bind Vm.StartEmptyCommand}" />
                        </StackPanel>
                    </InfoBar.Content>
                </InfoBar>
                <ctk:SettingsCard Header="Photo folder" Description="{x:Bind Vm.PhotoFreeText, Mode=OneWay}">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="{x:Bind Vm.PhotoRoot, Mode=OneWay}" VerticalAlignment="Center" MaxWidth="420" TextTrimming="CharacterEllipsis" />
                        <Button Content="Change…" Click="OnPickPhotoRoot" />
                    </StackPanel>
                </ctk:SettingsCard>
                <TextBlock Text="The photo folder is inside the video folder; that is supported." Style="{StaticResource CaptionText}"
                           Margin="16,0,0,4" Visibility="{x:Bind ui:UiFormat.VisibleIfInside(Vm.PhotoRoot, Vm.VideoRoot), Mode=OneWay}" />
                <ctk:SettingsExpander Header="Previous photo folders" Description="Still checked for photos you already imported">
                    <ctk:SettingsExpander.ItemsHeader>
                        <ItemsControl ItemsSource="{x:Bind Vm.PreviousPhotoRoots, Mode=OneWay}" Margin="16,4">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="rv:PreviousRootVm">
                                    <Grid ColumnSpacing="8" Margin="0,2">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="Auto" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{x:Bind Path}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                        <Button Grid.Column="1" Content="Forget" Command="{x:Bind ForgetCommand}" />
                                    </Grid>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ctk:SettingsExpander.ItemsHeader>
                </ctk:SettingsExpander>

                <TextBlock Text="History (ledger)" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="0,16,0,4" />
                <ctk:SettingsCard Header="History folder" Description="{x:Bind Vm.LedgerFolder, Mode=OneWay}">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button Content="Keep on this device" Command="{x:Bind Vm.KeepOnDeviceCommand}"
                                Visibility="{x:Bind ui:UiFormat.Visible(Vm.CanKeepOnDevice), Mode=OneWay}" />
                        <Button Content="Open" Command="{x:Bind Vm.OpenLedgerCommand}" />
                    </StackPanel>
                </ctk:SettingsCard>
                <InfoBar IsOpen="True" IsClosable="False" Severity="{x:Bind ui:UiFormat.BarSeverity(Vm.LedgerSeverity), Mode=OneWay}"
                         Message="{x:Bind Vm.LedgerStatus, Mode=OneWay}" />
                <ctk:SettingsCard Header="Local backup of the history" Description="{x:Bind Vm.BackupFolder, Mode=OneWay}">
                    <Button Content="Open" Command="{x:Bind Vm.OpenBackupCommand}" />
                </ctk:SettingsCard>

                <TextBlock Text="Grouping" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="0,16,0,4" />
                <ctk:SettingsCard Header="Default radius (miles)" Description="Clips farther apart than this start a new group">
                    <NumberBox Value="{x:Bind Vm.RadiusMiles, Mode=TwoWay}" Minimum="5" Maximum="100" SmallChange="1"
                               SpinButtonPlacementMode="Compact" Width="140" />
                </ctk:SettingsCard>
                <ctk:SettingsCard Header="Default day gap" Description="Days without flights that still keep one group">
                    <!-- GapDays is an int: shown through ToDouble, written back by OnGapChanged -->
                    <NumberBox x:Name="GapBox" Value="{x:Bind ui:UiFormat.ToDouble(Vm.GapDays), Mode=OneWay}" Minimum="0" Maximum="7"
                               SmallChange="1" SpinButtonPlacementMode="Compact" Width="140" ValueChanged="OnGapChanged" />
                </ctk:SettingsCard>

                <TextBlock Text="Drone clock" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="0,16,0,4" />
                <ctk:SettingsCard Header="The RC 2 clock" Description="{x:Bind Vm.ClockLearnedText, Mode=OneWay}">
                    <StackPanel Spacing="8">
                        <RadioButtons x:Name="ClockModeButtons" SelectedIndex="{x:Bind ui:UiFormat.ClockModeIndex(Vm.IsSiteLocal), Mode=OneWay}"
                                      SelectionChanged="OnClockModeChanged">
                            <x:String>Follows local time at each site</x:String>
                            <x:String>Fixed zone</x:String>
                        </RadioButtons>
                        <ComboBox x:Name="ZoneBox" ItemsSource="{x:Bind ZoneIds, Mode=OneWay}" SelectionChanged="OnZoneChanged"
                                  MinWidth="240" IsEnabled="{x:Bind ui:UiFormat.Not(Vm.IsSiteLocal), Mode=OneWay}" />
                    </StackPanel>
                </ctk:SettingsCard>

                <TextBlock Text="Photos" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="0,16,0,4" />
                <ctk:SettingsCard Header="Copy the JPG twin" Description="Copy the JPG that the drone saves next to each DNG">
                    <ToggleSwitch IsOn="{x:Bind Vm.CopyJpgTwin, Mode=TwoWay}" />
                </ctk:SettingsCard>

                <TextBlock Text="Map" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="0,16,0,4" />
                <ctk:SettingsCard Header="Default base map">
                    <RadioButtons x:Name="BaseButtons" SelectedIndex="{x:Bind ui:UiFormat.BaseIndex(Vm.MapBase), Mode=OneWay}" MaxColumns="3"
                                  SelectionChanged="OnBaseChanged">
                        <x:String>Streets</x:String>
                        <x:String>Satellite</x:String>
                        <x:String>Off</x:String>
                    </RadioButtons>
                </ctk:SettingsCard>
                <ctk:SettingsExpander Header="Advanced" Description="Base map addresses (OpenFreeMap, Esri, USGS)">
                    <ctk:SettingsExpander.Items>
                        <ctk:SettingsCard Header="Streets style URL">
                            <TextBox Text="{x:Bind Vm.StreetsUrl, Mode=TwoWay}" MinWidth="420" />
                        </ctk:SettingsCard>
                        <ctk:SettingsCard Header="Dark streets style URL">
                            <TextBox Text="{x:Bind Vm.StreetsDarkUrl, Mode=TwoWay}" MinWidth="420" />
                        </ctk:SettingsCard>
                        <ctk:SettingsCard Header="Satellite tile URL">
                            <StackPanel Orientation="Horizontal" Spacing="8">
                                <TextBox Text="{x:Bind Vm.SatelliteUrl, Mode=TwoWay}" MinWidth="420" />
                                <!-- SatellitePresets is a name → URL dictionary; the menu is built in OnNavigatedTo -->
                                <DropDownButton Content="Presets">
                                    <DropDownButton.Flyout>
                                        <MenuFlyout x:Name="PresetMenu" Placement="BottomEdgeAlignedRight" />
                                    </DropDownButton.Flyout>
                                </DropDownButton>
                            </StackPanel>
                        </ctk:SettingsCard>
                    </ctk:SettingsExpander.Items>
                </ctk:SettingsExpander>

                <TextBlock Text="About" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="0,16,0,4" />
                <ctk:SettingsCard Header="uas-sort">
                    <TextBlock Text="{x:Bind Vm.AboutText, Mode=OneWay}" TextWrapping="Wrap" MaxWidth="520" />
                </ctk:SettingsCard>
            </StackPanel>
        </ScrollViewer>
    </Grid>
</Page>
```

```csharp
// src/UasSort.App/Pages/SettingsPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class SettingsPage : Page
{
    private MainWindow _window = null!;
    public SettingsPage() => InitializeComponent();
    public SettingsPageVm Vm { get; private set; } = null!;

    /// <summary>The fixed-zone choices: DroneClock.UsZones, plus the saved zone when it is not one of them.</summary>
    public IReadOnlyList<string> ZoneIds { get; private set; } = [];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (SettingsPageVm)args.Vm;                            // ShellVm.Current on the Settings stage
        _window = args.Window;
        ZoneIds = DroneClock.UsZones.Contains(Vm.ClockZone) ? [.. DroneClock.UsZones] : [.. DroneClock.UsZones, Vm.ClockZone];
        Bindings.Update();
        ZoneBox.SelectedItem = Vm.ClockZone;
        PresetMenu.Items.Clear();
        foreach (var (name, url) in Vm.SatellitePresets)
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += (_, _) => Vm.SatelliteUrl = url;
            PresetMenu.Items.Add(item);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => _window.Services.Shell.CloseSettings();

    private async void OnPickVideoRoot(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.VideoRoot) is { } path) await Vm.ChangeVideoRootAsync(path);
    }

    private async void OnPickPhotoRoot(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.PhotoRoot) is { } path) Vm.ChangePhotoRoot(path);
    }

    private void OnGapChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (Vm is not null && !double.IsNaN(args.NewValue)) Vm.GapDays = (int)Math.Round(args.NewValue);
    }

    private void OnClockModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && ClockModeButtons.SelectedIndex >= 0) Vm.IsSiteLocal = ClockModeButtons.SelectedIndex == 0;
    }

    private void OnZoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && ZoneBox.SelectedItem is string zone) Vm.ClockZone = zone;
    }

    private void OnBaseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && BaseButtons.SelectedIndex >= 0) Vm.MapBase = UiFormat.BaseAt(BaseButtons.SelectedIndex);
    }
}
```

Add to `UiFormat` (Task 11.4 file):

```csharp
    /// <summary>SettingsPageVm.IsSiteLocal as the index of the drone-clock radio buttons (0 site-local, 1 fixed zone).</summary>
    public static int ClockModeIndex(bool siteLocal) => siteLocal ? 0 : 1;
```

Add to `ShellPage.StagePages`:

```csharp
        [Stage.Setup] = typeof(SetupPage),
        [Stage.Settings] = typeof(SettingsPage),
```

- [ ] **Step 4: Run it and watch it pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only page.settings` and `dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*XamlLintTests"`
Expected: `{"ok":true,…"checks":[{"name":"page.settings","status":"pass","detail":"Video folder card shows the sandbox root; About credits GeoNames"}]}`, `exit code 0`; lint `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: Setup stage and Settings page (roots, ledger status, clock, map URLs, About)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 11.6: Card and Scan pages

**Files:**
- Create: `src/UasSort.App/Pages/CardPage.xaml`, `Pages/CardPage.xaml.cs`, `Pages/ScanPage.xaml`, `Pages/ScanPage.xaml.cs`
- Modify: `src/UasSort.App/Pages/ShellPage.xaml.cs` (two `StagePages` entries), `src/UasSort.App/Services/UiFormat.cs` (`EnabledOpacity`), `src/UasSort.App/SelfTest/SelfTestChecks.cs` (register `page.card`), `src/UasSort.App/SelfTest/SelfTestChecks.Pages.cs` (add the check)

**Interfaces:**
- Consumes (registry Review VMs table): `CardStageVm` (`Rows`, `StatusText`, `Message`, `RescanCommand`, `Browse(string?)`), `CardRowVm` (`Text`, `KindText`, `IsDjiCard`, `IsWriteProtected`, `UseCommand`), `ShellVm.CanBrowse`, `ScanStageVm` (`PhaseText`, `Progress`, `IsIndeterminate`, `ErrorText`, `IsRunning`, `CancelCommand`), `ShellVm.RescanCommand`; `FolderPickerService` (Task 11.5).
- Produces (defined here): `CardPage` (`Vm`, `Shell`), `ScanPage` (`Vm`, `Shell`); selftest check `page.card`.

- [ ] **Step 1: Register the failing check**

Add `("page.card", PageCard),` to `SelfTestChecks.All`, and append to `SelfTestChecks.Pages.cs`:

```csharp
    private static async Task<SelfTestCheck> PageCard(SelfTestContext ctx)
    {
        // Under --selftest the Card stage lists no volumes (NoVolumes, Task 11.4), so it opens empty.
        if (!await WaitUntilAsync(() => CurrentPage<CardPage>(ctx) is { IsLoaded: true }, TimeSpan.FromSeconds(10)))
            return SelfTestCheck.Fail("page.card", $"Card stage not shown (stage {ctx.Services.Shell.Stage})");
        var page = CurrentPage<CardPage>(ctx)!;
        var browse = VisualTree.FindDescendant<Button>(page, b => (b.Content as string) == "Browse to folder…");
        var empty = page.Vm.Rows.Count == 0
                    && VisualTree.FindDescendant<TextBlock>(page, t => t.Text == page.Vm.StatusText && t.Text.Length > 0) is not null;
        return browse is { IsEnabled: true } && empty
            ? SelfTestCheck.Pass("page.card", "empty Card stage with Browse to folder…: " + page.Vm.StatusText)
            : SelfTestCheck.Fail("page.card", $"browse={browse?.IsEnabled} empty={empty}");
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only page.card`
Expected: `CS0246: The type or namespace name 'CardPage' could not be found` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```xml
<!-- src/UasSort.App/Pages/CardPage.xaml — Ref §9.1 Card: shown only for zero or several candidates -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.CardPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <Grid Padding="24" RowSpacing="12" MaxWidth="900">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <TextBlock Text="Choose a card" Style="{StaticResource TitleTextBlockStyle}" />
        <InfoBar Grid.Row="1" IsOpen="True" Severity="Error" IsClosable="False" Title="Can't use that folder"
                 Message="{x:Bind Vm.Message, Mode=OneWay}" Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.Message), Mode=OneWay}" />
        <Grid Grid.Row="2" RowSpacing="8">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <!-- StatusText: "No DJI card found…" (0 cards) or "N DJI cards found. Pick one…" (several); empty for exactly one -->
            <TextBlock Text="{x:Bind Vm.StatusText, Mode=OneWay}" Style="{StaticResource SubtitleTextBlockStyle}" TextWrapping="Wrap"
                       Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.StatusText), Mode=OneWay}" />
            <ItemsView x:Name="Volumes" Grid.Row="1" ItemsSource="{x:Bind Vm.Rows}" SelectionMode="None" IsItemInvokedEnabled="True"
                       ItemInvoked="OnVolumeInvoked" Visibility="{x:Bind ui:UiFormat.VisibleIfAny(Vm.Rows.Count), Mode=OneWay}">
                <ItemsView.ItemTemplate>
                    <DataTemplate x:DataType="rv:CardRowVm">
                        <ItemContainer>
                            <Grid Padding="12,8" ColumnSpacing="12" Opacity="{x:Bind ui:UiFormat.EnabledOpacity(IsDjiCard)}">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <FontIcon Glyph="&#xE7F1;" FontSize="24" />
                                <StackPanel Grid.Column="1">
                                    <TextBlock Text="{x:Bind Text}" Style="{StaticResource BodyStrongTextBlockStyle}" />
                                    <TextBlock Text="{x:Bind KindText}" Style="{StaticResource CaptionText}" />
                                </StackPanel>
                                <Border Grid.Column="2" Style="{StaticResource ChipBorder}" VerticalAlignment="Center"
                                        Visibility="{x:Bind ui:UiFormat.Visible(IsWriteProtected)}">
                                    <TextBlock Text="write-protected" Style="{StaticResource CaptionText}" />
                                </Border>
                            </Grid>
                        </ItemContainer>
                    </DataTemplate>
                </ItemsView.ItemTemplate>
            </ItemsView>
        </Grid>
        <StackPanel Grid.Row="3" Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
            <Button Content="Rescan" Command="{x:Bind Vm.RescanCommand}" ToolTipService.ToolTip="F5" />
            <Button Content="Browse to folder…" Click="OnBrowse" IsEnabled="{x:Bind Shell.CanBrowse, Mode=OneWay}" />
        </StackPanel>
    </Grid>
</Page>
```

```csharp
// src/UasSort.App/Pages/CardPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class CardPage : Page
{
    private MainWindow _window = null!;
    public CardPage() => InitializeComponent();
    public CardStageVm Vm { get; private set; } = null!;
    public ShellVm Shell { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (CardStageVm)args.Vm;                               // ShellVm.Current (= ShellVm.Card) on the Card stage
        _window = args.Window;
        Shell = _window.Services.Shell;
    }

    private void OnVolumeInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is CardRowVm row && row.UseCommand.CanExecute(null)) row.UseCommand.Execute(null);
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, null) is { } path) Vm.Browse(path);
    }
}
```

Add to `UiFormat` (Task 11.4 file):

```csharp
    /// <summary>Rows that can't be used (not a DJI card) are dimmed, not hidden (Ref §9.1 Card).</summary>
    public static double EnabledOpacity(bool enabled) => enabled ? 1.0 : 0.55;
```

```xml
<!-- src/UasSort.App/Pages/ScanPage.xaml — Ref §9.1 Scan; ErrorText covers a card removed during the scan (Ref §12) -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.ScanPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services">
    <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="12" Width="520">
        <TextBlock Text="Scanning" Style="{StaticResource TitleTextBlockStyle}" />
        <TextBlock x:Name="PhaseText" Text="{x:Bind Vm.PhaseText, Mode=OneWay}" TextTrimming="CharacterEllipsis" />
        <ProgressBar Maximum="1" Value="{x:Bind Vm.Progress, Mode=OneWay}" IsIndeterminate="{x:Bind Vm.IsIndeterminate, Mode=OneWay}"
                     Visibility="{x:Bind ui:UiFormat.Visible(Vm.IsRunning), Mode=OneWay}" />
        <InfoBar IsOpen="True" IsClosable="False" Severity="Error" Message="{x:Bind Vm.ErrorText, Mode=OneWay}"
                 Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.ErrorText), Mode=OneWay}">
            <InfoBar.ActionButton>
                <Button Content="Rescan" Command="{x:Bind Shell.RescanCommand}" />
            </InfoBar.ActionButton>
        </InfoBar>
        <Button Content="Cancel" Command="{x:Bind Vm.CancelCommand}" HorizontalAlignment="Right"
                Visibility="{x:Bind ui:UiFormat.Visible(Vm.IsRunning), Mode=OneWay}" />
    </StackPanel>
</Page>
```

```csharp
// src/UasSort.App/Pages/ScanPage.xaml.cs
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class ScanPage : Page
{
    public ScanPage() => InitializeComponent();
    public ScanStageVm Vm { get; private set; } = null!;
    public ShellVm Shell { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (ScanStageVm)args.Vm;                               // ShellVm.Current on the Scan stage
        Shell = args.Window.Services.Shell;
    }
}
```

Add to `ShellPage.StagePages`:

```csharp
        [Stage.Card] = typeof(CardPage),
        [Stage.Scan] = typeof(ScanPage),
```

- [ ] **Step 4: Run it and watch it pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only page.card,page.settings`
Expected: both checks `"status":"pass"`, `exit code 0`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: Card and Scan stage pages with Browse to folder

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.7: Map pane, first task — vendored MapLibre and the `.mjs` MIME check under the WebView2 virtual host

Ref §9.6 and §14 step 11: **before any other map work**, prove that `https://map.uas-sort.example/` serves MapLibre's ES modules with a JavaScript MIME type and that the page reaches `ready` and answers `ping` with `pong`. If it doesn't, apply the fallbacks **in order** (Step 5); each is complete below.

**Files:**
- Create: `tools/vendor-maplibre.ps1`
- Create (vendored output, committed): `src/UasSort.App/MapAssets/lib/manifest.json`, `lib/maplibre-gl.css`, `lib/maplibre-gl*.mjs`, `src/UasSort.App/MapAssets/fonts/Noto Sans Regular/{0-255,256-511}.pbf`, `fonts/Noto Sans Bold/{0-255,256-511}.pbf`
- Create: `src/UasSort.App/MapAssets/index.html`, `src/UasSort.App/MapAssets/map.js` (bootstrap; Task 11.8 replaces map.js)
- Create: `src/UasSort.App/MapHost/MapPane.xaml`, `MapHost/MapPane.xaml.cs` (folder `MapHost` = namespace `UasSort.App.MapHost`)
- Create: `src/UasSort.App/SelfTest/SelfTestChecks.Map.cs`
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.cs` (register `map.mime`, `map.ready`)
- Modify (only if Step 5 reaches fallback 2): `src/UasSort.Core/Ports/Ports.cs`, `src/UasSort.Platform/Shell/AppAssets.cs`, `tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs` (`BytesAssets`); Create: `tests/UasSort.Platform.Tests/Shell/AppAssetsMapAssetTests.cs`

**Interfaces:**
- Consumes: `AppServices` (Task 11.4: `WebView2DataDir`, `Platform.Shell.OpenHttps`, `Platform.Log` = Part 09's `FileLog` with `Info`/`Warn`/`Error`); Microsoft.Web.WebView2 1.0.4191.47 (`CoreWebView2Environment.CreateWithOptionsAsync`, `SetVirtualHostNameToFolderMapping`, `CallDevToolsProtocolMethodAsync`).
- Produces (defined here):
  - `tools/vendor-maplibre.ps1 [-RenameToJs] [-Leaflet]` — downloads the pinned npm tarball (maplibre-gl 6.11.2, or leaflet 1.9.4), verifies its `dist.integrity` sha512, copies the dist files into `MapAssets\lib\`, writes `lib\manifest.json` `{engine, version, entry, worker, integrity}`, downloads the four Noto Sans glyph ranges from OpenFreeMap's font server; never loads anything from a CDN at run time.
  - `sealed partial class MapPane : UserControl` (namespace `UasSort.App.MapHost`) — `const string Host = "map.uas-sort.example"`, `static readonly TimeSpan ReadyTimeout` (5 s), `bool IsReady`, `bool IsUnavailable`, `bool IsNavigated`, `event Action<string>? MessageReceived`, `event Action? Ready`, `Task InitializeAsync(AppServices services)`, `void PostJson(string json)` (any thread), `Task<string?> EvaluateAsync(string expression)`, `void ShowUnavailable(string reason)`, `Func<Uri?>? OpenInBrowserUri { get; set; }`, `void Close()`.
  - Selftest checks `map.mime` and `map.ready` (ready + ping/pong); helper `static Task<MapPane> StandaloneMapPaneAsync(SelfTestContext ctx)`.
  - Only if Step 5 reaches fallback 2 (registry decision 44): `Stream IAppAssets.OpenMapAsset(string relativePath)` in Part 02's port and Part 09's `AppAssets`, in this task's commit; the same commit adds the member (returning `Stream.Null`) to Part 04's test fake `BytesAssets` in `tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs`, the only other `IAppAssets` implementation.

- [ ] **Step 1: Write the failing checks**

Add `("map.mime", MapMime), ("map.ready", MapReady),` to `SelfTestChecks.All`, and create:

```csharp
// src/UasSort.App/SelfTest/SelfTestChecks.Map.cs
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    /// <summary>A MapPane overlaid on the shell (not the Review one), shared by the map checks, removed by the last of them.</summary>
    public static async Task<MapPane> StandaloneMapPaneAsync(SelfTestContext ctx)
    {
        if (ctx.Shared.TryGetValue("mapPane", out var existing)) return (MapPane)existing;
        var pane = new MapPane { Width = 640, Height = 420, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var messages = new List<string>();
        pane.MessageReceived += m => { lock (messages) messages.Add(m); };
        ctx.Shared["mapMessages"] = messages;
        var root = (Grid)ctx.Window.Shell!.Content;              // ShellPage's two-row grid (TitleBar, stage frame)
        Grid.SetRowSpan(pane, 2);
        root.Children.Add(pane);
        ctx.Shared["mapPane"] = pane;
        await pane.InitializeAsync(ctx.Services);
        return pane;
    }

    public static void RemoveStandaloneMapPane(SelfTestContext ctx)
    {
        if (!ctx.Shared.Remove("mapPane", out var p)) return;
        var pane = (MapPane)p;
        ((Grid)ctx.Window.Shell!.Content).Children.Remove(pane);
        pane.Close();
    }

    private static async Task<SelfTestCheck> MapMime(SelfTestContext ctx)
    {
        var pane = await StandaloneMapPaneAsync(ctx);
        await WaitUntilAsync(() => pane.IsNavigated, TimeSpan.FromSeconds(10));
        var answer = await pane.EvaluateAsync(
            "fetch('lib/manifest.json').then(r => r.json()).then(m => fetch('lib/' + m.entry))" +
            ".then(r => r.status + ' ' + (r.headers.get('content-type') || '(none)'))");
        bool ok = answer is not null && answer.StartsWith("200 ", StringComparison.Ordinal)
                  && (answer.Contains("text/javascript", StringComparison.OrdinalIgnoreCase)
                      || answer.Contains("application/javascript", StringComparison.OrdinalIgnoreCase));
        return ok ? SelfTestCheck.Pass("map.mime", "entry module served as " + answer)
                  : SelfTestCheck.Fail("map.mime", "entry module served as " + (answer ?? "(no answer)") + "; apply the Task 11.7 fallbacks in order");
    }

    private static async Task<SelfTestCheck> MapReady(SelfTestContext ctx)
    {
        var pane = await StandaloneMapPaneAsync(ctx);
        var messages = (List<string>)ctx.Shared["mapMessages"];
        try
        {
            if (!await WaitUntilAsync(() => pane.IsReady || pane.IsUnavailable, TimeSpan.FromSeconds(15)))
                return SelfTestCheck.Fail("map.ready", "no ready within 15 s; messages: " + string.Join(" | ", Snapshot(messages)));
            if (pane.IsUnavailable)
                return SelfTestCheck.Fail("map.ready", "map unavailable; messages: " + string.Join(" | ", Snapshot(messages)));
            pane.PostJson("""{"v":1,"type":"ping","n":1}""");
            bool pong = await WaitUntilAsync(() => Snapshot(messages).Any(IsPong1), TimeSpan.FromSeconds(5));
            var ready = Snapshot(messages).First(m => m.Contains("\"ready\"", StringComparison.Ordinal));
            return pong ? SelfTestCheck.Pass("map.ready", "ready " + ready + "; pong 1")
                        : SelfTestCheck.Fail("map.ready", "ready but no pong; messages: " + string.Join(" | ", Snapshot(messages)));
        }
        finally { RemoveStandaloneMapPane(ctx); }
    }

    private static List<string> Snapshot(List<string> list) { lock (list) return [.. list]; }

    private static bool IsPong1(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "pong"
               && doc.RootElement.TryGetProperty("n", out var n) && n.GetInt32() == 1;
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only map.mime,map.ready`
Expected: `CS0246: The type or namespace name 'MapPane' could not be found` → `BUILD FAILED` (the namespace exists through `Namespaces.cs`, the control does not yet).

- [ ] **Step 3: Implement — vendoring, page bootstrap and the host**

```powershell
# tools/vendor-maplibre.ps1 — pin and vendor the map engine into src\UasSort.App\MapAssets (Ref §9.6 Assets).
# Default: MapLibre GL JS 6.11.2 as ES modules (.mjs).
#   -RenameToJs : fallback 1 — rename every .mjs to .js, rewrite relative import specifiers, record the worker URL.
#   -Leaflet    : fallback 3 — Leaflet 1.9.4 instead of MapLibre.
param([switch]$RenameToJs, [switch]$Leaflet)
$ErrorActionPreference = 'Stop'
$repo   = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $repo 'src\UasSort.App\MapAssets'
$lib    = Join-Path $assets 'lib'
$fonts  = Join-Path $assets 'fonts'
$tmp    = Join-Path ([IO.Path]::GetTempPath()) ("uas-sort-vendor-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null

function Get-NpmPackage([string]$Name, [string]$Version) {
    $meta = Invoke-RestMethod "https://registry.npmjs.org/$Name/$Version"
    $tgz = Join-Path $tmp "$Name-$Version.tgz"
    Invoke-WebRequest $meta.dist.tarball -OutFile $tgz
    $actual = 'sha512-' + [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($tgz)))
    if ($actual -ne $meta.dist.integrity) { throw "Integrity mismatch for $Name@${Version}: $actual vs $($meta.dist.integrity)" }
    $dir = Join-Path $tmp $Name
    New-Item -ItemType Directory $dir | Out-Null
    & tar.exe -xzf $tgz -C $dir
    if ($LASTEXITCODE -ne 0) { throw "tar failed for $tgz" }
    [pscustomobject]@{ Dir = (Join-Path $dir 'package'); Integrity = $meta.dist.integrity }
}

try {
    if (Test-Path $lib) { Remove-Item $lib -Recurse -Force }
    New-Item -ItemType Directory $lib | Out-Null

    if ($Leaflet) {
        $p = Get-NpmPackage 'leaflet' '1.9.4'
        Copy-Item (Join-Path $p.Dir 'dist\leaflet.js'), (Join-Path $p.Dir 'dist\leaflet.css') $lib
        Copy-Item (Join-Path $p.Dir 'dist\images') (Join-Path $lib 'images') -Recurse
        $manifest = [ordered]@{ engine = 'leaflet'; version = '1.9.4'; entry = 'leaflet.js'; worker = $null; integrity = $p.Integrity }
    }
    else {
        $version = '6.11.2'
        $p = Get-NpmPackage 'maplibre-gl' $version
        $dist = Join-Path $p.Dir 'dist'
        if (-not (Test-Path (Join-Path $dist 'maplibre-gl.mjs'))) { throw 'dist\maplibre-gl.mjs is missing: the package layout changed; stop and re-check Ref §9.6' }
        Copy-Item (Join-Path $dist 'maplibre-gl.css') $lib
        Get-ChildItem $dist -Filter 'maplibre-gl*.mjs' | Copy-Item -Destination $lib
        $entry = 'maplibre-gl.mjs'; $worker = $null
        if ($RenameToJs) {
            foreach ($f in Get-ChildItem $lib -Filter '*.mjs') {
                $text = Get-Content -LiteralPath $f.FullName -Raw
                $text = [regex]::Replace($text, '(["''])\./([\w.-]+)\.mjs\1', '$1./$2.js$1')
                Set-Content -LiteralPath (Join-Path $lib ($f.BaseName + '.js')) -Value $text -NoNewline -Encoding utf8NoBOM
                Remove-Item -LiteralPath $f.FullName
            }
            $entry = 'maplibre-gl.js'
            $w = Get-ChildItem $lib -Filter '*worker*.js' | Select-Object -First 1
            if (-not $w) { throw 'No worker file after the rename' }
            $worker = 'lib/' + $w.Name
        }
        $manifest = [ordered]@{ engine = 'maplibre'; version = $version; entry = $entry; worker = $worker; integrity = $p.Integrity }
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $lib 'manifest.json') -Encoding utf8NoBOM

    foreach ($stack in 'Noto Sans Regular', 'Noto Sans Bold') {
        $d = Join-Path $fonts $stack
        New-Item -ItemType Directory $d -Force | Out-Null
        foreach ($range in '0-255', '256-511') {
            Invoke-WebRequest "https://tiles.openfreemap.org/fonts/$([uri]::EscapeDataString($stack))/$range.pbf" -OutFile (Join-Path $d "$range.pbf")
        }
    }
    Get-ChildItem $lib, $fonts -Recurse -File | ForEach-Object { '{0,10:N0}  {1}' -f $_.Length, $_.FullName.Substring($assets.Length + 1) }
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
```

Run it once: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\vendor-maplibre.ps1`. Expected: a listing of about 1.2 MB under `lib\` (`maplibre-gl.css`, `maplibre-gl.mjs`, `maplibre-gl-shared.mjs`, `maplibre-gl-worker.mjs`, `manifest.json`) and about 420 KB under `fonts\`.

```html
<!-- src/UasSort.App/MapAssets/index.html -->
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>uas-sort map</title>
<script>
  // First lines: forward every error to the host (Ref §2.7 #6), so a MIME rejection of the module is visible in the host log.
  (function () {
    const post = (m) => window.chrome?.webview?.postMessage(Object.assign({ v: 1 }, m));
    window.addEventListener('error', (e) => post({ type: 'error', base: null, message: String(e.message || e.type) + ' @' + (e.filename || '') + ':' + (e.lineno || 0) }));
    window.addEventListener('unhandledrejection', (e) => post({ type: 'error', base: null, message: 'unhandled rejection: ' + String(e.reason) }));
    window.__moduleFailed = () => post({ type: 'error', base: null, message: 'map.js module failed to load (MIME type or syntax)' });
  })();
</script>
<link rel="stylesheet" href="lib/maplibre-gl.css" onerror="this.remove()">
<style>
  html, body, #map { margin: 0; height: 100%; width: 100%; }
  body { background: #e9e6df; font: 12px "Segoe UI Variable", "Segoe UI", sans-serif; }
  body[data-theme="dark"] { background: #1f2328; }
  #badge { position: absolute; top: 8px; left: 8px; z-index: 2; padding: 3px 8px; border-radius: 4px; background: #000a; color: #fff; display: none; }
  .jump-label { background: #fffe; color: #222; border-radius: 3px; padding: 1px 4px; font-weight: 600; box-shadow: 0 1px 2px #0006;
                white-space: nowrap; pointer-events: none; }
</style>
</head>
<body>
<div id="badge"></div>
<div id="map"></div>
<script type="module" src="map.js" onerror="window.__moduleFailed()"></script>
</body>
</html>
```

```js
// src/UasSort.App/MapAssets/map.js — bootstrap for the MIME check (Task 11.8 replaces this file with the full map)
const bridge = window.chrome?.webview;
const post = (type, body = {}) => bridge?.postMessage({ v: 1, type, ...body });
const manifest = await (await fetch('lib/manifest.json')).json();
const mod = await import('./lib/' + manifest.entry);
const ml = mod.Map ? mod : mod.default;
if (manifest.worker) ml.setWorkerUrl(manifest.worker);
const webgl2 = !!document.createElement('canvas').getContext('webgl2');
bridge?.addEventListener('message', (ev) => { if (ev.data?.type === 'ping') post('pong', { n: ev.data.n }); });
if (!webgl2) post('ready', { maplibre: manifest.version, webgl2: false });
else {
  const map = new ml.Map({ container: 'map', style: { version: 8, sources: {}, layers: [] }, center: [-165.4, 64.5], zoom: 6 });
  map.once('load', () => post('ready', { maplibre: ml.getVersion?.() ?? manifest.version, webgl2: true }));
}
```

```xml
<!-- src/UasSort.App/MapHost/MapPane.xaml -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.MapHost.MapPane"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <WebView2 x:Name="MapView" />
        <StackPanel x:Name="UnavailablePanel" Visibility="Collapsed" Spacing="8" Padding="24"
                    HorizontalAlignment="Center" VerticalAlignment="Center">
            <TextBlock Text="Map unavailable" Style="{StaticResource SubtitleTextBlockStyle}" HorizontalAlignment="Center" />
            <TextBlock x:Name="UnavailableReason" Style="{StaticResource CaptionText}" TextWrapping="Wrap" MaxWidth="360" HorizontalAlignment="Center" />
            <Button x:Name="OpenInBrowserButton" Content="Open in browser" HorizontalAlignment="Center" Click="OnOpenInBrowser" />
        </StackPanel>
    </Grid>
</UserControl>
```

```csharp
// src/UasSort.App/MapHost/MapPane.xaml.cs — Ref §9.6 Host (WebView2 fenced to the virtual host)
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace UasSort.App.MapHost;

public sealed partial class MapPane : UserControl
{
    public const string Host = "map.uas-sort.example";
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private const string Origin = "https://" + Host + "/";

    private Task? _init;
    private AppServices? _services;
    private CoreWebView2Environment? _env;
    private DispatcherQueueTimer? _readyTimer;

    public MapPane() => InitializeComponent();

    public bool IsReady { get; private set; }
    public bool IsUnavailable { get; private set; }
    public bool IsNavigated { get; private set; }
    public Func<Uri?>? OpenInBrowserUri { get; set; }
    public CoreWebView2? Core => MapView.CoreWebView2;

    public event Action<string>? MessageReceived;
    public event Action? Ready;

    public Task InitializeAsync(AppServices services) => _init ??= InitCoreAsync(services);

    private async Task InitCoreAsync(AppServices services)
    {
        _services = services;
        _env = await CoreWebView2Environment.CreateWithOptionsAsync(null, services.WebView2DataDir, new CoreWebView2EnvironmentOptions());
        await MapView.EnsureCoreWebView2Async(_env);
        var core = MapView.CoreWebView2;
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;          // right-click goes to JS, then a WinUI MenuFlyout
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.AreDevToolsEnabled = Debugger.IsAttached;
        s.UserAgent = s.UserAgent + " uas-sort/" + (typeof(MapPane).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        core.Profile.PreferredColorScheme = ActualTheme == ElementTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "MapAssets"),
                                               CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal)) e.Cancel = true; };
        core.NavigationCompleted += (_, e) => IsNavigated = e.IsSuccess;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;                                // never a second browser window
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps) services.Platform.Shell.OpenHttps(u);
        };
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.WebMessageReceived += OnWebMessageReceived;     // registered before Navigate (Ref §2.7 #6)
        StartReadyTimer();
        core.Navigate(Origin + "index.html");
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.WebMessageAsJson;
        string? type = null;
        bool webgl2 = true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var t)) type = t.GetString();
            if (type == "ready" && doc.RootElement.TryGetProperty("webgl2", out var w)) webgl2 = w.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { _services?.Platform.Log.Warn("map: unparseable message " + json); }

        if (type is "error" or "tileError" or "baseUnavailable") _services?.Platform.Log.Warn("map: " + json);
        if (type == "ready")
        {
            if (!webgl2) ShowUnavailable("This PC can't run the map (no WebGL 2).");
            else { IsReady = true; HideUnavailable(); Ready?.Invoke(); }
        }
        MessageReceived?.Invoke(json);
    }

    private void StartReadyTimer()
    {
        _readyTimer = DispatcherQueue.CreateTimer();
        _readyTimer.Interval = ReadyTimeout;
        _readyTimer.IsRepeating = false;
        _readyTimer.Tick += (_, _) => { if (!IsReady) ShowUnavailable("The map didn't start within 5 seconds."); };
        _readyTimer.Start();
    }

    /// <summary>Posts one host→map message; callable from any thread (MapBridge's 10/s throttle posts from a timer thread).</summary>
    public void PostJson(string json)
    {
        if (DispatcherQueue.HasThreadAccess) MapView.CoreWebView2?.PostWebMessageAsJson(json);
        else DispatcherQueue.TryEnqueue(() => MapView.CoreWebView2?.PostWebMessageAsJson(json));
    }

    /// <summary>Evaluates a JS expression (awaiting a promise) through the DevTools protocol; returns the string value or null.</summary>
    public async Task<string?> EvaluateAsync(string expression)
    {
        if (MapView.CoreWebView2 is not { } core) return null;
        var request = JsonSerializer.Serialize(new EvalRequest(expression, true, true), MapPaneJson.Default.EvalRequest);
        var reply = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", request);
        using var doc = JsonDocument.Parse(reply);
        if (!doc.RootElement.TryGetProperty("result", out var r) || !r.TryGetProperty("value", out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }

    public void ShowUnavailable(string reason)
    {
        IsUnavailable = true;
        UnavailableReason.Text = reason;
        UnavailablePanel.Visibility = Visibility.Visible;
        MapView.Visibility = Visibility.Collapsed;
    }

    private void HideUnavailable()
    {
        IsUnavailable = false;
        UnavailablePanel.Visibility = Visibility.Collapsed;
        MapView.Visibility = Visibility.Visible;
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e)
    {
        var uri = OpenInBrowserUri?.Invoke() ?? new Uri("https://www.openstreetmap.org/");
        _services?.Platform.Shell.OpenHttps(uri);
    }

    public void Close()
    {
        _readyTimer?.Stop();
        MapView.Close();
    }
}

public sealed record EvalRequest(string Expression, bool AwaitPromise, bool ReturnByValue);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(EvalRequest))]
public partial class MapPaneJson : System.Text.Json.Serialization.JsonSerializerContext;
```

- [ ] **Step 4: Run the checks**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only map.mime,map.ready`
Expected when the virtual host serves `.mjs` as JavaScript: `map.mime` pass with detail `entry module served as 200 text/javascript` (or `application/javascript`), `map.ready` pass with `ready {"v":1,"type":"ready","maplibre":"6.11.2","webgl2":true}; pong 1`, `exit code 0`. Record the observed Content-Type in the commit message: it settles the Ref §9.6 UNVERIFIED item.

- [ ] **Step 5: Only if `map.mime` or `map.ready` fails — apply the fallbacks in this order, re-running Step 4 after each, and stop at the first that passes**

**Fallback 1 — `.js` files and `setWorkerUrl`.** Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\vendor-maplibre.ps1 -RenameToJs`. `lib\manifest.json` now says `"entry": "maplibre-gl.js", "worker": "lib/maplibre-gl-worker.js"`; `map.js` already imports `manifest.entry` and calls `setWorkerUrl(manifest.worker)`, so no code changes. Re-run Step 4; expected `map.mime` detail `200 text/javascript`.

**Fallback 2 — serve `lib/*` through `WebResourceRequested` with an explicit Content-Type.** Keep the fallback-1 files. Add `OpenMapAsset` to Part 02's port and Part 09's `AppAssets` in this task's commit (registry decision 44; nowhere else):

```csharp
// src/UasSort.Core/Ports/Ports.cs — the IAppAssets line becomes
public interface IAppAssets { Stream OpenPlaces(); Stream OpenSelfTest(string name); Stream OpenMapAsset(string relativePath); }
```

```csharp
// src/UasSort.Platform/Shell/AppAssets.cs — add inside Part 09's AppAssets(string appDir, Assembly selfTestAssembly)
    /// <summary>appDir\MapAssets\relativePath, read-only; a rooted path or ".." throws before any IO.</summary>
    public Stream OpenMapAsset(string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Split('/', '\\').Contains(".."))
            throw new UnsafeIoException("Map asset path escapes MapAssets: " + relativePath);
        var full = Path.Join(appDir, "MapAssets", relativePath.Replace('/', Path.DirectorySeparatorChar));
#pragma warning disable RS0030 // IO layer: read-only app-folder asset for the map's WebResourceRequested fallback (Ref §9.6)
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
#pragma warning restore RS0030
    }
```

```csharp
// tests/UasSort.Platform.Tests/Shell/AppAssetsMapAssetTests.cs
namespace UasSort.Platform.Tests.Shell;

public sealed class AppAssetsMapAssetTests
{
    [Fact]
    public void OpenMapAsset_RefusesPathsThatLeaveMapAssets()
    {
        using var temp = new TestTempDir();
        var assets = new AppAssets(temp.FullPath, typeof(AppAssetsMapAssetTests).Assembly);
        Assert.Throws<UnsafeIoException>(() => assets.OpenMapAsset(@"..\x.js"));
        Assert.Throws<UnsafeIoException>(() => assets.OpenMapAsset(@"C:\x.js"));
    }
}
```

The only other `IAppAssets` implementation is Part 04's test fake `BytesAssets` (Task 04.10, nested in `tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs`); in the same commit add this member to it, directly after its `OpenSelfTest` line, so `UasSort.Core.Tests` still compiles:

```csharp
// tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs — inside sealed class BytesAssets(byte[] places) : IAppAssets
        public Stream OpenMapAsset(string relativePath) => Stream.Null;
```

Then in `MapPane.InitCoreAsync`, directly before `StartReadyTimer();`:

```csharp
        core.AddWebResourceRequestedFilter(Origin + "lib/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var rel = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');       // "lib/maplibre-gl.js"
            if (!rel.EndsWith(".js", StringComparison.Ordinal) && !rel.EndsWith(".mjs", StringComparison.Ordinal)) return;
            var stream = services.Platform.Assets.OpenMapAsset(rel);
            e.Response = _env!.CreateWebResourceResponse(stream.AsRandomAccessStream(), 200, "OK",
                                                         "Content-Type: text/javascript; charset=utf-8");
        };
```

Run `dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*AppAssetsMapAssetTests"` (`Passed: 1`), then re-run Step 4.

**Fallback 3 — Leaflet 1.9.4.** Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\vendor-maplibre.ps1 -Leaflet` (manifest `engine: "leaflet"`), and in Task 11.8 use the Leaflet `map.js` given there (Step 3b) instead of the MapLibre one. For this task's bootstrap, replace `map.js` with:

```js
// src/UasSort.App/MapAssets/map.js — Leaflet bootstrap (fallback 3)
const bridge = window.chrome?.webview;
const post = (type, body = {}) => bridge?.postMessage({ v: 1, type, ...body });
const manifest = await (await fetch('lib/manifest.json')).json();
await new Promise((ok, fail) => { const s = document.createElement('script'); s.src = 'lib/' + manifest.entry; s.onload = ok; s.onerror = () => fail(new Error('leaflet failed to load')); document.head.appendChild(s); });
bridge?.addEventListener('message', (ev) => { if (ev.data?.type === 'ping') post('pong', { n: ev.data.n }); });
const map = L.map('map', { preferCanvas: true }).setView([64.5, -165.4], 6);
post('ready', { maplibre: 'leaflet ' + L.version, webgl2: true });
```

and change the `map.mime` check's accepted answer to also accept `200 text/javascript` for `leaflet.js` (it fetches `manifest.entry`, so no code change is needed). If all three fail, stop and raise it with the user (Ref §2.2 rule: never silently degrade further).

- [ ] **Step 6: Commit**

```bash
git add tools/vendor-maplibre.ps1 src/UasSort.App/MapAssets src/UasSort.App/MapHost src/UasSort.App/SelfTest
# only when fallback 2 was applied: git add src/UasSort.Core/Ports/Ports.cs src/UasSort.Platform/Shell/AppAssets.cs tests/UasSort.Platform.Tests/Shell/AppAssetsMapAssetTests.cs tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs
git commit -m "feat: vendored MapLibre 6.11.2 + fonts, fenced WebView2 map host, .mjs MIME check

MIME check: entry module served as text/javascript (replace with the detail map.mime printed, and name any fallback applied).

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.8: Map pane — full `map.js` (Ref §9.6 protocol) and the host side of the bridge

**Files:**
- Replace: `src/UasSort.App/MapAssets/map.js`
- Delete (`git rm`): `src/UasSort.App/MapAssets/probe.html` (Part 01's probe page, unused since Task 11.4)
- Modify: `src/UasSort.App/MapHost/MapPane.xaml.cs` (bridge attachment, theme, online, context menu, focus return)
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.Map.cs` (add `map.draw`), `SelfTestChecks.cs` (register it)

**Interfaces:**
- Consumes: `MapBridge` (registry: `MapBridge(Action<string> post, IReviewLog log, TimeProvider time)`, `Dispatch(string json)`, `Send(HostToMap)`), `MapSetTheme(string Theme)`; the Ref §9.6 message table and golden JSON (host→map `init`, `setData`, `select`, `setRadius`, `setBase`, `setTheme`, `fit`, `ping`; map→host `ready`, `pong`, `click`, `clickEmpty`, `contextMenu`, `tileError`, `baseUnavailable`, `error`); `Windows.Networking.Connectivity.NetworkInformation`.
- Produces (defined here):
  - `map.js` implementing every Ref §9.6 message; `window.__uas.stats()` → `{items, groups, jumps, radius, base, rev, selected, badge}` (selftest hook).
  - `MapPane.Attach(MapBridge bridge)`, `MapPane.Detach()`, `event Action<IReadOnlyList<string>, Windows.Foundation.Point>? ContextMenuRequested`, `event Action? FocusReturnRequested`, `event Action<bool>? OnlineChanged`, `static bool IsOnline()`, `static Uri OsmUri(GeoPoint? center)`, `string ThemeName`.
  - Selftest check `map.draw`.

- [ ] **Step 1: Write the failing check**

Add `("map.draw", MapDraw),` to `SelfTestChecks.All` (after `map.ready`), and append to `SelfTestChecks.Map.cs`:

```csharp
    private static async Task<SelfTestCheck> MapDraw(SelfTestContext ctx)
    {
        var pane = await StandaloneMapPaneAsync(ctx);
        try
        {
            if (!await WaitUntilAsync(() => pane.IsReady, TimeSpan.FromSeconds(15)))
                return SelfTestCheck.Fail("map.draw", "no ready");
            // Offline init (no tile traffic in the selftest), then the Ref §9.6 golden setData + select.
            pane.PostJson("""{"v":1,"type":"init","config":{"streetsStyleUrl":"https://tiles.openfreemap.org/styles/liberty","streetsDarkStyleUrl":"https://tiles.openfreemap.org/styles/dark","satelliteUrl":"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"},"base":"streets","radiusMiles":50,"online":false,"theme":"dark"}""");
            pane.PostJson("""{"v":1,"type":"setData","rev":7,"items":[{"id":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","lon":-153.738973,"lat":57.550442,"kind":"video"},{"id":"DCIM/DJI_001/DJI_20260927141000_0129_D.MP4","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","lon":-153.7409,"lat":57.5415,"kind":"videoNoGps"}],"groups":[{"id":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","color":"#1F77B4","center":[-153.7409,57.5415],"label":"Sep 27 · 13 clips"}],"jumps":[{"from":[-164.2657,64.6935],"to":[-165.3696,64.5627],"label":"34 mi · 21 h"}]}""");
            pane.PostJson("""{"v":1,"type":"select","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","itemIds":["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"],"fit":true,"bbox":[-153.76,57.53,-153.72,57.56]}""");
            pane.PostJson("""{"v":1,"type":"setRadius","radiusMiles":25}""");
            var stats = await PollEvaluateAsync(pane, "JSON.stringify(window.__uas.stats())", s =>
                s is not null && s.Contains("\"items\":2", StringComparison.Ordinal) && s.Contains("\"radius\":1", StringComparison.Ordinal)
                && s.Contains("\"jumps\":1", StringComparison.Ordinal) && s.Contains("\"selected\":1", StringComparison.Ordinal)
                && s.Contains("\"base\":\"none\"", StringComparison.Ordinal), TimeSpan.FromSeconds(8));
            bool ok = stats is not null && stats.Contains("\"base\":\"none\"", StringComparison.Ordinal)
                      && stats.Contains("\"items\":2", StringComparison.Ordinal) && stats.Contains("\"radius\":1", StringComparison.Ordinal);
            return ok ? SelfTestCheck.Pass("map.draw", "offline canvas drew " + stats)
                      : SelfTestCheck.Fail("map.draw", "stats " + (stats ?? "(none)"));
        }
        finally { RemoveStandaloneMapPane(ctx); }
    }

    /// <summary>Polls a JS expression from the UI thread without blocking it.</summary>
    private static async Task<string?> PollEvaluateAsync(MapPane pane, string expression, Func<string?, bool> done, TimeSpan timeout)
    {
        var until = TimeProvider.System.GetTimestamp() + (long)(timeout.TotalSeconds * TimeProvider.System.TimestampFrequency);
        string? last = null;
        while (TimeProvider.System.GetTimestamp() < until)
        {
            last = await pane.EvaluateAsync(expression);
            if (done(last)) return last;
            await Task.Delay(100);
        }
        return last;
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only map.draw`
Expected: `"name":"map.draw","status":"fail","detail":"stats (none)"` (the bootstrap `map.js` has no `window.__uas`), `exit code 1`.

- [ ] **Step 3: Implement the map page (MapLibre)**

```js
// src/UasSort.App/MapAssets/map.js — uas-sort map pane: MapLibre GL JS 6.11.2 (vendored ESM), Ref §9.6 protocol v1.
// Port of docs/research/spikes/map-pane/web/map.js. C# computes every distance, label and colour; JS only draws
// (the radius circle uses the same Earth radius as the C# haversine).
const PROTOCOL = 1;
const EARTH_R_M = 6371008.8;
const M_PER_MI = 1609.344;
const LOCAL_GLYPHS = location.href.replace(/[^/]*$/, '') + 'fonts/{fontstack}/{range}.pbf';
const OVERLAY_SOURCES = new Set(['radius', 'jumps', 'items']);

// ---------- host bridge (WebView2, or a shim in a plain browser for debugging) ----------
const outbox = (window.__outbox = []);
const bridge = window.chrome?.webview ?? {
  postMessage: (m) => outbox.push(m),
  addEventListener: (_t, fn) => { window.__hostSend = (data) => fn({ data }); },
};
const post = (type, body = {}) => bridge.postMessage({ v: PROTOCOL, type, ...body });

// ---------- engine (entry and worker from lib/manifest.json, written by tools/vendor-maplibre.ps1) ----------
const manifest = await (await fetch('lib/manifest.json')).json();
const mod = await import('./lib/' + manifest.entry);
const ml = mod.Map ? mod : mod.default;
if (manifest.worker) ml.setWorkerUrl(manifest.worker);

// ---------- state ----------
let cfg = null;
let theme = 'light';
let online = true;
let requestedBase = 'none';
let baseKey = 'none';
let baseErrors = 0;
let tileErrorSent = false;
let radiusMiles = 50;
let data = { rev: 0, items: [], groups: [], jumps: [] };
let selection = { groupId: null, itemIds: [] };
let badgeText = '';
const jumpMarkers = [];
const empty = () => ({ type: 'FeatureCollection', features: [] });

function setBadge(t) {
  badgeText = t;
  const b = document.getElementById('badge');
  b.textContent = t;
  b.style.display = t ? 'block' : 'none';
}

function ring(lon, lat, meters, n = 128) {
  const d = meters / EARTH_R_M, la1 = lat * Math.PI / 180, lo1 = lon * Math.PI / 180, pts = [];
  for (let i = 0; i <= n; i++) {
    const b = 2 * Math.PI * i / n;
    const la2 = Math.asin(Math.sin(la1) * Math.cos(d) + Math.cos(la1) * Math.sin(d) * Math.cos(b));
    const lo2 = lo1 + Math.atan2(Math.sin(b) * Math.sin(d) * Math.cos(la1), Math.cos(d) - Math.sin(la1) * Math.sin(la2));
    pts.push([lo2 * 180 / Math.PI, la2 * 180 / Math.PI]);
  }
  return { type: 'Feature', properties: {}, geometry: { type: 'Polygon', coordinates: [pts] } };
}

function itemsFC() {
  const color = Object.fromEntries(data.groups.map((g) => [g.id, g.color]));
  return { type: 'FeatureCollection', features: data.items.map((it) => ({
    type: 'Feature', id: it.id,
    properties: { id: it.id, g: it.groupId, color: color[it.groupId] ?? '#888888', kind: it.kind },
    geometry: { type: 'Point', coordinates: [it.lon, it.lat] } })) };
}
function radiusFC() {
  const g = data.groups.find((x) => x.id === selection.groupId);
  return g && g.center ? { type: 'FeatureCollection', features: [ring(g.center[0], g.center[1], radiusMiles * M_PER_MI)] } : empty();
}
function jumpsFC() {
  return { type: 'FeatureCollection', features: data.jumps.map((j) => ({
    type: 'Feature', properties: { label: j.label }, geometry: { type: 'LineString', coordinates: [j.from, j.to] } })) };
}

function overlay() {
  const noGps = ['==', ['get', 'kind'], 'videoNoGps'];
  const sel = ['boolean', ['feature-state', 'sel'], false];
  return {
    sources: {
      radius: { type: 'geojson', data: radiusFC() },
      jumps: { type: 'geojson', data: jumpsFC() },
      items: { type: 'geojson', data: itemsFC(), promoteId: 'id' },
    },
    layers: [
      { id: 'radius-fill', type: 'fill', source: 'radius', paint: { 'fill-color': '#4aa3ff', 'fill-opacity': 0.08 } },
      { id: 'radius-line', type: 'line', source: 'radius', paint: { 'line-color': '#4aa3ff', 'line-width': 2, 'line-dasharray': [3, 2] } },
      { id: 'jumps', type: 'line', source: 'jumps', paint: { 'line-color': '#ffcc33', 'line-width': 1.5, 'line-dasharray': [2, 2] } },
      { id: 'items', type: 'circle', source: 'items',
        paint: {
          'circle-color': ['case', noGps, 'rgba(0,0,0,0)', ['get', 'color']],
          'circle-radius': ['case', sel, 7, 5],
          'circle-stroke-color': ['case', noGps, ['get', 'color'], '#ffffff'],
          'circle-stroke-width': ['case', sel, 2.5, ['case', noGps, 2, 1]],
        } },
    ],
  };
}

function satelliteAttribution(url) {
  return url.includes('nationalmap.gov')
    ? 'Imagery: USGS The National Map / USDA NAIP'
    : 'Imagery: Esri, Vantor, Earthstar Geographics, and the GIS User Community | Powered by Esri';
}

async function baseParts(key) {
  if (key === 'streets') {
    const url = theme === 'dark' ? cfg.streetsDarkStyleUrl : cfg.streetsStyleUrl;
    const res = await fetch(url);
    if (!res.ok) throw new Error('style HTTP ' + res.status);
    const s = await res.json();
    return { sources: s.sources, layers: s.layers, glyphs: s.glyphs, sprite: s.sprite };
  }
  if (key === 'satellite') {
    return { sources: { base: { type: 'raster', tiles: [cfg.satelliteUrl], tileSize: 256, maxzoom: 19,
                                attribution: satelliteAttribution(cfg.satelliteUrl) } },
             layers: [{ id: 'base', type: 'raster', source: 'base' }] };
  }
  return { sources: {}, layers: [] };
}

async function buildStyle(key) {
  let base;
  try { base = await baseParts(key); }
  catch (e) {
    post('baseUnavailable', { base: key, message: String(e?.message ?? e) });
    setBadge('Offline: base map unavailable');
    key = 'none';
    base = await baseParts('none');
  }
  baseKey = key; baseErrors = 0; tileErrorSent = false;
  const ov = overlay();
  const hasBackground = base.layers.some((l) => l.type === 'background');
  const bg = { id: 'bg', type: 'background', paint: { 'background-color': theme === 'dark' ? '#2b3036' : '#e9e6df' } };
  return {
    version: 8,
    glyphs: base.glyphs ?? LOCAL_GLYPHS,
    ...(base.sprite ? { sprite: base.sprite } : {}),
    sources: { ...base.sources, ...ov.sources },
    layers: [...(hasBackground ? [] : [bg]), ...base.layers, ...ov.layers],
  };
}

async function applyBase(key) {
  requestedBase = key;
  map.setStyle(await buildStyle(online ? key : 'none'), { diff: false });
}

let lastSel = [];
function applySelectionState() {
  for (const id of lastSel) map.setFeatureState({ source: 'items', id }, { sel: false });
  lastSel = selection.itemIds.slice();
  for (const id of lastSel) map.setFeatureState({ source: 'items', id }, { sel: true });
}

function refreshSources() {
  if (!map.getSource('items')) return;                 // not isStyleLoaded(): false while tiles load (Ref §9.6)
  map.getSource('items').setData(itemsFC());
  map.getSource('radius').setData(radiusFC());
  map.getSource('jumps').setData(jumpsFC());
  applySelectionState();
  jumpMarkers.splice(0).forEach((m) => m.remove());
  for (const j of data.jumps) {
    const el = document.createElement('div');
    el.className = 'jump-label';
    el.textContent = j.label;
    jumpMarkers.push(new ml.Marker({ element: el }).setLngLat([(j.from[0] + j.to[0]) / 2, (j.from[1] + j.to[1]) / 2]).addTo(map));
  }
}

const fit = (bbox, animate) => map.fitBounds([[bbox[0], bbox[1]], [bbox[2], bbox[3]]], { padding: 40, maxZoom: 14, duration: animate ? 300 : 0 });

// ---------- WebGL 2 gate ----------
const webgl2 = !!document.createElement('canvas').getContext('webgl2');
if (!webgl2) {
  post('ready', { maplibre: manifest.version, webgl2: false });
  throw new Error('no WebGL 2');                        // host shows "Map unavailable"
}

// ---------- map ----------
const map = new ml.Map({
  container: 'map', style: { version: 8, glyphs: LOCAL_GLYPHS, sources: {}, layers: [] },
  center: [-165.4, 64.5], zoom: 6, attributionControl: { compact: false }, dragRotate: false, pitchWithRotate: false,
});
map.touchZoomRotate.disableRotation();
map.addControl(new ml.NavigationControl({ showCompass: false }), 'top-right');
map.addControl(new ml.ScaleControl({ unit: 'imperial', maxWidth: 120 }), 'bottom-left');
map.on('style.load', refreshSources);

const featuresAt = (point) => (map.getLayer('items') ? map.queryRenderedFeatures(point, { layers: ['items'] }) : []);
map.on('click', (e) => {
  const f = featuresAt(e.point);
  const mods = { ctrl: !!e.originalEvent.ctrlKey, shift: !!e.originalEvent.shiftKey };
  if (f.length) post('click', { itemIds: [...new Set(f.map((x) => x.properties.id))], groupId: f[0].properties.g, ...mods });
  else post('clickEmpty', { itemIds: [], groupId: null, ...mods });
});
map.on('contextmenu', (e) => {
  const f = featuresAt(e.point);
  if (f.length) post('contextMenu', { itemIds: [...new Set(f.map((x) => x.properties.id))], x: Math.round(e.point.x), y: Math.round(e.point.y) });
});
map.on('mouseenter', 'items', () => (map.getCanvas().style.cursor = 'pointer'));
map.on('mouseleave', 'items', () => (map.getCanvas().style.cursor = ''));
map.on('error', (e) => {
  const message = String(e.error?.message ?? e.error ?? 'map error');
  if (e.sourceId && !OVERLAY_SOURCES.has(e.sourceId)) {
    baseErrors++;
    if (!tileErrorSent) { tileErrorSent = true; post('tileError', { base: baseKey, message }); }
    if (baseErrors === 4) {                            // Ref §9.6: 4 tile errors = offline → overlays without a base
      post('baseUnavailable', { base: baseKey, message: '4 tile errors' });
      setBadge('Offline: base map unavailable');
      map.setStyle(buildStyleSync('none'), { diff: false });
      baseKey = 'none';
    }
  } else {
    post('error', { base: null, message });
  }
});

function buildStyleSync(key) {                         // 'none' needs no fetch
  const ov = overlay();
  return { version: 8, glyphs: LOCAL_GLYPHS, sources: { ...ov.sources },
           layers: [{ id: 'bg', type: 'background', paint: { 'background-color': theme === 'dark' ? '#2b3036' : '#e9e6df' } }, ...ov.layers] };
}

// ---------- host → map ----------
bridge.addEventListener('message', async (ev) => {
  const m = ev.data;
  if (!m || m.v !== PROTOCOL) { post('error', { base: null, message: 'bad protocol version' }); return; }
  switch (m.type) {
    case 'init':
      cfg = m.config; radiusMiles = m.radiusMiles; theme = m.theme; online = m.online;
      document.body.dataset.theme = theme;
      setBadge(online || m.base === 'none' ? '' : 'Offline: base map unavailable');
      await applyBase(m.base);
      break;
    case 'setData':
      if ((m.rev ?? 0) < (data.rev ?? 0)) break;       // an older derive never overwrites a newer one
      data = { rev: m.rev ?? 0, items: m.items ?? [], groups: m.groups ?? [], jumps: m.jumps ?? [] };
      refreshSources();
      break;
    case 'select':
      selection = { groupId: m.groupId ?? null, itemIds: m.itemIds ?? [] };
      refreshSources();
      if (m.fit && m.bbox) fit(m.bbox, true);
      break;
    case 'setRadius':
      radiusMiles = m.radiusMiles;
      map.getSource('radius')?.setData(radiusFC());
      break;
    case 'setBase':
      if (!online && m.base !== 'none') { setBadge('Offline: base map unavailable'); requestedBase = m.base; break; }
      setBadge('');
      await applyBase(m.base);
      break;
    case 'setTheme':
      theme = m.theme;
      document.body.dataset.theme = theme;
      if (baseKey === 'streets') await applyBase('streets');
      else if (map.getLayer('bg')) map.setPaintProperty('bg', 'background-color', theme === 'dark' ? '#2b3036' : '#e9e6df');
      break;
    case 'fit':
      if (m.bbox) fit(m.bbox, true);
      break;
    case 'ping':
      post('pong', { n: m.n });
      break;
    default:
      post('error', { base: null, message: 'unknown message type ' + m.type });
  }
});

window.__uas = {
  stats: () => ({ items: data.items.length, groups: data.groups.length, jumps: data.jumps.length,
                  radius: radiusFC().features.length, base: baseKey, rev: data.rev ?? 0,
                  selected: selection.itemIds.length, badge: badgeText }),
};

map.once('load', () => post('ready', { maplibre: ml.getVersion?.() ?? manifest.version, webgl2: true }));
```

**Step 3b (only when Task 11.7 ended on fallback 3, manifest `engine: "leaflet"`)** — use this `map.js` instead; same protocol, same `window.__uas.stats()`:

```js
// src/UasSort.App/MapAssets/map.js — Leaflet 1.9.4 fallback (Ref §9.6 fallback 3); same protocol v1.
const PROTOCOL = 1, EARTH_R_M = 6371008.8, M_PER_MI = 1609.344;
const bridge = window.chrome?.webview ?? { postMessage: (m) => (window.__outbox ??= []).push(m), addEventListener: (_t, fn) => { window.__hostSend = (d) => fn({ data: d }); } };
const post = (type, body = {}) => bridge.postMessage({ v: PROTOCOL, type, ...body });
const manifest = await (await fetch('lib/manifest.json')).json();
const css = document.createElement('link'); css.rel = 'stylesheet'; css.href = 'lib/leaflet.css'; document.head.appendChild(css);
await new Promise((ok, fail) => { const s = document.createElement('script'); s.src = 'lib/' + manifest.entry; s.onload = ok; s.onerror = () => fail(new Error('leaflet failed to load')); document.head.appendChild(s); });

let cfg = null, theme = 'light', online = true, baseKey = 'none', baseLayer = null, baseErrors = 0, tileErrorSent = false;
let radiusMiles = 50, data = { rev: 0, items: [], groups: [], jumps: [] }, selection = { groupId: null, itemIds: [] }, badgeText = '';
const map = L.map('map', { preferCanvas: true, zoomControl: true }).setView([64.5, -165.4], 6);
L.control.scale({ imperial: true, metric: false }).addTo(map);
const overlays = L.layerGroup().addTo(map);
const setBadge = (t) => { badgeText = t; const b = document.getElementById('badge'); b.textContent = t; b.style.display = t ? 'block' : 'none'; };
function ringLatLngs(lon, lat, meters, n = 128) {
  const d = meters / EARTH_R_M, la1 = lat * Math.PI / 180, lo1 = lon * Math.PI / 180, pts = [];
  for (let i = 0; i <= n; i++) {
    const b = 2 * Math.PI * i / n;
    const la2 = Math.asin(Math.sin(la1) * Math.cos(d) + Math.cos(la1) * Math.sin(d) * Math.cos(b));
    const lo2 = lo1 + Math.atan2(Math.sin(b) * Math.sin(d) * Math.cos(la1), Math.cos(d) - Math.sin(la1) * Math.sin(la2));
    pts.push([la2 * 180 / Math.PI, lo2 * 180 / Math.PI]);
  }
  return pts;
}
function setBase(key) {
  if (baseLayer) { map.removeLayer(baseLayer); baseLayer = null; }
  baseKey = online ? key : 'none'; baseErrors = 0; tileErrorSent = false;
  document.body.style.background = theme === 'dark' ? '#2b3036' : '#e9e6df';
  if (baseKey === 'none') return;
  const url = baseKey === 'streets' ? 'https://tile.openstreetmap.org/{z}/{x}/{y}.png' : cfg.satelliteUrl;
  const attribution = baseKey === 'streets' ? '&copy; OpenStreetMap contributors'
    : (cfg.satelliteUrl.includes('nationalmap.gov') ? 'Imagery: USGS The National Map / USDA NAIP' : 'Imagery: Esri, Vantor, Earthstar Geographics, and the GIS User Community | Powered by Esri');
  baseLayer = L.tileLayer(url, { maxZoom: 19, attribution }).addTo(map);
  baseLayer.on('tileerror', () => {
    baseErrors++;
    if (!tileErrorSent) { tileErrorSent = true; post('tileError', { base: baseKey, message: 'tile error' }); }
    if (baseErrors === 4) { post('baseUnavailable', { base: baseKey, message: '4 tile errors' }); setBadge('Offline: base map unavailable'); setBase('none'); }
  });
}
function redraw() {
  overlays.clearLayers();
  const color = Object.fromEntries(data.groups.map((g) => [g.id, g.color]));
  const g = data.groups.find((x) => x.id === selection.groupId);
  if (g?.center) L.polygon(ringLatLngs(g.center[0], g.center[1], radiusMiles * M_PER_MI), { color: '#4aa3ff', weight: 2, dashArray: '6 4', fillOpacity: 0.08, interactive: false }).addTo(overlays);
  for (const j of data.jumps)
    L.polyline([[j.from[1], j.from[0]], [j.to[1], j.to[0]]], { color: '#ffcc33', weight: 1.5, dashArray: '4 4' })
      .bindTooltip(j.label, { permanent: true, direction: 'center', className: 'jump-label' }).addTo(overlays);
  for (const it of data.items) {
    const c = color[it.groupId] ?? '#888888', sel = selection.itemIds.includes(it.id), noGps = it.kind === 'videoNoGps';
    const m = L.circleMarker([it.lat, it.lon], { radius: sel ? 7 : 5, color: noGps ? c : '#ffffff', weight: sel ? 2.5 : (noGps ? 2 : 1),
                                                fillColor: c, fillOpacity: noGps ? 0 : 1 }).addTo(overlays);
    m.on('click', (e) => { L.DomEvent.stopPropagation(e); post('click', { itemIds: [it.id], groupId: it.groupId, ctrl: !!e.originalEvent.ctrlKey, shift: !!e.originalEvent.shiftKey }); });
    m.on('contextmenu', (e) => { L.DomEvent.stopPropagation(e); post('contextMenu', { itemIds: [it.id], x: Math.round(e.containerPoint.x), y: Math.round(e.containerPoint.y) }); });
  }
}
map.on('click', (e) => post('clickEmpty', { itemIds: [], groupId: null, ctrl: !!e.originalEvent.ctrlKey, shift: !!e.originalEvent.shiftKey }));
const fit = (b) => map.fitBounds([[b[1], b[0]], [b[3], b[2]]], { padding: [40, 40], maxZoom: 14 });
bridge.addEventListener('message', (ev) => {
  const m = ev.data;
  if (!m || m.v !== PROTOCOL) { post('error', { base: null, message: 'bad protocol version' }); return; }
  switch (m.type) {
    case 'init': cfg = m.config; radiusMiles = m.radiusMiles; theme = m.theme; online = m.online;
      setBadge(online || m.base === 'none' ? '' : 'Offline: base map unavailable'); setBase(m.base); redraw(); break;
    case 'setData': if ((m.rev ?? 0) < (data.rev ?? 0)) break; data = { rev: m.rev ?? 0, items: m.items ?? [], groups: m.groups ?? [], jumps: m.jumps ?? [] }; redraw(); break;
    case 'select': selection = { groupId: m.groupId ?? null, itemIds: m.itemIds ?? [] }; redraw(); if (m.fit && m.bbox) fit(m.bbox); break;
    case 'setRadius': radiusMiles = m.radiusMiles; redraw(); break;
    case 'setBase': if (!online && m.base !== 'none') { setBadge('Offline: base map unavailable'); break; } setBadge(''); setBase(m.base); break;
    case 'setTheme': theme = m.theme; setBase(baseKey); break;
    case 'fit': if (m.bbox) fit(m.bbox); break;
    case 'ping': post('pong', { n: m.n }); break;
    default: post('error', { base: null, message: 'unknown message type ' + m.type });
  }
});
window.__uas = { stats: () => ({ items: data.items.length, groups: data.groups.length, jumps: data.jumps.length,
  radius: data.groups.some((x) => x.id === selection.groupId && x.center) ? 1 : 0, base: baseKey, rev: data.rev ?? 0,
  selected: selection.itemIds.length, badge: badgeText }) };
post('ready', { maplibre: 'leaflet ' + L.version, webgl2: true });
```

- [ ] **Step 4: Implement the host side of the bridge**

Registry Part 11 item 14: the page that owns a `ReviewVm` builds `new MapBridge(json => pane.PostJson(json), log, clock)`, calls `pane.Attach(bridge)`, sends `MapProjection.Init(...)` once the pane is ready and then assigns `review.Map = bridge` (that sends `setData`); that wiring is in the Review page (Task 11.10). The pane itself only forwards the page's messages to the attached bridge (`bridge.Dispatch`), sends `MapSetTheme` on a theme change, reports connectivity changes (the owner re-sends `init`) and raises the UI events. Pong/ready reach the owner through `ReviewVm.MapStatus`, the context menu through `ReviewVm.MapContextMenuRequested`.

Add to `src/UasSort.App/MapHost/MapPane.xaml.cs` (inside `MapPane`; add `using Windows.Networking.Connectivity;` and `using Windows.Foundation;`):

```csharp
    private MapBridge? _bridge;
    private bool _networkHooked;

    /// <summary>The raw contextMenu message as a point in this pane (CSS px = DIPs at WebView2 zoom 1; zoom control is off).
    /// The Review page shows its menu from ReviewVm.MapContextMenuRequested; this event serves hosts without a ReviewVm.</summary>
    public event Action<IReadOnlyList<string>, Point>? ContextMenuRequested;

    /// <summary>Ref §9.6: after a map click (click or clickEmpty) the host moves focus back to the clip list (UNVERIFIED with WebView2 focus).</summary>
    public event Action? FocusReturnRequested;

    /// <summary>Connectivity changed; the owner re-sends MapProjection.Init with the new online flag (Ref §9.6 offline).</summary>
    public event Action<bool>? OnlineChanged;

    public string ThemeName => ActualTheme == ElementTheme.Dark ? "dark" : "light";

    public static bool IsOnline() =>
        NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;

    /// <summary>Feeds every map→host message to this bridge (MapBridge.Dispatch parses it and raises Received).</summary>
    public void Attach(MapBridge bridge)
    {
        Detach();
        _bridge = bridge;
        MessageReceived += bridge.Dispatch;
        ActualThemeChanged += OnThemeChanged;
        if (!_networkHooked)
        {
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            _networkHooked = true;
        }
    }

    public void Detach()
    {
        if (_bridge is not { } b) return;
        MessageReceived -= b.Dispatch;
        ActualThemeChanged -= OnThemeChanged;
        _bridge = null;
    }

    /// <summary>The "Open in browser" address for a centre point (OpenStreetMap at zoom 12; world view without one).</summary>
    public static Uri OsmUri(GeoPoint? center) => center is { } c
        ? new Uri(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                                $"https://www.openstreetmap.org/?mlat={c.Lat:F5}&mlon={c.Lon:F5}#map=12/{c.Lat:F5}/{c.Lon:F5}"))
        : new Uri("https://www.openstreetmap.org/");

    private void OnThemeChanged(FrameworkElement sender, object args)
    {
        if (MapView.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = ThemeName == "dark" ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        _bridge?.Send(new MapSetTheme(ThemeName));
    }

    private void OnNetworkStatusChanged(object sender) =>
        DispatcherQueue.TryEnqueue(() => OnlineChanged?.Invoke(IsOnline()));

    /// <summary>UI events for the click and contextMenu messages (called from OnWebMessageReceived, on the UI thread).</summary>
    private void RaiseUiEvents(string? type, JsonElement root)
    {
        if (type is "click" or "clickEmpty") FocusReturnRequested?.Invoke();
        if (type == "contextMenu" && root.TryGetProperty("itemIds", out var ids) && root.TryGetProperty("x", out var x)
            && root.TryGetProperty("y", out var y))
        {
            var list = ids.EnumerateArray().Select(i => i.GetString() ?? "").ToList();
            ContextMenuRequested?.Invoke(list, new Point(x.GetDouble(), y.GetDouble()));
        }
    }
```

In `OnWebMessageReceived` (Task 11.7), call `RaiseUiEvents(type, doc.RootElement.Clone());` inside the `try` block right after `type` is read (the clone outlives the `JsonDocument`). Make `Close()` start with `Detach();` and, when `_networkHooked`, unsubscribe `NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;`. The default in `OnOpenInBrowser` becomes `OpenInBrowserUri?.Invoke() ?? OsmUri(null)`. Remove Part 01's probe page asset: `git rm src/UasSort.App/MapAssets/probe.html`.

- [ ] **Step 5: Run it and watch it pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only map.mime,map.ready,map.draw`
Expected: all three `pass`; `map.draw` detail `offline canvas drew {"items":2,"groups":1,"jumps":1,"radius":1,"base":"none","rev":7,"selected":1,"badge":"Offline: base map unavailable"}`; `exit code 0`.

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.App/MapAssets src/UasSort.App/MapHost src/UasSort.App/SelfTest
git commit -m "feat: map.js (protocol v1: bases, offline canvas, selection, radius, jumps, clicks) and MapPane bridge wiring

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.9: Selftest fixture — synthetic card, test ledger, StillProbe and ledger JSON checks, and the scan into Review

The Review checks need "a built-in fake plan" (Ref §13). It is produced by the real pipeline: the selftest writes a tiny synthetic card (three DJI clips and one DNG, generated once by `tools/fixtures/make-selftest-assets.cs` from the Part 03 builders, no user data) into its `%TEMP%` sandbox, puts the test ledger in the sandbox video root's `.uas-sort\`, and scans it through **Browse to folder** (`CardStageVm.Browse(path)`). The card is designed to give: group 1 = two Anvil clips across local midnight (a day-split banner; "Anvil Mountain" suggested from GeoNames), group 2 = one Zachar Bay clip 63 days later (a boundary-chip header "── 63 days ──" with [Merge]), and one DNG on Sep 27 (a Photos tile). The test ledger holds one line of every record kind, all about a 2025 Juneau trip that shares no file, date or place with the card, so it changes nothing in the plan.

`tools/fixtures/make-selftest-assets.cs` is Part 01's file (Task 01.7): this task extends `SelfTestAssets.All()` in place (it keeps `stack-exif.jpg`) and adds the directives it now needs; it is not recreated.

**Files:**
- Modify: `tools/fixtures/make-selftest-assets.cs` (Part 01: `SelfTestAssets.All()` adds `selftest.dng`, `ledger-v1.jsonl`, `selftest-0001.mp4`, `selftest-0002.mp4`, `selftest-0003.mp4`)
- Create: `tests/UasSort.Testing/LedgerSamples.cs` (registry decision 31: `UasSort.Testing.LedgerSamples.AllRecordKindsV1()`, built with `LedgerCodec.Serialize`)
- Create (generated, committed): `src/UasSort.App/SelfTest/selftest.dng`, `SelfTest/ledger-v1.jsonl`, `SelfTest/selftest-0001.mp4`, `SelfTest/selftest-0002.mp4`, `SelfTest/selftest-0003.mp4`
- Modify: `src/UasSort.App/UasSort.App.csproj` (Part 01's embedded-resource item also takes `*.mp4`; logical names stay `UasSort.App.SelfTest.<file>`)
- Create: `src/UasSort.App/SelfTest/SelfTestFixture.cs`, `SelfTest/SelfTestChecks.Review.cs`
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.cs` (register `probe.still`, `json.ledger`, `review.scan`)

**Interfaces:**
- Consumes: `SyntheticMp4Builder` (`WithMvhdUtc`, `WithDuration`, `WithDjmdGps`, `WithThumbnail`, `Build`), `SyntheticDngBuilder` (`WithDateTimeOriginal`, `WithGps`, `WithModel`, `WithThumbnail`, `Build`) (Part 03, `UasSort.Testing`); `LedgerCodec.Serialize`, `LedgerCodec.Version` (Part 05); every `LedgerRecord` case, `RunCard`, `RunRoots` (Part 02); `StillProbe.Read(Stream) → StillInfo`, `GpsFix`; `LedgerJsonContext.Default.LedgerRecord` (Part 02); `CardStageVm.Browse`, `CardStageVm.Message`, `ShellVm.Stage`/`Card`/`Review`, `ReviewVm.Videos`/`Photos`, `GroupCardVm.Chip` (`BoundaryChipVm.Text`), `GroupCardVm.Description`, `PhotosTabVm.Days` (registry); `SelfTestSandbox.WriteFile`, `CardRoot`, `VideoRoot` (Task 11.3); `LedgerPaths.FolderName`.
- Produces (defined here): `static class LedgerSamples { static IReadOnlyList<string> AllRecordKindsV1(); }` (`UasSort.Testing`); `internal static class SelfTestFixture` — `const string ClipA/ClipB/ClipC/Dng` (card rel paths), `const string LedgerFile`, `Stream Open(string resourceName)`, `byte[] ReadAll(string resourceName)`, `void Materialize(SelfTestSandbox s)`; `static Task<ReviewVm> SelfTestChecks.EnsureReviewAsync(SelfTestContext ctx)`; checks `probe.still`, `json.ledger`, `review.scan`.

- [ ] **Step 1: Write the ledger sample, the asset generator and the failing checks**

```csharp
// tests/UasSort.Testing/LedgerSamples.cs — owner Part 11 (registry decision 31)
namespace UasSort.Testing;

/// <summary>One ledger line of every record kind, format version 1, written with Part 05's LedgerCodec. The records describe a
/// 2025 Juneau trip, far from and long before any selftest or fixture card, so a ledger holding them changes no plan.</summary>
public static class LedgerSamples
{
    public static IReadOnlyList<string> AllRecordKindsV1()
    {
        const int v = LedgerCodec.Version;
        const string machine = "SELFTEST";
        const string run = "run-selftest-0001";
        const string name = "DJI_20250601120000_0001_D.MP4";
        const string src = "DCIM/DJI_001/" + name;
        const string still = "DJI_20250601121000_0002_D.DNG";
        const string folder = @"X:\UAS Videos\2025\2025-06\2025-06-01 Juneau";
        var at = new DateTime(2025, 6, 1, 22, 0, 0, DateTimeKind.Utc);
        var card = new RunCard("1A2B3C4D", "SELFTEST", "exFAT", "FC9113", "0000000000000000");
        LedgerRecord[] records =
        [
            new FileRecord(v, "sample-file", machine, run, at, "video", name, 1_048_576, src, "video", folder + @"\" + name,
                           "00000000000000000000000000000001", "unbuffered", at.AddHours(-2), at.AddHours(-2), "Mvhd", 58.3019, -134.4197,
                           "America/Juneau", new DateOnly(2025, 6, 1), at.AddHours(-2).AddMinutes(-5), "1581F0001", null),
            new FolderRecord(v, "sample-folder", machine, run, folder, "Juneau", "created", 58.3019, -134.4197,
                             new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 1), "America/Juneau"),
            new SeenRecord(v, "sample-seen", machine, run, at, still, 25_165_824, "DCIM/DJI_001/" + still, at.AddHours(-2), "New", "unticked", null),
            new DecisionRecord(v, "sample-decision", machine, run, at, "dismissed", still, 25_165_824, "DCIM/DJI_001/" + still,
                               at.AddHours(-2), "not needed", null),
            new RevokeRecord(v, "sample-revoke", machine, at.AddMinutes(1), "sample-decision"),
            new RunRecord(v, "sample-run", machine, run, at.AddMinutes(-10), at, "0.1.0", card,
                          new RunRoots(@"X:\UAS Videos", @"X:\UAS Videos\Picture Offload"), "Safe",
                          ImmutableDictionary<string, int>.Empty.Add("VerifiedThisRun", 1)),
            new TornRecord(v, "sample-torn", machine, at.AddMinutes(2), 7),
            new CardDeleteRecord(v, "sample-card-delete", machine, "run-selftest-0002", at.AddDays(1), name, 1_048_576, src, src,
                                 at.AddHours(-2), "InLedger", "in the history, verified", "beforeDate", card, null),
        ];
        return [.. records.Select(LedgerCodec.Serialize)];
    }
}
```

Replace Part 01's `tools/fixtures/make-selftest-assets.cs` with this extended version (the first two lines are new directives; `StackExifJpeg`, `Entry` and `Rationals` are Part 01's code unchanged; `All()` gains the five new assets):

```csharp
// Writes the checked-in selftest assets into src/UasSort.App/SelfTest/ (Ref §2.3, §13). No user data.
// usage (from the repo root, on Windows): dotnet run tools/fixtures/make-selftest-assets.cs -- .
#:property TargetFramework=net11.0-windows10.0.26100.0
#:project ../../tests/UasSort.Testing/UasSort.Testing.csproj
using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using UasSort.Core;
using UasSort.Testing;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

var repoRoot = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var selfTestDir = Path.Join(repoRoot, "src", "UasSort.App", "SelfTest");
Directory.CreateDirectory(selfTestDir);
foreach (var (name, bytes) in SelfTestAssets.All())
{
    var path = Path.Join(selfTestDir, name);
    File.WriteAllBytes(path, bytes);
    Console.WriteLine($"{path} ({bytes.Length} bytes)");
}

internal static class SelfTestAssets
{
    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;

    private static readonly GeoPoint Anvil = new(64.5627, -165.3696);
    private static readonly GeoPoint Zachar = new(57.5368, -153.7484);

    /// <summary>Every asset this tool writes: Part 01's stack-exif.jpg, then Part 11's synthetic card and test ledger.</summary>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("stack-exif.jpg", StackExifJpeg());

        // Part 11 (Task 11.9): the synthetic card. Drone clock US Eastern (mvhd = stamp + 4 h), as the RC 2 of the fixture.
        var thumb = Jpeg160x90();
        yield return ("selftest-0001.mp4", new SyntheticMp4Builder().WithMvhdUtc(new DateTime(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc))
            .WithDuration(TimeSpan.FromSeconds(222)).WithDjmdGps("dvtm_Air3s.proto", Anvil).WithThumbnail(thumb).Build());    // Jul 25 23:50 AKDT
        yield return ("selftest-0002.mp4", new SyntheticMp4Builder().WithMvhdUtc(new DateTime(2026, 7, 26, 8, 10, 0, DateTimeKind.Utc))
            .WithDuration(TimeSpan.FromSeconds(95)).WithDjmdGps("dvtm_Air3s.proto", Anvil).WithThumbnail(thumb).Build());     // Jul 26 00:10 AKDT
        yield return ("selftest-0003.mp4", new SyntheticMp4Builder().WithMvhdUtc(new DateTime(2026, 9, 27, 18, 1, 27, DateTimeKind.Utc))
            .WithDuration(TimeSpan.FromSeconds(3725)).WithDjmdGps("dvtm_Air3s.proto", Zachar).WithThumbnail(thumb).Build());  // Sep 27 10:01 AKDT
        yield return ("selftest.dng", new SyntheticDngBuilder().WithDateTimeOriginal(new DateTime(2026, 9, 27, 14, 5, 0))
            .WithGps(Zachar).WithModel("FC9113").WithThumbnail(thumb).Build());
        yield return ("ledger-v1.jsonl", Encoding.UTF8.GetBytes(string.Join("\n", LedgerSamples.AllRecordKindsV1()) + "\n"));
    }

    /// <summary>A real, decodable 160×90 JPEG (like a tnal thumbnail): a blue→orange gradient, encoded by WinRT.</summary>
    public static byte[] Jpeg160x90() => Jpeg160x90Async().GetAwaiter().GetResult();

    private static async Task<byte[]> Jpeg160x90Async()
    {
        const int w = 160, h = 90;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = (byte)(255 - x * 255 / w); px[i + 1] = (byte)(96 + y); px[i + 2] = (byte)(x * 255 / w); px[i + 3] = 255;   // BGRA
            }
        using var stream = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, w, h, 96, 96, px);
        await enc.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    /// <summary>
    /// SOI, APP1 "Exif" (little-endian TIFF: IFD0 -> Exif IFD with DateTimeOriginal, GPS IFD with Zachar Bay
    /// 57.5368 N 153.7484 W), EOI. A metadata carrier, not a decodable image; MetadataExtractor stops at EOI.
    /// </summary>
    public static byte[] StackExifJpeg()
    {
        const int Ifd0 = 8, ExifIfd = 38, Dto = 56, GpsIfd = 76, Lat = 142, Lon = 166, TiffLength = 190;
        var tiff = new byte[TiffLength];
        var s = tiff.AsSpan();
        s[0] = (byte)'I';
        s[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], Ifd0);

        // IFD0 (2 entries; next-IFD offset at Ifd0 + 26 stays 0)
        BinaryPrimitives.WriteUInt16LittleEndian(s[Ifd0..], 2);
        Entry(s, Ifd0 + 2, 0x8769, TypeLong, 1, ExifIfd);
        Entry(s, Ifd0 + 14, 0x8825, TypeLong, 1, GpsIfd);

        // Exif IFD: DateTimeOriginal
        BinaryPrimitives.WriteUInt16LittleEndian(s[ExifIfd..], 1);
        Entry(s, ExifIfd + 2, 0x9003, TypeAscii, 20, Dto);
        Encoding.ASCII.GetBytes("2026:09:27 14:01:27\0").CopyTo(s[Dto..]);

        // GPS IFD
        BinaryPrimitives.WriteUInt16LittleEndian(s[GpsIfd..], 5);
        Entry(s, GpsIfd + 2, 0x0000, TypeByte, 4, 0x0000_0302); // GPSVersionID 2.3.0.0
        Entry(s, GpsIfd + 14, 0x0001, TypeAscii, 2, 'N');       // "N\0" inline
        Entry(s, GpsIfd + 26, 0x0002, TypeRational, 3, Lat);
        Entry(s, GpsIfd + 38, 0x0003, TypeAscii, 2, 'W');       // "W\0" inline
        Entry(s, GpsIfd + 50, 0x0004, TypeRational, 3, Lon);
        Rationals(s[Lat..], (57, 1), (32, 1), (1248, 100));     // 57° 32' 12.48" = 57.5368
        Rationals(s[Lon..], (153, 1), (44, 1), (5424, 100));    // 153° 44' 54.24" = 153.7484

        var header = "Exif\0\0"u8;
        var app1Length = 2 + header.Length + tiff.Length;       // the APP1 length field counts itself
        var jpg = new byte[2 + 2 + app1Length + 2];
        jpg[0] = 0xFF;
        jpg[1] = 0xD8;                                          // SOI
        jpg[2] = 0xFF;
        jpg[3] = 0xE1;                                          // APP1
        BinaryPrimitives.WriteUInt16BigEndian(jpg.AsSpan(4), (ushort)app1Length);
        header.CopyTo(jpg.AsSpan(6));
        tiff.CopyTo(jpg.AsSpan(6 + header.Length));
        jpg[^2] = 0xFF;
        jpg[^1] = 0xD9;                                         // EOI
        return jpg;
    }

    private static void Entry(Span<byte> s, int at, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(s[at..], tag);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(at + 2)..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(at + 4)..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(at + 8)..], valueOrOffset);
    }

    private static void Rationals(Span<byte> s, params ReadOnlySpan<(uint Numerator, uint Denominator)> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(s[(i * 8)..], values[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(s[(i * 8 + 4)..], values[i].Denominator);
        }
    }
}
```

In `src/UasSort.App/UasSort.App.csproj`, Part 01's embedded-resource item also takes the MP4s (the logical names stay `UasSort.App.SelfTest.<file>`, which Part 09's `AppAssets.OpenSelfTest` also finds):

```xml
    <EmbeddedResource Include="SelfTest\*.jpg;SelfTest\*.dng;SelfTest\*.jsonl;SelfTest\*.mp4" LogicalName="UasSort.App.SelfTest.%(Filename)%(Extension)" />
```

```csharp
// src/UasSort.App/SelfTest/SelfTestFixture.cs
namespace UasSort.App.SelfTest;

/// <summary>The synthetic card and ledger the selftest scans (Ref §13 "built-in fake plan", produced by the real pipeline).</summary>
internal static class SelfTestFixture
{
    public const string ClipA = "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4";   // Jul 25 23:50 AKDT, Anvil
    public const string ClipB = "DCIM/DJI_001/DJI_20260726041000_0002_D.MP4";   // Jul 26 00:10 AKDT, Anvil (day split)
    public const string ClipC = "DCIM/DJI_001/DJI_20260927140127_0003_D.MP4";   // Sep 27 10:01 AKDT, Zachar Bay (63 days later)
    public const string Dng = "DCIM/DJI_001/DJI_20260927140500_0004_D.DNG";     // Sep 27 10:05 AKDT, Zachar Bay
    public const string LedgerFile = "ledger-SELFTEST.jsonl";

    private const string ResourcePrefix = "UasSort.App.SelfTest.";                // Part 01's LogicalName

    private static readonly (string Resource, string CardRel)[] CardFiles =
    [
        ("selftest-0001.mp4", ClipA), ("selftest-0002.mp4", ClipB), ("selftest-0003.mp4", ClipC), ("selftest.dng", Dng),
    ];

    public static Stream Open(string resourceName) =>
        typeof(SelfTestFixture).Assembly.GetManifestResourceStream(ResourcePrefix + resourceName)
        ?? throw new InvalidOperationException("missing embedded selftest asset " + resourceName);

    /// <summary>The card files under the sandbox's CardRoot and the test ledger in VideoRoot\.uas-sort (through SelfTestSandbox.WriteFile).</summary>
    public static void Materialize(SelfTestSandbox s)
    {
        var card = Path.GetRelativePath(s.Root, s.CardRoot);
        var video = Path.GetRelativePath(s.Root, s.VideoRoot);
        foreach (var (resource, rel) in CardFiles)
            s.WriteFile(Path.Join(card, rel.Replace('/', Path.DirectorySeparatorChar)), ReadAll(resource));
        s.WriteFile(Path.Join(video, LedgerPaths.FolderName, LedgerFile), ReadAll("ledger-v1.jsonl"));
    }

    public static byte[] ReadAll(string resourceName)
    {
        using var src = Open(resourceName);
        using var ms = new MemoryStream();
        src.CopyTo(ms);
        return ms.ToArray();
    }
}
```

Add `("probe.still", ProbeStill), ("json.ledger", JsonLedger), ("review.scan", ReviewScan),` to `SelfTestChecks.All` (before the `map.*` entries), and create:

```csharp
// src/UasSort.App/SelfTest/SelfTestChecks.Review.cs (Core namespaces come from GlobalUsings.Core.cs)
using System.Text;
using System.Text.Json;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    private static Task<SelfTestCheck> ProbeStill(SelfTestContext ctx)
    {
        using var dng = SelfTestFixture.Open("selftest.dng");
        var info = StillProbe.Read(dng);
        bool gps = info.Gps is GpsFix fix && Math.Abs(fix.Point.Lat - 57.5368) < 1e-4 && Math.Abs(fix.Point.Lon + 153.7484) < 1e-4;
        return Task.FromResult(info.DtoNaive == new DateTime(2026, 9, 27, 14, 5, 0) && gps && info.Model == "FC9113"
            ? SelfTestCheck.Pass("probe.still", "selftest.dng: DTO 2026-09-27 14:05:00, GPS Zachar Bay, FC9113")
            : SelfTestCheck.Fail("probe.still", $"DTO {info.DtoNaive:O}, gps {gps}, model {info.Model}"));
    }

    private static Task<SelfTestCheck> JsonLedger(SelfTestContext ctx)
    {
        var lines = Encoding.UTF8.GetString(SelfTestFixture.ReadAll("ledger-v1.jsonl"))
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var record = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerRecord)
                         ?? throw new JsonException("null record: " + line);
            var first = JsonSerializer.Serialize(record, LedgerJsonContext.Default.LedgerRecord);
            var again = JsonSerializer.Deserialize(first, LedgerJsonContext.Default.LedgerRecord);
            // compare serialised forms: records holding ImmutableDictionary/ImmutableArray have reference equality for those members
            if (again is null || again.GetType() != record.GetType()
                || JsonSerializer.Serialize(again, LedgerJsonContext.Default.LedgerRecord) != first
                || LedgerCodec.TryParse(line, out _) is null)             // Part 05's reader accepts the line too
                return Task.FromResult(SelfTestCheck.Fail("json.ledger", "round-trip changed " + line));
            kinds.Add(record.GetType().Name);
        }
        string[] expected = ["FileRecord", "FolderRecord", "SeenRecord", "DecisionRecord", "RevokeRecord", "RunRecord", "TornRecord", "CardDeleteRecord"];
        var missing = expected.Where(k => !kinds.Contains(k)).ToList();
        return Task.FromResult(missing.Count == 0
            ? SelfTestCheck.Pass("json.ledger", $"{lines.Length} lines, every record kind round-trips")
            : SelfTestCheck.Fail("json.ledger", "missing kinds: " + string.Join(", ", missing)));
    }

    /// <summary>Materialises the synthetic card once and scans it through Browse to folder; later checks reuse the ReviewVm.</summary>
    public static async Task<ReviewVm> EnsureReviewAsync(SelfTestContext ctx)
    {
        var shell = ctx.Services.Shell;
        if (ctx.Shared.ContainsKey("reviewScanned") && shell.Review is { } done) return done;
        SelfTestFixture.Materialize(ctx.Sandbox);
        ctx.Shared["reviewScanned"] = true;
        if (!await WaitUntilAsync(() => shell.Stage == Stage.Card && shell.Card is not null, TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("Card stage not reached (stage " + shell.Stage + ")");
        shell.Card!.Browse(ctx.Sandbox.CardRoot);                // CardChosen → ShellVm.UseCardAsync → Scan → Review
        if (!await WaitUntilAsync(() => shell.Stage == Stage.Review && shell.Review is not null, TimeSpan.FromSeconds(20)))
            throw new InvalidOperationException($"Review not reached: stage {shell.Stage}; refusal '{shell.Card?.Message}'");
        return shell.Review!;
    }

    private static async Task<SelfTestCheck> ReviewScan(SelfTestContext ctx)
    {
        var review = await EnsureReviewAsync(ctx);
        var groups = review.Videos.Timeline.OfType<GroupCardVm>().ToList();
        var chipText = groups.Count > 1 ? groups[1].Chip?.Text ?? "" : "";
        bool chip = groups.Count == 2 && chipText.Contains("63 days", StringComparison.Ordinal);
        bool photos = review.Photos.Days.Count == 1;
        return groups.Count == 2 && chip && photos
            ? SelfTestCheck.Pass("review.scan", $"2 groups ('{groups[0].Description}', '{groups[1].Description}'), chip '{chipText}', 1 photo day")
            : SelfTestCheck.Fail("review.scan", $"groups {groups.Count}, chip '{chipText}', photo days {review.Photos.Days.Count}");
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only probe.still,json.ledger,review.scan`
Expected: every check `fail` with `InvalidOperationException: missing embedded selftest asset selftest.dng` (and `…ledger-v1.jsonl`, `…selftest-0001.mp4`), `exit code 1`.

- [ ] **Step 3: Generate the assets**

Run: `dotnet run tools/fixtures/make-selftest-assets.cs -- .`
Expected: six lines `C:\dev\uas-sort\src\UasSort.App\SelfTest\<name> (<n> bytes)` for `stack-exif.jpg` (204 bytes, unchanged from Part 01), `selftest-0001.mp4`, `selftest-0002.mp4`, `selftest-0003.mp4`, `selftest.dng` and `ledger-v1.jsonl`. The files contain only synthetic data. Run `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StackExifFixtureTests"` → `failed: 0` (Part 01's fixture is byte-identical).

- [ ] **Step 4: Run them and watch them pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only probe.still,json.ledger,review.scan`
Expected:
```
{"ok":true,…,"checks":[{"name":"probe.still","status":"pass","detail":"selftest.dng: DTO 2026-09-27 14:05:00, GPS Zachar Bay, FC9113"},{"name":"json.ledger","status":"pass","detail":"8 lines, every record kind round-trips"},{"name":"review.scan","status":"pass","detail":"2 groups ('Anvil Mountain', ''), chip '── 63 days ──', 1 photo day"}]}
exit code 0
```
(The second group's description depends on the GeoNames extract near Zachar Bay; only the counts and the chip are asserted.)

- [ ] **Step 5: Commit**

```bash
git add tools/fixtures/make-selftest-assets.cs tests/UasSort.Testing/LedgerSamples.cs src/UasSort.App/SelfTest src/UasSort.App/UasSort.App.csproj
git commit -m "test: selftest synthetic card and ledger, StillProbe and ledger JSON checks, scan into Review

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.10: Review page — layout, tabs, splitters, map host, tuning strip, footer and issues flyout

**Files:**
- Create: `src/UasSort.App/Controls/InfoBarList.xaml`, `Controls/InfoBarList.xaml.cs`
- Create: `src/UasSort.App/Controls/TuningStrip.xaml`, `Controls/TuningStrip.xaml.cs`
- Create: `src/UasSort.App/Controls/FooterBar.xaml`, `Controls/FooterBar.xaml.cs`
- Create: `src/UasSort.App/Pages/ReviewPage.xaml`, `Pages/ReviewPage.xaml.cs`
- Modify: `src/UasSort.App/Pages/ShellPage.xaml.cs` (`[Stage.Review] = typeof(ReviewPage),`), `SelfTest/SelfTestChecks.cs` (register `review.layout`, `review.clock`, `review.tuning`, `review.map`), `SelfTest/SelfTestChecks.Review.cs` (add the checks)

**Interfaces:**
- Consumes (registry Review VMs table): `ReviewVm` (`InfoBars`, `CloseInfoBar(string)`, `Videos.Header`, `Photos.Header`, `Other.Header`, `SelectedTab`, `FooterText`, `OffloadCommand`, `OffloadDisabledReason`, `Issues`, `Tuning`, `Map`, `MapBase`, `GoTo(ItemId)`, `CanUndo`, `UndoCommand`, `Plan`), `InfoBarVm` (`Key`, `Severity`, `Message`, `IsClosable`, `Actions`), `QuickFixVm` (`Label`, `Command`), `TuningVm` (`RadiusMiles`, `GapDays`, `RadiusText`, `GapText`, `GroupCountText`, `ResetText`, `ResetCommand`, `BeginDrag()`, `EndDragAsync()`), `IssuesVm` (`Entries`, `BlockingCount`, `WarningCount`, `InfoCount`), `IssueVm` (`Glyph`, `Message`, `Code`, `Anchor`, `QuickFixes`), `MapBridge`, `MapProjection.Init`, `ShellVm.Settings.Layout`, `ShellVm.UpdateLayout(double, double)`; `MapPane` (Tasks 11.7–11.8), `FileReviewLog` (Task 11.4); CommunityToolkit `GridSplitter` (Sizers).
- Produces (defined here):
  - `InfoBarList : UserControl` — `ObservableCollection<InfoBarVm>? Items { get; set; }`, `Action<string>? Close { get; set; }` (closing a bar calls it with `InfoBarVm.Key`).
  - `TuningStrip : UserControl` — `ReviewVm? Review { get; set; }`, `Slider RadiusSlider`, `Slider GapSlider`, `void OnDragStarted()`, `Task OnDragEndedAsync()` (the pointer handlers call these; `handledEventsToo: true` on `PointerPressed`/`PointerReleased`, plus `PointerCaptureLost`).
  - `FooterBar : UserControl` — `ReviewVm? Review { get; set; }`.
  - `ReviewPage : Page` — `ReviewVm Vm`, `MapPane Map`, `SelectorBar Tabs`, named slots `TimelineSlot`, `ClipListSlot`, `PhotosSlot`, `OtherSlot` (Borders that Tasks 11.11–11.13 replace with their controls), `void ShowTab(int)`, `NavigationCacheMode.Required`; the map host wiring of registry Part 11 item 14 (one `MapBridge` per `ReviewVm`); the layout from `ShellVm.Settings.Layout`, saved through `ShellVm.UpdateLayout` 500 ms after the splitters rest.
  - Checks `review.layout`, `review.clock`, `review.tuning`, `review.map`.

- [ ] **Step 1: Write the failing checks**

Register `("review.layout", ReviewLayout), ("review.clock", ReviewClock), ("review.tuning", ReviewTuning), ("review.map", ReviewMap),` after `review.scan`, and append to `SelfTestChecks.Review.cs` (add `using CommunityToolkit.WinUI.Controls; using Microsoft.UI.Xaml.Controls;` — the App namespaces are global):

```csharp
    public static async Task<ReviewPage> ReviewPageAsync(SelfTestContext ctx)
    {
        await EnsureReviewAsync(ctx);
        if (!await WaitUntilAsync(() => CurrentPage<ReviewPage>(ctx) is { IsLoaded: true }, TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("ReviewPage not shown");
        return CurrentPage<ReviewPage>(ctx)!;
    }

    private static async Task<SelfTestCheck> ReviewLayout(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        var tabs = page.Tabs.Items.Select(i => i.Text).ToList();
        int splitters = VisualTree.FindAll<GridSplitter>(page).Count;
        var offload = VisualTree.FindDescendant<Button>(page, b => (b.Content as string)?.StartsWith("Offload", StringComparison.Ordinal) == true);
        bool ok = tabs.Count == 3 && tabs[0].StartsWith("Videos", StringComparison.Ordinal) && tabs[1].StartsWith("Photos", StringComparison.Ordinal)
                  && tabs[2].StartsWith("Other", StringComparison.Ordinal) && splitters == 2 && offload is not null
                  && VisualTree.FindDescendant<MapPane>(page) is not null;
        return ok ? SelfTestCheck.Pass("review.layout", $"tabs [{string.Join(" | ", tabs)}], 2 splitters, map, Offload")
                  : SelfTestCheck.Fail("review.layout", $"tabs [{string.Join(" | ", tabs)}], splitters {splitters}, offload {offload is not null}");
    }

    /// <summary>Ref §9.2/§6.5: the fixture's drone clock is US Eastern while every clip is in Alaska → the Warning InfoBar
    /// on Review and a "clock ≠ local" chip on the cards.</summary>
    private static async Task<SelfTestCheck> ReviewClock(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        var bar = VisualTree.FindDescendant<InfoBar>(page, b => b.Severity == InfoBarSeverity.Warning
                      && b.Message.Contains("Drone clock is set to", StringComparison.Ordinal) && b.Message.Contains("Alaska", StringComparison.Ordinal));
        var groups = page.Vm.Videos.Timeline.OfType<GroupCardVm>().ToList();
        bool chips = groups.Count == 2 && groups.All(g => g.Chips.Any(c => c.Kind == ChipKind.ClockMismatch && c.Text == "clock ≠ local"));
        return bar is not null && chips
            ? SelfTestCheck.Pass("review.clock", "clock-mismatch InfoBar shown; 'clock ≠ local' on both cards")
            : SelfTestCheck.Fail("review.clock", $"infobar {bar is not null}, chips {chips}");
    }

    private static async Task<SelfTestCheck> ReviewTuning(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        var strip = VisualTree.FindDescendant<TuningStrip>(page)!;
        var review = page.Vm;
        var tuning = review.Tuning;
        strip.OnDragStarted();                                  // what PointerPressed (handledEventsToo) calls
        strip.RadiusSlider.Value = 30;
        strip.RadiusSlider.Value = 25;                          // two previews, latest wins
        await strip.OnDragEndedAsync();                         // what PointerReleased / PointerCaptureLost call: ONE undo entry
        if (!await WaitUntilAsync(() => review.CanUndo && tuning.RadiusMiles == 25, TimeSpan.FromSeconds(5)))
            return SelfTestCheck.Fail("review.tuning", $"after drag: R {tuning.RadiusMiles}, canUndo {review.CanUndo}");
        await review.UndoCommand.ExecuteAsync(null);
        bool back = await WaitUntilAsync(() => tuning.RadiusMiles == 50 && !review.CanUndo, TimeSpan.FromSeconds(5));
        return back ? SelfTestCheck.Pass("review.tuning", "drag 50→30→25 committed once; one undo restored 50 mi")
                    : SelfTestCheck.Fail("review.tuning", $"after undo: R {tuning.RadiusMiles}, canUndo {review.CanUndo}");
    }

    private static async Task<SelfTestCheck> ReviewMap(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        if (!await WaitUntilAsync(() => page.Map.IsReady || page.Map.IsUnavailable, TimeSpan.FromSeconds(15)))
            return SelfTestCheck.Fail("review.map", "map not ready");
        var groups = page.Vm.Videos.Timeline.OfType<GroupCardVm>().ToList();
        page.Vm.Videos.SelectedEntry = groups[1];                // selecting a card sends select + fit (Ref §9.6 Sync)
        var stats = await PollEvaluateAsync(page.Map, "JSON.stringify(window.__uas.stats())",
            s => s is not null && s.Contains("\"items\":3", StringComparison.Ordinal) && s.Contains("\"groups\":2", StringComparison.Ordinal)
                 && s.Contains("\"radius\":1", StringComparison.Ordinal), TimeSpan.FromSeconds(8));
        bool ok = stats is not null && stats.Contains("\"items\":3", StringComparison.Ordinal) && stats.Contains("\"radius\":1", StringComparison.Ordinal);
        return ok ? SelfTestCheck.Pass("review.map", "bridge drew " + stats) : SelfTestCheck.Fail("review.map", "stats " + (stats ?? "(none)"));
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only review.layout,review.clock,review.tuning,review.map`
Expected: `CS0246: The type or namespace name 'ReviewPage' could not be found` (and `TuningStrip`) → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```xml
<!-- src/UasSort.App/Controls/InfoBarList.xaml — ReviewVm.InfoBars (Ref §9.2); closing calls ReviewVm.CloseInfoBar(Key) -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.Controls.InfoBarList"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <ItemsControl ItemsSource="{x:Bind Items, Mode=OneWay}">
        <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="rv:InfoBarVm">
                <InfoBar IsOpen="True" Severity="{x:Bind ui:UiFormat.BarSeverity(Severity)}" Message="{x:Bind Message}"
                         IsClosable="{x:Bind IsClosable}" Closed="OnBarClosed" Margin="0,4,0,0">
                    <InfoBar.Content>
                        <ItemsControl ItemsSource="{x:Bind Actions}" Margin="0,0,0,8"
                                      Visibility="{x:Bind ui:UiFormat.VisibleIfAny(Actions.Count)}">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="6" /></ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="rv:QuickFixVm">
                                    <Button Content="{x:Bind Label}" Command="{x:Bind Command}" />
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </InfoBar.Content>
                </InfoBar>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</UserControl>
```

```csharp
// src/UasSort.App/Controls/InfoBarList.xaml.cs
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class InfoBarList : UserControl
{
    private ObservableCollection<InfoBarVm>? _items;
    public InfoBarList() => InitializeComponent();
    public ObservableCollection<InfoBarVm>? Items { get => _items; set { _items = value; Bindings.Update(); } }

    /// <summary>Called with InfoBarVm.Key when the user closes a bar (the Review page passes ReviewVm.CloseInfoBar).</summary>
    public Action<string>? Close { get; set; }

    private void OnBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (sender.DataContext is InfoBarVm bar) Close?.Invoke(bar.Key);
    }
}
```

```xml
<!-- src/UasSort.App/Controls/TuningStrip.xaml — Ref §9.7: under the map; base toggle, R 5–100 mi, G 0–7 days, live group count -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.Controls.TuningStrip"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services">
    <Grid ColumnSpacing="16" Padding="8,4">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" MinWidth="220" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <!-- ReviewVm.MapBase ("streets" / "satellite" / "none"); its setter sends MapSetBase through ReviewVm.Map -->
        <RadioButtons x:Name="BaseToggle" MaxColumns="3" SelectedIndex="{x:Bind ui:UiFormat.BaseIndex(Review.MapBase), Mode=OneWay}"
                      SelectionChanged="OnBaseChanged" VerticalAlignment="Center">
            <x:String>Streets</x:String>
            <x:String>Satellite</x:String>
            <x:String>Off</x:String>
        </RadioButtons>
        <StackPanel Grid.Column="1">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="R" FontWeight="SemiBold" VerticalAlignment="Center" />
                <TextBlock Text="{x:Bind Review.Tuning.RadiusText, Mode=OneWay}" Style="{StaticResource CaptionText}" VerticalAlignment="Center" />
            </StackPanel>
            <Slider x:Name="RadiusSlider" Minimum="5" Maximum="100" StepFrequency="1" SmallChange="1" LargeChange="5"
                    TickPlacement="None" Value="{x:Bind Review.Tuning.RadiusMiles, Mode=TwoWay}"
                    AutomationProperties.Name="Grouping radius in miles" />
            <!-- tick labels at 10 / 25 / 50 / 75 / 100 mi on the 5–100 scale -->
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="5*" />
                    <ColumnDefinition Width="15*" />
                    <ColumnDefinition Width="25*" />
                    <ColumnDefinition Width="25*" />
                    <ColumnDefinition Width="25*" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="1" Text="10" Style="{StaticResource CaptionText}" />
                <TextBlock Grid.Column="2" Text="25" Style="{StaticResource CaptionText}" />
                <TextBlock Grid.Column="3" Text="50" Style="{StaticResource CaptionText}" />
                <TextBlock Grid.Column="4" Text="75" Style="{StaticResource CaptionText}" />
                <TextBlock Grid.Column="4" Text="100" Style="{StaticResource CaptionText}" HorizontalAlignment="Right" />
            </Grid>
        </StackPanel>
        <StackPanel Grid.Column="2" Width="150">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="G" FontWeight="SemiBold" VerticalAlignment="Center" />
                <TextBlock Text="{x:Bind Review.Tuning.GapText, Mode=OneWay}" Style="{StaticResource CaptionText}" VerticalAlignment="Center" />
            </StackPanel>
            <!-- TuningVm.GapDays is an int: shown through ToDouble, written back by OnGapChanged -->
            <Slider x:Name="GapSlider" Minimum="0" Maximum="7" StepFrequency="1" SmallChange="1" TickFrequency="1" TickPlacement="BottomRight"
                    Value="{x:Bind ui:UiFormat.ToDouble(Review.Tuning.GapDays), Mode=OneWay}" ValueChanged="OnGapChanged"
                    AutomationProperties.Name="Day gap" />
        </StackPanel>
        <StackPanel Grid.Column="3" VerticalAlignment="Center" Spacing="4">
            <TextBlock x:Name="GroupCount" Text="{x:Bind Review.Tuning.GroupCountText, Mode=OneWay}" FontWeight="SemiBold" />
            <HyperlinkButton Content="{x:Bind Review.Tuning.ResetText, Mode=OneWay}" Command="{x:Bind Review.Tuning.ResetCommand, Mode=OneWay}" Padding="0" />
        </StackPanel>
    </Grid>
</UserControl>
```

```csharp
// src/UasSort.App/Controls/TuningStrip.xaml.cs — Ref §9.7 / §2.7 #10: Slider has no drag-completed event
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace UasSort.App.Controls;

public sealed partial class TuningStrip : UserControl
{
    private ReviewVm? _review;
    private bool _dragging;

    public TuningStrip()
    {
        InitializeComponent();
        foreach (var slider in new[] { RadiusSlider, GapSlider })
        {
            // The Slider's thumb marks the pointer events handled, so handledEventsToo is required (Ref §9.7).
            slider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => OnDragStarted()), handledEventsToo: true);
            slider.AddHandler(PointerReleasedEvent, new PointerEventHandler(async (_, _) => await OnDragEndedAsync()), handledEventsToo: true);
            slider.PointerCaptureLost += async (_, _) => await OnDragEndedAsync();
        }
        // Keyboard and wheel changes have no release: TuningVm commits them after 400 ms (IdleCommit) without a change.
    }

    public ReviewVm? Review { get => _review; set { _review = value; Bindings.Update(); } }

    public void OnDragStarted()
    {
        if (_dragging || _review is null) return;
        _dragging = true;
        _review.Tuning.BeginDrag();
    }

    public async Task OnDragEndedAsync()
    {
        if (!_dragging || _review is null) return;
        _dragging = false;
        await _review.Tuning.EndDragAsync();                  // commits ONE undo entry
    }

    private void OnGapChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_review is not null) _review.Tuning.GapDays = (int)Math.Round(e.NewValue);
    }

    private void OnBaseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_review is not null && BaseToggle.SelectedIndex >= 0) _review.MapBase = UiFormat.BaseAt(BaseToggle.SelectedIndex);
    }
}
```

```xml
<!-- src/UasSort.App/Controls/FooterBar.xaml — Ref §9.10: per-drive totals, issue counters + flyout, Offload -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.Controls.FooterBar"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <Grid Padding="12,8" ColumnSpacing="12" BorderThickness="0,1,0,0" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock x:Name="Totals" Text="{x:Bind Review.FooterText, Mode=OneWay}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
        <DropDownButton x:Name="IssuesButton" Grid.Column="1" AutomationProperties.Name="Issues">
            <StackPanel Orientation="Horizontal" Spacing="10">
                <TextBlock><Run Text="&#x26D4;" /> <Run Text="{x:Bind ui:UiFormat.Count(Review.Issues.BlockingCount), Mode=OneWay}" /></TextBlock>
                <TextBlock><Run Text="&#x26A0;" /> <Run Text="{x:Bind ui:UiFormat.Count(Review.Issues.WarningCount), Mode=OneWay}" /></TextBlock>
                <TextBlock><Run Text="&#x24D8;" /> <Run Text="{x:Bind ui:UiFormat.Count(Review.Issues.InfoCount), Mode=OneWay}" /></TextBlock>
            </StackPanel>
            <DropDownButton.Flyout>
                <Flyout x:Name="IssuesFlyout" Placement="TopEdgeAlignedRight">
                    <ScrollViewer MaxHeight="420" Width="520">
                        <ItemsControl ItemsSource="{x:Bind Review.Issues.Entries, Mode=OneWay}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="rv:IssueVm">
                                    <Grid ColumnSpacing="8" Padding="0,6" BorderThickness="0,0,0,1" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="Auto" />
                                            <ColumnDefinition Width="*" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{x:Bind Glyph}" />
                                        <StackPanel Grid.Column="1" Spacing="4">
                                            <!-- click → ReviewVm.GoTo(issue.Anchor) (OnIssueClick) -->
                                            <HyperlinkButton Padding="0" Click="OnIssueClick">
                                                <TextBlock Text="{x:Bind Message}" TextWrapping="Wrap" />
                                            </HyperlinkButton>
                                            <TextBlock Text="{x:Bind ui:UiFormat.IssueCodeText(Code)}" Style="{StaticResource CaptionText}" />
                                            <ItemsControl ItemsSource="{x:Bind QuickFixes}">
                                                <ItemsControl.ItemsPanel>
                                                    <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="6" /></ItemsPanelTemplate>
                                                </ItemsControl.ItemsPanel>
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate x:DataType="rv:QuickFixVm">
                                                        <Button Content="{x:Bind Label}" Command="{x:Bind Command}" />
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                        </StackPanel>
                                    </Grid>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Flyout>
            </DropDownButton.Flyout>
        </DropDownButton>
        <Button x:Name="OffloadButton" Grid.Column="2" Content="Offload ▶" Style="{StaticResource AccentButtonStyle}"
                Command="{x:Bind Review.OffloadCommand, Mode=OneWay}" ToolTipService.ToolTip="{x:Bind Review.OffloadDisabledReason, Mode=OneWay}" />
    </Grid>
</UserControl>
```

```csharp
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
```

```xml
<!-- src/UasSort.App/Pages/ReviewPage.xaml — Ref §9.3 layout -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.ReviewPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctk="using:CommunityToolkit.WinUI.Controls"
    xmlns:ctl="using:UasSort.App.Controls"
    xmlns:map="using:UasSort.App.MapHost"
    xmlns:ui="using:UasSort.App.Services"
    NavigationCacheMode="Required">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <StackPanel Margin="12,0">
            <ctl:InfoBarList x:Name="ReviewInfoBars" />
            <!-- ReviewVm.LastError: a refused rename, retarget or browse (Ref §8.9) -->
            <InfoBar IsOpen="True" IsClosable="False" Severity="Error" Message="{x:Bind Vm.LastError, Mode=OneWay}" Margin="0,4,0,0"
                     Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.LastError), Mode=OneWay}" />
        </StackPanel>

        <SelectorBar x:Name="Tabs" Grid.Row="1" Margin="8,0" SelectionChanged="OnTabChanged">
            <SelectorBarItem x:Name="VideosTabItem" Text="{x:Bind Vm.Videos.Header, Mode=OneWay}" IsSelected="True" />
            <SelectorBarItem x:Name="PhotosTabItem" Text="{x:Bind Vm.Photos.Header, Mode=OneWay}" />
            <SelectorBarItem x:Name="OtherTabItem" Text="{x:Bind Vm.Other.Header, Mode=OneWay}" />
        </SelectorBar>

        <Grid x:Name="VideosContent" Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition x:Name="TimelineColumn" Width="380" MinWidth="280" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" MinWidth="420" />
            </Grid.ColumnDefinitions>
            <Border x:Name="TimelineSlot" SizeChanged="OnPaneSizeChanged" />
            <ctk:GridSplitter Grid.Column="1" Width="8" ResizeDirection="Columns" ResizeBehavior="PreviousAndNext" />
            <Grid Grid.Column="2">
                <Grid.RowDefinitions>
                    <RowDefinition x:Name="MapRow" Height="45*" MinHeight="160" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition x:Name="ClipRow" Height="55*" MinHeight="160" />
                </Grid.RowDefinitions>
                <map:MapPane x:Name="Map" SizeChanged="OnPaneSizeChanged" />
                <ctl:TuningStrip x:Name="Tuning" Grid.Row="1" />
                <ctk:GridSplitter Grid.Row="2" Height="8" ResizeDirection="Rows" ResizeBehavior="PreviousAndNext" />
                <Border x:Name="ClipListSlot" Grid.Row="3" SizeChanged="OnPaneSizeChanged" />
            </Grid>
        </Grid>
        <Border x:Name="PhotosSlot" Grid.Row="2" Visibility="Collapsed" />
        <Border x:Name="OtherSlot" Grid.Row="2" Visibility="Collapsed" />

        <ctl:FooterBar x:Name="Footer" Grid.Row="3" />
    </Grid>
</Page>
```

```csharp
// src/UasSort.App/Pages/ReviewPage.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class ReviewPage : Page
{
    private static readonly TimeSpan LayoutSaveDelay = TimeSpan.FromMilliseconds(500);

    private MainWindow _window = null!;
    private bool _layoutApplied;
    private DispatcherQueueTimer? _layoutTimer;
    private MapBridge? _bridge;
    private ReviewVm? _bridgeFor;

    public ReviewPage()
    {
        InitializeComponent();
        Map.Ready += ConnectMap;
        Map.OnlineChanged += _ => SendMapInit();                // Ref §9.6 offline: re-send init with the new online flag
    }

    public ReviewVm Vm { get; private set; } = null!;
    public MainWindow Window => _window;
    private ShellVm Shell => _window.Services.Shell;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        _window = args.Window;
        var previous = Vm;
        if (previous is not null) previous.PropertyChanged -= OnVmChanged;
        Vm = (ReviewVm)args.Vm;                                  // ShellVm.Current (= ShellVm.Review) on the Review stage
        Vm.PropertyChanged += OnVmChanged;
        Bindings.Update();                                       // a cached page gets a new ReviewVm after each rescan
        ReviewInfoBars.Items = Vm.InfoBars;
        ReviewInfoBars.Close = Vm.CloseInfoBar;
        Tuning.Review = Vm;
        Footer.Review = Vm;
        OnReviewAttached(previous);
        ShowTab(Vm.SelectedTab);
        ApplyLayout();
        Map.OpenInBrowserUri = () => MapPane.OsmUri(Vm.Videos.SelectedCard?.Group?.Centroid);
        await Map.InitializeAsync(_window.Services);             // WebView2 is created when Review opens (Ref §9.6)
        ConnectMap();                                            // no-op until the page says ready (then Map.Ready calls it)
    }

    /// <summary>Hands the new ReviewVm to the child controls (previous = the ReviewVm shown before, or null).
    /// Tasks 11.11–11.14 each add their lines here.</summary>
    private void OnReviewAttached(ReviewVm? previous)
    {
    }

    /// <summary>Registry Part 11 item 14: one MapBridge per ReviewVm; init once the pane is ready, then review.Map = bridge,
    /// which sends setData (Ref §9.6 Sync). Selection, radius and base messages then come from the ReviewVm itself.</summary>
    private void ConnectMap()
    {
        if (!Map.IsReady || Vm is null || ReferenceEquals(_bridgeFor, Vm)) return;
        if (_bridgeFor is not null && ReferenceEquals(_bridgeFor.Map, _bridge)) _bridgeFor.Map = null;
        _bridge?.Dispose();
        var services = _window.Services;
        var bridge = new MapBridge(json => Map.PostJson(json), new FileReviewLog(services.Platform.Log), services.Platform.Clock);
        Map.Attach(bridge);
        _bridge = bridge;
        _bridgeFor = Vm;
        SendMapInit();
        Vm.Map = bridge;
    }

    private void SendMapInit()
    {
        if (_bridge is null || Vm is null) return;
        var map = Shell.Settings.Map with { Base = Vm.MapBase };
        _bridge.Send(MapProjection.Init(map, Vm.Tuning.RadiusMiles, MapPane.IsOnline(), Map.ThemeName == "dark"));
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewVm.SelectedTab)) ShowTab(Vm.SelectedTab);
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0 && Vm is not null && Vm.SelectedTab != index) Vm.SelectedTab = index;
        ShowTab(index);
    }

    public void ShowTab(int index)
    {
        if (index < 0 || index > 2) return;
        if (Tabs.SelectedItem != Tabs.Items[index]) Tabs.SelectedItem = Tabs.Items[index];
        VideosContent.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PhotosSlot.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        OtherSlot.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Ref §9.3: pane sizes come from Settings.Layout and are saved through ShellVm.UpdateLayout once the splitters rest.
    private void ApplyLayout()
    {
        var layout = Shell.Settings.Layout;
        TimelineColumn.Width = new GridLength(Math.Max(280, layout.TimelineWidth));
        var ratio = Math.Clamp(layout.MapHeightRatio, 0.15, 0.85);
        MapRow.Height = new GridLength(ratio, GridUnitType.Star);
        ClipRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
        _layoutApplied = true;
    }

    private void OnPaneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_layoutApplied || Vm is null) return;
        if (_layoutTimer is null)
        {
            _layoutTimer = DispatcherQueue.CreateTimer();
            _layoutTimer.Interval = LayoutSaveDelay;
            _layoutTimer.IsRepeating = false;
            _layoutTimer.Tick += (_, _) => SaveLayout();
        }
        _layoutTimer.Stop();
        _layoutTimer.Start();
    }

    private void SaveLayout()
    {
        double mapH = Map.ActualHeight, clipH = ClipListSlot.ActualHeight, width = Math.Round(TimelineSlot.ActualWidth);
        if (mapH + clipH <= 0 || width <= 0) return;
        var ratio = Math.Round(mapH / (mapH + clipH), 3);
        var current = Shell.Settings.Layout;
        if (Math.Abs(current.TimelineWidth - width) < 1 && Math.Abs(current.MapHeightRatio - ratio) < 0.005) return;
        Shell.UpdateLayout(width, ratio);                        // saves Settings with { Layout = new(width, ratio) }
    }
}
```

Add `[Stage.Review] = typeof(ReviewPage),` to `ShellPage.StagePages`.

- [ ] **Step 4: Run them and watch them pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only review.scan,review.layout,review.clock,review.tuning,review.map`
Expected: all `pass`; `review.layout` detail `tabs [Videos · 2 to offload | Photos… | Other…], 2 splitters, map, Offload` (the Photos and Other headers are the VMs' `Header` texts); `review.map` detail `bridge drew {"items":3,"groups":2,…,"radius":1,…}`; `exit code 0`. Lint: `dotnet test --project tests\UasSort.Platform.Tests -- --filter-class "*XamlLintTests"` → `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: Review page layout (tabs, splitters, map host, tuning strip with one-entry drag commit, footer and issues flyout)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.11: Timeline `ItemsView` and the group card

**Files:**
- Create: `src/UasSort.App/Controls/TimelineTemplateSelector.cs`
- Create: `src/UasSort.App/Controls/TimelineView.xaml`, `Controls/TimelineView.xaml.cs`
- Modify: `src/UasSort.App/Pages/ReviewPage.xaml` (replace `<Border x:Name="TimelineSlot" SizeChanged="OnPaneSizeChanged" />` with `<ctl:TimelineView x:Name="TimelineSlot" SizeChanged="OnPaneSizeChanged" />`), `Pages/ReviewPage.xaml.cs` (`OnReviewAttached`: add `TimelineSlot.Attach(Vm, _window);`)
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.cs` (register `template.groupCard`, `template.suggestion`, `template.targetMenu`), `SelfTest/SelfTestChecks.Review.cs`

**Interfaces:**
- Consumes (registry Review VMs table): `VideosTabVm` (`Timeline` of `TimelineEntryVm`, `SelectedEntry`, `SelectedCard`, `ToggleFold(FoldedRunVm)`), `GroupCardVm` (`Chip` → `BoundaryChipVm` `Text`/`ButtonText`/`CanMerge`/`MergeTooltip`/`MergeCommand`, `Swatch`, `Description`, `DescriptionIsSuggestion`, `IsDescriptionReadOnly`, `ReadOnlyHint`, `Badge`, `ConfidenceText`, `TargetPath`, `DateRangeText`, `ZoneBadges`, `LocationText`, `VideoCountsText`, `PhotoCountsText`, `Thumbs` of `ThumbVm` (`Key`), `MoreThumbsText`, `Suggestions`, `Chips` of `ChipVm` (`Kind`, `Text`, `Tooltip`, `Actions`), `CommitDescriptionCommand`, `NewFolderInsteadCommand`, `RetargetOptions()`, `Group`), `FoldedRunVm` (`Text`, `ClipCount`, `IsExpanded`), `SuggestionVm` (`Text`, `Detail`), `RetargetOptionVm` (`Kind`, `Label`, `Detail`), `RetargetKind`, `ReviewVm.RetargetAsync(card, option)`, `ReviewVm.BrowseRetargetAsync(card, path)`, `ReviewVm.Index.VideoRoot`, `ReviewVm.SelectedTab`; `Thumb.Key` (Task 11.2); `FolderPickerService` (Task 11.5).
- Produces (defined here): `TimelineTemplateSelector : DataTemplateSelector` (`GroupCard`, `FoldedRun`); `TimelineView : UserControl` — `ItemsView Items`, `void Attach(ReviewVm review, MainWindow window)`, `bool FocusRenameBox()`, `bool FocusRenameBox(GroupCardVm card)`, `ItemContainer? ContainerFor(TimelineEntryVm vm)`, `void BuildTargetMenu(MenuFlyout flyout, GroupCardVm card)`; checks `template.groupCard`, `template.suggestion`, `template.targetMenu`.

- [ ] **Step 1: Write the failing checks**

Register `("template.groupCard", TemplateGroupCard), ("template.suggestion", TemplateSuggestion), ("template.targetMenu", TemplateTargetMenu),` and append to `SelfTestChecks.Review.cs` (add `using Microsoft.UI.Xaml; using Microsoft.UI.Xaml.Media;`):

```csharp
    private static async Task<(ReviewPage Page, TimelineView Timeline, List<GroupCardVm> Groups)> TimelineAsync(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        page.ShowTab(0);
        var timeline = VisualTree.FindDescendant<TimelineView>(page) ?? throw new InvalidOperationException("no TimelineView");
        var groups = page.Vm.Videos.Timeline.OfType<GroupCardVm>().ToList();
        await WaitUntilAsync(() => groups.All(g => timeline.ContainerFor(g) is not null), TimeSpan.FromSeconds(5));
        return (page, timeline, groups);
    }

    private static async Task<SelfTestCheck> TemplateGroupCard(SelfTestContext ctx)
    {
        var (_, timeline, groups) = await TimelineAsync(ctx);
        var second = timeline.ContainerFor(groups[1]);
        var first = timeline.ContainerFor(groups[0]);
        if (first is null || second is null) return SelfTestCheck.Fail("template.groupCard", "cards not realised");
        var chip = VisualTree.FindDescendant<FrameworkElement>(second, e => e.Name == "ChipHeader");
        bool chipText = VisualTree.FindDescendant<TextBlock>(second, t => t.Text.Contains("63 days", StringComparison.Ordinal)) is not null;
        bool merge = VisualTree.FindDescendant<Button>(second, b => (b.Content as string) == "Merge") is not null;
        int thumbs = VisualTree.FindAll<Image>(first, i => Thumb.GetKey(i).CardRelPath.Length > 0).Count;
        bool badge = VisualTree.FindDescendant<TextBlock>(first, t => t.Text == groups[0].Badge && t.Text.Length > 0) is not null;
        bool ok = chip?.Visibility == Visibility.Visible && chipText && merge && thumbs == 2 && badge;
        return ok ? SelfTestCheck.Pass("template.groupCard", $"chip header '63 days' + [Merge], badge '{groups[0].Badge}', {thumbs} thumbnails")
                  : SelfTestCheck.Fail("template.groupCard", $"chip {chip?.Visibility} text {chipText} merge {merge} thumbs {thumbs} badge {badge}");
    }

    private static async Task<SelfTestCheck> TemplateSuggestion(SelfTestContext ctx)
    {
        var (page, timeline, groups) = await TimelineAsync(ctx);
        var box = VisualTree.FindDescendant<AutoSuggestBox>(timeline.ContainerFor(groups[0])!, b => b.Name == "DescriptionBox");
        if (box is null) return SelfTestCheck.Fail("template.suggestion", "no rename box on the first card");
        box.Focus(FocusState.Programmatic);
        box.IsSuggestionListOpen = true;                                     // the popup, opened in code (Ref §13)
        bool found = await WaitUntilAsync(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
            .Any(p => p.Child is not null && VisualTree.FindDescendant<TextBlock>(p.Child, t => t.Text == "Anvil Mountain") is not null),
            TimeSpan.FromSeconds(5));
        bool noRecordText = !VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
            .Any(p => p.Child is not null && VisualTree.FindDescendant<TextBlock>(p.Child, t => t.Text.Contains("SuggestionVm", StringComparison.Ordinal)) is not null);
        box.IsSuggestionListOpen = false;
        return found && noRecordText ? SelfTestCheck.Pass("template.suggestion", "popup shows 'Anvil Mountain'")
                                     : SelfTestCheck.Fail("template.suggestion", $"found {found}, record text absent {noRecordText}");
    }

    private static async Task<SelfTestCheck> TemplateTargetMenu(SelfTestContext ctx)
    {
        var (_, timeline, groups) = await TimelineAsync(ctx);
        var button = VisualTree.FindDescendant<DropDownButton>(timeline.ContainerFor(groups[0])!, b => b.Name == "TargetButton");
        if (button?.Flyout is not MenuFlyout flyout) return SelfTestCheck.Fail("template.targetMenu", "no target DropDownButton");
        flyout.ShowAt(button);                                                  // Opening builds the items (Ref §9.4)
        await WaitUntilAsync(() => flyout.Items.Count > 0, TimeSpan.FromSeconds(3));
        var options = groups[0].RetargetOptions();
        var texts = flyout.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToList();
        flyout.Hide();
        bool ok = texts.Count == options.Count
                  && options.Any(o => o.Kind == RetargetKind.Browse) && options.Any(o => o.Kind == RetargetKind.Skip)
                  && options.Any(o => o.Kind == RetargetKind.Auto) && options.Any(o => o.Kind == RetargetKind.NewFolder);
        return ok ? SelfTestCheck.Pass("template.targetMenu", "menu built on Opening: " + string.Join(" | ", texts))
                  : SelfTestCheck.Fail("template.targetMenu", "items: " + string.Join(" | ", texts));
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only template.groupCard,template.suggestion,template.targetMenu`
Expected: `CS0246: The type or namespace name 'TimelineView' could not be found` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.App/Controls/TimelineTemplateSelector.cs — two selectable item kinds (Ref §9.3)
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class TimelineTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupCard { get; set; }
    public DataTemplate? FoldedRun { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) =>
        (item is FoldedRunVm ? FoldedRun : GroupCard) ?? throw new InvalidOperationException("timeline templates not set");

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
```

```xml
<!-- src/UasSort.App/Controls/TimelineView.xaml — Ref §9.3 timeline + §9.4 group card -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.Controls.TimelineView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctl="using:UasSort.App.Controls"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <UserControl.Resources>
        <!-- The boundary chip (GroupCardVm.Chip) is the HEADER of the card after it, so every row is a selectable item. -->
        <DataTemplate x:Key="GroupCardTemplate" x:DataType="rv:GroupCardVm">
            <ItemContainer>
                <StackPanel Padding="4,2">
                    <Grid x:Name="ChipHeader" ColumnSpacing="6" Padding="4,2" Visibility="{x:Bind ui:UiFormat.VisibleIfNotNull(Chip), Mode=OneWay}"
                          ToolTipService.ToolTip="{x:Bind Chip.MergeTooltip, Mode=OneWay}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Rectangle Height="1" Fill="{ThemeResource DividerStrokeColorDefaultBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="1" Text="{x:Bind Chip.Text, Mode=OneWay}" Style="{StaticResource CaptionText}" VerticalAlignment="Center" />
                        <!-- "Merge" or "Undo split"; MergeCommand's CanExecute is CanMerge (false across library folders) -->
                        <Button Grid.Column="2" Content="{x:Bind Chip.ButtonText, Mode=OneWay}" Command="{x:Bind Chip.MergeCommand, Mode=OneWay}"
                                Padding="8,1" FontSize="12" />
                        <Rectangle Grid.Column="3" Height="1" Fill="{ThemeResource DividerStrokeColorDefaultBrush}" VerticalAlignment="Center" />
                    </Grid>
                    <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                            BorderThickness="1" CornerRadius="6">
                        <Grid ColumnSpacing="8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="6" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <Rectangle x:Name="Swatch" Fill="{x:Bind ui:UiFormat.Brush(Swatch), Mode=OneWay}" RadiusX="3" RadiusY="3" />
                            <StackPanel Grid.Column="1" Spacing="3" Padding="0,8,8,8">
                                <Grid ColumnSpacing="6">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <!-- Suggestions holds at most 6 (DescriptionSuggester.Max); no filtering in the App -->
                                    <AutoSuggestBox x:Name="DescriptionBox" Text="{x:Bind Description, Mode=OneWay}" ItemsSource="{x:Bind Suggestions}"
                                                    UpdateTextOnSelect="False" PlaceholderText="Name this folder"
                                                    FontStyle="{x:Bind ui:UiFormat.Italic(DescriptionIsSuggestion), Mode=OneWay}"
                                                    Visibility="{x:Bind ui:UiFormat.Hidden(IsDescriptionReadOnly), Mode=OneWay}"
                                                    SuggestionChosen="OnSuggestionChosen" QuerySubmitted="OnQuerySubmitted"
                                                    LostFocus="OnDescriptionLostFocus">
                                        <AutoSuggestBox.ItemTemplate>
                                            <DataTemplate x:DataType="rv:SuggestionVm">
                                                <StackPanel Padding="0,2">
                                                    <TextBlock Text="{x:Bind Text}" />
                                                    <TextBlock Text="{x:Bind Detail}" Style="{StaticResource CaptionText}" />
                                                </StackPanel>
                                            </DataTemplate>
                                        </AutoSuggestBox.ItemTemplate>
                                    </AutoSuggestBox>
                                    <TextBox x:Name="ExistingDescription" Text="{x:Bind Description, Mode=OneWay}" IsReadOnly="True"
                                             Visibility="{x:Bind ui:UiFormat.Visible(IsDescriptionReadOnly), Mode=OneWay}" />
                                    <Border Grid.Column="1" Style="{StaticResource ChipBorder}" VerticalAlignment="Center">
                                        <TextBlock Text="{x:Bind Badge, Mode=OneWay}" FontSize="11" FontWeight="SemiBold" />
                                    </Border>
                                    <Border Grid.Column="2" Style="{StaticResource ChipBorder}" VerticalAlignment="Center"
                                            Visibility="{x:Bind ui:UiFormat.VisibleIfText(ConfidenceText), Mode=OneWay}">
                                        <TextBlock Text="{x:Bind ConfidenceText, Mode=OneWay}" FontSize="11" />
                                    </Border>
                                </Grid>
                                <!-- Append targets are read-only with a hint (Ref §9.4) -->
                                <StackPanel Orientation="Horizontal" Spacing="4" Visibility="{x:Bind ui:UiFormat.VisibleIfText(ReadOnlyHint), Mode=OneWay}">
                                    <TextBlock Text="{x:Bind ReadOnlyHint, Mode=OneWay}" Style="{StaticResource CaptionText}" VerticalAlignment="Center" />
                                    <HyperlinkButton Content="New folder instead" Command="{x:Bind NewFolderInsteadCommand}" Padding="2,0" />
                                </StackPanel>
                                <Grid ColumnSpacing="4">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="{x:Bind TargetPath, Mode=OneWay}" Style="{StaticResource CaptionText}" VerticalAlignment="Center"
                                               ToolTipService.ToolTip="{x:Bind TargetPath, Mode=OneWay}" />
                                    <DropDownButton x:Name="TargetButton" Grid.Column="1" Content="Target" Padding="8,2" FontSize="12">
                                        <DropDownButton.Flyout>
                                            <MenuFlyout Opening="OnTargetMenuOpening" Placement="BottomEdgeAlignedRight" />
                                        </DropDownButton.Flyout>
                                    </DropDownButton>
                                </Grid>
                                <StackPanel Orientation="Horizontal" Spacing="6">
                                    <TextBlock Text="{x:Bind DateRangeText, Mode=OneWay}" />
                                    <ItemsControl ItemsSource="{x:Bind ZoneBadges, Mode=OneWay}">
                                        <ItemsControl.ItemsPanel>
                                            <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="4" /></ItemsPanelTemplate>
                                        </ItemsControl.ItemsPanel>
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate x:DataType="x:String">
                                                <Border Style="{StaticResource ChipBorder}"><TextBlock Text="{x:Bind}" FontSize="11" /></Border>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </StackPanel>
                                <TextBlock Text="{x:Bind LocationText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                                <TextBlock Text="{x:Bind VideoCountsText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                                <!-- the photo counts open the Photos tab (ReviewVm.SelectedTab = 1) -->
                                <HyperlinkButton Padding="0" Click="OnShowPhotos"
                                                 Visibility="{x:Bind ui:UiFormat.VisibleIfText(PhotoCountsText), Mode=OneWay}">
                                    <TextBlock Text="{x:Bind PhotoCountsText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                                </HyperlinkButton>
                                <StackPanel Orientation="Horizontal" Spacing="3">
                                    <!-- Thumbs is an IReadOnlyList<ThumbVm> replaced whole on each plan -->
                                    <ItemsControl ItemsSource="{x:Bind Thumbs, Mode=OneWay}">
                                        <ItemsControl.ItemsPanel>
                                            <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="3" /></ItemsPanelTemplate>
                                        </ItemsControl.ItemsPanel>
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate x:DataType="rv:ThumbVm">
                                                <Border Width="48" Height="27" CornerRadius="2" Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                                                    <Image ctl:Thumb.Key="{x:Bind Key}" Stretch="UniformToFill" />
                                                </Border>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                    <TextBlock Text="{x:Bind MoreThumbsText, Mode=OneWay}" Style="{StaticResource CaptionText}" VerticalAlignment="Center"
                                               Visibility="{x:Bind ui:UiFormat.VisibleIfText(MoreThumbsText), Mode=OneWay}" />
                                </StackPanel>
                                <ItemsControl ItemsSource="{x:Bind Chips}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate x:DataType="rv:ChipVm">
                                            <StackPanel Orientation="Horizontal" Spacing="4" Margin="0,2,0,0" ToolTipService.ToolTip="{x:Bind Tooltip}">
                                                <Border Style="{StaticResource ChipBorder}"><TextBlock Text="{x:Bind Text}" FontSize="11" /></Border>
                                                <ItemsControl ItemsSource="{x:Bind Actions}">
                                                    <ItemsControl.ItemsPanel>
                                                        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="4" /></ItemsPanelTemplate>
                                                    </ItemsControl.ItemsPanel>
                                                    <ItemsControl.ItemTemplate>
                                                        <DataTemplate x:DataType="rv:QuickFixVm">
                                                            <HyperlinkButton Content="{x:Bind Label}" Command="{x:Bind Command}" Padding="2,0" FontSize="12" />
                                                        </DataTemplate>
                                                    </ItemsControl.ItemTemplate>
                                                </ItemsControl>
                                            </StackPanel>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Grid>
                    </Border>
                </StackPanel>
            </ItemContainer>
        </DataTemplate>

        <!-- A run of AlreadyImported groups folded into one row; expanding it is VideosTabVm.ToggleFold(run) -->
        <DataTemplate x:Key="FoldedRunTemplate" x:DataType="rv:FoldedRunVm">
            <ItemContainer>
                <StackPanel Padding="4,2">
                    <Border Background="{ThemeResource SubtleFillColorSecondaryBrush}" CornerRadius="6" Padding="10,6">
                        <Grid>
                            <TextBlock Text="{x:Bind Text, Mode=OneWay}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" VerticalAlignment="Center" />
                            <Button HorizontalAlignment="Right" Click="OnToggleFold" Style="{StaticResource SubtleButtonStyle}"
                                    ToolTipService.ToolTip="Show or hide these groups">
                                <FontIcon Glyph="&#xE70D;" FontSize="12" />
                            </Button>
                        </Grid>
                    </Border>
                </StackPanel>
            </ItemContainer>
        </DataTemplate>

        <ctl:TimelineTemplateSelector x:Key="TimelineSelector"
                                      GroupCard="{StaticResource GroupCardTemplate}" FoldedRun="{StaticResource FoldedRunTemplate}" />
    </UserControl.Resources>

    <ItemsView x:Name="Items" SelectionMode="Single" ItemTemplate="{StaticResource TimelineSelector}"
               SelectionChanged="OnSelectionChanged" TabFocusNavigation="Once" />
</UserControl>
```

```csharp
// src/UasSort.App/Controls/TimelineView.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class TimelineView : UserControl
{
    private ReviewVm? _review;
    private MainWindow? _window;
    private bool _syncing;

    public TimelineView() => InitializeComponent();

    public void Attach(ReviewVm review, MainWindow window)
    {
        if (_review is not null) _review.Videos.PropertyChanged -= OnVideosChanged;
        _review = review;
        _window = window;
        Items.ItemsSource = review.Videos.Timeline;       // ObservableCollection<TimelineEntryVm> (Ref §2.7 #4)
        review.Videos.PropertyChanged += OnVideosChanged;
        SyncSelectionFromVm();
    }

    public ItemContainer? ContainerFor(TimelineEntryVm vm) =>
        VisualTree.FindDescendant<ItemContainer>(Items, c => ReferenceEquals(c.DataContext, vm));

    private void OnSelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        if (_syncing || _review is null) return;
        _review.Videos.SelectedEntry = sender.SelectedItem as TimelineEntryVm;
    }

    private void OnVideosChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideosTabVm.SelectedEntry)) SyncSelectionFromVm();
    }

    private void SyncSelectionFromVm()
    {
        if (_review is null) return;
        var videos = _review.Videos;
        _syncing = true;
        try
        {
            int index = videos.SelectedEntry is { } s ? videos.Timeline.IndexOf(s) : -1;
            if (index < 0) Items.DeselectAll();
            else if (!ReferenceEquals(Items.SelectedItem, videos.SelectedEntry))
            {
                Items.Select(index);
                Items.StartBringItemIntoView(index, new BringIntoViewOptions());
            }
        }
        finally { _syncing = false; }
    }

    /// <summary>F2 (Ref §9.4, §9.12): focus the selected card's rename box.</summary>
    public bool FocusRenameBox() => _review?.Videos.SelectedCard is { } card && FocusRenameBox(card);

    /// <summary>ReviewVm.FocusRenameRequested ("Name it" quick fix, F2): focus this card's rename box.</summary>
    public bool FocusRenameBox(GroupCardVm card)
    {
        if (ContainerFor(card) is not { } c) return false;
        var box = VisualTree.FindDescendant<AutoSuggestBox>(c, b => b.Name == "DescriptionBox" && b.Visibility == Visibility.Visible);
        return box?.Focus(FocusState.Keyboard) ?? false;
    }

    // ── rename (Ref §9.4): a chosen suggestion only fills the box (UpdateTextOnSelect=False, never ToString of a record);
    //    QuerySubmitted (Enter or a clicked suggestion) and LostFocus commit: set Description, then CommitDescriptionCommand ──
    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SuggestionVm s) sender.Text = s.Text;
    }

    private async void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is GroupCardVm card)
            await CommitAsync(card, args.ChosenSuggestion is SuggestionVm s ? s.Text : args.QueryText);
    }

    private async void OnDescriptionLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is AutoSuggestBox box && box.DataContext is GroupCardVm card && box.Text != card.Description)
            await CommitAsync(card, box.Text);
    }

    private static Task CommitAsync(GroupCardVm card, string text)
    {
        card.Description = text;
        return card.CommitDescriptionCommand.ExecuteAsync(null);    // ReviewVm.RenameAsync(card, Description)
    }

    private void OnShowPhotos(object sender, RoutedEventArgs e)
    {
        if (_review is not null) _review.SelectedTab = 1;
    }

    private void OnToggleFold(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FoldedRunVm run) _review?.Videos.ToggleFold(run);
    }

    // ── retarget menu, built in code-behind on Opening from GroupCardVm.RetargetOptions() (Ref §9.4 item 4) ──
    private void OnTargetMenuOpening(object sender, object e)
    {
        var flyout = (MenuFlyout)sender;
        if ((flyout.Target as FrameworkElement)?.DataContext is GroupCardVm card) BuildTargetMenu(flyout, card);
    }

    /// <summary>Auto and New folder, then the append candidates (Planner.AppendCandidates), then Browse existing… and Skip,
    /// with a separator between the three sections; a click calls ReviewVm.RetargetAsync(card, option).</summary>
    public void BuildTargetMenu(MenuFlyout flyout, GroupCardVm card)
    {
        flyout.Items.Clear();
        int? section = null;
        foreach (var option in card.RetargetOptions())
        {
            var next = option.Kind switch { RetargetKind.Auto or RetargetKind.NewFolder => 0, RetargetKind.Append => 1, _ => 2 };
            if (section is { } s && s != next) flyout.Items.Add(new MenuFlyoutSeparator());
            section = next;
            var item = new MenuFlyoutItem { Text = string.IsNullOrEmpty(option.Detail) ? option.Label : option.Label + "  ·  " + option.Detail };
            if (option.Kind == RetargetKind.Browse) item.Click += async (_, _) => await BrowseExistingAsync(card);
            else item.Click += async (_, _) => { if (_review is not null) await _review.RetargetAsync(card, option); };
            flyout.Items.Add(item);
        }
    }

    /// <summary>Browse existing…: a FolderPicker starting in the video root; ReviewVm.BrowseRetargetAsync refuses a folder outside
    /// it, in .uas-sort, or in a photo root (RetargetIntoReservedFolder, Ref §8.9) and says why in LastError.</summary>
    private async Task BrowseExistingAsync(GroupCardVm card)
    {
        if (_window is null || _review is null) return;
        if (await FolderPickerService.PickFolderAsync(_window, _review.Index.VideoRoot) is { } path)
            await _review.BrowseRetargetAsync(card, path);
    }
}
```

Apply the two `ReviewPage` modifications listed under **Files**.

- [ ] **Step 4: Run them and watch them pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only template.groupCard,template.suggestion,template.targetMenu`
Expected: all three `pass` (`chip header '63 days' + [Merge], badge '<the first card's Badge>', 2 thumbnails`; `popup shows 'Anvil Mountain'`; `menu built on Opening: <RetargetOptions labels, Auto first, Skip last>`), `exit code 0`; XAML lint `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: timeline ItemsView with chip-header group cards, folded runs, rename box and retarget menu

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.12: Clip list — rows with day-split banners, thumbnails, selection sync and the context menu

**Files:**
- Create: `src/UasSort.App/Controls/ClipListView.xaml`, `Controls/ClipListView.xaml.cs`, `Controls/ClipMenu.cs`
- Modify: `src/UasSort.App/Pages/ReviewPage.xaml` (replace `<Border x:Name="ClipListSlot" Grid.Row="3" SizeChanged="OnPaneSizeChanged" />` with `<ctl:ClipListView x:Name="ClipListSlot" Grid.Row="3" SizeChanged="OnPaneSizeChanged" />`), `Pages/ReviewPage.xaml.cs` (see Step 3)
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.cs` (register `template.clipRow`, `thumb.keyRecheck`), `SelfTest/SelfTestChecks.Review.cs`

**Interfaces:**
- Consumes (registry Review VMs table): `VideosTabVm` (`Clips`, `SelectedClipIds`, `SetSelectedClips(IReadOnlyList<ItemId>)`, `ClipSelectionChanged`), `ClipRowVm` (`Id`, `ThumbKey`, `Name`, `TimeText`, `TimeGlyph`, `TimeTooltip`, `SizeText`, `StatusText`, `StatusTooltip`, `DistanceText`, `IsIncluded`, `IsReadOnly`, `Banner`, `ToggleIncludedCommand`, `SplitBeforeCommand`), `DaySplitBannerVm` (`Text`, `IsEmphasised`, `EmphasisText`, `SplitCommand`), `ReviewVm` (`MoveSelectedToNewGroupAsync()`, `MoveTargets()`, `MoveSelectedToGroupAsync(GroupCardVm)`, `SetIncludedAsync(IReadOnlyList<ItemId>, bool)`, `OpenClip(ItemId)`, `Plan`, `MapContextMenuRequested` with `MapContextMenu(ItemIds, X, Y)`), `PathRules.Join`; `MapPane.FocusReturnRequested` (Task 11.8); `Thumb`, `ThumbnailCache` (Task 11.2).
- Produces (defined here): `ClipListView : UserControl` — `ItemsView Items`, `void Attach(ReviewVm review)`, `void SelectRows(IReadOnlyList<ItemId> ids)`, `void FocusList()`, `ItemContainer? ContainerFor(ClipRowVm row)`, `ClipRowVm? FocusedRow()`; `static class ClipMenu { static MenuFlyout Build(ReviewVm review, ClipRowVm? row); static string CardPath(ReviewVm review, ClipRowVm row); }`; checks `template.clipRow`, `thumb.keyRecheck`.

- [ ] **Step 1: Write the failing checks**

Register `("template.clipRow", TemplateClipRow), ("thumb.keyRecheck", ThumbKeyRecheck),` and append to `SelfTestChecks.Review.cs`:

```csharp
    private static async Task<SelfTestCheck> TemplateClipRow(SelfTestContext ctx)
    {
        var (page, _, groups) = await TimelineAsync(ctx);
        page.Vm.Videos.SelectedEntry = groups[0];                // the Anvil group: clip A (Jul 25) and clip B (Jul 26)
        var list = VisualTree.FindDescendant<ClipListView>(page)!;
        if (!await WaitUntilAsync(() => page.Vm.Videos.Clips.Count == 2 && list.ContainerFor(page.Vm.Videos.Clips[1]) is not null, TimeSpan.FromSeconds(5)))
            return SelfTestCheck.Fail("template.clipRow", $"clip rows: {page.Vm.Videos.Clips.Count}");
        var rowB = page.Vm.Videos.Clips[1];
        var c = list.ContainerFor(rowB)!;
        var banner = VisualTree.FindDescendant<FrameworkElement>(c, e => e.Name == "DayBanner");
        bool splitHere = VisualTree.FindDescendant<Button>(c, b => (b.Content as string) == "Split here") is not null;
        bool name = VisualTree.FindDescendant<TextBlock>(c, t => t.Text == rowB.Name) is not null;
        var img = VisualTree.FindDescendant<Image>(c, i => Thumb.GetKey(i) == rowB.ThumbKey);
        bool noBannerOnA = VisualTree.FindDescendant<FrameworkElement>(list.ContainerFor(page.Vm.Videos.Clips[0])!, e => e.Name == "DayBanner")?.Visibility != Visibility.Visible;
        bool ok = rowB.Banner is not null && banner?.Visibility == Visibility.Visible && splitHere && name && img is not null && noBannerOnA;
        return ok ? SelfTestCheck.Pass("template.clipRow", $"row '{rowB.Name}' carries banner '{rowB.Banner?.Text}' with [Split here]")
                  : SelfTestCheck.Fail("template.clipRow", $"banner {banner?.Visibility} split {splitHere} name {name} thumb {img is not null} noBannerOnA {noBannerOnA}");
    }

    private static async Task<SelfTestCheck> ThumbKeyRecheck(SelfTestContext ctx)
    {
        await EnsureReviewAsync(ctx);
        var cache = ctx.Services.Thumbnails;
        var a = new ItemId(SelfTestFixture.ClipA);
        var b = new ItemId(SelfTestFixture.ClipB);
        cache.Clear();
        var image = new Image();
        Thumb.SetKey(image, a);
        Thumb.SetKey(image, b);                                  // recycled before A's load finished
        var imgA = await cache.GetAsync(a);
        var imgB = await cache.GetAsync(b);
        await WaitUntilAsync(() => image.Source is not null, TimeSpan.FromSeconds(3));
        await Task.Delay(300);                                   // let any late completion of A run
        bool ok = imgA is not null && imgB is not null && !ReferenceEquals(imgA, imgB) && ReferenceEquals(image.Source, imgB);
        return ok ? SelfTestCheck.Pass("thumb.keyRecheck", "late load for the old key was not assigned; Source is the new key's image")
                  : SelfTestCheck.Fail("thumb.keyRecheck", $"A {imgA is not null}, B {imgB is not null}, source is B {ReferenceEquals(image.Source, imgB)}");
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only template.clipRow,thumb.keyRecheck`
Expected: `CS0246: The type or namespace name 'ClipListView' could not be found` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```xml
<!-- src/UasSort.App/Controls/ClipListView.xaml — Ref §9.5 -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.Controls.ClipListView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctl="using:UasSort.App.Controls"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <!-- Shared column widths (header and rows): include 32 · 96 px thumbnail 104 · name * · local time 150 · size 72 · status 110 · miles 64 -->
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <Grid Padding="12,4" ColumnSpacing="8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="32" />
                <ColumnDefinition Width="104" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="150" />
                <ColumnDefinition Width="72" />
                <ColumnDefinition Width="110" />
                <ColumnDefinition Width="64" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="2" Text="Clips in selected group" Style="{StaticResource CaptionText}" />
            <TextBlock Grid.Column="3" Text="Local time" Style="{StaticResource CaptionText}" />
            <TextBlock Grid.Column="4" Text="Size" Style="{StaticResource CaptionText}" />
            <TextBlock Grid.Column="5" Text="Status" Style="{StaticResource CaptionText}" />
            <TextBlock Grid.Column="6" Text="From centre" Style="{StaticResource CaptionText}" />
        </Grid>
        <ItemsView x:Name="Items" Grid.Row="1" SelectionMode="Extended" SelectionChanged="OnSelectionChanged"
                   ContextRequested="OnContextRequested">
            <ItemsView.ItemTemplate>
                <DataTemplate x:DataType="rv:ClipRowVm">
                    <ItemContainer>
                        <StackPanel>
                            <!-- Day-split banner: part of the new day's first row, not a separate item (Ref §9.5) -->
                            <Grid x:Name="DayBanner" Padding="12,4" ColumnSpacing="8" Visibility="{x:Bind ui:UiFormat.VisibleIfNotNull(Banner), Mode=OneWay}">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="{x:Bind Banner.Text, Mode=OneWay, FallbackValue=''}" Style="{StaticResource CaptionText}" VerticalAlignment="Center" />
                                <!-- "Likely separate outing" for an emphasised split (DaySplit.Emphasised) -->
                                <TextBlock Grid.Column="1" Text="{x:Bind Banner.EmphasisText, Mode=OneWay, FallbackValue=''}" FontWeight="SemiBold"
                                           Style="{StaticResource CaptionText}" VerticalAlignment="Center" />
                                <Button Grid.Column="2" Content="Split here" Command="{x:Bind Banner.SplitCommand, Mode=OneWay}" Padding="8,1" FontSize="12" />
                            </Grid>
                            <Grid x:Name="Row" Padding="12,2" ColumnSpacing="8" PointerEntered="OnRowPointerEntered" PointerExited="OnRowPointerExited">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="32" />
                                    <ColumnDefinition Width="104" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="150" />
                                    <ColumnDefinition Width="72" />
                                    <ColumnDefinition Width="110" />
                                    <ColumnDefinition Width="64" />
                                </Grid.ColumnDefinitions>
                                <!-- IsIncluded is shown; the click runs ToggleIncludedCommand (an edit), and the next plan re-sets the box -->
                                <CheckBox x:Name="IncludeBox" IsChecked="{x:Bind IsIncluded, Mode=OneWay}" Command="{x:Bind ToggleIncludedCommand}"
                                          IsEnabled="{x:Bind ui:UiFormat.Not(IsReadOnly), Mode=OneWay}" MinWidth="0" AutomationProperties.Name="Include" />
                                <Border Grid.Column="1" Width="96" Height="54" CornerRadius="3" Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                                    <Image ctl:Thumb.Key="{x:Bind ThumbKey}" Stretch="UniformToFill" />
                                </Border>
                                <Grid Grid.Column="2">
                                    <TextBlock Text="{x:Bind Name, Mode=OneWay}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                    <Button x:Name="SplitBeforeButton" Content="Split before" Command="{x:Bind SplitBeforeCommand}" Opacity="0"
                                            HorizontalAlignment="Right" Padding="6,1" FontSize="11" ToolTipService.ToolTip="Start a new group here (Ctrl+Shift+S)" />
                                </Grid>
                                <StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center"
                                            ToolTipService.ToolTip="{x:Bind TimeTooltip, Mode=OneWay}">
                                    <TextBlock Text="{x:Bind TimeGlyph, Mode=OneWay}" FontSize="12" VerticalAlignment="Center" />
                                    <TextBlock Text="{x:Bind TimeText, Mode=OneWay}" />
                                </StackPanel>
                                <TextBlock Grid.Column="4" Text="{x:Bind SizeText, Mode=OneWay}" VerticalAlignment="Center" />
                                <Border Grid.Column="5" Style="{StaticResource ChipBorder}" VerticalAlignment="Center" HorizontalAlignment="Left"
                                        ToolTipService.ToolTip="{x:Bind StatusTooltip, Mode=OneWay}">
                                    <TextBlock Text="{x:Bind StatusText, Mode=OneWay}" FontSize="11" />
                                </Border>
                                <TextBlock Grid.Column="6" Text="{x:Bind DistanceText, Mode=OneWay}" VerticalAlignment="Center" />
                            </Grid>
                        </StackPanel>
                    </ItemContainer>
                </DataTemplate>
            </ItemsView.ItemTemplate>
        </ItemsView>
    </Grid>
</UserControl>
```

```csharp
// src/UasSort.App/Controls/ClipListView.xaml.cs
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace UasSort.App.Controls;

public sealed partial class ClipListView : UserControl
{
    private ReviewVm? _review;
    private bool _syncing;

    public ClipListView() => InitializeComponent();

    public void Attach(ReviewVm review)
    {
        if (_review is not null) _review.Videos.ClipSelectionChanged -= SyncSelectionFromVm;
        _review = review;
        Items.ItemsSource = review.Videos.Clips;
        review.Videos.ClipSelectionChanged += SyncSelectionFromVm;   // map clicks (Ctrl adds) change the selection
    }

    public ItemContainer? ContainerFor(ClipRowVm row) =>
        VisualTree.FindDescendant<ItemContainer>(Items, c => ReferenceEquals(c.DataContext, row));

    /// <summary>Selects these clips in the VM (which highlights their dots, Ref §9.6 Sync) and in the list.</summary>
    public void SelectRows(IReadOnlyList<ItemId> ids)
    {
        if (_review is null) return;
        _review.Videos.SetSelectedClips(ids);
        SyncSelectionFromVm(ids);
    }

    public void FocusList()
    {
        if (_review is null) return;
        var videos = _review.Videos;
        var target = videos.Clips.FirstOrDefault(r => videos.SelectedClipIds.Contains(r.Id)) ?? videos.Clips.FirstOrDefault();
        if (target is not null && ContainerFor(target) is { } c) c.Focus(FocusState.Programmatic);
        else Items.Focus(FocusState.Programmatic);
    }

    /// <summary>The row whose container (or a descendant that is not a text box) has keyboard focus.</summary>
    public ClipRowVm? FocusedRow()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        return VisualTree.FindAncestor<ItemContainer>(focused)?.DataContext as ClipRowVm;
    }

    private void OnSelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        if (_syncing || _review is null) return;
        _review.Videos.SetSelectedClips([.. sender.SelectedItems.OfType<ClipRowVm>().Select(r => r.Id)]);   // highlights their dots
    }

    /// <summary>Mirrors the VM's selection (after a map click, or SelectRows) in the list.</summary>
    private void SyncSelectionFromVm(IReadOnlyList<ItemId> ids)
    {
        if (_review is null) return;
        var clips = _review.Videos.Clips;
        _syncing = true;
        try
        {
            Items.DeselectAll();
            for (int i = 0; i < clips.Count; i++)
            {
                if (!ids.Contains(clips[i].Id)) continue;
                Items.Select(i);
                Items.StartBringItemIntoView(i, new BringIntoViewOptions());
            }
        }
        finally { _syncing = false; }
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (_review is null) return;
        var row = VisualTree.FindAncestor<ItemContainer>(args.OriginalSource as DependencyObject)?.DataContext as ClipRowVm;
        if (row is not null && !_review.Videos.SelectedClipIds.Contains(row.Id)) SelectRows([row.Id]);
        var menu = ClipMenu.Build(_review, row);
        if (args.TryGetPosition(Items, out var point)) menu.ShowAt(Items, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        else menu.ShowAt(Items);
        args.Handled = true;
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => SetSplitButtonOpacity(sender, 1);
    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => SetSplitButtonOpacity(sender, 0);

    private static void SetSplitButtonOpacity(object row, double opacity)
    {
        if (row is Grid g && VisualTree.FindDescendant<Button>(g, b => b.Name == "SplitBeforeButton") is { } b) b.Opacity = opacity;
    }
}
```

```csharp
// src/UasSort.App/Controls/ClipMenu.cs — Ref §9.5 context menu (also used for the map's contextMenu message, Ref §9.6)
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace UasSort.App.Controls;

public static class ClipMenu
{
    public static MenuFlyout Build(ReviewVm review, ClipRowVm? row)
    {
        var menu = new MenuFlyout();
        var toNew = new MenuFlyoutItem { Text = "Move selected to new group" };
        toNew.Click += async (_, _) => await review.MoveSelectedToNewGroupAsync();
        menu.Items.Add(toNew);
        var moveTo = new MenuFlyoutSubItem { Text = "Move to group ▸" };
        foreach (var g in review.MoveTargets())                 // every other card that is not AlreadyImported
        {
            var item = new MenuFlyoutItem { Text = string.IsNullOrEmpty(g.Description) ? g.DateRangeText : g.DateRangeText + " · " + g.Description };
            item.Click += async (_, _) => await review.MoveSelectedToGroupAsync(g);
            moveTo.Items.Add(item);
        }
        moveTo.IsEnabled = moveTo.Items.Count > 0;
        menu.Items.Add(moveTo);
        menu.Items.Add(new MenuFlyoutSeparator());
        var include = new MenuFlyoutItem { Text = "Include" };
        include.Click += async (_, _) => await review.SetIncludedAsync(review.Videos.SelectedClipIds, true);
        var exclude = new MenuFlyoutItem { Text = "Exclude" };
        exclude.Click += async (_, _) => await review.SetIncludedAsync(review.Videos.SelectedClipIds, false);
        menu.Items.Add(include);
        menu.Items.Add(exclude);
        if (row is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var play = new MenuFlyoutItem { Text = "Open in default player" };
            play.Click += (_, _) => review.OpenClip(row.Id);
            var copy = new MenuFlyoutItem { Text = "Copy card path" };
            copy.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(CardPath(review, row));
                Clipboard.SetContent(package);
            };
            menu.Items.Add(play);
            menu.Items.Add(copy);
        }
        return menu;
    }

    /// <summary>The clip's full path on the card (registry Part 11 item 16).</summary>
    public static string CardPath(ReviewVm review, ClipRowVm row) =>
        PathRules.Join(review.Plan.Base.Scan.Inventory.Source.Root, row.Id.CardRelPath.Replace('/', '\\'));
}
```

In `ReviewPage.xaml.cs`: add to `OnReviewAttached(ReviewVm? previous)`:

```csharp
        ClipListSlot.Attach(Vm);
        if (previous is not null) previous.MapContextMenuRequested -= OnMapContextMenu;
        Vm.MapContextMenuRequested += OnMapContextMenu;
```

add `Map.FocusReturnRequested += () => ClipListSlot.FocusList();` to the constructor after `InitializeComponent();`, and add this member:

```csharp
    // Ref §9.6: the map's contextMenu (through ReviewVm.MapContextMenuRequested) → a WinUI MenuFlyout at the pointer
    // (CSS px = DIPs at WebView2 zoom 1); the clicked dots become the clip selection first.
    private void OnMapContextMenu(MapContextMenu menu)
    {
        if (Vm is null) return;
        var ids = Vm.Videos.Clips.Where(r => menu.ItemIds.Contains(r.Id.CardRelPath)).Select(r => r.Id).ToList();
        if (ids.Count > 0) ClipListSlot.SelectRows(ids);
        var row = Vm.Videos.Clips.FirstOrDefault(r => ids.Contains(r.Id));
        ClipMenu.Build(Vm, row).ShowAt(Map, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(menu.X, menu.Y),
        });
    }
```

- [ ] **Step 4: Run them and watch them pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only template.clipRow,thumb.keyRecheck`
Expected: both `pass` (`row 'DJI_20260726041000_0002_D.MP4' carries banner 'Jul 25 → Jul 26 · …' with [Split here]`), `exit code 0`; XAML lint `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: clip list with in-row day-split banners, Thumb.Key thumbnails, map selection sync and context menu

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.13: Photos tab (day list + `LinedFlowLayout` wall) and Other tab

**Files:**
- Create: `src/UasSort.App/Controls/PhotosTab.xaml`, `Controls/PhotosTab.xaml.cs`, `Controls/OtherTab.xaml`, `Controls/OtherTab.xaml.cs`
- Modify: `src/UasSort.App/Pages/ReviewPage.xaml` (replace `<Border x:Name="PhotosSlot" Grid.Row="2" Visibility="Collapsed" />` with `<ctl:PhotosTab x:Name="PhotosSlot" Grid.Row="2" Visibility="Collapsed" />` and `<Border x:Name="OtherSlot" Grid.Row="2" Visibility="Collapsed" />` with `<ctl:OtherTab x:Name="OtherSlot" Grid.Row="2" Visibility="Collapsed" />`), `Pages/ReviewPage.xaml.cs` (`OnReviewAttached`: add `PhotosSlot.Attach(Vm);` and `OtherSlot.Vm = Vm.Other;`)
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.cs` (register `template.photoTile`, `template.otherTab`), `SelfTest/SelfTestChecks.Review.cs`

**Interfaces:**
- Consumes (registry Review VMs table): `PhotosTabVm` (`Days`, `Tiles`, `SelectedDay`, `ShowImportedDays`, `ShowImportedText`), `PhotoDayVm` (`DayText`, `CountsText`, `StatusText`, `Reason`, `IsIncluded` (`bool?`), `ToggleCommand`), `PhotoTileVm` (`ThumbKey`, `Text`, `StatusText`, `PairText`, `IsIncluded`, `CanUndo`, `ToggleCommand`, `UndoCommand`), `OtherTabVm` (`Sections`), `OtherSectionVm` (`Title`, `Note`, `Rows`, `IsCollapsed`), `OtherRowVm` (`Text`, `Detail`, `UndismissCommand`), `ReviewVm.Photos`/`Other`/`SelectedTab`.
- Produces (defined here): `PhotosTab : UserControl` — `ItemsView DayList`, `ItemsView Wall`, `PhotosTabVm? Vm`, `void Attach(ReviewVm review)`, `ItemContainer? ContainerFor(PhotoTileVm tile)`, `PhotoTileVm? FocusedTile()`; `OtherTab : UserControl` — `OtherTabVm? Vm { get; set; }`; checks `template.photoTile`, `template.otherTab`.

- [ ] **Step 1: Write the failing checks**

Register `("template.photoTile", TemplatePhotoTile), ("template.otherTab", TemplateOtherTab),` and append:

```csharp
    private static async Task<SelfTestCheck> TemplatePhotoTile(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        page.Vm.SelectedTab = 1;
        var photos = VisualTree.FindDescendant<PhotosTab>(page)!;
        page.Vm.Photos.SelectedDay = page.Vm.Photos.Days[0];
        if (!await WaitUntilAsync(() => page.Vm.Photos.Tiles.Count == 1 && photos.ContainerFor(page.Vm.Photos.Tiles[0]) is not null, TimeSpan.FromSeconds(5)))
            return SelfTestCheck.Fail("template.photoTile", $"tiles {page.Vm.Photos.Tiles.Count}");
        var tile = page.Vm.Photos.Tiles[0];
        var c = photos.ContainerFor(tile)!;
        bool thumb = VisualTree.FindDescendant<Image>(c, i => Thumb.GetKey(i).CardRelPath == SelfTestFixture.Dng) is not null;
        bool status = VisualTree.FindDescendant<TextBlock>(c, t => t.Text == tile.StatusText && t.Text.Length > 0) is not null;
        bool dayRow = VisualTree.FindDescendant<TextBlock>(photos.DayList, t => t.Text == page.Vm.Photos.Days[0].DayText) is not null;
        bool layout = photos.Wall.Layout is LinedFlowLayout;
        page.Vm.SelectedTab = 0;
        return thumb && status && dayRow && layout
            ? SelfTestCheck.Pass("template.photoTile", $"day '{page.Vm.Photos.Days[0].DayText}', tile '{tile.StatusText}' on a LinedFlowLayout wall")
            : SelfTestCheck.Fail("template.photoTile", $"thumb {thumb} status {status} day {dayRow} lined {layout}");
    }

    private static async Task<SelfTestCheck> TemplateOtherTab(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        page.Vm.SelectedTab = 2;
        var other = VisualTree.FindDescendant<OtherTab>(page)!;
        await WaitUntilAsync(() => other.IsLoaded, TimeSpan.FromSeconds(3));
        int expanders = VisualTree.FindAll<Expander>(other).Count;
        page.Vm.SelectedTab = 0;
        return expanders == page.Vm.Other.Sections.Count
            ? SelfTestCheck.Pass("template.otherTab", $"{expanders} sections rendered")
            : SelfTestCheck.Fail("template.otherTab", $"{expanders} expanders for {page.Vm.Other.Sections.Count} sections");
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only template.photoTile,template.otherTab`
Expected: `CS0246: The type or namespace name 'PhotosTab' could not be found` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```xml
<!-- src/UasSort.App/Controls/PhotosTab.xaml — Ref §9.8 -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.Controls.PhotosTab"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctl="using:UasSort.App.Controls"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <Grid ColumnSpacing="8" Padding="8">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="340" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Grid RowSpacing="4">
            <Grid.RowDefinitions>
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <ItemsView x:Name="DayList" SelectionMode="Single" SelectionChanged="OnDaySelectionChanged">
                <ItemsView.ItemTemplate>
                    <DataTemplate x:DataType="rv:PhotoDayVm">
                        <ItemContainer>
                            <Grid Padding="8,6" ColumnSpacing="8">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>
                                <!-- tri-state IsIncluded (bool?) is shown; the click runs ToggleCommand (SetDayIncluded) -->
                                <CheckBox IsThreeState="True" IsChecked="{x:Bind IsIncluded, Mode=OneWay}" Command="{x:Bind ToggleCommand}"
                                          MinWidth="0" VerticalAlignment="Top" AutomationProperties.Name="Include this day" />
                                <StackPanel Grid.Column="1" Spacing="2">
                                    <TextBlock Text="{x:Bind DayText, Mode=OneWay}" FontWeight="SemiBold" />
                                    <TextBlock Text="{x:Bind CountsText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                                    <TextBlock Text="{x:Bind StatusText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                                    <TextBlock Text="{x:Bind Reason, Mode=OneWay}" Style="{StaticResource CaptionText}" TextWrapping="Wrap" />
                                </StackPanel>
                            </Grid>
                        </ItemContainer>
                    </DataTemplate>
                </ItemsView.ItemTemplate>
            </ItemsView>
            <ToggleSwitch Grid.Row="1" Header="{x:Bind Vm.ShowImportedText, Mode=OneWay}" IsOn="{x:Bind Vm.ShowImportedDays, Mode=TwoWay}" />
        </Grid>
        <ItemsView x:Name="Wall" Grid.Column="1" SelectionMode="Extended">
            <ItemsView.Layout>
                <LinedFlowLayout LineHeight="150" LineSpacing="6" MinItemSpacing="6" />
            </ItemsView.Layout>
            <ItemsView.ItemTemplate>
                <DataTemplate x:DataType="rv:PhotoTileVm">
                    <ItemContainer>
                        <Grid Width="200" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" CornerRadius="4" Padding="4" RowSpacing="2">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="*" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>
                            <Border Background="{ThemeResource SubtleFillColorSecondaryBrush}" CornerRadius="3">
                                <Image ctl:Thumb.Key="{x:Bind ThumbKey}" Stretch="Uniform" />
                            </Border>
                            <CheckBox x:Name="TileInclude" IsChecked="{x:Bind IsIncluded, Mode=OneWay}" Command="{x:Bind ToggleCommand}"
                                      MinWidth="0" Margin="4" HorizontalAlignment="Left" VerticalAlignment="Top" AutomationProperties.Name="Include" />
                            <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="4">
                                <!-- StatusText also carries the set and name-clash wording -->
                                <Border Style="{StaticResource ChipBorder}"><TextBlock Text="{x:Bind StatusText, Mode=OneWay}" FontSize="11" /></Border>
                                <Border Style="{StaticResource ChipBorder}" Visibility="{x:Bind ui:UiFormat.VisibleIfText(PairText), Mode=OneWay}">
                                    <TextBlock Text="{x:Bind PairText, Mode=OneWay}" FontSize="11" />
                                </Border>
                                <Button Content="Undo" Command="{x:Bind UndoCommand}" Padding="6,0" FontSize="11"
                                        Visibility="{x:Bind ui:UiFormat.Visible(CanUndo), Mode=OneWay}" />
                            </StackPanel>
                            <StackPanel Grid.Row="2">
                                <TextBlock Text="{x:Bind Text, Mode=OneWay}" Style="{StaticResource CaptionText}" TextWrapping="Wrap" />
                            </StackPanel>
                        </Grid>
                    </ItemContainer>
                </DataTemplate>
            </ItemsView.ItemTemplate>
        </ItemsView>
    </Grid>
</UserControl>
```

```csharp
// src/UasSort.App/Controls/PhotosTab.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace UasSort.App.Controls;

public sealed partial class PhotosTab : UserControl
{
    private ReviewVm? _review;
    private bool _syncing;

    public PhotosTab() => InitializeComponent();

    public PhotosTabVm? Vm { get; private set; }

    public void Attach(ReviewVm review)
    {
        if (Vm is not null) Vm.PropertyChanged -= OnVmChanged;
        _review = review;
        Vm = review.Photos;
        Bindings.Update();
        DayList.ItemsSource = Vm.Days;
        Wall.ItemsSource = Vm.Tiles;
        Vm.PropertyChanged += OnVmChanged;
        SyncDay();
    }

    public ItemContainer? ContainerFor(PhotoTileVm tile) =>
        VisualTree.FindDescendant<ItemContainer>(Wall, c => ReferenceEquals(c.DataContext, tile));

    public PhotoTileVm? FocusedTile() =>
        VisualTree.FindAncestor<ItemContainer>(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject)?.DataContext as PhotoTileVm;

    private void OnDaySelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        if (!_syncing && Vm is not null) Vm.SelectedDay = sender.SelectedItem as PhotoDayVm;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotosTabVm.SelectedDay)) SyncDay();
    }

    private void SyncDay()
    {
        if (Vm is null) return;
        _syncing = true;
        try
        {
            int i = Vm.SelectedDay is { } d ? Vm.Days.IndexOf(d) : -1;
            if (i < 0) DayList.DeselectAll();
            else if (!ReferenceEquals(DayList.SelectedItem, Vm.SelectedDay)) DayList.Select(i);
        }
        finally { _syncing = false; }
    }
}
```

```xml
<!-- src/UasSort.App/Controls/OtherTab.xaml — Ref §9.9 -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.Controls.OtherTab"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <ScrollViewer Padding="12,8">
        <ItemsControl ItemsSource="{x:Bind Vm.Sections, Mode=OneWay}">
            <ItemsControl.ItemTemplate>
                <!-- OtherSectionVm is immutable (rebuilt per plan): IsCollapsed sets the initial state, the user may expand -->
                <DataTemplate x:DataType="rv:OtherSectionVm">
                    <Expander Header="{x:Bind Title}" IsExpanded="{x:Bind ui:UiFormat.Not(IsCollapsed)}"
                              HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" Margin="0,0,0,6">
                        <StackPanel Spacing="4">
                            <TextBlock Text="{x:Bind Note}" Style="{StaticResource CaptionText}" TextWrapping="Wrap"
                                       Visibility="{x:Bind ui:UiFormat.VisibleIfText(Note)}" />
                            <ItemsControl ItemsSource="{x:Bind Rows}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate x:DataType="rv:OtherRowVm">
                                        <Grid ColumnSpacing="8" Padding="0,3">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="Auto" />
                                            </Grid.ColumnDefinitions>
                                            <StackPanel>
                                                <TextBlock Text="{x:Bind Text}" TextTrimming="CharacterEllipsis" IsTextSelectionEnabled="True" />
                                                <TextBlock Text="{x:Bind Detail}" Style="{StaticResource CaptionText}" TextWrapping="Wrap"
                                                           Visibility="{x:Bind ui:UiFormat.VisibleIfText(Detail)}" />
                                            </StackPanel>
                                            <Button Grid.Column="1" Content="Un-dismiss" Command="{x:Bind UndismissCommand}"
                                                    Visibility="{x:Bind ui:UiFormat.VisibleIfNotNull(UndismissCommand)}" VerticalAlignment="Center" />
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </Expander>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</UserControl>
```

```csharp
// src/UasSort.App/Controls/OtherTab.xaml.cs
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class OtherTab : UserControl
{
    private OtherTabVm? _vm;
    public OtherTab() => InitializeComponent();
    public OtherTabVm? Vm { get => _vm; set { _vm = value; Bindings.Update(); } }
}
```

Apply the `ReviewPage` modifications listed under **Files**.

- [ ] **Step 4: Run them and watch them pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only template.photoTile,template.otherTab`
Expected: both `pass`, `exit code 0`; XAML lint `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: Photos tab (tri-state days, LinedFlowLayout wall) and Other tab

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.14: Keyboard — page accelerators (modified keys only), list-scoped keys, undo/redo routing

Every Review key goes through Part 10's `ReviewVm.HandleKey(ReviewKey, KeyMods, KeyFocus)` (Ref §9.12): the App only maps `VirtualKey` to `ReviewKey`, the modifier state to `KeyMods`, and the focused element to `KeyFocus` (`TextBox` for any text input, `TimelineItem`/`ClipItem`/`PhotoItem` for a focused item container), sets `ReviewVm.FocusedClip`/`FocusedTile`, and sets `Handled` from the returned bool. Only Tab (timeline → clip list) and the F2 focus move are App-side.

**Files:**
- Create: `src/UasSort.App/Services/KeyRouting.cs`
- Modify: `src/UasSort.App/Pages/ReviewPage.xaml.cs` (accelerators, `FocusRenameRequested`), `Controls/TimelineView.xaml` + `.cs` (F2, Tab), `Controls/ClipListView.xaml` + `.cs` (Space, Ctrl+Shift+S), `Controls/PhotosTab.xaml` + `.cs` (Space), `Pages/CardPage.xaml` + `.cs` (F5)
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.cs` (register `keys.spaceInRenameBox`, `keys.accelerators`), `SelfTest/SelfTestChecks.Review.cs`

**Interfaces:**
- Consumes (registry Review VMs table): `ReviewKey { Z, Y, M, N, S, D1, D2, D3, F2, F5, Enter, Space }`, `KeyMods { None, Ctrl, Shift, Alt }`, `KeyFocus { Other, TextBox, TimelineItem, ClipItem, PhotoItem }`, `ReviewVm.HandleKey(ReviewKey, KeyMods, KeyFocus)`, `ReviewVm.FocusedClip`, `ReviewVm.FocusedTile`, `ReviewVm.FocusRenameRequested`, `ReviewVm.SelectedTab`, `VideosTabVm.CardFor(ItemId)`, `VideosTabVm.SelectedClipIds`, `CardStageVm.RescanCommand`.
- Produces (defined here):
  - `static class KeyRouting` — `bool IsTextInput(object? focused)`, `bool IsInItemContainer(object? focused)`, `bool IsCheckBox(object? focused)`, `KeyMods Modifiers()`, `KeyMods ToMods(VirtualKeyModifiers m)`, `ReviewKey? ToReviewKey(VirtualKey key)`, `KeyFocus FocusOf(object? focused)`.
  - `ReviewPage.AcceleratorTable` (`IReadOnlyList<(string Action, VirtualKey Key, VirtualKeyModifiers Modifiers)>`), `bool ReviewPage.TryHandleAccelerator(string action, object? focused)`.
  - `bool TimelineView.HandleKey(VirtualKey key, KeyMods mods, object? focused)`, `bool ClipListView.HandleKey(VirtualKey key, KeyMods mods, object? focused)`, `bool PhotosTab.HandleKey(VirtualKey key, KeyMods mods, object? focused)` (synchronous, so `Handled` counts); `ClipListView? TimelineView.ClipList { get; set; }`.
  - Checks `keys.spaceInRenameBox`, `keys.accelerators`.

- [ ] **Step 1: Write the failing checks**

Register `("keys.spaceInRenameBox", KeysSpaceInRenameBox), ("keys.accelerators", KeysAccelerators),` and append (add `using Microsoft.UI.Xaml.Input; using Windows.System;`):

```csharp
    // Ref §9.12 / §13: "A smoke test types a space into the rename box and checks that Included is unchanged."
    private static async Task<SelfTestCheck> KeysSpaceInRenameBox(SelfTestContext ctx)
    {
        var (page, timeline, groups) = await TimelineAsync(ctx);
        page.Vm.Videos.SelectedEntry = groups[0];
        var list = VisualTree.FindDescendant<ClipListView>(page)!;
        await WaitUntilAsync(() => page.Vm.Videos.Clips.Count == 2 && list.ContainerFor(page.Vm.Videos.Clips[1]) is not null, TimeSpan.FromSeconds(5));
        var before = page.Vm.Videos.Clips.Select(c => c.IsIncluded).ToList();

        var box = VisualTree.FindDescendant<AutoSuggestBox>(timeline.ContainerFor(groups[0])!, b => b.Name == "DescriptionBox")!;
        box.Focus(FocusState.Keyboard);
        await WaitUntilAsync(() => KeyRouting.IsTextInput(FocusManager.GetFocusedElement(page.XamlRoot)), TimeSpan.FromSeconds(2));
        var focused = FocusManager.GetFocusedElement(page.XamlRoot);
        bool t = timeline.HandleKey(VirtualKey.Space, KeyMods.None, focused);
        bool c = list.HandleKey(VirtualKey.Space, KeyMods.None, focused);
        await Task.Delay(300);                                   // any (wrong) edit would have been applied by now
        var afterText = page.Vm.Videos.Clips.Select(x => x.IsIncluded).ToList();

        var rowB = page.Vm.Videos.Clips[1];
        var container = list.ContainerFor(rowB)!;
        container.Focus(FocusState.Keyboard);
        await WaitUntilAsync(() => KeyRouting.IsInItemContainer(FocusManager.GetFocusedElement(page.XamlRoot)), TimeSpan.FromSeconds(2));
        bool handled = list.HandleKey(VirtualKey.Space, KeyMods.None, FocusManager.GetFocusedElement(page.XamlRoot));
        bool toggled = await WaitUntilAsync(() => page.Vm.Videos.Clips.Count == 2 && page.Vm.Videos.Clips[1].IsIncluded != before[1], TimeSpan.FromSeconds(3));
        if (toggled) list.HandleKey(VirtualKey.Space, KeyMods.None, FocusManager.GetFocusedElement(page.XamlRoot));   // restore
        bool ok = !t && !c && afterText.SequenceEqual(before) && handled && toggled;
        return ok ? SelfTestCheck.Pass("keys.spaceInRenameBox", "space in the rename box changed nothing; space on a clip row toggled it")
                  : SelfTestCheck.Fail("keys.spaceInRenameBox", $"timeline {t} list {c} unchanged {afterText.SequenceEqual(before)} rowHandled {handled} toggled {toggled}");
    }

    private static async Task<SelfTestCheck> KeysAccelerators(SelfTestContext ctx)
    {
        var (page, timeline, groups) = await TimelineAsync(ctx);
        string[] expected = ["undo", "redo", "mergeNext", "moveToNewGroup", "tab1", "tab2", "tab3", "rescan", "offload"];
        var actions = ReviewPage.AcceleratorTable.Select(a => a.Action).Distinct().ToList();
        bool unmodifiedLetters = ReviewPage.AcceleratorTable.Any(a => a.Modifiers == VirtualKeyModifiers.None && a.Key != VirtualKey.F5);
        var box = VisualTree.FindDescendant<AutoSuggestBox>(timeline.ContainerFor(groups[0])!, b => b.Name == "DescriptionBox")!;
        box.Focus(FocusState.Keyboard);
        await WaitUntilAsync(() => KeyRouting.IsTextInput(FocusManager.GetFocusedElement(page.XamlRoot)), TimeSpan.FromSeconds(2));
        bool undoInText = page.TryHandleAccelerator("undo", FocusManager.GetFocusedElement(page.XamlRoot));
        bool tab = page.TryHandleAccelerator("tab2", FocusManager.GetFocusedElement(page.XamlRoot)) && page.Vm.SelectedTab == 1;
        page.Vm.SelectedTab = 0;
        bool ok = expected.All(actions.Contains) && page.KeyboardAccelerators.Count == ReviewPage.AcceleratorTable.Count
                  && !unmodifiedLetters && !undoInText && tab;
        return ok ? SelfTestCheck.Pass("keys.accelerators", $"{page.KeyboardAccelerators.Count} accelerators; Ctrl+Z left to the TextBox; Ctrl+2 → Photos")
                  : SelfTestCheck.Fail("keys.accelerators", $"actions [{string.Join(",", actions)}] unmodified {unmodifiedLetters} undoInText {undoInText} tab {tab}");
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only keys.spaceInRenameBox,keys.accelerators`
Expected: `CS0103: The name 'KeyRouting' does not exist in the current context` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.App/Services/KeyRouting.cs — Ref §9.12: page keys use modifiers; list keys act only on items, never in a TextBox
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.UI.Core;

namespace UasSort.App.Services;

public static class KeyRouting
{
    public static bool IsTextInput(object? focused) =>
        focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox
        || (focused is DependencyObject d && (VisualTree.FindAncestor<AutoSuggestBox>(d) is not null
                                              || VisualTree.FindAncestor<TextBox>(d) is not null
                                              || VisualTree.FindAncestor<NumberBox>(d) is not null));

    public static bool IsCheckBox(object? focused) => focused is CheckBox;

    public static bool IsInItemContainer(object? focused) =>
        focused is DependencyObject d && !IsTextInput(focused) && VisualTree.FindAncestor<ItemContainer>(d) is not null;

    public static KeyMods Modifiers()
    {
        static bool Down(VirtualKey k) => InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);
        return (Down(VirtualKey.Control) ? KeyMods.Ctrl : KeyMods.None)
             | (Down(VirtualKey.Shift) ? KeyMods.Shift : KeyMods.None)
             | (Down(VirtualKey.Menu) ? KeyMods.Alt : KeyMods.None);
    }

    public static KeyMods ToMods(VirtualKeyModifiers m) =>
        (m.HasFlag(VirtualKeyModifiers.Control) ? KeyMods.Ctrl : KeyMods.None)
        | (m.HasFlag(VirtualKeyModifiers.Shift) ? KeyMods.Shift : KeyMods.None)
        | (m.HasFlag(VirtualKeyModifiers.Menu) ? KeyMods.Alt : KeyMods.None);

    /// <summary>The keys ReviewVm.HandleKey knows (Part 10's ReviewKey); any other key is not the Review's.</summary>
    public static ReviewKey? ToReviewKey(VirtualKey key) => key switch
    {
        VirtualKey.Z => ReviewKey.Z,
        VirtualKey.Y => ReviewKey.Y,
        VirtualKey.M => ReviewKey.M,
        VirtualKey.N => ReviewKey.N,
        VirtualKey.S => ReviewKey.S,
        VirtualKey.Number1 => ReviewKey.D1,
        VirtualKey.Number2 => ReviewKey.D2,
        VirtualKey.Number3 => ReviewKey.D3,
        VirtualKey.F2 => ReviewKey.F2,
        VirtualKey.F5 => ReviewKey.F5,
        VirtualKey.Enter => ReviewKey.Enter,
        VirtualKey.Space => ReviewKey.Space,
        _ => null,
    };

    /// <summary>Where the key was pressed: any text input is TextBox (keys stay the box's own), else the kind of focused item.</summary>
    public static KeyFocus FocusOf(object? focused)
    {
        if (IsTextInput(focused)) return KeyFocus.TextBox;
        return VisualTree.FindAncestor<ItemContainer>(focused as DependencyObject)?.DataContext switch
        {
            TimelineEntryVm => KeyFocus.TimelineItem,
            ClipRowVm => KeyFocus.ClipItem,
            PhotoTileVm => KeyFocus.PhotoItem,
            _ => KeyFocus.Other,
        };
    }
}
```

In `ReviewPage.xaml.cs` add (`using Microsoft.UI.Xaml.Input; using Windows.System;`), and call `RegisterAccelerators();` in the constructor after `InitializeComponent();`:

```csharp
    /// <summary>Ref §9.12 page-level accelerators (modified keys only; F5 is a function key).</summary>
    public static readonly IReadOnlyList<(string Action, VirtualKey Key, VirtualKeyModifiers Modifiers)> AcceleratorTable =
    [
        ("undo", VirtualKey.Z, VirtualKeyModifiers.Control),
        ("redo", VirtualKey.Y, VirtualKeyModifiers.Control),
        ("redo", VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
        ("mergeNext", VirtualKey.M, VirtualKeyModifiers.Control),
        ("moveToNewGroup", VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
        ("tab1", VirtualKey.Number1, VirtualKeyModifiers.Control),
        ("tab2", VirtualKey.Number2, VirtualKeyModifiers.Control),
        ("tab3", VirtualKey.Number3, VirtualKeyModifiers.Control),
        ("rescan", VirtualKey.F5, VirtualKeyModifiers.None),
        ("offload", VirtualKey.Enter, VirtualKeyModifiers.Control),
    ];

    private void RegisterAccelerators()
    {
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        foreach (var (_, key, mods) in AcceleratorTable)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = mods };
            accelerator.Invoked += (_, e) => e.Handled = HandleReviewKey(key, mods, FocusManager.GetFocusedElement(XamlRoot));
            KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>Runs one accelerator of the table; false lets the key through (Ctrl+Z/Y in a text box are the box's own undo).</summary>
    public bool TryHandleAccelerator(string action, object? focused)
    {
        var (_, key, mods) = AcceleratorTable.First(a => string.Equals(a.Action, action, StringComparison.Ordinal));
        return HandleReviewKey(key, mods, focused);
    }

    /// <summary>ReviewVm.HandleKey decides (Part 10): undo/redo, merge, move, tabs, F5 rescan (RescanRequested), Ctrl+Enter offload.</summary>
    private bool HandleReviewKey(VirtualKey key, VirtualKeyModifiers mods, object? focused) =>
        Vm is not null && KeyRouting.ToReviewKey(key) is { } k && Vm.HandleKey(k, KeyRouting.ToMods(mods), KeyRouting.FocusOf(focused));

    /// <summary>ReviewVm.FocusRenameRequested (F2 on a card, or the "Name it" quick fix): select the card, then focus its rename box.</summary>
    private void OnFocusRenameRequested(ItemId anchor)
    {
        if (Vm?.Videos.CardFor(anchor) is not { } card) return;
        Vm.SelectedTab = 0;
        Vm.Videos.SelectedEntry = card;
        DispatcherQueue.TryEnqueue(() => TimelineSlot.FocusRenameBox(card));   // after the container is brought into view
    }
```

and in `OnReviewAttached(ReviewVm? previous)` add:

```csharp
        TimelineSlot.ClipList = ClipListSlot;                   // so Tab moves into the clip list
        if (previous is not null) previous.FocusRenameRequested -= OnFocusRenameRequested;
        Vm.FocusRenameRequested += OnFocusRenameRequested;
```

In `TimelineView.xaml`, add `PreviewKeyDown="OnItemsPreviewKeyDown"` to the `ItemsView`, and in `TimelineView.xaml.cs` add (`using Microsoft.UI.Xaml.Input; using Windows.System;`; the handler is synchronous so `Handled` takes effect):

```csharp
    public ClipListView? ClipList { get; set; }

    private void OnItemsPreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = HandleKey(e.Key, KeyRouting.Modifiers(), FocusManager.GetFocusedElement(XamlRoot));

    /// <summary>Timeline list keys (Ref §9.12), only on an item, never in a text box: Tab → clip list (App-side);
    /// F2 → ReviewVm.HandleKey(F2, …, TimelineItem), which raises FocusRenameRequested.</summary>
    public bool HandleKey(VirtualKey key, KeyMods mods, object? focused)
    {
        if (_review is null || !KeyRouting.IsInItemContainer(focused)) return false;
        if (key == VirtualKey.Tab && mods == KeyMods.None && ClipList is not null)
        {
            ClipList.FocusList();
            return true;
        }
        return key == VirtualKey.F2 && _review.HandleKey(ReviewKey.F2, mods, KeyFocus.TimelineItem);
    }
```

In `ClipListView.xaml`, add `PreviewKeyDown="OnItemsPreviewKeyDown"` to the `ItemsView`, and in `ClipListView.xaml.cs` add (`using Windows.System;`):

```csharp
    private void OnItemsPreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = HandleKey(e.Key, KeyRouting.Modifiers(), FocusManager.GetFocusedElement(XamlRoot));   // synchronous: Handled counts

    /// <summary>Clip-list keys (Ref §9.12) through ReviewVm.HandleKey: Space toggles the selected clips (the focused row is
    /// selected first), Ctrl+Shift+S splits before ReviewVm.FocusedClip. A focused CheckBox keeps Space; a text box keeps every key.</summary>
    public bool HandleKey(VirtualKey key, KeyMods mods, object? focused)
    {
        if (_review is null || KeyRouting.IsCheckBox(focused)
            || KeyRouting.ToReviewKey(key) is not { } k || k is not (ReviewKey.Space or ReviewKey.S)) return false;
        var focus = KeyRouting.FocusOf(focused);
        if (focus == KeyFocus.ClipItem && VisualTree.FindAncestor<ItemContainer>(focused as DependencyObject)?.DataContext is ClipRowVm row)
        {
            _review.FocusedClip = row;
            if (k == ReviewKey.Space && !_review.Videos.SelectedClipIds.Contains(row.Id)) SelectRows([row.Id]);
        }
        return _review.HandleKey(k, mods, focus);
    }
```

In `PhotosTab.xaml`, add `PreviewKeyDown="OnWallPreviewKeyDown"` to the `Wall` ItemsView, and in `PhotosTab.xaml.cs` add (`using Windows.System;`):

```csharp
    private void OnWallPreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = HandleKey(e.Key, KeyRouting.Modifiers(), FocusManager.GetFocusedElement(XamlRoot));

    /// <summary>Space on a photo tile toggles it through ReviewVm.HandleKey (ReviewVm.FocusedTile). A focused CheckBox keeps Space.</summary>
    public bool HandleKey(VirtualKey key, KeyMods mods, object? focused)
    {
        if (_review is null || key != VirtualKey.Space || KeyRouting.IsCheckBox(focused)) return false;
        var focus = KeyRouting.FocusOf(focused);
        if (focus == KeyFocus.PhotoItem && VisualTree.FindAncestor<ItemContainer>(focused as DependencyObject)?.DataContext is PhotoTileVm tile)
            _review.FocusedTile = tile;
        return _review.HandleKey(ReviewKey.Space, mods, focus);
    }
```

In `CardPage.xaml` add inside `<Page …>`:

```xml
    <Page.KeyboardAccelerators>
        <KeyboardAccelerator Key="F5" Invoked="OnRescanAccelerator" />
    </Page.KeyboardAccelerators>
```

and in `CardPage.xaml.cs`:

```csharp
    private void OnRescanAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Vm.RescanCommand.CanExecute(null)) Vm.RescanCommand.Execute(null);
        args.Handled = true;
    }
```

- [ ] **Step 4: Run them and watch them pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only keys.spaceInRenameBox,keys.accelerators`
Expected: both `pass` (`space in the rename box changed nothing; space on a clip row toggled it`; `10 accelerators; Ctrl+Z left to the TextBox; Ctrl+2 → Photos`), `exit code 0`. If a real Space press on a clip row still toggles the container's selection although `PreviewKeyDown` set `Handled`, leave Space to the row's focused CheckBox (Ref §9.12 note): let `OnItemsPreviewKeyDown` return without handling `VirtualKey.Space`, keep `HandleKey` for the check, and re-run.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: scoped keyboard handling (modified page accelerators, item-only list keys, TextBox-safe undo)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.15: Commit pages — Preflight sheet, Copy, Verdict

**Files:**
- Create: `src/UasSort.App/Pages/PreflightPage.xaml` + `.cs`, `Pages/CopyPage.xaml` + `.cs`, `Pages/VerdictPage.xaml` + `.cs`
- Create: `src/UasSort.App/Controls/IssueList.xaml` + `.cs`
- Modify: `src/UasSort.App/Pages/ShellPage.xaml.cs` (three `StagePages` entries), `SelfTest/SelfTestChecks.cs` (register `pages.construct`), `SelfTest/SelfTestChecks.Pages.cs`

**Interfaces:**
- Consumes (registry Review VMs table): `PreflightVm` (`Acks`, `Blocking`, `Warnings`, `Infos`, `FoldersToCreate`, `FoldersAppended`, `VolumeLines` — all `IReadOnlyList<string>` except `Acks` —, `CanStart`, `LockMessage`, `BackCommand`), `AckVm` (`Text`, `IsChecked`), `ShellVm.StartCopyAsync()`, `CopyVm` (`FilesText`, `BytesText`, `SpeedText`, `EtaText`, `CurrentText`, `PhaseText`, `Fraction`, `IsRunning`, `ErrorText`, `CancelCommand`), `VerdictVm` (`Level`, `Headline`, `LevelText`, `CategoryLines`, `CardChanges`, `SafeRemovalNote`, `SelectionText`, `CanCleanup`, `CleanupTooltip`, `NotCopied`, `NotCopiedDays`, `Ejects`, `Groups`, `RecordImportedCommand`, `MarkNotNeededCommand`, `UndoCommand`, `OpenPhotoRootCommand`, `OpenReportCommand`, `CleanupCommand`, `DoneCommand`, `ShowPlanCommand`), `NotCopiedRowVm` (`Text`, `Detail`, `SizeText`, `IsSelected`, `ToggleCommand`), `NotCopiedDayVm` (`Text`, `SelectDayCommand`), `EjectVm` (`Text`, `EjectCommand`, `ResultText`), `VerdictGroupRowVm` (`Text`, `Path`, `OpenFolderCommand`), `VerdictLevel`; `UiFormat.VerdictBrush` (Task 11.4).
- Produces (defined here): `PreflightPage`, `CopyPage`, `VerdictPage` (each `Vm` + `OnNavigatedTo(StageArgs)`); `IssueList : UserControl` (`void Show(IReadOnlyList<string> lines, InfoBarSeverity severity)`); check `pages.construct` (instantiates every stage page, so a XAML parse error fails the selftest even for stages the fixture never reaches).

- [ ] **Step 1: Write the failing check**

Register `("pages.construct", PagesConstruct),` and append to `SelfTestChecks.Pages.cs`:

```csharp
    /// <summary>Every stage page must load its XAML (x:Bind paths are compile-checked; markup errors only show at load).</summary>
    public static readonly List<Func<Page>> PageFactories =
    [
        () => new SetupPage(), () => new CardPage(), () => new ScanPage(), () => new SettingsPage(),
        () => new PreflightPage(), () => new CopyPage(), () => new VerdictPage(),
    ];

    private static Task<SelfTestCheck> PagesConstruct(SelfTestContext ctx)
    {
        var built = new List<string>();
        foreach (var make in PageFactories)
        {
            var page = make();                                   // InitializeComponent parses the page's XAML
            built.Add(page.GetType().Name);
        }
        return Task.FromResult(SelfTestCheck.Pass("pages.construct", "loaded " + string.Join(", ", built)));
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only pages.construct`
Expected: `CS0246: The type or namespace name 'PreflightPage' could not be found` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```xml
<!-- src/UasSort.App/Controls/IssueList.xaml — one InfoBar per preflight line (PreflightVm.Blocking / Warnings / Infos are strings) -->
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="UasSort.App.Controls.IssueList"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel x:Name="Host" Spacing="2" />
</UserControl>
```

```csharp
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
```

```xml
<!-- src/UasSort.App/Pages/PreflightPage.xaml — Ref §10.2 sheet -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.PreflightPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctl="using:UasSort.App.Controls"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <ScrollViewer>
            <!-- PreflightVm.Open() ran (synchronously) before this page was shown; the lists are the report's lines -->
            <StackPanel MaxWidth="900" Padding="24" Spacing="10">
                <TextBlock Text="Before the offload" Style="{StaticResource TitleTextBlockStyle}" />
                <InfoBar IsOpen="True" IsClosable="False" Severity="Error" Message="{x:Bind Vm.LockMessage, Mode=OneWay}"
                         Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.LockMessage), Mode=OneWay}" />
                <ctl:IssueList x:Name="BlockingList" />
                <TextBlock Text="Folders to create" Style="{StaticResource BodyStrongTextBlockStyle}" />
                <ItemsControl ItemsSource="{x:Bind Vm.FoldersToCreate, Mode=OneWay}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" IsTextSelectionEnabled="True" /></DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock Text="Adding to existing folders" Style="{StaticResource BodyStrongTextBlockStyle}" />
                <ItemsControl ItemsSource="{x:Bind Vm.FoldersAppended, Mode=OneWay}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" IsTextSelectionEnabled="True" /></DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock Text="Space" Style="{StaticResource BodyStrongTextBlockStyle}" />
                <ItemsControl ItemsSource="{x:Bind Vm.VolumeLines, Mode=OneWay}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" /></DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock Text="Please confirm" Style="{StaticResource BodyStrongTextBlockStyle}"
                           Visibility="{x:Bind ui:UiFormat.VisibleIfAny(Vm.Acks.Count), Mode=OneWay}" />
                <ItemsControl ItemsSource="{x:Bind Vm.Acks, Mode=OneWay}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="rv:AckVm">
                            <CheckBox IsChecked="{x:Bind IsChecked, Mode=TwoWay}">
                                <TextBlock Text="{x:Bind Text}" TextWrapping="Wrap" />
                            </CheckBox>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <ctl:IssueList x:Name="WarningList" />
                <ctl:IssueList x:Name="InfoList" />
            </StackPanel>
        </ScrollViewer>
        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right" Padding="24,12"
                    BorderThickness="0,1,0,0" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}">
            <Button Content="Back" Command="{x:Bind Vm.BackCommand}" />
            <!-- CanStart = PreflightAcks.CanStart(report, checked acks); the click is ShellVm.StartCopyAsync() -->
            <Button Content="Start offload" Style="{StaticResource AccentButtonStyle}" Click="OnStart"
                    IsEnabled="{x:Bind Vm.CanStart, Mode=OneWay}" />
        </StackPanel>
    </Grid>
</Page>
```

```csharp
// src/UasSort.App/Pages/PreflightPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class PreflightPage : Page
{
    private MainWindow _window = null!;
    public PreflightPage() => InitializeComponent();
    public PreflightVm Vm { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (PreflightVm)args.Vm;                               // ShellVm.Current (= ShellVm.Preflight) on the Preflight stage
        _window = args.Window;
        BlockingList.Show(Vm.Blocking, InfoBarSeverity.Error);
        WarningList.Show(Vm.Warnings, InfoBarSeverity.Warning);
        InfoList.Show(Vm.Infos, InfoBarSeverity.Informational);
    }

    private async void OnStart(object sender, RoutedEventArgs e) => await _window.Services.Shell.StartCopyAsync();
}
```

```xml
<!-- src/UasSort.App/Pages/CopyPage.xaml — Ref §10.3 progress at 10 Hz -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.CopyPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services">
    <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="10" Width="620">
        <TextBlock Text="Copying" Style="{StaticResource TitleTextBlockStyle}" />
        <ProgressBar Maximum="1" Value="{x:Bind Vm.Fraction, Mode=OneWay}" />
        <Grid ColumnSpacing="16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <StackPanel>
                <TextBlock Text="{x:Bind Vm.FilesText, Mode=OneWay}" />
                <TextBlock Text="{x:Bind Vm.BytesText, Mode=OneWay}" />
            </StackPanel>
            <StackPanel Grid.Column="1">
                <TextBlock Text="{x:Bind Vm.SpeedText, Mode=OneWay}" />
                <TextBlock Text="{x:Bind Vm.EtaText, Mode=OneWay}" />
            </StackPanel>
        </Grid>
        <TextBlock Text="{x:Bind Vm.CurrentText, Mode=OneWay}" TextTrimming="CharacterEllipsis" Style="{StaticResource CaptionText}" />
        <TextBlock Text="{x:Bind Vm.PhaseText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
        <InfoBar IsOpen="True" IsClosable="False" Severity="Error" Message="{x:Bind Vm.ErrorText, Mode=OneWay}"
                 Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.ErrorText), Mode=OneWay}" />
        <Button Content="Cancel" Command="{x:Bind Vm.CancelCommand}" HorizontalAlignment="Right"
                Visibility="{x:Bind ui:UiFormat.Visible(Vm.IsRunning), Mode=OneWay}" />
    </StackPanel>
</Page>
```

```csharp
// src/UasSort.App/Pages/CopyPage.xaml.cs
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class CopyPage : Page
{
    public CopyPage() => InitializeComponent();
    public CopyVm Vm { get; private set; } = null!;
    protected override void OnNavigatedTo(NavigationEventArgs e) => Vm = (CopyVm)((StageArgs)e.Parameter).Vm;
}
```

```xml
<!-- src/UasSort.App/Pages/VerdictPage.xaml — Ref §10.5 Verdict page -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.VerdictPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <ScrollViewer>
            <StackPanel MaxWidth="980" Padding="24" Spacing="10">
                <Border CornerRadius="6" Padding="16,12" Background="{x:Bind ui:UiFormat.VerdictBrush(Vm.Level), Mode=OneWay}">
                    <StackPanel Spacing="2">
                        <TextBlock x:Name="LevelLine" Text="{x:Bind Vm.LevelText, Mode=OneWay}" Foreground="White" FontWeight="SemiBold" />
                        <TextBlock x:Name="Headline" Text="{x:Bind Vm.Headline, Mode=OneWay}" Foreground="White" TextWrapping="Wrap"
                                   Style="{StaticResource SubtitleTextBlockStyle}" />
                    </StackPanel>
                </Border>
                <InfoBar IsOpen="True" IsClosable="False" Severity="Warning" Message="{x:Bind Vm.SafeRemovalNote, Mode=OneWay}"
                         Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.SafeRemovalNote), Mode=OneWay}">
                    <InfoBar.Content>
                        <ItemsControl ItemsSource="{x:Bind Vm.Ejects}" Margin="0,0,0,8">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="rv:EjectVm">
                                    <StackPanel Orientation="Horizontal" Spacing="8">
                                        <!-- EjectVm.ResultText is set by EjectCommand (not observable): OnEject shows it -->
                                        <Button Content="{x:Bind Text}" Click="OnEject" />
                                        <TextBlock Text="{x:Bind ResultText}" VerticalAlignment="Center" Style="{StaticResource CaptionText}" />
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </InfoBar.Content>
                </InfoBar>

                <ItemsControl ItemsSource="{x:Bind Vm.CategoryLines, Mode=OneWay}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" TextWrapping="Wrap" /></DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Expander Header="Changes on the card since the scan" HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch"
                          Visibility="{x:Bind ui:UiFormat.VisibleIfAny(Vm.CardChanges.Count), Mode=OneWay}">
                    <ItemsControl ItemsSource="{x:Bind Vm.CardChanges, Mode=OneWay}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" Style="{StaticResource CaptionText}" /></DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </Expander>

                <TextBlock Text="Folders" Style="{StaticResource BodyStrongTextBlockStyle}" />
                <ItemsControl ItemsSource="{x:Bind Vm.Groups}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="rv:VerdictGroupRowVm">
                            <Grid ColumnSpacing="8" Padding="0,2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="{x:Bind Text}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                                           ToolTipService.ToolTip="{x:Bind Path}" />
                                <Button Grid.Column="1" Content="Open folder" Command="{x:Bind OpenFolderCommand}" />
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Button Content="Open photo folder (for the Lightroom import)" Command="{x:Bind Vm.OpenPhotoRootCommand}" />

                <TextBlock Text="Not copied" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="0,8,0,0" />
                <TextBlock Text="Nothing is selected. Photos and sets can be selected per day; videos and unknown files one at a time."
                           Style="{StaticResource CaptionText}" TextWrapping="Wrap" />
                <!-- per-day selection for photos and sets (VerdictDecisions.Day) -->
                <ItemsControl ItemsSource="{x:Bind Vm.NotCopiedDays}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="6" /></ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="rv:NotCopiedDayVm">
                            <Button Content="{x:Bind Text}" Command="{x:Bind SelectDayCommand}" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <ItemsControl x:Name="NotCopiedList" ItemsSource="{x:Bind Vm.NotCopied}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="rv:NotCopiedRowVm">
                            <CheckBox IsChecked="{x:Bind IsSelected, Mode=OneWay}" Command="{x:Bind ToggleCommand}">
                                <StackPanel>
                                    <TextBlock>
                                        <Run Text="{x:Bind Text}" /> · <Run Text="{x:Bind SizeText}" />
                                    </TextBlock>
                                    <TextBlock Text="{x:Bind Detail}" Style="{StaticResource CaptionText}" />
                                </StackPanel>
                            </CheckBox>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock Text="{x:Bind Vm.SelectionText, Mode=OneWay}" Style="{StaticResource CaptionText}" TextWrapping="Wrap"
                           Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.SelectionText), Mode=OneWay}" />
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Content="Record selected photos as already imported" Command="{x:Bind Vm.RecordImportedCommand}" />
                    <Button Content="Mark selected as not needed" Command="{x:Bind Vm.MarkNotNeededCommand}" />
                    <!-- enabled by UndoCommand.CanExecute -->
                    <Button Content="Undo" Command="{x:Bind Vm.UndoCommand}" />
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
        <Grid Grid.Row="1" Padding="24,12" ColumnSpacing="8" BorderThickness="0,1,0,0" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Button Content="Open report" Command="{x:Bind Vm.OpenReportCommand}" />
            <Button Grid.Column="1" Content="Show plan" Command="{x:Bind Vm.ShowPlanCommand}" />
            <Button x:Name="VerdictCleanUpButton" Grid.Column="3" Content="Clean up card…" Command="{x:Bind Vm.CleanupCommand}"
                    IsEnabled="{x:Bind Vm.CanCleanup, Mode=OneWay}" ToolTipService.ToolTip="{x:Bind Vm.CleanupTooltip, Mode=OneWay}" />
            <Button Grid.Column="4" Content="Done" Style="{StaticResource AccentButtonStyle}" Command="{x:Bind Vm.DoneCommand}" />
        </Grid>
    </Grid>
</Page>
```

```csharp
// src/UasSort.App/Pages/VerdictPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class VerdictPage : Page
{
    public VerdictPage() => InitializeComponent();
    public VerdictVm Vm { get; private set; } = null!;

    // ShellVm.Current (= ShellVm.Verdict) on the Verdict stage
    protected override void OnNavigatedTo(NavigationEventArgs e) => Vm = (VerdictVm)((StageArgs)e.Parameter).Vm;

    /// <summary>Runs EjectVm.EjectCommand and shows its ResultText next to the button (Ref §10.5 eject).</summary>
    internal static void ShowEject(object sender)
    {
        if (sender is not Button { DataContext: EjectVm eject } button) return;
        eject.EjectCommand.Execute(null);
        if (button.Parent is Panel panel && panel.Children.OfType<TextBlock>().LastOrDefault() is { } result)
            result.Text = eject.ResultText ?? "";
    }

    private void OnEject(object sender, RoutedEventArgs e) => ShowEject(sender);
}
```

Add to `ShellPage.StagePages`:

```csharp
        [Stage.Preflight] = typeof(PreflightPage),
        [Stage.Copy] = typeof(CopyPage),
        [Stage.Verdict] = typeof(VerdictPage),
```

- [ ] **Step 4: Run it and watch it pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only pages.construct`
Expected: `pass`, detail `loaded SetupPage, CardPage, ScanPage, SettingsPage, PreflightPage, CopyPage, VerdictPage`, `exit code 0`; XAML lint `Failed: 0`. The Commit flow itself is covered by Part 07's fault-injection tests and Part 10's PreflightVm/VerdictVm tests; this task adds only their views.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: Commit pages (preflight sheet with acknowledgements, copy progress, verdict with decisions, eject and cleanup entry)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.16: Cleanup page and its entry points

**Files:**
- Create: `src/UasSort.App/Pages/CleanupPage.xaml`, `Pages/CleanupPage.xaml.cs`
- Modify: `src/UasSort.App/Pages/ShellPage.xaml.cs` (`[Stage.Cleanup] = typeof(CleanupPage),`), `SelfTest/SelfTestChecks.Pages.cs` (`() => new CleanupPage()` in `PageFactories`; the `cleanup.entry` check), `SelfTest/SelfTestChecks.cs` (register `cleanup.entry`)

**Interfaces:**
- Consumes (registry Review VMs table): `CleanupVm` (`Step`, `BlockingText`, `CanRescan`, `RescanCommand`, `Mode`, `PickedDate`, `PickDate(DateTimeOffset?)`, `FreeKind`, `GbValue`, `IncludeNotInLibrary`, `CardSummary`, `FreeNowText`, `WillDeleteText`, `CutoffText`, `ShortfallText`, `CanIncludeNotInLibraryFix`, `IncludeNotInLibraryCommand`, `CountsText`, `FilesText`, `RangeText`, `FreeAfterText`, `EvidenceText`, `NeverCopiedText`, `NeverTouchedText`, `Kept`, `Rows`, `FirstUndecided`, `KeepAllCommand`, `DeleteAllCommand`, `AckCantBeRecovered`, `AckNotInLibrary`, `ShowNotInLibraryAck`, `NotInLibraryAckText`, `CantBeRecoveredText`, `CanContinue`, `ContinueCommand`, `BackCommand`, `DeleteButtonText`, `CanDelete`, `DeleteCommand`, `CancelCommand`, `ProgressText`, `Result`, `DoneCommand`), `CleanupStep { Choose, Review, Confirm, Deleting, Result }`, `CleanupMode`, `FreeSpaceKind`, `RowDecision`, `KeptGroupVm` (`Reason`, `Lines`), `CleanupRowVm` (`ThumbKey`, `DateText`, `LengthText`, `LocationText`, `SizeText`, `ReasonText`, `Badge`, `Decision`, `KeepCommand`, `DeleteCommand`), `CleanupResultVm` (`HeadlineText`, `Problems`, `StillListed`, `VerdictText`, `StopText`, `SafeRemovalText`, `Eject`); the two **[Clean up card…]** entry points (Ref §10.6 table): `ShellVm.CleanupCommand`/`CleanupEnabled`/`CleanupTooltip` (title bar, Task 11.4) and `VerdictVm.CleanupCommand`/`CanCleanup`/`CleanupTooltip` (Task 11.15); `Thumb.Key`.
- Produces (defined here): `CleanupPage : Page` — `CleanupVm Vm`, `SelectorBar ModeBar`, `ItemsView ReviewList`; scrolls to `CleanupVm.FirstUndecided` whenever it changes; check `cleanup.entry`.

- [ ] **Step 1: Write the failing check**

Register `("cleanup.entry", CleanupEntry),` (after `keys.accelerators`), add `() => new CleanupPage(),` to `PageFactories`, and append to `SelfTestChecks.Pages.cs`:

```csharp
    /// <summary>Ref §10.6: the selftest card came from Browse to folder, so [Clean up card…] must be disabled with that reason.</summary>
    private static async Task<SelfTestCheck> CleanupEntry(SelfTestContext ctx)
    {
        await ReviewPageAsync(ctx);
        var shell = ctx.Services.Shell;
        const string reason = "Cleanup works only on a detected card. A browsed folder could be a backup copy.";
        var button = VisualTree.FindDescendant<Button>(ctx.Window.Shell!, b => b.Name == "CleanUpButton");
        bool ok = !shell.CleanupEnabled && shell.CleanupTooltip == reason && button is { IsEnabled: false }
                  && (ToolTipService.GetToolTip(button) as string) == reason;
        return ok ? SelfTestCheck.Pass("cleanup.entry", "title-bar [Clean up card…] disabled: " + reason)
                  : SelfTestCheck.Fail("cleanup.entry", $"enabled {shell.CleanupEnabled}, tooltip '{shell.CleanupTooltip}', button enabled {button?.IsEnabled}");
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only cleanup.entry,pages.construct`
Expected: `CS0246: The type or namespace name 'CleanupPage' could not be found` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```xml
<!-- src/UasSort.App/Pages/CleanupPage.xaml — Ref §10.6 (Choose → Not-in-library review → Confirm → Deleting → Result), a Page because the review list can be long -->
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="UasSort.App.Pages.CleanupPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctl="using:UasSort.App.Controls"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <ScrollViewer>
            <StackPanel MaxWidth="1000" Padding="24" Spacing="10">
                <TextBlock Text="Clean up card" Style="{StaticResource TitleTextBlockStyle}" />
                <TextBlock Text="{x:Bind Vm.CardSummary, Mode=OneWay}" Style="{StaticResource BodyStrongTextBlockStyle}" />
                <TextBlock Text="{x:Bind Vm.FreeNowText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                <!-- Preparation refused (different card, card gone, …): CleanupVm.BlockingText, with Rescan when CanRescan -->
                <InfoBar IsOpen="True" IsClosable="False" Severity="Error" Message="{x:Bind Vm.BlockingText, Mode=OneWay}"
                         Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.BlockingText), Mode=OneWay}">
                    <InfoBar.ActionButton>
                        <Button Content="Rescan" Command="{x:Bind Vm.RescanCommand}"
                                Visibility="{x:Bind ui:UiFormat.Visible(Vm.CanRescan), Mode=OneWay}" />
                    </InfoBar.ActionButton>
                </InfoBar>

                <!-- ── Choose (CleanupStep.Choose) ── -->
                <StackPanel x:Name="ChoosePanel" Spacing="10">
                    <SelectorBar x:Name="ModeBar" SelectionChanged="OnModeChanged">
                        <SelectorBarItem Text="Before a date" IsSelected="True" />
                        <SelectorBarItem Text="Free space" />
                    </SelectorBar>
                    <StackPanel x:Name="BeforeDatePanel" Spacing="6">
                        <TextBlock Text="Delete files captured before this day (local time at each site). That day itself is kept." TextWrapping="Wrap" />
                        <!-- CleanupVm.PickDate: the picker's own calendar day becomes the DateOnly cutoff (Ref §10.6 mode 1) -->
                        <CalendarDatePicker x:Name="CutoffPicker" Date="{x:Bind Vm.PickedDate, Mode=OneWay}" DateChanged="OnDateChanged"
                                            PlaceholderText="Pick a day" />
                    </StackPanel>
                    <StackPanel x:Name="FreeSpacePanel" Spacing="6" Visibility="Collapsed">
                        <RadioButtons x:Name="FreeKindButtons" SelectedIndex="{x:Bind ui:UiFormat.FreeKindIndex(Vm.FreeKind), Mode=OneWay}"
                                      SelectionChanged="OnFreeKindChanged">
                            <x:String>Have at least this much free</x:String>
                            <x:String>Free up this much</x:String>
                        </RadioButtons>
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <NumberBox x:Name="GigabytesBox" Value="{x:Bind Vm.GbValue, Mode=TwoWay}" Minimum="0" SmallChange="1"
                                       SpinButtonPlacementMode="Compact" Width="140" />
                            <TextBlock Text="GB" VerticalAlignment="Center" />
                        </StackPanel>
                        <TextBlock Text="{x:Bind Vm.WillDeleteText, Mode=OneWay}" TextWrapping="Wrap" />
                    </StackPanel>
                    <ToggleSwitch Header="Also delete files not in my library" IsOn="{x:Bind Vm.IncludeNotInLibrary, Mode=TwoWay}" />
                    <!-- CutoffText also carries "the flight continues…" (CleanupTexts.CutoffLine) -->
                    <TextBlock Text="{x:Bind Vm.CutoffText, Mode=OneWay}" TextWrapping="Wrap" FontWeight="SemiBold" />
                    <InfoBar IsOpen="True" IsClosable="False" Severity="Warning" Message="{x:Bind Vm.ShortfallText, Mode=OneWay}"
                             Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.ShortfallText), Mode=OneWay}">
                        <InfoBar.ActionButton>
                            <Button Content="Include files not in my library" Command="{x:Bind Vm.IncludeNotInLibraryCommand}"
                                    Visibility="{x:Bind ui:UiFormat.Visible(Vm.CanIncludeNotInLibraryFix), Mode=OneWay}" />
                        </InfoBar.ActionButton>
                    </InfoBar>
                </StackPanel>

                <!-- ── Not-in-library review (CleanupStep.Review): required whenever the switch is on (Ref §10.6) ── -->
                <StackPanel x:Name="ReviewPanel" Spacing="6" Visibility="Collapsed">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="Files not proven to be in your library" Style="{StaticResource BodyStrongTextBlockStyle}" VerticalAlignment="Center" />
                        <Button Content="Keep all" Command="{x:Bind Vm.KeepAllCommand}" />
                        <Button Content="Delete all" Command="{x:Bind Vm.DeleteAllCommand}" />
                    </StackPanel>
                    <ItemsView x:Name="ReviewList" SelectionMode="None" MaxHeight="520">
                        <ItemsView.ItemTemplate>
                            <DataTemplate x:DataType="rv:CleanupRowVm">
                                <ItemContainer>
                                    <Grid ColumnSpacing="10" Padding="4,4">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="104" />
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="Auto" />
                                        </Grid.ColumnDefinitions>
                                        <Border Width="96" Height="54" CornerRadius="3" Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                                            <Image ctl:Thumb.Key="{x:Bind ThumbKey}" Stretch="UniformToFill" />
                                        </Border>
                                        <StackPanel Grid.Column="1" Spacing="2">
                                            <TextBlock>
                                                <Run Text="{x:Bind DateText, Mode=OneWay}" FontWeight="SemiBold" /> · <Run Text="{x:Bind LengthText, Mode=OneWay}" /> · <Run Text="{x:Bind SizeText, Mode=OneWay}" />
                                            </TextBlock>
                                            <TextBlock Text="{x:Bind LocationText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                                            <TextBlock Text="{x:Bind ReasonText, Mode=OneWay}" Style="{StaticResource CaptionText}" TextWrapping="Wrap" />
                                            <Border Style="{StaticResource ChipBorder}" HorizontalAlignment="Left"
                                                    Visibility="{x:Bind ui:UiFormat.VisibleIfText(Badge), Mode=OneWay}">
                                                <TextBlock Text="{x:Bind Badge, Mode=OneWay}" FontSize="11" />
                                            </Border>
                                        </StackPanel>
                                        <!-- RowDecision Undecided / Keep / Delete: both buttons unchecked while undecided -->
                                        <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center">
                                            <ToggleButton Content="Keep" IsChecked="{x:Bind ui:UiFormat.IsKeep(Decision), Mode=OneWay}" Command="{x:Bind KeepCommand}" />
                                            <ToggleButton Content="Delete" IsChecked="{x:Bind ui:UiFormat.IsDelete(Decision), Mode=OneWay}" Command="{x:Bind DeleteCommand}" />
                                        </StackPanel>
                                    </Grid>
                                </ItemContainer>
                            </DataTemplate>
                        </ItemsView.ItemTemplate>
                    </ItemsView>
                </StackPanel>

                <!-- ── Confirm (CleanupStep.Confirm) ── -->
                <StackPanel x:Name="ConfirmPanel" Spacing="6" Visibility="Collapsed">
                    <TextBlock Text="{x:Bind Vm.CutoffText, Mode=OneWay}" TextWrapping="Wrap" FontWeight="SemiBold" />
                    <TextBlock Text="{x:Bind Vm.CountsText, Mode=OneWay}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.FilesText, Mode=OneWay}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.RangeText, Mode=OneWay}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.FreeAfterText, Mode=OneWay}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.EvidenceText, Mode=OneWay}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.NeverCopiedText, Mode=OneWay}" TextWrapping="Wrap"
                               Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.NeverCopiedText), Mode=OneWay}" />
                    <Expander Header="Kept (older than the cutoff but not deletable)" HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch">
                        <StackPanel Spacing="4">
                            <ItemsControl ItemsSource="{x:Bind Vm.Kept}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate x:DataType="rv:KeptGroupVm">
                                        <StackPanel Spacing="2" Margin="0,0,0,4">
                                            <TextBlock Text="{x:Bind Reason}" FontWeight="SemiBold" TextWrapping="Wrap" />
                                            <ItemsControl ItemsSource="{x:Bind Lines}">
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate x:DataType="x:String">
                                                        <TextBlock Text="{x:Bind}" Style="{StaticResource CaptionText}" TextWrapping="Wrap" />
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                        </StackPanel>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <TextBlock Text="{x:Bind Vm.NeverTouchedText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                        </StackPanel>
                    </Expander>
                    <CheckBox IsChecked="{x:Bind Vm.AckCantBeRecovered, Mode=TwoWay}" Content="{x:Bind Vm.CantBeRecoveredText}" />
                    <CheckBox IsChecked="{x:Bind Vm.AckNotInLibrary, Mode=TwoWay}" Visibility="{x:Bind ui:UiFormat.Visible(Vm.ShowNotInLibraryAck), Mode=OneWay}">
                        <TextBlock Text="{x:Bind Vm.NotInLibraryAckText, Mode=OneWay}" TextWrapping="Wrap" />
                    </CheckBox>
                </StackPanel>

                <!-- ── Deleting (CleanupStep.Deleting) ── -->
                <StackPanel x:Name="DeletingPanel" Spacing="10" Visibility="Collapsed">
                    <TextBlock Text="Cleaning up the card" Style="{StaticResource SubtitleTextBlockStyle}" />
                    <ProgressBar IsIndeterminate="True" />
                    <TextBlock Text="{x:Bind Vm.ProgressText, Mode=OneWay}" TextTrimming="CharacterEllipsis" />
                </StackPanel>

                <!-- ── Result (CleanupStep.Result) ── -->
                <StackPanel x:Name="ResultPanel" Spacing="10" Visibility="Collapsed">
                    <TextBlock Text="{x:Bind Vm.Result.HeadlineText, Mode=OneWay, FallbackValue=''}" Style="{StaticResource SubtitleTextBlockStyle}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.Result.VerdictText, Mode=OneWay, FallbackValue=''}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.Result.StopText, Mode=OneWay, FallbackValue=''}" TextWrapping="Wrap" FontWeight="SemiBold" />
                    <ItemsControl ItemsSource="{x:Bind Vm.Result.Problems, Mode=OneWay}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" TextWrapping="Wrap" /></DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <ItemsControl ItemsSource="{x:Bind Vm.Result.StillListed, Mode=OneWay}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" TextWrapping="Wrap" Style="{StaticResource CaptionText}" /></DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <InfoBar IsOpen="True" IsClosable="False" Severity="Informational" Message="{x:Bind Vm.Result.SafeRemovalText, Mode=OneWay, FallbackValue=''}">
                        <InfoBar.Content>
                            <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,0,0,8">
                                <Button Content="{x:Bind Vm.Result.Eject.Text, Mode=OneWay, FallbackValue=''}" Click="OnEject" />
                                <TextBlock x:Name="EjectResult" VerticalAlignment="Center" Style="{StaticResource CaptionText}" />
                            </StackPanel>
                        </InfoBar.Content>
                    </InfoBar>
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right" Padding="24,12"
                    BorderThickness="0,1,0,0" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}">
            <Button x:Name="BackButton" Content="Back" Command="{x:Bind Vm.BackCommand}" />
            <Button x:Name="ContinueButton" Content="Continue" Style="{StaticResource AccentButtonStyle}" Command="{x:Bind Vm.ContinueCommand}"
                    IsEnabled="{x:Bind Vm.CanContinue, Mode=OneWay}" />
            <Button x:Name="DeleteButton" Content="{x:Bind Vm.DeleteButtonText, Mode=OneWay}" Style="{StaticResource AccentButtonStyle}"
                    Command="{x:Bind Vm.DeleteCommand}" IsEnabled="{x:Bind Vm.CanDelete, Mode=OneWay}" Visibility="Collapsed" />
            <Button x:Name="StopButton" Content="Stop after the current file" Command="{x:Bind Vm.CancelCommand}" Visibility="Collapsed" />
            <Button x:Name="DoneButton" Content="Done" Style="{StaticResource AccentButtonStyle}" Command="{x:Bind Vm.DoneCommand}" Visibility="Collapsed" />
        </StackPanel>
    </Grid>
</Page>
```

```csharp
// src/UasSort.App/Pages/CleanupPage.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Globalization.NumberFormatting;

namespace UasSort.App.Pages;

public sealed partial class CleanupPage : Page
{
    public CleanupPage()
    {
        InitializeComponent();
        // Decimal GB with one decimal (Ref §10.6 mode 2).
        GigabytesBox.NumberFormatter = new DecimalFormatter
        {
            FractionDigits = 1,
            IntegerDigits = 1,
            NumberRounder = new IncrementNumberRounder { Increment = 0.1, RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp },
        };
    }

    public CleanupVm Vm { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (Vm is not null) Vm.PropertyChanged -= OnVmChanged;
        Vm = (CleanupVm)((StageArgs)e.Parameter).Vm;             // ShellVm.Current (= ShellVm.Cleanup) on the Cleanup stage
        Vm.PropertyChanged += OnVmChanged;
        ReviewList.ItemsSource = Vm.Rows;
        Bindings.Update();
        ShowMode(Vm.Mode);
        ShowStep(Vm.Step);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => Vm.PropertyChanged -= OnVmChanged;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CleanupVm.Step): ShowStep(Vm.Step); break;
            case nameof(CleanupVm.Mode): ShowMode(Vm.Mode); break;
            case nameof(CleanupVm.FirstUndecided): ScrollToFirstUndecided(); break;
        }
    }

    private void OnModeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index < 0 || Vm is null) return;
        var mode = index == 1 ? CleanupMode.FreeSpace : CleanupMode.BeforeDate;
        if (Vm.Mode != mode) Vm.Mode = mode;
        ShowMode(mode);
    }

    private void ShowMode(CleanupMode mode)
    {
        int index = UiFormat.ModeIndex(mode);
        if (ModeBar.SelectedItem != ModeBar.Items[index]) ModeBar.SelectedItem = ModeBar.Items[index];
        BeforeDatePanel.Visibility = mode == CleanupMode.BeforeDate ? Visibility.Visible : Visibility.Collapsed;
        FreeSpacePanel.Visibility = mode == CleanupMode.FreeSpace ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => Vm?.PickDate(args.NewDate);

    private void OnFreeKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && FreeKindButtons.SelectedIndex >= 0)
            Vm.FreeKind = FreeKindButtons.SelectedIndex == 1 ? FreeSpaceKind.FreeUp : FreeSpaceKind.HaveFree;
    }

    /// <summary>Ref §10.6: Choose → Review (not in library) → Confirm → Deleting → Result; the footer buttons follow the step.</summary>
    private void ShowStep(CleanupStep step)
    {
        static Visibility On(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        ChoosePanel.Visibility = On(step == CleanupStep.Choose);
        ReviewPanel.Visibility = On(step == CleanupStep.Review);
        ConfirmPanel.Visibility = On(step == CleanupStep.Confirm);
        DeletingPanel.Visibility = On(step == CleanupStep.Deleting);
        ResultPanel.Visibility = On(step == CleanupStep.Result);
        BackButton.Visibility = On(step is CleanupStep.Choose or CleanupStep.Review or CleanupStep.Confirm);
        ContinueButton.Visibility = On(step is CleanupStep.Choose or CleanupStep.Review);
        DeleteButton.Visibility = On(step == CleanupStep.Confirm);
        StopButton.Visibility = On(step == CleanupStep.Deleting);
        DoneButton.Visibility = On(step == CleanupStep.Result);
        if (step == CleanupStep.Result) EjectResult.Text = "";
        Bindings.Update();                                       // Result is created when the run ends
        if (step == CleanupStep.Review) ScrollToFirstUndecided();
    }

    // Ref §10.6: rows that join later arrive undecided and the list scrolls to the first undecided one.
    private void ScrollToFirstUndecided()
    {
        if (Vm.FirstUndecided is not { } row) return;
        int i = Vm.Rows.IndexOf(row);
        if (i >= 0) ReviewList.StartBringItemIntoView(i, new BringIntoViewOptions { VerticalAlignmentRatio = 0.1 });
    }

    private void OnEject(object sender, RoutedEventArgs e)
    {
        if (Vm.Result?.Eject is not { } eject) return;
        eject.EjectCommand.Execute(null);
        EjectResult.Text = eject.ResultText ?? "";
    }
}
```

Add `[Stage.Cleanup] = typeof(CleanupPage),` to `ShellPage.StagePages`.

- [ ] **Step 4: Run it and watch it pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only cleanup.entry,pages.construct`
Expected: `cleanup.entry` pass (`title-bar [Clean up card…] disabled: Cleanup works only on a detected card. A browsed folder could be a backup copy.`), `pages.construct` pass with `…, VerdictPage, CleanupPage`, `exit code 0`; XAML lint `Failed: 0`. The Cleanup logic (picker → `DateOnly`, undecided rows, fingerprint-bound acknowledgements, the "Delete N files (X GB)" label) is proven by Part 08/10's tests; this page only binds to it.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: Cleanup page (modes, review list with Keep/Delete, acknowledgements, progress, result) and entry points

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 11.17: Device-arrival refresh (`WM_DEVICECHANGE`, disabled during Commit and Cleanup)

**Files:**
- Create: `src/UasSort.App/Services/DeviceChangeWatcher.cs`
- Modify: `src/UasSort.App/MainWindow.xaml.cs` (create the watcher in `Start()`), `SelfTest/SelfTestChecks.cs` (register `device.hook`), `SelfTest/SelfTestChecks.Pages.cs`

**Interfaces:**
- Consumes: `WindowMessageHook`, `WindowInterop` (Task 11.3); `ShellVm.Stage` and `ShellVm.DeviceChanged()` (registry: refresh is suppressed while `Stage is Stage.Preflight or Stage.Copy or Stage.Cleanup`, Ref §9.1; `DeviceChanged` refreshes the Card stage, otherwise re-evaluates the cleanup availability).
- Produces (defined here): `sealed class DeviceChangeWatcher : IDisposable` — `static readonly TimeSpan Debounce` (750 ms), `DeviceChangeWatcher(nint hwnd, DispatcherQueue ui, ShellVm shell)`, `static bool IsSuppressed(Stage stage)`, `int Notifications`, `int Refreshes`, `int Suppressed`; `MainWindow.DeviceWatcher`; check `device.hook`.

- [ ] **Step 1: Write the failing check**

Register `("device.hook", DeviceHook),` and append to `SelfTestChecks.Pages.cs`:

```csharp
    private static async Task<SelfTestCheck> DeviceHook(SelfTestContext ctx)
    {
        var watcher = ctx.Window.DeviceWatcher;
        if (watcher is null) return SelfTestCheck.Fail("device.hook", "no DeviceChangeWatcher on the main window");
        int n0 = watcher.Notifications, r0 = watcher.Refreshes, s0 = watcher.Suppressed;
        bool enabled = !DeviceChangeWatcher.IsSuppressed(ctx.Services.Shell.Stage);
        // Two arrivals in quick succession → one debounced refresh (or one suppression while Commit/Cleanup runs).
        WindowInterop.Send(ctx.Window.Hwnd, WindowMessageHook.WM_DEVICECHANGE, WindowMessageHook.DBT_DEVICEARRIVAL, 0);
        WindowInterop.Send(ctx.Window.Hwnd, WindowMessageHook.WM_DEVICECHANGE, WindowMessageHook.DBT_DEVICEARRIVAL, 0);
        await Task.Delay(DeviceChangeWatcher.Debounce + TimeSpan.FromMilliseconds(500));
        bool ok = watcher.Notifications == n0 + 2
                  && (enabled ? watcher.Refreshes == r0 + 1 && watcher.Suppressed == s0 : watcher.Refreshes == r0 && watcher.Suppressed == s0 + 1);
        return ok ? SelfTestCheck.Pass("device.hook", $"2 notifications → {(enabled ? "1 refresh" : "suppressed")}")
                  : SelfTestCheck.Fail("device.hook", $"notifications {watcher.Notifications - n0}, refreshes {watcher.Refreshes - r0}, suppressed {watcher.Suppressed - s0}");
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only device.hook`
Expected: `CS1061: 'MainWindow' does not contain a definition for 'DeviceWatcher'` → `BUILD FAILED`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.App/Services/DeviceChangeWatcher.cs — Ref §4.2 App, §14 step 11 (UNVERIFIED): refresh on card arrival/removal
using Microsoft.UI.Dispatching;

namespace UasSort.App.Services;

public sealed class DeviceChangeWatcher : IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);   // a card arrival sends several broadcasts

    private readonly ShellVm _shell;
    private readonly DispatcherQueueTimer _timer;
    private readonly IDisposable _hook;

    public DeviceChangeWatcher(nint hwnd, DispatcherQueue ui, ShellVm shell)
    {
        _shell = shell;
        _timer = ui.CreateTimer();
        _timer.Interval = Debounce;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Fire();
        _hook = WindowMessageHook.Attach(hwnd, WindowMessageHook.WM_DEVICECHANGE, (wParam, _) =>
        {
            if (wParam != WindowMessageHook.DBT_DEVICEARRIVAL && wParam != WindowMessageHook.DBT_DEVICEREMOVECOMPLETE) return;
            Notifications++;
            _timer.Stop();                                      // the subclass proc runs on the UI thread: restart the debounce
            _timer.Start();
        });
    }

    public int Notifications { get; private set; }
    public int Refreshes { get; private set; }
    public int Suppressed { get; private set; }

    /// <summary>Commit (Preflight, Copy) and Cleanup never see a device refresh (Ref §9.1).</summary>
    public static bool IsSuppressed(Stage stage) => stage is Stage.Preflight or Stage.Copy or Stage.Cleanup;

    private void Fire()
    {
        if (IsSuppressed(_shell.Stage)) { Suppressed++; return; }
        Refreshes++;
        _shell.DeviceChanged();                                 // Card stage: re-detect; elsewhere: cleanup availability
    }

    public void Dispose()
    {
        _timer.Stop();
        _hook.Dispose();
    }
}
```

In `MainWindow.xaml.cs` add the property and create it at the start of `Start()`:

```csharp
    public DeviceChangeWatcher? DeviceWatcher { get; private set; }
```

```csharp
        var watcher = new DeviceChangeWatcher(Hwnd, DispatcherQueue, Services.Shell);
        DeviceWatcher = watcher;
        Closed += (_, _) => watcher.Dispose();
```

- [ ] **Step 4: Run it and watch it pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only device.hook`
Expected: `pass`, `2 notifications → 1 refresh` (the check runs on the Card stage, alone and in the full run, where refresh is enabled; under `--selftest` the Card stage's `NoVolumes` provider never enumerates real volumes), `exit code 0`. Real arrivals stay UNVERIFIED until a card is inserted by hand (Ref §15).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App
git commit -m "feat: device-arrival refresh via WM_DEVICECHANGE subclassing, debounced, disabled during Commit/Cleanup

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 11.18: The full `--selftest` — final check order, timeouts, two consecutive runs

**Files:**
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.cs` (final ordered registry + `debug.hang`), `src/UasSort.App/SelfTest/SelfTestRunner.cs` (default run skips `debug.*`)

**Interfaces:**
- Consumes: every check of Tasks 11.4–11.17; Part 13's contract (`--selftest --result <path>`, `{ok, firstFrameMs, checks[{name,status,detail}]}`, `notApplicable` only for `placeholderVisibility`).
- Produces (defined here): the final `SelfTestChecks.All` order; `debug.hang` (only via `--only`; proves the per-check timeout turns a hang into `fail` and exit 1).

- [ ] **Step 1: Write the failing check**

Replace the `All` initialiser in `SelfTestChecks.cs` with the final order (Card-stage checks first, standalone map checks next, then the scan and every Review check), and add the hang probe:

```csharp
    public static readonly List<(string Name, Func<SelfTestContext, Task<SelfTestCheck>> Run)> All =
    [
        ("shell.render", ShellRender),
        ("window.minSize", WindowMinSize),
        ("probe.timeZone", ProbeTimeZone),
        ("probe.still", ProbeStill),
        ("json.planEdit", JsonPlanEdit),
        ("json.ledger", JsonLedger),
        ("placeholderVisibility", PlaceholderVisibility),
        ("page.card", PageCard),
        ("page.settings", PageSettings),
        ("pages.construct", PagesConstruct),
        ("device.hook", DeviceHook),
        ("map.mime", MapMime),
        ("map.ready", MapReady),
        ("map.draw", MapDraw),
        ("review.scan", ReviewScan),
        ("review.layout", ReviewLayout),
        ("review.clock", ReviewClock),
        ("review.tuning", ReviewTuning),
        ("review.map", ReviewMap),
        ("template.groupCard", TemplateGroupCard),
        ("template.suggestion", TemplateSuggestion),
        ("template.targetMenu", TemplateTargetMenu),
        ("template.clipRow", TemplateClipRow),
        ("thumb.keyRecheck", ThumbKeyRecheck),
        ("template.photoTile", TemplatePhotoTile),
        ("template.otherTab", TemplateOtherTab),
        ("keys.spaceInRenameBox", KeysSpaceInRenameBox),
        ("keys.accelerators", KeysAccelerators),
        ("cleanup.entry", CleanupEntry),
        ("debug.hang", DebugHang),                                       // never in the default run
    ];

    private static async Task<SelfTestCheck> DebugHang(SelfTestContext ctx)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return SelfTestCheck.Pass("debug.hang", "unreachable");
    }
```

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1`
Expected (red): the default run still includes `debug.hang` (the runner doesn't filter yet), so it ends with `{"name":"debug.hang","status":"fail","detail":"timed out after 20 s"}`, `"ok":false`, `exit code 1`.

- [ ] **Step 2: Implement the default-run filter**

In `SelfTestRunner.RunAsync`, replace the `wanted` line with:

```csharp
        var wanted = SelfTestChecks.All
            .Where(c => ctx.Options.Only is null ? !c.Name.StartsWith("debug.", StringComparison.Ordinal) : ctx.Options.Only.Contains(c.Name))
            .ToList();
```

- [ ] **Step 3: Run the full selftest twice (cold, then warm) and the hang probe**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1` twice in a row.
Expected each time: `"ok":true`, 29 checks, each `pass` except `placeholderVisibility`, which is `pass` or `notApplicable`; `exit code 0`; total runtime well under 60 s. The second run's `firstFrameMs` is the warm figure (Debug/JIT here; Part 13 gates the Native AOT publish at ≤ 1000 ms).

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Only debug.hang`
Expected: `{"ok":false,…,"checks":[{"name":"debug.hang","status":"fail","detail":"timed out after 20 s"}]}`, `exit code 1`, after about 20 s.

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\selftest.ps1 -Configuration Release`
Expected: as the Debug run (Release is where trimming-sensitive code paths — source-generated JSON, x:Bind, CsWinRT — first run optimised; the AOT publish itself is Part 13).

Also confirm the sandbox is gone: `Get-ChildItem $env:TEMP -Directory -Filter 'uas-sort-selftest-*'` → no directories left by these runs (WebView2 may hold its folder for a moment; re-check after 5 s).

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test --solution uas-sort.slnx`
Expected: every test passes, including this part's `XamlLintTests` (14), `LruCacheTests` (5), `WindowMessageHookTests` (1), `PlatformServicesTests` (1) and the two tests added to Part 01's `SelfTestSandboxTests` (8 cases in that class).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App/SelfTest
git commit -m "test: full --selftest (29 checks, per-check timeout, 55 s watchdog, result JSON for the deploy gate)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

## Part 11 — Produces (summary)

**Command line and scripts**
- `uas-sort.exe` (Part 01's `Program.Main`, `DISABLE_XAML_GENERATED_MAIN`, unchanged): `PlaceholderMode.ExposePlaceholders()`, `LaunchOptions.Parse`, single instance through Part 01's `SingleInstanceGate` (`AppInstance.FindOrRegisterForKey("uas-sort")`, redirect on a worker thread, `ForegroundWindow.AllowSetForeground`, fallback `NamedMutexLock` on `Local\uas-sort`) — Part 11 adds `SingleInstanceGate.Activated`; `uas-sort.exe --selftest --result <path> [--only a,b]` parsed by Part 01's `LaunchOptions` (Part 13 contract: `{ok, firstFrameMs, checks[{name,status,detail}]}` from Part 01's `SelfTestCheck`/`SelfTestResult`/`SelfTestJsonContext`, exit 0/1, `notApplicable` only for `placeholderVisibility`).
- `tools/selftest.ps1 [-Only] [-Configuration] [-TimeoutSec 60]`; `tools/vendor-maplibre.ps1 [-RenameToJs] [-Leaflet]` (MapLibre 6.11.2 / Leaflet 1.9.4 with sha512 integrity, Noto Sans glyphs, `lib/manifest.json`); Part 01's `tools/fixtures/make-selftest-assets.cs` extended in place (`SelfTestAssets.All()` = `stack-exif.jpg` + `selftest.dng`, `ledger-v1.jsonl`, `selftest-000{1,2,3}.mp4`).

**Platform (Task 11.3)** — `SelfTestSandbox` extension (Part 01 type: `Create()`, `AppDataDir`, `VideoRoot`, `PhotoRoot`, `CardRoot`, `WriteFile`), `WindowInterop` (no foreground members: `Send`, `CreateMessageOnlyWindow`, `DestroyWindow`), `WindowMessageHook` (`Attach(hwnd, message, handler)`, `WM_DEVICECHANGE`, `DBT_*`), `PlatformServices` (`UasSort.Platform`, `Create(appDataDir, clock)`, writes nothing). Conditional (Task 11.7 fallback 2 only): `IAppAssets.OpenMapAsset(string)` in Part 02's port and Part 09's `AppAssets`.

**Review (Task 11.2)** — `LruCache<TKey,TValue>` (`UasSort.Review`).

**Testing (Task 11.9)** — `UasSort.Testing.LedgerSamples.AllRecordKindsV1()` (one `LedgerCodec.Serialize` line per record kind).

**App (namespace `UasSort.App` and children; `GlobalUsings.cs` = the registry list)**
- Shell: `App` (`MainWindow`, `WebView2Folder`), `MainWindow` (Part 01's `FirstFrameMs`, `ShowOffScreen`, `BringToFront` kept; `Services`, `Hwnd`, `Shell`, `DeviceWatcher`, `Start`, `ApplyMinimumSize`, `MinWidth`/`MinHeight`), `ShellPage` (TitleBar with card chip, Rescan/Undo/Redo/**Clean up card…**/Settings; `StageFrame` following `ShellVm.Current`; `StagePages` keyed by `Stage` for all nine stages), `StageArgs`, `CompositionRoot` (every `ShellDeps` factory, `CommitEnvironment`, `CleanupEnvironment`, `NoVolumes` under selftest) + `AppServices` (+ `SelfTestSettings`); `places.bin.gz` as App Content.
- Services: `WinUiDispatcher : IUiDispatcher`, `DialogService : IDialogService` (queued), `CardThumbnails : IThumbnailSource` (`Use(reader, items)`), `UiFormat`, `VisualTree`, `DeferredPlaceIndex : IPlaceIndex` (over `PlaceIndex.LoadAsync`), `VolumeFreeSpace : IFreeSpace`, `FileReviewLog : IReviewLog`, `NoVolumes`, `FolderPickerService`, `KeyRouting` (to `ReviewKey`/`KeyMods`/`KeyFocus`), `DeviceChangeWatcher`.
- Pages: `SetupPage`, `CardPage`, `ScanPage`, `ReviewPage` (accelerator table through `ReviewVm.HandleKey`, `TryHandleAccelerator`, `ShowTab`, the map wiring of one `MapBridge` per `ReviewVm`, layout through `ShellVm.UpdateLayout`), `PreflightPage` (Start → `ShellVm.StartCopyAsync()`), `CopyPage`, `VerdictPage`, `CleanupPage` (five `CleanupStep`s), `SettingsPage`.
- Controls: `Thumb.Key` + `ThumbnailCache` (LRU 400 at 96 px, key re-check), `InfoBarList`, `TuningStrip` (handledEventsToo pointer commit, `BeginDrag`/`EndDragAsync`), `FooterBar` (`FooterText`, issue flyout of `IssueVm` → `ReviewVm.GoTo`, Offload), `TimelineView` + `TimelineTemplateSelector` (chip-header cards from `GroupCardVm.Chip`, folded runs via `VideosTabVm.ToggleFold`, AutoSuggestBox with `UpdateTextOnSelect=False`, read-only on Append, `RetargetOptions()` menu built on `Opening`, Browse existing), `ClipListView` + `ClipMenu` (in-row day banners, Extended selection, map sync, context menu with Move to group ▸ and Copy card path), `PhotosTab` (tri-state days, `LinedFlowLayout` wall), `OtherTab`, `IssueList`.
- Map: `MapHost.MapPane` (fenced WebView2: user data folder, `map.uas-sort.example` with DenyCors, NavigationStarting/NewWindowRequested/DownloadStarting, WebMessageReceived before Navigate, 5 s ready timeout → "Map unavailable" + Open in browser, `Attach(MapBridge)`/`Detach`, `MapSetTheme` on theme change, `OnlineChanged`, `ContextMenuRequested`, `FocusReturnRequested`, `IsOnline`, `OsmUri`, `ThemeName`, `EvaluateAsync`); `MapAssets/index.html` (error forwarding first) and `map.js` (protocol v1: `init`, `setData`, `select`, `setRadius`, `setBase`, `setTheme`, `fit`, `ping` → `ready`, `pong`, `click`, `clickEmpty`, `contextMenu`, `tileError`, `baseUnavailable`, `error`; OpenFreeMap/Esri/USGS/none; offline canvas; `window.__uas.stats()`), vendored `lib/` and `fonts/`; Part 01's `probe.html` removed.
- Selftest (`internal`): `SelfTestContext`, `SelfTestRunner` (20 s per check, 55 s watchdog) over Part 01's `SelfTestCheck`/`SelfTestResult`/`SelfTestJsonContext` (no `SelfTestOptions`, no `SelfTestJson`), `SelfTestFixture`, 29 default checks: `shell.render`, `window.minSize`, `probe.timeZone`, `probe.still`, `json.planEdit`, `json.ledger`, `placeholderVisibility`, `page.card`, `page.settings`, `pages.construct`, `device.hook`, `map.mime`, `map.ready`, `map.draw`, `review.scan`, `review.layout`, `review.clock`, `review.tuning`, `review.map`, `template.groupCard`, `template.suggestion`, `template.targetMenu`, `template.clipRow`, `thumb.keyRecheck`, `template.photoTile`, `template.otherTab`, `keys.spaceInRenameBox`, `keys.accelerators`, `cleanup.entry` (+ `debug.hang` via `--only`). Part 01's `MinimalSelfTest`, `StackProbePage`, the probe VMs, `ProbeTemplateSelector` and `MainWindow.ProbePage` are deleted.

**Tests** — `tests/UasSort.Platform.Tests/XamlLintTests.cs` (`XamlLint.Check`), `Win32/WindowMessageHookTests.cs`, `PlatformServicesTests.cs`, two tests added to Part 01's `Stores/SelfTestSandboxTests.cs`; `tests/UasSort.Review.Tests/LruCacheTests.cs`.

**Consumed contract** — `00-interfaces.md` (the registry's Review VMs, Platform and App tables; the C11 → Part 10 mapping at the top of this part is applied everywhere).
