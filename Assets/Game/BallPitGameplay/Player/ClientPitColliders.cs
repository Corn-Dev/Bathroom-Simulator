// ClientPitColliders: the session client's player-pit driver (deliverable A, file plan P14; design d.5, d.6).
// One per built world on a client whose PitWorld has a collider shell (PlayerSpawner creates it on PitWorldBuilt and
// disposes it on PitWorldDisposed). Every fixed step, before the client's own Physics.Simulate, it fills the shell
// around the local player from the replicated Resting grid, and places a small pool of kinematic spheres on the moving
// balls nearest the local capsule at their interpolated poses (the track the renderer draws), so the local player
// stands on the pile and bumps into rolling balls where it sees them. The push is one way: the client never moves a ball.
using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class ClientPitColliders : IPitStepListener, System.IDisposable
    {
        private readonly PitWorld world;
        private readonly PitReplica replica;
        private readonly System.Func<float> playbackTime;
        private readonly System.Func<bool> synced;
        private readonly PlayerPitPhysicsConfig cfg;
        private readonly List<Vector4> sources = new List<Vector4>(4);
        private readonly List<int> scratch = new List<int>(64);
        private int step;
        private bool disposed, warnedCap;
        private PlayerAvatar checkedAvatar; // PlayerCapsules.Check runs once per avatar this driver sees (d.8)

        // Live-ball proxies (d.6): pooled per world, because the radius follows the profile's ball size.
        private readonly int budget;
        private readonly float proxyRadius;
        private readonly GameObject poolRoot;
        private readonly Transform[] proxyTr;
        private readonly Rigidbody[] proxyRb;
        private readonly SphereCollider[] proxyCol;
        private readonly int[] proxyBall, proxyStamp, freeStack;
        private readonly Dictionary<int, int> proxyOfBall;
        private int freeCount, stamp;
        private BallInstance[] buf = new BallInstance[0];
        private int[] ids = new int[0], cand = new int[0];
        private float[] keys = new float[0];

        /// <summary>Shell colliders this driver currently keeps alive (ColliderShell.Live).</summary>
        public int ShellColliders => world.Colliders != null ? world.Colliders.Live : 0;
        /// <summary>Live-ball proxies placed this step.</summary>
        public int ActiveProxies { get; private set; }
        public int ProxyBudget => budget;

        /// <summary>The session driver (design d.10): NetPit supplies the playback clock and the synced flag.</summary>
        public ClientPitColliders(BallPitFinalController controller, PitWorld world, PitReplica replica, NetPit net,
                                  PlayerPitPhysicsConfig physics)
            : this(world, replica ?? (controller != null ? controller.Replica : null),
                   PlaybackOf(net ?? (controller != null ? controller.Net : null)), SyncedOf(net ?? (controller != null ? controller.Net : null)),
                   physics) { }

        /// <summary>Test constructor (design d.14): the clock and the synced flag without NetPit, which needs NGO.</summary>
        public ClientPitColliders(PitWorld world, PitReplica replica, System.Func<float> playbackTime, System.Func<bool> synced,
                                  PlayerPitPhysicsConfig physics)
        {
            this.world = world ?? throw new System.ArgumentNullException(nameof(world));
            this.replica = replica;
            this.playbackTime = playbackTime ?? (() => 0f);
            this.synced = synced ?? (() => false);
            cfg = physics ?? new PlayerPitPhysicsConfig();
            cfg.Sanitize();
            if (!cfg.clientLiveBallColliders || replica == null) return; // off: no pool, no per-step work

            budget = Mathf.Clamp(cfg.liveProxyBudget, 1, 1024);
            proxyRadius = world.ColliderRadius * cfg.liveProxyRadiusScale;
            proxyTr = new Transform[budget];
            proxyRb = new Rigidbody[budget];
            proxyCol = new SphereCollider[budget];
            proxyBall = new int[budget];
            proxyStamp = new int[budget];
            freeStack = new int[budget];
            proxyOfBall = new Dictionary<int, int>(2 * budget); // no 2.35M-entry array on the client
            poolRoot = new GameObject("BallPitGameplay_LiveBallProxies"); // scene root
            for (int k = 0; k < budget; k++)
            {
                var go = new GameObject("LiveBallProxy");
                go.layer = world.S.ballLayer;
                go.transform.SetParent(poolRoot.transform, false);
                var col = go.AddComponent<SphereCollider>();
                col.radius = proxyRadius;
                col.sharedMaterial = world.Material;
                col.enabled = false;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                proxyTr[k] = go.transform; proxyRb[k] = rb; proxyCol[k] = col;
                proxyBall[k] = -1;
                freeStack[freeCount++] = budget - 1 - k; // pops 0, 1, 2, ...
            }
        }

        private static System.Func<float> PlaybackOf(NetPit net) => net != null ? (System.Func<float>)(() => net.PlaybackTime) : null;
        private static System.Func<bool> SyncedOf(NetPit net) => net != null ? (System.Func<bool>)(() => net.Synced) : null;

        // ------------------------------------------------------------------ Step (order PitStepOrder.ColliderSources)

        /// <summary>Client step: listeners Before (motor 0, interaction 100, this 200), SyncTransforms, Simulate, After.</summary>
        public void BeforePhysicsStep(float dt)
        {
            if (disposed) return;
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null || local.Motor == null || !synced()) return; // before the player object arrives; defensive sync check
            if (!ReferenceEquals(local, checkedAvatar)) { checkedAvatar = local; PlayerCapsules.Check(local); }
            ColliderShell shell = world.Colliders;
            step++; // clients have no PitAuthority.StepIndex

            // 3. Shell, every step: the same spheres the host uses (PlayerMotor.ComputeShellSources), from the Resting grid.
            if (shell != null)
            {
                local.Motor.GetShellSources(sources);
                for (int s = 0; s < sources.Count; s++)
                {
                    Vector4 v = sources[s];
                    world.Resting.Gather(new Vector3(v.x, v.y, v.z), v.w, world.Store.pos, scratch);
                    for (int i = 0; i < scratch.Count; i++) shell.Ensure(scratch[i], world.Store.pos[scratch[i]], step);
                }
                // 4. Expire: only the local player is a source, so a short keep shortens the trail without churn.
                int keep = dt > 0f ? Mathf.Max(1, Mathf.RoundToInt(cfg.clientShellKeepSeconds / dt)) : 1;
                shell.Expire(step, keep, world.S.colliderReleasePerStep);
            }

            // 5. Live-ball proxies.
            if (poolRoot != null) UpdateProxies(local);

            // 6. Capacity.
            if (shell != null && !warnedCap && shell.CapHits > 0)
            {
                warnedCap = true;
                Debug.LogWarning($"[ClientPitColliders] the client collider shell is full ({shell.Capacity:n0} colliders); " +
                                 "raise PlayerPitPhysicsConfig.clientShellMaxColliders on the PlayerSpawner");
            }
        }

        public void AfterPhysicsStep(float dt) { }

        // ------------------------------------------------------------------ Live-ball proxies (d.6)

        private void UpdateProxies(PlayerAvatar local)
        {
            stamp++;
            int count = replica.Count;
            int selected = 0;
            if (count > 0 && PlayerCapsules.GetLiveAxis(local, out Vector3 a, out Vector3 b, out float capsuleRadius))
            {
                // 1. Sample: the interpolated pose of every live ball at playback time, into this driver's own buffers
                //    (never GetDrawnDynamic: one frame stale at FixedUpdate, and the host-only held-ball override).
                EnsureBuffers(count);
                int n = replica.FillInstances(buf, ids, playbackTime());

                // 2. Filter: Active only (held balls never get a proxy), within range of the local capsule axis.
                float range2 = cfg.liveProxyRange * cfg.liveProxyRange;
                int m = 0;
                for (int i = 0; i < n; i++)
                {
                    int id = ids[i];
                    if ((uint)id >= (uint)world.Store.Count || world.Store.state[id] != BallStore.State.Active) continue;
                    float key = Geometry.PointSegmentSqrDistance(buf[i].pos, a, b);
                    if (key >= range2) continue;
                    keys[m] = key; cand[m] = i; m++;
                }

                // 3. Select the nearest budget (the layer touching the capsule first).
                if (m > budget) System.Array.Sort(keys, cand, 0, m);
                int take = Mathf.Min(m, budget);
                float inside = capsuleRadius + proxyRadius - 0.01f;
                float inside2 = inside > 0f ? inside * inside : 0f;

                // 4. Assign.
                for (int j = 0; j < take; j++)
                {
                    int i = cand[j];
                    int id = ids[i];
                    Vector3 pose = buf[i].pos;
                    if (proxyOfBall.TryGetValue(id, out int k))
                    {
                        Rigidbody rb = proxyRb[k];
                        if ((pose - rb.position).sqrMagnitude > cfg.liveProxyTeleportDistance * cfg.liveProxyTeleportDistance)
                        { proxyTr[k].position = pose; rb.position = pose; } // a resync jump or a dumped ball
                        else rb.MovePosition(pose); // a real kinematic velocity: it pushes the player at the ball's speed
                        proxyStamp[k] = stamp;
                        selected++;
                        continue;
                    }
                    if (keys[j] < inside2 || freeCount == 0) continue; // drawn inside the player: a proxy would pop it out
                    k = freeStack[--freeCount];
                    proxyTr[k].position = pose; // the transform too, so the following SyncTransforms keeps this pose
                    proxyRb[k].position = pose;
                    proxyCol[k].enabled = true;
                    proxyBall[k] = id;
                    proxyOfBall[id] = k;
                    proxyStamp[k] = stamp;
                    selected++;
                }
            }

            // 5. Release every proxy whose ball was not selected this step (out of range, over budget, held, no longer live).
            for (int k = 0; k < budget; k++)
                if (proxyBall[k] >= 0 && proxyStamp[k] != stamp) Release(k);
            ActiveProxies = selected;
        }

        // Proxy-to-shell handoff: a ball that came to rest this frame gets its shell collider in the same step, so the
        // player never loses support (its last moving pose and its frozen pose coincide).
        private void Release(int k)
        {
            int id = proxyBall[k];
            proxyCol[k].enabled = false;
            proxyBall[k] = -1;
            proxyOfBall.Remove(id);
            freeStack[freeCount++] = k;
            ColliderShell shell = world.Colliders;
            if (shell != null && (uint)id < (uint)world.Store.Count && world.Store.state[id] == BallStore.State.Resting)
                shell.Ensure(id, world.Store.pos[id], step);
        }

        // FillInstances clamps to the buffer length, so the buffers grow to cover every live ball.
        private void EnsureBuffers(int count)
        {
            if (buf.Length >= count) return;
            int size = Mathf.NextPowerOfTwo(count);
            buf = new BallInstance[size];
            ids = new int[size];
            keys = new float[size];
            cand = new int[size];
        }

        // ------------------------------------------------------------------ Dispose (PitWorldDisposed, EndSession)

        /// <summary>Destroys the proxy pool. The shell itself dies with the world. Idempotent.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (poolRoot != null) { poolRoot.SetActive(false); Object.Destroy(poolRoot); }
            proxyOfBall?.Clear();
            ActiveProxies = 0;
        }
    }
}
