/*  sigid_mod.c

    Native RX audio tap for the Digi Id (signal identification) feature.

    RX:  xsigid_rx(rx, buffs[0]) is called from xpipe() on the RX1/RX2
         audio pass.  The receiver audio (interleaved stereo doubles at
         rcvr[].ch_outrate) is de-interleaved to mono, downsampled to
         SIGID_MODEM_RATE with the WDSP xresampleFV and pushed into a
         per-receiver FIFO.  SigidDrainRx() lets the managed SigIdAnalyzer
         pull the samples out for the feature extraction.

         The tap sits BEFORE the ChannelMaster mixer, so the audio has not
         been through the VST3 RX chain (aamix.c).  That is deliberate: a VST
         equaliser or limiter would change the measured signal.

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

#include "cmcomm.h"
#include "sigid_mod.h"

#include <math.h>

extern void* create_resampleFV(int in_rate, int out_rate);
extern void  xresampleFV(float* input, float* output, int numsamps,
                         int* outsamps, void* ptr);
extern void  destroy_resampleFV(void* ptr);

/* The analysis rate.  24 kHz covers +/-12 kHz of demodulated audio, which is
 * enough to measure the occupied bandwidth and to estimate the symbol rate of
 * the FSK/PSK signals in the Artemis database, while still keeping the ACF
 * (pulse repetition) analysis cheap. */
#define SIGID_MODEM_RATE  24000
#define SIGID_MAX_BLOCK   8192

/* 4 seconds of headroom between the audio thread and the managed drain. */
#define SIGID_RX_FIFO_CAP (SIGID_MODEM_RATE * 4)

static volatile long g_sg_initialized = 0;
static volatile long g_sg_rx_enabled[SIGID_NRX] = { 0, 0 };
static CRITICAL_SECTION g_sg_cs;
static long g_sg_cs_inited = 0;

static void*            g_sg_rx_resamp[SIGID_NRX] = { NULL, NULL };
static int              g_sg_rx_outrate[SIGID_NRX] = { 0, 0 };
static float*           g_sg_rx_mono[SIGID_NRX]   = { NULL, NULL };
static float*           g_sg_rx_24k[SIGID_NRX]     = { NULL, NULL };
static float*           g_sg_rx_fifo[SIGID_NRX]   = { NULL, NULL };
static int              g_sg_rx_fifo_n[SIGID_NRX] = { 0, 0 };
static long             g_sg_rx_ovrun[SIGID_NRX]  = { 0, 0 };
static long             g_sg_rx_block_count[SIGID_NRX] = { 0, 0 };
static long             g_sg_rx_sample_count[SIGID_NRX] = { 0, 0 };
static long             g_sg_rx_zero_count[SIGID_NRX] = { 0, 0 };
static long             g_sg_rx_log_count[SIGID_NRX] = { 0, 0 };

/* ============================================================
 * FIFO helpers
 * ============================================================ */

/* Push samples, dropping the oldest ones when the FIFO is full. */
static void sg_fifo_push(int rx, const float* src, int count)
{
    if (count <= 0) return;

    int space = SIGID_RX_FIFO_CAP - g_sg_rx_fifo_n[rx];
    if (space <= 0)
    {
        /* completely full - drop the oldest half so the newest audio wins */
        int drop = count > (SIGID_RX_FIFO_CAP / 2) ? (SIGID_RX_FIFO_CAP / 2) : count;
        memmove(g_sg_rx_fifo[rx], g_sg_rx_fifo[rx] + drop,
                (SIGID_RX_FIFO_CAP - drop) * sizeof(float));
        g_sg_rx_fifo_n[rx] = SIGID_RX_FIFO_CAP - drop;
        g_sg_rx_ovrun[rx]++;
        space = drop;
    }

    if (count > space)
    {
        /* only the newest samples fit */
        src += (count - space);
        count = space;
        g_sg_rx_ovrun[rx]++;
    }

    memcpy(g_sg_rx_fifo[rx] + g_sg_rx_fifo_n[rx], src, count * sizeof(float));
    g_sg_rx_fifo_n[rx] += count;
}

static void sg_fifo_pop(float* buf, int* n, float* dst, int count)
{
    if (count <= 0 || count > *n) return;
    memcpy(dst, buf, count * sizeof(float));
    *n -= count;
    if (*n > 0) memmove(buf, buf + count, (*n) * sizeof(float));
}

static void sg_dbg(const char* s)
{
    char log[256];
    sprintf_s(log, sizeof(log), "[SIGID] %s\n", s);
    OutputDebugStringA(log);
}

/* ============================================================
 * Lifecycle
 * ============================================================ */

void create_sigid(void)
{
    if (_InterlockedAnd(&g_sg_initialized, 1)) return;

    int i;
    if (!g_sg_cs_inited)
    {
        InitializeCriticalSectionAndSpinCount(&g_sg_cs, 4000);
        g_sg_cs_inited = 1;
    }

    for (i = 0; i < SIGID_NRX; i++)
    {
        g_sg_rx_mono[i]   = (float*)calloc(SIGID_MAX_BLOCK, sizeof(float));
        g_sg_rx_24k[i]     = (float*)calloc(SIGID_MAX_BLOCK, sizeof(float));
        g_sg_rx_fifo[i]   = (float*)calloc(SIGID_RX_FIFO_CAP, sizeof(float));
        g_sg_rx_fifo_n[i] = 0;
        g_sg_rx_ovrun[i]  = 0;
    }

    _InterlockedExchange(&g_sg_initialized, 1);
    sg_dbg("module created");
}

void destroy_sigid(void)
{
    _InterlockedExchange(&g_sg_initialized, 0);

    int i;
    for (i = 0; i < SIGID_NRX; i++)
    {
        if (g_sg_rx_resamp[i]) { destroy_resampleFV(g_sg_rx_resamp[i]); g_sg_rx_resamp[i] = NULL; }
        free(g_sg_rx_mono[i]);  g_sg_rx_mono[i]  = NULL;
        free(g_sg_rx_24k[i]);    g_sg_rx_24k[i]    = NULL;
        free(g_sg_rx_fifo[i]);   g_sg_rx_fifo[i]   = NULL;
        g_sg_rx_fifo_n[i] = 0;
    }

    if (g_sg_cs_inited)
    {
        DeleteCriticalSection(&g_sg_cs);
        g_sg_cs_inited = 0;
    }
    sg_dbg("module destroyed");
}

/* ============================================================
 * Setters / drain API
 * ============================================================ */

PORT void SigidSetRxEnable(int rx, int enable)
{
    if (rx < 0 || rx >= SIGID_NRX) return;
    long prev = _InterlockedExchange(&g_sg_rx_enabled[rx], enable ? 1 : 0);
    if (prev && !enable)
    {
        /* Disable edge: drop any stale queued RX audio so a re-enable does not
         * analyse the tail of the previous session. */
        if (_InterlockedAnd(&g_sg_initialized, 1) && g_sg_cs_inited)
        {
            EnterCriticalSection(&g_sg_cs);
            g_sg_rx_fifo_n[rx] = 0;
            LeaveCriticalSection(&g_sg_cs);
        }
    }
}

PORT int SigidGetRxEnable(int rx)
{
    if (rx < 0 || rx >= SIGID_NRX) return 0;
    return (int)_InterlockedAnd(&g_sg_rx_enabled[rx], 1);
}

PORT int SigidDrainRx(int rx, float* out, int maxCount)
{
    if (!_InterlockedAnd(&g_sg_initialized, 1) || rx < 0 || rx >= SIGID_NRX)
        return 0;
    if (!out || maxCount <= 0) return 0;

    int have = 0;
    if (g_sg_cs_inited) EnterCriticalSection(&g_sg_cs);
    have = (g_sg_rx_fifo_n[rx] < maxCount) ? g_sg_rx_fifo_n[rx] : maxCount;
    if (have > 0)
        sg_fifo_pop(g_sg_rx_fifo[rx], &g_sg_rx_fifo_n[rx], out, have);
    if (g_sg_cs_inited) LeaveCriticalSection(&g_sg_cs);
    return have;
}

PORT void SigidFlushRx(int rx)
{
    if (!_InterlockedAnd(&g_sg_initialized, 1) || rx < 0 || rx >= SIGID_NRX) return;
    if (!g_sg_cs_inited) return;
    EnterCriticalSection(&g_sg_cs);
    g_sg_rx_fifo_n[rx] = 0;
    LeaveCriticalSection(&g_sg_cs);
}

PORT int SigidRxFifoLevel(int rx)
{
    if (rx < 0 || rx >= SIGID_NRX) return 0;
    return g_sg_rx_fifo_n[rx];
}

PORT long SigidRxOverrunCount(int rx)
{
    if (rx < 0 || rx >= SIGID_NRX) return 0;
    return g_sg_rx_ovrun[rx];
}

PORT int SigidRxRate(void)
{
    return SIGID_MODEM_RATE;
}

PORT long SigidRxBlockCount(int rx)
{
    if (rx < 0 || rx >= SIGID_NRX) return 0;
    return g_sg_rx_block_count[rx];
}

PORT long SigidRxSampleCount(int rx)
{
    if (rx < 0 || rx >= SIGID_NRX) return 0;
    return g_sg_rx_sample_count[rx];
}

PORT long SigidRxZeroCount(int rx)
{
    if (rx < 0 || rx >= SIGID_NRX) return 0;
    return g_sg_rx_zero_count[rx];
}

PORT void SigidCreate(void)
{
    create_sigid();
}

PORT void SigidDestroy(void)
{
    destroy_sigid();
}

/* ============================================================
 * Hot path: RX.  Called from xpipe() on the RX audio pass.
 * ============================================================ */

/* The actual work: de-interleave to mono, resample and queue.  Split out so the
 * managed self test can push a synthetic signal through the very same chain
 * without a radio being connected. */
PORT int SigidProcessRxBlock(int rx, const double* interleaved, int frames, int inRate)
{
    if (rx < 0 || rx >= SIGID_NRX) return 0;
    if (!_InterlockedAnd(&g_sg_initialized, 1)) return 0;
    if (!interleaved || frames <= 0 || inRate <= 0) return 0;
    if (frames > SIGID_MAX_BLOCK) return 0;

    if (!g_sg_rx_resamp[rx] || g_sg_rx_outrate[rx] != inRate)
    {
        if (g_sg_rx_resamp[rx]) destroy_resampleFV(g_sg_rx_resamp[rx]);
        g_sg_rx_resamp[rx] = create_resampleFV(inRate, SIGID_MODEM_RATE);
        g_sg_rx_outrate[rx] = inRate;
        if (!g_sg_rx_resamp[rx]) return 0;
    }

    /* De-interleave the L channel to mono. */
    for (int i = 0; i < frames; i++)
        g_sg_rx_mono[rx][i] = (float)interleaved[2 * i];

    int n24 = 0;
    xresampleFV(g_sg_rx_mono[rx], g_sg_rx_24k[rx], frames, &n24, g_sg_rx_resamp[rx]);

    g_sg_rx_block_count[rx]++;
    if (n24 <= 0) g_sg_rx_zero_count[rx]++;
    else g_sg_rx_sample_count[rx] += n24;

    if (n24 > 0 && g_sg_cs_inited)
    {
        EnterCriticalSection(&g_sg_cs);
        sg_fifo_push(rx, g_sg_rx_24k[rx], n24);
        LeaveCriticalSection(&g_sg_cs);
    }

    return n24;
}

void xsigid_rx(int rx, double* rbuff_io)
{
    if (rx < 0 || rx >= SIGID_NRX) return;
    if (!_InterlockedAnd(&g_sg_initialized, 1)) return;

    long en = _InterlockedAnd(&g_sg_rx_enabled[rx], 1);
    if (!en) return;

    if (!pcm || !rbuff_io) return;

    int outrate = pcm->rcvr[rx].ch_outrate;
    int outsize = pcm->rcvr[rx].ch_outsize;
    if (outrate <= 0 || outsize <= 0) return;
    if (outsize > SIGID_MAX_BLOCK) return;

    SigidProcessRxBlock(rx, rbuff_io, outsize, outrate);

    if (++g_sg_rx_log_count[rx] >= 1000)   /* roughly once every 20 s at 48k/64 */
    {
        char log[160];
        sprintf_s(log, sizeof(log),
            "[SIGID] RX%d feeding %d Hz -> %d Hz (fifo=%d)\n",
            rx + 1, outrate, SIGID_MODEM_RATE, g_sg_rx_fifo_n[rx]);
        OutputDebugStringA(log);
        g_sg_rx_log_count[rx] = 0;
    }
}
