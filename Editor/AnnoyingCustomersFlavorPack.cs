
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Espresso.EditorTools
{
    public static class AnnoyingCustomersFlavorPack
    {
        private const string RootFolder = "Assets/Flavor/AnnoyingCustomers";
        private const string BankPath = RootFolder + "/AnnoyingCustomers_FlavorBank.asset";

        [MenuItem("Espresso/Generate Annoying Customer Flavor Pack")]
        public static void Generate()
        {
            EnsureFolder(RootFolder);

            var bank = AssetDatabase.LoadAssetAtPath<FlavorBank>(BankPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<FlavorBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }
            bank.lines = new List<FlavorLine>();

            var entries = GetEntries();
            int index = 1;
            foreach (var e in entries)
            {
                string name = $"AC_{index:00}_{Sanitize(e.template)}";
                string path = $"{RootFolder}/{name}.asset";
                var line = AssetDatabase.LoadAssetAtPath<FlavorLine>(path);
                if (line == null)
                {
                    line = ScriptableObject.CreateInstance<FlavorLine>();
                    AssetDatabase.CreateAsset(line, path);
                }

                line.template             = e.template;
                line.tag                  = e.tag;
                line.weight               = e.weight;
                line.minCooldownSeconds   = e.cooldown;
                line.requireMode          = e.mode;
                line.minTotalParts        = e.minTotalParts;
                line.minBoilers           = e.minBoilers;
                line.requireWaterRouting  = e.reqWater;
                line.requirePowerRouting  = e.reqPower;
                line.oncePerSession       = e.once;

                EditorUtility.SetDirty(line);
                bank.lines.Add(line);
                index++;
            }

            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[AnnoyingCustomersFlavorPack] Generated {entries.Count} lines and bank at {BankPath}");
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Line";
            string clean = s;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                clean = clean.Replace(c, '_');
            clean = clean.Replace('{', '(').Replace('}', ')');
            if (clean.Length > 40) clean = clean.Substring(0, 40);
            return clean;
        }

        private class Entry
        {
            public string template;
            public string tag;
            public int    weight;
            public float  cooldown;
            public FlavorModeReq mode;
            public int    minTotalParts;
            public int    minBoilers;
            public bool   reqWater;
            public bool   reqPower;
            public bool   once;
        }

        private static List<Entry> GetEntries()
        {
            Entry E(string t, string tag = "annoy", int w = 1, float cd = 360f,
                    FlavorModeReq m = FlavorModeReq.Any, int minParts = 0, int minBoil = 0,
                    bool reqW = false, bool reqP = false, bool once = false)
            {
                return new Entry {
                    template = t, tag = tag, weight = w, cooldown = cd, mode = m,
                    minTotalParts = minParts, minBoilers = minBoil, reqWater = reqW, reqPower = reqP, once = once
                };
            }

            var L = new List<Entry>
            {
                E("Excuse me, it's been {minutes} minutes—how much longer for my 'perfect' shot?", w:2, cd:420f, m:FlavorModeReq.Inspect),
                E("Is {mode} mode really necessary? I’m late for my brunch.", cd:300f),
                E("I saw online you should have at least {boilers} boilers. You only have {boilers}? Hmm.", cd:540f, minBoil:1),
                E("Do your water routes even work? It says: {hasWaterRoute}. That’s… concerning.", cd:480f, reqW:true),
                E("And the power? {hasPowerRoute}? I brought a laptop, you know.", cd:480f, reqP:true),
                E("I counted {totalParts} parts. My cousin’s machine needed fewer. Just saying.", cd:360f, minParts:1),
                E("Why is the grouphead not shining? How many do you have? {groupheads}. Huh.", cd:360f),
                E("I demand single-origin water. Is that a thing? Make it a thing.", cd:600f),
                E("If you rotate that part one more time ({mode}), I will leave a review.", cd:360f),
                E("This boiler better reach 93°C. {boilers} boilers and still waiting.", cd:600f, minBoil:1),
                E("I want less crema but also more crema. Fix it.", cd:720f),
                E("If I don’t see pipes, how do I know water is real? {hasWaterRoute}.", cd:600f, reqW:true),
                E("Look, I’m an expert—I watch videos. Where’s your {powerBlocks} power block(s)?", cd:540f),
                E("Can you hurry? I’ve been here {seconds} seconds. That’s like an hour.", cd:300f),
                E("Valves? {valves}. My palate demands at least three.", cd:480f),
                E("This is a vibe, but my vibe needs {pumps} pumps minimum.", cd:480f),
                E("Are you in {mode} again? My aesthetic prefers 'done'.", w:2, cd:360f),
                E("I brought my own tamper. Don’t worry, it’s artisanal and hexagonal.", cd:900f),
                E("If the water isn’t volcanic, I won’t drink it.", cd:900f),
                E("Is this ethically sourced electricity? {hasPowerRoute}.", cd:600f, reqP:true),
                E("I know a guy who can do it cheaper with {totalParts} fewer parts.", cd:600f, minParts:2),
                E("What if we remove the grouphead ‘for clarity’? {groupheads} is cluttered.", cd:540f),
                E("I need Oatly pressure profiles. You do {mode}, I’ll manifest results.", cd:480f),
                E("Seen your boiler—mine is vintage. Yours is {boilers}.", cd:540f, minBoil:1),
                E("I’ll tip once crema aligns with Mars. How long now? {minutes} minutes.", cd:840f),
                E("Water routes say {hasWaterRoute}. So it’s artisanally… wet?", cd:540f, reqW:true),
                E("If you could make it bitter but also sweet, thanks.", cd:720f),
                E("Can you set it to 'Grandma’s Kitchen' mode? {mode} is fine I guess.", cd:540f),
                E("I brought a refractometer and a vibe check. Proceed.", cd:900f),
                E("I’ll be back after I write 3,000 words about this experience.", cd:900f, once:true),
            };

            return L;
        }
    }
}
#endif
