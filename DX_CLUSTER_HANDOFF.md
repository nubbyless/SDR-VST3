# DX CLUSTER (TELNET) HANDOFF

Created: 2026-10-16
Branch: `master`
Latest state (2026-10-17): thetisvst HEAD `38c29364`, HL2 HEAD `fb33d35`.
SWL (shortwave broadcast) spot toggle **implemented, built 0 errors (uncommitted)** — see the
"SWL spot toggle — IMPLEMENTED 2026-10-17" section at the bottom.
**Scroll to the bottom — the newest UPDATE sections and the "NEXT: SWL spot toggle"
plan supersede the original phased plan at the top.**
Scope: Design + phased build plan for a **native telnet DX cluster client** integrated
into Thetis. Phase 1 code not yet written at time of writing; findings below are verified
against the working tree.

---

## UPDATE 2026-10-16 (spots independent of TCI flag; spot list perf)

Goal: DX cluster spots must draw on the panadapter **without** requiring
`Setup > TCI Server > Show TCI Spots`, while TCI spots keep behaving exactly as before.

- **`display.cs`**: the 10 paint call sites were `if (_showTCISpots) drawSpots(...)`;
  now `if (shouldDrawSpots) drawSpots(...)` where
  `shouldDrawSpots = _showTCISpots || SpotManager2.HasSpotsFromPrefix("dxcluster:")`
  (added next to the `ShowTCISpots` property, `:13669`). Inside `drawSpots` the spot
  loop now skips non-cluster spots when `_showTCISpots` is false
  (`if (!_showTCISpots && !isClusterSpot(spot)) { Visible=false; continue; }`), so the
  flag still gates **TCI** spots only. `isClusterSpot` matches `source` prefix
  `"dxcluster:"`.
- **`SpotManager2.cs`**: added `HasSpotsFromPrefix(string)` and `isExternallySourced(smSpot)`
  (`source` non-empty). Externally sourced (cluster) spots are now excluded from:
  the `_maxNumber` (TCI "max spots") eviction count and candidate scan, the
  `ClearAllSpots` (TCI "clear spots") sweep. They **still** age out via the normal
  `_lifeTime` sweep. Net effect: TCI's cap/clear/lifetime settings keep their exact
  meaning; a busy cluster feed can no longer evict or clear TCI spots.
- **`DxClusterForm.cs`**: spot list perf — capped displayed rows at `MaxDisplayedSpots`
  (500, newest), batched layout (`SuspendLayout`/`ResumeLayout`), added
  `buildSpotRow` helper, enabled `DoubleBuffered` on both grids (`EnableDoubleBuffer`
  via reflection), fixed row height (18px), refresh cadence 1000ms -> 500ms. Removed the
  now-inaccurate "enable Show TCI Spots" status hint. Toolbar button "Servers..." ->
  "Server List"; dialog title "DX Cluster Server List".
- Build: `dotnet build "Thetis.csproj" -c Release -p:Platform=x64` -> 0 errors
  (exe 2026-10-16 22:14). NOTE: must close the running Thetis first or the RawInput/
  Midi2Cat post-build copies fail with MSB3027/MSB3021 (file locked).
- **Duplicate-server guard**: `DxClusterServersForm.onAdd` rejects a host:port already in the
  list (built-in default or prior add) with an "already in the list" message and ticks/
  selects the existing row (`alreadyListed`/`selectExisting`). `DxClusterManager.AddConnection`
  also returns an existing live connection for the same host:port instead of creating a second
  (belt-and-braces against duplicate connections). Build 0 errors (exe 2026-10-16 22:17).
- **Server List now applies live**: `DxClusterServersForm` subscribes `ItemCheck` (after the
  initial populate, guarded by `_loading`); ticking a node calls `EnsureConnected` immediately,
  unticking calls `DisconnectAndRemoveNode` immediately (so an untick removes the active
  connection straight away - the real fix for "re-ticking adds another one"). "Connect Checked"
  /"Cancel" replaced by a single "Close" button; `SelectedServers`/`applyServerSelection`
  removed. Caller `onServers` now sets the callsign, shows the dialog, then just refreshes the
  grids (changes are already applied). Cost: at most one connection per host:port. Build 0
  errors (exe 2026-10-16 22:23).
- **Duplicate rows on dialog close (fixed)**: `onServers`/`onDisconnectAll`/`removeSelectedNode`
  called `_connRowByKey.Clear()` immediately before `RefreshConnections()`. Clearing the dict
  (but not the grid rows) made `RefreshConnections` add a brand-new row per node while the old
  rows stayed (their keys were still in `live`, so the removal pass skipped them) -> the whole
  node list appeared to be "added again" on close. The `Clear()` calls are removed; the method's
  own reconcile logic handles added/removed nodes. Also: unticking now purges that node's spots
  (`DxClusterManager.DisconnectAndRemoveNode` removes them from the manager list and calls
  `SpotManager2.ClearSpotsBySource("dxcluster:"+host)`), and the redundant post-close
  `ResyncPanadapter()` was dropped (it caused a full spot re-push/flash). Build 0 errors
  (exe 2026-10-16 22:28).

---

## UPDATE 2026-10-16 (default server picker added)

- New `DxClusterDefaults.cs` holds the built-in public telnet node list
  (`ve7cc.net:23`, `dxspider.co.uk:7300`, `db0erf.de:41113`, `gb7baa.com:7300`,
  `gb7djk.dxcluster.net:7300`, `it9obk.org:41112`, `dx.harwood.com:8000`,
  `dxusa.net:7300`, `dxc.middlebrook.ca:8000`, `dxc.jg1vgx.net:7300`) as
  `DxClusterServerEntry` records.
- New `DxClusterServersForm.cs`: modal **Servers...** checklist (built-ins + any
  configured custom nodes) plus Name/Host/Port "Add" for user entries. Checked =
  `DxClusterManager.IsConfigured(host, port)` (i.e. persisted node existence), so the
  selection survives restarts. "Connect Checked" returns the ticked set; the main form
  applies it: unticked configured nodes are disconnected+removed, ticked nodes are all
  connected at once (`DxClusterManager.EnsureConnected` — one thread per node).
- **No auto-connect.** Auto-connect only happens for nodes whose per-node **Auto** flag
  is set (`ConnectAutoNodes`), which defaults off. Ticking in the picker never connects
  on startup; it only connects immediately on the user's explicit "Connect Checked".
- Manager gained `IsConfigured`, `EnsureConnected`, `DisconnectAndRemoveNode`.
- Build: Release x64 → 0 Errors.

---

## UPDATE 2026-10-16 (Phase 3 done)

- **Persistence wired.** `DxClusterManager` now owns a single `DxClusterSettings`
  (login callsign, master panadapter flag, filter band/text, node list with per-node
  `AutoConnect` / `ShowOnPanadapter` / `ColorArgb`). `LoadSettings()` runs from
  `console.Console_Shown` via new `initDxCluster()`; `ConnectAutoNodes()` opens any node
  flagged auto-connect. Settings saved on every user change and on shutdown.
- **Manager now owns its own bounded spot list** (`MaxSpots = 3000`, 60 s call/freq
  dedup). A spot is pushed into `SpotManager2` (panadapter) only when the master flag
  AND the owning node's flag are set; `ResyncPanadapter()` clears+re-adds cluster spots
  when either changes.
- **Form rebuilt** (`DxClusterForm.cs`): node list now shows all configured nodes with
  columns Node / State / Auto / Pana / Colour (checkbox + colourswatch, click to pick
  colour, right-click menu Connect/Disconnect/Remove Node). Toolbar has Connect /
  Disconnect / Disconnect All / **Spots on Panadapter** master toggle / Clear Spots.
  Filter row: band combo + free-text (callsign/spotter/comment). Spots list newest-first.
- **Dark theme pass**: new `DxClusterTheme` palette (mirrors `VstRackTheme` values) +
  a `ProfessionalColorTable` for the tool strips; grids, status bar and form themed.
- **Build:** Release x64 → **0 Errors** (only CA1416 platform-analyzer warnings, same
  category as the project-wide ~24.7k).

---

## UPDATE 2026-10-16 (Phases 1 & 2 done; UI + backend build clean)

- **Phase 1 (backend) DONE.** Added `DXCluster\DxClusterParser.cs`,
  `DxClusterConnection.cs`, `DxClusterManager.cs`. `SpotManager2.smSpot` gained a
  `source` field and `AddSpot(...)` an optional `source` param. Persistence via
  `DxClusterSettings` (JSON in `Options` DB, key `dx_cluster`). Registered in csproj.
- **Phase 2 (popup UI + menu) DONE.** Added `DXCluster\DxClusterForm.cs` (code-only,
  modeless; connection grid + spots grid + toolbar + status bar; double-click tunes
  RX1; persists geometry as `Common` table `DxClusterForm`). Added
  `SpotManager2.ClearSpotsBySource(string)`.
- **Menu wiring DONE.** `console.Designer.cs` now has `dxClusterToolStripMenuItem`
  placed between `wsjtXToolStripMenuItem` and `eSCToolStripMenuItem` (Text
  `"DX Cluster"`; AddRange at ~`:4300-4306`). `console.cs` gained
  `public DxClusterForm dxClusterForm;` and handler `dxClusterToolStripMenuItem_Click`
  → `showDxClusterForm()` (create-on-first-use; form hides itself on user-close).
  Shutdown hides it (~`:28698`) and calls `DxClusterManager.DisconnectAll()` +
  `dxClusterForm.Close()` (~`:28756`).
- **Build:** `dotnet build "Thetis.csproj" -c Debug -p:Platform=x64` → **0 Errors**.
  Only new warnings are `CA1416` platform-analyzer notices (same category as the
  ~24.5k pre-existing project warnings; non-fatal, not `TreatWarningsAsErrors`).
- **Remaining (Phase 3):** filters (band/mode/callsign), per-connection panadapter
  tags/visibility toggles, skin pass on the form, and wiring `DxClusterSettings`
  into the Setup/`SaveOptions` flow. Cluster spots on the panadapter still require
  Setup "Show TCI Spots" (`Display.ShowTCISpots`).

---

## 0. Requirement / locked decisions

- Add a **built-in telnet DX cluster client** to Thetis.
  - Spots feed the existing panadapter spot overlay.
  - A dedicated **modeless popup window** shows the spot list plus cluster controls
    (connect/disconnect, node/port, login/callsign, filters, options).
  - Support **multiple parallel telnet node connections** simultaneously.
- Transport is **raw telnet** (not a JSON/web feed). This mirrors the classic
  `spot.cs`/`SpotControl` behaviour (see §2).
- The whole feature is opened from a **top-level console menu item named `DX Cluster`**,
  placed **between `WSJT-X` and `Diversity`** in the menu strip.
- Build **Phase 1 now** (backend: connect + parse + feed `SpotManager2`).
- **Write this handoff file before any code.** (done)

---

## 1. Verified findings (file:line)

### 1.1 Existing spot pipeline (intact, single source of truth)

- `Project Files/Source/Console/SpotManager2.cs` — static store.
  - `class smSpot` at `:85`; fields `:87-120`.
  - `AddSpot(string callsign, DSPMode mode, long frequencyHz, Color colour,
    string additionalText, JsonSpotData jsonSpotData = null)` at `:473`.
  - `class JsonSpotData` at `:68` (Spotter, Comment, Heading, Continent, Country,
    UtcTime `"yyyy-MM-ddTHH:mm:ssZ"`, TextColor, Flag, FlagSpotter, Distance, IsSWL,
    SWLSecondsToLive). **The cluster parser can build a `JsonSpotData` and reuse the
    existing `AddSpot` path** — no new store needed.
  - De-dup: same callsign within 5000 Hz collapses (`:593-623`).
  - `GetFrequencySortedSpots()` `:675`; lifetime/max managed by `_tickTimer` `:244-259`.
- `Project Files/Source/Console/display.cs` — render.
  - `drawSpots(int rx, int nVerticalShift, int W, bool bottom)` at `:13288`,
    consumes `SpotManager2.GetFrequencySortedSpots()` (`:13292`).
  - `_showTCISpots` gate at `:13661` (spots only painted when true).
- `Project Files/Source/Console/TCIServer.cs` — spot ingress (`:4328`, `:4065`,
  `:3226`, `:4490`) via the same `AddSpot`.

### 1.2 The old built-in cluster was removed

- Removed `spot.cs` (KE9NS/Darrin Kohn `SpotControl`) deleted in commit
  `58b0e2da` "Code clean-up" (2020-01-24). Recover via
  `git show 58b0e2da^:"Project Files/Source/Console/spot.cs"`.
  - It used its **own** parallel spot arrays (not `SpotManager2`) — do **not** resurrect
    that pattern; feed `SpotManager2` instead.
  - A working copy was extracted to
    `C:\Users\W4YNY\AppData\Local\Temp\opencode\spot_removed.cs` (UTF8, 250635 bytes;
    **temp — may not persist**, re-extract from git if missing).
- Remnant: `Project Files/Source/Console/Memory/DXMemList.cs` (legacy node URL list).

### 1.3 Menu anchors (verified)

- `Project Files/Source/Console/console.Designer.cs`:
  - menu strip `AddRange` list `:4293-4317`.
  - `wsjtXToolStripMenuItem` (Text `"WSJT-X"` at `:4401`) added at `:4302`;
    field decl `:354`; init block `:4397-4402`.
  - **The visible `Diversity` item is `eSCToolStripMenuItem`** — its Text lives in
    resources: `console.resx:9091` `<value>Diversity</value>`, applied by
    `resources.ApplyResources(this.eSCToolStripMenuItem, "eSCToolStripMenuItem")`
    (`console.Designer.cs:4408`). Init block `:4404-4409`; added at `:4303`.
  - ⇒ Insert the new `dxClusterToolStripMenuItem` **between `:4302` and `:4303`** in the
    AddRange, and its field/init block near `:4397-4403`.
- New source files must be registered in
  `Project Files/Source/Console/Thetis.csproj`.

### 1.4 Existing networking / popup patterns to copy

- Telnet/TcpClient thread model: `Project Files/Source/Console/MeterManager.cs`
  (`TcpClientHandler` ~`:43477`).
- Manager + notifier pattern: `FreeDVReporter/FreeDVReporterManager.cs` (`:33`, `:557`).
- Modeless popup forms owned by console: `console.cs` fields `:137-162`, `:523-525`;
  guard pattern ~`:41457`; `DiversityForm` field `:147`.
- UI refresh on a form timer: `FreeDVReporter/FreeDVReporterForm.cs:278`.
- Sortable grid pattern: `frmFinder.cs` / `frmSeqLog.cs` (`SortableBindingList`).

---

## 2. Architecture (proposed)

```
telnet nodes ──► DxClusterConnection (one per node, background thread)
                     │  parses raw lines
                     ▼
                 DxClusterParser  ──► SpotManager2.AddSpot(..., JsonSpotData)
                     │                        ▲
                     │ raises Spot event      │ existing de-dup / lifetime / sort
                     ▼                        │
                 DxClusterManager  ───────────┘
                     │  owns N connections, aggregates a UI-facing list
                     ▼
                 DxClusterForm (modeless popup: grid + controls)
```

- **`DxClusterConnection`** — owns one `TcpClient` + reader thread; handles login
  (`callsign`), keepalive, reconnect/backoff; exposes state (Connecting/Connected/
  Disconnected/Error) and a raw line event. Thread-safe, `InvariantCulture`.
- **`DxClusterParser`** — parses standard DX cluster `DX de <spotter>: <freq> <call>
  <comment> <time>` lines into a normalized spot record; also `WWV`/`announce`/`To ALL`
  handling. Reusable/stateless.
- **`DxClusterManager`** — static/service; holds the list of connections, pushes parsed
  spots into `SpotManager2`, and exposes an aggregated, filterable list + change events
  for the form.
- **`DxClusterForm`** — modeless `Form`; `DataGridView` spot list, per-connection status,
  connect/disconnect, add/remove node, callsign, filters; owned by `console`; persists
  geometry.
- **`SpotManager2`** — add an optional **`source`** field to `smSpot` (`:85`) and an
  optional `source` parameter to `AddSpot` (`:473`) so the grid can filter by connection
  while TCI callers stay source-compatible. Keep a light tagging scheme (e.g. connection
  id / node name).

---

## 3. Persistence — recommendation

**Recommendation: store DX-cluster settings as a single JSON string in the existing
`Options` DB table** (key e.g. `dx_cluster`), via a small `DxClusterSettings` class
(`ToJson()` / `FromJson()`), and use `Common.SaveForm/RestoreForm` for window geometry.

Why:
- **Consistency.** `Options` is already the home for structured blobs — `discovered_radios`
  is stored the same way (`setup.cs:1818` add, `:2254-2256` read; persisted by
  `DB.SaveVarsDictionary("Options", ref a, true)` at `:1823` / read by
  `DB.GetVarsDictionary("Options")`). Cluster config (node list + ports + callsign +
  filters) is exactly this shape.
- **One DB, atomic.** No new table/file to migrate, back up, or keep in sync; survives the
  existing import/export and DB write path (`DB.WriteDB()` `:1825`).
- **Better than `SaveForm/RestoreForm` alone** (common.cs:359/416) — that only handles form
  geometry/state, not a variable-length node list. Use it only for the window rectangle.
- **Better than a dedicated XML** (the `MemoryList` style) — avoids a second persistence
  system for a small feature and keeps everything inspectable in one place.
- Serialize with the project's existing JSON handling; be culture-invariant.

---

## 4. Menu / UI placement

- New `ToolStripMenuItem` `dxClusterToolStripMenuItem`, `Text = "DX Cluster"`,
  `ForeColor = SystemColors.ControlLightLight` (match siblings).
- Insert into `menuStrip1.Items.AddRange` **between `wsjtXToolStripMenuItem` and
  `eSCToolStripMenuItem`** (`console.Designer.cs:4302`/`:4303`).
- Click handler toggles the single `DxClusterForm` instance (create-on-first-use, show /
  activate if already open), following the existing modeless-popup guard pattern.

---

## 5. Phased plan

- **Phase 1 (backend, no UI window):**
  - `DxClusterConnection.cs`, `DxClusterParser.cs`, `DxClusterManager.cs`.
  - `SpotManager2.smSpot.source` field + optional `AddSpot` `source` param.
  - Registration in `Thetis.csproj`.
  - Verification: unit-style parse checks / manual connect to a public node.
- **Phase 2 (popup window + menu):**
  - `DxClusterForm.cs` (+ designer) with `DataGridView`, connection controls.
  - `dxClusterToolStripMenuItem` in `console.Designer.cs` + handler in `console.cs`.
  - Wire form to `DxClusterManager` change events.
- **Phase 3 (polish):**
  - Filters (band/mode/de-dupe/window), spot lifetime integration, per-connection tags
    surfaced on the panadapter, settings persistence (§3), keepalive/reconnect tuning,
    skin/theme pass.

---

## 6. Phase 1 touch points

- New: `Project Files/Source/Console/DxClusterConnection.cs`,
  `Project Files/Source/Console/DxClusterParser.cs`,
  `Project Files/Source/Console/DxClusterManager.cs`.
- Edit: `Project Files/Source/Console/SpotManager2.cs` — `smSpot` (`:85`) field `source`;
  `AddSpot` (`:473`) optional `source` param; carry into the constructed `smSpot` (`:535`).
- Edit: `Project Files/Source/Console/Thetis.csproj` — `<Compile Include=...>` entries.
- Do **not** break TCI callers (`TCIServer.cs` `:4328/:4065/:3226/:4490`) — `source` is
  optional and defaults to existing behaviour.

## 7. Threading / culture notes

- Cluster reader threads run outside the UI thread; marshal to UI via
  `Control.BeginInvoke`/`Invoke` (existing pattern) and guard against culture issues —
  parse numbers/dates with `CultureInfo.InvariantCulture`
  (cf. the existing culture warning at `console.cs:626`).
- Respect the existing `_objLock` discipline in `SpotManager2` when tagging spots.

## 8. Open questions

- Default node list / callsign source on first run?
- Does the panadapter overlay need a per-source show/hide toggle, or is
  `_showTCISpots` (`display.cs:13661`) reused as-is in Phase 1?
- Exact `DataGridView` columns and filter set for Phase 2.

## UPDATE 2026-10-16 (node-grid cell tooltips)

Requirement: show the custom right-click hint tooltip when hovering the node
list data cells (Node and State columns), never on the column header, and
suppress the DataGridView auto-generated value tooltips entirely.

Key finding: `DataGridView.CellToolTipTextNeeded` only fires when the grid's
`DataSource` is set or `VirtualMode` is true. The node grid is populated
manually, so that handler never ran - only the auto value tooltips appeared.

Fix (`DxClusterForm.cs`):
- Removed the `CellToolTipTextNeeded` subscription + handler.
- `gridConns.ShowCellToolTips = false;` (kills all auto value tooltips).
- Added `private ToolTip connTip;` and drove it from
  `CellMouseEnter`/`CellMouseLeave` (+ `MouseLeave` to hide):
  Node/State cells show the right-click hint; Auto/Pana/Colour cells show
  their specific text. No header tooltip.
- Removed the earlier WinForms `nodeTip` on the whole grid (that caused a
  duplicate tooltip alongside the DGV value tooltip).

Build: Release x64 succeeded, 0 errors (exe 2026-10-16 22:44). Confirmed working.

## UPDATE 2026-10-16 (color spelling + header tooltips)

1) Spelling: changed `colour` -> `color` throughout the DX Cluster code
   (`DxClusterManager`: comment, `DefaultSpotColour` -> `DefaultSpotColor`,
   `pushSpot(..., Color color)`; `DxClusterConnection` comment; `DxClusterForm`
   comment, grid column `HeaderText = "Color"`, tooltip text, `DxColorTable`
   comment). No persisted keys changed (property is `ColorArgb`).

2) Tooltips now also show on column headers. `CellMouseEnter` was unreliable for
   header cells, so switched to `gridConns.MouseMove` + `HitTest`:
   - shows the per-column tip for both `DataGridViewHitTestType.Cell` and
     `DataGridViewHitTestType.ColumnHeader` targets;
   - tracks `_tipCol`/`_tipRow` so it only re-shows when the hover target
     changes; `MouseLeave` hides it.
   - `ShowCellToolTips = false` still suppresses the auto value tooltips.

Build: Release x64 succeeded, 0 errors (exe 2026-10-16 22:50).

---

## UPDATE 2026-10-17 (columns, flags, theme toggle)

### Committed + pushed (both repos, `origin/master`)
- `2860bf29` / `597f17b` — **login callsign from the Server List**; toolbar simplified to
  **Server List | Disconnect All | Spots on Panadapter | Clear Spots** (separators between);
  removed the inline node/port/call inputs + single Connect/Disconnect buttons.
- `3bf5d3d3` / `c9e730c` — **Country + Mode + Band columns**, columns renamed
  `Time (UTC)` / `Frequency (MHz)`, **Mode dropdown filter** (persisted), text filter now
  matches country/mode/band too. Mode is derived from the spot frequency via a band plan
  (`DxClusterParser.BandPlan` / `inferModeFromFrequency`) and drives both the panadapter
  DSP mode and double-click tune.
- `c32cf4ba` / `fb33d35` — **country flags** in the spots list. New public
  `SpotManager2.GetFlagImageForCallsign(string)` wraps the existing private
  `getFlagImageFromCallsign`; `DxClusterForm` paints the flag + country name by
  custom-painting (`gridSpots.CellPainting` → `onSpotCellPainting`) and caches images per
  spot id in `_spotFlagById`.
- Label `Filter:` -> `Text Filter:`.

### Uncommitted (builds clean in both repos; NOT yet committed/pushed)
- **Light/dark theme toggle** for the DX Cluster window:
  - `DxClusterTheme` refactored from `static readonly Color` fields into two `Palette`
    instances (dark + light) exposed through static properties that follow a live
    `IsDark` switch (`SetDark(bool)`); all existing `DxClusterTheme.Xxx` references were
    left untouched.
  - `DxClusterForm.ApplyTheme()` recolors form, both grids (`ApplyGridTheme`), tool/
    filter strips (`ApplyStripItems`), status bar, combos, text filter and node context
    menu; `onThemeToggle` persists via new `DxClusterSettings.DarkTheme` (default true).
  - Toolbar right side now has a `Theme:` label + a `btnTheme` button showing the theme it
    will switch TO (dark -> `Light`, light -> `Dark`). **Ordering gotcha:** `Alignment = Right`
    items flow outside-in, so the **button is added before the label** (button = rightmost).
  - The `Theme:` label + the label-reversal were the last edits; **rebuild + commit both**.

---

## SWL spot toggle — IMPLEMENTED 2026-10-17

Status: **done, built 0 errors** (Release x64 -> `Project Files\bin\x64\Release\Thetis.dll`).
The plan below was executed; see "Implementation summary (as built)" at the end of the section
for the exact deltas and resolved open questions.

### Locked decisions (confirmed with user 2026-10-17)
1. Toggle lives in the **DX Cluster window toolbar** (like the theme toggle).
2. When SWL is ON, telnet nodes **stay connected** but their spots are **hidden** from the
   list AND the panadapter.
3. Show only entries **on air now** (current UTC window), not the whole file.

### Data source — `http://www.eibispace.de/dx/sked-a26.csv` (Eibi HF broadcast schedule)
- ~528 KB, **9593 data lines**. Seasonal file: `a26` = summer 2026; the code changes each
  season, so make the URL a persisted setting with this default (or derive the season code).
- **Semicolon-delimited.** Line 1 is a header documenting the columns/widths:
  `kHz:75;Time(UTC):93;Days:59;ITU:49;Station:201;Lng:49;Target:62;Remarks:135;P:35;Start:60;Stop:60;`
- Data columns (index : meaning):
  - `0 kHz` — frequency in kHz (`16.3`, `49`)
  - `1 Time(UTC)` — `HHMM-HHMM` (`0000-2400` = full day, `1230-1315`)
  - `2 Days` — mostly empty (7686 rows); else weekly specs (`Mo-Fr`, `Su`, `Sa`, `Th`,
    `Tu,Fr`, `SaSu`, `Mo-Sa`, `156`), the literal `irr`, or a **date** like `24Oct`
    (seasonal/special, e.g. SAQ Grimeton). Plan: parse weekly specs; treat
    empty/`irr`/date-like/`Test` as "always" (do NOT drop).
  - `3 ITU` — country code (`IND`, `RUS`, `S`) -> show in Country column
  - `4 Station` — (`VTX1 Indian Navy`) -> use as the name/callsign column
  - `5 Lng` — language (`-CW`, `-TS`, or a language) -> into comment
  - `6 Target` — target area code (`SAs`, `NAO`) -> into comment
  - `7 Remarks` — 2-letter code -> into comment
  - `8 P` — power class (`1`..)
  - `9 Start` / `10 Stop` — dates (e.g. `0110`, `2410`) for seasonal entries; usually empty

### Reference notes for the build
- **AM DSP mode = number `14`** in `SpotManager2.SpotModeNumberToDSPMode` (-> `AM_USB`/`AM_LSB`).
- `SpotManager2.JsonSpotData` already has **`IsSWL`** (bool) and **`SWLSecondsToLive`**
  (long, `0` = never). They flow into `smSpot` and are honored by the lifetime sweep
  (`SpotManager2.cs:252-253`); SWL spots are excluded from the TCI `_maxNumber` cap and the
  TCI "Clear Spots" sweep (like externally-sourced spots). `display.cs:13516` skips the
  flash animation for `IsSWL`. => push SWL spots with `IsSWL = true` (and either
  `SWLSecondsToLive = 0`, or a value tied to the window end).
- External source markers: `DxClusterManager.SourcePrefix` = `"dxcluster:"`;
  `SpotManager2.HasSpotsFromPrefix` / `isExternallySourced` treat a non-empty `source` as
  external (drawn independent of the "Show TCI Spots" flag).

### Proposed implementation
- **New file `DXCluster\DxClusterSwl.cs`:**
  - `DxClusterSwlEntry` (FreqKhz, StartMin, StopMin, Days spec, ITU, Station, Language,
    Target, Remarks, raw Days text...).
  - `ParseCsv(string text)` -> entries (invariant culture; skip header; split on `;`).
  - `GetActive(DateTime utc)` -> entries whose time window contains now (handle `0000-2400`
    and midnight wrap) and whose weekly days match (best effort).
- **`DxClusterManager` additions:**
  - `const string SourcePrefixSwl = "swl:"`; separate `List<DxClusterSpot> _swlSpots` +
    cached `List<DxClusterSwlEntry> _swlEntries`.
  - `Settings.SwlMode` (bool, default false), `Settings.SwlUrl` (string, default the URL),
    maybe `Settings.SwlRefreshMinutes`.
  - `SetSwlMode(bool on)`: persist; on enable -> clear telnet sources from the panadapter +
    `LoadSwlAsync()`; on disable -> clear `swl:` source + re-push telnet cluster spots.
  - `LoadSwlAsync()`: background download (`WebClient`/`HttpClient`) -> `ParseCsv` -> cache
    -> `RebuildSwlSpots()`.
  - `RebuildSwlSpots()`: build `DxClusterSpot`s for `GetActive(now)`; Id from the shared
    counter; Node = `"SWL"`, NodeKey = `"swl"`, Callsign = Station, Country = ITU,
    Mode = `"AM"`, ModeNumber = `14`, Comment = time + days + target + language.
  - Periodic re-filter (windows change over the hour): coarse guard in the form's 500 ms
    timer, or a 60 s manager timer, calling `RebuildSwlSpots()` and pushing deltas.
  - `GetSpots()` returns `_swlSpots` when `SwlMode` else telnet `_spots`.
  - Suppress telnet panadapter push while `SwlMode` (in `addSpot`); re-push on disable.
  - `ClearSpots()` clears both lists + both sources.
- **`DxClusterForm` additions:**
  - Toolbar `btnSwl` (`CheckOnClick`, text `SWL`) reflecting `Settings.SwlMode` -> `SetSwlMode`.
  - Loading status text; `RefreshSpots()` shows SWL rows only while active.
  - Reuse columns: Callsign = station, Country = ITU, Mode = `AM`, Comment = time + days +
    target + language, Spotter blank, Node = `SWL`. Flags return none for a station name.
  - Double-click tune still works (frequency-based).

### Open questions — RESOLVED (2026-10-17)
- **SWL URL**: season-derived default + editable/persisted `Settings.SwlUrl` (blank = derived).
  Recomputed at load; custom value wins. Editable in the Server List dialog.
- **Hide-not-disconnect**: confirmed. Telnet nodes stay connected; their spots are buffered but
  not pushed to the panadapter and not listed while SWL is on.
- **Band column**: option (A) — added broadcast meter bands; `bandOf(spot)` picks ham vs
  broadcast ranges by `spot.IsSwl`.
- **De-dup**: none — same station on multiple frequencies shows as separate rows.
- **Refresh cadence**: manager-side 60 s rebuild timer + immediate rebuild on enable/load.
- **Color**: fixed default gold (`DxClusterManager.DefaultSpotColor`, `0xFFFFC800`).
- **Extra (added)**: flags + country name from an embedded ITU->ISO table, and a new
  **Language** column.

### Implementation summary (as built)
- **New `DXCluster\DxClusterSwl.cs`** (registered in `Thetis.csproj`):
  - `DxClusterSwlEntry` (FrequencyHz, StartMin/StopMin, TimeText, Days + `bool[7]` DayMask
    Mon..Sun, Itu, Station, LanguageCode, Target, Remarks, Persistence, Start/StopDate).
  - `ParseCsv` (semicolon, invariant, skips header, needs >=11 fields; `HHMM-HHMM` incl.
    `2400` -> 1440).
  - `GetActive(entries, utc)` + `TryGetWindowUtc` (midnight-wrap aware) + `EntryKey`
    (`freq|station|time|itu|target`).
  - `parseDays`: `Mo..Su`, `Mo-Fr` ranges, `Tu,Fr`, concatenated `SaSu`; any non-weekly spec
    (`irr`/`24Oct`/`test`/`156`/...) -> `null` mask = every day.
  - Embedded 97-entry `ITU=ISO|Name` country table -> `CountryIso`/`CountryName`
    (`ALS=US|Alaska`, `HWA=US|Hawaii`, `CLA=`/`UN=`/`XUU=` empty ISO).
  - `SetLanguageLegend(readmeText)` parses the EiBi README `I) Language codes` section
    (strips trailing `[...]`, `:`, ` (...)`) + `LanguageName(code)` (splits `F,E`).
  - `SeasonUrl(utc)`: Apr-Oct -> `a{yy}`, Nov-Dec -> `b{yy}`, Jan-Mar -> `b{yy-1}`;
    `DefaultUrl()`; `ReadmeUrl`.
- **`DxClusterParser.cs`** `DxClusterSpot`: added `CountryCode`, `Language`, `IsSwl`,
  `SwlSecondsToLive`, `Key`.
  - GOTCHA: `buildSwlSpot` must assign `Id = Interlocked.Increment(ref _nextSpotId)` (telnet
    spots get theirs in `addSpot`). Without it every SWL spot had `Id = 0`, so the form's
    `_spotRowById[0]` collapsed the whole list to a single visible row while the status still
    reported the real count.
- **`SpotManager2.cs`**: `public static Image GetFlagImageForAssetCode(string)` -> `getFlagImage`.
- **`DxClusterManager.cs`**: `SourcePrefixSwl="swl:"`; `_swlSpots`/`_swlEntries`/`_swlByKey`/
  `_swlPushedKeys`; 60 s `_swlTimer`; `_swlLoading`/`_swlStatus`/`_swlUrl`;
  `Settings.SwlMode`/`SwlUrl` (persist via existing JSON blob);
  `SetSwlMode(bool)` (enable: ResyncPanadapter + timer + `LoadSwlAsync`; disable: stop timer,
  drop swl spots, `ResyncPanadapter(MaxTelnetResyncSpots=200)`);
  `LoadSwlAsync()` (bg WebClient download of CSV + best-effort README, BOM-aware: UTF-8 BOM
  else Windows-1252 — **must** `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`
  on net10.0-windows or `GetEncoding(1252)` throws "No data is available for encoding 1252"
  (fallback to `Encoding.Latin1`; provider registered lazily in `windows1252()`); default-URL
  load falls back through neighbouring seasons `[0,-183,+183,-366]` days so a renamed/404 file
  on one season still loads); `RebuildSwlSpots()` (diff by key, add/drop, sort by freq, push new);
  `pushSwlSpot` (gold, `json.Flag=CountryCode`, `json.IsSWL=true`,
  `json.SWLSecondsToLive` = seconds to window end, min 30); `GetSpots()`/`SpotCount()` switch on
  `SwlMode`; `FindSpot` searches both; `ClearSpots` clears both; telnet push suppressed while
  `SwlMode`; `LoadSettings` resumes SWL if persisted on.
- **`DxClusterForm.cs`**: `btnSwl` toolbar toggle labelled "SWL Mode", **last in the left group**
  (after Clear Spots), `Tag="swl"`, green highlight when checked via `DxSwlRenderer`
  (`OnRenderButtonBackground` fills green + `OnRenderItemText` white). **GOTCHA:** do NOT set
  `ToolStrip.RenderMode` explicitly — WinForms throws `NotSupportedException` on `= Custom`,
  and `= ManagerRenderMode` makes the per-strip `Renderer` be ignored. Setting the `Renderer`
  property alone switches it to Custom; the `RenderMode` lines were removed from both strips
  (they had silently disabled `DxColorTable` before);
  **column visibility is mode-dependent** (`updateSwlColumns`, called each `RefreshSpots`,
  guarded by nullable `_swlColumnsHidden` so it only touches the grid on change): SWL mode
  hides **Spotter + Node**, shows **Language**; telnet mode shows Spotter + Node, hides
  Language;
  Toolbar: **SWL Mode** is last in the left group; both **"Spots on Panadapter"** and
  **"SWL Mode"** (checked) are painted green (white text) via the custom `DxToggleRenderer`
  (which uses a generic `"green-toggle"` tag and requires the per-strip Renderer to be
  set directly — **do not** set `ToolStrip.RenderMode = Custom/Manager`; setting `Renderer`
  alone switches to Custom). Removing that explicit `RenderMode` also restored the intended
  `DxColorTable` application (previously it was forcing ManagerRenderMode and disabling
  per-strip color tables).
  **Language** column inserted after Country (COL_SPOT_*: TIME 0, MHZ 1, BAND 2, CALL 3,
  COUNTRY 4, LANGUAGE 5, MODE 6, SPOTTER 7, COMMENT 8, NODE 9); `hamBandOf`/`broadcastBandOf`
  with `bandOf(spot)`; combo gets broadcast bands + `AM`; `buildSpotRow` sets Language and picks
  `GetFlagImageForAssetCode(CountryCode)` for SWL; text filter includes Language; status line
  shows SWL count/loading/url; `PrefillCallsign` restores the toggle; `onToggleSwl` clears rows
  and refreshes.
- **`display.cs`**: `shouldDrawSpots` also `HasSpotsFromPrefix("swl:")`; `isClusterSpot` also
  matches `"swl:"`.
- **`DxClusterServersForm.cs`**: editable **SWL URL** field + "Default" button (prefilled from
  `Settings.SwlUrl`; blank = season-derived); `applySwlUrl` persists and reloads if SWL is on.
- Build: MSBuild (VS18) `/p:Configuration=Release /p:Platform=x64` -> 0 errors.

> NOTE: SWL content uses the embedded ITU table for country/flag and the runtime README legend
> for language names; if the README cannot be fetched the raw language code is shown.

---
