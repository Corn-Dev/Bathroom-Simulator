using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// Static SphereColliders (no Rigidbody) at the EXACT pose of resting balls near activity.
    /// Pre-allocated pool; colliders are toggled with Collider.enabled (cheaper than SetActive) and only moved
    /// while disabled. Added eagerly, released lazily after keepSteps without being wanted.
    /// Exposure is deliberately NOT used: buried balls with colliders are harmless (static-static pairs are free),
    /// a missing collider under an active ball is not.
    /// </summary>
    public sealed class ColliderShell : System.IDisposable
    {
        public int Live => liveCount;
        public int Capacity => cols.Length;
        public int CapHits { get; private set; }

        private readonly SphereCollider[] cols;
        private readonly Transform[] trs;
        private readonly int[] ballOfSlot, lastWanted, liveIndexOfSlot, liveSlots;
        private readonly int[] slotOfBall;
        private readonly Stack<int> freeSlots;
        private int liveCount;
        private readonly GameObject root;

        public ColliderShell(int maxColliders, int ballCapacity, float colliderRadius, PhysicMaterial mat)
        {
            cols = new SphereCollider[maxColliders];
            trs = new Transform[maxColliders];
            ballOfSlot = new int[maxColliders];
            lastWanted = new int[maxColliders];
            liveIndexOfSlot = new int[maxColliders];
            liveSlots = new int[maxColliders];
            freeSlots = new Stack<int>(maxColliders);
            slotOfBall = new int[ballCapacity];
            for (int i = 0; i < ballCapacity; i++) slotOfBall[i] = -1;

            root = new GameObject("BallPitFinal_ColliderShell");
            Transform group = null;
            for (int s = maxColliders - 1; s >= 0; s--)
            {
                if (group == null || group.childCount >= 256) { group = new GameObject("group").transform; group.SetParent(root.transform, false); }
                var go = new GameObject("sc");
                go.transform.SetParent(group, false);
                var c = go.AddComponent<SphereCollider>();
                c.radius = colliderRadius;
                c.sharedMaterial = mat;
                c.enabled = false;
                cols[s] = c; trs[s] = go.transform;
                ballOfSlot[s] = -1; liveIndexOfSlot[s] = -1;
                freeSlots.Push(s);
            }
        }

        public bool Has(int id) => slotOfBall[id] >= 0;

        public void Ensure(int id, Vector3 p, int step)
        {
            int s = slotOfBall[id];
            if (s >= 0) { lastWanted[s] = step; return; }
            if (freeSlots.Count == 0) { CapHits++; return; }
            s = freeSlots.Pop();
            trs[s].position = p;
            cols[s].enabled = true;
            ballOfSlot[s] = id;
            slotOfBall[id] = s;
            lastWanted[s] = step;
            liveIndexOfSlot[s] = liveCount;
            liveSlots[liveCount++] = s;
        }

        public void Remove(int id)
        {
            int s = slotOfBall[id];
            if (s >= 0) ReleaseSlot(s);
        }

        public void Expire(int step, int keepSteps, int maxReleases)
        {
            for (int i = liveCount - 1; i >= 0 && maxReleases > 0; i--)
            {
                int s = liveSlots[i];
                if (step - lastWanted[s] > keepSteps) { ReleaseSlot(s); maxReleases--; }
            }
        }

        public void ClearAll()
        {
            while (liveCount > 0) ReleaseSlot(liveSlots[liveCount - 1]);
            CapHits = 0;
        }

        private void ReleaseSlot(int s)
        {
            cols[s].enabled = false;
            slotOfBall[ballOfSlot[s]] = -1;
            ballOfSlot[s] = -1;
            int li = liveIndexOfSlot[s], last = liveCount - 1;
            int moved = liveSlots[last];
            liveSlots[li] = moved;
            liveIndexOfSlot[moved] = li;
            liveIndexOfSlot[s] = -1;
            liveCount--;
            freeSlots.Push(s);
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
        }
    }
}
