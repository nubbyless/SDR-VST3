What is SDR-VST3?
SDR-VST3 is a fork of Thetis (the OpenHPSDR software-defined radio console application) that adds built-in VST3 audio plugin support to ham radio operations. It allows operators to insert professional audio plugins (EQs, compressors, gates, limiters, noise reduction, etc.) into both the RX (receive) and TX (transmit) signal chains — similar to how a DAW works, but for live radio signal processing.
Originally called "Thetis Plus", it was rebranded to SDR-VST3 in v4.1 (at the suggestion of Thetis maintainer Richie MW0LGE) to run as a fully independent, side-by-side installation alongside standard Thetis.

Key changes in this release:
- New name, new look — The fork is now called "SDR-VST3" with updated splash screen, app icon, and installer artwork.
- 
- It now Installs side-by-side — It installs as its own separate program, so it won't overwrite or conflict with an existing Thetis installation. You can run both. (Understand there's No guarantees here that i didn't miss something but in my own testing everything works separately as it should)
-  
- Separate settings — It keeps its own data folder (SDR-VST3-x64), so your VST chains and settings don't get mixed up with the original Thetis.
- 
- Fresh version numbering — Versioning now starts clean at v4.1, so it's easy to tell releases apart.
Install the MSI like any other Windows program — the installer will walk you through it.
-
-Since this is based on Thetis you can import your current database from your Thetis v2.10.3.15 if you like after the restart it will complain because of version conflicts with the new version system its ok allow it to update and you'll be all set.
-
-All future releases will install independent of Thetis so as to avoid conflicts.  


This version of thetis has a built in vst plugin system it can only use vst3 plugins it can not use .vst or .vst2 plugins
the plugin system is enabled by default, no shortcut flag is needed
it is only for 64bit systems there is no x86 32 bit version.


Special Thanks to chasingcoffee his hard work is what made this vst version possible  
Forked from his original at his github page 
https://github.com/ChasingCoffee/Thetis/tree/vst-support


Credits — RADE / FreeDV digital voice
The RADE (Radio AutoEncoder) digital-voice modem and FreeDV integration was ported from Christos Nikolaou's (SV1EIA) Thetis-RADE fork:
https://github.com/sv1eia/Thetis-RADE

Special thanks to:
- Christos Nikolaou (SV1EIA) <sv1eia@gmail.com> — the Thetis-RADE fork and C port of the RADE modem this feature is built on
- Peter B Marks — radae_nopy, the reference implementation the RADE port was made from
- David Rowe & Jean-Marc Valin — original authors of RADE (Radio AutoEncoder)
- The FreeDV project (David Rowe / drowe67) — FreeDV-GUI's rade_text reliable-text codec and the codec2 library used for EOO callsign frames

The RADE DSP dependencies vendored under Project Files/lib/ keep their own licences (BSD-2-Clause / BSD-3-Clause / MIT / LGPL-2.1); see the commit_pin.txt and LICENSE / NOTICE files in each lib/<vendor> directory.


Changelog
VST2 Support Removed
Removed VST2 plugin support entirely to avoid potential licensing and legal issues. It now exclusively supports VST3 plugins.

Scanner Improvements
- Added detailed progress output during plugin scanning, including per-plugin timing and probe status (resolved via moduleinfo.json vs probing out-of-process)
- Added WAVESHELL support — the scanner can now probe WAVESHell VST3 bundles without freezing, handling their slow plugin enumeration and problematic cleanup routines

RX/TX VST Bypass
Added front panel buttons to bypass RX VST and TX VST processing independently right clicking them will bring you directly into the chain host window



Release 4
 v2.10.3.15-4
 
In this version of Thetis plus with built in vst host we have added meter gadgets that can load a list of your active plugins for easy access from the front console just click the plugin and the editor instantly opens right from your console you can setup a container for rx and one for tx or load them both in one container however you choose.

Also included in this version are the opensource linux studio plugins to give you something to start with right out of the box there are much better paid plugins available but these are free and can get you going right away they include various eqs compressors gates dynamics limiters etc.. a full package of plugins




Version 4.1 Rebrand to avoid confusion
 V4.1

After a brief discussion with Richie [MW0LGE] who maintains the last version of Thetis v2.10.3.15 which this software fork is based on
He suggested a rebrand to keep versions from becoming confusing among other issues.
He also suggested it should be its own install databases etc. as to be able to run side by side with Thetis and not overwrite it.


Key changes in this release:

New name, new look — The fork is now called "SDR-VST3" with updated splash screen, app icon, and installer artwork.
It now Installs side-by-side — It installs as its own separate program, so it won't overwrite or conflict with an existing Thetis installation. You can run both. (Understand there's No guarantees here that i didn't miss something but in my own testing everything works separately as it should)
Separate settings — It keeps its own data folder (SDR-VST3-x64), so your VST chains and settings don't get mixed up with the original Thetis.
Fresh version numbering — Versioning now starts clean at v4.1, so it's easy to tell releases apart.
Install the MSI like any other Windows program — the installer will walk you through it.




 V4.2 all new user interface

 
Big shoutout to Ben Shapiro aka Chasing coffee for the new rack user interface and the mini rack user interface for the meter gadgets

VST Rack Gadgets

Added new TX and RX VST Plugin gadgets to the meter system. Instead of the old plain text plugin list, each gadget now shows the interactive rack: one row per plugin with artwork, a status light, and buttons to enable/disable, bypass/unbypass, open the editor, remove, or reorder plugins with a drag. A chain that's bypassed dims the whole rack.
Gadgets are added from Setup, and persist across restarts, and can be floated into their own window and placed anywhere (including another monitor) using the float button that appears when you hover over the top right corner.
Bypass & Enable sync

Enabling or bypassing a plugin (or the whole chain) now stays in sync everywhere at once: the rack gadgets, the chain manager, and the TX/RX VST bypass buttons on the main screen all update together, no matter which one you use.
Cleaner help

Hovering over rack controls now shows a short tooltip explaining what each button does (e.g. "Bypass plugin",



V4.3 updated to wdsp 2.00
 
What's new in this build

Tonight we upgraded the engine that powers SDR-VST3 — the digital signal processing core that does the heavy lifting for receiving, filtering, and noise reduction.

This newer engine (WDSP 2.00) modernizes the foundation underneath features you already use, including noise reduction, the audio equalizer/compressor, the spectrum display, and PureSignal's automatic transmit-signal correction.

We also:

Removed the need for the -vst flag to start in vst mode vst mode is the default now so no need to modify the shortcut any longer
Fixed a startup crash that could affect people with existing saved settings — your old profiles now load cleanly.
Fixed the Phase Rotator panel on the CFC tab, which was slightly too small and was clipping its new controls.
Confirmed SDR-VST3 is fully self-contained. It keeps its own settings folder, saved data, and even its own recording and firewall entries, so you can install and run it side-by-side with a normal Thetis installation without the two mixing up or overwriting each other's settings.

No need to start over — if you already have SDR-VST3 installed, your existing profiles and tuned-up radio performance carry straight over.
Thanks to Yurij-eu2av for the WDSP 2.00 integration work, and to ChasingCoffee for the original VST3 host support, which this project builds on.
It is not 100% Required to rebuild your wisdom file but if you know how you should its as simple as deleting it and when you start up again it will build a new one.
It is usually located at C:\Users"your username"\AppData\Roaming\OpenHPSDR\SDR-VST3-x64


V4.4 VST Routing for TCI and VAC Audio Visual changes
  
VST Routing for TCI and VAC Audio
You can now decide whether audio going to/from your virtual sound cards (VAC) and TCI software passes through Thetis's VST effects (EQ, compressors, etc.) or stays "raw" and untouched.

On the VAC audio options tab — Apply RX VST and Apply TX VST checkboxes for each VAC send your receive or transmit audio through the VST chains instead of the raw audio.
On the TCI network tab — Apply RX VST and Apply TX VST checkboxes do the same for audio going to/from TCI-connected software.
Each one is a simple checkbox, defaulting to off, so your sound stays exactly as it is today until you turn it on. Use them when you want software to hear your processed audio rather than the dry input.
Visual changes

The on-screen grid lines
Fresh installs of SDR-VST3 now start with the "Display Major Grid" and "Display Minor Grid" lines turned off by default, so the screen looks cleaner out of the box.
existing users settings are left completely alone. If you already had the grids on, they stay on. Only brand-new installs see them off.
You can still turn the grids on/off any time in the Setup → Appearance → RX Display screen.

New skin
New installs use the new SDRVST3 Skin automatically.
If you are upgrading from an older version you will get a one time popup after install of the new version.



V4.5 Transmit profile organizer and 3d panadapter

What's new in this version:

3D Spectrum Display — Your panadapter now has a retro-future 3D waterfall view with stacked perspective traces You can adjust perspective, depth, ridge height, speed, and atmospheric haze to taste.
Waterfall Color Sync option — The 3D display automatically uses your waterfall's color scheme, so everything looks cohesive.
3D Spectrum can also use color of your choosing or use the standard Thetis gradient.
WDSP 2.00 Engine — Upgraded the underlying DSP engine for better performance.
TX Profile Reordering — Cleaner transmit profile management.
Please Note:
The 3d spectrum display is still a work in progress if your experience any DirectX related crashes let me know and if possible provide a screenshot of the error.



V4.6 .NET 10 NEW GRAPHICS ENGINES

What's New in this version

Modernized Foundation (Still a work in progress)

The application has been upgraded to the latest .NET 10 platform — bringing faster startup, better performance, improved security, and a future-proof base for continued development.

The entire graphics system has been migrated from an outdated, no-longer-maintained DirectX library (SlimDX) to the modern, actively developed Vortice framework. All display drawing — panadapter, waterfall, meters, and scopes — now runs through this updated pipeline.

A built-in safety net automatically falls back from GPU-accelerated to CPU-based drawing if a graphics driver problem is detected, so the display keeps working on problematic systems.

Internal settings storage has been moved to a cleaner, more reliable modern format.

Numerous low-level memory-safety improvements make crashes caused by the old code far less likely.

-Redesigned 3D Panadapter
Look and accuracy

The 3D "waterfall history" display now uses proper perspective, ridges shrink and fade into the distance as they move back in time, giving a much stronger sense of depth.

The live trace at the front is drawn to exactly the same scale as the 3D surface behind it, so signals no longer appear twice or "jump up" between the trace and the history.

Hills and peaks are shaded by steepness — sharp signals catch the light, flat noise stays subdued — making the terrain easier to read at a glance.
New control

Floor Lift: raises the noise floor up into view so weak signals sitting just above the noise become visible instead of being buried flat against the bottom. Defaults to 0.90.
Colors

Waterfall Sync now reliably overrides every color option (colormaps, gradients, line colors), so the 3D surface always matches your waterfall palette when enabled.
Performance

-Faster, smoother display thanks to your graphics card
Parts of the spectrum display that were previously drawn piece-by-piece by your computer's main processor are now rendered by the graphics card (GPU) in one go.
This is an optional experiment — tick the "GPU mesh (exp.)" box in Setup → Display to try it. Everything looks the same as before; it's just lighter on your PC.

Waterfall: now scrolls via the graphics card instead of being shuffled around in memory.

Spectrum fill: the shaded area under the signal trace is now drawn as a single graphics-card operation instead of hundreds of individual pencil strokes.

3D history surface: view is also GPU-accelerated when in experimental mode.

-Neon glow for the signal trace
The live spectrum trace can now have a soft neon-style glow around it. Turn it on under the Appearance/RX Display tab the strength of the blur is adjustable with the data line slider.
It's rendered efficiently on the graphics card, and switches itself off automatically on machines where it would be too slow.

Safety Net
If the graphics path hits any problem — driver hiccups, window resizing, unsupported hardware — the display instantly falls back to the original drawing method.
You should never see a stuck or missing display; worst case, it simply behaves exactly like it did before this update.




v4.7 Latest GPU compute shaders dual zoom controls

What's New — v4.7.0

Independent RX1/RX2 pan and zoom controls — separate pan sliders, zoom sliders, center buttons, zoom-to-band presets, and recenter button for each receiver
-Automatic update checker offering download of new version if available

GPU Compute Shaders Waterfall color conversion moved to GPU compute shader — dBm-to-BGRA lookup runs entirely on the graphics card via HLSL, eliminating per-row CPU color math experimental for now enable gpu dhsders under Display/General DirectX setting best performance will be seen with both this and mesh enabled

Spectrum normalization moved to GPU compute shader — peak-height calculation for panadapter fill runs on the GPU instead of the CPU column loop

Both pipelines have full CPU fallback — if GPU compute fails, the existing CPU paths take over automatically

Pipeline uses Texture2D staging with CopySubresourceRegion for reliable GPU↔CPU data transfer (works on AMD and NVIDIA)

LUT is uploaded once per color-scheme/threshold change, not per frame
GPU Fallback Architecture

Graceful HW → WARP → CPU rendering chain on device loss or driver errors

Runtime GPU diagnostic log toggle (Options → options3→ Diagnostics → "Log GPU mesh events")

Error dialog capture logging — DX init errors, startup exceptions, and dialog text all written to ErrorLog.txt with version, render path, and stack trace

-Crash Safety Net
Unhandled exception handler catches and logs crashes with full stack trace to ErrorLog.txt before exit
Display Improvements

Waterfall gradient LUT upgraded from 101 to 1024 steps — eliminates color banding on Custom schemes

CI release pipeline



<br><br>

See LICENSE and LICENSE-DUAL-LICENSING for licensing details.
