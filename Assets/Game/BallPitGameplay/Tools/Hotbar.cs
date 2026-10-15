// Hotbar: the local player's tool selection (brief §4.4, §5). Reads Hotbar1..5 and the mouse wheel from A's input seam
// and equips the matching tool through PlayerInteractor.Register/Equip. The hotbar holds N tools; bite (a) registers
// exactly ONE (A's ArmsTool, already registered by the interactor), so the hotbar is a no-op selection today — but the
// seam genuinely admits more tools, powered tools and two-stage tools later with no rework. It also mirrors the active
// tool slot to PlayerToolState so remotes see which tool a player holds. Wires itself from PlayerAvatar.AnySpawned,
// local only; remotes never run it.
using System.Collections.Generic;
using UnityEngine;

namespace BallPitGameplay
{
    [DefaultExecutionOrder(-35)] // after PlayerInteractor (-40) wires, before the frame's tool Tick
    [DisallowMultipleComponent]
    public sealed class Hotbar : MonoBehaviour, IToolCatalog
    {
        private PlayerAvatar avatar;
        private PlayerInteractor interactor;
        private PlayerToolState toolState;
        private IPlayerInput input;
        private readonly int[] lastPress = new int[5];
        private bool wired, staticsSubscribed;
        private int activeIndex;

        /// <summary>The hotbar's ordered tools (currently the interactor's registered tools).</summary>
        public IReadOnlyList<IPlayerTool> Tools => interactor != null ? interactor.Tools : System.Array.Empty<IPlayerTool>();

        private void Awake()
        {
            avatar = GetComponent<PlayerAvatar>();
            interactor = GetComponent<PlayerInteractor>();
            toolState = GetComponent<PlayerToolState>();
            PlayerAvatar.AnySpawned += OnAnySpawned;
            PlayerAvatar.AnyDespawned += OnAnyDespawned;
            staticsSubscribed = true;
        }

        private void OnAnySpawned(PlayerAvatar a)
        {
            if (wired || a == null || a != avatar || a.Role != PlayerRole.Local) return;
            input = a.InputReader;
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();
            if (toolState == null) toolState = GetComponent<PlayerToolState>();
            SyncPresses();
            wired = input != null && interactor != null;
            activeIndex = 0;
            toolState?.SetTool(0);
        }

        private void OnAnyDespawned(PlayerAvatar a) { if (ReferenceEquals(a, avatar)) wired = false; }

        private void OnDestroy()
        {
            if (!staticsSubscribed) return;
            PlayerAvatar.AnySpawned -= OnAnySpawned;
            PlayerAvatar.AnyDespawned -= OnAnyDespawned;
            staticsSubscribed = false;
        }

        private void Update()
        {
            if (!wired || input == null || !input.Active) return;
            IReadOnlyList<IPlayerTool> tools = Tools;
            if (tools.Count <= 1) return; // bite (a): one tool, nothing to switch

            int target = activeIndex;
            for (int i = 0; i < 5 && i < tools.Count; i++)
            {
                int p = input.PressCount((PlayerButton)((int)PlayerButton.Hotbar1 + i));
                if (p != lastPress[i]) { lastPress[i] = p; target = i; }
            }
            float wheel = input.ToolWheel;
            if (Mathf.Abs(wheel) > 0.01f)
                target = ((activeIndex - (int)Mathf.Sign(wheel)) % tools.Count + tools.Count) % tools.Count;

            if (target != activeIndex) Equip(target);
        }

        private void Equip(int index)
        {
            IReadOnlyList<IPlayerTool> tools = Tools;
            if (index < 0 || index >= tools.Count) return;
            if (interactor.Equip(tools[index])) { activeIndex = index; toolState?.SetTool((byte)index); }
        }

        private void SyncPresses()
        {
            if (input == null) return;
            for (int i = 0; i < 5; i++) lastPress[i] = input.PressCount((PlayerButton)((int)PlayerButton.Hotbar1 + i));
        }
    }
}
