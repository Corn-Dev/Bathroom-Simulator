using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// The only live physics: a pool of real Rigidbodies. Interpolation is done here (prev/curr physics poses),
    /// not by Unity, so a pooled body teleported to a new ball never streaks from its old pose.
    /// All reads use rb.position (the physics pose), never transform.position.
    /// </summary>
    public sealed class ActivePhysics : System.IDisposable
    {
        private struct Body
        {
            public int id;
            public Rigidbody rb;
            public bool carried, teleport;
            public Vector3 prev, curr, eventPose, stillAnchor, lastSourcePos;
            public float stillTime;
            public int lastSourceStep;
        }

        public int MaxActive;
        public int Count => count;
        public bool CanPromote => count < MaxActive && free.Count > 0;

        private readonly Body[] bodies;
        private int count;
        private readonly int[] bodyOfBall; // per ball id -> index into bodies, or -1
        private readonly Stack<Rigidbody> free = new Stack<Rigidbody>();
        private readonly GameObject root;

        public ActivePhysics(int ballCapacity, int poolSize, int maxActive, float colliderRadius, PhysicMaterial mat,
                             float linearDrag, float angularDrag, float maxDepenetration)
        {
            MaxActive = Mathf.Min(maxActive, poolSize);
            bodies = new Body[poolSize];
            bodyOfBall = new int[ballCapacity];
            for (int i = 0; i < ballCapacity; i++) bodyOfBall[i] = -1;

            root = new GameObject("BallPitFinal_ActivePool");
            Transform group = null;
            for (int i = 0; i < poolSize; i++)
            {
                if (i % 128 == 0) { group = new GameObject("group").transform; group.SetParent(root.transform, false); }
                var go = new GameObject("active");
                go.transform.SetParent(group, false);
                var col = go.AddComponent<SphereCollider>();
                col.radius = colliderRadius;
                col.sharedMaterial = mat;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.1f;
                rb.drag = linearDrag;
                rb.angularDrag = angularDrag;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.maxDepenetrationVelocity = maxDepenetration;
                go.SetActive(false);
                free.Push(rb);
            }
        }

        public bool IsActive(int id) => bodyOfBall[id] >= 0;
        public int IdAt(int i) => bodies[i].id;
        public bool CarriedAt(int i) => bodies[i].carried;
        public Vector3 PosAt(int i) => bodies[i].curr;
        public Vector3 VelAt(int i) => bodies[i].carried ? Vector3.zero : bodies[i].rb.velocity;

        public bool Promote(int id, Vector3 p, Vector3 velocity, bool carried)
        {
            if (!CanPromote || bodyOfBall[id] >= 0) return false;
            Rigidbody rb = free.Pop();
            GameObject go = rb.gameObject;
            go.transform.position = p;
            go.SetActive(true);
            rb.isKinematic = carried;
            rb.position = p;
            if (!carried) { rb.velocity = velocity; rb.angularVelocity = Vector3.zero; }
            rb.WakeUp();
            bodies[count] = new Body
            {
                id = id, rb = rb, carried = carried, teleport = carried,
                prev = p, curr = p, eventPose = p, stillAnchor = p, lastSourcePos = p,
                stillTime = 0f, lastSourceStep = int.MinValue / 2
            };
            bodyOfBall[id] = count++;
            return true;
        }

        /// <summary>Removes the body from physics and returns its exact physics pose.</summary>
        public Vector3 Demote(int id)
        {
            int idx = bodyOfBall[id];
            if (idx < 0) return Vector3.zero;
            Rigidbody rb = bodies[idx].rb;
            Vector3 pose = rb.position;
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.gameObject.SetActive(false);
            free.Push(rb);
            int last = count - 1;
            if (idx != last) { bodies[idx] = bodies[last]; bodyOfBall[bodies[idx].id] = idx; }
            bodies[last] = default;
            count--;
            bodyOfBall[id] = -1;
            return pose;
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
            b.stillTime = 0f;
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
        /// After Physics.Simulate: copy poses into the store + active grid, emit motion events (ball moved more than
        /// moveThreshold since its last event; the OLD pose is reported because that is where it was supporting others),
        /// and collect freeze candidates (PhysX island asleep, or still for stillSeconds as a jitter fallback).
        /// </summary>
        public void PostStep(float dt, float moveThreshold, float stillEpsilon, float stillSeconds,
                             BallStore store, SpatialGrid activeGrid,
                             List<int> movedIds, List<Vector3> movedFrom, List<int> freezeCandidates)
        {
            float move2 = moveThreshold * moveThreshold, still2 = stillEpsilon * stillEpsilon;
            for (int i = 0; i < count; i++)
            {
                ref Body b = ref bodies[i];
                Vector3 p = b.rb.position;
                b.prev = b.curr;
                b.curr = p;
                store.pos[b.id] = p;
                activeGrid.Move(b.id, p);
                if (b.carried) continue;

                if ((p - b.eventPose).sqrMagnitude > move2)
                {
                    movedIds.Add(b.id);
                    movedFrom.Add(b.eventPose);
                    b.eventPose = p;
                }
                if ((p - b.stillAnchor).sqrMagnitude > still2) { b.stillAnchor = p; b.stillTime = 0f; }
                else b.stillTime += dt;

                if (b.rb.IsSleeping() || b.stillTime >= stillSeconds) freezeCandidates.Add(b.id);
            }
        }

        /// <summary>Interpolated render positions (alpha in [0,1] between the last two physics steps).</summary>
        public int FillInstances(BallInstance[] dst, float alpha, BallStore store)
        {
            int n = Mathf.Min(count, dst.Length);
            for (int i = 0; i < n; i++)
            {
                ref Body b = ref bodies[i];
                dst[i] = new BallInstance
                {
                    pos = Vector3.LerpUnclamped(b.prev, b.curr, alpha),
                    packed = store.color[b.id] | GpuBallRenderer.DrawableBit
                };
            }
            return n;
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
        }
    }
}
