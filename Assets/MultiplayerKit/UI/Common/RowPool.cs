// Pooled list rows (05 §3.7): Ensure(count) instantiates the missing rows under the content RectTransform,
// activates the first count and deactivates the rest. Rows are never destroyed during a screen's life, so
// refreshing a browser of 50 lobbies does not churn GameObjects. Pooled rows are rebound with different models,
// so row views clear their own listeners in Bind (05 §9.3). Plain class owned by a list view.
// Design: 05 §3.7, §9.3; 06 U6.

using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MultiplayerKit.UI
{
    public sealed class RowPool<T> where T : Component
    {
        private readonly T prefab;
        private readonly RectTransform content;
        private readonly List<T> rows = new List<T>();

        public RowPool(T prefab, RectTransform content)
        {
            if ((Object)prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (content == null) throw new ArgumentNullException(nameof(content));
            this.prefab = prefab;
            this.content = content;
        }

        /// <summary>Rows activated by the last Ensure.</summary>
        public int ActiveCount { get; private set; }

        /// <summary>Active row by index, 0 to ActiveCount - 1, in list order.</summary>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= ActiveCount) throw new ArgumentOutOfRangeException(nameof(index));
                return rows[index];
            }
        }

        /// <summary>Instantiates rows up to count (never fewer than exist), activates the first count, deactivates the rest.</summary>
        public void Ensure(int count)
        {
            if (count < 0) count = 0;

            while (rows.Count < count)
            {
                T row = Object.Instantiate(prefab, content, false);
                row.name = prefab.name + " " + rows.Count;
                rows.Add(row);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                GameObject go = rows[i].gameObject;
                bool active = i < count;
                if (go.activeSelf != active) go.SetActive(active);
            }
            ActiveCount = count;
        }
    }
}
