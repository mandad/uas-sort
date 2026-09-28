# uas-sort: design for grouping videos into folders and deciding what is new

## 0. Summary

- **Grouping rule.** Sort videos by capture time in UTC. A clip starts a new group in either of two cases:
  - **Time:** its site-local date is more than **G = 1 day** after the previous clip, and more than **H = 3 h** has passed.
  - **Location:** it has GPS and is more than **R = 25 km** from the current group's centroid.
- **What gets grouped.** Only videos decide folder boundaries. Photos attach to the nearest video group. Photos left over form "photo-only" groups, which exist only for display, because photos always go flat into the photo root.
- **Tested on the real library.** I replayed the whole video library as if it were one card, using names, sizes and mtimes, plus GPS already extracted. It reproduces all 8 of the user's folders exactly.
  - Any R from **12.5 to 53 km** works, and any G from **1 to 11 days** works.
  - R = 25 km sits in the middle: about twice the largest spread inside one folder (Nome Roads, 12.5 km) and about half the smallest split on consecutive days (Council Road to Anvil Mountain, 53.2 km).
- **Reference code and tests.** I wrote a Python reference implementation and **37 unit tests, all passing**. Mutation checks confirm that each rule is exercised: changing R, H, the watermark, the timezone lookup or the session rule makes specific tests fail.
  - The tests caught one real design bug. The "same power-on session" link must only look at members that have GPS. Otherwise a clip with no GPS fix can glue two sites 85 km apart into one group.
- **New measurements.** I read the first and last GPS fix of 36 more library clips: Newport 23, Safety Roadhouse 3, Nome Roads 7, Council Road 3. All of them are either plain local files or already downloaded (attributes 0x420). I skipped every Kodiak file, since all 30 are cloud-only (0x401620).
- **Listing the library does not download it.** After I ran `stat` (sizes and mtimes) across the whole library, the Kodiak files still showed cloud-only attributes (0x401620).

Spike files, all under `/tmp/claude-1000/-mnt-c-dev-uas-sort/187b6b83-e3e1-4971-8c9e-c13c3990d115/scratchpad/spikes/grouping/`:
- `grouping.py`: pure reference implementation, about 560 lines.
- `test_grouping.py`: 37 tests. Run `python3 -m unittest -v test_grouping`.
- `replay.py`: replays the real library and sweeps R and G.
- `readmore.py` and `more_gps.json`: the extra GPS reads.

I wrote nothing to user data or to `/mnt/c/dev/uas-sort`, and did not use a Windows temp directory.

### New calibration numbers (first and last fix of every readable clip)

| Folder | Clips | Largest distance between any two points | Largest distance from the running centroid |
|---|---|---|---|
| Newport, RI (morning and evening sessions, 9 h apart) | 23 | 5.40 km | 4.84 km |
| Safety Roadhouse | 3 | 0.35 | 0.20 |
| **Nome Roads** (clip 0101 to 0102: 12.0 km in 26 min, a drive) | 8 | **12.52** | **12.05** |
| Council Road (clips 3 h apart) | 4 | 1.54 | 0.88 |
| Anvil Mountain | 21 | 1.25 | 1.01 |
| Zachar Bay | 13 | 1.83 | 1.07 |

Closest points between folders:
- Council Road to Anvil Mountain: **53.2 km** (consecutive days, split by the user).
- Nome Roads to Anvil Mountain: 14.1 km (22 days apart).
- Safety Roadhouse to Council Road: 33.3 km.
- Zachar Bay to the Kodiak PANO: 84.7 km.

Time gaps: the shortest gap between folders, apart from Council Road to Anvil Mountain, is Newport to Kodiak at 12 days. That is why G = 12 wrongly merges folders and G ≤ 11 does not.

---

## 1. The normalized item

The metadata readers produce a `RawItem`. The pure core turns it into an `Item`.

```
RawItem { Id (card-relative path), Kind: Video|Photo|Set, Name, Size, Maker: Dji|Autel|Other,
          MtimeUtc, MvhdUtc?, ExifDto? (naive), ExifOffset?, GpsUtc?, Lat?, Lon?,
          SessionUtc? (= creation − djmd uptime field), Members[(name,size)] (Set only) }
Item    { Raw, CaptureUtc, TimeSource, Floating? (wall clock, no zone), Tz (IANA), TzSource,
          LocalDate (DateOnly), Status, Reason, LibFolder? }
```

**Units that are treated as one item:**
- A panorama or hyperlapse set is one `Set` item: the set folder, with the first frame's time and GPS.
- A JPG+DNG pair with the same base name is one item.
- `.trinf` and other dot-files are skipped.

### CaptureUtc: which source wins (first match)

| # | Condition | CaptureUtc | TimeSource |
|---|---|---|---|
| 1 | DJI video with `moov` | `mvhd` creation_time (true UTC) | `mvhd` |
| 2 | A real GPS time exists. Not on the Air 3S: its djmd has no GPS time and ExifTool's "GPSDateTime" is synthesized. Some other DJI models and EXIF GPSDateStamp do have one. | GPS time | `gps` |
| 3 | EXIF DateTimeOriginal plus OffsetTimeOriginal (not written by DJI today) | DTO − offset | `exif+OffsetTime` |
| 4 | DJI photo or set, or a truncated DJI video, with a drone-clock stamp (EXIF DTO, else the filename) and a known clock offset | stamp − offset | `droneclock+learned` or `droneclock+setting` |
| 5 | Autel video | `Floating` = creation_time taken as a wall clock (it is labelled Z but is really local); CaptureUtc is estimated from it | `autel-mvhd-floating` |
| 6 | Autel photo, or any unknown maker | `Floating` = mtime converted to the PC zone. FAT stamps are naive, and Windows applied the PC zone when reading them. | `mtime-floating` |
| 7 | Anything else | mtime | `mtime` (true UTC on exFAT is UNVERIFIED) |

**Why Autel is "floating":**
- Autel MAX_0065 was verified by the earlier spike.
- The Makaha library mtimes display as 07:04–07:08 AKDT, which is the Autel's wall clock. So a FAT naive stamp was converted with the PC's zone at copy time.
- Therefore `LocalDate = Floating.Date`, and it never goes through a timezone conversion.

**Learning the drone-clock offset:**
- Each DJI MP4 with a `moov` gives one sample: `(drone-clock stamp from filename, round15min(stamp − mvhd))`.
- For each photo or truncated clip, use the sample nearest in drone-clock time, if it is within 60 days. Otherwise use the most common offset on the card. Otherwise use the `DroneClockOffset` setting, saved from the last learned value (currently −4 h). Otherwise fall through to mtime.
- Using the nearest sample rather than a single card-wide value matters because it is **UNVERIFIED whether the drone clock follows Eastern DST** (UTC−5 in winter) or is fixed at UTC−4. The test `offset_nearest_sample_handles_clock_change` covers this.
- **Sanity check:** a CaptureUtc before 2015 or later than the PC clock plus 1 day is flagged "clock not set" and shown with its source.

### Site timezone and local date

- **Items with GPS:** use GeoTimeZone `TimeZoneLookup.GetTimeZone(lat, lon).Result`, an IANA id, then `TimeZoneInfo.FindSystemTimeZoneById`. In the spike this is a stub. The real package is being checked by the `dotnet-stack` spike (GeoTimeZone 6.1.0 in `meta.csproj`), so it is **UNVERIFIED here**.
- **Items without GPS:** take the zone of a GPS item in the same power-on session. Otherwise take the nearest GPS item within 12 h. Otherwise use the PC zone. `TzSource` records which one was used (`gps`, `inherited` or `pc`).
- **LocalDate:** `Floating?.Date ?? TimeZoneInfo.ConvertTimeFromUtc(CaptureUtc, tz).Date`.
- **Worked example:** filename `20260726035000` means 07:50Z, which is **7/25** 23:50 AKDT. A Hawaii clip at 09:30Z falls on 2/28 in Honolulu but 3/1 in Alaska. Both cases are tested.
- **Location of a clip** is its first djmd fix, found by probing when the first sample has no fix.
  - The last fix is used only to check for a stale first fix: if first and last differ by more than R, use the last. It is UNVERIFIED whether DJI ever records a stale fix.

---

## 2. Clustering

### Parameters (all user settings)

| Setting | Default | Justification |
|---|---|---|
| G, day gap | 1 | Consecutive days join: Kodiak covers 5/22–5/25. The replay works for G from 1 to 11. |
| R, radius | 25 km | The replay works for R from 12.5 to 53 km. 25 km is about twice the largest folder spread (Nome Roads drive, 12.5 km) and about half the Council Road / Anvil Mountain split (53.2 km). The ratio is about 2.1 on both sides. |
| H, hours guard | 3 h | A date-gap split also needs this much time since the previous item. It only matters when G = 0: it stops a flight at 23:50–00:10 being split at midnight. |
| Photo attach window | ±1 local day | |

### Rules (sequential, in CaptureUtc order; videos only)

1. **Time split:** `(x.LocalDate − prev.LocalDate).Days > G` and `x.CaptureUtc − prev.CaptureUtc > H`.
   - `prev` is the previous item of any kind, so a run of daily flights forms a chain.
2. **Location split:** all of these hold:
   - x has GPS;
   - the group has a centroid (the mean of unit vectors, so it is safe across the antimeridian);
   - x's power-on session is not the session of any **GPS-bearing** member of the group;
   - the haversine distance from x to the centroid is more than R.
   - One rule covers both of the user's ideas. The "radius around where they're taken" is the distance to the centroid. A "big jump" is almost always also more than R from the centroid, because a group can only spread about R from its centroid.
3. **Items without GPS** never trigger rule 2 and never move the centroid. They join the current group if the time rule allows.
   - When rule 2 fires, each trailing item without GPS (after the group's last GPS item) is re-homed:
     - it moves with x if it shares x's session;
     - it stays if it shares the session of the last GPS item;
     - otherwise it goes to whichever side is nearer in time.
   - Tested cases: a clip with no GPS between Council Road and Anvil Mountain goes to Anvil Mountain. The first clip of a new session, taken without a fix, follows its session.
4. There is **no automatic re-merge of an A-B-A pattern**, where the same day visits site A, then B, then A again. The result is three groups, the tests fix that behaviour, and the user merges in the UI.

### Photos

- **Photos do not take part in setting video boundaries.** Photos go flat into the photo root, so a photo must not create a folder boundary that no photo will ever be copied into. DNG times are also derived (from the drone clock), so they are less certain.
- **Attaching:** each photo or set goes to the video group with the smallest time distance (zero if inside the group's span) whose local dates, ±1 day, include the photo, and whose centroid is within R of the photo (or either side has no GPS).
- **Leftover photos** are clustered with the same rules into **PhotosOnly** groups. These are for display only and have no folder.
- A card with only photos gives only PhotosOnly groups.

### Groups the user edits in the UI

- **Merge** takes the union, and the folder date is the earliest start.
- **Split** happens at a boundary the user picks. Each resulting group gets its proposed name recomputed from its own start date, unless the user has typed a full name.
- After any edit, `Decide()` runs again on the affected groups.

---

## 3. Library index and appending to an existing folder (no file contents read)

**Building the index from a directory listing** (names, sizes, mtimes only):
- **Key index:** `(lower(name), size)` for files under the video root **and** the photo root.
  - The photo root is currently `UAS Videos/Picture Offload`, which sits inside the video root and holds old Autel `MAX_*.MP4` files, so both roots are needed.
  - Keep separate indexes by name and by size (size only for files of 1 MiB or more) to detect conflicts and duplicates.
- **Event folders:** a file's nearest ancestor directory, at any depth, whose name matches `^(\d{4})-(\d{2})-(\d{2})(?:\s+(.*))?$`. So `2022/2022-03-27 Makaha Valley` works. The photo-root subtree is excluded from event folders.

**Date range of a library folder:**
- Take the name date, plus each DJI member's `filename stamp − learned offset`, converted with **the zone of the card group being compared** (if they are the same trip, they share a zone), plus the ledger's entries for that folder.
- Cross-check against mtime. For DJI library files, mtime ≈ creation time + duration, which is true UTC (per the earlier spike). If the two disagree by more than 2 h, use mtime and flag it.

**Where a library folder was shot**, in order of preference:
1. **The ledger**, for folders the app created or appended to.
2. **Leftovers on the card:** card files that match library files by name and size are read *from the card*. The location is then saved to the ledger, so it survives formatting the card.
3. *Optional, off by default, needs the user's decision:* read the djmd sample 0 of **locally present** library clips, meaning attributes without Offline or RecallOnDataAccess. This costs about 2 KB and triggers no download. It does bend the "never read content" rule.
4. Otherwise unknown.

### Decision for each video group (`Decide`)

| Case | Action | Confidence |
|---|---|---|
| No `New` members | `AlreadyImported`, or `Review` if only Conflict or PossibleDuplicate members | – |
| The group contains Imported clips from **one** library folder F | `Append(F)` | high |
| The group contains Imported clips from **several** folders | `AppendSplit`: each new clip goes to the folder of its nearest-in-time Imported neighbour. This respects boundaries the user already drew. | high |
| No Imported members. Find folder F with `F.nameDate ≤ g.start ≤ F.end + G` (**never add clips dated before a folder's name date**), and both locations are known and within R | `Append(F)` | high |
| As above, but one side's location is unknown, **and** the dates overlap (the group starts on or before the folder's end date) | `Append(F)` | medium (shown with a badge) |
| Dates only adjacent and location unknown | `NewFolder`, with "append to F?" offered in the dropdown | – |
| Otherwise | `NewFolder` | – |

If several folders qualify, pick the smallest date gap, then the smallest distance.

The "adjacent and unknown means new" default follows the user's own split of Council Road and Anvil Mountain on consecutive days. Unknown locations only happen for folders made before the app existed and never seen again on a card.

---

## 4. Folder naming

- **New folder:** `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <desc>`, using the **site-local date of the earliest new video**. The year and month folders also come from that start date, even if the trip runs into another month or year: 7/31–8/2 goes under `2026\2026-07\2026-07-31 …`, and 12/31–1/1 under `2026\2026-12\…`. Both are tested.
- **Append:** use the existing folder's real path, whatever its depth.
- **Description suggestions**, in order:
  1. The existing folder's description, when appending.
  2. A description used before for a ledger folder whose centroid is within R, for example "Anvil Mountain" again.
  3. Offline reverse geocoding. GeoNames `cities500` and `US.txt` are being tried in the `dotnet-stack/geonames` spike. The idea is to prefer a named feature (mountain, bay, park, spot) within about 5 km, else "near <nearest town>". **UNVERIFIED**.
  4. Blank. The user must type something, since every existing folder has a description.
- **Cleaning the description:**
  - Replace `<>:"/\|?*` and characters below 0x20 with a space.
  - Collapse whitespace and trim.
  - Remove trailing dots and spaces.
  - Limit to 80 characters.
  - Commas are allowed, as in "Newport, RI".
  - Reserved device names cannot occur, because the name always starts with the date.
- **Collisions** (compared case-insensitively):
  - Same date, different description: allowed. Show a hint: "2026-09-27 Zachar Bay exists — append instead?"
  - Name identical to an existing folder: treat it as an explicit append and ask for confirmation.
  - Two groups in this session with the same target: they are merged at commit, with a "will merge" badge.

---

## 5. What counts as new

### Videos, in this order

| Status | Test | Ticked by default |
|---|---|---|
| Imported | `(name, size)` found in the video-root or photo-root index | hidden |
| RemovedFromLibrary | Found in the ledger but not in the library (the user culled it) | ☐ |
| Conflict | Same name, different size. Often an earlier copy that was cut short. Never overwrite silently. | ☐ with a warning |
| PossibleDuplicate | Same size (at least 1 MiB) under a different name (the user renamed it) | ☐ with a warning |
| New | Anything else | ☑ |

DJI names contain a timestamp, so they are unique. Autel `MAX_NNNN` names repeat, which is why the key is name plus size.

### Photos (a set counts only if its set folder and every member name and size match, because `PANO_000N` repeats across sets)

1. Found in the ledger: **Imported**.
2. Name and size (plus the set folder) found in the photo root: **Imported**.
3. CaptureUtc is later than the **watermark**: **New** ("after the last imported video"). The watermark is the latest start time of any already-imported video, from library names minus the offset, or from the ledger. **Today it is 2026-09-27 18:24:16Z** (Zachar Bay 0148), as the replay confirmed.
4. The photo's local date is a date that has New videos: **New**.
5. Otherwise **ProbablyImported**, unticked. The reason shown is either "imported videos exist that day" or "photo-only day before the watermark".

### Where the heuristic fails, and what to do about it

| Failure | Effect | Mitigation |
|---|---|---|
| Photos taken on a day whose videos were imported, but the photos never were | Wrongly shown as ProbablyImported, so photos could be missed | Show them grouped by day with the reason, one click to include a whole day. Warn when finishing: "N photos on this card have no import record — don't format yet." The ledger ends this problem once the app is in use. |
| Photo-only day earlier than the watermark | Same as above | A separate reason text and highlight |
| Photos imported earlier on a day that now has new videos (the user offloaded partway through the day) | Copied twice | Harmless: Lightroom skips duplicates. The ledger prevents it after the first run. |
| Drone-clock offset changes (DST, or the clock is reset) | A DNG's local date is wrong near midnight | Offset from the nearest MP4; the time source is shown |
| Library clip renamed or deleted | Offered again as new | PossibleDuplicate and RemovedFromLibrary statuses, plus the ledger |
| **First run (right now)** | The photo root holds **no 2026 DJI DNGs except the PANO set 001_0087**. My reading is that Lightroom moves photos out of it, or the user cleans it (UNVERIFIED). So "present in photo root" almost never matches, and every DNG on today's card from before 18:24Z will be ProbablyImported. | Show a first-run banner. Optionally seed the ledger from the library listing. A later, optional check could open the Lightroom catalog read-only, and only when Lightroom is closed (UNVERIFIED and out of scope). |

**Ledger** (`%LOCALAPPDATA%\uas-sort\ledger`): one record per offloaded file.
- Fields: name, size, kind, CaptureUtc, TimeSource, lat, lon, tz, session, drone serial (djmd 1-1-5), destination path, verification hash, offload time.
- It also stores folder location and date range, and items the user chose to "never import", so they are not asked about again.

---

## 6. Pseudocode (mirrors `grouping.py`)

```csharp
Plan Build(IList<RawItem> card, LibraryListing lib, Ledger led, Settings s) {
  var items = Normalize(card, s);                      // §1
  var index = new LibraryIndex(lib, s.PhotoRootRel, led);
  var videos = items.Where(i => i.Kind == Video).ToList();
  foreach (var v in videos) ClassifyVideo(v, index);   // §5
  index.LearnLocationsFromCard(videos);                // leftovers -> folder centroids (+persist to ledger)
  var newDates = videos.Where(v => v.Status == New).Select(v => v.LocalDate).ToHashSet();
  var wm = index.Watermark(offset);                    // max(lib DJI name−offset, ledger utc)
  foreach (var p in items.Where(i => i.Kind != Video)) ClassifyPhoto(p, index, newDates, wm);
  var vg = Cluster(videos, s);                         // §2
  var pg = AttachPhotos(vg, photos, s);                // leftovers -> Cluster(...) => PhotosOnly
  foreach (var g in vg) Decide(g, index, s);           // §3
  return new Plan(vg, pg);
}

List<Group> Cluster(items, s) {
  foreach x in items.OrderBy(i => (i.Utc, i.Id)):
    if cur == null            -> start group
    else if TimeSplit(cur.Last, x) -> start group            // dateGap > G && Δt > H
    else if x.HasGps && cur.Centroid != null
            && !(x.Session != null && cur.GpsSessions.Contains(x.Session))
            && Haversine(x, cur.Centroid) > R:
         newG = {}; foreach t in cur.TrailingNoGps: if Rehome(t, lastGps: cur.LastGps, x) move t -> newG
         newG.Add(x); start newG
    else cur.Add(x)
}
bool Rehome(t, lastGps, x) =>
    t.Session != null && t.Session == x.Session ? true
  : t.Session != null && t.Session == lastGps?.Session ? false
  : (x.Utc - t.Utc) < (t.Utc - lastGps.Utc);
```

---

## 7. Unit tests (37, all pass; `test_grouping.py`)

Sites used in the tests:

| Name | Coordinates |
|---|---|
| ANVIL | 64.5627, −165.3696 |
| COUNCIL | 64.6935, −164.2657 |
| NOME_A / NOME_B | 12.0 km apart |
| ZACHAR | 57.5368, −153.7484 |
| KODIAK_TOWN | 57.7996, −152.3902 (85 km from Zachar Bay) |
| NEWPORT_AM / NEWPORT_PM | 4.8 km apart |
| MAKAHA | 21.47, −158.21 |

Unless stated, the drone clock is UTC−4 and the PC zone is AK.

**Normalization**
- **Midnight:** clips `20260726035000` and `…041000` at ANVIL give 1 group starting **2026-07-25**, at `2026/2026-07/2026-07-25 …`.
- **G = 0 across midnight:** the same pair is still 1 group, because of the H guard.
- **DNG time:** DTO `2026-07-26 03:55` next to an MP4 on the card gives `droneclock+learned`, 07:55Z, local date 7/25.
- **Hawaii:** a clip at 09:30Z at MAKAHA gets zone Pacific/Honolulu and local date **2/28** (in AK it would be 3/1).
- **Clock change:** an MP4 in October at −4 and one in November at −5; a DNG in November gets 17:10Z (−5).
- **No MP4 on the card:** with the −4 h setting, source is `droneclock+setting`. With no setting, source is `mtime`.
- **Truncated clip (no `moov`):** time comes from the filename minus the learned offset (04:20:13Z), and it joins its group.
- **Autel MAX_0061–0065 plus a DNG without EXIF:** all on 2022-03-27, sources are floating, 1 group.

**Clustering**
- 7/31 → 8/1 → 8/2 within 3 km: 1 group, folder under `2026-07/2026-07-31`.
- 12/31 → 1/1: folder under `2026/2026-12/2026-12-31`.
- A 3-day date gap splits.
- **Consecutive days far apart:** COUNCIL on 7/25 and ANVIL on 7/26 give 2 groups.
- **Kodiak-style:** 4 consecutive days within 10 km give 1 group (5/22–5/25).
- **Same day, two distant sites:** ZACHAR then KODIAK_TOWN give 2 groups, both starting 9/27.
- **A-B-A on the same day:** 3 groups (fixed behaviour).
- Newport morning plus evening (4.8 km): 1 group. Nome A plus B (12 km): 1 group.
- A clip without GPS between COUNCIL (7/25) and ANVIL (7/26) goes to Anvil (nearer in time). When it shares Council's session, it stays with Council.
- **First clip of a new session without a fix:** ZACHAR (s1), no-GPS clip (s2), KODIAK_TOWN (s2) gives `[Z]`, `[noGPS, Kodiak]`. This is the regression test for the session bug.
- Everything without GPS: grouping by time only.
- R = 60 merges Council Road and Anvil Mountain; R = 25 splits them.

**Decisions and newness**
- **Video-only card, empty library:** `NewFolder 2026/2026-09/2026-09-27`, and no photo groups.
- **Append to today's folder:**
  - Zachar Bay clips left on the card plus 2 new ones at 16:00 drone clock: `Append(… Zachar Bay)`, high.
  - No leftovers, but ledger centroids: Append, "high (date + location)".
  - No leftovers and no ledger, same date: Append, **medium**.
- **Next day:**
  - At KODIAK_TOWN with a ledger centroid for Zachar Bay: `NewFolder 2026-09-28`.
  - At ZACHAR with a ledger centroid: Append.
  - Location unknown: NewFolder.
  - Clips dated **before** the folder's name date (9/26): NewFolder, never an append.
- **Legacy Autel:** library `2022/2022-03-27 Makaha Valley` holds 0061, 0062 and 0064; the card adds 0065: Append to that path (no month level).
- **AppendSplit:** the same site on two days that the user split into AM and PM folders; each new clip goes to the folder of its nearest-in-time neighbour.
- **Card with leftovers:** Council Road and Anvil Mountain (both imported) plus Zachar Bay (new), with DNGs on 7/25, 9/27 and 8/15.
  - Groups: `[AlreadyImported, AlreadyImported, NewFolder]`.
  - Photos: 7/25 is ProbablyImported, 9/27 is New, and 8/15 is New (after the watermark), in a PhotosOnly group.
- **Photo-only day before the watermark** (library holds Zachar Bay; photo on 8/15): ProbablyImported, reason mentions "photo-only".
- **Card with only photos, after the watermark:** both New, 1 PhotosOnly group.
- **Panorama sets:** `001_0087` present in the photo root is Imported. `001_0112`, with the same PANO names and sizes, is not.
- **Video statuses:** same name with a 7 MiB size gives Conflict. Same size under a renamed file gives PossibleDuplicate. In the ledger but missing from the library gives RemovedFromLibrary.
- **Cleaning names:** "Newport, RI" is kept. ` A/B: "test"? ` becomes `A B test`. `Nome Rd...` becomes `Nome Rd`. An empty description gives just the date.

**Replay on the real library (`replay.py`)**
- It produces 8 groups, which map exactly onto the 8 user folders. Newport's zone resolves to America/New_York; Kodiak and Makaha have no GPS.
- It gives the R and G ranges quoted in §0.
- Scenarios:
  - Card equals the whole library: all `AlreadyImported`, each pointing at the correct folder.
  - Library without Zachar Bay: `NewFolder 2026/2026-09/2026-09-27` with 13 clips.
  - Zachar Bay clips 0140–0148 missing from the library: `Append(2026/2026-09/2026-09-27 Zachar Bay)` with 4 new clips.
  - Anvil Mountain removed from the library, Council Road leftovers on the card: Council Road's location is learned from the leftovers, and Anvil Mountain becomes `NewFolder 2026-07-26` (53 km).

---

## 8. Unverified points and open questions

- **Kodiak.** Its GPS is unknown because all 30 files are cloud-only. If the trip went more than 25 km from its centroid (the Kodiak road system spans about 60 km), the default would split it, and the user would merge in the UI. UNVERIFIED.
- **GeoTimeZone, and IANA zone names through `FindSystemTimeZoneById` on Windows:** stubbed here. The dotnet-stack spike is checking them.
- **Drone clock and DST:** whether it is fixed at UTC−4 or follows Eastern DST is unknown. Whether card mtimes on exFAT are true UTC is also UNVERIFIED.
- **Stale first GPS fix** after power-on: UNVERIFIED. The first/last-fix check guards against it.
- **Decisions for the user:**
  - Whether to allow the opt-in read of locally present library files for location.
  - Whether to add a cap on how many days a group can span (none today; daily flights within 25 km chain into one group).
  - What ticking a Conflict item should do: replace the library copy with a confirmation, or skip.
- **Pitfall for the C# port:** the session link must use the sessions of **GPS-bearing members only**, as the test and fix above show.