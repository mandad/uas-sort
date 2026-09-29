# First-card acceptance: the expected folder list

Before the first-real-card acceptance (design reference §13, step 2), write down where you expect each clip on the card to go, **before** you run the dry run, so the comparison is honest.

1. Copy `first-card-expected.example.json` to `first-card-expected.json` in this folder.
2. For each folder you expect, give its path relative to the video root (`relPath`, e.g. `2026\\2026-10\\2026-10-04 Nome Roads` in JSON; for an append, the existing folder's path) and the file names of the MP4 clips that belong in it (`clips`, bare names such as `DJI_20261004163012_0151_D.MP4`). Optionally add `"target": "NewFolder"` or `"target": "Append"` if you want the target kind compared too. Clips you leave out are not compared. The file may contain `//` comments and trailing commas; `first-card-expected.schema.json` describes it for editors.
3. With the card in the reader (here `E:\`), run:

   ```powershell
   dotnet run --project src/UasSort.Cli -- plan --card E:\ --json --expect tests\acceptance\first-card-expected.json
   ```

   The JSON plan goes to stdout; the comparison goes to stderr (to stdout without `--json`) and ends with a line like `EXPECT edits: 1 (passes at <= 2): PASS`. The dry run writes nothing anywhere.

**How edits are counted** (one edit = one change you would make in Review, a `PlanEdit`):
- an expected folder whose clips are spread over k plan groups: k − 1 merges;
- a plan group holding clips of k expected folders: k − 1 splits or moves;
- 1 for each expected folder whose best-matching plan group has a different folder path (a rename) or, when you gave `target`, a different target kind (a retarget).

Expected clips that are not on the card are listed but not counted. Step 2 passes at **2 edits or fewer**. Also note the scan + plan time printed on stderr (success criterion 4).
