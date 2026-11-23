# fldigi Tier 2 Integration — Handoff / Master Plan

Status: **P1 implemented and passing in the forked tree** (transport, mode
switch, and offline decode gate all verified 2026-09-11; see §5.1–5.2).
**P2 code-complete and compiling 2026-09-12** — Thetis-side bridge (native
`fldigi_mod` RX/TX taps, `FldigiManager` spawn/restart, `FldigiAudioBridge` /
`FldigiControlBridge` named-pipe clients, `audio.cs` per-over MOX gate, Setup ->
DSP -> FLDIGI tab, app-exit teardown). Whole solution builds Release x64
(`ChannelMaster.dll` + `Thetis.dll`); on-air RX/TX round trip not yet exercised.
P3 (control-sync polish) and P4 (installer) pending.

This document is the handoff for adding fldigi (HF digital modes) to the SDR-VST3
Thetis fork as a transparently-managed sidecar process, with the fldigi payload
baked into the MSI installer. Copy this file into a fresh session context to
resume work without redoing the research.

---

## 1. Objective

Let users run fldigi digital modes (RTTY, PSK, Olivia, MT63, MFSK, Contestia,
dominoEX, Feld-Hell, CW... ) seamlessly and CFR-legally through the SDR-VST3
radio:

- Thetis spawns/stops our fldigi fork automatically (no user setup).
- Audio flows between Thetis and fldigi over named pipes — **no VB-Cable /
  virtual audio driver, no COM rig control, no Hamlib, no flrig**.
- Control (mode / frequency / filter / PTT) syncs both directions over a
  second pipe channel, reusing Thetis' existing TCI dispatch handlers.
- fldigi keeps its own window/FLTK GUI in Tier 2 (GUI re-homing = Tier 3).

## 2. Decisions locked

| Decision | Choice | Rationale |
|---|---|---|
| Tier (2 vs 3) | **Tier 2** (sidecar process + its own UI) | Weeks-months vs days; GUI re-home ~80% of cost; can revisit Tier 3 later |
| fldigi payload delivery | **Baked into MSI** (`INSTALLFOLDER\Fldigi`) | Silent/offline/version-locked; no EXE-installer (Bundle ExePackage) needed — fldigi is portable payload, not a machine runtime |
| Audio transport | **Named pipes**, not shared memory, not localhost TCP | Simple, no firewall prompts, no ports |
| Audio rate | Thetis resamples 48k -> 8k mono (fldigi default) via WDSP `xresampleFV`; back traffic goes into mic FIFO | Mirrors RADE TX resampling |
| Control transport | 2nd named pipe carrying JSON lines | Easy to debug by hand |
| fldigi build | MinGW (upstream route), **keep portaudio/hamlib compiled but unused for P1** — our SoundBase subclass is the default device so they're never selected | Removes all configure/Makefile stripping risk; stripping is a size optimization later, not a blocker |
| Config location | `%APPDATA%\SDR-VST3\fldigi` via `fldigi -configdir`, seeded by Thetis at first run | No install-time file surgery; survives uninstall like Thetis' own appdata |
| Installer type | Plain MSI components in `Product.wxs` | Same class as existing `RuntimeAssets`/`LicensesComponents` payloads; `.NET 10` was `ExePackage` only because it is a machine-level runtime |
| License | fldigi GPL-2.0+, repo LICENSE is GPL-2.0 — **compatible** | Add GPL NOTICE + `LicFldigi` RTF to installer |

User-reported time expectation: the RADE integration in this lineage took
**hours**; fldigi Tier 2 is a smaller variant of the same pattern. Honest
estimate: **~1 working day of agent code work**; the only unbounded term is
first-time MinGW build friction.

## 3. Architecture

```
Thetis (SDR-VST3)                     fldigi.exe (our fork, separate process)
┌─────────────────────┐               ┌──────────────────────────────────┐
│ pipe.c taps (rx)    ├──pipe0 RX────▶│ SndPipe: SoundBase subclass      │
│  48k mono, stereo   │               │   rx_process() / ModulateXmtr()  │
│   interleave dbl    │               │                                  │
│ mic FIFO (tx inject)│◀──pipe1 TX────│ SndPipe->Write() (modem TX out)  │
│   via RADE TX path  │               │                                  │
│ FLDigiControlBridge ├──pipe2 JSON──▶│ control-pipe thread -> set_freq /│
│   → TCI dispatch     │◀─────────────│ set_mode / PTT / filters          │
│   (existing handlers)│ mode/freq/    │                                  │
└─────────────────────┘  filter/ptt   └──────────────────────────────────┘
```

Key reuse points (all already in tree):
- RX audio: `Project Files/Source/Console/ChannelMaster/pipe.c:186` (RX1), `:230` (RX2+)
- TX audio: `Project Files/Source/Console/ChannelMaster/pipe.c:263`
- Resampler: WDSP `xresampleFV` (r8brain) — same as RADE TX now (radae.c)
- PTT/EOC arbiter: `Project Files/Source/Console/audio.cs:420-503`; mutex pattern `Project Files/Source/Console/ChannelMaster/cmaster.cs:1325-1332`
- Control-dispatch reuse (route JSON through these, do NOT fork new plumbing):
  - `Project Files/Source/Console/TCIServer.cs:3848` `handleVFOMessage`
  - `TCIServer.cs:3961` `handleModulationMessage`
  - `TCIServer.cs:4518` `handleRxFilterBand`

## 4. fldigi facts (verified)

- Latest version: **4.2.13** (2026-07). Source tarball ~5 MB, upstream Windows
  setup.exe ~7.4 MB. Mirror: `https://www.w1hkj.org/files/fldigi/`,
  SourceForge `w1hkj/fldigi` (GitHub) and `https://sourceforge.net/p/fldigi/fldigi/ci/master/tree/`.
- License: GPL-2.0-or-later. Compatible with this repo's GPL-2.0.
- Official Windows build route: MinGW cross-compile (Linux, WSL-with-MXE, or
  native MSYS2 MINGW64). Upstream uses autotools `./configure`+`make`, NSIS for
  installer. Scripts: `setupmxe.sh` / `buildmxe.sh`.
- Stock build DLL set (from upstream win-arm64 guide): libsndfile, libsamplerate,
  libportaudio, libhamlib, libogg, libpng16, libvorbis*, zlib1, and MinGW runtime
  (libstdc++/libgcc/winpthread).
- fldigi core architecture (from source):
  - `modem` base class (src/include/modem.h): pure virtual `rx_process(const double*, int)` / `tx_process()`.
  - Per-mode native sample rates (8k / 11.025k / 16k...).
  - TX out: `ModulateXmtr()` / `ModulateStereo()` → `TXscard->Write()`.
  - RX in: `trx_receive_loop()` reading a `SoundBase` instance (portaudio by default).
  - Heavy globals/state: progdefaults/progStatus, trx_state, mode_info[],
    qrunner/REQ, RSID/ReedSolomon, `put_rx_char()` / `get_tx_char()` text queues.
- fldigi has **no native TCI client** (bridges exist: tciadapter→Hamlib, flrig,
  Hamlib-master TCI PR #2078) — irrelevant to us; we own the pipe.

## 5. fldigi fork work (P1)

Fork `w1hkj/fldigi` at 4.2.13 into this repo (keep GPL notices):

1. **`soundcard/SndPipe.{cxx,h}`** — `SoundBase` subclass:
   - `Read()` consumes the RX pipe (Thetis→fldigi), resamples/rate-shapes into the
     modem buffer; `Write()` emits modem TX out onto the TX pipe.
   - Register it as the *default* audio device so portaudio is never opened.
2. **Control-pipe thread** — listen on pipe2, parse JSON lines, dispatch to the
   same internal setters the rigio paths call (frequency, mode, filters, PTT).
3. **Hidden CLI** — `fldigi.exe --sdrvst3-pipe [configdir]`.
4. **Build** — MinGW `x86_64-w64-mingw32`, `-static-libgcc -static-libstdc++`,
   static zlib/libpng where practical. Reuse/Harvest upstream `setupmxe.sh` for
   the toolchain bootstrap. **Do not strip portaudio/hamlib in P1.**
5. **P1 exit gate:** decode a known digital recording through the pipe path
   (offline) before touching Thetis.

### 5.1 Implemented wire format (VERIFIED PASSING)

fldigi = server; Thetis = client. Pipe names:
`\\.\pipe\<base>.<fldigi-pid>.AUDIO` and `.CTL`; base via `--sdrvst3-pipe=<base>`
(default `SDRVST3.FLDIGI`).

- **AUDIO pipe** — raw float32 little-endian mono, always 8 kHz, no framing.
  One-way: Thetis writes, fldigi reads (`audio_thread` → `sndpipe_read_audio`).
  Server never writes this pipe, so a plain blocking read is safe here.
- **CTL pipe** — newline-delimited JSON, bidirectional.
  Thetis→fldigi:
  ```
{"cmd":"ping"}
  {"cmd":"get_status"}
  {"cmd":"set_freq","rf":14070000}
  {"cmd":"set_mode","mode":"RTTY"}
  {"cmd":"set_ptt","on":true}
```
  fldigi→Thetis, every 250 ms:
  ```
  {"type":"status","mode":"RTTY","rate":8000,"rxfreq":"14070000","ptt":"RX","pid":1234,"txt":"<last 96 chars of RX text, JSON-escaped>"}
  ```
  plus request replies `{"type":"ok"}`, `{"type":"pong","ok":1}`, `{"type":"err","e":"unknown command"}`.
  fldigi→Thetis, on operator-initiated QSY (waterfall click / macro / undo,
  hooked in `do_qsy`):
  ```
  {"cmd":"qsy","rf":14070000}
  ```
  Two-way QSY mirrors a rig under CAT: radio dial move → Thetis `set_freq` →
  fldigi waterfall follows; fldigi waterfall click → Thetis sets `VFOAFreq`.
  No loop: the echoed `set_freq` lands in the sidecar's plain `qsy()` path,
  never back through `do_qsy`, and both sides dedupe identical values.
- `set_mode` matches `mode_info[i].name`/`sname` (e.g. RTTY sname/name both
  `"RTTY"`, PSK31 sname `"BPSK31"`/name `"BPSK-31"`) then `init_modem((trx_mode)i, 0)`.

### 5.2 Named-pipe concurrency rules (CRITICAL — verified deadlocks)

This Windows/.NET combo deadlocks permanently if a **blocking read** is pending
on a byte-mode duplex pipe while a **write** is issued on the same pipe. Proven
by pure-C# loopback (no fldigi): V2/V5 layouts freeze; V1/V4 do not.

- **Server (fldigi) rule:** never hold a blocking CTL `ReadFile` while the TX
  writer thread can write. Implemented with a `PeekNamedPipe` poll loop
  (Sleep(2) when no data; blocking read only when `avail > 0`). One-way AUDIO
  pipe is exempt.
- **Client (Thetis) rule:** never keep a blocking `StreamReader.Read*`
  outstanding while writing concurrently. Use overlapped/async reads
  (`BeginRead`/`EndRead` loop) or strictly sequential request/response.

## 6. Thetis work (P2/P3)

New dir `Project Files/Source/Console/FLDIGI/`:
- `FldigiManager.cs` — **done**: process lifecycle (spawn with
  `--sdrvst3-pipe=SDRVST3.FLDIGI --config-dir=<...>`, kill on app exit, 4 s
  auto-restart while enabled, resolves `Fldigi\fldigi.exe` next to the host or
  from `txtFLDIGIPath`), arms native taps, hosts desired freq/mode/PTT state.
- `FldigiAudioBridge.cs` — **done**: duplex `NamedPipeClientStream` to
  `SDRVST3.FLDIGI.<pid>.AUDIO`; async read loop pushes fldigi TX audio via
  `cmaster.FldigiPushTx`, writer thread drains `cmaster.FldigiDrainRx(0,…)` into
  the pipe. RX down-conversion happens natively in `fldigi_mod.c`.
- `FldigiControlBridge.cs` — **done**: duplex CTL pipe; sends `set_freq` /
  `set_mode` / `set_ptt` / `ping`, parses the 250 ms `status` JSON, subscribes
  `Console.MoxChangeHandlers` + `Console.VFOAFrequencyChangeHandlers`, forces
  DIGU once per session.
- `FldigiTxInject.cs` — **folded into** `audio.cs` (`fldigi_active_this_over`
  per-over gate mirroring the RADE arbiter) plus `fldigi_mod.c` native TX tap;
  RADE takes precedence if both are armed.
- Setup tab "FLDIGI" (`setup.cs` / `setup.designer.cs`): `tpFLDIGI` tab with
  `chkFLDIGI`, `chkFLDIGIAutoStart`, `txtFLDIGIPath`; persisted by the generic
  Options auto-walk; `ShowSetupTab(SetupTab.FLDIGI_Tab)` supported.
- App exit: `Thetis.FLDIGI.FldigiManager.Shutdown()` called from
  `Console.ExitConsole()`.
- First-run config seed (write minimal `fldigi_def.xml` / `fldigi.conf` into
  `%APPDATA%\SDR-VST3\fldigi`) — still pending.

### Control-mapping table (route through TCI handlers)
| Event (which side) | Action |
|---|---|
| Thetis VFO/freq change | push `rx_freq`/`vfo` to fldigi |
| fldigi UI mode change | notify Thetis, set mode via `handleModulationMessage` path |
| Thetis RX filter change | push filter via `handleRxFilterBand` path |
| PTT engage/release | push `ptt` both ways; gate TX audio flush |
| RX2 (optional) | map VFO B freq; else leave unsupported in Tier 2 |

### Explicit Tier 2 non-goals
- No text sync into Thetis windows.
- No Thetis macro panel.
- No AF-waterfall re-home.
- No RX2-on-VFO-B unless cheap.

## 7. Installer (P4)

`Project Files/Source/Thetis-Installer/Product.wxs`:
- New `Directory Id="FldigiDir"` under `INSTALLFOLDER` + `ComponentGroup Id="FldigiComponents"`
  with one `Component`/`File` per payload file; versioned KeyPath so MSI minor
  upgrades replace cleanly on fldigi version bump.
- Add `File Id="LicFldigi"` (GPL RTF) to `LicensesComponents`.
- No firewall rules (pipes only), no Run key, no VC-redist (static CRT), no
  driver/registry/admin.
- Typical payload after static linking ≈ a few MB to ~10 MB.

Reference structure (existing):
- `Thetis-Installer/Product.wxs:81-149` — Feature/ComponentGroupRef layout to copy.

## 8. Phases, effort, and gates

| Phase | Work | Effort (agent) | Gate |
|---|---|---|---|
| P1 | fldigi fork: SndPipe + control pipe + build | hours (build is the unknown term) | ✅ decode from recorded file offline — **DONE** |
| P2 | Thetis bridge: spawn, pipes, resample, PTT exclusion | hours | **code-complete, builds clean 2026-09-12** — on-air RX/TX round trip still to verify |
| P3 | control sync + Setup tab + config seed | hours | Setup tab + freq/mode/PTT sync **built**; mode/freq/filter round-trip verification + config seed remain |
| P4 | installer + license + version rules | ~1 hour | clean MSI install/upgrade/uninstall |

Total ≈ 1 working day of agent time; first-time MinGW build friction is the only
unbounded item.

## 9. Risks / notes

- fldigi single-instance lock → guard two Thetis runs.
- Audio latency/backpressure at PTT edges (flush TX pipe before unkey).
- fldigi `auto_save` can overwrite seeded config → control bridge re-applies.
- GPL: any forked fldigi source added to the repo must keep upstream copyright/
  license notices; add repo NOTICE entry.
- Observed repo build environment: MSVC (VS) builds the main solution via
  `Thetis_VS2026.sln`; the fldigi fork build is MinGW and is a separate, doc-
  tracked deliverable (not tied to the .sln).
- Unrelated-but-done in this lineage (do not redo): GPU mesh/compute/overlay
  defaulted to ON, persisted, and Force-CPU forces all GPU features to CPU mode
  (verified build EXIT=0; 5 files, +52/−10).

## 10. Next step

**P1 is complete** (fldigi.exe in the forked tree passes phase-1 transport +
mode switch + offline decode gate; see §5.1–5.2 and
`HANDOFF_FLDIGI_P1.md`). Next is **P2**: the Thetis-side bridge
(`FldigiManager`/`FldigiAudioBridge` under `Project Files/Source/Console/FLDIGI/`)
using the async client pattern of §5.2, feeding the existing `pipe.c` taps and
RADE TX path.

**Parallel track (WSJT-X FT8 sidecar):** completed P1 using the same sidecar pattern
(server-owned pipe, 48k→8k WDSP resample, RADE mutual exclusion, `WsjtManager` spawn
lifecycle). See `HANDOFF_FT8_P1.md` for the full walkthrough. P1 sidecar shipped as
ready-made binaries in `Project Files/bin/x64/Release/WsjtX/`.