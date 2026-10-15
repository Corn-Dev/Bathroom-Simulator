using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// The only live physics: real Rigidbodies, created lazily up to MaxActive. The pool IS the budget; there is no
    /// separate pool size that can silently clamp it (that clamp pinned the active set at 1,600 in milestone 2).
    /// Parked bodies stay on active GameObjects with the collider disabled and the body kinematic, so park/unpark never
    /// touches the hierarchy (no SetActive churn, no OnEnable/OnDisable, no editor hierarchy repaint).
    /// Interpolation is our own (prev/curr physics poses); all reads use rb.position (the physics pose).
    /// </summary>
    public sealed class ActivePhysics : System.IDisposable
    {
        private struct Body
        {
            public int id;
            public Rigidbody rb;
            public SphereCollider col;
            public bool carried, teleport, damped;
            public Vector3 prev, curr, eventPose, stillAnchor, lastSourcePos;
            public Vector3 velEma; // low-passed displacement velocity: solver jitter (high frequency) cancels out
            public float stillTime;
            public int lastSourceStep;
        }

        private struct Parked { public Rigidbody rb; public SphereCollider col; }

        /// <summary>Live budget (set every step from PitSettings.maxActive). The pool grows to it on demand.</summary>
        public int MaxActive;
        public int Count => count;
        public int Created => created;
        public bool CanPromote => count < MaxActive;
        public bool CanPromoteWithin(int limit) => count < Mathf.Min(limit, MaxActive);

        // Settling damping (rolling resistance for slow balls); set live from PitSettings.
        public float SettleSpeed = 0.25f, SettleDrag = 1f, SettleAngularDrag = 6f;
        public float LinearDrag, AngularDrag;
        /// <summary>Time constant of the smoothed (displacement) velocity used by the freeze and damping rules.</summary>
        public float SpeedSmoothingSeconds = 0.1f;

        private Body[] bodies;
        private int count, created;
        private readonly int[] bodyOfBall;
        private readonly Stack<Parked> free = new Stack<Parked>();
        private readonly GameObject root;
        private Transform group;
        private readonly float colliderRadius, maxDepen, sleepThreshold;
        private readonly PhysicMaterial mat;
        private readonly int layer, solverIterations;

        public ActivePhysics(int ballCapacity, int maxActive, int prewarm, float colliderRadius, PhysicMaterial mat,
                             float linearDrag, float angularDrag, float maxDepenetration, float sleepThreshold,
                             int layer, int solverIterations)
        {
            MaxActive = maxActive;
            this.colliderRadius = colliderRadius; this.mat = mat;
            LinearDrag = linearDrag; AngularDrag = angularDrag;
            maxDepen = maxDepenetration; this.sleepThreshold = sleepThreshold;
            this.layer = layer; this.solverIterations = solverIterations;
            bodies = new Body[Mathf.Max(64, maxActive)];
            bodyOfBall = new int[ballCapacity];
            for (int i = 0; i < ballCapacity; i++) bodyOfBall[i] = -1;
            root = new GameObject("BallPitFinal_ActivePool");
            Grow(Mathf.Max(0, prewarm));
        }

        private void Grow(int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (created % 128 == 0) { group = new GameObject("group").transform; group.SetParent(root.transform, false); }
                var go = new GameObject("active");
                go.layer = layer;
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(0f, -10000f, 0f);
                var col = go.AddComponent<SphereCollider>();
                col.radius = colliderRadius;
                col.sharedMaterial = mat;
                col.enabled = false;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.1f;
                rb.drag = LinearDrag;
                rb.angularDrag = AngularDrag;
                rb.sleepThreshold = sleepThreshold;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.maxDepenetrationVelocity = maxDepen;
                rb.centerOfMass = Vector3.zero;
                rb.inertiaTensorRotation = Quaternion.identity;
                rb.inertiaTensor = Vector3.one * (0.4f * rb.mass * colliderRadius * colliderRadius); // solid sphere, fixed (no recompute on collider toggles)
                if (solverIterations > 0) rb.solverIterations = solverIterations;
                rb.isKinematic = true;
                free.Push(new Parked { rb = rb, col = col });
                created++;
            }
        }

        public bool IsActive(int id) => bodyOfBall[id] >= 0;
        public int IdAt(int i) => bodies[i].id;
        public bool CarriedAt(int i) => bodies[i].carried;
        public Vector3 PosAt(int i) => bodies[i].curr;
        public Vector3 VelAt(int i) => bodies[i].carried ? Vector3.zero : bodies[i].rb.velocity;

        public Vector3 VelocityOf(int id)
        {
            int idx = bodyOfBall[id];
            if (idx < 0 || bodies[idx].carried) return Vector3.zero;
            return bodies[idx].rb.velocity;
        }

        /// <summary>Smoothed speed (low-passed displacement velocity). Use this, not rb.velocity, to decide "is it still".</summary>
        public float SmoothedSpeedAt(int i) => bodies[i].carried ? 0f : bodies[i].velEma.magnitude;

        public float SmoothedSpeedOf(int id)
        {
            int idx = bodyOfBall[id];
            return idx < 0 ? 0f : SmoothedSpeedAt(idx);
        }

        /// <summary>The collider of a live (active or held) ball, or null. Pooled: after Demote the same collider serves
        /// another ball, so never keep it past BeforeDemote.</summary>
        public SphereCollider ColliderOf(int id)
        {
            if ((uint)id >= (uint)bodyOfBall.Length) return null;
            int idx = bodyOfBall[id];
            return idx < 0 ? null : bodies[idx].col;
        }

        /// <summary>Optional, default null. Invoked at the start of Demote, while the ball's collider is still enabled.
        /// Must not change pit state (no Promote/Demote/transition calls).</summary>
        public System.Action<int, SphereCollider> BeforeDemote;

        public bool Promote(int id, Vector3 p, Vector3 velocity, bool carried)
        {
            if (count >= MaxActive || bodyOfBall[id] >= 0) return false;
            if (free.Count == 0) Grow(Mathf.Clamp(MaxActive - created, 1, 256));
            if (free.Count == 0) return false;
            Parked pk = free.Pop();
            Rigidbody rb = pk.rb;
            rb.transform.position = p;
            rb.position = p;
            rb.isKinematic = carried;
            pk.col.enabled = true;
            rb.drag = LinearDrag;
            rb.angularDrag = AngularDrag;
            if (!carried) { rb.velocity = velocity; rb.angularVelocity = Vector3.zero; rb.WakeUp(); }
            if (count == bodies.Length) System.Array.Resize(ref bodies, bodies.Length * 2);
            bodies[count] = new Body
            {
                id = id, rb = rb, col = pk.col, carried = carried, teleport = carried,
                prev = p, curr = p, eventPose = p, stillAnchor = p, lastSourcePos = p,
                velEma = carried ? Vector3.zero : velocity,
                stillTime = 0f, lastSourceStep = int.MinValue / 2
            };
            bodyOfBall[id] = count++;
            return true;
        }

        /// <summary>Removes the body from physics (parks it) and returns its exact physics pose.</summary>
        public Vector3 Demote(int id)
        {
            int idx = bodyOfBall[id];
            if (idx < 0) return Vector3.zero;
            BeforeDemote?.Invoke(id, bodies[idx].col);
            Rigidbody rb = bodies[idx].rb;
            SphereCollider col = bodies[idx].col;
            Vector3 pose = rb.position;
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            col.enabled = false;
            rb.isKinematic = true;
            free.Push(new Parked { rb = rb, col = col });
            int last = count - 1;
            if (idx != last) { bodies[idx] = bodies[last]; bodyOfBall[bodies[idx].id] = idx; }
            bodies[last] = default;
            count--;
            bodyOfBall[id] = -1;
            return pose;
        }

        /// <summary>Grab a ball that is already moving: the same body becomes kinematic and follows the hand.</summary>
        public void SetCarried(int id)
        {
            int idx = bodyOfBall[id];
            if (idx < 0) return;
            ref Body b = ref bodies[idx];
            if (b.carried) return;
            b.rb.velocity = Vector3.zero;
            b.rb.angularVelocity = Vector3.zero;
            b.rb.isKinematic = true;
            b.carried = true;
            b.teleport = true;
            b.velEma = Vector3.zero;
        }

        /// <summary>Kinematic follow for a held ball. First move after grabbing is a teleport (no sweep through the pile).</summary>
        public void MoveHeld(int id, Vector3 target)
        {
            int idx = bodyOfBall[id];
            if (idx < 0) return;
            ref Body b = ref bodies[idx];
            if (b.teleport)
            {
                b.rb.position = target;
                b.rb.transform.position = target;
                b.prev = b.curr = target;
                b.teleport = false;
            }
            else b.rb.MovePosition(target);
        }

        public void Release(int id, Vector3 velocity)
        {
            int idx = bodyOfBall[id];
            if (idx < 0) return;
            ref Body b = ref bodies[idx];
            b.carried = false;
            b.rb.isKinematic = false;
            b.rb.velocity = velocity;
            b.rb.angularVelocity = Vector3.zero;
            b.eventPose = b.stillAnchor = b.rb.position;
            b.velEma = velocity;
            b.stillTime = 0f;
            b.damped = false;
            b.rb.drag = LinearDrag;
            b.rb.angularDrag = AngularDrag;
        }

        public bool NeedsSourceRefresh(int i, int step, float minMoveSqr, int maxAgeSteps)
        {
            ref Body b = ref bodies[i];
            return step - b.lastSourceStep >= maxAgeSteps || (b.curr - b.lastSourcePos).sqrMagnitude > minMoveSqr;
        }

        public void MarkSourced(int i, int step)
        {
            ref Body b = ref bodies[i];
            b.lastSourceStep = step;
            b.lastSourcePos = b.curr;
        }

        /// <summary>
        /// After Physics.Simulate: copy poses into the store + live grid; emit motion events (moved more than
        /// moveThreshold since the last event; the OLD pose is reported because that is where it supported others);
        /// apply settling damping to slow balls; collect freeze candidates.
        /// Freeze rule (robust to solver jitter in deep piles): PhysX island asleep, OR SMOOTHED speed below freezeSpeed
        /// for freezeSeconds while drifting less than maxStillDrift. Both earlier rules (2 mm over 1 s; then instantaneous
        /// rb.velocity) were defeated by high-frequency jitter in packed piles, which stalled the active set.
        /// </summary>
        public void PostStep(float dt, float moveThreshold, float freezeSpeed, float freezeSeconds, float maxStillDrift,
                             BallStore store, SpatialGrid liveGrid,
                             List<int> movedIds, List<Vector3> movedFrom, List<int> freezeCandidates)
        {
            float move2 = moveThreshold * moveThreshold, fs2 = freezeSpeed * freezeSpeed, drift2 = maxStillDrift * maxStillDrift;
            float enter2 = SettleSpeed * SettleSpeed, exit2 = enter2 * 2.25f;
            float invDt = 1f / Mathf.Max(1e-5f, dt);
            float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, SpeedSmoothingSeconds));
            for (int i = 0; i < count; i++)
            {
                ref Body b = ref bodies[i];
                Vector3 p = b.rb.position;
                b.prev = b.curr;
                b.curr = p;
                store.pos[b.id] = p;
                liveGrid.Move(b.id, p);
                if (b.carried) continue;

                if ((p - b.eventPose).sqrMagnitude > move2)
                {
                    movedIds.Add(b.id);
                    movedFrom.Add(b.eventPose);
                    b.eventPose = p;
                }

                // Smoothed displacement velocity, NOT rb.velocity: in a packed pile the solver jitters at high frequency, so
                // the instantaneous velocity stays above any sane threshold while the ball goes nowhere (milestone-3 stall).
                b.velEma = Vector3.LerpUnclamped(b.velEma, (p - b.prev) * invDt, alpha);
                float v2 = b.velEma.sqrMagnitude;
                bool slow = b.damped ? v2 < exit2 : v2 < enter2;
                if (slow != b.damped)
                {
                    b.damped = slow;
                    b.rb.drag = slow ? SettleDrag : LinearDrag;
                    b.rb.angularDrag = slow ? SettleAngularDrag : AngularDrag;
                }

                if (v2 < fs2 && (p - b.stillAnchor).sqrMagnitude < drift2) b.stillTime += dt;
                else { b.stillTime = 0f; b.stillAnchor = p; }

                if (b.rb.IsSleeping() || b.stillTime >= freezeSeconds) freezeCandidates.Add(b.id);
            }
        }

        /// <summary>Interpolated render positions (alpha in [0,1] between the last two physics steps) + ids for picking.</summary>
        public int FillInstances(BallInstance[] dst, int[] ids, float alpha, BallStore store)
        {
            int n = Mathf.Min(count, Mathf.Min(dst.Length, ids.Length));
            for (int i = 0; i < n; i++)
            {
                ref Body b = ref bodies[i];
                dst[i] = new BallInstance
                {
                    pos = Vector3.LerpUnclamped(b.prev, b.curr, alpha),
                    packed = store.color[b.id] | GpuBallRenderer.DrawableBit
                };
                ids[i] = b.id;
            }
            return n;
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
        }
    }
}
