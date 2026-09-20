#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    // Explicit editor regression runner; no NUnit dependency in the portable package.
    internal static partial class UIEffectFidelityValidation
    {
        [MenuItem("工具/UI工具/验证效果图还原流水线")]
        public static void RunMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            string id = Guid.NewGuid().ToString("N");
            string folder = "Assets/Editor/UIEffectValidation_" + id;
            AssetDatabase.CreateFolder("Assets/Editor", "UIEffectValidation_" + id);
            var checks = new List<string>();
            var atlas = new Texture2D(16, 16 * 120, TextureFormat.RGBA32, false);
            Texture2D rendered = null;
            IUIEffectProjectAdapter previous = UIEffectProjectAdapterRegistry.Active;
            FieldInfo priorityField = typeof(UIEffectProjectAdapterRegistry).GetField("registeredPriority",
                BindingFlags.NonPublic | BindingFlags.Static);
            int priority = (int)priorityField.GetValue(null);
            var adapter = new GenericUGUIProjectAdapter();
            try
            {
                for (int y = 0; y < atlas.height; y++)
                    for (int x = 0; x < atlas.width; x++)
                        atlas.SetPixel(x, y, y / 16 == 110
                            ? (x > y % 16 ? new Color(.9f,.1f,.1f) : new Color(.9f,.8f,.1f))
                            : new Color(.05f, .4f + (y / 16 % 8) * .04f, .7f));
                atlas.Apply();
                string atlasPath = folder + "/Atlas.asset";
                AssetDatabase.CreateAsset(atlas, atlasPath);
                Sprite expectedSprite = null;
                for (int i = 0; i < 120; i++)
                {
                    Sprite sprite = Sprite.Create(atlas, new Rect(0, i * 16, 16, 16), new Vector2(.5f,.5f), 100);
                    sprite.name = "S" + i.ToString("000");
                    AssetDatabase.AddObjectToAsset(sprite, atlas);
                    if (i == 110) expectedSprite = sprite;
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
                expectedSprite = AssetDatabase.LoadAllAssetsAtPath(atlasPath).OfType<Sprite>().Single(item => item.name == "S110");
                using (var renderer = new UIEffectPreviewRenderer(64, 64, 64))
                    rendered = renderer.RenderSprite(expectedSprite, Color.white, true);
                Require(rendered.GetPixel(48, 32).a > .98f && rendered.GetPixel(48, 32).r > .5f,
                    "isolated UGUI preview renders actual pixels", checks);
                string referencePath = folder + "/Reference.png";
                File.WriteAllBytes(UIEffectEditorUtility.ToAbsolutePath(referencePath), rendered.EncodeToPNG());
                AssetDatabase.ImportAsset(referencePath, ImportAssetOptions.ForceSynchronousImport);
                var schema = new UIEffectSchema { name = "UIFidelityValidation_" + id, designWidth = 64,
                    designHeight = 64, referenceImage = referencePath, children = new List<UIEffectNode>
                    { new UIEffectNode { name = "Icon", type = "Image", semantic = "icon", width = 64, height = 64 } } };

                var resolver = new UIEffectResourceResolver(new[] { folder }, UIEffectResourceMatchMode.VisualSimilarity);
                List<UIEffectNodeEvidence> evidence = resolver.AnalyzeCandidates(schema);
                Require(evidence.Count == 1 && evidence[0].Candidates.Count > 0 &&
                    evidence[0].Candidates[0].Resource == atlasPath + "#S110",
                    "per-node full-library recall finds Sprite beyond first 100", checks);
                Require(string.IsNullOrEmpty(schema.children[0].resource), "candidate analysis preserves input schema", checks);
                ValidateSearchCache(resolver, schema, evidence, referencePath, checks);
                ValidateParallelScoring(resolver, schema, checks);
                ValidateReadback(checks);
                string evidenceFolder = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../Library/LxyUIEffectGenerator/Validation/Evidence"));
                Directory.CreateDirectory(evidenceFolder);
                typeof(UIEffectPrefabGeneratorWindow).GetMethod("CreateNodeCatalog", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { schema, evidence, evidenceFolder });
                Require(File.Exists(Path.Combine(evidenceFolder, schema.name + "_NodeEvidence_01.png")),
                    "node contact sheets include reference and rendered candidate evidence", checks);

                var rejected = new UIEffectNode { resourceCandidates = new List<string> { atlasPath + "#S000" } };
                Require(resolver.Resolve(rejected) == null, "rejected candidate cannot bypass visual validation", checks);
                UIEffectNode node = schema.children[0];
                node.resource = atlasPath + "#S000";
                node.resourcePolicy = "Verified";
                UIEffectPrefabGeneratorWindow.MergeNodeEvidence(schema, evidence);
                Require(node.resourcePolicy == "Candidate" && string.IsNullOrEmpty(node.resource) &&
                    node.resourceCandidates.Contains(atlasPath + "#S110"),
                    "incorrect AI Verified path downgraded and full-library evidence retained", checks);
                resolver.MatchVisualResources(schema);
                Require(node.resource == atlasPath + "#S110", "incorrect first AI candidate loses to stronger pixels", checks);
                UIEffectNodeEvidence proof = evidence[0];
                proof.RequiresReview = false;
                proof.Accepted = true;
                proof.ResourceRevision = UIEffectResourceResolver.ResourceRevision;
                node.resourcePolicy = "Verified";
                UIEffectPrefabGeneratorWindow.MergeNodeEvidence(schema, evidence);
                Require(node.resourcePolicy == "Verified", "independent current evidence permits an exact lock", checks);
                UIEffectResourceResolver.ClearCaches();
                UIEffectPrefabGeneratorWindow.MergeNodeEvidence(schema, evidence);
                Require(node.resourcePolicy == "Candidate" && string.IsNullOrEmpty(node.resource),
                    "changed resource cache invalidates previous Verified evidence", checks);
                resolver.MatchVisualResources(schema);

                var typography = new UIEffectSchema { children = new List<UIEffectNode>
                    { new UIEffectNode { name = "Label", type = "Text", text = "AB", noWrap = true,
                        lineSpacing = 2.5f, font = "Fonts/Body.asset", fontMaterial = "Fonts/Body.mat",
                        resourcePolicy = "Candidate", repeatCount = 2, repeatOffsetY = 30 } } };
                UIEffectSchema roundtrip = UIEffectSchemaUtility.ExpandRepeats(
                    UIEffectSchemaUtility.Parse(UIEffectSchemaUtility.ToCompactJson(typography)));
                Require(roundtrip.children.Count == 2 && roundtrip.children.All(item => item.noWrap &&
                    item.lineSpacing == 2.5f && item.font == "Fonts/Body.asset" && item.fontMaterial == "Fonts/Body.mat" &&
                    item.resourcePolicy == "Candidate"), "typography and policies survive compact JSON and repeat expansion", checks);

                UIEffectFidelityNodeReport identical = UIEffectFidelityAudit.CompareRegion(rendered, rendered, null,
                    new Rect(0,0,64,64), new List<Rect>(),64,64);
                Require(identical.samples > 100 && identical.colorError < .015f,
                    "audit aligns source pixel centers", checks);
                ValidateSlicedSampling(checks);
                ValidateResourceMatching(checks);
                ValidateGeometryRecovery(checks);
                ValidateRankedTemplates(checks);
                ValidateIndependentSurface(schema, folder, checks);

                UIEffectProjectAdapterRegistry.Register(adapter, int.MaxValue);
                ValidateTypography(folder, checks);
                var options = new UIEffectPrefabGenerationOptions { panelId = schema.name, prefabFolder = folder,
                    scriptType = UIEffectScriptType.None, resourceMatchMode = UIEffectResourceMatchMode.VisualSimilarity,
                    resourceSearchRoots = new[] { folder } };
                // Already resolved nodes represent an explicit reviewed schema.
                node.resourcePolicy = "Verified";
                node.children.Add(new UIEffectNode { name = "AlignmentLabel", type = "Text",
                    width = 20, height = 12, alignment = "MiddleLeft" });
                UIEffectPrefabGenerationResult generation = UIEffectPrefabBuilder.Generate(
                    UIEffectSchemaUtility.ToCompactJson(schema), options, false);
                Require(File.Exists(generation.FidelityReportPath), "saved Prefab generates actual preview and audit report", checks);
                Require(File.Exists(Path.Combine(Path.GetDirectoryName(generation.FidelityReportPath),"Index.html")),
                    "readable comparison report accompanies machine-readable audit",checks);
                UIEffectFidelityReport report = JsonUtility.FromJson<UIEffectFidelityReport>(File.ReadAllText(generation.FidelityReportPath));
                Require(report.nodes.Any(item => item.type == "Image" && item.samples > 100 && item.colorError < .025f),
                    "saved Prefab and reference agree in isolated rendering", checks);
                GameObject contents = PrefabUtility.LoadPrefabContents(generation.PrefabPath);
                try
                {
                    new GameObject("ManualSibling", typeof(RectTransform)).transform.SetParent(contents.transform, false);
                    PrefabUtility.SaveAsPrefabAsset(contents, generation.PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                UIEffectPrefabBuilder.Generate(UIEffectSchemaUtility.ToCompactJson(schema), options, false);
                GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(generation.PrefabPath);
                Require(saved.transform.Find("ManualSibling") != null, "regeneration preserves manual Prefab siblings", checks);
                Require(saved.GetComponentInChildren<TextMeshProUGUI>(true).alignment == TextAlignmentOptions.Left,
                    "MiddleLeft schema alignment builds a left-aligned TMP component", checks);

                string result = "PASS " + checks.Count + " checks:\n" + string.Join("\n", checks);
                string artifactFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/LxyUIEffectGenerator/Validation"));
                Directory.CreateDirectory(artifactFolder);
                File.WriteAllText(Path.Combine(artifactFolder, "Latest.txt"), result);
                File.Copy(report.preview, Path.Combine(artifactFolder, "Preview.png"), true);
                File.Copy(referencePath, Path.Combine(artifactFolder, "Reference.png"), true);
                return result;
            }
            finally
            {
                UIEffectProjectAdapterRegistry.Unregister(adapter);
                UIEffectProjectAdapterRegistry.Register(previous, priority);
                if (rendered != null) UnityEngine.Object.DestroyImmediate(rendered);
                if (folder == "Assets/Editor/UIEffectValidation_" + id && id.Length == 32)
                    AssetDatabase.DeleteAsset(folder);
                UIEffectResourceResolver.ClearCaches();
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Require(bool condition, string description, List<string> checks)
        {
            if (!condition) throw new InvalidOperationException("Fidelity regression failed: " + description);
            checks.Add(description);
        }

        private static void ValidateSearchCache(UIEffectResourceResolver resolver, UIEffectSchema schema,
            List<UIEffectNodeEvidence> original, string referencePath, List<string> checks)
        {
            int hits = UIEffectResourceResolver.VisualSearchCacheHits;
            List<UIEffectNodeEvidence> repeated = resolver.AnalyzeCandidates(schema);
            Require(UIEffectResourceResolver.VisualSearchCacheHits > hits && SameEvidence(original, repeated),
                "identical search reuses all ranked candidates, exact scores and tints", checks);
            // Changing a returned list must not contaminate later cache hits.
            repeated[0].Candidates[0].Score = -1;
            Require(SameEvidence(original, resolver.AnalyzeCandidates(schema)),
                "callers cannot mutate cached ranked evidence", checks);

            byte[] reference = File.ReadAllBytes(referencePath);
            var changed = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            bool previous = UIEffectResourceResolver.EnableVisualSearchCache;
            try
            {
                Color[] pixels = Enumerable.Repeat(new Color(.05f, .9f, .2f), 64 * 64).ToArray();
                changed.SetPixels(pixels);
                changed.Apply();
                // Same path and dimensions, deliberately no import to exercise pixel-based invalidation.
                File.WriteAllBytes(referencePath, changed.EncodeToPNG());
                int misses = UIEffectResourceResolver.VisualSearchCacheMisses;
                List<UIEffectNodeEvidence> cached = resolver.AnalyzeCandidates(schema);
                Require(UIEffectResourceResolver.VisualSearchCacheMisses > misses,
                    "changed reference pixels cannot reuse an old search", checks);
                UIEffectResourceResolver.EnableVisualSearchCache = false;
                List<UIEffectNodeEvidence> uncached = resolver.AnalyzeCandidates(schema);
                Require(SameEvidence(cached, uncached), "changed-image cached and full searches agree exactly", checks);
            }
            finally
            {
                File.WriteAllBytes(referencePath, reference);
                UIEffectResourceResolver.EnableVisualSearchCache = previous;
                UnityEngine.Object.DestroyImmediate(changed);
            }

            var moved = UIEffectSchemaUtility.Parse(UIEffectSchemaUtility.ToCompactJson(schema));
            moved.children[0].width -= 3;
            var changedGeometry = resolver.AnalyzeCandidates(moved);
            try
            {
                UIEffectResourceResolver.EnableVisualSearchCache = false;
                Require(SameEvidence(changedGeometry, resolver.AnalyzeCandidates(moved)),
                    "changed geometry preserves full-search results", checks);
            }
            finally { UIEffectResourceResolver.EnableVisualSearchCache = previous; }
            UIEffectResourceResolver.ClearCaches();
            Require(UIEffectResourceResolver.VisualSearchCacheHits == 0 &&
                UIEffectResourceResolver.VisualSearchCacheMisses == 0,
                "resource invalidation clears complete-search cache", checks);
        }

        private static bool SameEvidence(List<UIEffectNodeEvidence> first, List<UIEffectNodeEvidence> second)
        {
            if (first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
            {
                var a = first[i]; var b = second[i];
                if (a.Path != b.Path || a.Rect != b.Rect || a.Accepted != b.Accepted ||
                    a.RequiresReview != b.RequiresReview || a.Candidates.Count != b.Candidates.Count) return false;
                for (int c = 0; c < a.Candidates.Count; c++)
                {
                    var x = a.Candidates[c]; var y = b.Candidates[c];
                    if (x.Resource != y.Resource || x.Score != y.Score || x.Tint != y.Tint ||
                        x.SourceSize != y.SourceSize || x.Border != y.Border) return false;
                }
            }
            return true;
        }

        private static void ValidateParallelScoring(UIEffectResourceResolver resolver,
            UIEffectSchema schema, List<string> checks)
        {
            bool parallel = UIEffectResourceResolver.EnableParallelScoring;
            bool cache = UIEffectResourceResolver.EnableVisualSearchCache;
            try
            {
                UIEffectResourceResolver.EnableVisualSearchCache = false;
                UIEffectResourceResolver.EnableParallelScoring = false;
                var sequential = resolver.AnalyzeCandidates(schema);
                UIEffectResourceResolver.EnableParallelScoring = true;
                for (int run = 0; run < 3; run++)
                    Require(SameEvidence(sequential, resolver.AnalyzeCandidates(schema)),
                        "parallel scoring preserves exact scores, order and tint: run " + (run + 1), checks);
            }
            finally
            {
                UIEffectResourceResolver.EnableParallelScoring = parallel;
                UIEffectResourceResolver.EnableVisualSearchCache = cache;
            }
        }

        private static void ValidateReadback(List<string> checks)
        {
            var texture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            try
            {
                var bytes = new Color32[256];
                for (int i = 0; i < 256; i++) bytes[i] = new Color32((byte)i, (byte)(255-i), (byte)(i^127), (byte)i);
                texture.SetPixels32(bytes);
                texture.Apply();
                Color[] floats = texture.GetPixels();
                Color32[] compact = texture.GetPixels32();
                Require(floats.Select((pixel, i) => pixel.Equals(UIEffectResourceResolver.DecodeNativeColor(compact[i]))).All(equal => equal),
                    "RGBA32 readback preserves every channel value exactly across all 256 values", checks);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void ValidateRankedTemplates(List<string> checks)
        {
            var schema = new UIEffectSchema { name = "RankTemplates", designWidth = 900, designHeight = 700 };
            string[] ranks = { "Second", "First", "Third" };
            for (int i = 0; i < ranks.Length; i++)
            {
                string prefix = ranks[i] + "Place";
                schema.children.Add(new UIEffectNode
                {
                    name = prefix + "Panel", type = "Container", x = i * 280, width = 260, height = 612,
                    children = new List<UIEffectNode>
                    {
                        new UIEffectNode { name = prefix + "Emblem", type = "Image", semantic = "emblem",
                            x = i == 1 ? 70 : 40, y = 50, width = i == 1 ? 110 : 180, height = 180 },
                        new UIEffectNode { name = prefix + "Score", type = "Text", x = 12, y = 240,
                            width = 200, height = 40, text = (100 + i).ToString() },
                    },
                });
            }
            string original = UIEffectSchemaUtility.ToCompactJson(schema);
            Require(UIEffectSchemaUtility.InferRuntimeTemplateGroups(schema) == 3,
                "ranked data templates allow different intrinsic emblem geometry", checks);
            var resolver = new UIEffectResourceResolver(Array.Empty<string>(), UIEffectResourceMatchMode.ColorBlocks);
            resolver.MatchVisualResources(schema);
            Require(schema.children.Count == 1 && schema.children[0].name == "SecondPlacePanel" &&
                schema.children[0].x == 0 && schema.children[0].children[0].width == 180,
                "template pruning keeps one original subtree and absolute geometry", checks);
            var layout = UIEffectSchemaUtility.Parse(original);
            for (int i = 0; i < layout.children.Count; i++)
                layout.children[i].name = new[] { "LeftGuildPanel", "CenterGuildPanel", "RightGuildPanel" }[i];
            Require(UIEffectSchemaUtility.InferRuntimeTemplateGroups(layout) == 0 && layout.children.Count == 3,
                "fixed left/center/right panels remain independent", checks);
            var different = UIEffectSchemaUtility.Parse(original);
            different.children[1].children[1].type = "Button";
            Require(UIEffectSchemaUtility.InferRuntimeTemplateGroups(different) == 0,
                "different ranked subtrees cannot be silently pruned", checks);
        }

        private static void ValidateIndependentSurface(UIEffectSchema source, string folder, List<string> checks)
        {
            var schema = UIEffectSchemaUtility.Parse(UIEffectSchemaUtility.ToCompactJson(source));
            var parent = schema.children[0];
            parent.name = "Backdrop";
            parent.semantic = "background";
            parent.resource = string.Empty;
            parent.resourcePolicy = "Auto";
            var field = new UIEffectNode { name = "ScoreField", type = "Image", semantic = "field",
                x = 12, y = 24, width = 40, height = 12,
                children = new List<UIEffectNode> { new UIEffectNode { name = "Value", type = "Text",
                    width = 40, height = 12, text = "123" } } };
            parent.children.Add(field);
            new UIEffectResourceResolver(new[] { folder }, UIEffectResourceMatchMode.VisualSimilarity)
                .MatchVisualResources(schema);
            Require(parent.children.Contains(field) && field.type == "Image" && field.children.Count == 1 &&
                field.x == 12 && field.y == 24,
                "an unresolved data field stays an Image even when the ancestor explains its background", checks);
        }

        private static void ValidateSlicedSampling(List<string> checks)
        {
            var texture = new Texture2D(32,32,TextureFormat.RGBA32,false);
            Sprite sprite = null;
            Texture2D actual = null;
            IDisposable descriptorRenderer = null;
            try
            {
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        texture.SetPixel(x,y, x < 6 || x >= 26 || y < 6 || y >= 26
                            ? new Color(.8f,.2f,.05f) : new Color(.1f,.3f,.8f));
                texture.Apply();
                sprite = Sprite.Create(texture,new Rect(0,0,32,32),new Vector2(.25f,.75f),200,0,
                    SpriteMeshType.FullRect,new Vector4(6,6,6,6));
                using(var preview = new UIEffectPreviewRenderer(120,48,240))
                    actual = preview.RenderSprite(sprite,Color.white,true);
                Type rendererType = typeof(UIEffectResourceResolver).GetNestedType("SpritePreviewRenderer",BindingFlags.NonPublic);
                descriptorRenderer = (IDisposable)Activator.CreateInstance(rendererType, new object[]{24});
                object descriptor = rendererType.GetMethod("Render").Invoke(descriptorRenderer,
                    new object[]{sprite,120f,48f,true,32f,32f});
                Color[] pixels = (Color[])descriptor.GetType().GetProperty("Pixels").GetValue(descriptor);
                MethodInfo coordinate = typeof(UIEffectResourceResolver).GetMethod("GetVisualSampleCoordinate",
                    BindingFlags.NonPublic | BindingFlags.Static);
                float error = 0;
                for(int y=0;y<24;y++)
                    for(int x=0;x<24;x++)
                    {
                        Color expected = actual.GetPixelBilinear((float)coordinate.Invoke(null,new object[]{x}),
                            (float)coordinate.Invoke(null,new object[]{y}));
                        Color sampled = pixels[y*24+x];
                        error += (Mathf.Abs(expected.r-sampled.r)+Mathf.Abs(expected.g-sampled.g)+Mathf.Abs(expected.b-sampled.b))/3;
                    }
                Require(error / pixels.Length < .045f,
                    "nine-slice scoring agrees with UGUI for non-default PPU and pivot",checks);
                int nativeHits = UIEffectResourceResolver.NativePixelCacheHits;
                object cachedSize = rendererType.GetMethod("Render").Invoke(descriptorRenderer,
                    new object[]{sprite,83f,37f,true,32f,32f});
                Require(UIEffectResourceResolver.NativePixelCacheHits > nativeHits,
                    "different nine-slice sizes reuse the identical native image", checks);
                bool nativeEnabled = UIEffectResourceResolver.EnableNativePixelCache;
                try
                {
                    UIEffectResourceResolver.EnableNativePixelCache = false;
                    object uncachedSize = rendererType.GetMethod("Render").Invoke(descriptorRenderer,
                        new object[]{sprite,83f,37f,true,32f,32f});
                    Color[] cachedPixels = (Color[])cachedSize.GetType().GetProperty("Pixels").GetValue(cachedSize);
                    Color[] directPixels = (Color[])uncachedSize.GetType().GetProperty("Pixels").GetValue(uncachedSize);
                    Require(cachedPixels.SequenceEqual(directPixels),
                        "native reuse preserves exact target-size sampling with custom PPU and pivot", checks);
                }
                finally { UIEffectResourceResolver.EnableNativePixelCache = nativeEnabled; }
                using(var preview = new UIEffectPreviewRenderer(32,32,32))
                {
                    UnityEngine.Object.DestroyImmediate(actual);
                    actual = preview.RenderSprite(sprite,new Color(1,1,1,.5f),false);
                    Color center = actual.GetPixel(16,16);
                    Require(Mathf.Abs(center.a-.5f)<.03f && center.b>.7f,
                        "transparent preview exports straight alpha without double darkening",checks);
                }
            }
            finally
            {
                descriptorRenderer?.Dispose();
                if(actual!=null)UnityEngine.Object.DestroyImmediate(actual);
                if(sprite!=null)UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
#endif
