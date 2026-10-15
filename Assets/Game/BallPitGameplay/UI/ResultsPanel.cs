// ResultsPanel: the placeholder end-of-pit results (brief §4.6; game-design §6). Shown on every peer when the win
// results arrive from GameplaySession (host-detected, broadcast over GameplayNet). Shows time, balls sold, money,
// upgrade levels and a couple of fun awards. Close returns to free roam (the save is already marked completed on the
// host). The optional Main Menu button ends the session: SessionController.SaveAndQuit() on the host, Leave() on a
// client (public kit API — no kit edits). Reads only IGameplayReadModel + the win payload. Unstyled uGUI/TMP.
using System;
using MultiplayerKit;
using MultiplayerKit.Ngo;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class ResultsPanel : MonoBehaviour
    {
        [SerializeField] private GameplaySession session;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private Button closeButton;
        [Tooltip("Optional: ends the session (host saves and quits; a client leaves).")]
        [SerializeField] private Button mainMenuButton;

        private bool cursorClaimed, shown;

        private void Awake()
        {
            if (session == null) session = FindObjectOfType<GameplaySession>();
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnEnable() { if (session != null) session.WinResults += Show; }
        private void OnDisable() { if (session != null) session.WinResults -= Show; ReleaseCursor(); }

        private void Show(ResultsData data)
        {
            shown = true;
            if (panelRoot != null) panelRoot.SetActive(true);
            GameplayCursor.Claim(this); cursorClaimed = true;
            if (bodyText == null) return;
            IGameplayReadModel m = session != null ? session.ReadModel : null;
            string topColor = BestColorLine(m);
            int min = Mathf.FloorToInt(data.TimeSeconds / 60f), sec = Mathf.FloorToInt(data.TimeSeconds % 60f);
            bodyText.text =
                $"Pit drained!\n\n" +
                $"Time: {min:00}:{sec:00}\n" +
                $"Balls sold: {data.BallsSold}\n" +
                $"Coins earned: {data.Money}\n" +
                $"Upgrade levels: {data.UpgradeLevels}\n" +
                $"Wrong-colour returns: {data.Rejects}\n" +
                (topColor != null ? "\n" + topColor : "");
        }

        private static string BestColorLine(IGameplayReadModel m)
        {
            if (m == null || m.ColorCount <= 0) return null;
            int best = 0, bestTotal = -1;
            for (int c = 0; c < m.ColorCount; c++) if (m.Total(c) > bestTotal) { bestTotal = m.Total(c); best = c; }
            return bestTotal <= 0 ? null : $"Most sorted colour: {m.ColorName(best)} ({m.Total(best)})";
        }

        private void Close()
        {
            shown = false;
            if (panelRoot != null) panelRoot.SetActive(false);
            ReleaseCursor();
        }

        private void GoToMainMenu()
        {
            SessionController controller = MultiplayerKitBootstrap.Instance != null ? MultiplayerKitBootstrap.Instance.Controller : null;
            if (controller == null) { Debug.LogWarning("[ResultsPanel] no SessionController; cannot leave", this); return; }
            NetworkManager nm = NetworkManager.Singleton;
            try
            {
                if (nm != null && nm.IsServer) controller.SaveAndQuit();
                else controller.Leave();
            }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void ReleaseCursor()
        {
            if (cursorClaimed) { GameplayCursor.Release(this); cursorClaimed = false; }
        }
    }
}
