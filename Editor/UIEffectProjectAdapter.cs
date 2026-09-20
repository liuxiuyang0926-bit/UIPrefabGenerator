#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Lxy.UIEffectGenerator.Editor
{
    public enum UIEffectScriptType
    {
        ProjectDefault = -1,
        CSharp = 0,
        Lua = 1,
        None = 2,
    }

    public enum UIEffectLayer
    {
        Auto = 0,
        Bottom = 10,
        Stack = 20,
        Popup = 30,
        Guide = 40,
        Top = 50,
        Loading = 60,
        Tips = 70,
        Debug = 80,
    }

    public sealed class UIEffectProjectDefaults
    {
        /// <summary>
        /// 公开的prefabFolder数据。
        /// </summary>
        public string prefabFolder = "Assets/GeneratedUI/Prefabs";
        /// <summary>
        /// 公开的codeNamespace数据。
        /// </summary>
        public string codeNamespace = "Game.UI";
        /// <summary>
        /// 公开的脚本Folder数据。
        /// </summary>
        public string scriptFolder = "Assets/GeneratedUI/Scripts";
        /// <summary>
        /// 公开的资源Search根节点数据。
        /// </summary>
        public string resourceSearchRoot = "Assets";
        /// <summary>
        /// 公开的引用Folder数据。
        /// </summary>
        public string referenceFolder =
            "Assets/Editor/UIEffectGenerator/References";
        /// <summary>
        /// 公开的schemaFolder数据。
        /// </summary>
        public string schemaFolder =
            "Assets/Editor/UIEffectGenerator/Schemas";
        /// <summary>
        /// 公开的FigmaSpriteFolder数据。
        /// </summary>
        public string figmaSpriteFolder =
            "Assets/GeneratedUI/FigmaSprites";
        /// <summary>
        /// 公开的脚本类型数据。
        /// </summary>
        public UIEffectScriptType scriptType = UIEffectScriptType.None;
        /// <summary>
        /// 公开的ui层级数据。
        /// </summary>
        public UIEffectLayer uiLayer = UIEffectLayer.Auto;
    }

    public readonly struct UIEffectPrefabHostResult
    {
        /// <summary>
        /// 创建UIEffectPrefabHostResult实例。
        /// </summary>
        public UIEffectPrefabHostResult(string prefabPath)
        {
            PrefabPath = prefabPath;
        }

        /// <summary>
        /// 向调用方提供Prefab路径。
        /// </summary>
        public string PrefabPath { get; }
    }

    /// <summary>
    /// Project-specific integration point. The portable package owns the
    /// Generated subtree; adapters only prepare the Prefab shell and optional
    /// binding/script metadata around it.
    /// </summary>
    public interface IUIEffectProjectAdapter
    {
        string Id { get; }
        string DisplayName { get; }
        bool SupportsScriptGeneration { get; }
        bool SupportsLayerSelection { get; }
        /// <summary>
        /// 创建Defaults。
        /// </summary>
        UIEffectProjectDefaults CreateDefaults();

        UIEffectPrefabHostResult CreateOrUpdatePrefab(
            UIEffectPrefabGenerationOptions options,
            bool promptForExistingPrefab);

        void BeforeReplaceGeneratedTree(
            GameObject prefabRoot,
            Transform previousGeneratedRoot);

        void AfterBuildGeneratedTree(
            GameObject prefabRoot,
            UIEffectPrefabGenerationOptions options);
    }

    public static class UIEffectProjectAdapterRegistry
    {
        private static readonly IUIEffectProjectAdapter GenericAdapter =
            new GenericUGUIProjectAdapter();

        private static IUIEffectProjectAdapter registeredAdapter;
        private static int registeredPriority = int.MinValue;

        /// <summary>
        /// 向调用方提供Active。
        /// </summary>
        public static IUIEffectProjectAdapter Active =>
            registeredAdapter ?? GenericAdapter;

        /// <summary>
        /// 注册当前实例。
        /// </summary>
        public static void Register(
            IUIEffectProjectAdapter adapter,
            int priority = 0)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            if (registeredAdapter != null && priority < registeredPriority)
            {
                return;
            }

            registeredAdapter = adapter;
            registeredPriority = priority;
        }

        /// <summary>
        /// 注销当前实例。
        /// </summary>
        public static void Unregister(IUIEffectProjectAdapter adapter)
        {
            if (!ReferenceEquals(registeredAdapter, adapter))
            {
                return;
            }

            registeredAdapter = null;
            registeredPriority = int.MinValue;
        }
    }

    public static class UIEffectEditorUtility
    {
        private static readonly HashSet<string> CSharpKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "abstract", "as", "base", "bool", "break", "byte",
                "case", "catch", "char", "checked", "class", "const",
                "continue", "decimal", "default", "delegate", "do",
                "double", "else", "enum", "event", "explicit", "extern",
                "false", "finally", "fixed", "float", "for", "foreach",
                "goto", "if", "implicit", "in", "int", "interface",
                "internal", "is", "lock", "long", "namespace", "new",
                "null", "object", "operator", "out", "override", "params",
                "private", "protected", "public", "readonly", "ref",
                "return", "sbyte", "sealed", "short", "sizeof",
                "stackalloc", "static", "string", "struct", "switch",
                "this", "throw", "true", "try", "typeof", "uint",
                "ulong", "unchecked", "unsafe", "ushort", "using",
                "virtual", "void", "volatile", "while",
            };

        /// <summary>
        /// 执行转换为Absolute路径相关逻辑。
        /// </summary>
        public static string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                NormalizeAssetPath(assetPath)));
        }

        /// <summary>
        /// 执行Sanitize类型名称相关逻辑。
        /// </summary>
        public static string SanitizeTypeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UIExample";
            }

            var builder = new StringBuilder();
            bool uppercaseNext = true;
            foreach (char character in value.Trim())
            {
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    uppercaseNext = true;
                    continue;
                }

                if (builder.Length == 0 && char.IsDigit(character))
                {
                    builder.Append('_');
                }

                if (character == '_')
                {
                    uppercaseNext = true;
                    continue;
                }

                builder.Append(
                    uppercaseNext
                        ? char.ToUpperInvariant(character)
                        : character);
                uppercaseNext = false;
            }

            string result = builder.ToString();
            if (result.Length == 0)
            {
                return "UIExample";
            }

            return CSharpKeywords.Contains(result)
                ? "_" + result
                : result;
        }

        /// <summary>
        /// 执行规范化资源路径相关逻辑。
        /// </summary>
        public static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// 执行Combine资源路径相关逻辑。
        /// </summary>
        public static string CombineAssetPath(
            string folder,
            string fileName)
        {
            string normalizedFolder = NormalizeAssetPath(folder);
            return normalizedFolder.Length == 0
                ? fileName
                : normalizedFolder + "/" + fileName;
        }

        /// <summary>
        /// 确保资源目录。
        /// </summary>
        public static void EnsureAssetFolder(string folder)
        {
            folder = NormalizeAssetPath(folder);
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            if (folder != "Assets" &&
                !folder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "目录必须位于 Assets 下：" + folder);
            }

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }
    }

    internal sealed class GenericUGUIProjectAdapter :
        IUIEffectProjectAdapter
    {
        /// <summary>
        /// 向调用方提供标识。
        /// </summary>
        public string Id => "generic-ugui";
        /// <summary>
        /// 向调用方提供Display名称。
        /// </summary>
        public string DisplayName => "通用 UGUI";
        /// <summary>
        /// 向调用方提供SupportsScriptGeneration。
        /// </summary>
        public bool SupportsScriptGeneration => false;
        /// <summary>
        /// 向调用方提供Supports层级Selection。
        /// </summary>
        public bool SupportsLayerSelection => false;

        /// <summary>
        /// 创建Defaults。
        /// </summary>
        public UIEffectProjectDefaults CreateDefaults()
        {
            return new UIEffectProjectDefaults();
        }

        /// <summary>
        /// 创建OrUpdate预制体。
        /// </summary>
        public UIEffectPrefabHostResult CreateOrUpdatePrefab(
            UIEffectPrefabGenerationOptions options,
            bool promptForExistingPrefab)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            string prefabPath = UIEffectEditorUtility.CombineAssetPath(
                options.prefabFolder,
                options.panelId + ".prefab");
            UIEffectEditorUtility.EnsureAssetFolder(
                Path.GetDirectoryName(prefabPath)?.Replace('\\', '/'));

            bool exists =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            if (exists && promptForExistingPrefab &&
                !EditorUtility.DisplayDialog(
                    "Prefab 已存在",
                    prefabPath + " 已存在。\n" +
                    "只会重建 Generated 子树，是否继续？",
                    "使用现有 Prefab",
                    "取消"))
            {
                throw new OperationCanceledException();
            }

            if (exists)
            {
                PrepareExistingPrefab(prefabPath);
            }
            else
            {
                CreatePrefab(prefabPath, options.panelId);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                prefabPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            return new UIEffectPrefabHostResult(prefabPath);
        }

        /// <summary>
        /// 执行BeforeReplace生成结果Tree相关逻辑。
        /// </summary>
        public void BeforeReplaceGeneratedTree(
            GameObject prefabRoot,
            Transform previousGeneratedRoot)
        {
        }

        /// <summary>
        /// 执行After构建生成结果Tree相关逻辑。
        /// </summary>
        public void AfterBuildGeneratedTree(
            GameObject prefabRoot,
            UIEffectPrefabGenerationOptions options)
        {
        }

        /// <summary>
        /// 创建预制体。
        /// </summary>
        private static void CreatePrefab(
            string prefabPath,
            string panelId)
        {
            var root = new GameObject(
                panelId,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster),
                typeof(CanvasGroup));
            try
            {
                ConfigureRoot(root, true);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    prefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        "无法创建通用 UGUI Prefab：" + prefabPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 准备Existing预制体。
        /// </summary>
        private static void PrepareExistingPrefab(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                throw new InvalidOperationException(
                    "无法加载现有 Prefab：" + prefabPath);
            }

            try
            {
                ConfigureRoot(root, false);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    prefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        "无法保存现有 Prefab：" + prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 执行配置根节点相关逻辑。
        /// </summary>
        private static void ConfigureRoot(
            GameObject root,
            bool configureNewCanvas)
        {
            RectTransform rect = root.GetComponent<RectTransform>();
            if (rect == null)
            {
                throw new InvalidOperationException(
                    "目标 Prefab 根节点必须使用 RectTransform：" +
                    root.name);
            }

            if (configureNewCanvas)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = root.AddComponent<Canvas>();
                configureNewCanvas = true;
            }

            if (configureNewCanvas)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            if (root.GetComponent<GraphicRaycaster>() == null)
            {
                root.AddComponent<GraphicRaycaster>();
            }

            if (root.GetComponent<CanvasGroup>() == null)
            {
                root.AddComponent<CanvasGroup>();
            }
        }
    }
}
#endif
