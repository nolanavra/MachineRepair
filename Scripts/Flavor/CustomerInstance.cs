using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MachineRepair.Flavor
{
    /// <summary>
    /// Runtime representation of a single chatting customer.
    /// Stores authored tags, portrait, and a lightweight mood gauge.
    /// </summary>
    [System.Serializable]
    public class CustomerInstance
    {
        [Tooltip("Portrait sprite to render when this customer speaks.")]
        [SerializeField] private Sprite portrait;

        [Tooltip("Audience tags represented by this customer.")]
        [SerializeField] private List<FlavorContextTag> tags = new() { FlavorContextTag.General };

        [Range(0, 100)]
        [SerializeField, Tooltip("Lightweight sentiment meter. 0 = hostile, 100 = delighted.")]
        private int mood = 70;

        private HashSet<FlavorContextTag> _tagSet;

        public Sprite Portrait => portrait;
        public int Mood => mood;
        public float MoodFactor => Mathf.Lerp(0.5f, 1.5f, Mathf.Clamp01(mood / 100f));

        public IReadOnlyCollection<FlavorContextTag> Tags
        {
            get
            {
                _tagSet ??= new HashSet<FlavorContextTag>(tags ?? Enumerable.Empty<FlavorContextTag>());
                return _tagSet;
            }
        }

        public bool HasTag(FlavorContextTag tag) => Tags.Contains(tag);

        /// <summary>Clamp the stored mood into range and return the clamped value.</summary>
        public int ClampMood()
        {
            mood = Mathf.Clamp(mood, 0, 100);
            return mood;
        }

        /// <summary>Adjust mood by delta, clamp to [0,100], and return the new value.</summary>
        public int AdjustMood(int delta)
        {
            mood = Mathf.Clamp(mood + delta, 0, 100);
            return mood;
        }

        /// <summary>Explicitly set mood (clamped) and return the clamped value.</summary>
        public int SetMood(int value)
        {
            mood = Mathf.Clamp(value, 0, 100);
            return mood;
        }

        /// <summary>Replace tag list with a new set of tags.</summary>
        public void SetTags(IEnumerable<FlavorContextTag> newTags)
        {
            tags = newTags != null ? newTags.Distinct().ToList() : new List<FlavorContextTag> { FlavorContextTag.General };
            _tagSet = null;
        }

        public void SetPortrait(Sprite sprite)
        {
            portrait = sprite;
        }
    }
}
