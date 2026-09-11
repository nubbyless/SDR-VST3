# LayoutCheck Fix Log (LOCAL ONLY - do not commit)

Workers that run the fix loop must append every change here.

## Tool calibration (local-only Program.cs changes, NOT source fixes)
- Added `Appearance.Button` detection: checkbox/radio rendered as toggle buttons
  use button padding (12) not checkbox padding (22). Dropped 13 false positives.
- Text scanner now converts escaped `\r`/`\n` into real newlines so multiline
  controls measure per-line (was seeing "rn" literals). Dropped 10 FPs.
- Added `FitsByWrapping`: label whose single-line text overflows but wraps to fit
  its box height is skipped (paragraph labels). Dropped ~140 FPs.
- **FIXED FONT PARSER BUG (2026-09-09)**: `ParseFontArgs` mis-split quoted font
  names at the comma after the closing quote, injecting empty parts, so EVERY
  designer-set Font (e.g. "Microsoft Sans Serif" 6.75F) failed to parse and
  controls silently fell back to parent Segoe UI 9pt. This inflated widths for
  anything with an explicit Font. After the fix: lblPS now measures 56px (was
  72px) and several findings recalibrated. Re-run baseline shows the ORIGINAL
  code should be treated as **87 CLIP + 40 CLIP@125** (was reported 95 + 27 under
  the bug). CLIP@125 items are all now resolved. NOTE: the earlier 107-width
  batch was computed under the bug; 15 of its widths were over-wide and created
  real overlaps - those 15 were reverted, leaving them tight again (see below).

Baseline progression on `--clip-only`:
  256 CLIP (start) -> 243 -> 233 -> 95 CLIP + 27 CLIP@125 (under font bug)
  -> after font fix + reverts: **22 CLIP + 1 CLIP@125** (33 overlaps, no regressions)

## Source fixes
### frmCFCConfig.Designer.cs - 2 widths widened
### Memory/MemoryForm.Designer.cs - 3 widths widened
### PSForm.designer.cs - 2 widths widened
### scan.Designer.cs - 1 width kept (labelTS23 revert reverted to 63, tight)
### setup.designer.cs - bulk widths widened; labelQuickSplitInfo 112->126;
  13 CLIP@125 micro-fixes (5 "Band" labels 32->36, 8 "%" buttons 28->31)

## Remaining (judgment items) - as of 2026-09-10 evening: 6 CLIP + 0 CLIP@125
remain, all wrap-to-fit paragraphs (designed multiline, no action needed):
frmSeqLog labelTS9, labelStreamOutHint, lblWarningBufferType/FilterSize/
BufferSize, and chkPreventTXonDifferentBandToRX (user confirmed it looks fine
wrapped). The rest of the original list is RESOLVED (see Log). Global:
6 CLIP / 0 CLIP@125 / 183 TIGHT / 0 OVERLAP.
Historical list of the original judgment items preserved below.

### A. Wrap-to-fit paragraphs / multiline (fits at runtime by wrapping) - 9 CLIP
- frmSeqLog.Designer.cs:457 labelTS9 [295,29 239x38] "obtained by running
  DumpCap.exe -D on command line and using number on left of list"
  need 474px/592px@120 vs avail 239 (long tooltip-style caption)
- setup.designer.cs:1943 labelStreamOutHint [14,176 540x30]
  "Sends a copy of the radio audio to the selected Windows output device..."
  need 1117/1400 vs avail 540 (hint paragraph)
- setup.designer.cs:2305 lblWarningBufferType / 2306 lblWarningFilterSize /
  2307 lblWarningBufferSize [13..260,357 115x46] "X are different. Slow mode
  change is possible" - warning labels, wrap in 46px-high boxes
- setup.designer.cs:3821 chkLimitPowerCATTCIMsgs [16,136 176x33]
  "Apply power limits to CAT/TCI power related queries (out)" (2-line checkbox)
- setup.designer.cs:285 chkPreventTXonDifferentBandToRX [129,46 115x50]
  (multiline checkbox)
- setup.designer.cs:75444 chkAutoPACalibrate [580,278 120x32]
- setup.designer.cs:3007 chkUsePowerOnDrvTunPA [12,25 111x39]

### B. Tight labels, widen headroom blocked by touching neighbor - 13 CLIP + 1 CLIP@125
These were over-widened in the earlier (buggy-metrics) batch, created real
overlaps, and were reverted to original width. They still clip at original width.
Fixing requires nudging the neighbor (or shortening text), not just widen.
- scan.Designer.cs:65 labelTS23 [70,36 63x18] "dBm Thres:" need 67/82; right
  neighbor udIDThres [133,36] - zero gap
- setup.designer.cs:75642 labelTS325 [8,52 80x13] "Key-Down (mS)" need 90/113;
  neighbor udHWKeyDownDelay [93,50]
- setup.designer.cs:315 chkCTLimitDragToSpectral [34,203 107x16] need 93/118;
  neighbor chkCTLimitDragMouseOnly [145,203]
- setup.designer.cs:314 chkCTLimitDragMouseOnly [145,203 109x16] need 96/120
- setup.designer.cs:2072 lblDisplayMeterTextHoldTime [8,42 120x16]
  "Digital Peak Hold (ms):" need 128/161; neighbor udDisplayMultiTextHoldTime
  [136,40]
- setup.designer.cs:2074 lblDisplayMultiPeakHoldTime [8,18 128x16]
  "Analog Peak Hold (ms):" need 132/164; neighbor udDisplayMultiPeakHoldTime
  [136,16]
- setup.designer.cs:3241 lblAppearanceGenBtnSel [154,71 87x15] need 93/117;
  neighbor clrbtnBtnSel [245,66]
- setup.designer.cs:3238 labelTS8 [154,100 85x15] need 89/113; neighbor
  clrbtnSliderLimitBar [245,95]
- setup.designer.cs:2260 lblTXWFAmpMax [8,43 61x19] "High Level:" need 66/82;
  neighbor udTXWFAmpMax [72,42]
- setup.designer.cs:3947 btnFormLocationHelper [347,55 106x23] need 99/124;
  neighbor btnShowSeqLog [459,53]
- setup.designer.cs:75748 lblCWBreakInDelay [8,48 64x16] "Delay (ms):" need 66/83
- setup.designer.cs:2104 lblWaterfallAGCOffsetRX1 / 2180 RX2 [8,69 64x16]
  "AGC Offset" need 66/82
- setup.designer.cs:3634 lblKBTuneDigit [16,16 32x16] "Digit" CLIP@125 (m-2@120)

### C. Misc notes
- lblPS (ucInfoBar) resolved: font-parse fix shows it measures 56px now
  (Microsoft Sans Serif 6.75pt) still need > 44 avail; anchored right of the
  status bar. Deferred, not in the 22+1 because ucInfoBar not in clip run
  (probe only). Check before acting.

## cwx.cs (CWX "CW Memories and Keyboard" window) - 2026-09-10
The CWX form's layout is defined inline in `cwx.cs` (not a *.Designer.cs), so
the tool never scanned it. Added `cwx.cs` to the file filter (local tool change)
so the window is now covered by the clip scan.
Findings + fixes (verified unchanged from ramdor/master upstream first):
- chkAlwaysOnTop [528,8 104x24] "Always On Top" CLIP (need86/avail82@96) -> 112
- chkFocusRequired [637,12] nudged right to 646 so the wider checkbox above it
  doesn't collide (autosized "Focus")
- clearButton [120,152 75x23] "Clear (F12)" CLIP@125 (need80/avail78@120) -> 78
- dropdelaylabel [384,32 64x16] "Drop Delay" CLIP (need65/avail64@96) -> 68
  (this label is hidden at runtime, but fixed for consistency)
- stopButton [48,8 72x24] "Stop (Esc)" TIGHT m0@120 -> 76
Untouched judgment items: F-key buttons s1-s9 and speedLabel stay TIGHT (no
neighbour headroom; left as-is).
Post-fix scan: cwx.cs clean (0 CLIP / 0 CLIP@125 / 0 OVERLAP); global totals
unchanged at 22 CLIP + 1 CLIP@125 + 33 OVERLAP (baseline, no regressions).
Build: managed compile clean; only pre-existing MSB3030 copy error for missing
`lib\fftw_AnyCPU\libfftw3-3.dll` (env issue, not from this change).

## Log
- (2026-09-09) started; tool calibrated; no source edits yet.
- (2026-09-09) 107 designer widths widened; font-parser bug found + fixed;
  15 over-wide widths reverted (overlap regressions); overlaps back to 33 baseline;
  full app compiles clean (x64 Release).
- (2026-09-09 evening) Built OK and user eyeballed the running app. User reported
  they "still see some issues" - NOT yet itemized. Deferred to next session.
  TODO first actions next time:
  1. Ask user which specific UI areas still look wrong (screenshots/coordinates).
  2. Re-run clip/overlap report with the FIXED font parser for baseline.
  3. Reconsider the 22 CLIP + 1 CLIP@125 judgment items listed above.
- (2026-09-10) CWX window: tool now scans cwx.cs (inline designer code); fixed 5
  CWX controls (see section above); re-ran scan - CWX clean, totals at baseline.
- (2026-09-10) CWX size/scale fix (follow-up, user report: window "too big for the
  window", content overflow right/bottom):
  - Saved-size mismatch: expand/collapse toggle now drives the CLIENT area
    (704x281 expanded / 450x151 collapsed) instead of outer Width/Height, so
    .NET frame differences no longer leave fixed-coordinate content clipped;
    added `NormalizeSize()` after RestoreForm so it always opens at a canonical
    size rather than a stale saved one (previously the window could open "really
    big" until collapse+re-expand snapped it back).
  - Root cause of "content too big for the window": legacy `AutoScaleBaseSize
    (5,13)` line. Measured at 125% DPI: it makes .NET 10 scale contents ~1.48x
    (X) / ~1.62x (Y) while scaling the window client only to 1042x455, so right
    edge 1098/bottom 458 poked out. Replaced with the standard pattern used by
    every other form: `AutoScaleDimensions (6F,13F)` + `AutoScaleMode.Font`,
    which scales window + contents uniformly (window 939x432, content fits).
    cwedit.cs (the other inline-layout form) uses the (5,13) dims form; leaving
    as-is (its window isn't reported broken).
  - Verif: LayoutCheck extent scan (new --extent mode) = no cwx.cs control
    exceeds 704x281 design space beside the form.
  - Build: x64 Release clean (0 errors).
  - Tool: LayoutCheck/Program.cs gained `--extent` (design-space fit check).
- (2026-09-10) CWX follow-up: uniform scaling leaves only ~1px of margin at the
  window edges (keyboard border y=263, rightmost controls ~701), which reads as
  "a little small". Bumped the EXPANDED client from 704x281 to 716x290
  (+12 right / +9 bottom design slack); nothing in the layout moves (extent
  scan stays clean), so at any scale the window now clears the content.
  Collapsed client unchanged (450x151). Rebuilt x64 Release, 0 errors.
- (2026-09-10) CWX follow-up: at 125% DPI the saved-form size and expanded
  state still clipped content ("right side of data cut off"). Screenshot
  pixel-analysis (ASCII via temp scripts) showed content to the extreme right
  (x~893/895) and below the client bottom (dark element ~y375-390 vs scaled
  client bottom ~368) - the design's debug labels (label4..label7 at y304-384)
  and drawn content were beyond the 704x281 canvas. Enlarged EXPANDED client
  716x290 -> 760x340 -> 820x340 (expandButton to 736,316 then 796,316) until
  all controls cleared the edges. Verified by user: "all controls are showing
  now".
- (2026-09-10) CWX follow-up: field/button rows overlapped the drawn keyboard
  pad because show_keys() painted at FIXED design coords (pad y=180..263,
  key step dx=11/dy=19) while WinForms scales the real controls at 125%
  (effective Y-scale ~1.45, larger than the Current/AutoScaleDimensions ratio
  ~1.23). First fix scaled geometry by the font ratio (mismatch remained);
  final fix anchors the pad rect + key spacing to the hidden txtdummy1
  control's ACTUAL rendered bounds (sfx = Width/665, sfy = Height/82, font
  pet size = 14*sfy, pad = txtdummy1.Bounds+1). Pad now tracks whatever scale
  the form really applies. Verified by user: "it looks good now". Rebuilt x64
  Release, 0 errors.
- (2026-09-10) eqform.cs ("10-Band Equalizer" radio clipped): rad10Band (and
  rad3Band) are 120px wide; "10-Band Equalizer" wraps to a second line at the
  net10 default font (Segoe UI 9pt, wider than the MS Sans Serif 8.25 the
  net48 Thetis used, where it fit on one line). Widened both radios 120->150
  (Location unchanged: 12,3 / 140,3; right edge 290 < pnlLegacyEQ 536, nothing
  to collide). LayoutCheck: eqform.cs has no findings; global totals unchanged
  (22 CLIP / 1 CLIP@125 / 33 OVERLAP -> 0 now; OVERLAP 0).
  Verified by user visually. Rebuilt x64 Release, 0 errors (needed Thetis
  closed - it was locking bin\x64\Release DLLs during copy).
- (2026-09-10) eqform.cs follow-up: my wider 150px radios now overlapped each
  other on the same row (rad3Band 12..162 vs rad10Band 140..290) - rad10Band's
  leading "1" and radio dot were hidden under rad3Band (read as "0-band
  Equalizer", dot gone). Moved rad10Band to (170,3) keeping width 150; rad3Band
  stays (12,3) 150 -> 8px gap, no overlap. Verified by user: "fixed looks
  normal now". Rebuilt x64 Release, 0 errors.
- (2026-09-10) scan.Designer.cs: user reported text overlapping the RIGHT
  frame of the group boxes. Cause: prior layout fix widened chkAlwaysOnTop
  104->110 and its right edge (300+110=410) now lands EXACTLY on groupBoxTS2's
  width (410); udIDTimer (357+53=410) likewise. Moved the group boxes' right
  frames out: groupBoxTS2 410->426, grpGenCustomTitleText 620->636 (both keep
  the same shared right edge), form ClientSize 644->660 to fit (no right
  anchors on textBox3/dataGridView2/currFBox, so nothing else stretches).
  LayoutCheck: scan has no new OVERLAP (0), global totals unchanged (22/1/0).
  Built x64 Release, 0 errors. Verif pending.
- (2026-09-10) scan.Designer.cs follow-up (user verified frame fix): widened
  textBox3 608->630 (18..648) and currFBox 620->636 (12..648) so both right
  edges now match the new frame edge (x=648 = grpGenCustomTitleText/groupBoxTS2
  shared right edge). Built x64 Release, 0 errors. Verified by user: "perfect".
- (2026-09-10) setup.designer.cs + scan.Designer.cs CLIP batch (from the tool's
  remaining 22 CLIP + 1 CLIP@125 list). Fixed by widening + nudging the touching
  neighbour (all verified later by user at run time):
  - scan.Designer.cs: labelTS23 "dBm Thres:" 63->71, udIDThres 133->141
  - grpCWDelay: labelTS325 "Key-Down (mS)" 80->95, udHWKeyDownDelay 93->103
  - grpOptMisc: chkCTLimitDragToSpectral 107->117; chkCTLimitDragMouseOnly
    109->121 and moved 145->155
  - grpDisplayMultimeter: lblDisplayMeterTextHoldTime 120->131,
    lblDisplayMultiPeakHoldTime 128->134; spinner column 136->146
    (udDisplayMultiTextHoldTime/MultiPeakHoldTime/MeterDelay/MeterAvg/
    MeterDigitalDelay)
  - grpTXWFAmpScale: lblTXWFAmpMax 61->69, udTXWFAmpMax 72->77
  - grpDisplayWaterfall/grpRX2DisplayWaterfall: lblWaterfallAGCOffsetRX1/RX2
    64->69, spinners 72->77 (x2)
  - groupBoxTS31: labelTS8 85->92, lblAppearanceGenBtnSel 87->95, clrbtns
    245->250 (x2)
  - grpKBTune: lblKBTuneDigit 32->46 (was the only CLIP@125)
  - grpDSPKeyerSemiBreakIn: lblCWBreakInDelay 64->69, udCWBreakInDelay 72->77
  - groupBoxTS30: btnFormLocationHelper 106->114
  CLIP 22 -> 9 (after this batch), CLIP@125 1 -> 0, 0 overlaps. Rebuilt x64
  Release, 0 errors. Remaining 9 = wrap-to-fit paragraphs + 4 checkbox cases.
- (2026-09-10) 4 checkbox wrap cases handled with user (visually confirmed in
  the running app):
  1. chkPreventTXonDifferentBandToRX - user: looks fine as-is, LEFT untouched.
  2. chkLimitPowerCATTCIMsgs "queries" cutoff -> widened 176->204 (16..220,
     full group inner width) so text wraps to 2 lines. Verified.
  3. chkUsePowerOnDrvTunPA "slider" cutoff -> checkbox 111->150; nud
     MaxPowerForBandPA 139->166; "watts" label 206->231; panelTS1 widened
     252->271, then shifted (269->250,314) so its RIGHT edge returns to 521,
     realigning with panelAdjustGain above it (the +19 width had broken the
     521 alignment). Content sized as-is. Verified: "perfect".
  4. chkAutoPACalibrate [580,278 120x32] "Use Advanced Calibration Routine" -
     this is a HIDDEN debug toggle (only appears with Ctrl+Alt+A / Ctrl+Alt+P
     in setup, MW0LGE_22b). User revealed it and confirmed "Routine" was cut
     by 2 letters. Widened 120->136 (580..716, page inner width) so the text
     wraps to two lines; panelAutoPACalibrate (560,8 156x247) is revealed by
     checking it. Verified: "perfect".
  Global: 6 CLIP / 0 CLIP@125 / 183 TIGHT / 0 OVERLAP. The remaining 6 CLIP
  are all designed-to-wrap paragraph labels (see Remaining section). Rebuilt
  x64 Release, 0 errors.

- (2026-09-10 evening) User-reported batch (all verified in running app):
  1. Display/General -> grpSpectralWarningLeds: chkSpecWarningLEDRenderDelay
     "Unable to render in time" was clipped by the 147-wide group (checkbox
     161px spilled past the group client, text needs 135). Group moved
     394->383, widened 147->176 (383..558, clear of grpDisplayDriverEngine at
     566); checkbox 161->159 -> fully inside, m+3.
  2. Options/Options2 -> grpQuickSplit: the 3-line note (139,139 126x39) was
     clipped right (past group inner edge) AND bottom (3 lines > 39px). No
     room to grow the group, so the note was redesigned as a 2-line,
     full-meaning label at (84,142) 165x34 "note: options are\r\napplied when
     QSPLT ON" (right of PanAudio, within inner width); Trimmed width so line2
     fits (was need 168 vs avail 165 - shortened to "applied when QSPLT ON").
  3. VAC1/VAC2 -> Buffer Latency (ms) groups (grpAudioLatency2 /
     grpVAC2LatencyManual): the 4 "Manual" checkboxes per tab were 16px tall
     so the top of the check glyph was clipped; 8 checkboxes 16->17.
  4. DSP/AGCalc -> groupBoxTS17 "AGC with automatic noise floor compensation":
     chkAutoAGCRX1/RX2 text "(requires pana/water)" collided with the ±Shift
     nud boxes; checkboxes 201->219 and udRX1/2AutoAGCOffset 222->235,
     label1 "± Shift" 224->237.
  5. DSP/FM -> grpDSPFM: lblFMDetLimGain "Limiter Gain" (5,94) AutoSize grew
     into tbDSPFMDetLimGain (y111); moved to y=88, clear of slider.
  6. Audio/Streaming -> groupBoxStreamOut: labelStreamOutHint 540x30 only
     held 2 lines; now (14,166) 540x48 for all 3 lines, and group grown
     210->216 so the 3rd line isn't cut at the inner bottom. Verified.
  Global after batch: 5 CLIP / 0 CLIP@125 / 0 OVERLAP (CLIP list is now only
  wrap-to-fit paragraphs: labelTS9, lblWarningBuffer* x3,
  chkPreventTXonDifferentBandToRX). Rebuilt x64 Release, 0 errors.
  NOTE: chkGpuComputeShaders (grpDisplayDriverEngine 566,166 147 wide) is
  still untouched and may clip "(exp.)" at the group edge - tbd with user.
  RESOLVED same session (below).
  Batch 2 (2026-09-10 evening, user-proposed layout): "GPU compute shaders
  (exp.)" cut at the DirectX Display Settings group edge. Moved
  grpSpectralWarningLeds 383->365 so its right edge (541) aligns with
  grpDisplay3DPanadapter (394..541), then widened grpDisplayDriverEngine
  ("DirectX Display Settings") leftwards: Location 566->541, Size (147,175)
  ->(172,175) (541..713, flush-right with page). Checkbox AutoSize now fits
  inside the 172-wide group client. LayoutCheck: 0 OVERLAP.
  NOTE 2: VAC1/VAC2 "Buffer Latency (ms)" Manual checkboxes: user reported the
  tops "nipped" ~1-2px. Height 16->17 then spinners nudged up 2px did not
  remove it; user decided it is negligible - LEFT AS-IS (spinner rows now
  sit at y33/101 with 2px clearance, keep that; checkbox tops not pursued).

does not remove it; user decided it is negligible - LEFT AS-IS (spinner rows now
  sit at y33/101 with 2px clearance, keep that; checkbox tops not pursued).

## Batch 3 (2026-09-10 late, VAC Buffer Latency follow-up)
  User: on both VAC tabs the 4 "Manual" checkbox tops under RingBuffer /
  PortAudio were "nipped" (on VAC2 the ringbuffer RIGHT one was not, the other
  3 were). Attempted full fix + user layout moves below. Builds all exit=0;
  LayoutCheck after all: 5 CLIP / 0 CLIP@125 / 184 TIGHT / 0 OVERLAP (unchanged
  findings; no new CLIP/OVERLAP from these edits).
  1. Shared first pass (both tabs): grpDirectIQOutput/grpVAC2DirectIQ height
     94->78 (bottom 106->90) to give the latency group room; grpAudioLatency2 /
     grpVAC2LatencyManual grew 155->171 and top 112->96; ALL 12 labels +
     8 spinners + 8 checkboxes in the two latency groups re-spread +6 vertically
     (RingBuffer hdr 16->22, row1 In:/Out: 37->43 + nuds 33->39 + chk 56->66,
     PortAudio hdr 87->93, row2 In:/Out: 105->111 + nuds 101->107 +
     chk 124->134, mixture of IDs: labelTS360-371, udAudioLatency2{,_Out,
     PAIn,PAOut}, udVAC2Latency{,_Out,PAIn,PAOut}, chkAudioLatency{Manual2,
     Manual2_Out,PAInManual,PAOutManual}, chkVAC2Latency{Manual,OutManual,
     PAInManual,PAOutManual}). Spinner rows now clear their checkboxes by ~7px.
  2. This clipped VAC1's extra 4th row "Use RX2" (chkAudioRX2toVAC 16,68,
     bottom 84 > 78-height client 72). User suggestion to put Use RX2 to the
     right of SwapIQ was NOT possible: "SwapIQ" row already holds "Calibrate
     I/Q" at x93 (93..183); measured Segoe UI 9pt widths - SwapIQ 63, Use RX2
     65, Calibrate I/Q 90 - cannot share one 141px-client row. Chose the
     user's fallback idea: restore VAC1 grpDirectIQOutput to 149x94 (bottom
     106, Use RX2 clear) and drop grpAudioLatency2 top 96->112 (height stays
     171; internal rows stay +6-spread, moving everything ~16px lower and
     filling the extra bottom space).
  3. VAC2 mirrored for consistency: grpVAC2DirectIQ back to 149x94,
     grpVAC2LatencyManual top 96->112. Both tabs now: DirectIQ (550,12)
     149x94 (12..106), latency (550,112) 150x171 (112..283).

## Handoff state
- Working tree: 5 designer files modified (frmCFCConfig, MemoryForm, PSForm,
  scan, setup) + local-only Program.cs font fix. All UNCOMMITTED.
- git = ahead of origin/master by 3 commits; none of this session's work committed.
- Do not push/commit anything (LOCAL ONLY).