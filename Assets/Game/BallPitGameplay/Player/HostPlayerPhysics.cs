// HostPlayerPhysics: the host's player-pit driver (deliverable A, file plan P13; design d.3, d.4, d.7, d.8, 8.4).
// One per built world on the host: PlayerSpawner creates it on PitWorldBuilt and disposes it on PitWorldDisposed. Every
// step it adds each player's collider-shell sources (the pile under every player is solid), moves a kinematic pusher per
// remote player (remote players push rolling balls at walking speed), and keeps every capsule from colliding with held
// balls (plus a release grace). It installs PitAuthority.FreezeBlocker (no ball freezes on a player), GrabFilter (reach)
// and PlayerCapsules.HostRemoteAxis, and removes each on Dispose only if it is still this driver's. FreezeBlockRule is
// below; HeldBallPairs is in Venue/OutOfBoundsVolume.cs (the 460-line cap, file plan 12.10 finding 1).
using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class HostPlayerPhysics : IPitStepListener, System.IDisposable
    {
        private sealed class Pusher
        {
            public PlayerAvatar Avatar;
            public GameObject Go;
            public Rigidbody Rb;
            public CapsuleCollider Capsule, RootCapsule;
            public Vector3 Target, LastTarget;
            public Quaternion Yaw = Quaternion.identity;
            public bool HasTarget;
        }

        private readonly PitAuthority authority;
        private readonly PitWorld world;
        private readonly PlayerPitPhysicsConfig cfg;
        private readonly float underFootCos;
        private readonly System.Func<int, Vector3, bool> freezeBlocker;
        private readonly System.Func<int, int, bool> grabFilter;
        private readonly System.Action<int, SphereCollider> beforeDemote;
        private readonly CapsuleAxisProvider remoteAxis;
        private readonly System.Func<int, bool> isHeld;
        private readonly System.Func<int, int, bool> isClear;
        private readonly Dictionary<PlayerAvatar, Pusher> pushers = new Dictionary<PlayerAvatar, Pusher>();
        private readonly List<PlayerAvatar> gone = new List<PlayerAvatar>();
        private readonly List<Vector4> sources = new List<Vector4>(4);
        private readonly HeldBallPairs pairs;
        private readonly Dictionary<int, CapsuleCollider> capsuleOfKey = new Dictionary<int, CapsuleCollider>();
        private readonly int[] heldIds = new int[PitAuthority.MaxPlayers];
        private int heldCount;
        private Vector3[] segA = new Vector3[4], segB = new Vector3[4];
        private float[] segRadius = new float[4];
        private int capsuleCount, segmentsStep = -1, lastCapHits;
        private Bounds blockAabb;
        private bool disposed, warnedCap, warnedGrab, warnedSegments;

        /// <summary>The controller whose world this driver serves (diagnostics and B).</summary>
        public BallPitFinalController Controller { get; }
        /// <summary>Balls the freeze blocker kept active in the last Step (reset in BeforePhysicsStep).</summary>
        public int BlockedThisStep { get; private set; }
        /// <summary>Grabs rejected by the reach check (a diagnostic counter; rejections are never logged per click).</summary>
        public int RejectedGrabs { get; private set; }
        public int PusherCount => pushers.Count;
        public int PairCount => pairs.Count;

        public HostPlayerPhysics(BallPitFinalController controller, PitAuthority authority, PitWorld world, PlayerPitPhysicsConfig physics)
        {
            this.authority = authority ?? throw new System.ArgumentNullException(nameof(authority));
            this.world = world ?? throw new System.ArgumentNullException(nameof(world));
            Controller = controller;
            cfg = physics ?? new PlayerPitPhysicsConfig();
            cfg.Sanitize();
            underFootCos = Mathf.Cos(cfg.underFootAngleDeg * Mathf.Deg2Rad);
            pairs = new HeldBallPairs(ApplyPair);
            freezeBlocker = BlocksFreeze;
            grabFilter = GrabAllowed;
            beforeDemote = OnBeforeDemote;
            remoteAxis = TryGetCapsuleAxis;
            isHeld = IsHeldNow;
            isClear = IsClearOfCapsule;
            authority.FreezeBlocker = freezeBlocker;
            authority.GrabFilter = grabFilter;
            authority.Active.BeforeDemote += beforeDemote;
            PlayerCapsules.HostRemoteAxis = remoteAxis;
            lastCapHits = world.Colliders != null ? world.Colliders.CapHits : 0;
            PlayerAvatar.AnySpawned += OnAnySpawned;
            PlayerAvatar.AnyDespawned += OnAnyDespawned;
            IReadOnlyList<PlayerAvatar> all = PlayerAvatar.All; // avatars that spawned before this world (d.8)
            for (int i = 0; i < all.Count; i++) OnAnySpawned(all[i]);
        }

        // ------------------------------------------------------------------ Step (order PitStepOrder.ColliderSources)

        /// <summary>Before PitAuthority.Step, after the motor and interaction listeners (design d.4 steps 1-5).</summary>
        public void BeforePhysicsStep(float dt)
        {
            if (disposed) return;
            BlockedThisStep = 0;
            segmentsStep = -1; // the freeze blocker re-reads the post-Simulate poses

            // 1. Prune pushers whose avatar was destroyed without a despawn.
            gone.Clear();
            foreach (KeyValuePair<PlayerAvatar, Pusher> kv in pushers) if (kv.Key == null) gone.Add(kv.Key);
            for (int i = 0; i < gone.Count; i++) RemoveAvatar(gone[i]);

            // 2-3. Pushers and shell sources, every step, for every capsule on this peer.
            IReadOnlyList<PlayerAvatar> all = PlayerAvatar.All;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerAvatar a = all[i];
                if (a == null) continue;
                if (a.Role == PlayerRole.Local)
                {
                    if (a.Motor == null) continue;
                    a.Motor.GetShellSources(sources);
                    AddSources();
                }
                else if (a.Role == PlayerRole.Remote)
                {
                    if (!pushers.TryGetValue(a, out Pusher p)) { CreatePusher(a); if (!pushers.TryGetValue(a, out p)) continue; }
                    Vector3 velocity = DrivePusher(p, dt);
                    CapsuleCollider c = a.Capsule;
                    if (c == null || a.Config == null) continue; // destroyed mid-step
                    PlayerMotor.ComputeShellSources(p.Target, velocity, dt, c.radius, c.height, a.Config, world.Diameter, sources);
                    AddSources();
                }
            }

            // 4. Held-ball ignore pairs (d.7).
            UpdatePairs();

            ColliderShell shell = world.Colliders;
            if (shell == null) return;
            if (!warnedCap && shell.CapHits > lastCapHits)
            {
                warnedCap = true;
                Debug.LogWarning($"[HostPlayerPhysics] the collider shell is full ({shell.Capacity:n0} colliders): resting balls far " +
                                 "from the newest sources have no collider. Raise PitSettings.maxColliders.");
            }
            lastCapHits = shell.CapHits;
        }

        public void AfterPhysicsStep(float dt) { }

        private void AddSources()
        {
            for (int k = 0; k < sources.Count; k++)
            {
                Vector4 s = sources[k];
                authority.AddToolSource(new Vector3(s.x, s.y, s.z), s.w); // RefreshColliders ensures them inside this Step
            }
        }

        // ------------------------------------------------------------------ Remote-player pushers (d.4)

        private void CreatePusher(PlayerAvatar a)
        {
            CapsuleCollider root = a.Capsule;
            if (root == null || pushers.ContainsKey(a)) return;
            Transform t = a.transform;
            Quaternion yaw = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
            int slot = a.Slot != null ? a.Slot.Index : -1;
            var go = new GameObject($"PlayerPusher (slot {slot})"); // scene root: writes to the player's transform never move it
            go.layer = PlayerLayers.Player >= 0 ? PlayerLayers.Player : root.gameObject.layer;
            go.transform.SetPositionAndRotation(t.position, yaw); // positioned first: autoSyncTransforms is off
            var c = go.AddComponent<CapsuleCollider>();
            c.center = root.center; c.radius = root.radius; c.height = root.height; c.direction = root.direction;
            c.sharedMaterial = root.sharedMaterial;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            root.enabled = false; // one physics body per player on the host: the pusher is this player's capsule
            pushers.Add(a, new Pusher { Avatar = a, Go = go, Rb = rb, Capsule = c, RootCapsule = root, Yaw = yaw });
        }

        // Target = the root pose ClientNetworkTransform wrote in Update (yaw only). MovePosition carries a kinematic
        // velocity into the solver, so balls are pushed at the player's speed (one way). Returns the estimated velocity.
        private Vector3 DrivePusher(Pusher p, float dt)
        {
            Transform t = p.Avatar.transform;
            Vector3 target = t.position;
            Quaternion yaw = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
            if (!p.HasTarget) { p.LastTarget = target; p.HasTarget = true; }
            Vector3 v = dt > 0f ? (target - p.LastTarget) / dt : Vector3.zero;
            if (v.sqrMagnitude > cfg.sourceMaxSpeed * cfg.sourceMaxSpeed) v = v.normalized * cfg.sourceMaxSpeed; // a teleport inflates one step
            p.LastTarget = target;
            p.Target = target;
            p.Yaw = yaw;
            if (p.Rb == null) return v;
            if ((target - p.Rb.position).sqrMagnitude > cfg.pusherTeleportDistance * cfg.pusherTeleportDistance)
            {
                p.Go.transform.SetPositionAndRotation(target, yaw);
                p.Rb.position = target;
                p.Rb.rotation = yaw;
            }
            else
            {
                p.Rb.MovePosition(target);
                p.Rb.MoveRotation(yaw);
            }
            return v;
        }

        private void OnAnySpawned(PlayerAvatar a)
        {
            if (disposed || a == null) return;
            PlayerCapsules.Check(a);
            if (a.Role == PlayerRole.Remote) CreatePusher(a);
        }

        private void OnAnyDespawned(PlayerAvatar a)
        {
            if (!disposed && !ReferenceEquals(a, null)) RemoveAvatar(a);
        }

        // The avatar's pairs are restored (where the call guard passes) and dropped first, then its pusher is destroyed
        // and the root capsule re-enabled if the root still exists.
        private void RemoveAvatar(PlayerAvatar a)
        {
            CapsuleCollider own = a != null ? a.Capsule : null;
            if (!ReferenceEquals(own, null)) DropCapsule(own);
            if (!pushers.TryGetValue(a, out Pusher p)) return;
            pushers.Remove(a);
            if (!ReferenceEquals(p.Capsule, null)) DropCapsule(p.Capsule);
            DestroyPusher(p);
        }

        private void DropCapsule(CapsuleCollider c)
        {
            int key = c.GetInstanceID();
            pairs.RemoveCapsule(key, restore: true);
            capsuleOfKey.Remove(key);
        }

        private static void DestroyPusher(Pusher p)
        {
            if (p.Go != null) { p.Go.SetActive(false); Object.Destroy(p.Go); } // inactive now: no Simulate in this frame sees it
            if (p.RootCapsule != null) p.RootCapsule.enabled = true;
        }

        /// <summary>PlayerCapsules.HostRemoteAxis: a remote avatar's capsule at its pusher target.</summary>
        private bool TryGetCapsuleAxis(PlayerAvatar a, out Vector3 axisA, out Vector3 axisB, out float radius)
        {
            axisA = axisB = Vector3.zero; radius = 0f;
            if (disposed || a == null || !pushers.TryGetValue(a, out Pusher p) || p.Capsule == null || p.Rb == null) return false;
            PlayerCapsules.GetAxis(p.Capsule, p.HasTarget ? p.Target : p.Rb.position, p.HasTarget ? p.Yaw : p.Rb.rotation, out axisA, out axisB);
            radius = p.Capsule.radius;
            return true;
        }

        // ------------------------------------------------------------------ Held balls never collide with players (d.7)

        // Every capsule (the host's own, every pusher) x every held ball: Hold, then Release (no longer held), then Clear
        // (released and clear of that capsule). Store.pos is the last post-step pose.
        private void UpdatePairs()
        {
            heldCount = 0;
            for (int s = 0; s < PitAuthority.MaxPlayers; s++) if (authority.HeldId[s] >= 0) heldIds[heldCount++] = authority.HeldId[s];
            IReadOnlyList<PlayerAvatar> all = PlayerAvatar.All;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerAvatar a = all[i];
                if (a == null) continue;
                CapsuleCollider c = a.Role == PlayerRole.Local ? a.Capsule
                                  : a.Role == PlayerRole.Remote && pushers.TryGetValue(a, out Pusher p) ? p.Capsule : null;
                if (c == null) continue;
                int key = c.GetInstanceID();
                capsuleOfKey[key] = c;
                for (int h = 0; h < heldCount; h++) pairs.Hold(key, heldIds[h]);
            }
            pairs.MarkReleased(isHeld);
            pairs.ClearReleased(isClear);
        }

        private bool IsHeldNow(int id)
        {
            for (int h = 0; h < heldCount; h++) if (heldIds[h] == id) return true;
            return false;
        }

        private bool IsClearOfCapsule(int key, int id)
        {
            if (!capsuleOfKey.TryGetValue(key, out CapsuleCollider c) || c == null) return true; // the capsule is gone
            if ((uint)id >= (uint)world.Store.Count) return true;
            Rigidbody rb = c.attachedRigidbody;
            PlayerCapsules.GetAxis(c, rb != null ? rb.position : c.transform.position, rb != null ? rb.rotation : c.transform.rotation,
                                   out Vector3 a, out Vector3 b);
            float limit = c.radius + world.ColliderRadius + cfg.releaseClearMargin;
            return Geometry.PointSegmentSqrDistance(world.Store.pos[id], a, b) > limit * limit;
        }

        // The pair sink, with the call guard: IgnoreCollision only between enabled, active colliders (Unity logs otherwise).
        private void ApplyPair(int key, int ballId, bool ignore)
        {
            if (!capsuleOfKey.TryGetValue(key, out CapsuleCollider capsule)) return;
            SphereCollider col = authority.Active.ColliderOf(ballId);
            if (capsule == null || !capsule.enabled || !capsule.gameObject.activeInHierarchy || col == null || !col.enabled) return;
            Physics.IgnoreCollision(capsule, col, ignore);
        }

        // ActivePhysics.BeforeDemote: the pooled collider still belongs to this ball and is enabled. Restoring here means no
        // reused collider ever carries an old ignore (freeze, Excavate, Lose, B's Deposit).
        private void OnBeforeDemote(int id, SphereCollider col) => pairs.Demote(id);

        // ------------------------------------------------------------------ Freeze blocker (d.3)

        // Inside PitAuthority.Step (FreezeBottomUp, PressureFreeze), just before a live ball would freeze. Exception-free.
        private bool BlocksFreeze(int id, Vector3 p)
        {
            if (pairs.ReleasingCount > 0 && pairs.IsReleasing(id)) { BlockedThisStep++; return true; } // may sit inside a capsule
            if (authority.StepIndex != segmentsStep) RefreshSegments(); // once per Step, post-Simulate poses
            if (capsuleCount == 0 || !blockAabb.Contains(p)) return false;
            float ballRadius = world.ColliderRadius, margin = cfg.freezeTouchMargin;
            for (int i = 0; i < capsuleCount; i++)
            {
                if (!FreezeBlockRule.Blocks(p, segA[i], segB[i], segRadius[i], ballRadius, margin, underFootCos)) continue;
                BlockedThisStep++;
                return true;
            }
            return false;
        }

        // The physics pose of every capsule: the host's own body (just simulated), every remote player's pusher.
        private void RefreshSegments()
        {
            segmentsStep = authority.StepIndex;
            capsuleCount = 0;
            try
            {
                float touch = world.ColliderRadius + cfg.freezeTouchMargin;
                IReadOnlyList<PlayerAvatar> all = PlayerAvatar.All;
                for (int i = 0; i < all.Count; i++)
                {
                    PlayerAvatar a = all[i];
                    if (a == null) continue;
                    CapsuleCollider c;
                    Rigidbody rb;
                    if (a.Role == PlayerRole.Local) { c = a.Capsule; rb = c != null ? c.attachedRigidbody : null; }
                    else if (a.Role == PlayerRole.Remote && pushers.TryGetValue(a, out Pusher p)) { c = p.Capsule; rb = p.Rb; }
                    else continue;
                    if (c == null || rb == null) continue;
                    if (capsuleCount == segA.Length) Grow();
                    PlayerCapsules.GetAxis(c, rb.position, rb.rotation, out Vector3 sa, out Vector3 sb);
                    segA[capsuleCount] = sa; segB[capsuleCount] = sb; segRadius[capsuleCount] = c.radius;
                    var box = new Bounds((sa + sb) * 0.5f, Vector3.zero);
                    box.SetMinMax(Vector3.Min(sa, sb) - Vector3.one * (c.radius + touch), Vector3.Max(sa, sb) + Vector3.one * (c.radius + touch));
                    if (capsuleCount == 0) blockAabb = box; else blockAabb.Encapsulate(box);
                    capsuleCount++;
                }
            }
            catch (System.Exception e)
            {
                capsuleCount = 0; // no blocking this step rather than an aborted Step
                if (!warnedSegments) { warnedSegments = true; Debug.LogException(e); }
            }
        }

        private void Grow()
        {
            System.Array.Resize(ref segA, segA.Length * 2);
            System.Array.Resize(ref segB, segB.Length * 2);
            System.Array.Resize(ref segRadius, segRadius.Length * 2);
        }

        // ------------------------------------------------------------------ Grab filter (file plan 8.4)

        // Inside PitAuthority.TryGrab, before its own checks; NetPit's intent handler calls it for client grabs. Never
        // throws: slot -1, no avatar for the slot, a bad id or any failed check rejects, silently (RejectedGrabs).
        private bool GrabAllowed(int player, int id)
        {
            try
            {
                if (player < 0 || (uint)id >= (uint)world.Store.Count) return Reject();
                IReadOnlyList<PlayerAvatar> all = PlayerAvatar.All;
                for (int i = 0; i < all.Count; i++)
                {
                    PlayerAvatar a = all[i];
                    if (a == null || a.Slot == null || a.Slot.Index != player) continue;
                    PlayerConfig c = a.Config;
                    PlayerCapabilities caps = a.Capabilities;
                    if (c == null || caps == null) return Reject();
                    Vector3 head = a.transform.position + Vector3.up * c.eyeHeight; // the host's view of that player
                    float limit = c.reach * caps.Reach + cfg.grabReachSlack;    // slack: latency and interpolation
                    return (world.Store.pos[id] - head).sqrMagnitude <= limit * limit || Reject();
                }
                return Reject();
            }
            catch (System.Exception e)
            {
                if (!warnedGrab) { warnedGrab = true; Debug.LogException(e); }
                return Reject();
            }
        }

        private bool Reject() { RejectedGrabs++; return false; }

        // ------------------------------------------------------------------ Dispose (PitWorldDisposed, EndSession)

        /// <summary>Restores every live pair, destroys the pushers (root capsules re-enabled) and removes each hook that is
        /// still this driver's. Idempotent.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            PlayerAvatar.AnySpawned -= OnAnySpawned;
            PlayerAvatar.AnyDespawned -= OnAnyDespawned;
            pairs.Clear(restore: true);
            authority.Active.BeforeDemote -= beforeDemote;
            foreach (Pusher p in pushers.Values) DestroyPusher(p);
            pushers.Clear();
            capsuleOfKey.Clear();
            if (authority.FreezeBlocker == freezeBlocker) authority.FreezeBlocker = null;
            if (authority.GrabFilter == grabFilter) authority.GrabFilter = null;
            if (PlayerCapsules.HostRemoteAxis == remoteAxis) PlayerCapsules.HostRemoteAxis = null;
        }
    }

    /// <summary>The freeze blocker's per-capsule test (design d.3), pure.</summary>
    public static class FreezeBlockRule
    {
        /// <summary>
        /// True when a ball at p touches the capsule a-b (radius capsuleRadius) within margin and is not under it. A ball
        /// whose nearest capsule point lies within the under-foot angle of straight above its center supports the player
        /// and may freeze (underFootCos = cos(underFootAngleDeg); 1 = the brief's literal rule). On the axis: blocked.
        /// </summary>
        public static bool Blocks(Vector3 p, Vector3 a, Vector3 b, float capsuleRadius, float ballRadius, float margin, float underFootCos)
        {
            Vector3 d = Geometry.ClosestPointOnSegment(p, a, b) - p;
            float r = capsuleRadius + ballRadius + margin;
            float d2 = d.sqrMagnitude;
            if (d2 >= r * r) return false;                          // not touching
            return !(d.y > underFootCos * Mathf.Sqrt(d2));          // the capsule is on top of the ball: it may freeze
        }
    }
}
