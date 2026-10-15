// PlayerHud: the local player's minimal HUD (deliverable A, file plan P10; design c 10, M6): a crosshair Graphic and a
// hint line, on the root of PlayerHud.prefab. Unstyled: sprites, fonts, sizes and colors live in the prefab; the code
// only toggles visibility, color and text. PlayerView instantiates it for the local player at the first release, hides
// it on freeze and destroys it on despawn. It has no GraphicRaycaster, so it never eats a click meant for the kit.
// Reads PlayerAvatar, PlayerClimber and PlayerInputReader only; the view and the interactor push state into it.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BallPitGameplay
{
    public enum CrosshairState { Idle, Target, Holding }
    public enum PlayerHint { None, RespawnOffer, RespawnUnavailable }

    [DisallowMultipleComponent]
    public sealed class PlayerHud : MonoBehaviour
    {
        [Tooltip("PlayerHud/Crosshair's Image (or a TMP_Text). Raycast Target off.")]
        [SerializeField] private Graphic crosshair;
        [SerializeField] private Color idleColor = Color.white;
        [SerializeField] private Color targetColor = new Color(1f, 0.9f, 0.2f);
        [SerializeField] private Color holdingColor = new Color(0.4f, 0.9f, 1f);
        [Tooltip("PlayerHud/Hint's text. Raycast Target off.")]
        [SerializeField] private TMP_Text hint;
        [SerializeField] private string respawnOfferText = "Stuck? Press R to respawn.";
        [SerializeField] private string respawnUnavailableText = "Respawn unavailable.";
        [Tooltip("How long the 'Respawn unavailable' hint stays (s, unscaled).")]
        [SerializeField, Min(0f)] private float unavailableHintSeconds = 2f;

        private PlayerAvatar avatar;
        private PlayerClimber climber;
        private PlayerInputReader input;
        private bool crosshairWanted = true;
        private CrosshairState state = CrosshairState.Idle;
        private PlayerHint shown = PlayerHint.None;
        private float hintUntil;

        public CrosshairState Crosshair => state;
        public PlayerHint Hint => shown;

        /// <summary>Binds to the local avatar: the climber's respawn offer and unavailable events, and the input block
        /// (polled). Rebinding drops the previous avatar.</summary>
        public void Bind(PlayerAvatar target)
        {
            Unbind();
            avatar = target;
            climber = target != null ? target.Climber : null;
            input = target != null ? target.InputReader : null;
            if (climber != null)
            {
                climber.RespawnOfferChanged += OnRespawnOfferChanged;
                climber.RespawnUnavailable += OnRespawnUnavailable;
            }
            SetCrosshair(CrosshairState.Idle);
            ShowHint(climber != null && climber.RespawnOffered ? PlayerHint.RespawnOffer : PlayerHint.None, 0f);
        }

        /// <summary>An extra switch on top of the automatic rule (released and not under a menu, modal or panel).</summary>
        public void SetCrosshairVisible(bool visible)
        {
            crosshairWanted = visible;
            ApplyCrosshairVisibility();
        }

        /// <summary>The interactor's hover and holding state.</summary>
        public void SetCrosshair(CrosshairState value)
        {
            state = value;
            if (crosshair == null) return;
            crosshair.color = value == CrosshairState.Target ? targetColor : value == CrosshairState.Holding ? holdingColor : idleColor;
        }

        /// <summary>Shows a hint; seconds 0 = until replaced. Timed with unscaled time.</summary>
        public void ShowHint(PlayerHint value, float seconds)
        {
            shown = value;
            hintUntil = seconds > 0f ? Time.unscaledTime + seconds : float.PositiveInfinity;
            if (hint == null) return;
            string text = value == PlayerHint.RespawnOffer ? respawnOfferText
                        : value == PlayerHint.RespawnUnavailable ? respawnUnavailableText : "";
            hint.text = text;
            hint.enabled = text.Length > 0;
        }

        private void Awake()
        {
            if (crosshair == null) Debug.LogWarning("[PlayerHud] no crosshair Graphic assigned", this);
            if (hint == null) Debug.LogWarning("[PlayerHud] no hint text assigned", this);
            ShowHint(PlayerHint.None, 0f);
        }

        private void Update()
        {
            ApplyCrosshairVisibility();
            if (shown != PlayerHint.None && Time.unscaledTime >= hintUntil)
                ShowHint(climber != null && climber.RespawnOffered ? PlayerHint.RespawnOffer : PlayerHint.None, 0f);
        }

        private void OnDestroy() => Unbind();

        // Visible only while the avatar is released and no kit menu, modal or gameplay panel owns the cursor.
        private void ApplyCrosshairVisibility()
        {
            if (crosshair == null) return;
            bool blocked = input != null && InputBlockPolicy.BlocksDevKeys(input.BlockReasons);
            bool show = crosshairWanted && avatar != null && avatar.IsReleased && !blocked;
            if (crosshair.enabled != show) crosshair.enabled = show;
        }

        private void OnRespawnOfferChanged(bool offered)
        {
            if (shown == PlayerHint.RespawnUnavailable && Time.unscaledTime < hintUntil) return; // the timed hint finishes first
            ShowHint(offered ? PlayerHint.RespawnOffer : PlayerHint.None, 0f);
        }

        private void OnRespawnUnavailable() => ShowHint(PlayerHint.RespawnUnavailable, unavailableHintSeconds);

        private void Unbind()
        {
            if (climber != null)
            {
                climber.RespawnOfferChanged -= OnRespawnOfferChanged;
                climber.RespawnUnavailable -= OnRespawnUnavailable;
            }
            avatar = null;
            climber = null;
            input = null;
        }
    }
}
