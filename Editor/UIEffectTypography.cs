#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    // Resolve optional typography before creating or changing a Prefab. Never infer a
    // font by a similar filename, or persist a guessed path into the source Schema.
    internal static class UIEffectTypography
    {
        internal const string EssentialsMessage = "尚未导入 TextMeshPro 必需资源。请先执行 " +
            "Window > TextMeshPro > Import TMP Essential Resources，再生成 UI。";
        internal const string FontMessage = "当前项目没有可用的 TMP 字体。请导入 TMP 必需资源，" +
            "或在生成窗口的“默认 TMP 字体”中选择当前项目的 TMP_FontAsset。";

        internal static bool IsUsable(TMP_FontAsset font)
        {
            return font != null && font.atlasTextures != null && font.atlasTextures.Length > 0 &&
                font.atlasTextures[0] != null && IsCompatible(font.material, font);
        }

        internal static bool IsCompatible(Material material, TMP_FontAsset font)
        {
            return material != null && font != null && font.atlasTextures != null &&
                font.atlasTextures.Length > 0 && font.atlasTextures[0] != null &&
                material.HasProperty("_MainTex") && material.mainTexture == font.atlasTextures[0];
        }

        internal static string GetEnvironmentProblem(TMP_FontAsset selectedFont)
        {
            // Unlike TMP_Settings.instance, this does not open an importer window.
            if (TMP_Settings.LoadDefaultSettings() == null) return EssentialsMessage;
            if (selectedFont != null && (!IsUsable(selectedFont) || !AssetDatabase.Contains(selectedFont)))
                return "所选默认 TMP 字体的材质或图集不可用，请选择完整的 TMP_FontAsset 资源。";
            return IsUsable(selectedFont) || IsUsable(TMP_Settings.defaultFontAsset) ||
                FindProjectFont() != null ? null : FontMessage;
        }

        internal static List<string> Prepare(UIEffectSchema schema, TMP_FontAsset selectedFont)
        {
            var nodes = new List<KeyValuePair<string, UIEffectNode>>();
            Collect(schema.children, schema.name, nodes);
            var warnings = new List<string>();
            if (nodes.Count == 0) return warnings;
            if (TMP_Settings.LoadDefaultSettings() == null) throw new InvalidOperationException(EssentialsMessage);
            if (selectedFont != null && (!IsUsable(selectedFont) || !AssetDatabase.Contains(selectedFont)))
                throw new InvalidOperationException("所选默认 TMP 字体的材质或图集不可用，请重新选择字体。");

            var fonts = new Dictionary<string, TMP_FontAsset>(StringComparer.Ordinal);
            foreach (var item in nodes)
            {
                string path = NormalizePath(item.Value.font);
                if (!fonts.ContainsKey(path)) fonts[path] = LoadFont(path);
            }
            // A missing override commonly results from one misspelled AI path. Use
            // the most-used, actually loaded font in this Schema before a generic
            // project default (which might only contain Latin glyphs).
            TMP_FontAsset schemaFont = nodes.Select(item => fonts[NormalizePath(item.Value.font)])
                .Where(IsUsable).GroupBy(font => font)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => AssetDatabase.GetAssetPath(group.Key), StringComparer.Ordinal)
                .Select(group => group.Key).FirstOrDefault();
            TMP_FontAsset projectDefault = TMP_Settings.defaultFontAsset;
            if (!IsUsable(projectDefault)) projectDefault = null;
            TMP_FontAsset ordinaryDefault = selectedFont != null ? selectedFont : projectDefault;
            ordinaryDefault = ordinaryDefault != null ? ordinaryDefault : schemaFont;
            if (ordinaryDefault == null) ordinaryDefault = FindProjectFont();
            if (ordinaryDefault == null) throw new InvalidOperationException(FontMessage);
            TMP_FontAsset missingOverrideDefault = selectedFont != null ? selectedFont : schemaFont;
            missingOverrideDefault = missingOverrideDefault != null ? missingOverrideDefault : ordinaryDefault;

            foreach (var item in nodes)
            {
                UIEffectNode node = item.Value;
                string requested = NormalizePath(node.font);
                TMP_FontAsset resolved = fonts[requested];
                if (!IsUsable(resolved))
                {
                    resolved = requested.Length == 0 ? ordinaryDefault : missingOverrideDefault;
                    if (requested.Length > 0)
                        warnings.Add(item.Key + "：TMP 字体不存在或图集/材质无效：" + requested +
                            "；已改用 " + AssetDatabase.GetAssetPath(resolved) + "，请复核文字外观。");
                }
                node.font = AssetDatabase.GetAssetPath(resolved);
                string materialPath = NormalizePath(node.fontMaterial);
                if (materialPath.Length > 0)
                {
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (!IsCompatible(material, resolved))
                    {
                        warnings.Add(item.Key + "：字体材质不存在或与实际字体图集不兼容：" +
                            materialPath + "；已使用实际字体的默认材质。");
                        materialPath = string.Empty;
                    }
                }
                node.fontMaterial = materialPath;
            }
            return warnings;
        }

        private static TMP_FontAsset LoadFont(string path)
        {
            return path.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        internal static void RemoveUnevidencedAiFonts(List<UIEffectNode> nodes, List<string> repairs)
        {
            foreach (UIEffectNode node in nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.font) || !string.IsNullOrWhiteSpace(node.fontMaterial))
                {
                    repairs.Add(node.name + "：AI 未获得字体资源清单，已移除其字体/材质路径，使用当前项目字体。");
                    node.font = string.Empty;
                    node.fontMaterial = string.Empty;
                }
                RemoveUnevidencedAiFonts(node.children, repairs);
            }
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }

        private static TMP_FontAsset FindProjectFont()
        {
            return AssetDatabase.FindAssets("t:TMP_FontAsset").Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path.StartsWith("Assets/", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>).FirstOrDefault(IsUsable);
        }

        private static void Collect(List<UIEffectNode> nodes, string parent,
            List<KeyValuePair<string, UIEffectNode>> result)
        {
            foreach (UIEffectNode node in nodes)
            {
                string path = parent + "/" + node.name;
                bool isText = string.Equals(node.type, "Text", StringComparison.OrdinalIgnoreCase);
                bool hasLabel = (string.Equals(node.type, "Button", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.type, "Toggle", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(node.text) && !node.children.Any(child =>
                        string.Equals(child.type, "Text", StringComparison.OrdinalIgnoreCase));
                if (isText || hasLabel) result.Add(new KeyValuePair<string, UIEffectNode>(path, node));
                Collect(node.children, path, result);
            }
        }
    }

    public sealed partial class UIEffectPrefabGeneratorWindow
    {
        [SerializeField] private TMP_FontAsset defaultFont;
        private static string DefaultFontPreferenceKey =>
            "Lxy.UIEffectGenerator.DefaultFont." + Hash128.Compute(Application.dataPath);

        private void LoadDefaultFontPreference()
        {
            if (defaultFont == null)
                defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(
                    EditorPrefs.GetString(DefaultFontPreferenceKey, string.Empty)));
        }

        private void DrawDefaultFont()
        {
            EditorGUI.BeginChangeCheck();
            defaultFont = (TMP_FontAsset)EditorGUILayout.ObjectField(
                new GUIContent("默认 TMP 字体", "当前项目的默认字体。留空使用 TMP Settings；Schema 中有效的独立字体仍会保留。"),
                defaultFont, typeof(TMP_FontAsset), false);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(DefaultFontPreferenceKey, AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(defaultFont)));
        }

        private bool CheckTextEnvironment()
        {
            string problem = UIEffectTypography.GetEnvironmentProblem(defaultFont);
            if (problem == null) return true;
            requestStatus = problem;
            return false;
        }
    }
}
#endif
