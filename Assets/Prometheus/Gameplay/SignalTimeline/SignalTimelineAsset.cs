using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{

    [Obsolete]
    /// <summary>
    /// A data-only timeline. The list uses SerializeReference so it can contain any Signal subtype.
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Signal Timeline", fileName = "SignalTimeline")]
    public sealed class SignalTimelineAsset : ScriptableObject
    {
        [SerializeField, Min(0.01f)] private float _duration = 1f;
        [SerializeReference] private List<Signal> _signals = new List<Signal>();

        public float Duration
        {
            get => _duration;
            set => _duration = Mathf.Max(0.01f, value);
        }

        public IReadOnlyList<Signal> Signals => _signals;

        public void SortSignals()
        {
            _signals.Sort(CompareSignals);
        }

        private void OnValidate()
        {
            _duration = Mathf.Max(0.01f, _duration);

            if (_signals == null)
                _signals = new List<Signal>();

            for (var i = _signals.Count - 1; i >= 0; i--)
            {
                if (_signals[i] == null)
                    _signals.RemoveAt(i);
            }
        }

        private static int CompareSignals(Signal left, Signal right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            return left.Time.CompareTo(right.Time);
        }
    }
}
