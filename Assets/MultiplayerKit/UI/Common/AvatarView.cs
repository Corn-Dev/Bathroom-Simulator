// RawImage avatar for one player (05 §3.7): the placeholder first, then the identity service's cached texture.
// A generation counter plus the id check drop late callbacks after a pooled row was rebound or disabled.
// IdentityChanged for the shown player re-requests, because the LRU cache may have evicted and destroyed the old
// texture (01 §4.5). The identity service owns every texture: this view never destroys one, and uvRect stays
// (0,0,1,1) because the service already flipped the rows. A plain MonoBehaviour: rows pass the identity in.
// Design: 05 §1 (RawImage), §3.7, §9.3 (inspector fields); 06 U5.

using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class AvatarView : MonoBehaviour
    {
        [SerializeField] private RawImage image;
        [SerializeField] private Texture placeholder;

        private IPlayerIdentity identity;
        private PlayerId player;
        private int generation;
        private bool subscribed;

        /// <summary>The player currently shown; None after Clear.</summary>
        public PlayerId Player => player;

        private void Awake()
        {
            if (image == null) image = GetComponent<RawImage>();
            if (image == null) KitLog.Error("AvatarView on '" + name + "' has no RawImage; avatars will not show.");
            if (!player.IsValid) SetTexture(placeholder);   // Show may already have run on an inactive row
        }

        private void OnDisable()
        {
            Clear();
        }

        /// <summary>Shows the placeholder, then the avatar when it arrives. Rebinding the shown player keeps its texture.</summary>
        public void Show(IPlayerIdentity playerIdentity, PlayerId shownPlayer)
        {
            // Rows are rebound on every roster change; a texture that is still alive stays. A destroyed (evicted)
            // texture reads as null here and is requested again.
            if (subscribed && playerIdentity == identity && shownPlayer == player && image != null && image.texture != null) return;

            Unsubscribe();
            identity = playerIdentity;
            player = shownPlayer;
            generation++;
            SetTexture(placeholder);
            if (identity == null || !player.IsValid) return;

            identity.IdentityChanged += OnIdentityChanged;
            subscribed = true;
            Request();
        }

        /// <summary>Stops listening and shows the placeholder.</summary>
        public void Clear()
        {
            Unsubscribe();
            identity = null;
            player = PlayerId.None;
            generation++;
            SetTexture(placeholder);
        }

        private void Request()
        {
            int requested = generation;
            identity.RequestAvatar(player, (id, texture) => OnAvatar(requested, id, texture));
        }

        // A cache hit calls back synchronously, inside Request.
        private void OnAvatar(int requested, PlayerId id, Texture2D texture)
        {
            if (this == null) return;                                    // destroyed under a pending request
            if (requested != generation || id != player) return;         // rebound or cleared since
            if (texture == null) return;                                 // no avatar or timed out: keep the placeholder
            SetTexture(texture);
        }

        private void OnIdentityChanged(PlayerId id)
        {
            if (this == null || identity == null || id != player) return;
            generation++;
            SetTexture(placeholder);   // the old texture may have been evicted and destroyed
            Request();
        }

        private void Unsubscribe()
        {
            if (subscribed && identity != null) identity.IdentityChanged -= OnIdentityChanged;
            subscribed = false;
        }

        // Without a placeholder the image hides instead of drawing a white square.
        private void SetTexture(Texture texture)
        {
            if (image == null) return;
            image.texture = texture;
            image.enabled = texture != null;
        }
    }
}
