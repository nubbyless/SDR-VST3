// GPU spectrum overlays (roadmap #5): renders the spectral peak-hold fill
// (Active Peak Fill) and the peak trace line into an offscreen GPU sheet and
// composites it with a single D2D DrawBitmap, exactly where the per-column
// peak strokes would have been drawn (after the pana fill, before the data
// line). The D2D peak strokes remain the permanent CPU fallback - every
// failure returns false and the column loop draws them as before (GPU
// fallback rule 1). Mirrors the Tier 3 GPU 2D panafill sheet pipeline
// (Display.Pan2DMesh.cs).
//
// Copyright (c) 2026 Thetis project contributors. GPL-3.0.
// Third-party: GPU interop via Vortice.Windows (MIT License, Copyright (c) Amer Koleci and Contributors).
// Full license text ships with the app (Licenses folder) and lives in the repo under Project Files\lib\licenses\.
using System;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using MapMode = Vortice.Direct3D11.MapMode;

namespace Thetis
{
    partial class Display
    {
        #region GPU spectrum overlay fields

        private const int OverlaySlotCount = 2;   // index 0 = rx1, index 1 = rx2

        private static bool _ovlShadersBuilt;
        private static ID3D11VertexShader _ovlVS;
        private static ID3D11PixelShader _ovlPS;
        private static ID3D11Buffer _ovlCB;          // per-pane constants
        private static ID3D11RasterizerState _ovlRS; // CullNone + scissor enabled

        private struct OverlaySheetState
        {
            public ID3D11Texture2D SheetTex;       // W x H, BGRA8, RT binding
            public ID3D11RenderTargetView SheetRTV;
            public ID2D1Bitmap SheetBitmap;         // shared wrapping of SheetTex
            public ID3D11Texture2D CornersTex;      // 2*cols x 1, R32G32B32A32 dynamic (single row, RowPitch-safe)
            public ID3D11ShaderResourceView CornersSRV;
            public int W, H, Cols;
        }
        private static readonly OverlaySheetState[] _ovl = new OverlaySheetState[OverlaySlotCount];

        private static float[] _ovlScratch;          // cols * 8 corner floats

        private static bool _ovlLoggedActive;
        private static readonly bool[] _ovlGuardLogged = new bool[OverlaySlotCount];
        private static readonly bool[] _ovlDiagLogged = new bool[OverlaySlotCount];

        #endregion

        #region GPU spectrum overlay HLSL

        private const string OVERLAY_HLSL = @"
            cbuffer OvCB : register(b0)
            {
                float4 OvSheet;   // SheetW, SheetH, Shift, Pad
                float4 OvCol;     // R, G, B, A (straight alpha 0..1)
            };

            Texture2D<float4> OvCorners : register(t0);

            struct SO
            {
                float4 pos : SV_POSITION;
            };

            // Quad soup straight off SV_VertexID: 6 verts per column. Corner
            // table: tri A = (0,1,2), tri B = (1,3,2). Each column packs its
            // 4 corners (pixel coords, target space) into two texels:
            // texel 2*col = corner0,corner1, texel 2*col+1 = corner2,corner3.
            // The texture is a SINGLE row of 2*cols texels so the flat CPU
            // upload below never depends on D3D's RowPitch (a cols x 2 layout
            // written with one contiguous MemoryCopy misplaces row 1 on padded
            // pitches, slanting every quad sideways - the L-R bar geometry bug).
            // Y is shifted to sheet-local in the VS like the panafill sheet.
            SO vs_ovl(uint vid : SV_VertexID)
            {
                SO o;
                uint col = vid / 6u;
                static const uint ovlCorner[6] = { 0u, 1u, 2u, 1u, 3u, 2u };
                uint k = ovlCorner[vid % 6u];
                uint tx = col * 2u;

                float4 cA = OvCorners.Load(int3(tx, 0, 0));
                float4 cB = OvCorners.Load(int3(tx + 1u, 0, 0));

                float2 p;
                if (k < 2u) p = (k == 0u) ? cA.xy : cA.zw;
                else        p = (k == 2u) ? cB.xy : cB.zw;

                float yPane = p.y - OvSheet.z;
                o.pos = float4(p.x / OvSheet.x * 2.0 - 1.0, 1.0 - yPane / OvSheet.y * 2.0, 0.0, 1.0);
                return o;
            }

            // Same colour/alpha the D2D peak strokes use, premultiplied in the
            // shader so the sheet composites identically under SourceOver.
            float4 ps_ovl(SO i) : SV_Target
            {
                return float4(OvCol.rgb * OvCol.a, OvCol.a);
            }
            ";

        #endregion

        #region GPU spectrum overlay lifecycle

        /// <summary>Master toggle. Session-only like its siblings; the D2D peak
        /// strokes are always the fallback.</summary>
        public static bool GpuOverlayEnabled { get; set; } = true;

        private static bool OverlayMeshArmed
        {
            get { return GpuOverlayEnabled && m_eRenderPath == DXRenderPath.Hardware && _device != null && _bDX2Setup; }
        }

        /// <summary>Releases the D2D-side wrappers only - used on render-target
        /// recreation (resize) where the textures themselves survive.</summary>
        private static void ReleaseSpectrumOverlayFrameState()
        {
            for (int i = 0; i < OverlaySlotCount; i++)
            {
                _ovl[i].SheetBitmap?.Dispose();
                _ovl[i].SheetBitmap = null;
            }
        }

        private static void ReleaseSpectrumOverlayObjects()
        {
            ReleaseSpectrumOverlayFrameState();
            for (int i = 0; i < OverlaySlotCount; i++)
            {
                ref OverlaySheetState s = ref _ovl[i];
                s.SheetRTV?.Dispose(); s.SheetRTV = null;
                s.CornersSRV?.Dispose(); s.CornersSRV = null;
                s.CornersTex?.Dispose(); s.CornersTex = null;
                s.SheetTex?.Dispose(); s.SheetTex = null;
                s.W = 0; s.H = 0; s.Cols = 0;
            }
            _ovlVS?.Dispose(); _ovlVS = null;
            _ovlPS?.Dispose(); _ovlPS = null;
            _ovlCB?.Dispose(); _ovlCB = null;
            _ovlRS?.Dispose(); _ovlRS = null;
            _ovlShadersBuilt = false;
        }

        private static bool BuildOverlayPipeline(ID3D11Device device)
        {
            if (_ovlShadersBuilt) return true;
            try
            {
                byte[] vsBytes = Vortice.D3DCompiler.Compiler.Compile(OVERLAY_HLSL, "vs_ovl", "overlaymesh.hlsl", "vs_5_0",
                    Vortice.D3DCompiler.ShaderFlags.None, Vortice.D3DCompiler.EffectFlags.None).ToArray();
                byte[] psBytes = Vortice.D3DCompiler.Compiler.Compile(OVERLAY_HLSL, "ps_ovl", "overlaymesh.hlsl", "ps_5_0",
                    Vortice.D3DCompiler.ShaderFlags.None, Vortice.D3DCompiler.EffectFlags.None).ToArray();
                _ovlVS = device.CreateVertexShader(vsBytes, null);
                _ovlPS = device.CreatePixelShader(psBytes);

                _ovlCB = device.CreateBuffer(new BufferDescription(32, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
                _ovlRS = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, Vortice.Direct3D11.FillMode.Solid)
                {
                    ScissorEnable = true,
                });

                _ovlScratch = new float[4096];
                _ovlShadersBuilt = true;
                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU overlay: pipeline build failed - " + e.Message);
                ReleaseSpectrumOverlayObjects();
                return false;
            }
        }

        private static bool EnsureOverlaySheet(ID3D11Device device, int slot, int w, int h, int cols)
        {
            ref OverlaySheetState s = ref _ovl[slot];
            if (s.SheetTex != null && s.W == w && s.H == h && s.Cols == cols)
            {
                // textures survive render-target recreation, but the D2D wrapper
                // dies with the old target (ReleaseSpectrumOverlayFrameState) -
                // rebuild just that wrapper or the blit silently no-ops forever
                if (s.SheetBitmap != null) return true;
                try
                {
                    using IDXGISurface surf = s.SheetTex.QueryInterface<IDXGISurface>();
                    s.SheetBitmap = _d2dRenderTarget.CreateSharedBitmap(surf, new BitmapProperties(
                        new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)));
                    return true;
                }
                catch (Exception e)
                {
                    Common.MeshDiagLog("GPU overlay: shared bitmap rebuild failed - " + e.Message);
                    s.SheetBitmap?.Dispose(); s.SheetBitmap = null;
                    return false;
                }
            }

            try
            {
                s.SheetBitmap?.Dispose(); s.SheetBitmap = null;
                s.SheetRTV?.Dispose(); s.SheetRTV = null;
                s.CornersSRV?.Dispose(); s.CornersSRV = null;
                s.CornersTex?.Dispose(); s.CornersTex = null;
                s.SheetTex?.Dispose(); s.SheetTex = null;

                s.SheetTex = device.CreateTexture2D(new Texture2DDescription()
                {
                    Width = (uint)w,
                    Height = (uint)h,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                });
                s.SheetRTV = device.CreateRenderTargetView(s.SheetTex);

                using IDXGISurface surf = s.SheetTex.QueryInterface<IDXGISurface>();
                s.SheetBitmap = _d2dRenderTarget.CreateSharedBitmap(surf, new BitmapProperties(
                    new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)));

                s.CornersTex = device.CreateTexture2D(new Texture2DDescription()
                {
                    Width = (uint)(cols * 2),
                    Height = 1,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Dynamic,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.Write,
                });
                s.CornersSRV = device.CreateShaderResourceView(s.CornersTex);

                s.W = w; s.H = h; s.Cols = cols;
                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU overlay: sheet build failed (" + w + "x" + h + ") - " + e.Message);
                s.SheetBitmap?.Dispose(); s.SheetBitmap = null;
                s.SheetRTV?.Dispose(); s.SheetRTV = null;
                s.CornersSRV?.Dispose(); s.CornersSRV = null;
                s.CornersTex?.Dispose(); s.CornersTex = null;
                s.SheetTex?.Dispose(); s.SheetTex = null;
                s.W = 0; s.H = 0; s.Cols = 0;
                return false;
            }
        }

        #endregion

        #region GPU spectrum overlay render + composite

        private struct OverlayConstants
        {
            public float SheetW, SheetH, Shift, Pad;   // OvSheet
            public float R, G, B, A;                   // OvCol (straight alpha)
        }

        /// <summary>
        /// Renders the spectral peak-hold overlay (Active Peak Fill columns or the
        /// connected peak trace line) for this pane into the offscreen GPU sheet.
        /// Called from DrawPanadapterDX2D after the pana-fill sheet composite and
        /// before the column loop; returns true when the caller must composite the
        /// sheet (BlitSpectrumOverlayMesh) and skip its per-column peak strokes.
        /// Geometry replicates the D2D column-loop Y mapping exactly so the two
        /// paths are pixel-identical.
        /// </summary>
        private static bool TryRenderSpectrumOverlayMesh(int rx, int nVerticalShift, int W, int H,
            int nDecimatedWidth, float[] data, float fOffset, int grid_min, int grid_max,
            Maximums[] spectralPeaks, bool bSpectralPeakHold, bool bActivePeakFill, float lineWidth,
            bool live3DMapping, float live3DBottomY, float live3DRidge, float live3DZCurve)
        {
            int slot = rx == 2 ? 1 : 0;
            if (!OverlayMeshArmed || _paused_display)
            {
                if (!_ovlGuardLogged[slot])
                {
                    _ovlGuardLogged[slot] = true;
                    Common.MeshDiagLog("GPU overlay: blocked by guard - armed=" + OverlayMeshArmed +
                        " paused=" + _paused_display + " enabled=" + GpuOverlayEnabled +
                        " path=" + m_eRenderPath + " dx2=" + _bDX2Setup + " device=" + (_device != null));
                }
                return false;
            }

            int yRange = grid_max - grid_min;
            if (!bSpectralPeakHold || spectralPeaks == null || nDecimatedWidth < 2 ||
                data == null || data.Length < nDecimatedWidth || spectralPeaks.Length < nDecimatedWidth ||
                yRange <= 0 || W <= 0 || H <= 0)
            {
                if (!_ovlGuardLogged[slot])
                {
                    _ovlGuardLogged[slot] = true;
                    Common.MeshDiagLog("GPU overlay: blocked - peakHold=" + bSpectralPeakHold +
                        " peaks=" + (spectralPeaks == null ? "null" : spectralPeaks.Length.ToString()) +
                        " cols=" + nDecimatedWidth);
                }
                return false;
            }

            try
            {
                if (!BuildOverlayPipeline(_device)) return false;
                if (!EnsureOverlaySheet(_device, slot, W, H, nDecimatedWidth)) return false;
                if (!EnsureMeshRTV(_device)) return false;   // for the backbuffer restore below
                ref OverlaySheetState s = ref _ovl[slot];

                float[] sc = _ovlScratch;
                int need = nDecimatedWidth * 8;
                if (sc == null || sc.Length < need) _ovlScratch = sc = new float[Math.Max(need, sc.Length * 2)];

                // ---- geometry: replicate the D2D column-loop Y mapping ----
                float dbmToPixel = H / (float)yRange;
                var yPlain = new Func<float, float>(v => (int)((grid_max - v) * dbmToPixel - 0.5f) + nVerticalShift);
                var yLive = new Func<float, float>(v =>
                {
                    float sP = (v - grid_min) / (float)yRange;
                    if (sP < 0) sP = 0; else if (sP > 1) sP = 1;
                    return (int)(live3DBottomY - Math.Pow(sP, live3DZCurve) * live3DRidge - 0.5f);
                });
                Func<float, float> yMap = live3DMapping ? yLive : yPlain;

                int o = 0;
                if (bActivePeakFill)
                {
                    // per-column crest-to-peak bars, width = local_Decimation
                    // (D2D replicates: DrawLine(point, spectralPeakPoint, brush, dec))
                    float halfDec = m_nDecimation * 0.5f;
                    for (int i = 0; i < nDecimatedWidth; i++)
                    {
                        float max = data[i] + fOffset;
                        float peakUsed = Math.Max(max, spectralPeaks[i].max_dBm);
                        float yData = yMap(max);
                        float yPeak = yMap(peakUsed);
                        float x0 = i * m_nDecimation - halfDec;
                        float x1 = i * m_nDecimation + halfDec;

                        sc[o++] = x0; sc[o++] = yData; sc[o++] = x0; sc[o++] = yPeak;
                        sc[o++] = x1; sc[o++] = yData; sc[o++] = x1; sc[o++] = yPeak;
                    }
                }
                else
                {
                    // connected trace line: one extruded quad per segment,
                    // width = line_width, first point mirrors the D2D
                    // oldSpectralPeakPoint init (plain formula + H clamp)
                    float yPrev = (int)(((grid_max - spectralPeaks[0].max_dBm) * dbmToPixel) - 0.5f);
                    if (yPrev >= H) yPrev = H;
                    yPrev += nVerticalShift;
                    float w2 = lineWidth * 0.5f;
                    for (int i = 0; i < nDecimatedWidth; i++)
                    {
                        float max = data[i] + fOffset;
                        float peakUsed = Math.Max(max, spectralPeaks[i].max_dBm);
                        float yPeak = yMap(peakUsed);
                        float x0 = i == 0 ? 0f : (i - 1) * m_nDecimation;
                        float x1 = i * m_nDecimation;

                        float dx = x1 - x0, dy = yPeak - yPrev;
                        float len = (float)Math.Sqrt(dx * dx + dy * dy);
                        float nx = 0f, ny = 0f;
                        if (len > 1e-4f) { nx = -dy / len * w2; ny = dx / len * w2; }

                        sc[o++] = x0 + nx; sc[o++] = yPrev + ny;
                        sc[o++] = x0 - nx; sc[o++] = yPrev - ny;
                        sc[o++] = x1 + nx; sc[o++] = yPeak + ny;
                        sc[o++] = x1 - nx; sc[o++] = yPeak - ny;
                        yPrev = yPeak;
                    }
                }

                ID3D11DeviceContext dc = _device.ImmediateContext;

                // ---- upload corners (cols*8 floats) ----
                // Single-row texture: the flat copy below is exactly the row contents,
                // so driver RowPitch padding can no longer shift the 2nd texel pair.
                // (Original cols x 2 layout + contiguous MemoryCopy misread row 1 on
                //  padded pitches like RX 580 -> sideways/slanted quads.)
                MappedSubresource hm = dc.Map((ID3D11Resource)s.CornersTex, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                unsafe
                {
                    fixed (float* src = sc)
                    {
                        uint bytes = (uint)need * sizeof(float);
                        Buffer.MemoryCopy(src, (void*)hm.DataPointer, bytes, bytes);
                    }
                }
                dc.Unmap((ID3D11Resource)s.CornersTex, 0);

                // ---- colour / alpha from the D2D peak brush (kept in lockstep) ----
                float cr, cg, cb2, ca;
                if (m_bDX2_dataPeaks_fill_fpen_brush is ID2D1SolidColorBrush solid)
                {
                    Color4 c = solid.Color;                         // D2D premultiplied 0..1
                    ca = c.A;
                    if (ca > 1e-4f) { cr = c.R / ca; cg = c.G / ca; cb2 = c.B / ca; }
                    else { cr = 0f; cg = 0f; cb2 = 0f; }
                }
                else
                {
                    System.Drawing.Color pc = ((System.Drawing.SolidBrush)dataPeaks_fill_fpen.Brush).Color;
                    cr = pc.R / 255f; cg = pc.G / 255f; cb2 = pc.B / 255f; ca = pc.A / 255f;
                }

                // ---- one-shot diagnostic summary (diag mode only): snaps to the FIRST frame
                // where real peak values exist (past the warm-up sentinel) and logs a
                // single line proving the geometry landed - data/peak ranges, how many
                // columns hold a peak above the data, anything pinned at the grid top
                // or outside the pane, plus the peak brush colour. Per-slot, once per
                // session; on-screen "GPU overlay mesh active" covers routine operation.
                if (Common.MeshDiagLogEnabled && !_ovlDiagLogged[slot])
                {
                    int nSent = 0, nNan = 0, nUp = 0, nTopBar = 0, nOut = 0;
                    float pMin = float.MaxValue, pMax = float.MinValue, dMin = float.MaxValue, dMax = float.MinValue;
                    int iMaxCol = 0;
                    for (int i = 0; i < nDecimatedWidth; i++)
                    {
                        float max = data[i] + fOffset;
                        float p = spectralPeaks[i].max_dBm;
                        if (p < -1e6f) nSent++;
                        if (float.IsNaN(p)) nNan++;
                        if (p > pMax) { pMax = p; iMaxCol = i; }
                        if (p < pMin) pMin = p;
                        if (max < dMin) dMin = max;
                        if (max > dMax) dMax = max;
                        float up = Math.Max(max, p);
                        if (up > max + 1e-3f) nUp++;
                        if (up >= grid_max - 1f) nTopBar++;
                        float y = yMap(up);
                        if (y < 0f || y > H) nOut++;
                    }
                    if (nSent < nDecimatedWidth)
                    {
                        _ovlDiagLogged[slot] = true;
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        sb.Append("GPU overlay diag rx").Append(rx)
                          .Append(' ').Append(bActivePeakFill ? "fill" : "trace")
                          .Append(" cols=").Append(nDecimatedWidth)
                          .Append(" grid=[").Append(grid_min).Append("..").Append(grid_max).Append(']')
                          .Append(" data[").Append(dMin.ToString("0.0")).Append("..").Append(dMax.ToString("0.0")).Append(']')
                          .Append(" peak[").Append(pMin.ToString("0.0")).Append("..").Append(pMax.ToString("0.0")).Append(']')
                          .Append(" nNan=").Append(nNan).Append(" nUp=").Append(nUp)
                          .Append(" nTopBar=").Append(nTopBar).Append(" nOut=").Append(nOut)
                          .Append(" col=").Append(cr.ToString("0.00")).Append('/').Append(cg.ToString("0.00")).Append('/')
                          .Append(cb2.ToString("0.00")).Append(" a=").Append(ca.ToString("0.00"));
                        for (int t = 0; t < 3 && t < nDecimatedWidth; t++)
                        {
                            float max = data[t] + fOffset;
                            sb.Append(" |c").Append(t).Append("(d=").Append(max.ToString("0.0"))
                              .Append(",p=").Append(spectralPeaks[t].max_dBm.ToString("0.0"))
                              .Append(",y=").Append(yMap(Math.Max(max, spectralPeaks[t].max_dBm)).ToString("0")).Append(')');
                        }
                        if (iMaxCol < nDecimatedWidth)
                        {
                            float max = data[iMaxCol] + fOffset;
                            sb.Append(" |cmax@").Append(iMaxCol).Append("(d=").Append(max.ToString("0.0"))
                              .Append(",p=").Append(spectralPeaks[iMaxCol].max_dBm.ToString("0.0"))
                              .Append(",y=").Append(yMap(Math.Max(max, spectralPeaks[iMaxCol].max_dBm)).ToString("0")).Append(')');
                        }
                        Common.MeshDiagLog(sb.ToString());
                    }
                }

                var cb = new OverlayConstants
                {
                    SheetW = W, SheetH = H, Shift = nVerticalShift, Pad = 0f,
                    R = cr, G = cg, B = cb2, A = ca,
                };
                MappedSubresource cbMap = dc.Map((ID3D11Resource)_ovlCB, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                unsafe { System.Runtime.CompilerServices.Unsafe.Write((void*)cbMap.DataPointer, cb); }
                dc.Unmap((ID3D11Resource)_ovlCB, 0);

                // pending D2D batch first, then our D3D pass, then back to D2D -
                // keeps the interleaving deterministic on the shared queue
                ulong flushTag1, flushTag2;
                _d2dRenderTarget.Flush(out flushTag1, out flushTag2);

                dc.OMSetRenderTargets(new[] { s.SheetRTV }, null);
                dc.ClearRenderTargetView(s.SheetRTV, new Color4(0f, 0f, 0f, 0f));
                dc.OMSetBlendState(null);
                dc.RSSetState(_ovlRS);
                dc.RSSetScissorRects(new[] { new Vortice.RawRect(0, 0, W, H) });
                dc.RSSetViewport(new Viewport(0f, 0f, (float)W, (float)H));
                dc.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
                dc.VSSetShader(_ovlVS);
                dc.VSSetConstantBuffer(0, _ovlCB);
                dc.VSSetShaderResource(0, s.CornersSRV);
                dc.PSSetShader(_ovlPS);
                dc.PSSetConstantBuffer(0, _ovlCB);
                dc.Draw((uint)nDecimatedWidth * 6u, 0u);
                dc.Flush();

                // restore the backbuffer binding + full scissor before returning
                // to D2D (same dance as the panafill sheet - otherwise every later
                // D2D draw of this frame would land inside the sheet)
                dc.OMSetRenderTargets(new[] { _meshRTV }, null);
                dc.RSSetScissorRects(new[] { new Vortice.RawRect(0, 0,
                    (int)displayTargetWidth, (int)displayTargetHeight) });
                // we ALSO changed the viewport for the sheet render - unlike the
                // panafill sheet (which leaves the viewport untouched) this stays
                // active and every later D2D draw of THIS frame (data line, glow,
                // overlays) would rasterise through the W x H pane viewport
                // instead of the full target, scrambling the rest of the frame.
                // Restore it like the OM/scissor dance above.
                dc.RSSetViewport(new Viewport(0f, 0f,
                    (float)displayTargetWidth, (float)displayTargetHeight));
                dc.Flush();

                _ovlGuardLogged[slot] = false;
                if (!_ovlLoggedActive)
                {
                    _ovlLoggedActive = true;
                    Common.MeshDiagLog("GPU overlay mesh active (" + (nDecimatedWidth * 6) + " verts, " +
                        (bActivePeakFill ? "fill" : "trace") + " mode, rx" + rx + ")");
                }
                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU overlay: render failed - falling back to D2D peak strokes : " + e.Message);
                ReleaseSpectrumOverlayObjects();
                return false;
            }
        }

        /// <summary>Composites the GPU sheet at the exact position the per-column
        /// peak strokes occupied. Must be called inside the spectrum clip region.</summary>
        private static void BlitSpectrumOverlayMesh(int rx, int nVerticalShift, int W, int H)
        {
            ID2D1Bitmap bmp = _ovl[rx == 2 ? 1 : 0].SheetBitmap;
            if (bmp == null) return;
            _d2dRenderTarget.DrawBitmap(bmp,
                new Rect(0f, nVerticalShift, W, H),
                1f, BitmapInterpolationMode.Linear, null);
        }

        #endregion
    }
}