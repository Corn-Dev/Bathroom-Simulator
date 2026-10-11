using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BallPitFinal
{
    [System.Serializable]
    public sealed class NetSettings
    {
        public string address = "127.0.0.1";
        public ushort port = 7777;
        [Tooltip("Snapshot + event flush rate (Hz).")]
        public float sendRate = 20f;
        [Tooltip("Unreliable snapshot budget per client. Each entry is 9 bytes (3 id + 6 pos).")]
        public int snapshotBytesPerSecond = 64000;
        [Tooltip("Reliable pacing per client (join sync + events). Keep under the transport's send buffer / rate.")]
        public int reliableBytesPerSecond = 262144;
        public int reliableBurstBytes = 65536;
        public int syncChunkBytes = 16384;
        [Tooltip("Client playback delay behind the newest host time (s).")]
        public float interpolationDelay = 0.12f;
        public float viewSendRate = 20f;
    }

    /// <summary>
    /// Host-authoritative replication through NGO named messages (no NetworkObject per ball; transport-agnostic, so it
    /// runs on UnityTransport for local tests and on the Steam transport unchanged).
    ///   Header  (reliable)   host -> joining client: fill settings, exact play bounds, fill checksum, player slot.
    ///   Ready   (reliable)   client -> host: checksum of its locally built fill (mismatch => full resync).
    ///   Rel     (reliable fragmented, paced) host -> client: sync chunks (every ball modified since the fill),
    ///                        sync-done (with ledger totals), then live event batches stamped with host steps.
    ///   Snap    (unreliable) host -> client: priority-selected live ball poses (Q16) under a byte budget.
    ///   Intent  (reliable)   client -> host: grab / throw / undermine.
    ///   View    (unreliable) client -> host: camera, hand, continuous tool (vacuum / dump).
    /// Clients replay events through PitWorld transitions (exact ledger on every peer) and interpolate live balls.
    /// </summary>
    public sealed class NetPit : IPitEvents, System.IDisposable
    {
        public const byte Protocol = 2; // 2: header carries maxActive (pool size removed)
        private const string MsgHeader = "BP.Hdr", MsgReady = "BP.Ready", MsgRel = "BP.Rel", MsgSnap = "BP.Snap",
                             MsgIntent = "BP.Intent", MsgView = "BP.View";
        private const byte EvThaw = 1, EvRest = 2, EvGone = 3, EvActive = 4, EvHeld = 5, EvRelease = 6;
        private const byte RelSync = 1, RelSyncDone = 2, RelLive = 3;
        public const byte IntentGrab = 1, IntentThrow = 2, IntentUndermine = 3;
        public const byte ToolNone = 0, ToolVacuum = 1, ToolDump = 2;
        private const int SnapEntriesPerMessage = 110; // 8 + 110 * 9 = 998 bytes, under the NGO/Steam MTU

        public readonly NetworkManager NM;
        public readonly NetSettings N;
        public readonly bool IsHost;

        // Stats (per second, updated once a second)
        public int StatReliableBps, StatSnapshotBps, StatEventsPerSec;
        private int accReliable, accSnapshot, accEvents;
        private float statTimer;

        // ------------------------------------------------------------------ Host state
        private sealed class Link
        {
            public ulong clientId;
            public int player;
            public bool streaming;  // Ready received: sync + events queued for it
            public bool live;       // sync fully sent: receives snapshots
            public readonly Queue<byte[]> outq = new Queue<byte[]>();
            public byte[] syncDoneMarker;
            public float tokens;
            public int[] lastSent;
            public Vector3 viewPos, hand, toolPoint;
            public byte tool;
            public float lastViewTime = -100f;
        }

        private PitAuthority A;
        private readonly Dictionary<ulong, Link> links = new Dictionary<ulong, Link>();
        private readonly List<Link> linkList = new List<Link>();
        private byte[] evBuf = new byte[8192];
        private int evLen, evCount, evBaseTick;
        private float sendAccum;
        private float[] prioKeys = new float[2048];
        private int[] prioIdx = new int[2048];

        // ------------------------------------------------------------------ Client state
        private struct Ev { public byte type, state; public sbyte player; public int tick, id; public ulong q; }

        public bool HeaderPending { get; private set; }
        public PitSettings PendingSettings { get; private set; }
        public Bounds PendingBounds { get; private set; }
        public int LocalPlayer { get; private set; } = -1;
        public bool Synced { get; private set; }
        public bool Disconnected { get; private set; }
        public float HostDt { get; private set; } = 0.02f;
        public int PendingEvents => events.Count;
        private PitWorld CW;
        private PitReplica R;
        private ulong hostChecksum;
        private readonly Queue<Ev> events = new Queue<Ev>();
        private readonly int[] heldBy = { -1, -1, -1, -1 };
        private double clockOffset;
        private bool haveClock;
        private float viewAccum;
        private Vector3 viewCam, viewHand, viewToolPoint;
        private byte viewTool;

        private NetPit(NetworkManager nm, NetSettings n, bool host) { NM = nm; N = n; IsHost = host; }

        // ================================================================== HOST

        public static NetPit StartHost(NetworkManager nm, NetSettings n, PitAuthority authority)
        {
            var net = new NetPit(nm, n, true) { A = authority };
            authority.Events = net;
            var cmm = nm.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler(MsgReady, net.OnReady);
            cmm.RegisterNamedMessageHandler(MsgIntent, net.OnIntent);
            cmm.RegisterNamedMessageHandler(MsgView, net.OnView);
            nm.OnClientConnectedCallback += net.OnClientConnected;
            nm.OnClientDisconnectCallback += net.OnClientDisconnected;
            return net;
        }

        public int ClientCount => linkList.Count;

        private void OnClientConnected(ulong clientId)
        {
            if (!IsHost || clientId == NM.LocalClientId || links.ContainsKey(clientId)) return;
            int player = -1;
            for (int p = 1; p < PitAuthority.MaxPlayers && player < 0; p++)
            {
                bool used = false;
                for (int i = 0; i < linkList.Count; i++) if (linkList[i].player == p) used = true;
                if (!used) player = p;
            }
            if (player < 0) { NM.DisconnectClient(clientId); return; }
            var link = new Link { clientId = clientId, player = player, lastSent = new int[A.Store.Capacity], tokens = N.reliableBurstBytes };
            links[clientId] = link;
            linkList.Add(link);
            SendHeader(link);
            Debug.Log($"[BallPitNet] client {clientId} connected as player {player}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsHost)
            {
                if (clientId == NM.LocalClientId || clientId == NetworkManager.ServerClientId) Disconnected = true;
                return;
            }
            if (!links.TryGetValue(clientId, out var link)) return;
            A.ThrowHeld(link.player, Vector3.zero);
            links.Remove(clientId);
            linkList.Remove(link);
            Debug.Log($"[BallPitNet] client {clientId} (player {link.player}) disconnected");
        }

        private void SendHeader(Link link)
        {
            PitSettings s = A.S;
            Bounds b = A.W.PlayBounds;
            using (var w = new FastBufferWriter(256, Allocator.Temp))
            {
                w.WriteValueSafe(Protocol);
                w.WriteValueSafe((byte)link.player);
                w.WriteValueSafe(Time.fixedDeltaTime);
                w.WriteValueSafe(A.StepIndex);
                w.WriteValueSafe(b.center);
                w.WriteValueSafe(b.size);
                w.WriteValueSafe((byte)s.fillType);
                w.WriteValueSafe(s.pitX); w.WriteValueSafe(s.pitY); w.WriteValueSafe(s.pitZ);
                w.WriteValueSafe(s.ballDiameter); w.WriteValueSafe(s.colorCount); w.WriteValueSafe(s.seed);
                w.WriteValueSafe(s.colliderRadiusScale); w.WriteValueSafe(s.jitter); w.WriteValueSafe(s.maxSupportGapDegrees);
                w.WriteValueSafe(s.pitWalls); w.WriteValueSafe(s.renderWalls);
                w.WriteValueSafe(s.maxActive); w.WriteValueSafe(s.ballLayer);
                w.WriteValueSafe(A.W.FillChecksum);
                NM.CustomMessagingManager.SendNamedMessage(MsgHeader, link.clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private void OnReady(ulong sender, FastBufferReader r)
        {
            if (!links.TryGetValue(sender, out var link) || link.streaming) return;
            r.ReadValueSafe(out ulong checksum);
            bool full = checksum != A.W.FillChecksum;
            if (full) Debug.LogWarning($"[BallPitNet] player {link.player} fill checksum mismatch: sending a FULL resync ({A.Store.Count:n0} balls)");
            FlushEvents();           // everything so far is already in the store, so it is covered by the sync
            link.streaming = true;   // from now on live batches queue up behind the sync for this client
            EnqueueSync(link, full);
        }

        private void EnqueueSync(Link link, bool full)
        {
            BallStore st = A.Store;
            Quantizer q = A.W.Q;
            var chunk = new List<byte>(N.syncChunkBytes + 64);
            int count = 0;
            int messages = 0;

            void FlushChunk()
            {
                if (count == 0) return;
                var msg = new byte[3 + chunk.Count];
                msg[0] = RelSync;
                msg[1] = (byte)count; msg[2] = (byte)(count >> 8);
                chunk.CopyTo(msg, 3);
                link.outq.Enqueue(msg);
                chunk.Clear(); count = 0; messages++;
            }

            void Emit(int id)
            {
                BallStore.State s = st.state[id];
                switch (s)
                {
                    case BallStore.State.Resting:
                        chunk.Add(EvRest); chunk.Add(0); AddId(chunk, id); AddU64(chunk, q.Encode(st.pos[id])); break;
                    case BallStore.State.Active:
                        chunk.Add(EvActive); chunk.Add(0); AddId(chunk, id); AddU64(chunk, q.Encode(st.pos[id])); break;
                    case BallStore.State.Held:
                        int holder = 0;
                        for (int p = 0; p < PitAuthority.MaxPlayers; p++) if (A.HeldId[p] == id) holder = p;
                        chunk.Add(EvHeld); chunk.Add(0); AddId(chunk, id); chunk.Add((byte)holder); AddU64(chunk, q.Encode(st.pos[id])); break;
                    case BallStore.State.Hopper:
                        return; // emitted per hopper below (needs the owner)
                    default:
                        chunk.Add(EvGone); chunk.Add(0); AddId(chunk, id); chunk.Add((byte)s); chunk.Add(0xFF); break;
                }
                count++;
                if (chunk.Count >= N.syncChunkBytes || count == ushort.MaxValue) FlushChunk();
            }

            if (full) { for (int id = 0; id < st.Count; id++) Emit(id); }
            else { for (int i = 0; i < A.W.ModifiedCount; i++) Emit(A.W.ModifiedAt(i)); }
            for (int p = 0; p < PitAuthority.MaxPlayers; p++)
            {
                var h = A.Hopper(p);
                for (int i = 0; i < h.Count; i++)
                {
                    chunk.Add(EvGone); chunk.Add(0); AddId(chunk, h[i]); chunk.Add((byte)BallStore.State.Hopper); chunk.Add((byte)p);
                    count++;
                    if (chunk.Count >= N.syncChunkBytes || count == ushort.MaxValue) FlushChunk();
                }
            }
            FlushChunk();

            var done = new byte[1 + 4 + 4 * BallStore.StateCount];
            done[0] = RelSyncDone;
            PutI32(done, 1, A.StepIndex);
            for (int s = 0; s < BallStore.StateCount; s++) PutI32(done, 5 + 4 * s, st.StateTotal((BallStore.State)s));
            link.syncDoneMarker = done;
            link.outq.Enqueue(done);
            Debug.Log($"[BallPitNet] player {link.player}: sync queued ({messages} chunks, {(full ? st.Count : A.W.ModifiedCount):n0} balls)");
        }

        // ---- IPitEvents (host): append to the shared live batch

        private bool AnyStreaming()
        {
            for (int i = 0; i < linkList.Count; i++) if (linkList[i].streaming) return true;
            return false;
        }

        private void BeginEvent(byte type, int id, int extra)
        {
            int tick = A.StepIndex;
            if (evCount == 0) evBaseTick = tick;
            int delta = tick - evBaseTick;
            if (delta > 255 || delta < 0 || evLen > N.syncChunkBytes || evCount == ushort.MaxValue) { FlushEvents(); evBaseTick = tick; delta = 0; }
            if (evLen + 5 + extra > evBuf.Length) System.Array.Resize(ref evBuf, evBuf.Length * 2);
            evBuf[evLen++] = type;
            evBuf[evLen++] = (byte)delta;
            evBuf[evLen++] = (byte)id; evBuf[evLen++] = (byte)(id >> 8); evBuf[evLen++] = (byte)(id >> 16);
            evCount++;
            accEvents++;
        }

        private void PutQ(ulong q) { for (int b = 0; b < 8; b++) evBuf[evLen++] = (byte)(q >> (b * 8)); }

        public void Thawed(int id) { if (!AnyStreaming()) return; BeginEvent(EvThaw, id, 0); }
        public void Froze(int id, ulong q) { if (!AnyStreaming()) return; BeginEvent(EvRest, id, 8); PutQ(q); }
        public void Spawned(int id, ulong q) { if (!AnyStreaming()) return; BeginEvent(EvActive, id, 8); PutQ(q); }
        public void Released(int id) { if (!AnyStreaming()) return; BeginEvent(EvRelease, id, 0); }

        public void Gone(int id, BallStore.State s, int player)
        {
            if (!AnyStreaming()) return;
            BeginEvent(EvGone, id, 2);
            evBuf[evLen++] = (byte)s; evBuf[evLen++] = (byte)(player < 0 ? 0xFF : player);
        }

        public void Held(int id, int player, ulong q)
        {
            if (!AnyStreaming()) return;
            BeginEvent(EvHeld, id, 9);
            evBuf[evLen++] = (byte)player; PutQ(q);
        }

        private void FlushEvents()
        {
            if (evCount == 0) return;
            var msg = new byte[7 + evLen];
            msg[0] = RelLive;
            PutI32(msg, 1, evBaseTick);
            msg[5] = (byte)evCount; msg[6] = (byte)(evCount >> 8);
            System.Buffer.BlockCopy(evBuf, 0, msg, 7, evLen);
            for (int i = 0; i < linkList.Count; i++) if (linkList[i].streaming) linkList[i].outq.Enqueue(msg);
            evLen = 0; evCount = 0;
        }

        /// <summary>Host: apply remote players' hands and continuous tools. Call in FixedUpdate before authority.Step.</summary>
        public void HostPreStep()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < linkList.Count; i++)
            {
                Link l = linkList[i];
                if (now - l.lastViewTime > 0.3f) continue;
                A.HandTarget[l.player] = l.hand;
                if (l.tool == ToolVacuum) A.Excavate(l.player, l.toolPoint, A.S.vacuumRadius, A.S.vacuumPerStep);
                else if (l.tool == ToolDump) A.Dump(l.player, l.toolPoint + Vector3.up * A.S.dumpHeight, A.S.dumpPerStep);
            }
        }

        /// <summary>Host: flush events, send snapshots, pump paced reliable queues. Call once per frame (Update).</summary>
        public void HostUpdate(float dt)
        {
            sendAccum += dt;
            float interval = 1f / Mathf.Max(1f, N.sendRate);
            if (sendAccum >= interval)
            {
                sendAccum = Mathf.Min(sendAccum - interval, interval);
                FlushEvents();
                SendSnapshots();
            }
            for (int i = 0; i < linkList.Count; i++) Pump(linkList[i], dt);
            TickStats(dt);
        }

        private void Pump(Link l, float dt)
        {
            l.tokens = Mathf.Min(l.tokens + N.reliableBytesPerSecond * dt, N.reliableBurstBytes);
            while (l.outq.Count > 0 && l.tokens > 0f)
            {
                byte[] m = l.outq.Dequeue();
                using (var w = new FastBufferWriter(m.Length, Allocator.Temp))
                {
                    w.WriteBytesSafe(m, m.Length);
                    NM.CustomMessagingManager.SendNamedMessage(MsgRel, l.clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
                }
                l.tokens -= m.Length;
                accReliable += m.Length;
                if (ReferenceEquals(m, l.syncDoneMarker)) { l.live = true; l.syncDoneMarker = null; Debug.Log($"[BallPitNet] player {l.player} sync sent"); }
            }
        }

        private void SendSnapshots()
        {
            ActivePhysics act = A.Active;
            int n = act.Count, tick = A.StepIndex;
            if (prioKeys.Length < n) { prioKeys = new float[Mathf.NextPowerOfTwo(n)]; prioIdx = new int[prioKeys.Length]; }
            int budget = Mathf.Max(8, Mathf.RoundToInt(N.snapshotBytesPerSecond / Mathf.Max(1f, N.sendRate) / 9f));
            Quantizer q = A.W.Q;
            for (int li = 0; li < linkList.Count; li++)
            {
                Link l = linkList[li];
                if (!l.live) continue;
                for (int i = 0; i < n; i++)
                {
                    int id = act.IdAt(i);
                    float age = Mathf.Max(1, tick - l.lastSent[id]);
                    float speed = act.CarriedAt(i) ? 10f : act.VelAt(i).magnitude;
                    float dist = (act.PosAt(i) - l.viewPos).magnitude;
                    prioKeys[i] = -(age * (0.25f + speed) / (1f + dist * 0.05f));
                    prioIdx[i] = i;
                }
                if (n > 1) System.Array.Sort(prioKeys, prioIdx, 0, n);
                int total = Mathf.Min(n, budget), sent = 0;
                do
                {
                    int m = Mathf.Min(SnapEntriesPerMessage, total - sent);
                    int size = 6 + m * 9;
                    using (var w = new FastBufferWriter(size, Allocator.Temp))
                    {
                        w.WriteValueSafe(tick);
                        w.WriteValueSafe((ushort)m);
                        for (int k = 0; k < m; k++)
                        {
                            int i = prioIdx[sent + k];
                            int id = act.IdAt(i);
                            q.Encode16(act.PosAt(i), out ushort x, out ushort y, out ushort z);
                            w.WriteByteSafe((byte)id); w.WriteByteSafe((byte)(id >> 8)); w.WriteByteSafe((byte)(id >> 16));
                            w.WriteValueSafe(x); w.WriteValueSafe(y); w.WriteValueSafe(z);
                            l.lastSent[id] = tick;
                        }
                        NM.CustomMessagingManager.SendNamedMessage(MsgSnap, l.clientId, w, NetworkDelivery.Unreliable);
                        accSnapshot += size;
                    }
                    sent += m;
                } while (sent < total);
            }
        }

        private void OnIntent(ulong sender, FastBufferReader r)
        {
            if (!links.TryGetValue(sender, out var l)) return;
            r.ReadValueSafe(out byte type);
            switch (type)
            {
                case IntentGrab: r.ReadValueSafe(out int id); A.TryGrab(l.player, id); break;
                case IntentThrow: r.ReadValueSafe(out Vector3 v); A.ThrowHeld(l.player, v); break;
                case IntentUndermine: r.ReadValueSafe(out Vector3 c); A.Excavate(l.player, c, A.S.undermineRadius, A.S.undermineMax); break;
            }
        }

        private void OnView(ulong sender, FastBufferReader r)
        {
            if (!links.TryGetValue(sender, out var l)) return;
            r.ReadValueSafe(out l.viewPos);
            r.ReadValueSafe(out l.hand);
            r.ReadValueSafe(out l.tool);
            r.ReadValueSafe(out l.toolPoint);
            l.lastViewTime = Time.unscaledTime;
        }

        public string HostStatus()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"HOST clients {linkList.Count}  rel {StatReliableBps / 1024f:0.0} KB/s  snap {StatSnapshotBps / 1024f:0.0} KB/s  ev {StatEventsPerSec}/s");
            for (int i = 0; i < linkList.Count; i++)
            {
                Link l = linkList[i];
                sb.Append($"  | p{l.player} {(l.live ? "live" : l.streaming ? "syncing" : "joining")} q{l.outq.Count}");
            }
            return sb.ToString();
        }

        // ================================================================== CLIENT

        public static NetPit StartClient(NetworkManager nm, NetSettings n)
        {
            var net = new NetPit(nm, n, false);
            var cmm = nm.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler(MsgHeader, net.OnHeader);
            cmm.RegisterNamedMessageHandler(MsgRel, net.OnRel);
            cmm.RegisterNamedMessageHandler(MsgSnap, net.OnSnap);
            nm.OnClientDisconnectCallback += net.OnClientDisconnected;
            return net;
        }

        private void OnHeader(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out byte protocol);
            if (protocol != Protocol) { Debug.LogError($"[BallPitNet] protocol {protocol} != {Protocol}"); return; }
            r.ReadValueSafe(out byte player);
            r.ReadValueSafe(out float dt);
            r.ReadValueSafe(out int tick);
            r.ReadValueSafe(out Vector3 center);
            r.ReadValueSafe(out Vector3 size);
            var s = new PitSettings();
            r.ReadValueSafe(out byte fill); s.fillType = (FillType)fill;
            r.ReadValueSafe(out s.pitX); r.ReadValueSafe(out s.pitY); r.ReadValueSafe(out s.pitZ);
            r.ReadValueSafe(out s.ballDiameter); r.ReadValueSafe(out s.colorCount); r.ReadValueSafe(out s.seed);
            r.ReadValueSafe(out s.colliderRadiusScale); r.ReadValueSafe(out s.jitter); r.ReadValueSafe(out s.maxSupportGapDegrees);
            r.ReadValueSafe(out s.pitWalls); r.ReadValueSafe(out s.renderWalls);
            r.ReadValueSafe(out s.maxActive); r.ReadValueSafe(out s.ballLayer);
            r.ReadValueSafe(out hostChecksum);
            LocalPlayer = player;
            HostDt = dt;
            PendingSettings = s;
            PendingBounds = new Bounds(center, size);
            HeaderPending = true;
            Debug.Log($"[BallPitNet] header: player {player}, {s.pitX * s.pitY * s.pitZ:n0} balls, fill {s.fillType}");
        }

        /// <summary>Client: the controller built the pit from PendingSettings/PendingBounds; report the checksum.</summary>
        public void AttachClientWorld(PitWorld world, PitReplica replica)
        {
            HeaderPending = false;
            CW = world; R = replica;
            if (world.FillChecksum != hostChecksum)
                Debug.LogWarning("[BallPitNet] local fill differs from the host's (different build or missing baked fill). Host will send a full resync.");
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(world.FillChecksum);
                NM.CustomMessagingManager.SendNamedMessage(MsgReady, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnRel(ulong sender, FastBufferReader r)
        {
            if (CW == null) return;
            r.ReadValueSafe(out byte kind);
            if (kind == RelSync)
            {
                ReadU16(ref r, out int count);
                for (int i = 0; i < count; i++) Apply(ReadEvent(ref r, 0));
            }
            else if (kind == RelSyncDone)
            {
                r.ReadValueSafe(out int tick);
                string mismatch = null;
                for (int s = 0; s < BallStore.StateCount; s++)
                {
                    r.ReadValueSafe(out int hostTotal);
                    int mine = CW.Store.StateTotal((BallStore.State)s);
                    if (mine != hostTotal) mismatch += $" {(BallStore.State)s}:{mine}/{hostTotal}";
                }
                if (mismatch != null) Debug.LogError("[BallPitNet] ledger mismatch after sync (client/host):" + mismatch);
                else Debug.Log("[BallPitNet] synced, ledger matches host");
                UpdateClock(tick);
                Synced = true;
            }
            else if (kind == RelLive)
            {
                r.ReadValueSafe(out int baseTick);
                ReadU16(ref r, out int count);
                for (int i = 0; i < count; i++)
                {
                    Ev e = ReadEvent(ref r, baseTick);
                    if (Synced) events.Enqueue(e); else Apply(e);
                }
                UpdateClock(baseTick);
            }
        }

        private void OnSnap(ulong sender, FastBufferReader r)
        {
            if (!Synced) return;
            r.ReadValueSafe(out int tick);
            r.ReadValueSafe(out ushort m);
            UpdateClock(tick);
            float t = tick * HostDt;
            for (int k = 0; k < m; k++)
            {
                int id = ReadId(ref r);
                r.ReadValueSafe(out ushort x); r.ReadValueSafe(out ushort y); r.ReadValueSafe(out ushort z);
                if ((uint)id < (uint)CW.Store.Count && R.IsLive(id)) R.AddSample(id, CW.Q.Decode16(x, y, z), t);
            }
        }

        private void UpdateClock(int tick)
        {
            double sample = tick * (double)HostDt - Time.unscaledTimeAsDouble;
            if (!haveClock || sample > clockOffset) { clockOffset = sample; haveClock = true; }
            else clockOffset += (sample - clockOffset) * 0.02;
        }

        /// <summary>Host time the client is currently displaying.</summary>
        public float PlaybackTime => haveClock ? (float)(Time.unscaledTimeAsDouble + clockOffset - N.interpolationDelay) : 0f;

        public void ClientUpdate(float dt)
        {
            if (CW != null && Synced)
            {
                float playback = PlaybackTime;
                while (events.Count > 0 && events.Peek().tick * HostDt <= playback) Apply(events.Dequeue());
                viewAccum += dt;
                if (viewAccum >= 1f / Mathf.Max(1f, N.viewSendRate))
                {
                    viewAccum = 0f;
                    using (var w = new FastBufferWriter(64, Allocator.Temp))
                    {
                        w.WriteValueSafe(viewCam); w.WriteValueSafe(viewHand); w.WriteValueSafe(viewTool); w.WriteValueSafe(viewToolPoint);
                        NM.CustomMessagingManager.SendNamedMessage(MsgView, NetworkManager.ServerClientId, w, NetworkDelivery.Unreliable);
                    }
                }
            }
            TickStats(dt);
        }

        public void SetView(Vector3 cam, Vector3 hand, byte tool, Vector3 toolPoint)
        {
            viewCam = cam; viewHand = hand; viewTool = tool; viewToolPoint = toolPoint;
            if (R != null) R.LocalHand = hand;
        }

        public void SendGrab(int id)
        {
            using (var w = new FastBufferWriter(8, Allocator.Temp))
            {
                w.WriteValueSafe(IntentGrab);
                w.WriteValueSafe(id);
                NM.CustomMessagingManager.SendNamedMessage(MsgIntent, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        public void SendThrow(Vector3 v) => SendVectorIntent(IntentThrow, v);
        public void SendUndermine(Vector3 c) => SendVectorIntent(IntentUndermine, c);

        private void SendVectorIntent(byte type, Vector3 v)
        {
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(type);
                w.WriteValueSafe(v);
                NM.CustomMessagingManager.SendNamedMessage(MsgIntent, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void Apply(Ev e)
        {
            BallStore st = CW.Store;
            if ((uint)e.id >= (uint)st.Count) return;
            float t = e.tick * HostDt;
            switch (e.type)
            {
                case EvThaw:
                    if (st.state[e.id] != BallStore.State.Resting) break;
                    CW.ToLive(e.id, BallStore.State.Active, st.pos[e.id]);
                    R.Begin(e.id, st.pos[e.id], t);
                    break;
                case EvActive:
                {
                    Vector3 p = CW.Q.Decode(e.q);
                    ClearHeld(e.id);
                    CW.ToLive(e.id, BallStore.State.Active, p);
                    R.Begin(e.id, p, t);
                    break;
                }
                case EvHeld:
                {
                    Vector3 p = CW.Q.Decode(e.q);
                    CW.ToLive(e.id, BallStore.State.Held, p);
                    R.Begin(e.id, p, t);
                    if (e.player >= 0 && e.player < heldBy.Length) heldBy[e.player] = e.id;
                    if (e.player == LocalPlayer) R.LocalHeld = e.id;
                    break;
                }
                case EvRelease:
                    if (st.state[e.id] != BallStore.State.Held) break;
                    ClearHeld(e.id);
                    CW.ToLive(e.id, BallStore.State.Active, st.pos[e.id]);
                    break;
                case EvRest:
                    ClearHeld(e.id);
                    R.End(e.id);
                    CW.ToResting(e.id, CW.Q.Decode(e.q));
                    break;
                case EvGone:
                    ClearHeld(e.id);
                    R.End(e.id);
                    CW.ToGone(e.id, (BallStore.State)e.state);
                    break;
            }
        }

        private void ClearHeld(int id)
        {
            for (int p = 0; p < heldBy.Length; p++) if (heldBy[p] == id) heldBy[p] = -1;
            if (R != null && R.LocalHeld == id) R.LocalHeld = -1;
        }

        public int LocalHeld => R != null ? R.LocalHeld : -1;

        public string ClientStatus()
        {
            string state = Disconnected ? "DISCONNECTED" : !Synced ? (CW == null ? "waiting for header/build" : "syncing") : "live";
            float lag = haveClock ? (float)(Time.unscaledTimeAsDouble + clockOffset) - PlaybackTime : 0f;
            return $"CLIENT p{LocalPlayer} {state}  events pending {events.Count}  live {(R != null ? R.Count : 0)}  delay {lag * 1000f:0} ms";
        }

        // ================================================================== Codec helpers

        private static void AddId(List<byte> b, int id) { b.Add((byte)id); b.Add((byte)(id >> 8)); b.Add((byte)(id >> 16)); }
        private static void AddU64(List<byte> b, ulong v) { for (int i = 0; i < 8; i++) b.Add((byte)(v >> (i * 8))); }
        private static void PutI32(byte[] a, int o, int v) { a[o] = (byte)v; a[o + 1] = (byte)(v >> 8); a[o + 2] = (byte)(v >> 16); a[o + 3] = (byte)(v >> 24); }

        // Readers are passed by ref so the read position always advances on the caller's reader.
        private static int ReadId(ref FastBufferReader r)
        {
            r.ReadByteSafe(out byte b0); r.ReadByteSafe(out byte b1); r.ReadByteSafe(out byte b2);
            return b0 | (b1 << 8) | (b2 << 16);
        }

        private static void ReadU16(ref FastBufferReader r, out int v)
        {
            r.ReadByteSafe(out byte b0); r.ReadByteSafe(out byte b1);
            v = b0 | (b1 << 8);
        }

        private static ulong ReadU64(ref FastBufferReader r)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) { r.ReadByteSafe(out byte b); v |= (ulong)b << (i * 8); }
            return v;
        }

        private static Ev ReadEvent(ref FastBufferReader r, int baseTick)
        {
            var e = new Ev { player = -1 };
            r.ReadByteSafe(out e.type);
            r.ReadByteSafe(out byte delta);
            e.tick = baseTick + delta;
            e.id = ReadId(ref r);
            switch (e.type)
            {
                case EvRest:
                case EvActive:
                    e.q = ReadU64(ref r); break;
                case EvHeld:
                    r.ReadByteSafe(out byte pl); e.player = (sbyte)pl; e.q = ReadU64(ref r); break;
                case EvGone:
                    r.ReadByteSafe(out e.state); r.ReadByteSafe(out byte gp); e.player = gp == 0xFF ? (sbyte)-1 : (sbyte)gp; break;
            }
            return e;
        }

        private void TickStats(float dt)
        {
            statTimer += dt;
            if (statTimer < 1f) return;
            StatReliableBps = Mathf.RoundToInt(accReliable / statTimer);
            StatSnapshotBps = Mathf.RoundToInt(accSnapshot / statTimer);
            StatEventsPerSec = Mathf.RoundToInt(accEvents / statTimer);
            accReliable = accSnapshot = accEvents = 0;
            statTimer = 0f;
        }

        public void Dispose()
        {
            if (A != null && ReferenceEquals(A.Events, this)) A.Events = null;
            if (NM == null) return;
            NM.OnClientConnectedCallback -= OnClientConnected;
            NM.OnClientDisconnectCallback -= OnClientDisconnected;
            var cmm = NM.CustomMessagingManager;
            if (cmm == null) return;
            if (IsHost) { cmm.UnregisterNamedMessageHandler(MsgReady); cmm.UnregisterNamedMessageHandler(MsgIntent); cmm.UnregisterNamedMessageHandler(MsgView); }
            else { cmm.UnregisterNamedMessageHandler(MsgHeader); cmm.UnregisterNamedMessageHandler(MsgRel); cmm.UnregisterNamedMessageHandler(MsgSnap); }
        }
    }
}
