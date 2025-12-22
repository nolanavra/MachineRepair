using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MachineRepair.Flavor
{
    /// <summary>
    /// Lightweight UI helper that spawns canned response buttons while a chat bubble is active.
    /// Expects a prefabbed Button with a TMP label.
    /// </summary>
    public class FlavorResponseUI : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private RectTransform root;
        [SerializeField] private RectTransform optionContainer;
        [SerializeField] private Button optionButtonPrefab;

        private readonly List<Button> _spawnedButtons = new();
        private Action<int> _onSelected;

        void Awake()
        {
            Hide();
        }

        public bool ShowResponses(IReadOnlyList<string> labels, Action<int> onSelected)
        {
            ClearOptions();
            _onSelected = onSelected;

            if (labels == null || labels.Count == 0)
            {
                Hide();
                return false;
            }

            if (optionButtonPrefab == null || ResolveContainer() == null)
            {
                Debug.LogWarning("[FlavorResponseUI] Missing button prefab or container; cannot show responses.");
                Hide();
                return false;
            }

            var rootTransform = ResolveRoot();
            if (rootTransform != null && !rootTransform.gameObject.activeSelf)
                rootTransform.gameObject.SetActive(true);

            for (int i = 0; i < labels.Count; i++)
            {
                SpawnOption(labels[i], i);
            }

            return _spawnedButtons.Count > 0;
        }

        public void Hide()
        {
            _onSelected = null;
            ClearOptions();
            var rootTransform = ResolveRoot();
            if (rootTransform != null)
                rootTransform.gameObject.SetActive(false);
        }

        void SpawnOption(string label, int index)
        {
            var btn = Instantiate(optionButtonPrefab, ResolveContainer());
            btn.gameObject.SetActive(true);

            var text = btn.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = string.IsNullOrWhiteSpace(label) ? "Respond" : label;
            }

            btn.onClick.AddListener(() => OnOptionClicked(index));
            _spawnedButtons.Add(btn);
        }

        void OnOptionClicked(int index)
        {
            _onSelected?.Invoke(index);
        }

        void ClearOptions()
        {
            foreach (var btn in _spawnedButtons)
            {
                if (btn != null)
                    Destroy(btn.gameObject);
            }
            _spawnedButtons.Clear();
        }

        RectTransform ResolveRoot()
        {
            if (root != null) return root;
            return transform as RectTransform;
        }

        RectTransform ResolveContainer()
        {
            if (optionContainer != null) return optionContainer;
            return transform as RectTransform;
        }
    }
}
