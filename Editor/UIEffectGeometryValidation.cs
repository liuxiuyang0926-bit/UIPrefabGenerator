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
        public static string RunGeometryRecovery()
        {
            var checks = new List<string>();
            ValidateGeometryRecovery(checks);
            return "PASS " + checks.Count + " geometry checks:\n" + string.Join("\n", checks);
        }

        private static void ValidateGeometryRecovery(List<string> checks)
        {
            var texture = new Texture2D(256, 128, TextureFormat.RGBA32, false);
            try
            {
                MethodInfo boundary = typeof(UIEffectResourceResolver).GetMethod("HasIndependentTextSurfaceBoundary",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var schema = new UIEffectSchema { designWidth = 256, designHeight = 128 };
                var rect = new Rect(48, 44, 160, 40);
                var textRects = new[] { new Rect(56, 46, 144, 36) };
                Func<bool> hasBoundary = () => (bool)boundary.Invoke(null, new object[] { texture, schema, rect, textRects });
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                        texture.SetPixel(x, y, new Color(.25f + x * .001f, .12f + y * .001f, .08f));
                texture.Apply();
                Require(!hasBoundary(), "continuous gradient behind text cannot create a new plate", checks);
                for (int y = 44; y < 84; y++)
                    for (int x = 48; x < 208; x++)
                        texture.SetPixel(x, texture.height - 1 - y, new Color(.12f, .23f, .38f));
                texture.Apply();
                Require(hasBoundary(), "real plate with narrow text padding retains independent edge evidence", checks);
                textRects = new[] { rect };
                Require(!hasBoundary(), "text-covered boundaries do not supply false surface evidence", checks);
                textRects = new[] { new Rect(56, 46, 144, 36) };
                for (int y = 0; y < texture.height; y++)
                    for (int x = 48; x < 208; x++)
                        texture.SetPixel(x, y, new Color(.12f, .23f, .38f));
                texture.Apply();
                Require(!hasBoundary(), "long ancestor edges cannot masquerade as a text plate", checks);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }

            ValidatePaddedGraphicRecovery(checks);
        }

        private static void ValidatePaddedGraphicRecovery(List<string> checks)
        {
            string folder = "Assets/Editor/UIEffectGeometryValidation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/Editor", Path.GetFileName(folder));
            var icon = new Texture2D(100, 100, TextureFormat.RGBA32, false);
            var reference = new Texture2D(800, 800, TextureFormat.RGBA32, false);
            try
            {
                // Distinct asymmetric opaque motif surrounded by alpha padding.
                // The input schema deliberately measures only its visible silhouette.
                for (int y = 0; y < 100; y++)
                    for (int x = 0; x < 100; x++)
                        icon.SetPixel(x, y, x >= 15 && x < 85 && y >= 18 && y < 82
                            ? (x > y ? new Color(.9f, .15f, .1f) : new Color(.95f, .8f, .1f))
                            : Color.clear);
                icon.Apply();
                string iconPath = folder + "/Padded.png";
                ImportValidationSprite(iconPath, icon.EncodeToPNG());
                var backdrop = new Color(.06f, .17f, .35f);
                for (int y = 0; y < 800; y++)
                    for (int x = 0; x < 800; x++)
                    {
                        Color pixel = x >= 70 && x < 170 && y >= 650 && y < 750
                            ? icon.GetPixel(x - 70, y - 650) : Color.clear;
                        reference.SetPixel(x, y, Color.Lerp(backdrop, pixel, pixel.a));
                    }
                reference.Apply();
                string referencePath = folder + "/Reference.png";
                File.WriteAllBytes(referencePath, reference.EncodeToPNG());
                var marker = new UIEffectNode { name = "Anchor", type = "Container", x = 20, y = 20, width = 5, height = 5 };
                var graphic = new UIEffectNode { name = "Emblem", type = "Image", semantic = "emblem",
                    x = 85, y = 68, width = 70, height = 64, preserveAspect = true,
                    children = new List<UIEffectNode> { marker } };
                var schema = new UIEffectSchema { name = "GeometryRecovery", designWidth = 800, designHeight = 800,
                    referenceImage = referencePath, children = new List<UIEffectNode> { graphic } };
                UIEffectResourceResolver.ClearCaches();
                var resolver = new UIEffectResourceResolver(new[] { folder }, UIEffectResourceMatchMode.VisualSimilarity);
                var before = resolver.AnalyzeCandidates(schema);
                Require(!before[0].Accepted, "cropped silhouette reproduces a rejected padded graphic", checks);
                resolver.MatchVisualResources(schema);
                Require(graphic.resource == iconPath && graphic.width > 90 && graphic.height > 90,
                    "padded graphic matches at recovered full-Sprite dimensions", checks);
                Require(Mathf.Abs(graphic.x + marker.x - 105) < .01f && Mathf.Abs(graphic.y + marker.y - 88) < .01f,
                    "graphic expansion preserves descendant absolute positions", checks);
                Require(Mathf.Abs(graphic.x + graphic.width / 2 - 120) < .01f &&
                    Mathf.Abs(graphic.y + graphic.height / 2 - 100) < .01f,
                    "graphic expansion preserves its original center", checks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(icon);
                UnityEngine.Object.DestroyImmediate(reference);
                AssetDatabase.DeleteAsset(folder);
                UIEffectResourceResolver.ClearCaches();
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
