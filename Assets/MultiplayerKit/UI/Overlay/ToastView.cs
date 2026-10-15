// Toasts (05 §3.5): one visible at a time, top-centre, from a queue. Info 3 s, Warning 5 s, Error 6 s of unscaled
// time including a short fade in and out. Queue cap 8: when full the oldest queued toast is dropped, never the
// visible one. Identical text that is visible, or was queued less than 2 s ago, is dropped, so a burst of identical
// Core warnings shows once. Severity colours tint the background; a click dismisses early.
// Design: 05 §3.5, §9.1 (inspector fields); 06 U11. Shown and hidden through its CanvasGroup.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class ToastView : KitView
    {
        public const int QueueCapacity = 8;
        public const float DedupeSeconds = 2f;

        private const float MinimumSeconds = 0.5f;

        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text text;
        [SerializeField] private Button dismissButton;
        [SerializeField] private float infoSeconds = 3f;
        [SerializeField] private float warningSeconds = 5f;
        [SerializeField] private float errorSeconds = 6f;
        [SerializeField] private Color infoColor = new Color(0.16f, 0.18f, 0.22f, 0.94f);
        [SerializeField] private Color warningColor = new Color(0.55f, 0.36f, 0.04f, 0.96f);
        [SerializeField] private Color errorColor = new Color(0.64f, 0.13f, 0.13f, 0.96f);
        [SerializeField] private float fadeSeconds = 0.2f;

        private struct Entry
        {
            public ToastSeverity Severity;
            public string Text;
            public float QueuedAt;
        }

        private readonly List<Entry> queue = new List<Entry>();
        private Entry current;
        private bool showing;
        private bool dismissed;
        private float shownAt;
        private float endAt;   // seconds after shownAt at which the visible toast is gone

        public bool IsShowing => showing;
        public int QueuedCount => queue.Count;

        private void Awake()
        {
            if (dismissButton != null) dismissButton.onClick.AddListener(Dismiss);
            if (text != null) text.richText = false;   // toasts can carry player names; never parse them as tags
            if (!showing) SetPanelVisible(group, false);
        }

        protected override bool ValidateReferences()
        {
            return Require(group, nameof(group)) & Require(background, nameof(background)) & Require(text, nameof(text))
                & Require(dismissButton, nameof(dismissButton));
        }

        // KitOverlay drives this widget; it has nothing of its own to bind.
        protected override void OnBind(KitServices services)
        {
        }

        protected override void OnUnbind()
        {
        }

        public void Enqueue(ToastSeverity severity, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            float now = Time.unscaledTime;
            if (showing && !dismissed && current.Text == message) return;
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].Text == message && now - queue[i].QueuedAt < DedupeSeconds) return;
            }

            if (queue.Count >= QueueCapacity) queue.RemoveAt(0);
            queue.Add(new Entry { Severity = severity, Text = message, QueuedAt = now });
            if (!showing) ShowNext(now);
        }

        /// <summary>Fades the visible toast out now (click); the next queued toast follows.</summary>
        public void Dismiss()
        {
            if (!showing || dismissed) return;
            dismissed = true;
            float elapsed = Time.unscaledTime - shownAt;
            endAt = Mathf.Min(endAt, elapsed + Mathf.Max(0f, fadeSeconds));
            if (group != null)
            {
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }

        private void Update()
        {
            if (!showing) return;

            float now = Time.unscaledTime;
            float elapsed = now - shownAt;
            if (elapsed >= endAt)
            {
                showing = false;
                SetPanelVisible(group, false);
                if (queue.Count > 0) ShowNext(now);
                return;
            }
            if (group != null) group.alpha = AlphaAt(elapsed);
        }

        private void ShowNext(float now)
        {
            current = queue[0];
            queue.RemoveAt(0);
            showing = true;
            dismissed = false;
            shownAt = now;
            endAt = Mathf.Max(MinimumSeconds, DurationFor(current.Severity));

            if (text != null) text.text = current.Text;
            if (background != null) background.color = ColorFor(current.Severity);
            SetPanelVisible(group, true);
            if (group != null) group.alpha = AlphaAt(0f);
        }

        // Fade in over the first fadeSeconds, hold, fade out over the last fadeSeconds.
        private float AlphaAt(float elapsed)
        {
            if (fadeSeconds <= 0f) return 1f;
            return Mathf.Clamp01(Mathf.Min(elapsed / fadeSeconds, (endAt - elapsed) / fadeSeconds));
        }

        private float DurationFor(ToastSeverity severity)
        {
            switch (severity)
            {
                case ToastSeverity.Error: return errorSeconds;
                case ToastSeverity.Warning: return warningSeconds;
                default: return infoSeconds;
            }
        }

        private Color ColorFor(ToastSeverity severity)
        {
            switch (severity)
            {
                case ToastSeverity.Error: return errorColor;
                case ToastSeverity.Warning: return warningColor;
                default: return infoColor;
            }
        }
    }
}
