// FadeTransition: the v1 stub IPitTransition (Deliverable D; plan item 5). IMGUI only, so it needs no prefab, Canvas or asset:
// a full-screen white fade with a centred "Next shipment..." label. fade in -> run the director's work -> hold -> fade out,
// all on unscaled time. Replace with a garage door / crate-drop later by implementing IPitTransition.
using System.Collections;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class FadeTransition : MonoBehaviour, IPitTransition
    {
        [SerializeField] private float fadeInSeconds = 0.6f;
        [SerializeField] private float minHoldSeconds = 0.5f;
        [SerializeField] private float fadeOutSeconds = 0.6f;
        [SerializeField] private string message = "Next shipment…";

        private float alpha;
        private GUIStyle labelStyle;

        public IEnumerator Play(IEnumerator work)
        {
            yield return Fade(0f, 1f, fadeInSeconds);
            if (work != null) yield return work; // Unity runs the nested enumerator to completion
            float hold = Mathf.Max(0f, minHoldSeconds);
            for (float t = 0f; t < hold; t += Time.unscaledDeltaTime) yield return null;
            yield return Fade(1f, 0f, fadeOutSeconds);
            alpha = 0f;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            if (seconds <= 0f) { alpha = to; yield break; }
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                alpha = Mathf.Lerp(from, to, t / seconds);
                yield return null;
            }
            alpha = to;
        }

        private void OnGUI()
        {
            if (alpha <= 0.001f) return;
            GUI.depth = -1000;
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.1f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 32 };
            }
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), message, labelStyle);
            GUI.color = previous;
        }
    }
}
