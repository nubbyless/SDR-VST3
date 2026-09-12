/*  fldigi_mod.h

    Wrapper module that bridges the fldigi HF-digital-modes sidecar into
    Thetis.  The fldigi process is spawned by the managed FldigiManager and
    talks over two named pipes (AUDIO + CTL); this native module provides
    the audio taps so RX audio can move Thetis -> fldigi (modem decode) and
    fldigi TX audio can move fldigi -> Thetis mic path (modem modulate).

    Copyright (C) 2026  SDR-VST3 Thetis integration (fldigi sidecar)

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor,
    Boston, MA  02110-1301  USA
*/

#ifndef _fldigi_mod_h
#define _fldigi_mod_h

/* PORT export macro from cmcomm.h's neighbours; fall back if standalone */
#ifndef PORT
#define PORT __declspec(dllexport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define FLDIGI_NRX 2   /* RX1 + RX2; current Tier-2 bridge uses RX1 only */

/* ============================================================
 * Lifecycle -- called once from create_pipe() / destroy_pipe().
 * ============================================================ */
void create_fldigi(void);
void destroy_fldigi(void);

/* ============================================================
 * Public C# API (PORT-exported from ChannelMaster.dll).
 * ============================================================ */

/* RX master enable, per receiver. */
PORT void SetFldigiRxEnable(int rx, int enable);
PORT int  GetFldigiRxEnable(int rx);

/* TX master enable.  Single -- the TXA chain is single. */
PORT void SetFldigiTxEnable(int enable);
PORT int  GetFldigiTxEnable(void);

/* MOX-state mirror, pushed by audio.cs on every MOX edge (global).
 * Also clears the TX FIFO on the 1 -> 0 edge so no stale modem audio
 * leaks into the next over. */
PORT void SetFldigiMoxState(int mox);

/* Transport FIFOs.  The managed FldigiAudioBridge drains the downsampled
 * RX audio (8 kHz mono float) and writes it into the fldigi AUDIO pipe,
 * and pushes fldigi's returned TX audio (8 kHz mono float) into the TX
 * FIFO for xfldigi_tx to upsample into mic_io. */
PORT int  FldigiDrainRx(int rx, float* out, int maxCount);   /* returns # floats drained */
PORT void FldigiPushTx(const float* in8k, int count);
PORT void FldigiFlush(void);                                  /* clear all FIFOs */

/* ============================================================
 * Hot-path entry points called from xpipe() in pipe.c.
 * ============================================================ */
void xfldigi_rx(int rx, double* rbuff_io);
void xfldigi_tx(double* mic_io);

#ifdef __cplusplus
}
#endif

#endif /* _fldigi_mod_h */