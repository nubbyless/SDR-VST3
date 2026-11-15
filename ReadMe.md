# SDR-VST3

**The OpenHPSDR Thetis console for Apache Labs radios — with built-in VST3 audio plugin hosting, embedded WSJT-X / fldigi / RADE digital modems, a DX Cluster and scanner, the latest WDSP 2.11 DSP engine, and a modernised .NET 10 + Vortice graphics pipeline.**

---

## What is SDR-VST3?

SDR-VST3 is a fork of [Thetis](https://github.com/ramdor/Thetis) — the OpenHPSDR software-defined radio console used with Apache Labs radios. Where Thetis gives you the radio, SDR-VST3 adds the things you actually wanted from a DAW, a contest logger and a digital modem all in one window.

Three things make it different:

1. **Built-in VST3 plugin hosting.** Professional audio plugins (EQs, compressors, gates, dynamics processors, limiters, noise reduction) slot into both the **receive (RX)** and **transmit (TX)** signal chains — like a DAW, but for live radio. Bring your own VST3 plugins, build a chain per radio profile, and share chains with other operators.
2. **Embedded digital modems.** **WSJT-X** (FT8/FT4/WSPR), **fldigi** (RTTY, PSK31, Olivia, MT63, MFSK…) and **RADE / FreeDV** digital voice ship inside the installer and talk to the radio over internal named pipes. No virtual audio cable, no serial CAT rig, no Hamlib, nothing exposed on the network.
3. **The latest WDSP 2.11 DSP engine, on a modern platform.** The Wideband DSP core that does the heavy lifting for receiving, filtering, noise reduction, the spectrum display and PureSignal's transmit-signal correction is kept right up to date — currently **WDSP 2.11**. On top of that, the app runs on **.NET 10** with the graphics stack migrated from the abandoned SlimDX to the actively developed **Vortice** framework, plus optional GPU-accelerated spectrum/waterfall rendering with automatic CPU fallback.

Originally called "Thetis Plus", it was rebranded to **SDR-VST3** in v4.1 at the suggestion of Thetis maintainer Richie MW0LGE, so it can run as a fully independent, side-by-side installation next to standard Thetis.

> **VST3 only.** VST2 (`.vst` / `.vst2`) support was removed entirely to avoid licensing and legal risk. 64-bit only — there is no x86 build.

---

## Why a fork?

Thetis is an excellent radio console, but it is deliberately conservative: it is one console, one radio, one chain of DSP. SDR-VST3 keeps everything Thetis does and adds the parts that ham operators kept bolting on top of it with virtual cables and serial ports.

**It installs side by side.** Its own program entry, its own splash screen and artwork, its own database, its own settings folder (`…\AppData\Roaming\OpenHPSDR\SDR-VST3-x64`), its own recording and firewall entries. Run it and Thetis at the same time — neither overwrites the other.

**It never touches your Thetis database.** VST chains and a lot of the new state live in JSON files, not in the database, so upgrades carry your profiles and tuned radio performance straight across.

---

## Feature tour

### ⚙ WDSP 2.11 — the engine under it all

SDR-VST3 runs the most up-to-date **WDSP (Wideband DSP) 2.11** engine — the digital signal processing core that does the heavy lifting for receiving, filtering, noise reduction, the audio equaliser/compressor, the spectrum display, and PureSignal's automatic transmit-signal correction.

- **Kept current, not frozen.** The engine was first jumped to WDSP 2.00 to modernise the foundation underneath features you already use, and is now on **WDSP 2.11** — the latest release, which also brought the new NR5 neural noise reduction.
- **Integration by Yurij-eu2av**, who did the WDSP 2.00 work this fork builds on.
- **NR5 — built-in neural network noise reduction.** A new fifth noise-reduction algorithm with its own model architecture, selectable per receiver from the **NR** button on the main screen (cycles *Off → NR1 → NR2 → NR3 → NR4 → NR5 → Off*) or straight from the DSP menu. RX1 and RX2 each get their own independent section under `Setup → DSP → NR5`, with:
  - **Model (0–3)** — a choice of built-in neural networks; model 1 is the default larger general-purpose model, the others are smaller general or alternative models trained with more context (useful for weaker or CW-style signals)
  - **Mask floor (dB)** — the lowest gain the reducer may apply. Default −25 dB
  - **Max gain (dB)** — the most the processor may boost a signal above the noise estimate. Default 12 dB
  - **Alpha** and **Alpha knee (dB)** — shape how aggressively low-level signals below the knee are treated. Defaults 1 and 10 dB
  - **Tau (s)** — how quickly the noise estimate adapts. Default 2 s
  - **Smooth attack / Smooth release (ms)** — how fast the reduction opens and closes. Defaults 0 ms
  - **Output audio in both I and Q** — keep audio in I with Q zeroed (default), or put it in both channels
  - **Defaults** — resets that receiver's NR5 controls in one click
- **Remote controllable.** NR5 is accepted through the existing CAT (`ZZNE` / `ZZNF`) and TCI noise-reduction commands, so it can be driven from your logging software.

> Because WDSP is the shared foundation, upgrading the engine improves noise reduction, the audio EQ/compressor, the spectrum display and PureSignal all at once.

### 🎛 VST3 plugin hosting — the headline feature

- **Independent RX and TX chains.** Each has its own plugin list, its own state, its own bypass.
- **Chain manager window** with per-plugin **enable/disable** and **bypass/unbypass**.
- **Main-screen bypass buttons** (`TXVST` / `RXVST`) to kill processing instantly — right-click to jump straight into the chain host.
- **VST rack gadgets** on the console — an interactive row per plugin with artwork, a status light, enable/bypass, open-editor, remove and drag-to-reorder. A bypassed chain dims the whole rack.
- **Bypass and enable stay in sync everywhere at once** — rack gadget, chain manager and main-screen buttons always agree, whichever one you touch.
- **Chain import / export.** Export both the RX and TX chains in a single file and share them. If a plugin is missing on an imported chain you are told, the missing slot is greyed out, and you can install it or drop it from the chain.
- **Per-profile chains.** Every Thetis profile keeps its own RX/TX chain, saved as JSON. The *Save Profile* button in the TX tab saves the chain state along with the transmit profile.
- **Route the processed audio out to software.** `Apply RX VST` / `Apply TX VST` checkboxes on the VAC audio tab and the TCI network tab let virtual sound cards and TCI-connected software hear your EQ'd, compressed audio instead of the dry input. Both default to **off**, so nothing changes until you opt in.
- **Plugin scanner** with detailed progress output, per-plugin probe timing, and **WAVESHell** support (probes up to 2048 plugin classes without freezing).
- **A bundle of free open-source plugins ships with the install** — EQs, compressors, gates, dynamics and limiters — so the chains work out of the box before you buy anything.

### 📊 Meter & gadget system

- **VST rack gadgets** for both RX and TX, added from Setup, persistent across restarts, and **floatable** into their own window on any monitor.
- **Custom meter faces.** The cross-needle and multimeter analog meters can each take their own dial image — import `.png`, `.jpg`, `.jpeg` or `.bmp`. Imported images are copied to a safe appdata folder so updates never overwrite them, each gadget remembers its own choice across restarts, and *(Default skin)* puts the normal face back.
- **Frequency Analyzer gadget** (new in v6.3).
- **RADE meters** — sync lock, SNR (dB), RX level (dBFS), clip indicator, EOO decodes, TX mic level and TX mic clip.
- FreeDV / RADE mode buttons available in the gadget mode list.
- Hermes Lite 2 scales for the cross-needle and multimeter gadgets (HL2 build).

### 📻 Digital modes — embedded, with no plumbing

- **WSJT-X** (FT8 / FT4 / WSPR) — bundled and launched straight from the console menu.
- **fldigi** (RTTY, PSK31, Olivia, MT63, MFSK, Contestia, dominoEX, Feld-Hell, CW) — bundled and launched from the console menu.
- Both connect over **two named pipes**: an **audio pipe** (transmit audio flows straight into the transmitter path, received audio flows back out for decoding, at the radio's exact sample rate) and a **control pipe** (PTT, mode changes, frequency moves pass between the two automatically). No virtual sound card, no serial port, no rig-control port opened, and because both pipes are process-internal nothing is exposed on the network.
- You do not configure the modem side. Callsign and grid square for WSJT-X; rig settings, soundcard and clock for fldigi — all set once by SDR-VST3. It even stops fldigi and the rig-control port fighting over the radio.
- **RADE / FreeDV digital voice**, ported from the Thetis-RADE fork, with **FreeDV Reporter** integration (`qso.freedv.org`) — callsign and Maidenhead grid with input cleaning, a short status message, and optional publishing of your frequency, transmit state and live SNR to the live station map. RX1 and RX2 each get their own reporting channel.
- RADE settings (enabled, mic level, RX levels, mic EQ, AGC target, noise reduction) are stored **per TX profile**.
- **Digital modem audio bypasses your mic effects automatically.** FT8 and fldigi no longer get mangled by your EQ, compression and other mic effects — the console skips them while a modem is keyed.

### ⚡ PureSignal 3.0

Transmit-signal linearisation that measures your amplifier instead of guessing.

- **Single Cal** — one measurement. Pick a stimulus, press the button, it keys up, measures, applies the correction and stops. The correction **stays applied** until you switch PureSignal off.
- **PS-A (auto-calibrate)** — continuous re-measure and re-correct, so it keeps up with the amplifier as it warms up, the band changes and the power changes. Works only while you are transmitting.
- **Noise stimulus** (recommended — the closest analogue to speech) or **Two-Tone** (repeatable numbers you can compare before and after a change).
- **PS Correction** — *Strict* (default) / *Medium* / *Relaxed*, how hard it pushes the correction.
- **AmpView** shows the measured gain curve and the correction curve, with save/restore.
- **Console indicators** — a colour-coded *Feedback* readout (green just right, yellow close, blue too much, red too little) and a *Pure Signal / Correcting* light.
- Couples with the **TX step attenuator**, walking the ATT to hold the feedback level at the program target so the correction stays valid.

> PureSignal 3.0 is under active development — many fixes landed in v6.3.1 and more are on the way.

### 🛰 DX Cluster & SWL mode

- **DX Cluster terminal window** — live traffic to and from each cluster node, type commands directly, switch nodes, and save the log.
- **Per-node log view and node selector**, and **optional per-node passwords** that are sent automatically when the cluster asks.
- **SWL Mode** (Shortwave Listening) — flip it on and the spot list stops showing ham spots and starts showing shortwave broadcast stations that are on the air *right now*. Station data comes from the public EiBi schedule; the app works out who is transmitting this minute and lists them with country flag, country name and a **Language** column.
- SWL Mode hides the ham-only clutter without disconnecting anything — your cluster connections stay alive in the background, so flipping back is instant.
- **Spots on Panadapter** toggle.
- Cluster login supports an optional password beside your callsign.

### 🔍 Scanner

Built into the console, three scan modes:

- **Memory Group scan** — step through every memory in a group of your choosing.
- **Bandstack scan** — sweep the current band's saved bandstack entries.
- **Custom Scanner List** — load any plain-text list (up to 100 entries); hover the load button for a tooltip describing the format.

Ships with an **HFGCS list** — 100 military HF frequencies (the global communications system "hot hundred" used by military aircraft, carriers and their ground stations), all USB, 3–24 MHz — installed to the right folder so the Custom Scanner works straight out of the box.

### 🖥 Spectrum, waterfall & graphics

- **3D panadapter** — a retro-future 3D waterfall-history view with proper perspective: ridges shrink and fade into the distance, hills and peaks are shaded by steepness so sharp signals catch the light and flat noise stays subdued. Adjustable perspective, depth, ridge height, speed and atmospheric haze.
- **Floor Lift** raises the noise floor into view so weak signals just above it are visible instead of buried flat against the bottom.
- The live trace is drawn at exactly the same scale as the 3D surface behind it, so signals no longer appear twice or jump between trace and history.
- **Waterfall Colour Sync** — the 3D view follows your waterfall palette automatically.
- **GPU acceleration (experimental)** in `Setup → Display`: *GPU mesh*, *GPU compute shaders* (waterfall dBm→BGRA colour lookup and spectrum normalisation moved into HLSL on the GPU) and *GPU overlay* for peak-hold lines, which used to cost roughly 7,600 separate CPU draw commands per second per receiver. Spectrum fill and waterfall scrolling are now single GPU operations instead of hundreds of CPU strokes.
- **Graceful fallback** — a hardware → WARP → CPU rendering chain takes over on device loss or driver errors. If anything goes wrong with the GPU path the display instantly reverts to the old drawing method; you should never see a stuck or missing display.
- **Neon glow** on the live spectrum trace, with adjustable blur, auto-disabled on machines where it would be too slow.
- **1024-step waterfall gradient LUT** — eliminates colour banding on custom schemes.
- **Independent RX1 / RX2 pan and zoom** — separate pan sliders, zoom sliders, centre buttons, zoom-to-band presets and a recenter button for each receiver, and separate 3D display history so the two receivers never interfere with each other's trace.
- Wider bandstack window, tidier setup layouts, a fresh **SDRVST3 skin**, and cleaner default grid lines on new installs.

### 🎙 Audio, streaming & courtesy tones

- **Audio Streaming tab** (`Setup → Audio → Streaming`) — tap a copy of your **RX** and/or **TX** audio to any Windows output device. Each stream has its own on/off, device dropdown, gain (−20 … +40 dB) and live level meter, plus a status box showing the rate. Perfect for OBS: install a virtual cable, point the stream at it, add an Audio Output Capture in OBS. It is a headphone splitter for OBS.
- **Courtesy tones** — play a `.wav` on TX start, and another on TX release. The release tone holds your transmitter keyed until it has fully played, without disturbing your MOX state. Pick the files, set their volumes individually, and preview them.
- **TX profile management** with reordering, per-profile VST chains and per-profile RADE state.
- **Discord integration** built into the console — connect, read and send messages, and jump to the server invite from the app.
- Diversity/Phasing window now reopens at the size you left it.
- cmASIO driver fixed to list all ASIO devices.
- The RADE/FreeDV toggle has its own button on the main radio screen; right-click it to jump straight to the RADE settings.

### 🛡 Reliability, diagnostics & tooling

- **Automatic update checker** — offers the new version when one is available.
- **Crash safety net** — unhandled exceptions are caught and logged with a full stack trace to `ErrorLog.txt` before exit.
- **Error dialog capture logging** — DX init errors, startup exceptions and dialog text are written to `ErrorLog.txt` with version, render path and stack trace.
- **Runtime GPU diagnostic log** toggle at `Options → options3 → Diagnostics → "Log GPU mesh events"`.
- Large memory-safety clean-ups over the inherited code, and a migration of internal settings storage to a cleaner modern format.

---

## Which build do I want?

| Build | Radio |
|---|---|
| **[SDR-VST3](https://github.com/nubbyless/SDR-VST3)** | Apache Labs ANAN radios |
| **[SDR-VST3-HL2](https://github.com/nubbyless/SDR-VST3-HL2)** | Hermes Lite 2 — with HL2 watt meters, HL2 meter scales, IO board and amplifier switching, and the auto-attenuator fixes. Installs side by side with the ANAN build *and* with Thetis |

---

## Requirements

- **Windows, 64-bit only.** There is no 32-bit build.
- **.NET 10 Desktop Runtime (x64).** The installer checks for it and tells you where to get it if it is missing: <https://dotnet.microsoft.com/download/dotnet/10.0>
- A supported radio — Apache Labs ANAN or Hermes Lite 2 depending on your build.

## Installing

1. Download the `.msi` from the [Releases page](https://github.com/nubbyless/SDR-VST3/releases).
2. Run it. It installs like any other Windows program, side by side with Thetis.
3. Launch it from its own shortcut. Your data lives in `…\AppData\Roaming\OpenHPSDR\SDR-VST3-x64`.
4. Installing over an older SDR-VST3 upgrades in place — your profiles and settings carry over.

> Coming from Thetis v2.10.3.15? You can import your existing database. Thetis will complain about the version conflict on first restart — let it update and you are set.

---

## Credits

SDR-VST3 builds on a lot of other people's hard work.

- **[Thetis / OpenHPSDR](https://github.com/ramdor/Thetis)** — Richard Samphire **MW0LGE** and the Thetis team. This project would not exist without them.
- **Ben Shapiro (ChasingCoffee)** — the original VST3 host support that this fork is built on: <https://github.com/ChasingCoffee/Thetis/tree/vst-support>
- **Yurij-eu2av** — WDSP 2.00 integration.
- **Christos Nikolaou SV1EIA** — the Thetis-RADE fork and the C port of the RADE modem: <https://github.com/sv1eia/Thetis-RADE>
- **Peter B Marks** — `radae_nopy`, the reference implementation the RADE port was made from.
- **David Rowe & Jean-Marc Valin** — original authors of RADE (Radio AutoEncoder).
- **The FreeDV project** (David Rowe / drowe67) — FreeDV-GUI's `rade_text` reliable-text codec and the codec2 library used for EOO callsign frames.
- **dr. warren pratt** — creator of PureSignal, and the author of the operator guidance on choosing the right calibration stimulus.
- The **HFGCS** community — the military HF frequency list that ships with the scanner.
- Everyone who filed a bug report, tested a release and sent a fix.

## Licensing

SDR-VST3 is released under the **GNU General Public License v2.0** — see [LICENSE](LICENSE) and [LICENSE-DUAL-LICENSING](LICENSE-DUAL-LICENSING).

The dual-licensing statement applies only to Richard Samphire MW0LGE's own contributions, which he may also license under closed-source or proprietary terms in addition to the GPL. Code contributed by others remains under its original terms and is unaffected by that statement.

The RADE DSP dependencies vendored under `Project Files/lib/` keep their own licences (BSD-2-Clause / BSD-3-Clause / MIT / LGPL-2.1) — see the `commit_pin.txt` and `LICENSE` / `NOTICE` files in each `lib/<vendor>` directory. The bundled fldigi is GPL-2.0+, which is compatible.

---

## Links

- **Source:** <https://github.com/nubbyless/SDR-VST3>
- **Releases:** <https://github.com/nubbyless/SDR-VST3/releases>
- **Hermes Lite 2 build:** <https://github.com/nubbyless/SDR-VST3-HL2>
- **Upstream Thetis:** <https://github.com/ramdor/Thetis>
- **Issues & bug reports:** <https://github.com/nubbyless/SDR-VST3/issues>

---

*SDR-VST3 is not affiliated with or endorsed by the OpenHPSDR project or by Apache Labs. It is an independent fork. Use it at your own risk, and operate within the amateur radio regulations that apply to you.*
