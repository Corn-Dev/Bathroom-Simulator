// ShopRow: one upgrade row in the placeholder shop (brief §4.6). Shows name, level/cost/prereq state and a Buy button
// that reports the upgrade id back to the ShopPanel. Unstyled; the owner restyles the row prefab.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class ShopRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text detailText;
        [SerializeField] private Button buyButton;

        private string upgradeId;
        private System.Action<string> onBuy;

        private void Awake()
        {
            if (buyButton != null) buyButton.onClick.AddListener(OnBuyClicked);
        }

        public void Bind(in UpgradeView view, System.Action<string> buyCallback)
        {
            upgradeId = view.Id;
            onBuy = buyCallback;
            if (nameText != null) nameText.text = view.Name;
            if (detailText != null)
            {
                string cost = view.Level >= view.MaxLevel ? "MAX" : "cost " + view.NextCost;
                string reason = string.IsNullOrEmpty(view.Reason) ? "" : "  (" + view.Reason + ")";
                detailText.text = $"lv {view.Level}/{view.MaxLevel} • {cost}{reason}";
            }
            if (buyButton != null) buyButton.interactable = view.CanBuy;
        }

        private void OnBuyClicked() { if (!string.IsNullOrEmpty(upgradeId)) onBuy?.Invoke(upgradeId); }
    }
}
