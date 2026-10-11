using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BallPitFinal
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BallInstance { public Vector3 pos; public uint packed; } // 16 bytes; packed = color (low 8 bits) | DrawableBit

    [StructLayout(LayoutKind.Sequential)]
    internal struct BallUpdate { public uint id; public Vector3 pos; public uint packed; } // 20 bytes

    /// <summary>
    /// GPU-driven renderer for RESTING balls. One persistent instance buffer indexed by ball id mirrors the store
    /// (only dirty ids are uploaded). Every frame, on the GPU:
    ///   1. last frame's visible balls are drawn from THIS frame's camera into an eye-depth target (occluders),
    ///   2. the target is reduced to 8x8-pixel tile maxima,
    ///   3. every resting ball is frustum + occlusion tested and appended to a near or far LOD list,
    ///   4. two DrawMeshInstancedIndirect calls draw the lists in URP's normal passes.
    /// Occluders are always real balls that exist this frame, so culling is conservative (never hides a visible ball).
    /// Active/held balls are drawn separately via DrawDynamic (not occluders, not occluded).
    /// </summary>
    public sealed class GpuBallRenderer : System.IDisposable
    {
        public const uint DrawableBit = 1u << 8;

        // Settings (safe to change at runtime)
        public bool OcclusionEnabled = true;
        public bool FlipOcclusionY = false;
        public float LodPixelRadius = 12f;
        public ShadowCastingMode CastShadows = ShadowCastingMode.Off;
        public bool ReceiveShadows = true;
        public int Layer = 0;

        // Stats (read back asynchronously, a few frames late)
        public int DrawnNear { get; private set; }
        public int DrawnFar { get; private set; }
        public int LastUploadCount { get; private set; }

        private const int MaxUpdatesPerFrame = 65536;

        private readonly ComputeShader cs;
        private readonly int kScatter, kReduce, kCull;
        private readonly Material ballMat, occMat;
        private readonly Mesh nearMesh, farMesh;
        private readonly float radius;
        private readonly Bounds drawBounds;
        private readonly int capacity;
        private int maxDynamic;

        private readonly ComputeBuffer instances, updates, stats;
        private ComputeBuffer dynInstances, identityIds; // grow on demand (the active set is no longer capped by a pool size)
        private readonly ComputeBuffer[] visNear = new ComputeBuffer[2], visFar = new ComputeBuffer[2];
        private readonly ComputeBuffer[] argsNear = new ComputeBuffer[2], argsFar = new ComputeBuffer[2];

        private readonly BallUpdate[] updateStaging = new BallUpdate[MaxUpdatesPerFrame];
        private readonly int[] dirtyList;
        private readonly bool[] dirtyFlag;
        private int dirtyCount;
        private bool fullUploadPending = true;

        private RenderTexture occRT, tileRT;
        private int rtW, rtH, tilesX, tilesY;

        private readonly CommandBuffer cmd = new CommandBuffer { name = "BallPit GPU Cull" };
        private readonly MaterialPropertyBlock mpbNear = new MaterialPropertyBlock(), mpbFar = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock mpbOccNear = new MaterialPropertyBlock(), mpbOccFar = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock mpbDyn = new MaterialPropertyBlock();
        private readonly Plane[] planes = new Plane[6];
        private readonly Vector4[] planeVecs = new Vector4[6];
        private readonly int[] int2 = new int[2];
        private readonly System.Action<AsyncGPUReadbackRequest> onStats;
        private int parity, frame;
        private bool hasPrev;

        private static readonly int IdInstances = Shader.PropertyToID("_Instances");
        private static readonly int IdInstancesRW = Shader.PropertyToID("_InstancesRW");
        private static readonly int IdUpdates = Shader.PropertyToID("_Updates");
        private static readonly int IdUpdateCount = Shader.PropertyToID("_UpdateCount");
        private static readonly int IdVisibleIds = Shader.PropertyToID("_VisibleIds");
        private static readonly int IdVisibleNear = Shader.PropertyToID("_VisibleNear");
        private static readonly int IdVisibleFar = Shader.PropertyToID("_VisibleFar");
        private static readonly int IdOccDepth = Shader.PropertyToID("_OccDepth");
        private static readonly int IdTileMaxRW = Shader.PropertyToID("_TileMaxRW");
        private static readonly int IdTileMax = Shader.PropertyToID("_TileMax");
        private static readonly int IdScreenSize = Shader.PropertyToID("_ScreenSize");
        private static readonly int IdTileCount = Shader.PropertyToID("_TileCount");
        private static readonly int IdCullScreen = Shader.PropertyToID("_CullScreen");
        private static readonly int IdInstanceCount = Shader.PropertyToID("_InstanceCount");
        private static readonly int IdFrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
        private static readonly int IdCullView = Shader.PropertyToID("_CullView");
        private static readonly int IdCullViewProj = Shader.PropertyToID("_CullViewProj");
        private static readonly int IdCullRadius = Shader.PropertyToID("_CullRadius");
        private static readonly int IdNearClip = Shader.PropertyToID("_NearClip");
        private static readonly int IdProjScaleY = Shader.PropertyToID("_ProjScaleY");
        private static readonly int IdLodPixelRadius = Shader.PropertyToID("_LodPixelRadius");
        private static readonly int IdOcclusionOn = Shader.PropertyToID("_OcclusionOn");
        private static readonly int IdFlipY = Shader.PropertyToID("_FlipY");
        private static readonly int IdDepthBias = Shader.PropertyToID("_DepthBias");
        private static readonly int IdOccViewProj = Shader.PropertyToID("_OccViewProj");
        private static readonly int IdOccView = Shader.PropertyToID("_OccView");
        private static readonly int IdBallRadius = Shader.PropertyToID("_BallRadius");
        private static readonly int IdPalette = Shader.PropertyToID("_Palette");

        public GpuBallRenderer(int capacity, int maxDynamic, float ballRadius, Bounds drawBounds,
                               Mesh nearMesh, Mesh farMesh, Color[] palette)
        {
            cs = Resources.Load<ComputeShader>("BallPitCull");
            var ballShader = Resources.Load<Shader>("BallPitInstancedBall");
            var occShader = Resources.Load<Shader>("BallPitOccluder");
            if (cs == null || ballShader == null || occShader == null)
                throw new System.InvalidOperationException("BallPitFinal: missing BallPitCull.compute / BallPitInstancedBall.shader / BallPitOccluder.shader in a Resources folder.");
            if (!SystemInfo.supportsComputeShaders) throw new System.NotSupportedException("Compute shaders not supported.");
            if ((capacity + 63) / 64 > 65535) throw new System.NotSupportedException("Capacity > 4.19M needs a 2D cull dispatch.");

            this.capacity = capacity; this.maxDynamic = Mathf.Max(1, maxDynamic);
            radius = ballRadius; this.drawBounds = drawBounds; this.nearMesh = nearMesh; this.farMesh = farMesh;
            kScatter = cs.FindKernel("CSScatter"); kReduce = cs.FindKernel("CSReduceTiles"); kCull = cs.FindKernel("CSCull");

            ballMat = new Material(ballShader) { name = "BallPitInstancedBall (runtime)" };
            occMat = new Material(occShader) { name = "BallPitOccluder (runtime)" };
            var pal = new Vector4[16];
            for (int i = 0; i < 16; i++) { Color c = palette[i % palette.Length]; pal[i] = new Vector4(c.r, c.g, c.b, 1f); }
            ballMat.SetVectorArray(IdPalette, pal);

            instances = new ComputeBuffer(capacity, 16, ComputeBufferType.Structured);
            updates = new ComputeBuffer(MaxUpdatesPerFrame, 20, ComputeBufferType.Structured);
            stats = new ComputeBuffer(2, 4, ComputeBufferType.Raw);
            for (int i = 0; i < 2; i++)
            {
                visNear[i] = new ComputeBuffer(capacity, 4, ComputeBufferType.Append);
                visFar[i] = new ComputeBuffer(capacity, 4, ComputeBufferType.Append);
                argsNear[i] = MakeArgs(nearMesh);
                argsFar[i] = MakeArgs(farMesh);
            }
            AllocateDynamic(this.maxDynamic);

            dirtyList = new int[capacity];
            dirtyFlag = new bool[capacity];
            onStats = OnStats;
        }

        private static ComputeBuffer MakeArgs(Mesh m)
        {
            var b = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            b.SetData(new uint[] { m.GetIndexCount(0), 0u, m.GetIndexStart(0), m.GetBaseVertex(0), 0u });
            return b;
        }

        public static uint Pack(BallStore store, int id)
        {
            uint p = store.color[id];
            if (store.state[id] == BallStore.State.Resting) p |= DrawableBit;
            return p;
        }

        /// <summary>Call whenever a ball enters/leaves Resting or its resting position changes.</summary>
        public void MarkDirty(int id)
        {
            if (fullUploadPending || dirtyFlag[id]) return;
            dirtyFlag[id] = true;
            dirtyList[dirtyCount++] = id;
        }

        /// <summary>Re-upload everything on the next Render (after a build/load).</summary>
        public void MarkAllDirty()
        {
            for (int i = 0; i < dirtyCount; i++) dirtyFlag[dirtyList[i]] = false;
            dirtyCount = 0;
            fullUploadPending = true;
            hasPrev = false;
        }

        /// <summary>Cull + draw resting balls for this camera. Call once per frame after the camera has moved (LateUpdate).</summary>
        public void Render(Camera cam, BallStore store)
        {
            EnsureTargets(Mathf.Max(1, cam.pixelWidth), Mathf.Max(1, cam.pixelHeight));
            int cur = parity, prev = parity ^ 1;
            cmd.Clear();

            UploadDirty(store);

            Matrix4x4 view = cam.worldToCameraMatrix;
            Matrix4x4 proj = cam.projectionMatrix;
            Matrix4x4 viewProj = proj * view;
            Matrix4x4 gpuViewProj = GL.GetGPUProjectionMatrix(proj, true) * view;

            if (OcclusionEnabled)
            {
                cmd.SetRenderTarget(occRT);
                cmd.ClearRenderTarget(true, true, new Color(1e8f, 0f, 0f, 0f), 1f);
                if (hasPrev)
                {
                    SetupOccluder(mpbOccNear, visNear[prev], gpuViewProj, view);
                    cmd.DrawMeshInstancedIndirect(nearMesh, 0, occMat, 0, argsNear[prev], 0, mpbOccNear);
                    SetupOccluder(mpbOccFar, visFar[prev], gpuViewProj, view);
                    cmd.DrawMeshInstancedIndirect(farMesh, 0, occMat, 0, argsFar[prev], 0, mpbOccFar);
                }
                cmd.SetComputeTextureParam(cs, kReduce, IdOccDepth, occRT);
                cmd.SetComputeTextureParam(cs, kReduce, IdTileMaxRW, tileRT);
                int2[0] = rtW; int2[1] = rtH;
                cmd.SetComputeIntParams(cs, IdScreenSize, int2);
                cmd.DispatchCompute(cs, kReduce, tilesX, tilesY, 1);
            }

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < 6; i++) planeVecs[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);

            cmd.SetBufferCounterValue(visNear[cur], 0);
            cmd.SetBufferCounterValue(visFar[cur], 0);
            cmd.SetComputeBufferParam(cs, kCull, IdInstances, instances);
            cmd.SetComputeBufferParam(cs, kCull, IdVisibleNear, visNear[cur]);
            cmd.SetComputeBufferParam(cs, kCull, IdVisibleFar, visFar[cur]);
            cmd.SetComputeTextureParam(cs, kCull, IdTileMax, tileRT);
            cmd.SetComputeIntParam(cs, IdInstanceCount, store.Count);
            int2[0] = tilesX; int2[1] = tilesY;
            cmd.SetComputeIntParams(cs, IdTileCount, int2);
            cmd.SetComputeVectorParam(cs, IdCullScreen, new Vector4(rtW, rtH, 0f, 0f));
            cmd.SetComputeVectorArrayParam(cs, IdFrustumPlanes, planeVecs);
            cmd.SetComputeMatrixParam(cs, IdCullView, view);
            cmd.SetComputeMatrixParam(cs, IdCullViewProj, viewProj);
            cmd.SetComputeFloatParam(cs, IdCullRadius, radius * 1.05f);
            cmd.SetComputeFloatParam(cs, IdNearClip, cam.nearClipPlane);
            cmd.SetComputeFloatParam(cs, IdProjScaleY, proj[1, 1] * rtH * 0.5f);
            cmd.SetComputeFloatParam(cs, IdLodPixelRadius, LodPixelRadius);
            cmd.SetComputeFloatParam(cs, IdOcclusionOn, OcclusionEnabled ? 1f : 0f);
            cmd.SetComputeFloatParam(cs, IdFlipY, FlipOcclusionY ? 1f : 0f);
            cmd.SetComputeFloatParam(cs, IdDepthBias, radius * 0.1f);
            cmd.DispatchCompute(cs, kCull, Mathf.Max(1, (store.Count + 63) / 64), 1, 1);
            cmd.CopyCounterValue(visNear[cur], argsNear[cur], 4);
            cmd.CopyCounterValue(visFar[cur], argsFar[cur], 4);
            cmd.CopyCounterValue(visNear[cur], stats, 0);
            cmd.CopyCounterValue(visFar[cur], stats, 4);
            Graphics.ExecuteCommandBuffer(cmd);

            mpbNear.SetBuffer(IdInstances, instances); mpbNear.SetBuffer(IdVisibleIds, visNear[cur]); mpbNear.SetFloat(IdBallRadius, radius);
            mpbFar.SetBuffer(IdInstances, instances); mpbFar.SetBuffer(IdVisibleIds, visFar[cur]); mpbFar.SetFloat(IdBallRadius, radius);
            Graphics.DrawMeshInstancedIndirect(nearMesh, 0, ballMat, drawBounds, argsNear[cur], 0, mpbNear, CastShadows, ReceiveShadows, Layer, cam);
            Graphics.DrawMeshInstancedIndirect(farMesh, 0, ballMat, drawBounds, argsFar[cur], 0, mpbFar, CastShadows, ReceiveShadows, Layer, cam);

            if ((frame++ % 10) == 0) AsyncGPUReadback.Request(stats, onStats);
            hasPrev = true;
            parity ^= 1;
        }

        /// <summary>Draw non-resting balls (active / held) from CPU positions. No culling (count is small).</summary>
        private void AllocateDynamic(int n)
        {
            dynInstances?.Release();
            identityIds?.Release();
            maxDynamic = Mathf.Max(1, n);
            dynInstances = new ComputeBuffer(maxDynamic, 16, ComputeBufferType.Structured);
            identityIds = new ComputeBuffer(maxDynamic, 4, ComputeBufferType.Structured);
            var ident = new uint[maxDynamic];
            for (int i = 0; i < ident.Length; i++) ident[i] = (uint)i;
            identityIds.SetData(ident);
        }

        public void DrawDynamic(Camera cam, BallInstance[] items, int count)
        {
            if (count <= 0) return;
            if (count > maxDynamic) AllocateDynamic(Mathf.NextPowerOfTwo(count));
            dynInstances.SetData(items, 0, 0, count);
            mpbDyn.SetBuffer(IdInstances, dynInstances);
            mpbDyn.SetBuffer(IdVisibleIds, identityIds);
            mpbDyn.SetFloat(IdBallRadius, radius);
            Graphics.DrawMeshInstancedProcedural(nearMesh, 0, ballMat, drawBounds, count, mpbDyn, CastShadows, ReceiveShadows, Layer, cam);
        }

        private void SetupOccluder(MaterialPropertyBlock mpb, ComputeBuffer visible, Matrix4x4 gpuViewProj, Matrix4x4 view)
        {
            mpb.SetBuffer(IdInstances, instances);
            mpb.SetBuffer(IdVisibleIds, visible);
            mpb.SetMatrix(IdOccViewProj, gpuViewProj);
            mpb.SetMatrix(IdOccView, view);
            mpb.SetFloat(IdBallRadius, radius);
        }

        private void UploadDirty(BallStore store)
        {
            LastUploadCount = 0;
            if (!fullUploadPending && dirtyCount > MaxUpdatesPerFrame)
            {
                for (int i = 0; i < dirtyCount; i++) dirtyFlag[dirtyList[i]] = false;
                dirtyCount = 0;
                fullUploadPending = true;
            }
            if (fullUploadPending)
            {
                var all = new NativeArray<BallInstance>(capacity, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                for (int id = 0; id < store.Count; id++) all[id] = new BallInstance { pos = store.pos[id], packed = Pack(store, id) };
                instances.SetData(all);
                all.Dispose();
                fullUploadPending = false;
                LastUploadCount = store.Count;
                return;
            }
            if (dirtyCount == 0) return;
            for (int i = 0; i < dirtyCount; i++)
            {
                int id = dirtyList[i];
                dirtyFlag[id] = false;
                updateStaging[i] = new BallUpdate { id = (uint)id, pos = store.pos[id], packed = Pack(store, id) };
            }
            updates.SetData(updateStaging, 0, 0, dirtyCount);
            cmd.SetComputeBufferParam(cs, kScatter, IdUpdates, updates);
            cmd.SetComputeBufferParam(cs, kScatter, IdInstancesRW, instances);
            cmd.SetComputeIntParam(cs, IdUpdateCount, dirtyCount);
            cmd.DispatchCompute(cs, kScatter, (dirtyCount + 63) / 64, 1, 1);
            LastUploadCount = dirtyCount;
            dirtyCount = 0;
        }

        private void EnsureTargets(int w, int h)
        {
            if (occRT != null && w == rtW && h == rtH) return;
            ReleaseTargets();
            rtW = w; rtH = h;
            tilesX = (w + 7) / 8; tilesY = (h + 7) / 8;
            occRT = new RenderTexture(w, h, 24, RenderTextureFormat.RFloat) { name = "BallPit OccluderEyeDepth", filterMode = FilterMode.Point };
            occRT.Create();
            tileRT = new RenderTexture(tilesX, tilesY, 0, RenderTextureFormat.RFloat) { name = "BallPit TileMax", enableRandomWrite = true, filterMode = FilterMode.Point };
            tileRT.Create();
        }

        private void ReleaseTargets()
        {
            if (occRT != null) { occRT.Release(); Object.Destroy(occRT); occRT = null; }
            if (tileRT != null) { tileRT.Release(); Object.Destroy(tileRT); tileRT = null; }
        }

        private void OnStats(AsyncGPUReadbackRequest r)
        {
            if (r.hasError || r.done == false) return;
            var d = r.GetData<uint>();
            if (d.Length >= 2) { DrawnNear = (int)d[0]; DrawnFar = (int)d[1]; }
        }

        public void Dispose()
        {
            AsyncGPUReadback.WaitAllRequests();
            instances?.Release(); updates?.Release(); stats?.Release(); dynInstances?.Release(); identityIds?.Release();
            for (int i = 0; i < 2; i++) { visNear[i]?.Release(); visFar[i]?.Release(); argsNear[i]?.Release(); argsFar[i]?.Release(); }
            ReleaseTargets();
            cmd.Release();
            if (ballMat != null) Object.Destroy(ballMat);
            if (occMat != null) Object.Destroy(occMat);
        }
    }
}
