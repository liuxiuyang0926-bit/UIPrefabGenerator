#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    internal static partial class UIEffectFidelityValidation
    {
        public static string RunResourceMatching()
        {
            var checks = new List<string>();
            ValidateResourceMatching(checks);
            return "PASS " + checks.Count + " resource checks:\n" + string.Join("\n", checks);
        }

        private static void ValidateResourceMatching(List<string> checks)
        {
            string folder = "Assets/Editor/UIEffectResourceValidation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/Editor", Path.GetFileName(folder));
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Texture2D atlas = null;
            try
            {
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        texture.SetPixel(x, y, x > y ? new Color(.9f, .1f, .1f) : new Color(.9f, .8f, .1f));
                texture.Apply();
                byte[] png = texture.EncodeToPNG();
                string a = folder + "/A.png", b = folder + "/B.png";
                ImportValidationSprite(a, png);
                ImportValidationSprite(b, png);
                string reference = folder + "/Reference.png";
                File.WriteAllBytes(reference, png);
                var schema = new UIEffectSchema
                {
                    name = "ResourceValidation", designWidth = 64, designHeight = 64, referenceImage = reference,
                    children = new List<UIEffectNode>
                    {
                        new UIEffectNode { name = "Icon", type = "Image", semantic = "icon", width = 64, height = 64 },
                    },
                };
                UIEffectResourceResolver.ClearCaches();
                var resolver = new UIEffectResourceResolver(new[] { folder }, UIEffectResourceMatchMode.VisualSimilarity);
                var evidence = resolver.AnalyzeCandidates(schema);
                Require(evidence[0].Accepted && evidence[0].Candidates.Count == 1 && evidence[0].Candidates[0].Resource == a,
                    "identical imported files form one stable visual choice instead of a zero-margin rejection", checks);
                resolver.MatchVisualResources(schema);
                Require(schema.children[0].resource == a && resolver.Resolve(schema.children[0]) != null,
                    "duplicate Sprite match reaches the component assignment resolver", checks);

                var node = schema.children[0];
                node.resource = folder + "/Deleted.png";
                node.resourcePolicy = "Verified";
                node.intentionalColor = true;
                resolver.MatchVisualResources(schema);
                Require(node.resource == a && !node.intentionalColor && node.resourcePolicy == "Auto",
                    "missing cross-project Verified path is released and matched against current resources", checks);
                node.resource = b;
                node.resourcePolicy = "Verified";
                node.intentionalColor = true;
                resolver.MatchVisualResources(schema);
                Require(node.resource == b && !node.intentionalColor,
                    "existing reviewed Sprite survives duplicate selection and overrides a stale color flag", checks);
                ValidateResourcePrefab(schema, folder, a, checks);
                node.resourcePolicy = "ColorFallback";
                resolver.MatchVisualResources(schema);
                Require(string.IsNullOrEmpty(node.resource) && node.intentionalColor,
                    "explicit ColorFallback retains its intended pure-color behavior", checks);

                // A tiny difference may disappear in the 24x24 descriptor. Texture
                // identity must still keep it as a separate, genuinely ambiguous choice.
                texture.SetPixel(2, 30, Color.blue);
                texture.Apply();
                ImportValidationSprite(b, texture.EncodeToPNG());
                UIEffectResourceResolver.ClearCaches();
                node.resourcePolicy = "Auto";
                node.intentionalColor = false;
                resolver = new UIEffectResourceResolver(new[] { folder }, UIEffectResourceMatchMode.VisualSimilarity);
                evidence = resolver.AnalyzeCandidates(schema);
                Require(evidence[0].Candidates.Count == 2 && !evidence[0].Accepted,
                    "different source pixels cannot be merged just because sampled previews look alike", checks);

                // Same texture content with different slicing is also a distinct choice.
                ImportValidationSprite(b, png);
                var importer = (TextureImporter)AssetImporter.GetAtPath(b);
                importer.spriteBorder = new Vector4(8, 8, 8, 8);
                importer.SaveAndReimport();
                UIEffectResourceResolver.ClearCaches();
                resolver = new UIEffectResourceResolver(new[] { folder }, UIEffectResourceMatchMode.VisualSimilarity);
                Require(resolver.AnalyzeCandidates(schema)[0].Candidates.Count == 2,
                    "different Sprite borders are never collapsed as duplicates", checks);

                string atlasFolder = folder + "/SubSprites";
                AssetDatabase.CreateFolder(folder, "SubSprites");
                string atlasPath = atlasFolder + "/Atlas.asset";
                atlas = new Texture2D(128, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 128; x++)
                        atlas.SetPixel(x, y, x < 64 ? (x > y ? new Color(.9f, .1f, .1f) :
                            new Color(.9f, .8f, .1f)) : Color.blue);
                atlas.Apply();
                AssetDatabase.CreateAsset(atlas, atlasPath);
                var wrong = Sprite.Create(atlas, new Rect(64, 0, 64, 64), new Vector2(.5f, .5f));
                wrong.name = "AFirst";
                AssetDatabase.AddObjectToAsset(wrong, atlas);
                var expected = Sprite.Create(atlas, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f));
                expected.name = "Atlas";
                AssetDatabase.AddObjectToAsset(expected, atlas);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
                UIEffectResourceResolver.ClearCaches();
                resolver = new UIEffectResourceResolver(new[] { atlasFolder }, UIEffectResourceMatchMode.VisualSimilarity);
                node.resource = string.Empty;
                resolver.MatchVisualResources(schema);
                Require(node.resource == atlasPath + "#Atlas" && resolver.Resolve(node).name == "Atlas",
                    "sub-Sprite named like its file retains an unambiguous selector through assignment", checks);
                Require(resolver.UsedResources.Single() == atlasPath + "#Atlas",
                    "resource reporting does not append the sub-Sprite selector twice", checks);
                Require(resolver.Resolve(new UIEffectNode { resource = atlasPath + "#Missing" }) == null,
                    "missing sub-Sprite never falls back to the first atlas entry", checks);
                MethodInfo isPath = typeof(UIEffectResourceResolver).GetMethod("IsAssetResourcePath",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Require((bool)isPath.Invoke(null, new object[] { "Packages/com.example.ui/Atlas.png#Icon" }),
                    "embedded and installed package resource paths are recognized", checks);

                string empty = folder + "/Empty";
                AssetDatabase.CreateFolder(folder, "Empty");
                resolver = new UIEffectResourceResolver(new[] { empty }, UIEffectResourceMatchMode.VisualSimilarity);
                Require(resolver.ResourceWarnings.Count == 1 && resolver.ResourceWarnings[0].Contains(empty) &&
                    resolver.ResourceWarnings[0].Contains("Sprite (2D and UI)"),
                    "empty resource index reports selected roots and required Sprite import type", checks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                if (atlas != null && !AssetDatabase.Contains(atlas)) UnityEngine.Object.DestroyImmediate(atlas);
                AssetDatabase.DeleteAsset(folder);
                UIEffectResourceResolver.ClearCaches();
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ValidateResourcePrefab(UIEffectSchema source, string folder, string expectedPath,
            List<string> checks)
        {
            IUIEffectProjectAdapter previous = UIEffectProjectAdapterRegistry.Active;
            FieldInfo priorityField = typeof(UIEffectProjectAdapterRegistry).GetField("registeredPriority",
                BindingFlags.NonPublic | BindingFlags.Static);
            int priority = (int)priorityField.GetValue(null);
            var adapter = new GenericUGUIProjectAdapter();
            try
            {
                UIEffectProjectAdapterRegistry.Register(adapter, int.MaxValue);
                var schema = UIEffectSchemaUtility.Parse(UIEffectSchemaUtility.ToCompactJson(source));
                schema.name = Path.GetFileName(folder);
                schema.children[0].resource = folder + "/Deleted.png";
                schema.children[0].intentionalColor = true;
                var result = UIEffectPrefabBuilder.Generate(UIEffectSchemaUtility.ToCompactJson(schema),
                    new UIEffectPrefabGenerationOptions
                    {
                        panelId = schema.name, prefabFolder = folder,
                        resourceMatchMode = UIEffectResourceMatchMode.VisualSimilarity,
                        resourceSearchRoots = new[] { folder },
                    }, false);
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
                var image = saved.transform.Find("Generated").GetComponentInChildren<UnityEngine.UI.Image>(true);
                Require(image.sprite != null && AssetDatabase.GetAssetPath(image.sprite) == expectedPath &&
                    image.color.a > .99f && result.MissingResources.Count == 0,
                    "saved generic Prefab displays the matched duplicate Sprite after stale-path recovery", checks);
                var report = JsonUtility.FromJson<UIEffectFidelityReport>(File.ReadAllText(result.FidelityReportPath));
                Require(result.Warnings.Any(w => w.Contains("Deleted.png")) &&
                    report.warnings.Any(w => w.Contains("Deleted.png")),
                    "resource recovery diagnostics reach both generation result and persistent report", checks);
                Require(schema.children[0].resource.EndsWith("/Deleted.png", StringComparison.Ordinal),
                    "resource recovery preserves the caller's source Schema", checks);
            }
            finally
            {
                UIEffectProjectAdapterRegistry.Unregister(adapter);
                UIEffectProjectAdapterRegistry.Register(previous, priority);
            }
        }

        private static void ImportValidationSprite(string path, byte[] png)
        {
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
#endif
