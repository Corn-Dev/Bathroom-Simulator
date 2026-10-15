// GameplayHud: the placeholder loop HUD (brief §4.6). Shows money, per-colour balls-left + % sold, the local carry
// count + current tool, and a short sale/purchase feed. Reads ONLY IGameplayReadModel (from GameplaySession); it never
// reaches into the kit, NGO or the pit. Unstyled uGUI/TMP — the owner restyles the prefab. Author-placed on a Canvas in
// the gameplay scene.
using System.Text;
using TMPro;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayHud : MonoBehaviour
    {
        [SerializeField] private GameplaySession session;
        [Tooltip("Money text.")]
        [SerializeField] private TMP_Text moneyText;
        [Tooltip("Per-colour progress (one line per colour).")]
        [SerializeField] private TMP_Text colorsText;
        [Tooltip("Carry count + current tool.")]
        [SerializeField] private TMP_Text carryText;
        [Tooltip("Recent sale/purchase feed.")]
        [SerializeField] private TMP_Text feedText;
        [Tooltip("Refresh interval (s).")]
        [SerializeField, Min(0f)] private float refreshInterval = 0.25f;

        private readonly StringBuilder sb = new StringBuilder(256);
        private float nextRefresh;

        private void Awake()
        {
            if (session == null) session = FindObjectOfType<GameplaySession>();
        }

        private void OnEnable()
        {
            if (session != null) session.FeedChanged += RefreshFeed;
        }

        private void OnDisable()
        {
            if (session != null) session.FeedChanged -= RefreshFeed;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            Refresh();
        }

        private void Refresh()
        {
            IGameplayReadModel m = session != null ? session.ReadModel : null;
            if (m == null || !m.IsLive) return;

            if (moneyText != null) moneyText.text = "Coins: " + m.Money;

            if (colorsText != null)
            {
                sb.Clear();
                int colors = m.ColorCount;
                for (int c = 0; c < colors; c++)
                {
                    int pct = Mathf.RoundToInt(m.SoldFraction(c) * 100f);
                    sb.Append(m.ColorName(c)).Append(": ").Append(m.InPlay(c)).Append(" left (").Append(pct).Append("%)");
                    if (c < colors - 1) sb.Append('\n');
                }
                colorsText.text = sb.ToString();
            }

            if (carryText != null) carryText.text = $"Carrying {m.CarryCount} • {m.CurrentToolName}";
            RefreshFeed();
        }

        private void RefreshFeed()
        {
            IGameplayReadModel m = session != null ? session.ReadModel : null;
            if (m == null || feedText == null) return;
            sb.Clear();
            System.Collections.Generic.IReadOnlyList<string> feed = m.Feed;
            for (int i = 0; i < feed.Count; i++) { sb.Append(feed[i]); if (i < feed.Count - 1) sb.Append('\n'); }
            feedText.text = sb.ToString();
        }
    }
}
