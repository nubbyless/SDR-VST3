# PureSignal Update Handoff

Created: 2026-11-26
Status: **Implemented on `ps-reference-port`; full Release x64 build verified; G2E testing pending.**

## User decision

The user tested the `widesausagepotato/Thetis` PureSignal implementation and reported that it works much better on G2E than our current local PureSignal work.

The new baseline is therefore the **reference implementation**, not our local PS experiments. Do not re-litigate the overrange/tone-spacing reasoning in `WDSP_2.11_HANDOFF.md`; treat it as background only.

## Scope

Port the reference PureSignal behavior as the baseline, then re-add only these local features:

1. Noise stimulus as an option for **Single Cal**.
2. A **Noise** test button / TX noise generator path like the one we have.
3. The **spectral regrowth readout** shown while the noise stimulus is active.
4. The **PSCorrection / deadlock** options: `Strict`, `Medium`, `Relaxed`.
5. Correct handling of the **DB migration** described below.

Everything else local-only in PureSignal should be dropped unless separately requested.

## Sources

- Local repo: `C:\Users\W4YNY\Documents\thetisvst`
  - Current HEAD: `26a63aed Harden WDSP and fix 2TON skin state`
- Reference / “his” implementation, imported into this repo:
  - Remote: `widesausagepotato` -> `https://github.com/widesausagepotato/Thetis.git`
  - Local ref: `widesausagepotato/wdsp-2.11-r1` (tag `r1`)
  - Commit: `9625405aa7317997907d38d806cf185adeb276ad Port WDSP 2.11 and package Thetis r1`
  - Inspect with `git show widesausagepotato/wdsp-2.11-r1:<path>` and `git diff HEAD widesausagepotato/wdsp-2.11-r1 -- <path>`.

The imported reference branch does **not** contain later local VST/RADE/TCI/streaming work. Do **not** blindly copy whole shared files such as `setup.cs`, `setup.designer.cs`, `cmaster.cs`, or `MeterManager.cs`; port only the PureSignal-related behavior into the current tree.

## Key findings

### 1) The reference PS behavior is the desired baseline

Reference behavior to restore:

- `wdsp/calcc.c`: `SAMPLE_ACCEPT_OVERRANGE 1`; overrange samples go into the top bucket.
- Reference two-tone defaults: `700/1900` and stealth `70/190`.
- Fixed feedback thresholds: `90`, `128`, `181` (`PSForm.cs` reference ~1086-1117).
- `CalibrationAttemptsChanged` from `Info[7]`, 16-int `GetPSInfo`.
- Reference `GetPSDisp` marshalling (`out int` / `out double`) and reference `AmpView` lifecycle.
- Reference `ForcePS()`: starts the existing post-generator state; does not reapply our 700/1950 stimulus.
- No local `PS_debug.log`, failed-single state machine, `Info[6]` warning, target-feedback property, or tone-spacing warning.

### 2) The noise feature is mostly C#/UI, not WDSP

The reference WDSP already has the required pre-generator noise API:

- `Project Files/Source/wdsp/gen.c:644` — `SetTXAPreGenMode`
- `Project Files/Source/wdsp/gen.c:669` — `SetTXAPreGenNoiseMag`
- `wdsp.h` declares the same API in both trees.

So the noise feature can be re-added without changing WDSP generator code.

### 3) Noise needs deliberately high drive

Keep the local noise leveling behavior exactly. The noise is injected into the TXA **pre** generator before the TX filter and ALC, so it does not drive the radio as hard as the two-tone post generator.

Required pieces in `Project Files/Source/Console/setup.cs`:

- `applyTestStimulusGenerator(bool noise)` around local lines 11783-11881.
- Noise path:
  - `TXPostGenRun = 0`
  - `TXPreGenMode = 2`
  - `TXPreGenNoiseMag = 7.09 * modeScale * 0.49999 * Math.Pow(10.0, ttmag / 20.0)` at local line 11845.
  - `modeScale = 1.74` for `AM`, `AM_LSB`, `AM_USB` at local lines 11841-11844.
  - `TXPreGenRun = 1`
- `setNoiseTestLeveling(bool noise)` at local lines 36934-36952:
  - Save `MicGain` and `TXLevelerOn`.
  - For noise, force `MicGain = 1.0` and `TXLevelerOn = false`.
  - Restore both when the test stops.
- `TTgenrun` stop path must call `stopTestStimulus()` even when MOX is already off (local lines 11596-11614).

Do not reduce the `7.09` noise factor unless measured G2E calibration proves it is too high.

### 4) Strict / Medium / Relaxed is a native API change

The reference `calcc.c` has a fixed `DEADLOCK_MIN_FRAC 0.06` (reference line ~302). Our local version added:

- `double deadlock_min_frac` (local `calcc.c:50`)
- `DEFAULT_DEADLOCK_MIN_FRAC 0.06` (local `calcc.c:314`)
- Initialization (local `calcc.c:1044`)
- `top_bucket_useful_frac(..., double min_frac)` (local `calcc.c:836`) and call sites (~2025-2027, ~2066-2067)
- `SetPSDeadlockMinFrac` (local `calcc.c:2318-2327`, `wdsp.h:311`)

Plan: start from reference `calcc.c`, then re-add only this deadlock-fraction feature. Keep the reference `SAMPLE_ACCEPT_OVERRANGE 1`.

Do **not** port the rest of local `calcc.c` hardening unless requested later.

### 5) DB migration must be handled explicitly

Current local code has three relevant DB actions in `Project Files/Source/Console/setup.cs`:

- Lines 1922-1924: remove stale `chkTestIMDNoise`.
- Lines 1926-1939: one-time migration writing `udTestIMDFreq1 = 700`, `udTestIMDFreq2 = 1950`, marker `udTestToneFreq1250Hz`.
- Lines 1941-1947: overwrite stale `udPSTargetFeedbackLevel`.

Because the new baseline is the reference two-tone behavior:

- Remove the tone rewrite to `1950` and the `udTestToneFreq1250Hz` marker.
- Restore designer/preset defaults to `700/1900` and `70/190`.
- Keep a cleanup migration for stale `chkTestIMDNoise` if needed, since noise selection now lives in the PureSignal form.
- Remove hidden `udPSTargetFeedbackLevel` control/migration/property if the target-feedback feature is dropped.
- Decide before implementation whether DBs already carrying `udTestToneFreq1250Hz` should be reset once to `700/1900` or simply left at the operator’s current values after the marker is removed. Recommended: reset once to `700/1900` for DBs we previously forced, then remove the marker.

New installs should get reference tone defaults with no forced migration.

## Implementation plan

### Phase 1 — restore the reference PS/native baseline

1. Restore reference PureSignal behavior in:
   - `Project Files/Source/wdsp/calcc.c`
   - `Project Files/Source/wdsp/wdsp.h`
   - `Project Files/Source/wdsp/iqc.c` / `iqc.h`
   - `Project Files/Source/wdsp/TXA.c`
   - `Project Files/Source/wdsp/siphon.c`
   - `Project Files/Source/Console/AmpView.cs` and related designer/resource files
2. Keep reference `GetPSInfo` size and `GetPSDisp` ABI; update all C# P/Invokes to match.
3. Rebuild `wdsp.dll` and the C# app together. Do not mix local `wdsp.dll` with reference C# or vice versa.

### Phase 2 — restore reference PSForm/setup two-tone baseline

1. In `PSForm.cs`, restore reference state machine, thresholds, info handling, and `ForcePS()` behavior.
2. In `PSForm.designer.cs`, restore reference PSForm layout/control set as the base.
3. In `setup.cs` / `setup.designer.cs`, restore reference two-tone generator path, presets, and defaults.
4. Restore reference `console.resx` PureSignal tooltip if convenient; cosmetic only.
5. Leave `cmaster.cs` and `MeterManager.cs` alone except for PS-specific lines; their PS setup is functionally identical.

### Phase 3 — re-add deadlock options

1. Add `comboPSDeadlock` with `Strict`, `Medium`, `Relaxed`, default `Strict`.
2. Add handler mapping:
   - `Strict = 0.06`
   - `Medium = 0.04`
   - `Relaxed = 0.02`
3. Add `SetPSDeadlockMinFrac` P/Invoke and reapply it in `ForcePS()`.
4. Re-add the minimal native `calcc.c` support described above.

### Phase 4 — re-add noise stimulus and noise test button

1. Add PSForm controls:
   - `comboPSCalStimulus` with `Two-Tone` and `Noise`, default `Two-Tone`.
   - `btnPSNoiseGen`.
   - Keep `btnPSTwoToneGen`.
2. Add PSForm logic:
   - `singleCalUseNoise`, `UseNoiseStimulus`, `SetCalStimulus`.
   - `startSingleCalStimulus()` / `stopSingleCalStimulus()`.
   - `btnPSCalibrate_Click()` start/stop hooks.
   - Noise button highlight via `updateTestGenButtons()`.
   - `ForcePS()` should reapply the selected setup stimulus via `RestartTestStimulus()` only if this does not disturb the reference PS behavior; verify on G2E.
3. Add setup logic:
   - `TestIMDNoiseStimulus`, `StartTestStimulus(bool)`, `EnsureTestStimulus(bool)`, `RestartTestStimulus()`.
   - Stimulus-aware `chkTestIMD_CheckedChanged()`.
   - `applyTestStimulusGenerator(bool noise)` with the high noise drive and leveling described above.
   - `OnCalStimulusChanged()`, `UpdateCalStimulusUI()`, `TTgenrun` stop cleanup.

### Phase 5 — include regrowth readout

Keep the local noise measurement display:

- `Project Files/Source/Console/display.cs:363-369` — `Display.TestIMDNoise`.
- `display.cs:5475-5479` — noise EMA fields.
- `display.cs:5769-5770` — switch between IMD and noise measurement display.
- `display.cs:6624-6742` — spectral regrowth box:
  - in-band level
  - worst out-of-band bin
  - worst-bin regrowth
  - total/integrated regrowth
  - worst OOB frequency

The setup/PSForm noise code must set `Display.TestIMDNoise` on start, stop, and stimulus swap so the correct readout is shown.

### Phase 6 — DB migration cleanup

Implement the DB decisions above. Verify both:

- Fresh install: `700/1900`, `70/190`, no stale noise checkbox, no forced target.
- Existing DB with old marker: behaves according to the chosen reverse-migration policy.

## Explicitly out of scope for this pass

- `SAMPLE_ACCEPT_OVERRANGE 0` rationale and related two-tone corrections.
- `700/1950` and `70/194` tone presets unless the user reverses the baseline decision.
- Tone-spacing warning UI.
- Local scaled feedback thresholds and target-relative auto-attenuator.
- Local failed-single/retry state machine.
- `PS_debug.log` unless separately requested for debugging.
- Local `Info[16..19]` IQC logging.
- Local `SetPSCorrectionOff` and immediate correction shutdown.
- Local-only WDSP hardening/NURB/IQC changes except the deadlock API needed above.

## Verification checklist

1. Build x64 Release and installer with `0 Error(s)`.
2. Fresh DB check:
   - Test tones `700/1900`.
   - Stealth `70/190`.
   - No `chkTestIMDNoise` stale key.
   - No forced target/tone marker.
3. G2E two-tone PS:
   - Confirm reference behavior/calibration quality is retained.
4. G2E noise calibration:
   - Single Cal with `Noise` selected completes.
   - Noise test button drives the radio hard enough to reach calibration.
   - Mic gain/leveler restored after stopping the noise test.
5. Regrowth readout:
   - Appears only for noise stimulus.
   - Shows in-band, worst OOB, regrowth, total RG, and OOB frequency.
   - Falls back to normal IMD readout for two-tone.
6. Deadlock options:
   - `Strict`, `Medium`, `Relaxed` change the native fraction.
   - Setting survives `ForcePS()` / PS restart.
7. Confirm no mixed WDSP/C# ABI mismatch (`GetPSInfo`, `GetPSDisp`, IQC signatures).

## Next action

Wait for the user’s go-ahead, then implement in the phase order above, starting with the reference PS/native baseline plus the minimal deadlock API.
