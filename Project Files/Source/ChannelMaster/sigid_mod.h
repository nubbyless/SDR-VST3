/*  sigid_mod.h

    Native RX audio tap for the Digi Id (signal identification) feature.

    The tap is placed in the WDSP pipe (see pipe.c, xpipe()) next to the
    fldigi and WSJT-X taps.  That position matters: the VST3 RX chain is
    applied later, in the ChannelMaster mixer (aamix.c), so audio taken here
    has NOT been through any VST plugin.  A VST equaliser, limiter or delay
    would otherwise destroy the signature the analyser is trying to measure.

    The receiver audio (interleaved stereo doubles at rcvr[].ch_outrate) is
    de-interleaved to mono, downsampled to SIGID_MODEM_RATE with the WDSP
    resampler and pushed into a per-receiver FIFO.  The managed SigIdAnalyzer
    drains it with SigidDrainRx() and does the feature extraction.

    Copyright (C) 2026  SDR-VST3 Thetis integration (Digi Id)

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

#ifndef _sigid_mod_h
#define _sigid_mod_h

#ifndef PORT
#define PORT __declspec(dllexport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define SIGID_NRX 2   /* RX1 + RX2 */

/* ============================================================
 * Lifecycle -- called from create_pipe() / destroy_pipe().
 * ============================================================ */
void create_sigid(void);
void destroy_sigid(void);

/* ============================================================
 * Public C# API (PORT-exported from ChannelMaster.dll).
 * ============================================================ */

/* RX master enable, per receiver. */
PORT void SigidSetRxEnable(int rx, int enable);
PORT int  SigidGetRxEnable(int rx);

/* Transport FIFO.  The managed SigIdAnalyzer drains the downsampled RX
 * audio (mono float, SIGID_MODEM_RATE Hz) from here. */
PORT int  SigidDrainRx(int rx, float* out, int maxCount);  /* returns # floats drained */
PORT void SigidFlushRx(int rx);                              /* drop any queued audio */

/* Diagnostics. */
PORT int  SigidRxFifoLevel(int rx);       /* samples currently queued */
PORT long SigidRxOverrunCount(int rx);    /* times the FIFO overflowed */
PORT int  SigidRxRate(void);              /* the analysis rate in Hz */
PORT long SigidRxBlockCount(int rx);      /* hot path invocations */
PORT long SigidRxSampleCount(int rx);     /* samples produced by the resampler */
PORT long SigidRxZeroCount(int rx);       /* hot path calls that produced nothing */

/* ============================================================
 * Self test.  These let the managed code push a synthetic signal through the
 * very same de-interleave -> resample -> FIFO chain that the live tap uses,
 * so the DSP path can be verified without a radio being connected.
 * ============================================================ */
PORT void SigidCreate(void);              /* create the module (idempotent) */
PORT void SigidDestroy(void);             /* tear the module down */

/* Queues one block of interleaved stereo doubles at inRate and returns the
 * number of analysis-rate samples produced. */
PORT int  SigidProcessRxBlock(int rx, const double* interleaved, int frames, int inRate);

/* ============================================================
 * Hot-path entry point called from xpipe() in pipe.c.
 * ============================================================ */
void xsigid_rx(int rx, double* rbuff_io);

#ifdef __cplusplus
}
#endif

#endif /* _sigid_mod_h */
