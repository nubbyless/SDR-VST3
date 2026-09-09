# Thetis Scanner Port — Session Handoff / Plan

> Purpose: resume-safe plan for porting the KE9NS PowerSDR **Scanner** feature into the
> **thetisvst** repo (Thetis fork). Research complete; thetis integration points VERIFIED.
> `scan.cs`/`scan.Designer.cs`/`scan.resx` ported into the Thetis Console dir as
> `namespace Thetis`. All code + project wiring done and **the build passes**. Next:
> manual smoke test (Rx1 + Rx2) per section 15.

- Created: 2026-09-08
- Repo: `C:\Users\W4YNY\Documents\thetisvst` (git). Branch: check `git status` first.
- Thetis work dir: `C:\Users\W4YNY\Documents\thetisvst\Project Files\Source\Console`
- KE9NS source (local clone): `C:\Users\W4YNY\AppData\Local\Temp\opencode\PowerSDR-KE9NS`
  (fresh clone; DO NOT re-clone unless deleted)

---

## 1. Objective

Port the KE9NS scanner (single WinForms form `ScanControl`, ~4098 lines of logic +
~1065 lines designer) into Thetis as Native Thetis code (`namespace Thetis`), adapting
all PowerSDR-specific APIs to Thetis equivalents. Add a menu item to open it, feed it
squelch/signal data from the console, and integrate the bandstack (memory-register)
scan mode with Thetis's `BandStackManager`.

## 2. Current status

- Both codebases fully investigated; memory model verified identical (no memory changes needed).
- `scan.cs` copied to `thetisvst\Project Files\Source\Console\scan.cs`, namespace → `Thetis`
  (line 35). `scan.Designer.cs` + `scan.resx` also copied (still KE9NS namespace/content).
- Todos (10) exist in the session; superseded by checklist in section 17.
- **Integration verifications DONE (2026-09-08)** — see section 2a. No logic edits yet.

## 2a. Integration verification results (2026-09-08)

All checked against the actual files; conflicts with earlier assumptions are marked.

- **MemoryRecord property order is IDENTICAL** in KE9NS and Thetis (both start
  `Group, RXFreq, Name, DSPMode, StartDate, Duration, Recording, Repeating, Repeatingm,
  Comments, Scan, ... RXFilter, RXFilterLow, RXFilterHigh, AGCMode, ...`). →
  `dataGridView2` auto-column indices are UNCHANGED from KE9NS:
  `[0]=Group, [1]=RXFreq, [2]=Name, [3]=DSPMode, [10]="Scan", [20]=RXFilter`.
  **No column remapping needed.** (Resolves §16 Q4.)
- **Thetis SetupForm has NO `chkBoxAutoFocus`.** The scan.cs:3932 autofocus line
  (`console.setupForm.chkBoxAutoFocus.Checked`) must be DROPPED (no equivalent).
  SetupForm access pattern in Thetis = `console.IsSetupFormNull` (console.cs:1170) +
  `console.SetupForm` property.
- `console.MemoryList` exists: console.cs:161 `public MemoryList MemoryList { get; private set; }`.
- `Common.RestoreForm(Form, string, bool)` exists: common.cs:416; `SaveForm` at 359.
- `memoryForm` field: console.cs:160 `public MemoryForm memoryForm;` → add
  `public ScanControl ScanForm;` right next to it. (Resolves §16 Q5: lazy-create in menu
  handler, NOT the ctor.)
- Menu structure verified: `memoryToolStripMenuItem` declared console.Designer.cs:348,
  instantiated 773, wired into `menuStrip1.Items.AddRange` 4287-4308 (after
  `this.memoryToolStripMenuItem,`), config block 4339-4344 (`.ForeColor=ControlLightLight;
  .Name=...; ApplyResources; .Click += memoryToolStripMenuItem_Click;`). Handler pattern:
  console.cs:41449-41465.
- Shutdown: console.cs:28664-28671 `if (memoryForm != null) memoryForm.Close();` →
  add `if (ScanForm != null && !ScanForm.IsDisposed) ScanForm.Dispose();` after it.
- `SetBand` 6-arg (console.cs:5884) signature confirmed:
  `(string mode, string filter, double freq, bool CTUN, int zoomFactor, double centerFreq)`.
  **No 3-arg overload in Thetis** → the adapter (see §7) calls with
  `CTUN=false, zoomFactor=ptbDisplayZoom.Value, centerFreq=0`.
- **No `SetBand2`, no `TXFreq2`, no `VFOBCenter` in Thetis.** Port SetBand2 per §7/§8a
  (use `RX2DSPMode` 17784, `RX2Filter` 17948, `VFOBFreq` 18414, `PanCentreRX2` 19636).
  `UpdateRX2Filters(low,high)` **does exist** (console.cs:7659). `RecallMemoryB` RX2 TX path:
  port KE9NS body using `TXFreq` (Thetis has no TXFreq2 — single shared TX freq), drop the
  VFOB-pan/SpotForm extras. (Resolves §16 Q3.)
- Band change hook: `public delegate void BandChanged(int rx, Band oldBand, Band newBand)`
  console.cs:45938; `private event BandChanged BandChangeHandlers;` 46102; set via
  `SetBandChangeHanders` 46109; `OnBandChangeHandler` **46773** (early-returns if
  `m_bSetBandRunning` or `!BandStackManager.Ready` or `rx != 1`). → Call
  `ScanForm?.UpdateBandScanRange()` there for rx==1. (Also call in `ScanControl_Load`.)
- `Band` enum: enums.cs:273 — GEN, B160M..B11M, WWV, VHF0-13, BLMF, B120M..B11M, LAST.
  Matches KE9NS updateindex switch cases. Arrays keyed by `(int)Band`.
- `public bool initializing` console.cs:293 (exists, as KE9NS).
- Squelch fields: `ptbSquelch` PrettyTrackBar (Designer 7854-7870, -160..0), `ptbRX2Squelch`
  (Designer 6066-6082), `sql_data` console.cs:25554, `rx2_sql_data` 25580.
- KE9NS menu handler pattern `ScanMenuItem_Click` lazy-creates: console.cs:79706-79714
  (`if (ScanForm==null||ScanForm.IsDisposed) ScanForm = new ScanControl(this);
  ScanForm.Show(); ScanForm.Focus(); WindowState=Normal;`).
- KE9NS never initializes the low/high boxes on load; freq scan uses whole-band range until
  the box is edited. Improvement in port: `UpdateBandScanRange()` sets `freq_Low/freq_High`
  (+ box text) on band change and on form load.

## 3. Repos / paths

| Item | Path |
|---|---|
| KE9NS scanner | `...\PowerSDR-KE9NS\Console\scan.cs` (4098 lines, `namespace PowerSDR`) |
| KE9NS designer | `...\PowerSDR-KE9NS\Console\scan.Designer.cs` (1065 lines) |
| KE9NS resources | `...\PowerSDR-KE9NS\Console\scan.resx` (1 icon `$this.Icon` + tooltips; small) |
| KE9NS console integration | `...\PowerSDR-KE9NS\Console\console.cs` (lines: 3308, 3921-22, 4408, 51746+, 59101-59123, 74153-74174, 79706-79714) |
| Thetis console | `thetisvst\Project Files\Source\Console\console.cs` |
| Thetis bandstack | `...\clsBandStackManager.cs` |
| Thetis memory | `...\Memory\` (MemoryForm.cs, MemoryRecord.cs, MemoryList.cs) |
| Thetis project | `...\Thetis.csproj` (SDK-style, `net10.0-windows`, `EnableDefaultCompileItems=false`) |
| Solution | `...\Thetis_VS2026.sln` |

## 4. Confirmed decisions (user-approved)

1. **Adapt scanner to Thetis**, do NOT rewrite Thetis to old PowerSDR bandstack model.
2. **Drop SWL scan** (textBox1 `SWL` list, SCAN6/btnGroupMemory1 handling, all
   `SpotControl.*` / `console.VFOAFreq`-to-SWL references). Thetis has no SWL subsystem.
3. Memory model: no changes — Thetis `Memory\` already carries KE9NS additions
   (`Scan`, `StartDate`, `Duration`, `Repeating`, `Repeatingm`, `Recording`,
   `ScheduleOn` columns; see ke9ns comments in MemoryForm.cs:232-234).
4. Keep scan modes: **SCAN1** group-memory, **SCAN2** bandstack (adapted to
   `BandStackManager`), **SCAN3** custom text list, **SCAN4** low-high freq sweep
   (`button5`/SCANNER).
5. **Keep RX2 / VFO-B scanning** (user-confirmed `2026-09-08`): `ScanVFOB` middle-click
   mode, `RecallMemoryB`, `SetBand2`, `SQL2/SIG2/ScanStop2` feed, RX2 auto-on when
   recalling to VFO-B. Adapted to Thetis RX2 properties (section 8a).

## 5. Scope

### Keep (port)
- Memory scan (SCAN1), bandstack scan (SCAN2), custom list scan (SCAN3), freq sweep (SCAN4).
- Left-click memory row → VFOA recall (via `console.RecallMemory` / `SetBand`).
- Middle-click memory row / btnGroupMemory → **VFO-B (RX2)** recall (adapted, section 8a).
- Right-click memory row → toggle memory Channel-Scan (Scan) flag.
- Squelch-break pause + wait modes, pause button, speed/gap/pause controls, group combo.
- The scan form's own display grid (currFBox), dataGridView2 backing table, resx tooltips+icon.

### Deferred / later
- Band-edge (`SLowScan/SHighScan`) persistence to disk (v1 keeps in memory, section 12 note).

### Drop from port (decided, ALL user-confirmed)
- SWL list & SWL scan (SCAN6) — decision 2.
- F1 help popup integration (`console.helpboxForm`, `helpbox.SWRScanner`, `Console.HELPSWR`,
  `Console.HELPX`). Thetis has no equivalent helpbox; tooltips already ship via resx.
- SWR plot scanner (`button2_Click`, `checkBoxSWR`, `numericSWRTest`, `SWR_TESTRUN`,
  `AppDataPath\SWR_PLOTS`) — Flex-hardware-specific (10 W TX tune loop). Confirmed dropped by
  user on 2026-09-08.

### Defer (recorded, optional)
- Band-edge (`SLowScan/SHighScan`) persistence. KE9NS persisted these in the legacy DB;
  Thetis v1 keeps them in `ScanControl` memory only (see section 12 note).

## 6. Architecture map (source of truth)

- `ScanControl : System.Windows.Forms.Form` (namespace `PowerSDR` → becomes `Thetis`).
- Scanner runs on background threads (`new Thread(SCAN1/SCAN2/SCAN3/SCANNER)`,
  `IsBackground=true`); **all UI updates already go through `UpdateText()`/`UpdateText3()`**
  which use BeginInvoke — preserve this on port.
- Console feeds scan state every ~100 ms from its meter loops:
  - `ScanControl.SQL = ptbSquelch.Value` (threshold)
  - `ScanControl.SIG = sql_data` (signal dBm)
  - `ScanControl.ScanStop = 1` when squelch breaks (FM `GetFMSquelchBreak` in KE9NS;
    Thetis equivalent = `sql_data > ptbSquelch.Value`, gated to FM-like modes).
- Band low/high edges for SCAN4 live in KE9NS `console.SLowScan/SHighScan` (strings,
  indexed by `(int)RX1Band`) + `console.scanUpdate`. **Thetis does not have these** →
  move them into `ScanControl` as internal storage, refresh boxes on `BandChangeHandlers`.

## 7. KE9NS dependency → Thetis adaptation table (CRITICAL)

| KE9NS usage (scan.cs) | Thetis replacement | Notes / verified refs |
|---|---|---|
| `console.SetBand(mode, filter, freq)` (3-arg) | Add overload in Thetis console: `public void SetBand(string mode, string filter, double freq)` wrapping 6-arg | Existing 6-arg: console.cs:5884 `SetBand(string mode, string filter, double freq, bool CTUN, int zoomFactor, double centerFreq)`. Adapter body → `SetBand(mode, filter, freq, false, ptbDisplayZoom.Value, 0);` (CTUN off + current zoom + no recentre — verified fields). |
| `console.SetBand2(mode, filter, freq)` (RX2) | **PORT** — add Thetis `public void SetBand2(string mode, string filter, double freq)` | Port KE9NS body (console.cs:11080-11121): trim trailing `@` lockout suffix, `RX2DSPMode = Enum.Parse<DSPMode>(mode,true)`, `RX2Filter = Enum.Parse<Filter>(filter,true)` (skip for DRM/SPEC), `VFOBFreq = freq`, replace `VFOBCenter()` with Thetis **`PanCentreRX2()`** (console.cs:19636). Drop `tempVFOBFreqIF`/SpotForm bits (those are PowerSDR-only). |
| `console.RecallMemory(record)` | Exists, unchanged | Thetis console.cs:41364 (`RecallMemory`), plus `memoryToolStripMenuItem_Click`:41449 pattern. |
| `console.RecallMemoryB(record)` | **PORT** — add Thetis `public void RecallMemoryB(MemoryRecord record)` | Port KE9NS body (console.cs:77152-77251) to Thetis RX2 props: `RX2DSPMode = record.DSPMode; VFOBFreq = record.RXFreq;` FM block (mirror Thetis RecallMemory:41371-41378: FMTXMode, FMTXOffsetMHz, CTCSSOn, CTCSSFreq, FMDeviation_Hz), else `TXFilter`/`RX2Filter` + `UpdateRX2Filters(record.RXFilterLow, record.RXFilterHigh)` for VAR1/VAR2 (verified: `UpdateRX2Filters` console.cs:7659; **no TXFreq2 → use `TXFreq`**), then `RX2AGCMode`, `RF`. Drop PowerSDR `MessageBox` validation blocks. |
| `DB.GetBandStack(band, index, ...)` for RX2 | `BandStackManager.GetFilter(console.RX2Band)` → `EntryByIndex(xxx)` → `console.SetBand2(...)` | Same as RX1 path (line 106) but using `RX2Band` (Thetis console.cs:17460). Applies to middle-click bandstack recall (scan.cs:3669-3685) and any RX2 SCAN2 path. |
| RX2 auto-on (`setupForm.chkRX2AutoOn`, `chkVAC2Enable`, `console.chkRX2`) | **ADAPT** — no `chkRX2AutoOn` in Thetis | Replace KE9NS blocks (scan.cs:3551-3558, 3587-3601, 3611-3618, 3676-3683) with: `if (console.chkRX2 != null && console.chkRX2.Visible && !console.chkRX2.Checked) console.chkRX2.Checked = true;`. VAC2 auto-enable NOT ported (user decision, §16 item 2). |
| `DB.GetBandStack(band, index, out mode, out filter, out freq)` | `BandStackManager.GetFilter(RX1Band)` → `bandStackFilter.EntryByIndex(index)` | Thetis clsBandStackManager.cs: `class BandStackManager` (744), `GetFilter` static; `BandStackFilter` (201) with `EntryByIndex(int)` (465), `NumberOfEntries` (306). See section 10. |
| `console.band_stacks[nnn]` (entry count) | `bandStackFilter.NumberOfEntries` | |
| `console.iii`, `console.updateindex()`, `yyy` lock-check | Not needed; bandstack position tracked by Thetis (LastVisited/Current). Use `Current()`/`EntryByIndex`. | After applying an entry via `SetBand(bse...)`, Thetis auto-maintains the current position (console.cs:46410 `handleBSFChange`, 46420 `updateLastVisited`). |
| `console.UpdateWaterfallLevelValues()` | Exists | Thetis console.cs:8913 `public void UpdateWaterfallLevelValues()`. |
| `DttSP.GetFMSquelchBreak(0,0,&sq)` (+ reset timers) | `int ScanStop = (sql_data > ptbSquelch.Value) ? 1 : 0` fed from `UpdateSQL()` | See section 11. Thetis `ptbSquelch` = `PrettyTrackBar`, -160..0 (console.Designer.cs:7854-7870). `sql_data` field console.cs:25554. Feed **unconditionally** (KE9NS gated to FM-squelch only). |
| `ScanControl.SQL/SIG/ScanStop` feed sites (RX1) | Feed from Thetis `UpdateSQL()` (RX1) | Null-guard `console.ScanForm` (field, §2a). `ScanControl.SQL=(int)ptbSquelch.Value; ScanControl.SIG=(int)sql_data; ScanControl.ScanStop=(sql_data>ptbSquelch.Value)?1:0;` |
| `ScanControl.SQL2/SIG2/ScanStop2` feed sites (RX2) | Feed from Thetis `UpdateRX2SQL()` (console.cs:25572) | `.SQL2 = (int)ptbRX2Squelch.Value; .SIG2 = (int)rx2_sql_data; .ScanStop2 = (rx2_sql_data > ptbRX2Squelch.Value) ? 1 : 0;` — `rx2_sql_data` console.cs:25580, `ptbRX2Squelch` = PrettyTrackBar -160..0 (console.Designer.cs:6066-6082). `ScanVFOB` gates in scan.cs switch between ScanStop/ScanStop2 (scan.cs:1587, 1752). |
| `console.SLowScan/SHighScan` + `console.scanUpdate` | Move into `ScanControl`: local `string[] SLowScan/SHighScan` keyed by `(int)Band` + new `public void UpdateBandScanRange()` | Band-edge table + overrides live inside ScanControl. Called from Thetis `OnBandChangeHandler` (console.cs:46773, rx==1) and `ScanControl_Load`. `button_reset_Click` uses `console.RX1Band` (console.cs:17303). |
| `console.tempVFOAFreq` | Not needed — drop | Thetis `VFOAFreq` setter handles CTUN internally. |
| `console.SWR_TESTRUN`, `HelpSWR`, `AppDataPath + "SWR_PLOTS"` | DROP (SWR) | |
| `SpotControl.SWL_*` arrays, `swl_index` | DROP (SWL) | |
| `MemoryRecord` (Memory namespace) | Same type in Thetis | `new MemoryRecord((MemoryRecord)comboMemGroupName.SelectedItem)`. |
| TS controls: `ComboBoxTS, CheckBoxTS, NumericUpDownTS, GroupBoxTS, LabelTS, PrettyTrackBar` | Present in Thetis under `namespace System.Windows.Forms` | Verified `comboboxts.cs:31 namespace System.Windows.Forms`; `ptbSquelch = new Thetis.PrettyTrackBar()` (console.Designer.cs:1170). Designer file ports nearly verbatim. |
| `dataGridView2[i,j]` column indices (0=name,1=RXFREQ,3=DSPMODE,20=filter, `["Scan"]`) | **Keep as-is, VERIFIED** | MemoryRecord property order identical to KE9NS (§2a) → auto-column order identical. `[0]=Group,[1]=RXFreq,[2]=Name,[3]=DSPMode,[10]="Scan",[20]=RXFilter.` No changes. |

## 8. Port files plan

Create in `thetisvst\Project Files\Source\Console\`:
1. `scan.cs` — copy from KE9NS, then:
   - `namespace PowerSDR` → `namespace Thetis`.
   - Remove SWL: `textBox1_TextChanged` body (scan.cs:4014-4040) → clear-scan only,
     remove SWL branches in `currFBox_MouseUp` (3400-3410, 3530-3538, 3601-3621,
     3751-3759, 3810-3820), remove `swl_index/swl_count/SWL load` code.
   - Remove SWR: `button2_Click`, `checkBoxSWR`, `numericSWRTest_ValueChanged`,
     `button2_MouseEnter/Leave`, F1 help block (3878-3926, uses `console.helpboxForm`).
   - Remove `console.HELPSWR`/`HELPX` + `ScanControl_MouseEnter/Leave` F1 refs.
   - **Keep RX2** paths (ScanVFOB, RecallMemoryB, SetBand2, SQL2/SIG2/ScanStop2,
     middle-click handlers 3487-3703, auto-on blocks) — adapt per section 8a.
   - Bandstack (SCAN2): replace `DB.GetBandStack(band_list[nnn], xxx,...)` +
     `console.band_stacks[nnn]` + `console.iii`/`updateindex()` with
     `BandStackManager.GetFilter(console.RX1Band)` + `EntryByIndex(xxx)` +
     `NumberOfEntries` (see section 10). RX2 bandstack variant uses `RX2Band` +
     `SetBand2` (section 8a).
   - `SetBand(mode, filter, freq)` calls stay literally the same (new Thetis overload).
   - `SLowScan/SHighScan` → local arrays; keep `button_reset_Click` semantics.
2. `scan.Designer.cs` — copy verbatim from KE9NS; change `namespace PowerSDR` →
   `namespace Thetis`. NOTE: KE9NS designer ends with `public System.Windows.Forms.CheckBoxTS chkIDdBM; chkIDSIG;` (lines 1060-1061) — those two fields are referenced by `InitializeComponent` but I did not see a declaration at top (only at bottom); keep both sets as-is (it compiles in KE9NS). If dropping SWR controls, remove `button2`, `checkBoxSWR`, `numericSWRTest` + their init lines.
3. `scan.resx` — copy verbatim (icon + tooltips). Ensure `<EmbeddedResource>` registered.
4. Edit `console.cs`:
   - Add fields: `public ScanControl ScanForm;` + `public ScanControl s_scan => ScanForm;`(or feed via static). KE9NS pattern: console.cs:991 (field), :3921-22 (ctor create), :3308 (`ScanControl.console = this`), :4408 (dispose). Thetis: constructor creates forms around console.cs:3921 — find Thetis form instantiation block (near memoryForm creation) and mirror.
   - Add `public void SetBand(string mode, string filter, double freq)` overload (adapter).
   - Add `public void SetBand2(string mode, string filter, double freq)` + `public void RecallMemoryB(MemoryRecord record)` (RX2, section 8a).
   - Feed `UpdateSQL()` (console.cs:25555): after `sql_data = num;` add ScanControl feed (section 11).
   - Feed `UpdateRX2SQL()` (console.cs:25572): after `rx2_sql_data = num;` add RX2 feed (section 11).
   - Menu: add `scanToolStripMenuItem` next to `memoryToolStripMenuItem`
     (console.Designer.cs:348/773/4289/4339-4344) → handler `scanToolStripMenuItem_Click` modeled on `memoryToolStripMenuItem_Click` (console.cs:41449-41465) creating/showing/focusing `ScanForm`.
   - Disposal: console.cs:28666 area (`memoryForm.Close()`) → add `ScanForm.Close()`.
   - Add `using System.Threading;` if not present for `Thread`. (Check file header usings.)
5. Edit `Thetis.csproj` — add (SDK-style, explicit items):
   ```xml
   <Compile Include="scan.cs" />
   <Compile Include="scan.Designer.cs"><DependentUpon>scan.cs</DependentUpon></Compile>
   <EmbeddedResource Include="scan.resx"><DependentUpon>scan.cs</DependentUpon></EmbeddedResource>
   ```
   Match the exact `<ItemGroup>` style used for other forms (memoryForm.cs etc.).

## 8a. RX2 / VFO-B scanning adaptation (in scope)

Thetis RX2 state properties (all verified in console.cs):
`RX2Band` (17460), `RX2DSPMode` (17784), `RX2Filter` (17948), `VFOBFreq` (18414),
`RX2AGCMode` (19805), `ClickTuneRX2Display` (10879), `CentreRX2Frequency` (10802),
`RX2Enabled` (38040), `chkRX2` checkbox (console.Designer.cs), console-level
`rx2_enabled` field (audio.cs:280). Squelch slider `ptbRX2Squelch` (-160..0),
`rx2_sql_data` (console.cs:25580).

What to port from KE9NS into `scan.cs` (KEEP, don't strip):
- Field `bool ScanVFOB` (scan.cs:941) + `btnGroupMemory_MouseDown` middle-click toggle (4042-4094).
- Static `ScanStop2/SQL2/SIG2` (142, 1411, 1414) and all `ScanVFOB`-branched logic in
  SCAN1 (1435-1830), plus `ScanStop2 = 0` reset (1516/1756) and `ScanStop2 = 1` at
  SCAN1 end (1830).
- Middle-click `currFBox_MouseUp` VFOB branch (3487-3703): `RecallMemoryB` for memory
  rows (3548), `SetBand2` for mem-match (3584) and bandstack rows (3673), auto-RX2-on
  blocks (3551-3558, 3587-3601, 3611-3618, 3676-3683).

New console.cs additions (RX2):
- `public void SetBand2(string mode, string filter, double freq)` — port of KE9NS
  console.cs:11080; body uses Thetis `RX2DSPMode`/`RX2Filter`/`VFOBFreq`; replace
  `VFOBCenter()` with `PanCentreRX2()` (console.cs:19636); drop `tempVFOBFreqIF`,
  `setupForm.chkVFOLargeWindow`, SpotForm cross-talk.
- `public void RecallMemoryB(MemoryRecord record)` — port of KE9NS console.cs:77152
  body; Thetis FM props already used by RX1 `RecallMemory` (41371-41378); for non-FM
  use `TXFreq` (no `TXFreq2` in Thetis — VERIFIED) + `RX2Filter` (+ `UpdateRX2Filters`
  for VAR1/VAR2, console.cs:7659 — VERIFIED exists); then `RX2AGCMode`, `RF`.
- RX2 auto-on adapter: `if (console.chkRX2 != null && console.chkRX2.Visible && !console.chkRX2.Checked) console.chkRX2.Checked = true;`
  (VAC2 auto-enable deliberately NOT ported — user decision, section 16 item 2).

RX2 bandstack scan (SCAN2 on VFO-B): `BandStackFilter bsf2 =
BandStackManager.GetFilter(console.RX2Band);` then `bsf2.EntryByIndex(i)` →
`console.SetBand2(bse.Mode.ToString(), bse.Filter.ToString(), bse.Frequency);` +
`console.UpdateWaterfallLevelValues();`.

## 9. Console integration checklist (Thetis, current line numbers)

| Where | Edit |
|---|---|
| console.cs field area (~:160, near `public MemoryForm memoryForm;`) | `public ScanControl ScanForm;` |
| console.cs form-init block (~:3921-22 in KE9NS; find Thetis equiv, ~:41358 memoryForm pattern is in handler — the ctor block is near :3920s) | `ScanForm = new ScanControl(this);` |
| Constructor, after `memoryForm` init | `ScanControl.console = this;` (or ctor param — ScanControl ctor currently takes `Console console`; KE9NS used `ScanControl(Console console)` — reuse) |
| Dispose (Thetis ~:28666) | `if (ScanForm != null) ScanForm.Close();` |
| `UpdateSQL()` (console.cs:25555) | inside loop, after `sql_data = num;`: feed scan |
| `UpdateRX2SQL()` (console.cs:25572) | inside loop, after `rx2_sql_data = num;`: feed RX2 scan |
| Menu designer + handler | add `scanToolStripMenuItem` + `scanToolStripMenuItem_Click` (create/show/focus, dispose-safe, Invoke-safe like :41434-41465) |
| `SetBand` overload | add 3-arg adapter next to :5884 |
| `SetBand2` + `RecallMemoryB` | add adjacent to `RecallMemory` :41364 region (see section 8a) |

## 10. Bandstack (SCAN2) adaptation detail

Thetis API (`clsBandStackManager.cs`):
```csharp
BandStackFilter bsf = BandStackManager.GetFilter(console.RX1Band); // static
int n = bsf.NumberOfEntries;                 // == KE9NS console.band_stacks[nnn]
BandStackEntry bse = bsf.EntryByIndex(i);    // random access by index
BandStackEntry cur = bsf.Current();          // tracked position
BandStackEntry nxt = bsf.Next();
bse.Mode / bse.Filter / bse.Frequency / bse.CTUNEnabled / bse.ZoomSlider / bse.CentreFrequency
```
Apply an entry (same as Thetis console.cs:46766):
```csharp
console.SetBand(bse.Mode.ToString(), bse.Filter.ToString(), bse.Frequency,
                bse.CTUNEnabled, bse.ZoomSlider, bse.CentreFrequency);
console.UpdateWaterfallLevelValues();
```
Thetis will update its own "current/lastVisited" position via `handleBSFChange`
(console.cs:46410) — so no manual `updateindex()` needed.

## 11. Squelch / SIG feed design (Thetis)

RX1 — in `UpdateSQL()` (console.cs:25555), after `sql_data = num;` add (null-safe):
```csharp
var sc = s_scan;                    // ScanControl instance holder
if (sc != null && !sc.IsDisposed)
{
    ScanControl.SQL = (int)ptbSquelch.Value;
    ScanControl.SIG = (int)sql_data;
    ScanControl.ScanStop = (sql_data > ptbSquelch.Value) ? 1 : 0;
}
```
RX2 — in `UpdateRX2SQL()` (console.cs:25572), after `rx2_sql_data = num;` add (null-safe):
```csharp
var sc = s_scan;
if (sc != null && !sc.IsDisposed)
{
    ScanControl.SQL2 = (int)ptbRX2Squelch.Value;
    ScanControl.SIG2 = (int)rx2_sql_data;
    ScanControl.ScanStop2 = (rx2_sql_data > ptbRX2Squelch.Value) ? 1 : 0;
}
```
Notes:
- Keep `ScanStop` one-shot semantics like KE9NS (it reads `ScanStop == 1` then continues;
  KE9NS resets via `ScanStop = 0` in scan threads — scan.cs does that already, e.g. line 3113, 3294, 3243).
- Thetis `ptbSquelch` Min/Max = -160/0 (matches `sql_data` range; KE9NS used same semantics with `ptbSquelch.Value`).
- Evaluation runs every 100 ms (task delay) — same cadence as KE9NS meter loop.
- FM squelch: Thetis uses `rx1_fm_squelch_threshold_scroll` internally (console.cs:48398);
  `sql_data > ptbSquelch.Value` approximates the KE9NS `GetFMSquelchBreak`. Acceptable v1.
  (Optional refinement: gate ScanStop to `RX1DSPMode` FM-like modes.)

## 12. Scan modes kept (behavior reference)

1. **SCAN1** group-memory: `btnGroupMemory_MouseDown` (scan.cs:4042) → thread `SCAN1`
   (uses `currentMemIndex/memIndex/memcount`, `comboMemGroupName.SelectedIndex`,
   `console.RecallMemory(recordToRestore)`, `console.SetBand(mode,filter,freq)`).
   When `ScanVFOB` (middle-click) → `console.RecallMemoryB` + SQL2/SIG2/ScanStop2
   squelch gate (scan.cs:1435-1830).
2. **SCAN2** bandstack: thread `SCAN2` — port per section 10 (replace DB/Bandstack-index APIs).
3. **SCAN3** custom list: `btnCustomList_Click` (scan.cs:2873) reads text file (Name,MHz,Mode,Filter)
   into `customMem/customFilter/customMode/custSize`, `SCAN3()` (scan.cs:3051) drives.
4. **SCAN4** freq sweep: `button5_Click` (scan.cs:2474, SCANNER logic ~2500-2690) — sweeps
   `lowFBox`→`highFBox` per `udstepBox` kHz, uses `SLowScan/SHighScan [band]`, stops on squelch break.
- Pause handling: `pausebtn_Click` (3278), `ScanPause` state machine inside each scan loop;
  `ST2`=speed timer, `ST3`=pause-length timer. Port unchanged.
- `UpdateText()/UpdateText3()` update currFBox; `linelength = 65`? — verify constant & `linelength`
  checks (e.g. scan.cs uses `band_index` at 3156/3177 — verify variable is populated in Thetis port).

## 13. Menu integration

- Designer (console.Designer.cs): add `scanToolStripMenuItem` beside `memoryToolStripMenuItem`
  (see :348, :773, :4289 for the menu-item-array add, :4339 for init, add `.Click += scanToolStripMenuItem_Click`).
- Handler (copy `memoryToolStripMenuItem_Click` guard pattern console.cs:41449-41465):
```csharp
private void scanToolStripMenuItem_Click(object sender, EventArgs e)
{
    if (ScanForm == null || ScanForm.IsDisposed) ScanForm = new ScanControl(this);
    if (ScanForm.InvokeRequired) ScanForm.Invoke(new MethodInvoker(() => { ScanForm.Show(); ScanForm.Focus(); }));
    else { ScanForm.Show(); ScanForm.Focus(); }
}
```

## 14. Build

- MSBuild: `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- `dotnet` on PATH (likely `dotnet build` works too; sln = `Thetis_VS2026.sln`).
- Prefer one command:
  `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "Project Files\Source\Thetis_VS2026.sln" /m /v:m`
  (run from thetisvst root; x64 only).

## 15. Verification plan

- Build clean (M2/M1 warning level; treat errors as blocking).
- Manual: open Scanner from new menu item → group memory scan runs, rows populate,
  left-click selects/tunes, **middle-click recalls to VFO-B (RX2 auto-on)**,
  right-click toggles Scan flag, squelch-break pause works (both RX1/RX2),
  bandstack scan steps Thetis bandstack entries of current band (RX1 and RX2),
  low/high sweep stops on squelch, custom list file loads.
- No SWL / SWR controls on the form.

## 16. Open questions for the next session / user

1. `linelength`/`band_index` constants — confirm values from scan.cs during edit
   (`linelength` search; earlier notes suggest lines of `currFBox` text are fixed-width).
2. ~~VAC2 auto-enable on RX2 recall~~ **RESOLVED (2026-09-08): DROP.** KE9NS forced
   `setupForm.chkVAC2Enable = true` when auto-onning RX2 (routes VFO-B audio to VAC2).
   User confirmed: do not port it. See section 8a.
3. ~~`TXFreq2` + `UpdateRX2Filters`~~ **RESOLVED (2026-09-08):** Thetis has NO `TXFreq2`
   → `RecallMemoryB` uses `TXFreq`. `UpdateRX2Filters` EXISTS (console.cs:7659).
4. ~~`dataGridView2` column indices~~ **RESOLVED (2026-09-08):** MemoryRecord property
   order identical to KE9NS → indices unchanged (`[1]=RXFreq,[3]=DSPMode,[20]=RXFilter,
   ["Scan"]`). No remap.
5. ~~Thetis constructor block equivalent for `ScanForm = new ScanControl(this)`~~
   **RESOLVED (2026-09-08):** lazy-create in `scanToolStripMenuItem_Click` (mirrors KE9NS
   console.cs:79706-79714); no ctor edit needed.

## 17. Ordered task checklist

1. [x] Copy `scan.cs` → Thetis Console dir; namespace → `Thetis`. (DONE; Designer+resx
      copied, namespace TODO.)
2. [x] Strip SWL + SWR + F1-help code (per section 7 table). KEEP RX2 paths.
3. [x] Adapt SCAN1/SCAN2/SCANNER for Thetis (bandstack via `BandStackManager`,
     `SetBand` 3-arg, `ScanVFOB` RX2 flow, auto-RX2-on). Sections 8a, 10.
4. [x] Add local `SLowScan/SHighScan` + `UpdateBandScanRange()` to ScanControl; call from
     `ScanControl_Load` and Thetis `OnBandChangeHandler` (console.cs:46773, rx==1).
5. [x] Add `SetBand(string,string,double)`, `SetBand2(string,string,double)`,
     `RecallMemoryB(MemoryRecord)` to Thetis console.cs (sections 7, 8a).
6. [x] Copy `scan.Designer.cs` + `scan.resx` (namespace; drop SWR/SWL controls).
7. [x] Add console.cs fields + dispose + `scanToolStripMenuItem` + handler (§13; lazy create).
8. [x] Feed squelch/SIG from `picSquelch_Paint` + `picRX2Squelch_Paint` (uses static
     `ScanControl.SQL/SIG/ScanStop` + `SQL2/SIG2/ScanStop2`, matching KE9NS).
9. [x] Add csproj Compile/EmbeddedResource entries.
10. [x] Build (`MSBuild.exe` on sln) and fix errors. — Build PASSES with
     `-p:Platform=x64 -p:Configuration=Debug`.
11. [ ] Manual smoke test (RX1 + RX2 paths) per section 15.
12. [ ] (Later/optional) band-edge persistence to disk.

> Note re todo 8: feeds were placed in the squelch **Paint** handlers
> (`picSquelch_Paint` console.cs:29289, `picRX2Squelch_Paint` console.cs:39406) rather
> than the async `UpdateSQL`/`UpdateRX2SQL` loops — the Paint handlers already run on the
> UI thread every squelch update and carry the exact `sql_data`/`ptbSquelch.Value` values
> (mirrors KE9NS console.cs:59101-59123). Static access matches KE9NS (`ScanControl.SQL`
> etc.), no instance/IsDisposed guard needed. If cross-thread updates ever become noisy,
> move to the SQL loops per section 11.

## 18. Quick reference line numbers

KE9NS: scan.cs — SCAN1 thread (~1250s), SCANNER/button5 (2474), SCAN2 (~2146-2473),
SCAN3 (3051), currFBox click handlers (3278-3842), pause (3278), button5/SWRLow box logic.
KE9NS console.cs — feed: 51791-51801 (RX1), 51842-51852 (RX2), 51746 (UpdateSQL),
59101-59123 (scan stop extras), 79706-79714 (menu); `SetBand2` 11080-11121,
`RecallMemoryB` 77152-77251.

Thetis console.cs — `UpdateSQL` 25555, `rx2_sql_data`/`UpdateRX2SQL` 25571-25586,
`sql_data` 25554, `SetBand` 5884, `UpdateWaterfallLevelValues` 8913, `RX1Band` 17303,
`RX2Band` 17460, `RF` (AGC-T), `RX2DSPMode` 17784, `RX2Filter` 17948, `VFOBFreq` 18414,
`RX2AGCMode` 19805, `ClickTuneRX2Display` 10879, `CentreRX2Frequency` 10802,
`RX2Enabled` 38040, `PanCentreRX2` 19636, `RecallMemory` 41364,
`memoryToolStripMenuItem_Click` 41449-41465, memoryForm dispose 28666, `SetBandStack` 52904,
bandstack apply 46766, `handleBSFChange` 46410, `BandChangeHandlers` field 46102 / delegate 45938.

Thetis clsBandStackManager.cs — `BandStackEntry` 114, `BandStackFilter` 201,
`NumberOfEntries` 306, `EntryByIndex` 465, `Current` 531, `Next` 551, `Previous` 570,
`First` 520, `BandStackManager` 744.

Thetis MemoryForm.cs — KE9NS columns confirmed: RXFilter "comboboxColumnFilter" (line 142),
DSPMode (107), StartDate/Repeating/Repeatingm ke9ns (232-234).