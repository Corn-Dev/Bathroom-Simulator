// OutOfBoundsVolume: an authored trigger that sends the local player back to its spawn point (deliverable A, file plan
// V6; design c 8, d.9; brief 6.4, 11.1). Place it anywhere, on a GameObject with trigger collider(s); no parent or child
// is required. The optional site link limits it to one PitSite (empty = whichever site is active). One API for players
// and balls: OnTriggerEnter asks the local player's motor for an out-of-bounds teleport (the motor stays the only
// Rigidbody writer); Contains / AnyContains let B's host test active balls as data, and let the spawner and the venue
// validator skip spawn points inside a volume. Holds no player state. Also HostPlayerPhysics' HeldBallPairs (the
// 460-line cap, file plan 12.10 finding 1).
using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class OutOfBoundsVolume : MonoBehaviour
    {
        [Tooltip("Optional. The site this volume belongs to; empty = whichever site is active.")]
        [SerializeField] private PitSite site;

        private static readonly List<OutOfBoundsVolume> active = new List<OutOfBoundsVolume>();
        private Collider[] colliders;

#if UNITY_EDITOR
        [System.NonSerialized] private string lastValidation;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();

        /// <summary>Enabled volumes (OnEnable/OnDisable): a volume under an inactive site root never applies.</summary>
        public static IReadOnlyList<OutOfBoundsVolume> Active => active;

        /// <summary>The optional site link (null = any active site). Read by the editor tools.</summary>
        public PitSite Site => site;

        /// <summary>True when this volume counts for the active site (an unlinked or deleted link applies everywhere).</summary>
        public bool AppliesTo(PitSite activeSite) => site == null || site == activeSite;

        /// <summary>
        /// True when p is inside one of this GameObject's colliders (Collider.ClosestPoint(p) == p: box, sphere, capsule
        /// and convex mesh). False for a missing, disabled or inactive collider, because ClosestPoint can hand the query
        /// point back unchanged for one, which would read as inside. Works in edit mode (call Physics.SyncTransforms first).
        /// </summary>
        public bool Contains(Vector3 p)
        {
            if (colliders == null) colliders = GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c is MeshCollider mesh && !mesh.convex) continue; // ClosestPoint needs a convex mesh
                if (c.ClosestPoint(p) == p) return true;
            }
            return false;
        }

        /// <summary>True when p is inside any enabled volume that applies to activeSite (null = site-less).</summary>
        public static bool AnyContains(Vector3 p, PitSite activeSite)
        {
            for (int i = 0; i < active.Count; i++)
            {
                OutOfBoundsVolume v = active[i];
                if (v != null && v.AppliesTo(activeSite) && v.Contains(p)) return true;
            }
            return false;
        }

        private void OnEnable()
        {
            colliders = GetComponents<Collider>(); // colliders added later are picked up on the next enable
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable() => active.Remove(this);

        // Only the local player reacts, and only while released: each owner handles itself, remote avatars are ignored.
        // Trigger callbacks fire inside the host's script-mode Physics.Simulate and the session client's own step.
        private void OnTriggerEnter(Collider other)
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null || other == null || other != local.Capsule || !local.IsReleased) return;
            BallPitFinalController c = local.Controller;
            if (!AppliesTo(c != null ? c.ActiveSite as PitSite : null)) return;
            PlayerMotor motor = local.Motor;
            if (motor != null) motor.RequestOutOfBounds();
        }

#if UNITY_EDITOR
        // Warns once per change: no collider, a non-trigger collider, or a non-convex mesh (ClosestPoint cannot test it).
        private void OnValidate()
        {
            string text = null;
            Collider[] own = GetComponents<Collider>();
            if (own.Length == 0) text = "no Collider on this GameObject; add a trigger collider (box, sphere, capsule or convex mesh)";
            else
                foreach (Collider c in own)
                {
                    if (!c.isTrigger) { text = $"collider '{c.GetType().Name}' is not a trigger; players would collide with it"; break; }
                    if (c is MeshCollider mesh && !mesh.convex) { text = "a MeshCollider must be convex (Contains cannot test a concave mesh)"; break; }
                }
            colliders = null; // re-read on the next Contains
            if (text == lastValidation) return;
            lastValidation = text;
            if (text != null) Debug.LogWarning($"[OutOfBoundsVolume] '{name}': {text}", this);
        }
#endif
    }

    // HostPlayerPhysics' pair state machine lives in this file only for the 460-line cap (file plan 12.10 finding 1:
    // batch 3's headroom file is V6). It is player-physics code; HostPlayerPhysics.cs is its only user.

    /// <summary>
    /// The held-ball ignore pairs (design d.7), pure: integer capsule keys and ball ids, with an injected sink
    /// (capsuleKey, ballId, ignore) that HostPlayerPhysics maps to Physics.IgnoreCollision. A pair is Holding or Releasing.
    /// </summary>
    public sealed class HeldBallPairs
    {
        private struct Pair { public int Capsule, Ball; public bool Releasing; }

        private readonly List<Pair> pairs = new List<Pair>();
        private readonly System.Action<int, int, bool> sink;

        public HeldBallPairs(System.Action<int, int, bool> sink) { this.sink = sink; }

        public int Count => pairs.Count;
        public int ReleasingCount { get; private set; }

        public bool IsReleasing(int ball)
        {
            for (int i = 0; i < pairs.Count; i++) if (pairs[i].Ball == ball && pairs[i].Releasing) return true;
            return false;
        }

        /// <summary>Ensures a Holding pair; a new pair ignores (sink true); a Releasing pair goes back to Holding (no call:
        /// it is still ignored).</summary>
        public void Hold(int capsule, int ball)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                Pair p = pairs[i];
                if (p.Capsule != capsule || p.Ball != ball) continue;
                if (p.Releasing) { p.Releasing = false; pairs[i] = p; ReleasingCount--; }
                return;
            }
            pairs.Add(new Pair { Capsule = capsule, Ball = ball });
            sink?.Invoke(capsule, ball, true);
        }

        /// <summary>Holding pairs whose ball is no longer held (throw, drop, disconnect) become Releasing.</summary>
        public void MarkReleased(System.Func<int, bool> isHeld)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                Pair p = pairs[i];
                if (p.Releasing || isHeld(p.Ball)) continue;
                p.Releasing = true;
                pairs[i] = p;
                ReleasingCount++;
            }
        }

        /// <summary>Releasing pairs whose ball is clear of their capsule (isClear(capsule, ball)) are restored and removed.</summary>
        public void ClearReleased(System.Func<int, int, bool> isClear)
        {
            for (int i = pairs.Count - 1; i >= 0; i--)
            {
                Pair p = pairs[i];
                if (!p.Releasing || !isClear(p.Capsule, p.Ball)) continue;
                RemoveAt(i, restore: true);
            }
        }

        /// <summary>The ball's collider is about to be reused (ActivePhysics.BeforeDemote): restore and remove its pairs.</summary>
        public void Demote(int ball)
        {
            for (int i = pairs.Count - 1; i >= 0; i--) if (pairs[i].Ball == ball) RemoveAt(i, restore: true);
        }

        /// <summary>A capsule left (despawn): its pairs are restored when restore is set, then dropped.</summary>
        public void RemoveCapsule(int capsule, bool restore)
        {
            for (int i = pairs.Count - 1; i >= 0; i--) if (pairs[i].Capsule == capsule) RemoveAt(i, restore);
        }

        /// <summary>Dispose: every pair restored when restore is set, then all cleared.</summary>
        public void Clear(bool restore)
        {
            for (int i = pairs.Count - 1; i >= 0; i--) RemoveAt(i, restore);
        }

        private void RemoveAt(int i, bool restore)
        {
            Pair p = pairs[i];
            pairs.RemoveAt(i);
            if (p.Releasing) ReleasingCount--;
            if (restore) sink?.Invoke(p.Capsule, p.Ball, false);
        }
    }
}
