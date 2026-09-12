/*  fldigi_mod.c

    Native audio taps for the fldigi sidecar bridge.

    RX:  xfldigi_rx(rx, buffs[0]) is called from xpipe() on the RX1/RX2
         audio pass.  The receiver audio (interleaved stereo doubles at
         rcvr[].ch_outrate) is de-interleaved to mono, downsampled to the
         fldigi base rate (8000 Hz) with WDSP xresampleFV and pushed into a
         per-receiver FIFO.  FldigiDrainRx() lets the managed bridge pull
         8 kHz samples out for the named-pipe write to fldigi.

    TX:  FldigiPushTx() receives the modem TX audio fldigi wrote on the
         AUDIO pipe (8 kHz mono float).  xfldigi_tx(buff) is called from
         xpipe() on the TX mic pass while keyed (MOX on); it drains the
         8 kHz FIFO, upsamples to the TX input rate and injects it into
         mic_io.  When no modem data is pending the mic_io is silenced so a
         keyed fldigi over never transmits the local mic.

    Copyright (C) 2026  SDR-VST3 Thetis integration (fldigi sidecar)

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.
*/

#include "cmcomm.h"
#include "fldigi_mod.h"

#include <math.h>

extern void* create_resampleFV(int in_rate, int out_rate);
extern void  xresampleFV(float* input, float* output, int numsamps,
                         int* outsamps, void* ptr);
extern void  destroy_resampleFV(void* ptr);

#define FLDIGI_MODEM_RATE  8000
#define FLDIGI_MAX_BLOCK   8192
#define FLDIGI_RX_FIFO_CAP 65536   /* 8 s of 8 kHz mono float per receiver */
#define FLDIGI_TX_FIFO_CAP 65536   /* 8 s of 8 kHz mono float */

/* ============================================================
 * State
 * ============================================================ */

static volatile long g_fl_initialized = 0;

static volatile long g_fl_rx_enabled[FLDIGI_NRX] = { 0 };
static volatile long g_fl_tx_enabled             = 0;
static volatile long g_fl_mox                    = 0;

static CRITICAL_SECTION g_fl_cs;    /* guards both FIFO sets */
static int              g_fl_cs_inited = 0;

static void*            g_fl_rx_resamp[FLDIGI_NRX] = { NULL };
static int              g_fl_rx_outrate[FLDIGI_NRX] = { 0 };
static float*           g_fl_rx_mono[FLDIGI_NRX]   = { NULL };
static float*           g_fl_rx_8k[FLDIGI_NRX]     = { NULL };
static float*           g_fl_rx_fifo[FLDIGI_NRX]   = { NULL };
static int              g_fl_rx_fifo_n[FLDIGI_NRX] = { 0 };
static long             g_fl_rx_ovrun[FLDIGI_NRX]  = { 0 };

static void*            g_fl_tx_resamp = NULL;
static int              g_fl_tx_outrate = 0;
static float*           g_fl_tx_out    = NULL;
static float*           g_fl_tx_fifo   = NULL;
static int              g_fl_tx_fifo_n = 0;
static float*           g_fl_tx_scratch = NULL;
static long             g_fl_tx_ovrun  = 0;

static long             g_fl_rx_block_count[FLDIGI_NRX] = { 0 };
static long             g_fl_tx_block_count = 0;

/* ============================================================
 * FIFO helpers
 * ============================================================ */

static void fl_fifo_push_check(float* buf, int* n, int cap, const float* src,
                               int count, long* counter, const char* tag)
{
    int avail = cap - *n;
    int take  = (count < avail) ? count : avail;
    if (take > 0)
    {
        memcpy(buf + *n, src, take * sizeof(float));
        *n += take;
    }
    if (take < count)
    {
        long c = ++(*counter);
        if (c == 1 || (c % 250) == 0)
        {
            char log[160];
            sprintf_s(log, sizeof(log),
                "[FLDIGI] %s OVRUN dropped=%d total=%ld\n",
                tag, count - take, c);
            OutputDebugStringA(log);
        }
    }
}

static void fl_fifo_pop(float* buf, int* n, float* dst, int count)
{
    if (count <= 0 || count > *n) return;
    memcpy(dst, buf, count * sizeof(float));
    *n -= count;
    if (*n > 0) memmove(buf, buf + count, (*n) * sizeof(float));
}

static void fl_dbg(const char* s)
{
    char log[256];
    sprintf_s(log, sizeof(log), "[FLDIGI] %s\n", s);
    OutputDebugStringA(log);
}

/* ============================================================
 * Lifecycle
 * ============================================================ */

void create_fldigi(void)
{
    if (_InterlockedAnd(&g_fl_initialized, 1)) return;

    int i;
    if (!g_fl_cs_inited)
    {
        InitializeCriticalSectionAndSpinCount(&g_fl_cs, 4000);
        g_fl_cs_inited = 1;
    }

    for (i = 0; i < FLDIGI_NRX; i++)
    {
        g_fl_rx_mono[i] = (float*)calloc(FLDIGI_MAX_BLOCK, sizeof(float));
        g_fl_rx_8k[i]   = (float*)calloc(FLDIGI_MAX_BLOCK, sizeof(float));
        g_fl_rx_fifo[i] = (float*)calloc(FLDIGI_RX_FIFO_CAP, sizeof(float));
        g_fl_rx_fifo_n[i] = 0;
    }
    g_fl_tx_out     = (float*)calloc(FLDIGI_MAX_BLOCK * 8, sizeof(float));
    g_fl_tx_fifo    = (float*)calloc(FLDIGI_TX_FIFO_CAP, sizeof(float));
    g_fl_tx_scratch = (float*)calloc(FLDIGI_MAX_BLOCK, sizeof(float));
    g_fl_tx_fifo_n  = 0;

    _InterlockedExchange(&g_fl_initialized, 1);
    fl_dbg("module created");
}

void destroy_fldigi(void)
{
    _InterlockedExchange(&g_fl_initialized, 0);

    int i;
    for (i = 0; i < FLDIGI_NRX; i++)
    {
        if (g_fl_rx_resamp[i]) { destroy_resampleFV(g_fl_rx_resamp[i]); g_fl_rx_resamp[i] = NULL; }
        free(g_fl_rx_mono[i]);  g_fl_rx_mono[i]  = NULL;
        free(g_fl_rx_8k[i]);    g_fl_rx_8k[i]    = NULL;
        free(g_fl_rx_fifo[i]);  g_fl_rx_fifo[i]  = NULL;
        g_fl_rx_fifo_n[i] = 0;
    }
    if (g_fl_tx_resamp)  { destroy_resampleFV(g_fl_tx_resamp); g_fl_tx_resamp = NULL; }
    free(g_fl_tx_out);      g_fl_tx_out     = NULL;
    free(g_fl_tx_fifo);     g_fl_tx_fifo    = NULL;
    free(g_fl_tx_scratch);  g_fl_tx_scratch = NULL;
    g_fl_tx_fifo_n = 0;

    if (g_fl_cs_inited)
    {
        DeleteCriticalSection(&g_fl_cs);
        g_fl_cs_inited = 0;
    }
    fl_dbg("module destroyed");
}

/* ============================================================
 * Setters
 * ============================================================ */

PORT void SetFldigiRxEnable(int rx, int enable)
{
    if (rx < 0 || rx >= FLDIGI_NRX) return;
    long prev = _InterlockedExchange(&g_fl_rx_enabled[rx], enable ? 1 : 0);
    if (prev && !enable)
    {
        /* Disable edge: drop any stale queued RX audio. */
        if (_InterlockedAnd(&g_fl_initialized, 1) && g_fl_cs_inited)
        {
            EnterCriticalSection(&g_fl_cs);
            g_fl_rx_fifo_n[rx] = 0;
            LeaveCriticalSection(&g_fl_cs);
        }
    }
}

PORT int GetFldigiRxEnable(int rx)
{
    if (rx < 0 || rx >= FLDIGI_NRX) return 0;
    return (int)_InterlockedAnd(&g_fl_rx_enabled[rx], 1);
}

PORT void SetFldigiTxEnable(int enable)
{
    _InterlockedExchange(&g_fl_tx_enabled, enable ? 1 : 0);
    if (_InterlockedAnd(&g_fl_initialized, 1) && g_fl_cs_inited)
    {
        EnterCriticalSection(&g_fl_cs);
        g_fl_tx_fifo_n = 0;
        LeaveCriticalSection(&g_fl_cs);
    }
}

PORT int GetFldigiTxEnable(void)
{
    return (int)_InterlockedAnd(&g_fl_tx_enabled, 1);
}

PORT void SetFldigiMoxState(int mox)
{
    long prev = _InterlockedExchange(&g_fl_mox, mox ? 1 : 0);
    if (prev && !mox)
    {
        /* Un-key edge: clear the TX FIFO so stale modem audio can never
         * leak into the next over. */
        if (_InterlockedAnd(&g_fl_initialized, 1) && g_fl_cs_inited)
        {
            EnterCriticalSection(&g_fl_cs);
            g_fl_tx_fifo_n = 0;
            LeaveCriticalSection(&g_fl_cs);
        }
    }
}

PORT int FldigiDrainRx(int rx, float* out, int maxCount)
{
    if (!_InterlockedAnd(&g_fl_initialized, 1) || rx < 0 || rx >= FLDIGI_NRX)
        return 0;
    if (!out || maxCount <= 0) return 0;
    int have = 0;
    if (g_fl_cs_inited) EnterCriticalSection(&g_fl_cs);
    have = (g_fl_rx_fifo_n[rx] < maxCount) ? g_fl_rx_fifo_n[rx] : maxCount;
    if (have > 0)
    {
        fl_fifo_pop(g_fl_rx_fifo[rx], &g_fl_rx_fifo_n[rx], out, have);
    }
    if (g_fl_cs_inited) LeaveCriticalSection(&g_fl_cs);
    return have;
}

PORT void FldigiPushTx(const float* in8k, int count)
{
    if (!_InterlockedAnd(&g_fl_initialized, 1) || !in8k || count <= 0) return;
    if (g_fl_cs_inited) EnterCriticalSection(&g_fl_cs);
    fl_fifo_push_check(g_fl_tx_fifo, &g_fl_tx_fifo_n, FLDIGI_TX_FIFO_CAP,
                       in8k, count, &g_fl_tx_ovrun, "tx_fifo");
    if (g_fl_cs_inited) LeaveCriticalSection(&g_fl_cs);
}

PORT void FldigiFlush(void)
{
    if (!_InterlockedAnd(&g_fl_initialized, 1) || !g_fl_cs_inited) return;
    EnterCriticalSection(&g_fl_cs);
    for (int rx = 0; rx < FLDIGI_NRX; rx++)
        g_fl_rx_fifo_n[rx] = 0;
    g_fl_tx_fifo_n = 0;
    LeaveCriticalSection(&g_fl_cs);
}

/* ============================================================
 * Hot path: RX.  Called from xpipe() on the RX audio pass.
 * ============================================================ */

void xfldigi_rx(int rx, double* rbuff_io)
{
    if (rx < 0 || rx >= FLDIGI_NRX) return;
    if (!_InterlockedAnd(&g_fl_initialized, 1)) return;
    long en = _InterlockedAnd(&g_fl_rx_enabled[rx], 1);
    if (!en) return;
    if (!pcm || !rbuff_io) return;

    int outrate = pcm->rcvr[rx].ch_outrate;
    int outsize = pcm->rcvr[rx].ch_outsize;
    if (outrate <= 0 || outsize <= 0) return;
    if (outsize > FLDIGI_MAX_BLOCK) return;

    if (!g_fl_rx_resamp[rx] || g_fl_rx_outrate[rx] != outrate)
    {
        if (g_fl_rx_resamp[rx]) destroy_resampleFV(g_fl_rx_resamp[rx]);
        g_fl_rx_resamp[rx] = create_resampleFV(outrate, FLDIGI_MODEM_RATE);
        g_fl_rx_outrate[rx] = outrate;
    }

    /* De-interleave L channel to mono. */
    for (int i = 0; i < outsize; i++)
        g_fl_rx_mono[rx][i] = (float)rbuff_io[2 * i];

    int n8 = 0;
    xresampleFV(g_fl_rx_mono[rx], g_fl_rx_8k[rx], outsize, &n8,
                g_fl_rx_resamp[rx]);
    if (n8 > 0 && g_fl_cs_inited)
    {
        EnterCriticalSection(&g_fl_cs);
        fl_fifo_push_check(g_fl_rx_fifo[rx], &g_fl_rx_fifo_n[rx],
                           FLDIGI_RX_FIFO_CAP, g_fl_rx_8k[rx], n8,
                           &g_fl_rx_ovrun[rx], "rx_fifo");
        LeaveCriticalSection(&g_fl_cs);
    }

    if (++g_fl_rx_block_count[rx] >= 625)   /* ~ once a second at 48k/750 */
    {
        char log[140];
        sprintf_s(log, sizeof(log),
            "[FLDIGI] RX%d feeding %d Hz -> %d Hz (fifo=%d)\n",
            rx + 1, outrate, FLDIGI_MODEM_RATE, g_fl_rx_fifo_n[rx]);
        OutputDebugStringA(log);
        g_fl_rx_block_count[rx] = 0;
    }
}

/* ============================================================
 * Hot path: TX.  Called from xpipe() on the TX mic pass.
 * ============================================================ */

void xfldigi_tx(double* mic_io)
{
    if (!_InterlockedAnd(&g_fl_initialized, 1)) return;
    long en = _InterlockedAnd(&g_fl_tx_enabled, 1);
    if (!en) return;
    if (!pcm || !mic_io) return;

    int outrate = pcm->xcm_inrate[inid(1, 0)];
    int outsize = pcm->xcm_insize[inid(1, 0)];
    if (outrate <= 0 || outsize <= 0) return;
    if (outsize > FLDIGI_MAX_BLOCK) return;

    long mox = _InterlockedAnd(&g_fl_mox, 1);
    if (!mox)
        return;   /* pass mic through untouched when not keyed */

    if (!g_fl_tx_resamp || g_fl_tx_outrate != outrate)
    {
        if (g_fl_tx_resamp) destroy_resampleFV(g_fl_tx_resamp);
        g_fl_tx_resamp = create_resampleFV(FLDIGI_MODEM_RATE, outrate);
        g_fl_tx_outrate = outrate;
    }

    /* How many 8 kHz samples does a full outrate block need? */
    int need8k = (int)(((int64_t)outsize * FLDIGI_MODEM_RATE + outrate - 1) / outrate);
    if (need8k > FLDIGI_MAX_BLOCK) need8k = FLDIGI_MAX_BLOCK;

    int have = 0;
    if (g_fl_cs_inited) EnterCriticalSection(&g_fl_cs);
    have = (g_fl_tx_fifo_n < need8k) ? g_fl_tx_fifo_n : need8k;
    if (have > 0)
        fl_fifo_pop(g_fl_tx_fifo, &g_fl_tx_fifo_n, g_fl_tx_scratch, have);
    if (g_fl_cs_inited) LeaveCriticalSection(&g_fl_cs);

    if (have > 0)
    {
        int nout = 0;
        xresampleFV(g_fl_tx_scratch, g_fl_tx_out, have, &nout, g_fl_tx_resamp);
        for (int i = 0; i < outsize; i++)
        {
            double s = (i < nout) ? (double)g_fl_tx_out[i] : 0.0;
            mic_io[2 * i]     = s;
            mic_io[2 * i + 1] = 0.0;
        }
    }
    else
    {
        /* Keyed but no modem audio pending: keyed silence.  In digital
         * mode the fldigi modem always writes audio (even silence) while
         * transmitting, so this branch only appears at over start / under
         * pipe backpressure. */
        for (int i = 0; i < outsize; i++)
        {
            mic_io[2 * i]     = 0.0;
            mic_io[2 * i + 1] = 0.0;
        }
    }

    if (++g_fl_tx_block_count >= 125)   /* ~ a few times a second */
    {
        char log[140];
        sprintf_s(log, sizeof(log),
            "[FLDIGI] TX injecting %d Hz (fifo=%d mox=1)\n",
            outrate, g_fl_tx_fifo_n);
        OutputDebugStringA(log);
        g_fl_tx_block_count = 0;
    }
}