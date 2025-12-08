using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MachineRepair.Flavor
{
    public class FlavorChatService : MonoBehaviour
    {
        [Header("Authoring")]
        public FlavorBank bank;

        [Header("UI Spawn")]
        public ChatBubbleUI bubblePrefab;
        [Tooltip("ChatBubbleContainer RectTransform under the front-view UI; falls back to the nearest Canvas if null.")]
        public RectTransform bubbleParent;
        [Min(1f)] public float bubbleLifetime = 6f;

        [Header("Customer Visuals")]
        public List<Sprite> customerSprites = new();
        public CameraGridFocusController cameraFocusController;

        [Header("Timing (minutes)")]
        public Vector2 intervalMinutes = new Vector2(3f, 8f);

        [Header("Context Source")]
        public MonoBehaviour contextSourceBehaviour; // must implement IFlavorContextSource

        [Header("Debug")]
        public bool verbose = false;

        // internal
        private IFlavorContextSource contextSource;
        private readonly Dictionary<FlavorLine, double> _nextAllowed = new();
        private readonly HashSet<FlavorLine> _shownThisSession = new();
        private Coroutine _loop;
        private RectTransform _resolvedParent;

        void Awake()
        {
            contextSource = contextSourceBehaviour as IFlavorContextSource;
            if (verbose && contextSource == null)
                Debug.LogWarning("[FlavorChatService] contextSourceBehaviour is null or does not implement IFlavorContextSource.");
        }

        void OnEnable()
        {
            if (_loop == null) _loop = StartCoroutine(Loop());
        }

        void OnDisable()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
        }

        IEnumerator Loop()
        {
            while (true)
            {
                float min = Mathf.Min(intervalMinutes.x, intervalMinutes.y);
                float max = Mathf.Max(intervalMinutes.x, intervalMinutes.y);
                float wait = Random.Range(min, max) * 60f;
                yield return new WaitForSeconds(wait);
                TryEmit(false);
            }
        }

        /// <summary>Editor/Debug hook: emit immediately (bypasses cooldowns).</summary>
        public void ForceEmitNow()
        {
            TryEmit(true); // <-- force path
        }

#if UNITY_EDITOR
        [ContextMenu("Force Emit Now")]
        void CtxForceEmitNow()
        {
            ForceEmitNow();
        }
#endif

        /// <summary>
        /// Attempts to emit one flavor line. If forced==true, cooldowns are ignored,
        /// but other conditions (mode/requirements) still apply.
        /// </summary>
        void TryEmit(bool forced)
        {
            if (verbose) Debug.Log($"[FlavorChatService] TryEmit(forced={forced})");

            if (bank == null || bank.lines == null || bank.lines.Count == 0)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] No bank or lines assigned.");
                return;
            }
            if (contextSource == null)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] No context source.");
                return;
            }
            if (bubblePrefab == null)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] bubblePrefab is not assigned.");
                return;
            }

            var ctx = contextSource.CaptureFlavorContext();
            var now = Time.realtimeSinceStartupAsDouble;

            var candidates = new List<(FlavorLine line, int weight)>();
            int skippedCooldown = 0, skippedMode = 0, skippedReqs = 0;

            foreach (var line in bank.lines)
            {
                if (line == null) continue;

                if (line.oncePerSession && _shownThisSession.Contains(line))
                    continue;

                if (!forced && _nextAllowed.TryGetValue(line, out double tNext) && now < tNext)
                {
                    skippedCooldown++;
                    continue;
                }

                // Mode requirement
                if (line.requireMode == FlavorModeReq.Place && !ctx.inPlaceMode) { skippedMode++; continue; }
                if (line.requireMode == FlavorModeReq.Inspect && !ctx.inInspectMode) { skippedMode++; continue; }

                // Other requirements
                bool fail = false;
                if (ctx.totalParts < line.minTotalParts) fail = true;
                if (ctx.boilers < line.minBoilers) fail = true;
                if (line.requireWaterRouting && !ctx.hasWaterRoute) fail = true;
                if (line.requirePowerRouting && !ctx.hasPowerRoute) fail = true;
                if (fail) { skippedReqs++; continue; }

                candidates.Add((line, Mathf.Max(1, line.weight)));
            }

            if (verbose)
                Debug.Log($"[FlavorChatService] lines={bank.lines.Count}, candidates={candidates.Count}, skipped cooldown={skippedCooldown}, mode={skippedMode}, reqs={skippedReqs}; ctx: parts={ctx.totalParts}, boilers={ctx.boilers}, water={ctx.hasWaterRoute}, power={ctx.hasPowerRoute}, mode={(ctx.inPlaceMode ? "Place" : "Inspect")}");

            if (candidates.Count == 0)
            {
                if (forced)
                    SpawnBubble("[debug] Forced emit: no candidates matched.", PickCustomerSprite());
                return;
            }

            // Weighted pick
            int sum = 0;
            foreach (var c in candidates) sum += c.weight;
            int pick = Random.Range(0, sum);
            FlavorLine chosen = null;
            foreach (var c in candidates)
            {
                if (pick < c.weight) { chosen = c.line; break; }
                pick -= c.weight;
            }
            if (chosen == null)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] Weighted pick failed.");
                return;
            }

            string text = FillTemplate(chosen.template, ctx);
            SpawnBubble(text, PickCustomerSprite());

            _nextAllowed[chosen] = now + chosen.minCooldownSeconds;
            if (chosen.oncePerSession) _shownThisSession.Add(chosen);

            if (verbose) Debug.Log($"[FlavorChatService] Emitted: {text}");
        }

        // ----------------- Helpers defined in THIS CLASS -----------------

        string FillTemplate(string tpl, FlavorContext ctx)
        {
            if (string.IsNullOrEmpty(tpl)) return string.Empty;

            string s = tpl;
            s = s.Replace("{minutes}", Mathf.FloorToInt((float)(ctx.sessionSeconds / 60.0)).ToString());
            s = s.Replace("{seconds}", Mathf.FloorToInt((float)(ctx.sessionSeconds)).ToString());

            s = s.Replace("{totalParts}", ctx.totalParts.ToString());
            s = s.Replace("{boilers}", ctx.boilers.ToString());
            s = s.Replace("{valves}", ctx.valves.ToString());
            s = s.Replace("{pumps}", ctx.pumps.ToString());
            s = s.Replace("{groupheads}", ctx.groupheads.ToString());
            s = s.Replace("{powerBlocks}", ctx.powerBlocks.ToString());

            s = s.Replace("{hasWaterRoute}", ctx.hasWaterRoute ? "yes" : "no");
            s = s.Replace("{hasPowerRoute}", ctx.hasPowerRoute ? "yes" : "no");

            s = s.Replace("{mode}", ctx.inPlaceMode ? "placement" : "inspection");
            return s;
        }

        void SpawnBubble(string text, Sprite portrait = null)
        {
            if (bubblePrefab == null)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] bubblePrefab is null.");
                return;
            }

            var parent = ResolveBubbleParent();
            if (parent == null)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] Could not resolve a bubble parent.");
                return;
            }

            if (!parent.gameObject.activeInHierarchy)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] Bubble parent was inactive; activating so chat bubble coroutines can run.");
                parent.gameObject.SetActive(true);
            }

            var ui = Instantiate(bubblePrefab, parent); // parent is a scene RectTransform now

            if (!ui.gameObject.activeSelf)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] bubblePrefab was inactive; activating spawned bubble.");
                ui.gameObject.SetActive(true);
            }

            if (!ui.gameObject.activeInHierarchy)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] Spawned bubble is still inactive in hierarchy; skipping Play().");
                return;
            }

            ui.SetText(text);
            ui.SetPortrait(portrait);
            ui.Play(bubbleLifetime);
        }

        Sprite PickCustomerSprite()
        {
            if (customerSprites == null || customerSprites.Count == 0)
                return null;

            if (cameraFocusController == null)
                return null;

            int idx = Random.Range(0, customerSprites.Count);
            return customerSprites[idx];
        }

        RectTransform ResolveBubbleParent()
        {
            if (_resolvedParent != null && _resolvedParent.gameObject != null && _resolvedParent.gameObject.scene.IsValid())
            {
                return _resolvedParent;
            }

            if (bubbleParent != null && bubbleParent.gameObject.scene.IsValid())
            {
                _resolvedParent = bubbleParent;
                return _resolvedParent;
            }

            // Prefer a Canvas in our own hierarchy.
            Canvas canvas = GetComponentInParent<Canvas>();
#if UNITY_2023_1_OR_NEWER
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
#else
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
#endif

            if (canvas == null)
            {
                // create a quick runtime canvas
                var go = new GameObject("RuntimeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var c = go.GetComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas = c;
                if (verbose) Debug.Log("[FlavorChatService] Created a RuntimeCanvas.");
            }

            if (canvas == null) return null;

            var br = canvas.transform.Find("BubbleRoot") as RectTransform;
            if (br == null)
            {
                var go = new GameObject("BubbleRoot", typeof(RectTransform), typeof(VerticalLayoutGroup));
                br = go.GetComponent<RectTransform>();
                br.SetParent(canvas.transform, false);
                br.anchorMin = new Vector2(1f, 1f); // top-right
                br.anchorMax = new Vector2(1f, 1f);
                br.pivot = new Vector2(1f, 1f);
                br.anchoredPosition = new Vector2(-1400f, 0f);
                br.sizeDelta = new Vector2(400f, 600f);

                var layout = go.GetComponent<VerticalLayoutGroup>();
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                layout.childAlignment = TextAnchor.UpperRight;
                layout.spacing = 8f;

                if (verbose) Debug.Log("[FlavorChatService] Created BubbleRoot under Canvas.");
            }

            _resolvedParent = br;
            if (verbose)
            {
                string parentName = br.parent != null ? br.parent.name : "<root>";
                Debug.Log($"[FlavorChatService] Bubble parent: {br.name} under {parentName}.");
            }
            return _resolvedParent;
        }
    }
}
