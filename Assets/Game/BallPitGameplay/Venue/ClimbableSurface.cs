// ClimbableSurface: marks colliders a player can wall-climb (deliverable A, file plan V5; design c 8, brief 11.1).
// Place it anywhere, on the collider's GameObject or above it; no parent or child is required. The optional site link
// limits it to one PitSite (empty = whichever site is active). The motor looks a steep contact up through TryGet, one
// dictionary lookup per contact, over a registry filled in OnEnable and emptied in OnDisable.
using System.Collections.Generic;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class ClimbableSurface : MonoBehaviour
    {
        [Tooltip("Optional. The site this surface belongs to; empty = whichever site is active.")]
        [SerializeField] private PitSite site;
        [Tooltip("Optional. The climbable colliders; empty = the Colliders on this GameObject and its children " +
                 "(colliders added after this component was enabled are picked up on its next enable).")]
        [SerializeField] private Collider[] colliders;

        private static readonly Dictionary<Collider, ClimbableSurface> registry = new Dictionary<Collider, ClimbableSurface>();
        private readonly List<Collider> registered = new List<Collider>();
        private static readonly List<Collider> scratch = new List<Collider>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => registry.Clear();

        /// <summary>The optional site link (null = any active site). Read by the editor tools.</summary>
        public PitSite Site => site;

        /// <summary>True when this surface counts for the active site (an unlinked or deleted link applies everywhere).</summary>
        public bool AppliesTo(PitSite active) => site == null || site == active;

        /// <summary>The surface registered for a collider, O(1). False for an unmarked or destroyed collider.</summary>
        public static bool TryGet(Collider collider, out ClimbableSurface surface)
        {
            surface = null;
            if (ReferenceEquals(collider, null) || !registry.TryGetValue(collider, out surface)) return false;
            if (surface != null) return true;
            registry.Remove(collider); // its surface was destroyed without OnDisable (scene teardown)
            surface = null;
            return false;
        }

        /// <summary>Clears results, then adds the authored list, else this GameObject's and its children's colliders
        /// (inactive ones included). Works in edit mode.</summary>
        public void GetColliders(List<Collider> results)
        {
            if (results == null) return;
            results.Clear();
            if (colliders != null && colliders.Length > 0)
            {
                for (int i = 0; i < colliders.Length; i++)
                    if (colliders[i] != null && !results.Contains(colliders[i])) results.Add(colliders[i]);
                return;
            }
            GetComponentsInChildren(true, results);
        }

        private void OnEnable()
        {
            GetColliders(scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                Collider c = scratch[i];
                if (registry.TryGetValue(c, out ClimbableSurface other) && other != null && other != this)
                    Debug.LogWarning($"[ClimbableSurface] collider '{c.name}' is marked by two ClimbableSurfaces; '{name}' wins", this);
                registry[c] = this;
                registered.Add(c);
            }
            scratch.Clear();
        }

        private void OnDisable()
        {
            for (int i = 0; i < registered.Count; i++)
            {
                Collider c = registered[i];
                if (ReferenceEquals(c, null)) continue;
                if (registry.TryGetValue(c, out ClimbableSurface owner) && ReferenceEquals(owner, this)) registry.Remove(c);
            }
            registered.Clear();
        }
    }
}
