// PlayerSlot: the networked pit slot, player id and display name of a player object (deliverable A, file plan P3;
// design d.11). The host writes the values in its OnNetworkSpawn, so they ride in the spawn payload. Also the shared
// static helpers of the player and spawn code: SlotNames (d.11), PlayerLayers (d.2), PlayerCapsules (d.8) and
// Geometry (d.3), placed here so batch-2 files can use them without naming a batch-3 type.
using System.Collections.Generic;
using MultiplayerKit;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class PlayerSlot : NetworkBehaviour
    {
        // Default permissions: everyone reads, the server writes.
        private readonly NetworkVariable<int> slot = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<ulong> playerId = new NetworkVariable<ulong>(0UL);
        private readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>();

        private bool hasPending;
        private int pendingSlot = -1;
        private ulong pendingPlayer;
        private FixedString64Bytes pendingName;

        /// <summary>The pit slot as the host's mirror assigned it; -1 until the host has written it.</summary>
        public int Index => slot.Value;
        public PlayerId Player => new PlayerId(playerId.Value);
        public string DisplayName => displayName.Value.ToString();
        /// <summary>Any of the three values changed (raised on every peer, and once at spawn).</summary>
        public event System.Action<PlayerSlot> Changed;

        /// <summary>Host, before SpawnAsPlayerObject: stores the values, written in OnNetworkSpawn (writing a
        /// NetworkVariable before spawn makes NGO warn). After spawn on the server it writes them at once.</summary>
        public void ServerInit(int slotIndex, PlayerId player, string name)
        {
            FixedString64Bytes fixedName = SlotNames.ToFixed64(name);
            if (IsSpawned)
            {
                if (!IsServer) { Debug.LogWarning("[PlayerSlot] ServerInit ignored: only the host writes slots", this); return; }
                slot.Value = slotIndex; playerId.Value = player.Value; displayName.Value = fixedName;
                return;
            }
            pendingSlot = slotIndex; pendingPlayer = player.Value; pendingName = fixedName;
            hasPending = true;
        }

        public override void OnNetworkSpawn()
        {
            slot.OnValueChanged += OnIntChanged;
            playerId.OnValueChanged += OnUlongChanged;
            displayName.OnValueChanged += OnNameChanged;
            if (IsServer && hasPending)
            {
                slot.Value = pendingSlot; playerId.Value = pendingPlayer; displayName.Value = pendingName;
                hasPending = false;
            }
            RaiseChanged();
        }

        public override void OnNetworkDespawn()
        {
            slot.OnValueChanged -= OnIntChanged;
            playerId.OnValueChanged -= OnUlongChanged;
            displayName.OnValueChanged -= OnNameChanged;
        }

        private void OnIntChanged(int previous, int current) => RaiseChanged();
        private void OnUlongChanged(ulong previous, ulong current) => RaiseChanged();
        private void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current) => RaiseChanged();

        private void RaiseChanged()
        {
            System.Action<PlayerSlot> handlers = Changed;
            if (handlers == null) return;
            foreach (System.Delegate d in handlers.GetInvocationList())
            {
                try { ((System.Action<PlayerSlot>)d)(this); }
                catch (System.Exception e) { Debug.LogException(e, this); }
            }
        }
    }

    public static class SlotNames
    {
        /// <summary>FixedString64Bytes holds at most this many UTF-8 bytes.</summary>
        public const int MaxNameBytes = 61;

        /// <summary>The name cut to MaxNameBytes UTF-8 bytes on a character boundary, so a long name can never throw.</summary>
        public static FixedString64Bytes ToFixed64(string name)
        {
            string cut = CutUtf8(name ?? "", MaxNameBytes);
            try { return new FixedString64Bytes(cut); }
            catch (System.Exception) { return default; }
        }

        /// <summary>The longest prefix of s whose UTF-8 encoding fits maxBytes, never splitting a surrogate pair.</summary>
        public static string CutUtf8(string s, int maxBytes)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int bytes = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int n, chars = 1;
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { n = 4; chars = 2; }
                else if (c < 0x80) n = 1;
                else if (c < 0x800) n = 2;
                else n = 3; // includes a lone surrogate, which encodes as U+FFFD
                if (bytes + n > maxBytes) return s.Substring(0, i);
                bytes += n;
                i += chars - 1;
            }
            return s;
        }
    }

    /// <summary>The Player and PlayerBody layers (design d.2), resolved by name on first use and cached; -1 when missing.</summary>
    public static class PlayerLayers
    {
        public const string PlayerName = "Player", PlayerBodyName = "PlayerBody";
        private static int player = -2, playerBody = -2; // -2 = not resolved yet

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { player = -2; playerBody = -2; }

        public static int Player { get { if (player == -2) player = LayerMask.NameToLayer(PlayerName); return player; } }
        public static int PlayerBody { get { if (playerBody == -2) playerBody = LayerMask.NameToLayer(PlayerBodyName); return playerBody; } }

        /// <summary>The d.2 rules: Errors for a missing Player layer, Player == ballLayer, or Player x ball collision off;
        /// Warnings (when a list is given) for Player x Player collision off and a missing PlayerBody. Logs nothing.
        /// Returns errors.Count == 0.</summary>
        public static bool Validate(int ballLayer, List<string> errors, List<string> warnings = null)
        {
            int p = Player;
            if (p < 0) errors.Add($"the '{PlayerName}' layer is missing: players count as ball support (Project Settings > Tags and Layers)");
            else if (p == ballLayer) errors.Add($"the '{PlayerName}' layer ({p}) equals the ball layer; players are not excluded from support");
            else
            {
                if (ballLayer >= 0 && ballLayer < 32 && Physics.GetIgnoreLayerCollision(p, ballLayer))
                    errors.Add($"'{PlayerName}' x ball layer {ballLayer} collision is off in the physics matrix: players fall through the pile");
                if (warnings != null && Physics.GetIgnoreLayerCollision(p, p))
                    warnings.Add($"'{PlayerName}' x '{PlayerName}' collision is off in the physics matrix: players pass through each other");
            }
            if (warnings != null && PlayerBody < 0)
                warnings.Add($"the '{PlayerBodyName}' layer is missing: body renderers keep their own layer and ownerBodyMode CullLayer cannot work");
            return errors.Count == 0;
        }
    }

    /// <summary>A capsule axis source (the host's remote-player pushers): hemisphere centers a, b and the radius.</summary>
    public delegate bool CapsuleAxisProvider(PlayerAvatar avatar, out Vector3 a, out Vector3 b, out float radius);

    /// <summary>Player capsule math and checks (design d.8). The root pivot is at the feet.</summary>
    public static class PlayerCapsules
    {
        /// <summary>Host only: HostPlayerPhysics sets it to its pusher poses and clears it on Dispose. Null elsewhere.</summary>
        public static CapsuleAxisProvider HostRemoteAxis;
        private static readonly Dictionary<int, bool> checkedAvatars = new Dictionary<int, bool>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { HostRemoteAxis = null; checkedAvatars.Clear(); }

        /// <summary>Hemisphere centers for the capsule at the physics pose pos/rot (direction Y, unit scale assumed).</summary>
        public static void GetAxis(CapsuleCollider c, Vector3 pos, Quaternion rot, out Vector3 a, out Vector3 b)
        {
            float half = Mathf.Max(0f, c.height * 0.5f - c.radius);
            a = pos + rot * (c.center - Vector3.up * half);
            b = pos + rot * (c.center + Vector3.up * half);
        }

        public static Vector3 Feet(Vector3 pos) => pos;

        /// <summary>
        /// The capsule as physics sees it now: the host's pusher pose for a remote avatar (HostRemoteAxis), the body
        /// pose for the local avatar, else the root transform (what ClientNetworkTransform writes). False without a capsule.
        /// </summary>
        public static bool GetLiveAxis(PlayerAvatar avatar, out Vector3 a, out Vector3 b, out float radius)
        {
            a = b = Vector3.zero; radius = 0f;
            if (avatar == null) return false;
            CapsuleCollider c = avatar.Capsule;
            if (c == null) return false;
            if (avatar.Role == PlayerRole.Remote && HostRemoteAxis != null)
            {
                try { if (HostRemoteAxis(avatar, out a, out b, out radius)) return true; }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            Transform t = avatar.transform;
            Rigidbody rb = avatar.Role == PlayerRole.Local ? c.attachedRigidbody : null;
            GetAxis(c, rb != null ? rb.position : t.position, rb != null ? rb.rotation : t.rotation, out a, out b);
            radius = c.radius;
            return true;
        }

        /// <summary>The once-per-instance runtime backstop for the prefab wiring (layer, direction, scale, stray colliders).
        /// Logs on the first call; later calls return the first result.</summary>
        public static bool Check(PlayerAvatar avatar)
        {
            if (avatar == null) return false;
            int id = avatar.GetInstanceID();
            if (checkedAvatars.TryGetValue(id, out bool ok)) return ok;
            ok = true;
            CapsuleCollider c = avatar.Capsule;
            if (c == null) { Debug.LogError($"[PlayerCapsules] '{avatar.name}' has no CapsuleCollider", avatar); ok = false; }
            else
            {
                if (c.gameObject.layer != PlayerLayers.Player)
                { Debug.LogError("[PlayerCapsules] player capsule is not on the 'Player' layer; balls will treat this player as support", avatar); ok = false; }
                Vector3 s = avatar.transform.lossyScale;
                if (c.direction != 1 || Mathf.Abs(s.x - 1f) > 1e-3f || Mathf.Abs(s.y - 1f) > 1e-3f || Mathf.Abs(s.z - 1f) > 1e-3f)
                { Debug.LogError("[PlayerCapsules] the player capsule must be Y-direction at unit scale; capsule math is approximate", avatar); ok = false; }
                var stray = new List<string>();
                foreach (Collider other in avatar.GetComponentsInChildren<Collider>(true))
                    if (other != c && other.enabled && !other.isTrigger && other.gameObject.layer != PlayerLayers.Player) stray.Add(other.name);
                if (stray.Count > 0)
                    Debug.LogWarning($"[PlayerCapsules] '{avatar.name}': colliders not on the 'Player' layer: {string.Join(", ", stray)}", avatar);
            }
            checkedAvatars[id] = ok;
            return ok;
        }
    }

    /// <summary>Segment distance helpers (design d.3), shared by the freeze blocker, the proxies and the spawn rules.</summary>
    public static class Geometry
    {
        public static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = Vector3.Dot(ab, ab);
            if (len2 <= 1e-12f) return a;
            return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        }

        public static float PointSegmentSqrDistance(Vector3 p, Vector3 a, Vector3 b) => (p - ClosestPointOnSegment(p, a, b)).sqrMagnitude;

        /// <summary>Squared distance between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        public static float SegmentSegmentSqrDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            const float eps = 1e-12f;
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= eps && e <= eps) return r.sqrMagnitude;
            if (a <= eps) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= eps) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - (p2 + d2 * t)).sqrMagnitude;
        }
    }
}
