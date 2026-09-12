// Tier 3 extension - GPU compute shaders for spectrum normalisation and
// waterfall colour conversion (experimental)
//
// Offloads two CPU-side per-pixel loops onto D3D11 compute shaders:
//   1. Waterfall colour conversion: dBm float → BGRA pixel via a 1024-entry
//      colour LUT, feeding the existing GPU waterfall ring (Display.WaterfallMesh.cs)
//      or the D2D bitmap scroll path directly.
//   2. Spectrum normalisation: dBm float → [0..1] height value, feeding the
//      existing GPU 2D panafill mesh (Display.Pan2DMesh.cs) height texture.
//
// Both shaders use the same fallback contract as every other GPU path (rule 1):
// any failure returns false and the caller runs the CPU loop unchanged.
// The LUT is precomputed on the CPU per scheme (all 6 schemes are piecewise
// ramps or explicit gradient arrays that reduce to a 1024-entry table) and
// uploaded as a texture once per frame when the scheme/parameters change.
// GPU sync uses a D3D11 Event query to ensure readback completes before
// the staging buffer is mapped.
//
// IMPORTANT: CopyResource on Buffer resources is broken on AMD RX 580
// (data silently never reaches/leaves the GPU buffer). All GPU→CPU and
// CPU→GPU data transfer MUST use Texture2D + CopySubresourceRegion with
// staging textures. This applies to BOTH the waterfall and spectrum
// compute pipelines. See EnsureWaterfallComputeBuffers for the pattern.
//
// Scope: engages only when GpuComputeEnabled is set by the user AND the
// render path is Hardware AND _device is alive. WARP / Force-CPU sessions
// never arm any compute path.
using System;
using System.Drawing;
using System.Runtime.CompilerServices;
// Third-party: GPU interop via Vortice.Windows (MIT License, Copyright (c) Amer Koleci and Contributors).
// Full license text ships with the app (Licenses folder) and lives in the repo under Project Files\lib\licenses\.
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapMode = Vortice.Direct3D11.MapMode;

namespace Thetis
{
    partial class Display
    {
        #region GPU compute shader fields

        // --- waterfall colour compute ---
        private static bool _wfComputeShadersBuilt;
        private static ID3D11ComputeShader _wfCS;
        private static ID3D11Buffer _wfComputeCB;              // 16 bytes (low, high, linLogCor, scheme)
        private static ID3D11Texture2D _wfComputeInputTex;     // R32_Float, W x 1, Default (GPU input)
        private static ID3D11ShaderResourceView _wfComputeInputSRV;
        private static ID3D11Texture2D _wfComputeOutputTex;    // R32_UInt, W x 1, Default UAV (GPU output)
        private static ID3D11UnorderedAccessView _wfComputeOutputUAV;
        private static ID3D11Texture2D _wfComputeInputStaging; // R32_Float, Staging Write (CPU→GPU upload)
        private static ID3D11Texture2D _wfComputeOutputStaging;// R32_UInt, Staging Read (GPU→CPU readback)
        private static ID3D11Texture2D _wfComputeLutTex;       // 1024 x 1, BGRA8 (Default, read by shader)
        private static ID3D11Texture2D _wfComputeLutUpload;    // Staging: 1024 x 1, BGRA8 (CPU→GPU upload)
        private static ID3D11ShaderResourceView _wfComputeLutSRV;
        private static ID3D11SamplerState _wfComputeLutSamp;
        private static ID3D11Query _wfComputeEvent;

        private static byte[] _wfComputeLutPixels;       // 1024*4 scratch

        // --- spectrum normalisation compute ---
        private static bool _specComputeShadersBuilt;
        private static ID3D11ComputeShader _specCS;
        private static ID3D11Buffer _specComputeCB;              // 16 bytes (fOffset, gridMin, invRange, pad)
        private static ID3D11Texture2D _specComputeInputTex;     // R32_Float, W x 1, Default (GPU input)
        private static ID3D11ShaderResourceView _specComputeInputSRV;
        private static ID3D11Texture2D _specComputeOutputTex;    // R32_Float, W x 1, Default UAV (GPU output)
        private static ID3D11UnorderedAccessView _specComputeOutputUAV;
        private static ID3D11Texture2D _specComputeInputStaging; // R32_Float, Staging Write (CPU→GPU upload)
        private static ID3D11Texture2D _specComputeOutputStaging;// R32_Float, Staging Read (GPU→CPU readback)
        private static ID3D11Query _specComputeEvent;

        private const int WfLutSize = 1024;
        private const int ComputeGroupSize = 64;         // threads per group (both shaders)

        // tracked for dirty checking (avoid redundant LUT uploads)
        private static int _wfComputeLutVersion = -1;
        private static int _wfComputeLutScheme;
        private static float _wfComputeLutLow;
        private static float _wfComputeLutHigh;

        // first-success logging (mirrors _wfLoggedActive pattern in WaterfallMesh)
        private static bool _wfComputeLoggedActive;
        private static bool _specComputeLoggedActive;

        #endregion

        #region GPU compute shader public control

        /// <summary>Experimental GPU compute shader toggle (session only).
        /// When true and the render path is Hardware, the colour conversion
        /// and spectrum normalisation are offloaded to D3D11 compute shaders.</summary>
        public static bool GpuComputeEnabled { get; set; } = true;

        /// <summary>True when all conditions for compute dispatch are met.</summary>
        private static bool ComputeArmed
        {
            get { return GpuComputeEnabled && m_eRenderPath == DXRenderPath.Hardware && _device != null && _bDX2Setup; }
        }

        #endregion

        #region GPU compute shader HLSL - waterfall colour conversion

        private const string WF_COMPUTE_HLSL = @"
            cbuffer WfCB : register(b0)
            {
                float CB_Low;        // low threshold dBm
                float CB_High;       // high threshold dBm
                float CB_LinLogCor;  // LinLog/Lin offset correction
                uint  CB_Scheme;     // 0=Custom,1=enhanced,2=SPECTRAN,3=BLACKWHITE,
                                     // 4=LinLog,5=LinRad,6=LinAuto,7=off,8=Custom
            };

            Texture2D<float4> WfLut : register(t0);
            SamplerState WfLutSamp : register(s0);

            Texture2D<float>  Input  : register(t1);
            RWTexture2D<uint> Output : register(u0);

            [numthreads(64, 1, 1)]
            void cs_main(uint3 tid : SV_DispatchThreadID)
            {
                uint idx = tid.x;
                float dBm = Input.Load(int3(idx, 0, 0));

                // clamp to threshold range for LUT lookup
                float t;
                if (dBm <= CB_Low)
                    t = 0.0;
                else if (dBm >= CB_High)
                    t = 1.0;
                else
                {
                    float v = dBm - CB_Low + CB_LinLogCor;
                    t = v / (CB_High - CB_Low);
                }
                t = clamp(t, 0.0, 1.0);

                float u = (t * 1023.0 + 0.5) / 1024.0;
                float4 col = WfLut.SampleLevel(WfLutSamp, float2(u, 0.5), 0);

                // pack as uint: R in low byte, G in second byte, B in third byte
                uint r = (uint)(col.r * 255.0 + 0.5);
                uint g = (uint)(col.g * 255.0 + 0.5);
                uint b = (uint)(col.b * 255.0 + 0.5);
                Output[int2(idx, 0)] = r | (g << 8) | (b << 16) | (0xFFu << 24);
            }
            ";

        #endregion

        #region GPU compute shader HLSL - spectrum normalisation

        private const string SPEC_COMPUTE_HLSL = @"
            cbuffer SpecCB : register(b0)
            {
                float CB_Offset;     // fOffset (RX offset dBm)
                float CB_GridMin;    // grid_min
                float CB_InvRange;   // 1.0 / (grid_max - grid_min)
                float CB_Pad;
            };

            Texture2D<float>  Input  : register(t0);
            RWTexture2D<float> Output : register(u0);

            [numthreads(64, 1, 1)]
            void cs_main(uint3 tid : SV_DispatchThreadID)
            {
                uint idx = tid.x;
                float dBm = Input.Load(int3(idx, 0, 0));
                float v = (dBm + CB_Offset - CB_GridMin) * CB_InvRange;
                Output[int2(idx, 0)] = clamp(v, 0.0, 1.0);
            }
            ";

        #endregion

        #region GPU compute shader lifecycle

        private static void ReleaseComputeObjects()
        {
            _wfCS?.Dispose(); _wfCS = null;
            _wfComputeCB?.Dispose(); _wfComputeCB = null;
            _wfComputeInputTex?.Dispose(); _wfComputeInputTex = null;
            _wfComputeInputSRV?.Dispose(); _wfComputeInputSRV = null;
            _wfComputeOutputTex?.Dispose(); _wfComputeOutputTex = null;
            _wfComputeOutputUAV?.Dispose(); _wfComputeOutputUAV = null;
            _wfComputeInputStaging?.Dispose(); _wfComputeInputStaging = null;
            _wfComputeOutputStaging?.Dispose(); _wfComputeOutputStaging = null;
            _wfComputeLutTex?.Dispose(); _wfComputeLutTex = null;
            _wfComputeLutUpload?.Dispose(); _wfComputeLutUpload = null;
            _wfComputeLutSRV?.Dispose(); _wfComputeLutSRV = null;
            _wfComputeLutSamp?.Dispose(); _wfComputeLutSamp = null;
            _wfComputeEvent?.Dispose(); _wfComputeEvent = null;
            _wfComputeShadersBuilt = false;
            _wfComputeLutVersion = -1;

            _specCS?.Dispose(); _specCS = null;
            _specComputeCB?.Dispose(); _specComputeCB = null;
            _specComputeInputTex?.Dispose(); _specComputeInputTex = null;
            _specComputeInputSRV?.Dispose(); _specComputeInputSRV = null;
            _specComputeOutputTex?.Dispose(); _specComputeOutputTex = null;
            _specComputeOutputUAV?.Dispose(); _specComputeOutputUAV = null;
            _specComputeInputStaging?.Dispose(); _specComputeInputStaging = null;
            _specComputeOutputStaging?.Dispose(); _specComputeOutputStaging = null;
            _specComputeEvent?.Dispose(); _specComputeEvent = null;
            _specComputeShadersBuilt = false;
        }

        /// <summary>Called when the DX device is lost or resized - tears down all
        /// compute resources so they are rebuilt on the next dispatch.</summary>
        private static void ReleaseComputeResources()
        {
            ReleaseComputeObjects();
        }

        #endregion

        #region GPU compute shader pipeline build

        private static bool BuildWaterfallComputePipeline(ID3D11Device device)
        {
            if (_wfComputeShadersBuilt) return true;
            try
            {
                byte[] csBytes = Vortice.D3DCompiler.Compiler.Compile(WF_COMPUTE_HLSL, "cs_main",
                    "wf_compute.hlsl", "cs_5_0",
                    Vortice.D3DCompiler.ShaderFlags.None, Vortice.D3DCompiler.EffectFlags.None).ToArray();
                _wfCS = device.CreateComputeShader(csBytes);

                _wfComputeCB = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer,
                    ResourceUsage.Dynamic, CpuAccessFlags.Write));

                _wfComputeLutTex = device.CreateTexture2D(new Texture2DDescription()
                {
                    Width = (uint)WfLutSize, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                });
                _wfComputeLutUpload = device.CreateTexture2D(new Texture2DDescription()
                {
                    Width = (uint)WfLutSize, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Write,
                });
                _wfComputeLutSRV = device.CreateShaderResourceView(_wfComputeLutTex);
                _wfComputeLutSamp = device.CreateSamplerState(new SamplerDescription(
                    Vortice.Direct3D11.Filter.MinMagMipPoint,
                    TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp));

                _wfComputeEvent = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));

                _wfComputeLutPixels = new byte[WfLutSize * 4];

                _wfComputeShadersBuilt = true;
                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU compute waterfall: pipeline build failed - " + e.Message);
                ReleaseComputeObjects();
                return false;
            }
        }

        private static bool EnsureWaterfallComputeBuffers(ID3D11Device device, int count)
        {
            if (_wfComputeInputTex != null && _wfComputeInputTex.Description.Width == (uint)count)
                return true;
            try
            {
                _wfComputeInputTex?.Dispose(); _wfComputeInputSRV?.Dispose();
                _wfComputeOutputTex?.Dispose(); _wfComputeOutputUAV?.Dispose();
                _wfComputeInputStaging?.Dispose(); _wfComputeInputStaging = null;
                _wfComputeOutputStaging?.Dispose(); _wfComputeOutputStaging = null;

                // Input texture: R32_Float, W x 1, Default usage, SRV bind
                _wfComputeInputTex = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)count, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                });
                _wfComputeInputSRV = device.CreateShaderResourceView(_wfComputeInputTex);

                // Output texture: R32_UInt, W x 1, Default usage, UAV bind
                _wfComputeOutputTex = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)count, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32_UInt,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess,
                });
                _wfComputeOutputUAV = device.CreateUnorderedAccessView(_wfComputeOutputTex);

                // Input staging: R32_Float, W x 1, Staging, CpuAccessFlags.Write
                _wfComputeInputStaging = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)count, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Write,
                });

                // Output staging: R32_UInt, W x 1, Staging, CpuAccessFlags.Read
                _wfComputeOutputStaging = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)count, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32_UInt,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                });

                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU compute waterfall: texture build failed (n=" + count + ") - " + e.Message);
                _wfComputeInputTex?.Dispose(); _wfComputeInputTex = null;
                _wfComputeInputSRV?.Dispose(); _wfComputeInputSRV = null;
                _wfComputeOutputTex?.Dispose(); _wfComputeOutputTex = null;
                _wfComputeOutputUAV?.Dispose(); _wfComputeOutputUAV = null;
                _wfComputeInputStaging?.Dispose(); _wfComputeInputStaging = null;
                _wfComputeOutputStaging?.Dispose(); _wfComputeOutputStaging = null;
                return false;
            }
        }

        private static bool BuildSpectrumComputePipeline(ID3D11Device device)
        {
            if (_specComputeShadersBuilt) return true;
            try
            {
                byte[] csBytes = Vortice.D3DCompiler.Compiler.Compile(SPEC_COMPUTE_HLSL, "cs_main",
                    "spec_compute.hlsl", "cs_5_0",
                    Vortice.D3DCompiler.ShaderFlags.None, Vortice.D3DCompiler.EffectFlags.None).ToArray();
                _specCS = device.CreateComputeShader(csBytes);

                _specComputeCB = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer,
                    ResourceUsage.Dynamic, CpuAccessFlags.Write));

                _specComputeEvent = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));

                _specComputeShadersBuilt = true;
                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU compute spectrum: pipeline build failed - " + e.Message);
                ReleaseComputeObjects();
                return false;
            }
        }

        private static bool EnsureSpectrumComputeBuffers(ID3D11Device device, int count)
        {
            if (_specComputeInputTex != null && _specComputeInputTex.Description.Width == (uint)count)
                return true;
            try
            {
                _specComputeInputTex?.Dispose(); _specComputeInputSRV?.Dispose();
                _specComputeOutputTex?.Dispose(); _specComputeOutputUAV?.Dispose();
                _specComputeInputStaging?.Dispose(); _specComputeInputStaging = null;
                _specComputeOutputStaging?.Dispose(); _specComputeOutputStaging = null;

                _specComputeInputTex = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)count, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                });
                _specComputeInputSRV = device.CreateShaderResourceView(_specComputeInputTex);

                _specComputeOutputTex = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)count, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess,
                });
                _specComputeOutputUAV = device.CreateUnorderedAccessView(_specComputeOutputTex);

                _specComputeInputStaging = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)count, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Write,
                });

                _specComputeOutputStaging = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)count, Height = 1, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                });

                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU compute spectrum: texture build failed (n=" + count + ") - " + e.Message);
                _specComputeInputTex?.Dispose(); _specComputeInputTex = null;
                _specComputeInputSRV?.Dispose(); _specComputeInputSRV = null;
                _specComputeOutputTex?.Dispose(); _specComputeOutputTex = null;
                _specComputeOutputUAV?.Dispose(); _specComputeOutputUAV = null;
                _specComputeInputStaging?.Dispose(); _specComputeInputStaging = null;
                _specComputeOutputStaging?.Dispose(); _specComputeOutputStaging = null;
                return false;
            }
        }

        #endregion

        #region GPU compute LUT builder

        /// <summary>
        /// Precomputes a 1024-entry BGRA gradient LUT for the given waterfall
        /// colour scheme.  Pixel-identical to the CPU colour switch in
        /// DrawWaterfallDX2D (display.cs lines ~8349-9246).
        /// </summary>
        private static void BuildWaterfallComputeLut(ColorScheme scheme, float lowThreshold,
            float highThreshold, float linCor, bool isRx2, bool isMox)
        {
            byte[] px = _wfComputeLutPixels;

            if (scheme == ColorScheme.Custom)
            {
                Color[] cols;
                if (isMox) cols = _tx_waterfall_grad;
                else if (isRx2) cols = _rx2_waterfall_grad;
                else cols = _rx1_waterfall_grad;

                if (cols == null || cols.Length < 2)
                {
                    for (int i = 0; i < WfLutSize; i++) { px[i * 4] = 0; px[i * 4 + 1] = 0; px[i * 4 + 2] = 0; px[i * 4 + 3] = 255; }
                    return;
                }
                int gradLast = cols.Length - 1;
                for (int i = 0; i < WfLutSize; i++)
                {
                    float t = i / (float)(WfLutSize - 1);
                    int idx = (int)(t * gradLast);
                    if (idx < 0) idx = 0; else if (idx > gradLast) idx = gradLast;
                    px[i * 4 + 0] = (byte)cols[idx].B;
                    px[i * 4 + 1] = (byte)cols[idx].G;
                    px[i * 4 + 2] = (byte)cols[idx].R;
                    px[i * 4 + 3] = 255;
                }
                return;
            }

            // For all other schemes, we build the LUT by simulating the per-pixel
            // colour conversion at 1024 evenly-spaced dBm values across the
            // threshold range. The LUT is indexed by (value - low) / (high - low).

            float range = highThreshold - lowThreshold;
            Color lowColor = isMox ? waterfall_low_color_tx : waterfall_low_color;

            for (int i = 0; i < WfLutSize; i++)
            {
                float t = i / (float)(WfLutSize - 1); // 0..1
                float dBm = lowThreshold + t * range;  // synthetic dBm
                int R = 0, G = 0, B = 0;

                switch (scheme)
                {
                    case ColorScheme.enhanced:
                        if (t <= 0f) { R = lowColor.R; G = lowColor.G; B = lowColor.B; }
                        else if (t >= 1f) { R = 192; G = 124; B = 255; }
                        else
                        {
                            if (t < 2.0 / 9) { float lp = t / (2.0f / 9); R = (int)((1.0 - lp) * lowColor.R); G = (int)((1.0 - lp) * lowColor.G); B = (int)(lowColor.B + lp * (255 - lowColor.B)); }
                            else if (t < 3.0 / 9) { float lp = (t - 2.0f / 9) / (1.0f / 9); R = 0; G = (int)(lp * 255); B = 255; }
                            else if (t < 4.0 / 9) { float lp = (t - 3.0f / 9) / (1.0f / 9); R = 0; G = 255; B = (int)((1.0 - lp) * 255); }
                            else if (t < 5.0 / 9) { float lp = (t - 4.0f / 9) / (1.0f / 9); R = (int)(lp * 255); G = 255; B = 0; }
                            else if (t < 7.0 / 9) { float lp = (t - 5.0f / 9) / (2.0f / 9); R = 255; G = (int)((1.0 - lp) * 255); B = 0; }
                            else if (t < 8.0 / 9) { float lp = (t - 7.0f / 9) / (1.0f / 9); R = 255; G = 0; B = (int)(lp * 255); }
                            else { float lp = (t - 8.0f / 9) / (1.0f / 9); R = (int)((0.75 + 0.25 * (1.0 - lp)) * 255); G = (int)(lp * 255 * 0.5); B = 255; }
                        }
                        px[i * 4 + 0] = (byte)B; px[i * 4 + 1] = (byte)G; px[i * 4 + 2] = (byte)R; px[i * 4 + 3] = 255;
                        break;

                    case ColorScheme.SPECTRAN:
                        if (t <= 0f) { R = G = B = 0; }
                        else if (t >= 1f) { R = G = B = 240; }
                        else
                        {
                            float lp = t * 100f;
                            if (lp < 51f) { R = G = 0; B = (int)lp * 5; }
                            else if (lp < 66f) { R = G = (int)(lp - 50) * 2; B = 255; }
                            else if (lp < 77f) { R = G = (int)(lp - 50) * 3; B = 255; }
                            else if (lp < 88f) { R = G = (int)(lp - 50) * 4; B = 255; }
                            else if (lp < 99f) { R = G = (int)(lp - 50) * 5; B = 255; }
                            else { R = G = 255; B = 255; }
                        }
                        px[i * 4 + 0] = (byte)B; px[i * 4 + 1] = (byte)G; px[i * 4 + 2] = (byte)R; px[i * 4 + 3] = 255;
                        break;

                    case ColorScheme.BLACKWHITE:
                        R = G = B = (int)(t * 255);
                        if (t <= 0f) R = G = B = 0;
                        else if (t >= 1f) R = G = B = 255;
                        px[i * 4 + 0] = (byte)B; px[i * 4 + 1] = (byte)G; px[i * 4 + 2] = (byte)R; px[i * 4 + 3] = 255;
                        break;

                    case ColorScheme.LinLog:
                        BuildLinRadLutEntry(px, i, t, range, lowThreshold, linCor, true);
                        break;

                    case ColorScheme.LinRad:
                        BuildLinRadLutEntry(px, i, t, range, lowThreshold, linCor, false);
                        break;

                    case ColorScheme.LinAuto:
                        BuildLinRadLutEntry(px, i, t, range, lowThreshold, linCor, false);
                        break;

                    default:
                        // original / off: black
                        px[i * 4 + 0] = 0; px[i * 4 + 1] = 0; px[i * 4 + 2] = 0; px[i * 4 + 3] = 255;
                        break;
                }
            }
        }

        /// <summary>Builds a single LUT entry for the LinRad / LinLog / LinAuto
        /// 23-band piecewise palette. Mirrors display.cs lines ~8626-9245.</summary>
        private static void BuildLinRadLutEntry(byte[] px, int i, float t, float range,
            float lowThreshold, float linCor, bool isLinLog)
        {
            int R = 0, G = 0, B = 0;
            float dBm = lowThreshold + t * range;
            float offset, overallPercent;

            if (isLinLog)
            {
                offset = dBm - lowThreshold + LinLogCor;
                float specBits = 1024f;
                overallPercent = (specBits * offset) / range;
                float logFract = (float)Math.Log10(specBits);
                if (overallPercent == 0) overallPercent = 0.001f;
                overallPercent = (float)Math.Log10(overallPercent);
                BuildLinRadPalette(ref R, ref G, ref B, overallPercent, logFract, 23);
            }
            else
            {
                offset = dBm - lowThreshold + LinCor;
                overallPercent = offset / range;
                BuildLinRadPalette(ref R, ref G, ref B, overallPercent, 1.0f, 23);
            }

            // LinRad/LinLin palette uses reverse RGB order: RGB → BGRA storage
            px[i * 4 + 0] = (byte)R;
            px[i * 4 + 1] = (byte)G;
            px[i * 4 + 2] = (byte)B;
            px[i * 4 + 3] = 255;
        }

        /// <summary>Shared 23-band LinRad palette. thresholds are in terms of
        /// overallPercent/logFract (LinLog) or overallPercent*1 (LinRad/LinAuto).
        /// Mirrors the fixed colour stops from display.cs.</summary>
        private static void BuildLinRadPalette(ref int R, ref int G, ref int B,
            float overallPercent, float logFract, int bands)
        {
            float step = logFract / bands;
            if (overallPercent < step) { R = 0; G = 0; B = 0; }
            else if (overallPercent < 2 * step) { R = 32; G = 0; B = 0; }
            else if (overallPercent < 3 * step) { R = 64; G = 0; B = 0; }
            else if (overallPercent < 4 * step) { R = 96; G = 0; B = 0; }
            else if (overallPercent < 5 * step) { R = 104; G = 40; B = 0; }
            else if (overallPercent < 6 * step) { R = 112; G = 60; B = 0; }
            else if (overallPercent < 7 * step) { R = 116; G = 88; B = 0; }
            else if (overallPercent < 8 * step) { R = 92; G = 112; B = 0; }
            else if (overallPercent < 9 * step) { R = 80; G = 132; B = 0; }
            else if (overallPercent < 10 * step) { R = 20; G = 140; B = 0; }
            else if (overallPercent < 11 * step) { R = 0; G = 160; B = 40; }
            else if (overallPercent < 12 * step) { R = 0; G = 160; B = 120; }
            else if (overallPercent < 13 * step) { R = 0; G = 140; B = 148; }
            else if (overallPercent < 14 * step) { R = 0; G = 132; B = 192; }
            else if (overallPercent < 15 * step) { R = 0; G = 112; B = 200; }
            else if (overallPercent < 16 * step) { R = 0; G = 88; B = 208; }
            else if (overallPercent < 17 * step) { R = 0; G = 60; B = 232; }
            else if (overallPercent < 18 * step) { R = 0; G = 40; B = 252; }
            else if (overallPercent < 19 * step) { R = 80; G = 80; B = 252; }
            else if (overallPercent < 20 * step) { R = 124; G = 124; B = 252; }
            else if (overallPercent < 21 * step) { R = 172; G = 172; B = 252; }
            else { R = 252; G = 252; B = 252; }
        }

        #endregion

        #region GPU compute dispatch - waterfall colour conversion

        /// <summary>
        /// Dispatches the waterfall colour compute shader: takes dBm float values
        /// and produces a BGRA row in the staging buffer.  Returns the row bytes
        /// via the provided array (must be W*4 long).
        /// Returns false on any failure - caller falls back to the CPU colour switch.
        /// </summary>
        /// <param name="waterfallData">offset-adjusted dBm values (nDecimatedWidth floats)</param>
        /// <param name="row">output BGRA byte array (W * 4) - filled by compute or CPU fallback</param>
        /// <param name="W">display width in pixels</param>
        /// <param name="nDecimatedWidth">decimated width (W / decimation)</param>
        /// <param name="m_nDecimation">decimation factor</param>
        /// <param name="scheme">current colour scheme</param>
        /// <param name="lowThreshold">low waterfall threshold dBm</param>
        /// <param name="highThreshold">high waterfall threshold dBm</param>
        /// <param name="linCor">LinCor or 0 for non-LinRad schemes</param>
        /// <param name="isRx2">RX2 flag (for gradient selection)</param>
        /// <param name="isMox">MOX/TX flag (for gradient selection)</param>
        /// <returns>true if the row was filled by the compute shader</returns>
        private static bool TryDispatchWaterfallCompute(float[] waterfallData, byte[] row,
            int W, int nDecimatedWidth, int m_nDecimation, ColorScheme scheme,
            float lowThreshold, float highThreshold, float linCor, bool isRx2, bool isMox)
        {
            if (!ComputeArmed || _paused_display) return false;

            try
            {
                if (!BuildWaterfallComputePipeline(_device)) return false;
                if (!EnsureWaterfallComputeBuffers(_device, nDecimatedWidth)) return false;

                ID3D11DeviceContext dc = _device.ImmediateContext;

                // --- upload LUT if dirty ---
                int lutHash = ((int)scheme * 73856093) ^ lowThreshold.GetHashCode() ^
                    highThreshold.GetHashCode() ^ linCor.GetHashCode();
                if (lutHash != _wfComputeLutVersion)
                {
                    BuildWaterfallComputeLut(scheme, lowThreshold, highThreshold, linCor, isRx2, isMox);

                    unsafe
                    {
                        MappedSubresource lm = dc.Map((ID3D11Resource)_wfComputeLutUpload, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
                        fixed (byte* src = _wfComputeLutPixels)
                        {
                            uint bytes = (uint)(WfLutSize * 4);
                            Buffer.MemoryCopy(src, (void*)lm.DataPointer, bytes, bytes);
                        }
                        dc.Unmap((ID3D11Resource)_wfComputeLutUpload, 0);
                    }
                    dc.CopySubresourceRegion((ID3D11Resource)_wfComputeLutTex, 0, 0u, 0u, 0u,
                        (ID3D11Resource)_wfComputeLutUpload, 0, null);
                    _wfComputeLutVersion = lutHash;


                }

                // --- upload dBm data to input texture via staging texture + CopySubresourceRegion ---
                unsafe
                {
                    MappedSubresource um = dc.Map((ID3D11Resource)_wfComputeInputStaging, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
                    fixed (float* src = waterfallData)
                    {
                        Buffer.MemoryCopy(src, (void*)um.DataPointer, (uint)(nDecimatedWidth * 4), (uint)(nDecimatedWidth * 4));
                    }
                    dc.Unmap((ID3D11Resource)_wfComputeInputStaging, 0);
                }
                dc.CopySubresourceRegion((ID3D11Resource)_wfComputeInputTex, 0, 0u, 0u, 0u,
                    (ID3D11Resource)_wfComputeInputStaging, 0, null);

                // --- upload constants ---
                var cb = new WfComputeConstants
                {
                    Low = lowThreshold,
                    High = highThreshold,
                    LinLogCor = linCor,
                    Scheme = (uint)scheme,
                };
                MappedSubresource cm = dc.Map((ID3D11Resource)_wfComputeCB, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                unsafe { Unsafe.Write((void*)cm.DataPointer, cb); }
                dc.Unmap((ID3D11Resource)_wfComputeCB, 0);

                // --- dispatch ---
                if (_wfCS == null || _wfComputeOutputUAV == null || _wfComputeInputSRV == null ||
                    _wfComputeLutSRV == null || _wfComputeLutSamp == null || _wfComputeCB == null ||
                    _wfComputeOutputTex == null || _wfComputeOutputStaging == null)
                {
                    ReleaseComputeObjects();
                    return false;
                }
                var removed = _device.DeviceRemovedReason;
                if (removed.Failure)
                {
                    Common.MeshDiagLog("GPU wf dispatch ABORT: device removed " + removed);
                    return false;
                }

                dc.CSSetShader(_wfCS);
                dc.CSSetConstantBuffer(0, _wfComputeCB);
                dc.CSSetShaderResources(0, new[] { _wfComputeLutSRV, _wfComputeInputSRV });
                dc.CSSetUnorderedAccessViews(0, new[] { _wfComputeOutputUAV }, new[] { 0u });
                dc.CSSetSamplers(0, new[] { _wfComputeLutSamp });

                uint groups = (uint)((nDecimatedWidth + ComputeGroupSize - 1) / ComputeGroupSize);
                dc.Dispatch(groups, 1, 1);

                // --- unbind UAV so CopySubresourceRegion can access the texture ---
                dc.CSSetShader(null);
                dc.CSSetUnorderedAccessViews(0, new[] { (ID3D11UnorderedAccessView)null }, new[] { 0u });
                dc.CSSetShaderResources(0, new[] { (ID3D11ShaderResourceView)null, (ID3D11ShaderResourceView)null });

                // --- GPU sync: flush, end event, spin ---
                dc.Flush();
                dc.End(_wfComputeEvent);

                int spinCount = 0;
                while (dc.GetData(_wfComputeEvent, AsyncGetDataFlags.None) == null)
                {
                    spinCount++;
                    if (spinCount > 10000000)
                    {
                        Common.MeshDiagLog("GPU wf: event query spin timeout!");
                        return false;
                    }
                }

                var removedAfter = _device.DeviceRemovedReason;
                if (removedAfter.Failure)
                {
                    Common.MeshDiagLog("GPU wf dispatch ABORT: device removed AFTER dispatch " + removedAfter);
                    return false;
                }

                // --- copy output texture to staging for CPU read ---
                dc.CopySubresourceRegion((ID3D11Resource)_wfComputeOutputStaging, 0, 0u, 0u, 0u,
                    (ID3D11Resource)_wfComputeOutputTex, 0, null);

                // --- read back and expand to full W*4 row ---
                MappedSubresource rm = dc.Map((ID3D11Resource)_wfComputeOutputStaging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                unsafe
                {
                    uint* src = (uint*)rm.DataPointer;
                    for (int i = 0; i < nDecimatedWidth; i++)
                    {
                        uint packed = src[i];
                        int dest = (i * m_nDecimation) * 4;
                        row[dest + 0] = (byte)((packed >> 16) & 0xFF); // B (BGRA byte order)
                        row[dest + 1] = (byte)((packed >> 8) & 0xFF);  // G
                        row[dest + 2] = (byte)(packed & 0xFF);         // R
                        row[dest + 3] = 255;                            // A

                        // fill decimation sub-pixels
                        for (int j = 1; j < m_nDecimation; j++)
                        {
                            int d = dest + j * 4;
                            row[d] = row[dest]; row[d + 1] = row[dest + 1];
                            row[d + 2] = row[dest + 2]; row[d + 3] = 255;
                        }
                    }

                }
                dc.Unmap((ID3D11Resource)_wfComputeOutputStaging, 0);

                if (!_wfComputeLoggedActive)
                {
                    _wfComputeLoggedActive = true;
                    Common.MeshDiagLog("GPU compute waterfall active (" + nDecimatedWidth + " pixels, scheme=" + scheme + ", W=" + W + ", dec=" + m_nDecimation + ")");
                }

                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU compute waterfall: dispatch failed - " + e.Message);
                ReleaseComputeObjects();
                return false;
            }
        }

        #endregion

        #region GPU compute dispatch - spectrum normalisation

        /// <summary>
        /// Dispatches the spectrum normalisation compute shader: takes dBm float
        /// values and produces [0..1] normalised heights, writing them directly
        /// into the existing height texture (HeightTex) via an intermediate buffer.
        /// Returns false on any failure - caller falls back to the CPU normalisation loop.
        /// </summary>
        /// <param name="data">raw dBm values (nDecimatedWidth floats)</param>
        /// <param name="fOffset">RX display offset</param>
        /// <param name="gridMin">grid_min</param>
        /// <param name="gridMax">grid_max</param>
        /// <param name="nDecimatedWidth">number of decimated columns</param>
        /// <returns>true if normalisation was completed on the GPU</returns>
        private static bool TryDispatchSpectrumCompute(float[] data, float fOffset,
            int gridMin, int gridMax, int nDecimatedWidth)
        {
            if (!ComputeArmed || _paused_display) return false;
            int yRange = gridMax - gridMin;
            if (yRange <= 0) return false;

            try
            {
                if (!BuildSpectrumComputePipeline(_device)) return false;
                if (!EnsureSpectrumComputeBuffers(_device, nDecimatedWidth)) return false;

                ID3D11DeviceContext dc = _device.ImmediateContext;

                // --- upload dBm data via staging texture + CopySubresourceRegion ---
                unsafe
                {
                    MappedSubresource um = dc.Map((ID3D11Resource)_specComputeInputStaging, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
                    fixed (float* src = data)
                    {
                        Buffer.MemoryCopy(src, (void*)um.DataPointer, (uint)(nDecimatedWidth * 4), (uint)(nDecimatedWidth * 4));
                    }
                    dc.Unmap((ID3D11Resource)_specComputeInputStaging, 0);
                }
                dc.CopySubresourceRegion((ID3D11Resource)_specComputeInputTex, 0, 0u, 0u, 0u,
                    (ID3D11Resource)_specComputeInputStaging, 0, null);

                // --- upload constants ---
                var cb = new SpecComputeConstants
                {
                    Offset = fOffset,
                    GridMin = gridMin,
                    InvRange = 1f / yRange,
                    Pad = 0f,
                };
                MappedSubresource cm = dc.Map((ID3D11Resource)_specComputeCB, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                unsafe { Unsafe.Write((void*)cm.DataPointer, cb); }
                dc.Unmap((ID3D11Resource)_specComputeCB, 0);

                // --- dispatch ---
                dc.CSSetShader(_specCS);
                dc.CSSetConstantBuffer(0, _specComputeCB);
                dc.CSSetShaderResources(0, new[] { _specComputeInputSRV });
                dc.CSSetUnorderedAccessViews(0, new[] { _specComputeOutputUAV }, new[] { 0u });

                uint groups = (uint)((nDecimatedWidth + ComputeGroupSize - 1) / ComputeGroupSize);
                dc.Dispatch(groups, 1, 1);

                // --- unbind UAV so CopySubresourceRegion can access ---
                dc.CSSetShader(null);
                dc.CSSetUnorderedAccessViews(0, new[] { (ID3D11UnorderedAccessView)null }, new[] { 0u });
                dc.CSSetShaderResources(0, new[] { (ID3D11ShaderResourceView)null });

                // --- GPU sync ---
                dc.Flush();
                dc.End(_specComputeEvent);

                int spinCount = 0;
                while (dc.GetData(_specComputeEvent, AsyncGetDataFlags.None) == null)
                {
                    spinCount++;
                    if (spinCount > 10000000)
                    {
                        Common.MeshDiagLog("GPU spec: event query spin timeout!");
                        return false;
                    }
                }

                // --- copy output texture to staging for CPU read ---
                dc.CopySubresourceRegion((ID3D11Resource)_specComputeOutputStaging, 0, 0u, 0u, 0u,
                    (ID3D11Resource)_specComputeOutputTex, 0, null);

                // --- read back normalised values into the height scratch array ---
                MappedSubresource rm = dc.Map((ID3D11Resource)_specComputeOutputStaging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                unsafe
                {
                    float* src = (float*)rm.DataPointer;
                    float[] scratch = _specHeightScratch;
                    if (scratch.Length < nDecimatedWidth)
                        _specHeightScratch = scratch = new float[Math.Max(nDecimatedWidth, scratch.Length * 2)];
                    for (int i = 0; i < nDecimatedWidth; i++)
                        scratch[i] = src[i];
                }
                dc.Unmap((ID3D11Resource)_specComputeOutputStaging, 0);

                if (!_specComputeLoggedActive)
                {
                    _specComputeLoggedActive = true;
                    Common.MeshDiagLog("GPU spec compute active (" + nDecimatedWidth + " cols, offset=" + fOffset + " gridMin=" + gridMin + " gridMax=" + gridMax + ")");
                }

                return true;
            }
            catch (Exception e)
            {
                Common.MeshDiagLog("GPU compute spectrum: dispatch failed - " + e.Message);
                ReleaseComputeObjects();
                return false;
            }
        }

        #endregion

        #region GPU compute constant buffer structs

        private struct WfComputeConstants
        {
            public float Low;
            public float High;
            public float LinLogCor;
            public uint Scheme;
        }

        private struct SpecComputeConstants
        {
            public float Offset;
            public float GridMin;
            public float InvRange;
            public float Pad;
        }

        #endregion
    }
}
