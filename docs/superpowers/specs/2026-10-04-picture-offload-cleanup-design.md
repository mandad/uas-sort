# uas-sort — Picture Offload cleanup: design spec

Status: approved in conversation 2026-10-04 (brainstorming, architectural path). Extends the main spec `2026-09-27-uas-sort-design.md` and its reference `2026-09-27-uas-sort-design-reference.md`; where this document is silent, they govern. Outside code, `&lt;` stands for a literal less-than sign.

## 1. Purpose and intent

After a card is offloaded, photos land in the **photo root** ("Picture Offload", e.g. `C:\Users\damia\OneDrive\Pictures\UAS Videos\Picture Offload`). The user then imports them into Lightroom with **Copy as DNG** into a Lightroom library folder on `D:`, renaming them (their name + a date serial). After that, the Picture Offload copies are redundant and only use OneDrive space. This feature removes them **safely**.

What the user said:
- Import is Copy as DNG to a library folder on D:, with renamed files.
- Most of the time they will pick a **date cutoff**; a **verify-against-Lightroom** mode must exist for extra protection.
- Deleted items go to the **Recycle Bin**.
- Cover **single photos and set folders** (panoramas, hyperlapses). Panorama frames are only imported into Lightroom when DJI's stitch isn't good enough; hyperlapse frames almost never (the DJI result video is used).
- Approach: a **separate "Clean up Picture Offload" page** (not a mode of card cleanup, not CLI-only).

Assumptions (agreed): nothing is deleted that is the only copy without an explicit, second acknowledgement; the app never writes to the Lightroom folder and never touches `D:\LR_Catalog`.

Success: the user picks a cutoff, reviews a clear list, confirms, and the chosen Picture Offload items move to the Recycle Bin, each recorded in the ledger; a later card holding the same photos never shows them as New; nothing outside the photo root is ever touched.

## 2. Screens and flow

- **Entry:** a **[Clean up Picture Offload…]** button in the shell title bar next to [Clean up card…], and a link on the Settings page. Enabled whenever no offload, card cleanup or scan is running and the photo root is set and available; no card is needed. When disabled, the reason is shown as visible text (same pattern as Task U4 for card cleanup).
- **Settings:** a new optional **Lightroom library folder** (`Settings.LightroomFolder`, string?, default null; folder picker; must not be inside the photo root, the video root, or `D:\LR_Catalog`, and must not be the photo root's ancestor). Verify mode is offered only when it is set and available.
- **Step 1 — Choose:** a **cutoff date** ("photos shot on or before", `DateOnly`, local date per §5.2 of the main spec), and a **"Verify against Lightroom"** switch (off by default; disabled with a reason when no Lightroom folder is configured). [Next] builds the plan.
- **Step 2 — Review:** a list grouped by local date. Each **photo unit** (a photo, or a DNG+JPG pair) and each **set** (one row per set folder: kind, date, frame count) has a thumbnail (existing ThumbnailCache; never hydrates — cloud-only items show a placeholder), size, status, and a **Keep/Delete** toggle. In date mode every eligible row starts **Delete**; in verify mode verified rows start Delete and unverified rows start **Keep**. Totals: "N photos, M sets, X GB to the Recycle Bin". [Delete all] / [Keep all]. Unverified rows show the reason ("not found in Lightroom", "not on this PC (cloud-only) — couldn't check", "hyperlapse result video not in the library", …).
- **Step 3 — Confirm and run:** acknowledgement "Move N items (X GB) from Picture Offload to the Recycle Bin". If any **unverified** row is set to Delete (only possible in verify mode), a second acknowledgement: "K of them are not confirmed in Lightroom or your library — the Recycle Bin may hold their only copy". Then the run with progress (cancellable between items), then a **result** page: moved / kept / skipped-because-changed / failed, the Recycle Bin hint, and the saved report path.

## 3. What is eligible

- Only **direct children of the photo root**: loose photo files (DNG, JPG and the other photo extensions the scan already treats as photos) and **set folders** (the folders the offload creates for panoramas / hyperlapses / other sets, per §8.8 of the main spec). Nothing in any other folder; never the video root; never a previous photo root (out of scope v1); never files outside the photo root.
- A photo unit is eligible when its **capture local date ≤ cutoff**. A **set** is eligible when **every** member's capture local date ≤ cutoff (a set straddling the cutoff is not eligible).
- **Capture time source, in order:** (1) the ledger `file` record for that destination (`Dest`) — its `CaptureUtc`/`LocalDate`; (2) the photo's EXIF `DateTimeOriginal` (+ sub-seconds, + offset or the clock model as the scan does) read through the existing StillProbe **only if the file is fully local** (not a cloud-only placeholder; checked by attributes first, never hydrating); (3) otherwise the item is **"date unknown (cloud-only)"** and is shown but **not eligible** in either mode (it can't be dated without downloading it).
- Unknown/other files under the photo root are listed on the Review page's "Not touched" note with a count, never deleted.

## 4. Verification (verify mode)

- The Lightroom folder is scanned **read-only**, recursively, for `.dng` files whose **capture date falls in the eligible date range ± 1 day** (filter by file mtime and folder names first where possible; read EXIF only for candidates). Its index: `(DateTimeOriginal to the sub-second, camera Model)` → file. `D:\LR_Catalog` and any `*.lrcat*` / `*Previews.lrdata` / `*Smart Previews.lrdata` folders are excluded and never opened.
- **Photo unit verified:** a Lightroom DNG with the **same DateTimeOriginal (including sub-seconds when both have them; else to the second)** and the **same camera Model**. For a DNG+JPG pair, the DNG decides.
- **Set verified** when any of:
  - every member is verified as a photo; or
  - **hyperlapse:** the ledger holds a `file` record of kind `video` whose capture session matches the set's session (same `SessionUtc`, or capture time within the set's span ± 2 min on the same drone `Serial`) — i.e. DJI's result video was copied to the video library by this app. **Branch-2 ruling:** the offload records stills without a session or serial (only MP4s carry one), so the working link is a `video` file record from the **same offload Run** as the frames' file records (the same card) whose capture time lies within the set's span ± 2 min (evidence `hyperlapseResult`); the session/serial paths stay as additional ways; frames with no file record are unverified ("these frames have no offload record in the history"); or
  - **panorama:** a Lightroom DNG/JPG matches the **stitched panorama image** DJI produced for that set (its capture time and Model), where the stitched image is identified as in the scan's set rules. **Branch-2 ruling:** if any JPG-only photo without a stamp (cloud-only, unreadable, no DateTimeOriginal) is dated within the frames' local dates ± 1 day or has no date, the set is unverified ("a JPG next to the frames couldn't be read — can't tell which is the stitched panorama"), so another panorama's stitch never verifies it.
- **What the walk couldn't read (branch-2 ruling):** the index's listing errors, unreadable files (cloud-only/corrupt) and count travel with the plan, are shown on the Review/Confirm page ("Part of the Lightroom library couldn't be read: …") and written to the report (`lightroom`); while any exist, row reasons say "not found in the part of the Lightroom library that could be read". A library folder inside a protected root is an Errors entry, never a silent empty index.
- Everything else is **unverified** with its reason. Date mode does not run verification and labels rows "not verified (date mode)".

## 5. Safety model

- **New Platform port** `IPhotoRootRecycler` (Platform only; Core/Review/App never touch the file system): `RecycleAsync(ConfirmedPhotoCleanupPlan plan, IProgress<…>, CancellationToken)` → per-item outcome. It moves each item to the **Windows Recycle Bin** (`IFileOperation` with `FOFX_RECYCLEONDELETE` / `FOF_ALLOWUNDO`, no UI, no confirmation dialogs). Folders (sets) are recycled as a unit.
- **Guard (IoGuardPolicy extension):** a new operation, e.g. `IoOp.PhotoRootRecycle`, allowed only for a path that is **in the confirmed plan** and **directly under the photo root** (a set folder, or a file in the root). Refused for anything else, including the video root, previous photo roots, the ledger folder, the Lightroom folder and `LR_Catalog`. The new op is added to BannedSymbols suppressions only inside the recycler (with a justification), and a source-guard test (like "eraser-only deletes") asserts only `IPhotoRootRecycler` recycles/deletes under the photo root.
- **Per-item re-check just before the move:** the item still exists with the same size and mtime (sets: same member list, sizes, mtimes) as at review; otherwise it is **skipped** ("changed since review"). Cloud-only items are never opened; they are recycled by path (the OneDrive behaviour for a cloud-only file is **UNVERIFIED** — §8 acceptance proves it on a scratch file before the first real run).
- **ConfirmedPhotoCleanupPlan** mirrors ConfirmedCleanupPlan: built only by `PhotoCleanupPlan.Confirm(ack, clock)` from a reviewed plan; carries the photo root, the exact relative paths, sizes/mtimes, verification status per item, and both acknowledgements; immutable.
- **Lightroom folder:** read-only through the existing guarded lister/reader; never written; `D:\LR_Catalog` refused by the guard.
- Single-instance and the existing offload lock: the page takes the offload lock while running (no offload/card cleanup concurrently).

## 6. Records and newness

- **Ledger record** `photoDelete` (new `LedgerRecord` kind, JSON `"t":"photoDelete"`, v1): `Id, Machine, Run, At, Name, Size, Dest` (the photo-root path that was recycled), `CaptureUtc?`, `Set?`, `Evidence` (`lightroom` | `hyperlapseResult` | `panoramaStitch` | `dateOnly` | `unverifiedConfirmed`), `Mode` (`beforeDate` | `verify`), `Cutoff` (DateOnly). One record per file (set members each get one, carrying `set`). Written through the existing `LedgerWriter` (own ledger file) **after** each successful move; a failed append is reported on the result page (like the offload's LedgerIncomplete) and never silent.
- **Newness (§7.3 rule 1 extension):** an unrevoked `photoDelete` record for a photo (by `FileKey` = name+size, and for sets every member) counts like a `file` record → **Imported**, "removed from Picture Offload on <date>". A photo the app copied already has its `file` record, so this mainly protects photos that reached the photo root another way.
- **Card cleanup evidence (§10.6):** a photo with a `photoDelete` record is eligible like one with a ledger `file` record (evidence `InLedger`, detail "removed from Picture Offload after Lightroom import" / "removed by date"). Videos are unaffected.
- **Report:** `%LOCALAPPDATA%\uas-sort\reports\yyyyMMdd-HHmmss-run8-photos.json` (CoreJsonContext), listing every item with its decision, verification, outcome.
- **Snapshot:** the ledger snapshot/backup behaviour is unchanged (the existing SnapshotToBackup runs before the first append, as for the offload).

## 7. Components (fits the existing layout)

- **Core** (namespace `UasSort.Core.Cleanup`, folder `src/UasSort.Core/Cleanup/Photos/` — reuses the existing global using; no new namespace): `PhotoCleanupPlanner` (eligibility + capture-time resolution from ledger/EXIF + cutoff), `LightroomIndex` (build from a listing + EXIF probe results; match), `PhotoCleanupVerifier` (photo/set/hyperlapse/panorama rules), `PhotoCleanupPlan` / `ConfirmedPhotoCleanupPlan`, `PhotoCleanupExecutor` (re-check, recycle via port, ledger records, report), `PhotoDeleteRecord` + newness/cleanup-evidence hooks, `IoGuardPolicy` extension, `Settings.LightroomFolder` (+ codec, defaults, Settings page row).
- **Platform:** `WindowsPhotoRootRecycler : IPhotoRootRecycler` (IFileOperation recycle), guard wiring, reuse of the lister and StillProbe reader for the Lightroom folder.
- **Review (VMs):** `PhotoCleanupVm` (steps, rows, toggles, acks, run, result), availability + visible reason, ShellVm stage `PhotoCleanup`.
- **App:** `PhotoCleanupPage` (x:Bind only, AOT-safe, FlowPanel/ItemsRepeater as needed), title-bar button, Settings row + folder picker; selftest checks for the page.
- **CLI:** none in v1.

## 8. Testing and acceptance

- **Core tests:** eligibility by cutoff (date edges, sets straddling, pairs), capture-time source order (ledger, local EXIF, cloud-only → not eligible), verification (photo match incl. sub-seconds/Model mismatch, set by frames, hyperlapse by ledger result video, panorama by stitched image), plan → confirm → executor with a fake recycler (re-check skip, cancel, failures, ledger records, report), newness and card-cleanup evidence from `photoDelete`, guard refusals (outside photo root, video root, ledger, Lightroom folder, `LR_Catalog`).
- **Platform tests:** `WindowsPhotoRootRecycler` against `%TEMP%\uas-sort-test-*` folders only (moves to the Recycle Bin and verifies the source is gone; then the test **purges only its own items** from the Recycle Bin or uses a scratch sub-folder naming scheme so nothing of the user's is touched); guard refusals end-to-end.
- **Selftest:** the page renders, the plan/review flow works on a synthetic photo root inside the selftest sandbox, recycling only sandbox files.
- **User acceptance (first real use):** (1) set the Lightroom folder; (2) on a **scratch copy** (a test folder set as a temporary photo root) recycle one photo and one set, including a cloud-only OneDrive file, and confirm they appear in the Recycle Bin / OneDrive recycle bin and that cloud-only recycling behaves (closes the UNVERIFIED in §5); (3) real run with a cutoff covering one old day in verify mode; check the Recycle Bin, the `photoDelete` records, and that a rescan of a card holding those photos shows them Imported.

## 9. Out of scope (v1)

Previous photo roots; permanent delete; deleting from the Lightroom folder; reading or writing the Lightroom catalog; automatic scheduling; CLI.
