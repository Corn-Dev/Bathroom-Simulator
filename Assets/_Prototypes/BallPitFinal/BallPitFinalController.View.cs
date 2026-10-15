// BallPitFinalController.View: the gameplay surface of the core runtime (deliverable A, file plan F3; design a 3.3-3.5,
// a 9, c 4). Events, read-only accessors, the load phase, the camera handover and loading view, the local view, the
// highlight draw, and the physics step-listener registry. Pit code: names no gameplay type.
using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>Coarse world lifecycle for an owner-built loading screen (with BuildProgress).</summary>
    public enum PitLoadPhase { Idle, Waiting, Building, Syncing, Ready, Failed }

    /// <summary>Gameplay hook around the physics step (FixedUpdate): host around PitAuthority.Step, session client around its own step.</summary>
    public interface IPitStepListener
    {
        void BeforePhysicsStep(float dt);   // apply forces, move kinematic proxies, add collider sources
        void AfterPhysicsStep(float dt);    // read results (poses, contacts were gathered during Simulate)
    }

    public static class PitStepOrder
    {
        public const int PlayerMotor = 0;          // locomotion and climbing; may move rb.position (step-up)
        public const int PlayerInteraction = 100;  // HandTarget[slot] from the hold point
        public const int ColliderSources = 200;    // host AddToolSource per player; client shell + live spheres
    }

    /// <summary>The registration half, so gameplay code and tests depend on an interface, not on the controller.</summary>
    public interface IPitStepRegistry
    {
        void AddStepListener(IPitStepListener listener, int order);
        void RemoveStepListener(IPitStepListener listener);
    }

    /// <summary>Copy-on-write list sorted by (order, registration sequence); changes made in a callback apply next step.
    /// Steady state allocates nothing; each callback is isolated (an exception is logged at most once per listener per 5 s).</summary>
    public sealed class StepListenerList
    {
        private struct Entry { public IPitStepListener Listener; public int Order; public long Seq; }

        private static readonly Entry[] None = new Entry[0];
        private Entry[] entries = None;
        private long nextSeq;
        private readonly System.Func<float> clock;
        private Dictionary<IPitStepListener, float> lastLogged;

        public StepListenerList(System.Func<float> clock = null) { this.clock = clock ?? (() => Time.realtimeSinceStartup); }

        public int Count => entries.Length;
        public IPitStepListener ListenerAt(int index) => entries[index].Listener;

        /// <summary>Re-adding updates the order (and the sequence); null is ignored.</summary>
        public void Add(IPitStepListener listener, int order)
        {
            if (listener == null) return;
            var list = new List<Entry>(entries.Length + 1);
            for (int i = 0; i < entries.Length; i++) if (!ReferenceEquals(entries[i].Listener, listener)) list.Add(entries[i]);
            list.Add(new Entry { Listener = listener, Order = order, Seq = nextSeq++ });
            list.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seq.CompareTo(b.Seq));
            entries = list.ToArray();
        }

        /// <summary>Idempotent; safe inside a callback.</summary>
        public void Remove(IPitStepListener listener)
        {
            if (listener == null) return;
            int at = -1;
            for (int i = 0; i < entries.Length; i++) if (ReferenceEquals(entries[i].Listener, listener)) { at = i; break; }
            if (at < 0) return;
            var next = new Entry[entries.Length - 1];
            System.Array.Copy(entries, 0, next, 0, at);
            System.Array.Copy(entries, at + 1, next, at, entries.Length - at - 1);
            entries = next;
        }

        public void Run(bool before, float dt)
        {
            Entry[] snapshot = entries;
            for (int i = 0; i < snapshot.Length; i++)
            {
                IPitStepListener l = snapshot[i].Listener;
                try { if (before) l.BeforePhysicsStep(dt); else l.AfterPhysicsStep(dt); }
                catch (System.Exception e)
                {
                    float now = clock();
                    if (lastLogged == null) lastLogged = new Dictionary<IPitStepListener, float>();
                    if (lastLogged.TryGetValue(l, out float last) && now - last < 5f) continue;
                    lastLogged[l] = now;
                    Debug.LogException(e);
                }
            }
        }
    }

    public sealed partial class BallPitFinalController : IPitStepRegistry
    {
        // ------------------------------------------------------------------ Events (isolated, allocation-free raise)

        /// <summary>Copy-on-write handler array with per-handler exception isolation (at most one log per 5 s).</summary>
        private sealed class HandlerList<T> where T : class
        {
            private static readonly T[] None = new T[0];
            private T[] items = None;
            private Dictionary<T, float> lastLogged;

            public T[] Items => items;

            public void Add(T handler)
            {
                if (handler == null) return;
                var next = new T[items.Length + 1];
                System.Array.Copy(items, next, items.Length);
                next[items.Length] = handler;
                items = next;
            }

            public void Remove(T handler)
            {
                if (handler == null) return;
                for (int i = items.Length - 1; i >= 0; i--)
                {
                    if (!items[i].Equals(handler)) continue;
                    var next = new T[items.Length - 1];
                    System.Array.Copy(items, 0, next, 0, i);
                    System.Array.Copy(items, i + 1, next, i, items.Length - i - 1);
                    items = next;
                    return;
                }
            }

            public void Report(T handler, System.Exception e, string eventName)
            {
                float now = Time.realtimeSinceStartup;
                if (lastLogged == null) lastLogged = new Dictionary<T, float>();
                if (lastLogged.TryGetValue(handler, out float last) && now - last < 5f) return;
                lastLogged[handler] = now;
                Debug.LogError($"[BallPitFinal] a {eventName} handler threw (logged at most once per 5 s per handler)");
                Debug.LogException(e);
            }
        }

        private readonly HandlerList<System.Action<PitWorld, IPitSite>> pitWorldBuiltHandlers = new HandlerList<System.Action<PitWorld, IPitSite>>();
        private readonly HandlerList<System.Action<PitWorld>> pitWorldDisposedHandlers = new HandlerList<System.Action<PitWorld>>();
        private readonly HandlerList<System.Action<Camera>> viewCameraChangedHandlers = new HandlerList<System.Action<Camera>>();

        /// <summary>The world is built and gameplay-ready (IsPitWorldReady already true): host after Restore and NetPit.StartHost,
        /// before Ready; client on the first Update with NetPit.Synced; standalone at the end of each build. Site = ActiveSite.</summary>
        public event System.Action<PitWorld, IPitSite> PitWorldBuilt { add => pitWorldBuiltHandlers.Add(value); remove => pitWorldBuiltHandlers.Remove(value); }

        /// <summary>From DisposePit, before the environment teardown and any disposal (the world is still readable).
        /// Only for a world whose PitWorldBuilt was raised. IsPitWorldReady is already false.</summary>
        public event System.Action<PitWorld> PitWorldDisposed { add => pitWorldDisposedHandlers.Add(value); remove => pitWorldDisposedHandlers.Remove(value); }

        /// <summary>RenderCamera changed identity (it may be null during teardown).</summary>
        public event System.Action<Camera> ViewCameraChanged { add => viewCameraChangedHandlers.Add(value); remove => viewCameraChangedHandlers.Remove(value); }

        private void RaisePitWorldBuilt(PitWorld w, IPitSite site)
        {
            var hs = pitWorldBuiltHandlers.Items;
            for (int i = 0; i < hs.Length; i++)
                try { hs[i](w, site); } catch (System.Exception e) { pitWorldBuiltHandlers.Report(hs[i], e, nameof(PitWorldBuilt)); }
        }

        private void RaisePitWorldDisposed(PitWorld w)
        {
            var hs = pitWorldDisposedHandlers.Items;
            for (int i = 0; i < hs.Length; i++)
                try { hs[i](w); } catch (System.Exception e) { pitWorldDisposedHandlers.Report(hs[i], e, nameof(PitWorldDisposed)); }
        }

        private void RaiseViewCameraChanged(Camera c)
        {
            var hs = viewCameraChangedHandlers.Items;
            for (int i = 0; i < hs.Length; i++)
                try { hs[i](c); } catch (System.Exception e) { viewCameraChangedHandlers.Report(hs[i], e, nameof(ViewCameraChanged)); }
        }

        /// <summary>Sets the world ready and raises PitWorldBuilt once per built world.</summary>
        private void MarkWorldReady()
        {
            if (pitWorldReady || world == null) return;
            pitWorldReady = true;
            RaisePitWorldBuilt(world, activeSite);
        }

        // ------------------------------------------------------------------ Read-only accessors

        /// <summary>The environment for the current and next build (a site's collision proxies); null = dev layout.</summary>
        public IPitEnvironment Environment { get; private set; }
        public PitReplica Replica => replica;
        public Mode CurrentMode => mode;
        public IPitSite ActiveSite => activeSite;
        public BallVisualConfig BallVisuals => ballVisuals;
        public bool IsPitWorldReady => pitWorldReady;
        public float BuildProgress => pitWorldReady ? 1f : Mathf.Clamp01(buildProgress);

        public PitLoadPhase LoadPhase
        {
            get
            {
                if (hostFailed || clientBuildFailed) return PitLoadPhase.Failed;
                if (pitWorldReady) return PitLoadPhase.Ready;
                if (building) return PitLoadPhase.Building;
                if (world != null && mode == Mode.Client) return PitLoadPhase.Syncing; // built, not yet synced
                if (pending != null || clientRequest != null) return PitLoadPhase.Waiting;
                return PitLoadPhase.Idle;
            }
        }

        /// <summary>The pit slot of this peer: 0 offline or host; on a client NetPit.LocalPlayer; -1 unknown.</summary>
        public int LocalSlot => mode == Mode.Client ? (netPit != null ? netPit.LocalPlayer : -1) : 0;

        /// <summary>The ball this peer holds, from authoritative state (host HeldId, client NetPit.LocalHeld); -1 none.</summary>
        public int LocalHeldBall
        {
            get
            {
                if (authority != null)
                {
                    int s = LocalSlot;
                    return (uint)s < PitAuthority.MaxPlayers ? authority.HeldId[s] : -1;
                }
                return netPit != null ? netPit.LocalHeld : -1;
            }
        }

        /// <summary>The camera registered through SetViewCamera, or null.</summary>
        public Camera ViewCamera => viewCamera;
        /// <summary>The camera the balls are rendered with this frame (registered camera, loading view, or dev camera).</summary>
        public Camera RenderCamera => renderCamera;

        /// <summary>Dynamic instances drawn by the last LateUpdate (the controller's own buffers: read only, valid until the
        /// next LateUpdate). Pass them to PitWorld.RaycastSpheres to pick moving balls as drawn.</summary>
        public int GetDrawnDynamic(out BallInstance[] instances, out int[] ids)
        {
            instances = dynamicInstances;
            ids = dynamicIds;
            return dynamicCount;
        }

        /// <summary>Installed by gameplay input in a session: true while a menu, modal or panel owns the cursor. The dev
        /// harness ignores its keys while it is true. Null = never blocked.</summary>
        public System.Func<bool> InputBlocked;

        // ------------------------------------------------------------------ Camera handover and loading view

        private Camera viewCamera;     // registered (SetViewCamera)
        private Camera renderCamera;   // resolved
        private Camera loadingCamera;  // the loading-view camera currently switched on (overview or fallback)
        private Camera fallbackCamera; // created loading view, parented under the controller
        private bool warnedViewDestroyed;

        /// <summary>Registers (non-null) or unregisters (null) the gameplay render camera. The caller enables its own Camera
        /// and AudioListener; registering turns the loading view off. Last call wins.</summary>
        public void SetViewCamera(Camera camera)
        {
            if (camera != null)
            {
                viewCamera = camera;
                warnedViewDestroyed = false;
                HideLoadingView();
                if (devCam != null) devCam.enabled = false;
            }
            else
            {
                viewCamera = null;
                if (!SessionDriven && devCam != null) devCam.enabled = true;
            }
            ResolveRenderCamera();
        }

        /// <summary>Session: shows the loading view while no registered camera renders. Cheap and idempotent.</summary>
        private void RefreshLoadingView()
        {
            if (SessionDriven) ResolveRenderCamera();
        }

        /// <summary>Picks RenderCamera: a live, enabled registered camera; else the loading view (session) or the dev
        /// camera (standalone). Raises ViewCameraChanged on an identity change.</summary>
        private Camera ResolveRenderCamera()
        {
            if (!ReferenceEquals(viewCamera, null) && viewCamera == null)
            {
                viewCamera = null;
                if (!warnedViewDestroyed)
                {
                    warnedViewDestroyed = true;
                    Debug.LogWarning("[BallPitFinal] view camera destroyed without SetViewCamera(null)");
                }
                if (!SessionDriven && devCam != null) devCam.enabled = true;
            }
            Camera next;
            if (viewCamera != null && viewCamera.isActiveAndEnabled)
            {
                if (loadingCamera != null || fallbackCamera != null) HideLoadingView();
                next = viewCamera;
            }
            else next = SessionDriven ? ShowLoadingView() : devCam;
            if (next != renderCamera)
            {
                renderCamera = next;
                RaiseViewCameraChanged(next);
            }
            return renderCamera;
        }

        // Creates or enables nothing while the controller is destroyed, the scene unloads, or the session was detached.
        private Camera ShowLoadingView()
        {
            if (destroying || !gameObject.scene.isLoaded || sessionDetached) return null;
            if (overviewCamera != null)
            {
                if (fallbackCamera != null) DestroyFallbackCamera();
                if (loadingCamera != overviewCamera)
                {
                    overviewCamera.enabled = true;
                    var listener = overviewCamera.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = true;
                    loadingCamera = overviewCamera;
                }
                return overviewCamera;
            }
            if (fallbackCamera == null) fallbackCamera = CreateFallbackCamera();
            loadingCamera = fallbackCamera;
            return fallbackCamera;
        }

        private void HideLoadingView()
        {
            if (overviewCamera != null)
            {
                overviewCamera.enabled = false;
                var listener = overviewCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
            DestroyFallbackCamera();
            loadingCamera = null;
        }

        private void DestroyFallbackCamera()
        {
            if (fallbackCamera != null) Destroy(fallbackCamera.gameObject);
            fallbackCamera = null;
        }

        // Default camera (MainCamera) + listener, framed like the dev orbit (yaw 35, pitch 35, 1.4 x the larger pit size) on
        // the world, else the host request, else 60 m from the origin. It renders the authored venue; no geometry, no light.
        private Camera CreateFallbackCamera()
        {
            var go = new GameObject("BallPit Loading View");
            go.tag = "MainCamera";
            var c = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            Vector3 center = Vector3.zero;
            float d = 60f;
            if (world != null)
            {
                center = new Vector3(world.PitCenter.x, world.FloorY + world.PitSize.y * 0.6f, world.PitCenter.z);
                d = Mathf.Max(world.PitSize.x, world.PitSize.z) * 1.4f;
            }
            else if (pending != null && pending.Settings != null)
            {
                PitWorld.ComputeInterior(pending.Settings, out float x0, out float x1, out float z0, out float z1, out float floorY);
                center = new Vector3((x0 + x1) * 0.5f, floorY, (z0 + z1) * 0.5f);
                d = Mathf.Max(x1 - x0, z1 - z0) * 1.4f;
            }
            Quaternion rot = Quaternion.Euler(35f, 35f, 0f);
            go.transform.position = center + rot * new Vector3(0f, 0f, -d);
            go.transform.rotation = Quaternion.LookRotation(center - go.transform.position, Vector3.up);
            go.transform.SetParent(transform, worldPositionStays: true);
            return c;
        }

        // ------------------------------------------------------------------ Local view (the one SetView per frame)

        private struct LocalView { public Vector3 ViewPos, HoldPos, ToolPoint; public byte Tool; public int Frame; }
        private LocalView localView = new LocalView { Frame = -1 };

        private bool LocalViewFresh => localView.Frame == Time.frameCount;

        /// <summary>This frame's local view (head, render-time hold point, tool fields): clients send it as the one NetPit.SetView
        /// and hand; the host draws its held ball at holdPos. A view not set this frame is ignored. Never writes HandTarget.</summary>
        public void SetLocalView(Vector3 viewPos, Vector3 holdPos, byte tool, Vector3 toolPoint)
        {
            localView = new LocalView { ViewPos = viewPos, HoldPos = holdPos, Tool = tool, ToolPoint = toolPoint, Frame = Time.frameCount };
        }

        // Client, from Update: standalone sends today's dev view; a session sends the fresh local view (or the render
        // camera position with ToolNone), with the dev tool fields on top while a dev tool key is held.
        private void PushLocalView()
        {
            if (netPit == null) return;
            if (!SessionDriven)
            {
                DevToolOverride(out byte devTool, out Vector3 devPoint);
                netPit.SetView(devCam != null ? devCam.transform.position : Vector3.zero, DevHandPos(), devTool, devPoint);
                return;
            }
            Vector3 view, hand, point;
            byte tool;
            if (LocalViewFresh) { view = localView.ViewPos; hand = localView.HoldPos; tool = localView.Tool; point = localView.ToolPoint; }
            else
            {
                view = hand = renderCamera != null ? renderCamera.transform.position : Vector3.zero;
                tool = NetPit.ToolNone; point = Vector3.zero;
            }
            if (DevToolOverride(out byte t, out Vector3 p)) { tool = t; point = p; MarkDevToolUsed(LocalSlot); }
            netPit.SetView(view, hand, tool, point);
        }

        // Host, from LateUpdate after the active pool's FillInstances: the local held ball is drawn at the hold point.
        private void DrawLocalHeldAtHoldPoint()
        {
            if (!LocalViewFresh || authority == null) return;
            int held = LocalHeldBall;
            if (held < 0) return;
            for (int i = 0; i < dynamicCount; i++)
                if (dynamicIds[i] == held) { dynamicInstances[i].pos = localView.HoldPos; break; }
        }

        /// <summary>Today's highlight (near mesh, highlight material, 1.12 x the ball diameter) for RenderCamera.</summary>
        public void DrawHighlight(Vector3 center)
        {
            Camera rc = renderCamera;
            if (rc == null || nearMesh == null || highlightMat == null) return;
            float s = (world != null ? world.Diameter : settings.ballDiameter) * 1.12f;
            Graphics.DrawMesh(nearMesh, Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * s), highlightMat, 0, rc);
        }

        // ------------------------------------------------------------------ Physics step listeners

        private readonly StepListenerList stepListeners = new StepListenerList();

        [Tooltip("Session clients step PhysX once per FixedUpdate (local player, client colliders). Off = no client physics, today's behaviour.")]
        public bool clientPhysicsStepping = true;

        /// <summary>Re-adding updates the order; null ignored.</summary>
        public void AddStepListener(IPitStepListener listener, int order) => stepListeners.Add(listener, order);
        /// <summary>Idempotent; safe inside a callback.</summary>
        public void RemoveStepListener(IPitStepListener listener) => stepListeners.Remove(listener);

        private void RunStepListeners(bool before, float dt) => stepListeners.Run(before, dt);

        /// <summary>Session client: step at the host's fixed step (NetPit.HostDt) when it is in the physicsHz range.</summary>
        private void AdoptHostStep(float hostDt)
        {
            if (!float.IsNaN(hostDt) && !float.IsInfinity(hostDt) && hostDt >= 1f / 120f && hostDt <= 1f / 20f)
                Time.fixedDeltaTime = hostDt;
            else
                Debug.LogWarning($"[BallPitFinal] host step {hostDt} s is outside [1/120, 1/20] s; keeping {Time.fixedDeltaTime} s");
        }
    }
}
