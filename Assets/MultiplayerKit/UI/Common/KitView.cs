// Base class of every kit view (05 §2.3): binds its presenter while enabled and the kit services are published,
// unbinds in OnDisable, and stays unbound while a required serialized field is missing (05 §10). Also holds the
// helpers every view shares: CanvasGroup visibility, guarded callbacks and keyboard selection (05 §11).
// Design: 05 §2.1-§2.3, §10, §11; 06 U2. Subclasses override OnEnable/OnDisable only with base calls.

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MultiplayerKit.UI
{
    public abstract class KitView : MonoBehaviour
    {
        private bool bound;
        private bool waitingForServices;

        /// <summary>The published services while bound; null otherwise.</summary>
        protected KitServices Services { get; private set; }

        protected bool IsBound => bound;

        /// <summary>Create the presenter and call its Bind, which renders the current state at once (05 §2.3).</summary>
        protected abstract void OnBind(KitServices services);

        /// <summary>Call the presenter's Unbind and drop it. Runs only after OnBind.</summary>
        protected abstract void OnUnbind();

        /// <summary>
        /// Checks the required serialized fields with <see cref="Require"/>, combined with the non-short-circuit
        /// <c>&amp;</c> so every missing field is logged. False keeps the view unbound until it is enabled again.
        /// </summary>
        protected virtual bool ValidateReferences()
        {
            return true;
        }

        protected virtual void OnEnable()
        {
            if (!ValidateReferences()) return;

            KitServices current = KitRuntime.Current;
            if (current != null)
            {
                Bind(current);
                return;
            }

            // The bootstrap publishes in its Start, so views of the first scene usually enable before it (05 §2.1).
            if (!waitingForServices)
            {
                KitRuntime.Published += OnServicesPublished;
                waitingForServices = true;
            }
        }

        protected virtual void OnDisable()
        {
            StopWaiting();
            if (!bound) return;

            bound = false;
            try
            {
                OnUnbind();
            }
            catch (Exception e)
            {
                KitLog.Error(Describe() + " threw in OnUnbind.", e);
            }
            Services = null;
        }

        private void OnServicesPublished(KitServices services)
        {
            StopWaiting();
            if (this == null || !isActiveAndEnabled) return;
            Bind(services);
        }

        private void Bind(KitServices services)
        {
            if (bound || services == null) return;

            Services = services;
            bound = true;   // stays true if OnBind throws, so OnDisable still removes whatever was subscribed
            try
            {
                OnBind(services);
            }
            catch (Exception e)
            {
                KitLog.Error(Describe() + " threw in OnBind.", e);
            }
        }

        private void StopWaiting()
        {
            if (!waitingForServices) return;
            KitRuntime.Published -= OnServicesPublished;
            waitingForServices = false;
        }

        /// <summary>True if assigned; otherwise logs an Error naming the field and the GameObject (05 §10).</summary>
        protected bool Require(Object reference, string fieldName)
        {
            if (reference != null) return true;
            KitLog.Error(Describe() + ": required field '" + fieldName + "' is not assigned; the view stays inactive.");
            return false;
        }

        private string Describe()
        {
            return GetType().Name + " on '" + name + "'";
        }

        /// <summary>Shows or hides a panel through its CanvasGroup (alpha, interactable, blocksRaycasts).</summary>
        protected static void SetGroupVisible(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        /// <summary>
        /// Overlay widgets stay active and hide through their CanvasGroup, so their Update keeps running. A widget
        /// left inactive in the prefab is activated the first time it is shown.
        /// </summary>
        protected void SetPanelVisible(CanvasGroup group, bool visible)
        {
            if (visible && !gameObject.activeSelf) gameObject.SetActive(true);
            SetGroupVisible(group, visible);
        }

        /// <summary>Runs a caller's callback and logs instead of throwing, so a failing caller cannot break a widget.</summary>
        protected static void SafeInvoke(Action callback, string what)
        {
            if (callback == null) return;
            try
            {
                callback();
            }
            catch (Exception e)
            {
                KitLog.Error(what + " callback threw.", e);
            }
        }

        protected static void SafeInvoke<T>(Action<T> callback, T argument, string what)
        {
            if (callback == null) return;
            try
            {
                callback(argument);
            }
            catch (Exception e)
            {
                KitLog.Error(what + " callback threw.", e);
            }
        }

        /// <summary>Selects a control for keyboard navigation and returns what was selected before (or null).</summary>
        protected static GameObject SelectAndRemember(Selectable target)
        {
            EventSystem system = EventSystem.current;
            if (system == null) return null;

            GameObject previous = system.currentSelectedGameObject;
            if (target != null && target.gameObject.activeInHierarchy) system.SetSelectedGameObject(target.gameObject);
            return previous;
        }

        /// <summary>Restores a selection saved by SelectAndRemember if it is still active, otherwise clears it.</summary>
        protected static void RestoreSelection(GameObject previous)
        {
            EventSystem system = EventSystem.current;
            if (system == null) return;
            system.SetSelectedGameObject(previous != null && previous.activeInHierarchy ? previous : null);
        }
    }
}
