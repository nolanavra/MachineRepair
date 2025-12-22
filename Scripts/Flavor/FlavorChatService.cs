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

        [Header("Customers")]
        [Tooltip("Authored customer pool that will be selected when emitting lines. If empty, portraitProfiles will seed defaults at runtime.")]
        public List<CustomerInstance> customers = new();
        [Header("UI Spawn")]
        public ChatBubbleUI bubblePrefab;
        [Tooltip("ChatBubbleContainer RectTransform under the front-view UI; falls back to the nearest Canvas if null.")]
        public RectTransform bubbleParent;
        [Min(1f)] public float bubbleLifetime = 6f;
        [SerializeField, Min(0f), Tooltip("Fallback delay for flavor line segments when no per-line value is supplied.")] private float defaultSegmentDelaySeconds = 1.5f;
        [SerializeField, Min(0f), Tooltip("Extra time appended after the final segment before fading out.")] private float segmentTailPaddingSeconds = 0.5f;

        [Header("Portrait Spawn")]
        [Tooltip("Prefab with a SpriteRenderer used to show the speaking customer's portrait in world space.")]
        [SerializeField] private SpriteRenderer portraitPrefab;
        [SerializeField] private Vector2 portraitOffsetFromSubGrid = new Vector2(0.5f, 0.5f);

        [Header("Customer Visuals")]
        [Tooltip("Legacy portrait definitions; used to seed customers when the dedicated list is empty.")]
        public List<CustomerPortraitProfile> portraitProfiles = new();
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
        private CustomerInstance _lastSpeaker;

        void Awake()
        {
            contextSource = contextSourceBehaviour as IFlavorContextSource;
            if (verbose && contextSource == null)
                Debug.LogWarning("[FlavorChatService] contextSourceBehaviour is null or does not implement IFlavorContextSource.");

            EnsureCustomers();
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
            EnsureCustomers();

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
                {
                    var debugSegments = new List<ChatBubbleSegment>
                    {
                        new ChatBubbleSegment("[debug] Forced emit: no candidates matched.", 0f)
                    };
                    SpawnBubble(debugSegments, 0f, bubbleLifetime, _lastSpeaker);
                }
                return;
            }

            var chosenSpeaker = PickCustomer(candidates);
            FlavorLine chosen = PickLineForCustomer(candidates, chosenSpeaker);
            if (chosen == null)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] Weighted pick failed.");
                return;
            }

            float defaultDelay = ResolveDefaultDelay(chosen);
            bool usedAuthoredSegments;
            var segments = BuildSegments(chosen, ctx, defaultDelay, out usedAuthoredSegments);
            float lifetime = ComputeLifetime(segments, usedAuthoredSegments);
            float playbackDefaultDelay = usedAuthoredSegments ? defaultDelay : 0f;
            SpawnBubble(segments, playbackDefaultDelay, lifetime, chosenSpeaker);

            _nextAllowed[chosen] = now + chosen.minCooldownSeconds;
            if (chosen.oncePerSession) _shownThisSession.Add(chosen);
            _lastSpeaker = chosenSpeaker;

            if (verbose)
            {
                string preview = (segments != null && segments.Count > 0) ? segments[0].Text : "<no text>";
                Debug.Log($"[FlavorChatService] Emitted: {preview} (segments={segments?.Count ?? 0}, lifetime={lifetime:0.00}s)");
            }
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

        List<ChatBubbleSegment> BuildSegments(FlavorLine line, FlavorContext ctx, float defaultDelaySeconds, out bool usedAuthoredSegments)
        {
            var segments = new List<ChatBubbleSegment>();
            usedAuthoredSegments = line != null && line.segments != null && line.segments.Count > 0;

            if (usedAuthoredSegments)
            {
                foreach (var segment in line.segments)
                {
                    if (segment == null) continue;

                    string text = FillTemplate(segment.text, ctx);
                    float delay = ResolveDelay(segment.delaySeconds, defaultDelaySeconds);
                    segments.Add(new ChatBubbleSegment(text, delay));
                }
            }

            if (segments.Count == 0)
            {
                string text = FillTemplate(line.template, ctx);
                segments.Add(new ChatBubbleSegment(text, 0f));
                usedAuthoredSegments = false;
            }

            return segments;
        }

        float ComputeLifetime(IReadOnlyList<ChatBubbleSegment> segments, bool usedAuthoredSegments)
        {
            if (!usedAuthoredSegments || segments == null || segments.Count == 0)
            {
                return Mathf.Max(0f, bubbleLifetime);
            }

            float total = 0f;
            for (int i = 0; i < segments.Count; i++)
            {
                total += ResolveDelay(segments[i].DelaySeconds, defaultSegmentDelaySeconds);
            }

            // Allow the final line to hang briefly before fading.
            float lifetime = total + segmentTailPaddingSeconds;
            return Mathf.Max(0.01f, lifetime);
        }

        float ResolveDefaultDelay(FlavorLine line)
        {
            if (line != null && line.defaultSegmentDelaySeconds > 0f)
                return line.defaultSegmentDelaySeconds;
            return defaultSegmentDelaySeconds;
        }

        float ResolveDelay(float segmentDelay, float defaultDelay)
        {
            if (segmentDelay > 0f) return segmentDelay;
            if (defaultDelay > 0f) return defaultDelay;
            return defaultSegmentDelaySeconds;
        }

        void SpawnBubble(IReadOnlyList<ChatBubbleSegment> segments, float defaultDelaySeconds, float lifetimeSeconds, CustomerInstance speaker = null)
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

            ui.SetPortrait(null); // portrait now lives in world space
            ui.PlaySequence(segments, defaultDelaySeconds, Mathf.Max(0f, lifetimeSeconds));

            SpawnWorldPortrait(speaker?.Portrait, Mathf.Max(0f, lifetimeSeconds));
        }

        CustomerInstance PickCustomer(List<(FlavorLine line, int weight)> candidates)
        {
            if (customers == null || customers.Count == 0)
                return null;

            var pool = new List<(CustomerInstance customer, int weight)>();
            foreach (var customer in customers)
            {
                if (customer == null) continue;

                int overlap = 0;
                foreach (var candidate in candidates)
                {
                    overlap += ScoreTagOverlap(customer, candidate.line);
                }

                int moodWeight = Mathf.Max(1, Mathf.RoundToInt(1f + customer.MoodFactor));
                int finalWeight = Mathf.Max(1, overlap + 1) * moodWeight;
                pool.Add((customer, finalWeight));
            }

            if (pool.Count == 0)
                return null;

            int sum = 0;
            foreach (var entry in pool) sum += entry.weight;

            int pick = Random.Range(0, sum);
            foreach (var entry in pool)
            {
                if (pick < entry.weight)
                    return entry.customer;
                pick -= entry.weight;
            }

            return pool[0].customer; // fallback
        }

        FlavorLine PickLineForCustomer(List<(FlavorLine line, int weight)> candidates, CustomerInstance speaker)
        {
            if (candidates == null || candidates.Count == 0) return null;

            var weighted = new List<(FlavorLine line, int weight)>();
            int sum = 0;
            foreach (var c in candidates)
            {
                int adjusted = c.weight;
                if (speaker != null)
                {
                    int tagScore = ScoreTagOverlap(speaker, c.line);
                    float moodFactor = speaker.MoodFactor;
                    adjusted = Mathf.Max(1, Mathf.RoundToInt(adjusted * (1f + 0.5f * tagScore) * moodFactor));
                }
                weighted.Add((c.line, adjusted));
                sum += adjusted;
            }

            int pick = Random.Range(0, sum);
            foreach (var c in weighted)
            {
                if (pick < c.weight) return c.line;
                pick -= c.weight;
            }

            return weighted[0].line;
        }

        int ScoreTagOverlap(CustomerInstance customer, FlavorLine line)
        {
            if (customer == null || line == null || line.tags == null) return 0;

            int score = 0;
            foreach (var t in line.tags)
            {
                if (t == null) continue;
                if (customer.HasTag(t.tag))
                    score += Mathf.Max(1, t.weight);
            }
            return score;
        }

        void EnsureCustomers()
        {
            if (customers == null)
                customers = new List<CustomerInstance>();

            if (customers.Count == 0 && portraitProfiles != null && portraitProfiles.Count > 0)
            {
                foreach (var profile in portraitProfiles)
                {
                    if (profile == null || profile.sprite == null) continue;
                    var tags = profile.tags?.Where(t => t != null).Select(t => t.tag) ?? Enumerable.Empty<FlavorContextTag>();
                    var instance = new CustomerInstance();
                    instance.SetPortrait(profile.sprite);
                    instance.SetTags(tags);
                    instance.SetMood(70);
                    customers.Add(instance);
                }
            }

            foreach (var customer in customers)
            {
                customer?.ClampMood();
            }
        }

        public CustomerInstance GetLastSpeaker() => _lastSpeaker;

        public void AdjustMoodForTags(IEnumerable<FlavorContextTag> tags, int delta)
        {
            if (customers == null || tags == null) return;
            var tagSet = new HashSet<FlavorContextTag>(tags);
            foreach (var customer in customers)
            {
                if (customer == null) continue;
                if (customer.Tags.Any(tagSet.Contains))
                    customer.AdjustMood(delta);
            }
        }

        public void AdjustMoodForCustomer(CustomerInstance customer, int delta)
        {
            if (customer == null || customers == null) return;
            if (!customers.Contains(customer)) return;
            customer.AdjustMood(delta);
        }

        public void AdjustMoodForLastSpeaker(int delta)
        {
            if (_lastSpeaker == null) return;
            AdjustMoodForCustomer(_lastSpeaker, delta);
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

        void SpawnWorldPortrait(Sprite portrait, float lifetimeSeconds)
        {
            if (portrait == null)
                return;

            if (portraitPrefab == null)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] portraitPrefab is null; skipping portrait spawn.");
                return;
            }

            if (cameraFocusController == null || cameraFocusController.SubGridCenter == null)
            {
                if (verbose) Debug.LogWarning("[FlavorChatService] Missing cameraFocusController or sub grid center; skipping portrait spawn.");
                return;
            }

            var anchor = cameraFocusController.SubGridCenter.position;
            var worldPos = anchor + new Vector3(portraitOffsetFromSubGrid.x, portraitOffsetFromSubGrid.y, 0f);
            var instance = Instantiate(portraitPrefab, worldPos, Quaternion.identity);
            instance.sprite = portrait;

            StartCoroutine(DestroyPortraitAfter(instance.gameObject, Mathf.Max(0f, lifetimeSeconds)));
        }

        IEnumerator DestroyPortraitAfter(GameObject go, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (go != null)
                Destroy(go);
        }
    }
}
