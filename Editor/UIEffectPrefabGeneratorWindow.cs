#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcessWindowStyle = System.Diagnostics.ProcessWindowStyle;
using UnityEditor;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    public sealed partial class UIEffectPrefabGeneratorWindow : EditorWindow
    {
        private const string CodexCommandEditorPrefsKey =
            "Lxy.UIEffectGenerator.CodexCommand";
        private const string CodexAutoGenerateEditorPrefsKey =
            "Lxy.UIEffectGenerator.CodexAutoGenerate";
        private const string StableSchemaReuseEditorPrefsKey =
            "Lxy.UIEffectGenerator.StableSchemaReuse";
        private const string ResourceRootGuidEditorPrefsKey =
            "Lxy.UIEffectGenerator.ResourceRootGuid";
        private const string ResourceMatchModeEditorPrefsKey =
            "Lxy.UIEffectGenerator.ResourceMatchMode";
        private const string LocalGenerationModeEditorPrefsKey =
            "Lxy.UIEffectGenerator.LocalGenerationMode";
        private const string LegacyEditorPrefsPrefix =
            "LxyDemo.UIEffectPrefabGenerator.";
        private static readonly string[] ScriptTypeNames =
        {
            "C Sharp",
            "Lua",
        };
        private static string ReferenceFolder =>
            GetProjectDefaults().referenceFolder;
        private static string SchemaFolder =>
            GetProjectDefaults().schemaFolder;
        private enum ReferenceSource
        {
            LocalImage,
            FigmaNodeUrl,
        }

        private enum CodexTaskKind
        {
            None,
            LocalImage,
            FullFidelity,
            FigmaMcp,
        }

        private enum LocalGenerationMode
        {
            HighFidelitySinglePass,
            CompactSchema,
        }

        private ReferenceSource source = ReferenceSource.LocalImage;
        private Texture2D referenceImage;
        private string sourceUrl = string.Empty;
        private TextAsset schemaAsset;
        private string panelId = "UIExample";
        private string prefabFolder = string.Empty;
        private DefaultAsset prefabOutputFolder;
        private static string PrefabFolderPreferenceKey =>
            "Lxy.UIEffectGenerator.PrefabFolderGuid." + Hash128.Compute(Application.dataPath);
        private UIEffectScriptType scriptType =
            UIEffectScriptType.ProjectDefault;
        private string codeNamespace = string.Empty;
        private string logicClassName = "UIExample";
        private string scriptFolder = string.Empty;
        private UIEffectLayer uiLayer = UIEffectLayer.Auto;
        private string resourceSearchRoots = string.Empty;
        private UIEffectResourceMatchMode resourceMatchMode =
            UIEffectResourceMatchMode.VisualSimilarity;
        private DefaultAsset resourceRootFolder;
        private Vector2 scrollPosition;
        private bool requestInProgress;
        private string requestStatus = string.Empty;
        private string codexCommand = "codex";
        private bool autoGenerateAfterCodex = true;
        private bool reuseSchemaForUnchangedReference = true;
        private LocalGenerationMode localGenerationMode =
            LocalGenerationMode.HighFidelitySinglePass;
        private bool showCodexSettings;
        private UIEffectCodexRunner codexRunner;
        private string pendingSchemaAssetPath = string.Empty;
        private string pendingReferenceAssetPath = string.Empty;
        private string pendingCodexOutputPath = string.Empty;
        private int pendingReferenceWidth;
        private int pendingReferenceHeight;
        private string pendingReferenceImageHash = string.Empty;
        private readonly List<string> pendingCodexImageInputs =
            new List<string>();
        private bool pendingSchemaExisted;
        private bool pendingAutoGenerate;
        private bool resourceRescanScheduled;
        private UIEffectCodexUsage lastCodexUsage;
        private CodexTaskKind pendingCodexTask = CodexTaskKind.None;
        private string pendingFigmaRequestedPanelId = string.Empty;
        private Process figmaNodeDownloadProcess;
        private string figmaNodeDownloadPath = string.Empty;
        private Action<byte[]> figmaNodeDownloadSuccess;

        [Serializable]
        private sealed class UIEffectCodexResponse
        {
            /// <summary>
            /// 公开的schemaJson数据。
            /// </summary>
            public string schemaJson = string.Empty;
            /// <summary>
            /// 公开的summary数据。
            /// </summary>
            public string summary = string.Empty;
        }

        [Serializable]
        private sealed class UIEffectFullFidelityResponse
        {
            /// <summary>
            /// 公开的schemaJson数据。
            /// </summary>
            public string schemaJson = string.Empty;
            /// <summary>
            /// 公开的summary数据。
            /// </summary>
            public string summary = string.Empty;
        }

        private sealed class CodexSpriteCatalog
        {
            /// <summary>
            /// 公开的清单数据。
            /// </summary>
            public string Manifest = string.Empty;
            /// <summary>
            /// 公开的图片路径数据。
            /// </summary>
            public string[] ImagePaths = Array.Empty<string>();
            /// <summary>
            /// 公开的Sprite数量数据。
            /// </summary>
            public int SpriteCount;
        }

        [Serializable]
        private sealed class UIEffectFigmaMcpResponse
        {
            /// <summary>
            /// 公开的mcp成功数据。
            /// </summary>
            public bool mcpSucceeded;
            /// <summary>
            /// 公开的mcp错误数据。
            /// </summary>
            public string mcpError = string.Empty;
            /// <summary>
            /// 公开的帧名称数据。
            /// </summary>
            public string frameName = string.Empty;
            /// <summary>
            /// 公开的schemaJson数据。
            /// </summary>
            public string schemaJson = string.Empty;
            /// <summary>
            /// 公开的引用图片地址数据。
            /// </summary>
            public string referenceImageUrl = string.Empty;
            /// <summary>
            /// 公开的summary数据。
            /// </summary>
            public string summary = string.Empty;
        }

        private bool IsCodexRunning =>
            codexRunner != null && codexRunner.IsRunning;

        private bool IsBusy => requestInProgress || IsCodexRunning;

        /// <summary>
        /// 执行打开相关逻辑。
        /// </summary>
        [MenuItem("工具/UI工具/根据效果图生成Prefab")]
        private static void Open()
        {
            var window = GetWindow<UIEffectPrefabGeneratorWindow>();
            window.titleContent = new GUIContent("UI预制体生成器");
            window.minSize = new Vector2(510f, 650f);
            window.Show();
        }

        /// <summary>
        /// 打开English。
        /// </summary>
        [MenuItem("Tools/UI Tools/Generate Prefab From Design")]
        private static void OpenEnglish()
        {
            Open();
        }

        /// <summary>
        /// 获取ProjectDefaults。
        /// </summary>
        private static UIEffectProjectDefaults GetProjectDefaults()
        {
            return UIEffectProjectAdapterRegistry.Active.CreateDefaults() ??
                   new UIEffectProjectDefaults();
        }

        /// <summary>
        /// 获取默认值资源Search根节点。
        /// </summary>
        private static string GetDefaultResourceSearchRoot()
        {
            string path = GetProjectDefaults().resourceSearchRoot;
            return AssetDatabase.IsValidFolder(path)
                ? path
                : "Assets";
        }

        /// <summary>
        /// 应用ProjectDefaults。
        /// </summary>
        private void ApplyProjectDefaults()
        {
            UIEffectProjectDefaults defaults = GetProjectDefaults();
            if (string.IsNullOrWhiteSpace(prefabFolder))
            {
                prefabFolder = defaults.prefabFolder;
            }

            if (scriptType == UIEffectScriptType.ProjectDefault)
            {
                scriptType = defaults.scriptType;
            }

            if (string.IsNullOrWhiteSpace(codeNamespace))
            {
                codeNamespace = defaults.codeNamespace;
            }

            if (string.IsNullOrWhiteSpace(scriptFolder))
            {
                scriptFolder = defaults.scriptFolder;
            }

            if (string.IsNullOrWhiteSpace(resourceSearchRoots))
            {
                resourceSearchRoots = GetDefaultResourceSearchRoot();
            }

            if (uiLayer == UIEffectLayer.Auto)
            {
                uiLayer = defaults.uiLayer;
            }
        }

        /// <summary>
        /// 在组件启用时建立运行时关联。
        /// </summary>
        private void OnEnable()
        {
            titleContent = new GUIContent("UI预制体生成器");
            ApplyProjectDefaults();
            LoadPrefabFolderPreference();
            LoadDefaultFontPreference();
            codexCommand = GetStringPreference(
                CodexCommandEditorPrefsKey,
                LegacyEditorPrefsPrefix + "CodexCommand",
                "codex");
            int savedGenerationMode = GetIntPreference(
                LocalGenerationModeEditorPrefsKey,
                LegacyEditorPrefsPrefix + "LocalGenerationMode",
                (int)LocalGenerationMode.HighFidelitySinglePass);
            localGenerationMode = savedGenerationMode ==
                                  (int)LocalGenerationMode.CompactSchema
                ? LocalGenerationMode.CompactSchema
                : LocalGenerationMode.HighFidelitySinglePass;
            autoGenerateAfterCodex = GetBoolPreference(
                CodexAutoGenerateEditorPrefsKey,
                LegacyEditorPrefsPrefix + "CodexAutoGenerate",
                true);
            reuseSchemaForUnchangedReference = GetBoolPreference(
                StableSchemaReuseEditorPrefsKey,
                LegacyEditorPrefsPrefix + "StableSchemaReuse",
                true);
            int savedResourceMode = GetIntPreference(
                ResourceMatchModeEditorPrefsKey,
                LegacyEditorPrefsPrefix + "ResourceMatchMode",
                (int)UIEffectResourceMatchMode.VisualSimilarity);
            resourceMatchMode = savedResourceMode ==
                                (int)UIEffectResourceMatchMode.ColorBlocks
                ? UIEffectResourceMatchMode.ColorBlocks
                : UIEffectResourceMatchMode.VisualSimilarity;
            string resourceRootGuid = GetStringPreference(
                ResourceRootGuidEditorPrefsKey,
                LegacyEditorPrefsPrefix + "ResourceRootGuid",
                string.Empty);
            string savedResourceRoot =
                AssetDatabase.GUIDToAssetPath(resourceRootGuid);
            if (!AssetDatabase.IsValidFolder(savedResourceRoot))
            {
                savedResourceRoot = resourceSearchRoots;
            }

            if (AssetDatabase.IsValidFolder(savedResourceRoot))
            {
                resourceSearchRoots = savedResourceRoot;
                resourceRootFolder =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                        savedResourceRoot);
            }
            codexRunner = new UIEffectCodexRunner();
            EditorApplication.update += PollCodexRunner;
            ScheduleResourceRescan("打开 UI预制体生成器");
        }

        /// <summary>
        /// 获取字符串Preference。
        /// </summary>
        private static string GetStringPreference(
            string key,
            string legacyKey,
            string fallback)
        {
            return EditorPrefs.HasKey(key)
                ? EditorPrefs.GetString(key, fallback)
                : EditorPrefs.GetString(legacyKey, fallback);
        }

        /// <summary>
        /// 获取整数Preference。
        /// </summary>
        private static int GetIntPreference(
            string key,
            string legacyKey,
            int fallback)
        {
            return EditorPrefs.HasKey(key)
                ? EditorPrefs.GetInt(key, fallback)
                : EditorPrefs.GetInt(legacyKey, fallback);
        }

        /// <summary>
        /// 获取布尔值Preference。
        /// </summary>
        private static bool GetBoolPreference(
            string key,
            string legacyKey,
            bool fallback)
        {
            return EditorPrefs.HasKey(key)
                ? EditorPrefs.GetBool(key, fallback)
                : EditorPrefs.GetBool(legacyKey, fallback);
        }

        /// <summary>
        /// 在组件停用时解除运行时关联。
        /// </summary>
        private void OnDisable()
        {
            StopNativePixelWarmup();
            if (IsBusy) UIEffectGenerationTiming.Finish("已中断");
            EditorApplication.update -= PollCodexRunner;
            EditorApplication.delayCall -= RebuildResourceCache;
            resourceRescanScheduled = false;
            CancelNodeDownload();
            codexRunner?.Dispose();
            codexRunner = null;
            DeleteCodexOutputFile();
        }

        /// <summary>
        /// 绘制编辑器窗口界面。
        /// </summary>
        private void OnGUI()
        {
            DrawGenerationTiming();
            scrollPosition = EditorGUILayout.BeginScrollView(
                scrollPosition);
            DrawIntroduction();
            EditorGUILayout.Space(8f);
            DrawReferenceSection();
            EditorGUILayout.Space(12f);
            DrawSchemaSection();
            EditorGUILayout.Space(12f);
            DrawGenerationSection();
            if (!string.IsNullOrEmpty(lastFidelityReportPath) && File.Exists(lastFidelityReportPath) &&
                GUILayout.Button("打开还原检查报告与预览"))
                Application.OpenURL(new Uri(Path.Combine(Path.GetDirectoryName(lastFidelityReportPath), "Index.html")).AbsoluteUri);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 绘制Introduction。
        /// </summary>
        private void DrawIntroduction()
        {
            EditorGUILayout.HelpBox(
                "本地效果图默认先量取结构，再按节点检索完整 Sprite 资源库，" +
                "结合原图裁片和目标尺寸预览校正层级、资源与文字。Unity Builder " +
                "生成后保存实际预览及差异检查报告。轻量 UISchema 模式仍可作为可选入口；" +
                "Figma Frame 链接通过 Codex 配置的 Figma MCP 连接读取，" +
                "Unity C# 负责校验并生成 UISchema/Prefab。",
                MessageType.Info);
        }

        /// <summary>
        /// 绘制引用Section。
        /// </summary>
        private void DrawReferenceSection()
        {
            EditorGUILayout.LabelField(
                "1. 设计来源",
                EditorStyles.boldLabel);
            source = (ReferenceSource)EditorGUILayout.EnumPopup(
                "来源",
                source);

            switch (source)
            {
                case ReferenceSource.LocalImage:
                    referenceImage = (Texture2D)EditorGUILayout.ObjectField(
                        "效果图",
                        referenceImage,
                        typeof(Texture2D),
                        false);
                    using (new EditorGUI.DisabledScope(IsBusy))
                    {
                        if (GUILayout.Button("从磁盘导入 PNG/JPG"))
                        {
                            ImportLocalReference();
                        }
                    }
                    break;
                case ReferenceSource.FigmaNodeUrl:
                    DrawFigmaFields();
                    break;
            }

            if (!string.IsNullOrWhiteSpace(requestStatus))
            {
                EditorGUILayout.HelpBox(
                    requestStatus,
                    IsBusy
                        ? MessageType.Info
                        : MessageType.None);
            }
        }

        /// <summary>
        /// 绘制FigmaFields。
        /// </summary>
        private void DrawFigmaFields()
        {
            sourceUrl = EditorGUILayout.TextField(
                "Figma 节点链接",
                sourceUrl);

            EditorGUILayout.HelpBox(
                "粘贴带 node-id 的 Frame/Component 链接即可生成。" +
                "需要先在 Codex CLI 中配置名为 figma 的官方 " +
                "Figma MCP 并完成 OAuth；未连接时会在 AI 调用前停止。" +
                "MCP 读取设计和临时截图，C# 将复杂视觉节点裁切为本地 " +
                "Sprite；Unity 不读取或保存任何 Figma 凭据。",
                MessageType.None);

            using (new EditorGUI.DisabledScope(IsBusy))
            {
                if (GUILayout.Button(
                        "读取 Figma 节点并生成 Prefab",
                        GUILayout.Height(34f)))
                {
                    BeginFigmaImportAndGenerate();
                }
            }
        }

        /// <summary>
        /// 绘制结构定义Section。
        /// </summary>
        private void DrawSchemaSection()
        {
            EditorGUILayout.LabelField(
                "2. 本地生成方式 / UISchema",
                EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            localGenerationMode = (LocalGenerationMode)EditorGUILayout.Popup(
                "生成方式",
                (int)localGenerationMode,
                new[]
                {
                    "高精度还原（结构分析 + 逐节点校正）",
                    "轻量 UISchema（旧流程）",
                });
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(
                    LocalGenerationModeEditorPrefsKey,
                    (int)localGenerationMode);
            }

            EditorGUILayout.HelpBox(
                localGenerationMode ==
                LocalGenerationMode.HighFidelitySinglePass
                    ? "先量取完整布局，再按每个节点从资源库检索候选，" +
                      "对照局部原图校正资源与层级。生成后输出实际预览、差异图" +
                      "和文字检查报告。首次升级建议关闭“同图复用现有 Schema”。"
                    : "轻量模式只让 Codex 量取精简 UISchema，资源匹配和 " +
                      "Prefab 生成由本地 C# 完成。",
                localGenerationMode ==
                LocalGenerationMode.HighFidelitySinglePass
                    ? MessageType.Info
                    : MessageType.None);
            schemaAsset = (TextAsset)EditorGUILayout.ObjectField(
                "Schema JSON",
                schemaAsset,
                typeof(TextAsset),
                false);
            string previousPanelId = panelId;
            panelId = EditorGUILayout.TextField("Panel ID", panelId);
            if (!string.Equals(
                    previousPanelId,
                    panelId,
                    StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(logicClassName) ||
                 string.Equals(
                     logicClassName,
                     UIEffectEditorUtility.SanitizeTypeName(previousPanelId),
                     StringComparison.Ordinal)))
            {
                logicClassName = GetSafePanelId();
            }

            using (new EditorGUI.DisabledScope(
                       referenceImage == null || IsBusy))
            {
                if (GUILayout.Button("创建基础 UISchema"))
                {
                    CreateStarterSchema();
                }
            }

            EditorGUILayout.HelpBox(
                "“创建基础 UISchema”只建立画布、背景和标题样例。" +
                "让 Codex 根据效果图补齐真实层级、布局、文字和资源语义后，" +
                "再生成 Prefab。",
                MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Codex 分析",
                EditorStyles.boldLabel);
            if (localGenerationMode == LocalGenerationMode.CompactSchema)
            {
                autoGenerateAfterCodex = EditorGUILayout.Toggle(
                    "完成后自动生成 Prefab",
                    autoGenerateAfterCodex);
            }
            EditorGUI.BeginChangeCheck();
            reuseSchemaForUnchangedReference = EditorGUILayout.Toggle(
                "同图复用现有 Schema",
                reuseSchemaForUnchangedReference);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(
                    StableSchemaReuseEditorPrefsKey,
                    reuseSchemaForUnchangedReference);
            }
            EditorGUILayout.HelpBox(
                "Panel、效果图路径、原始尺寸和图片内容哈希都一致时，" +
                "复用已审核的 UISchema。需要重新量图时关闭此选项。",
                MessageType.None);
            showCodexSettings = EditorGUILayout.Foldout(
                showCodexSettings,
                "Codex 设置",
                true);
            if (showCodexSettings)
            {
                EditorGUI.indentLevel++;
                codexCommand = EditorGUILayout.TextField(
                    "Codex 命令",
                    codexCommand);
                EditorPrefs.SetString(
                    CodexCommandEditorPrefsKey,
                    codexCommand);

                EditorGUILayout.HelpBox(
                    "默认填写 codex。找不到命令时，可填写 .cmd、" +
                    ".exe 或 .ps1 的完整路径。两种本地效果图分析都保持项目" +
                    "只读；Prefab 由当前 Unity Editor 内的 Builder 生成。",
                    MessageType.None);
                EditorGUI.indentLevel--;
            }

            if (lastCodexUsage != null)
            {
                EditorGUILayout.HelpBox(
                    "上次 Codex 分析 Token：" +
                    $"输入 {lastCodexUsage.input_tokens:N0}，" +
                    $"缓存输入 {lastCodexUsage.cached_input_tokens:N0}，" +
                    $"输出 {lastCodexUsage.output_tokens:N0}，" +
                    $"推理输出 {lastCodexUsage.reasoning_output_tokens:N0}，" +
                    $"合计 {lastCodexUsage.TotalTokens:N0}。",
                    MessageType.None);
            }

            if (IsCodexRunning)
            {
                if (GUILayout.Button("取消 Codex 分析"))
                {
                    codexRunner.Cancel();
                    StopNativePixelWarmup();
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(
                           referenceImage == null || requestInProgress))
                {
                    string buttonLabel;
                    if (localGenerationMode ==
                        LocalGenerationMode.HighFidelitySinglePass)
                    {
                        buttonLabel = "Codex 高精度还原 Prefab";
                    }
                    else
                    {
                        string action = reuseSchemaForUnchangedReference
                            ? "稳定复用/分析效果图"
                            : "强制重新分析效果图";
                        buttonLabel = autoGenerateAfterCodex
                            ? action + "并生成 Prefab"
                            : action + "并生成 UISchema";
                    }
                    if (GUILayout.Button(
                            buttonLabel,
                            GUILayout.Height(34f)))
                    {
                        if (localGenerationMode ==
                            LocalGenerationMode.HighFidelitySinglePass)
                        {
                            BeginFullFidelityGeneration();
                        }
                        else
                        {
                            BeginCodexAnalysis();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 绘制GenerationSection。
        /// </summary>
        private void DrawGenerationSection()
        {
            EditorGUILayout.LabelField(
                "3. 生成 Prefab",
                EditorStyles.boldLabel);
            DrawPrefabFolder();
            DrawDefaultFont();
            int resourceModeIndex = resourceMatchMode ==
                                    UIEffectResourceMatchMode.ColorBlocks
                ? 1
                : 0;
            int selectedResourceMode = EditorGUILayout.Popup(
                "视觉资源",
                resourceModeIndex,
                new[]
                {
                    "效果图匹配项目 Sprite",
                    "仅生成色块",
                });
            UIEffectResourceMatchMode selectedMode =
                selectedResourceMode == 1
                    ? UIEffectResourceMatchMode.ColorBlocks
                    : UIEffectResourceMatchMode.VisualSimilarity;
            if (selectedMode != resourceMatchMode)
            {
                resourceMatchMode = selectedMode;
                EditorPrefs.SetInt(
                    ResourceMatchModeEditorPrefsKey,
                    (int)resourceMatchMode);
                if (resourceMatchMode ==
                    UIEffectResourceMatchMode.VisualSimilarity)
                {
                    ScheduleResourceRescan("已启用效果图资源匹配");
                }
            }

            if (resourceMatchMode ==
                UIEffectResourceMatchMode.VisualSimilarity)
            {
                EditorGUI.BeginChangeCheck();
                DefaultAsset selectedFolder =
                    (DefaultAsset)EditorGUILayout.ObjectField(
                        "资源总目录",
                        resourceRootFolder,
                        typeof(DefaultAsset),
                        false);
                if (EditorGUI.EndChangeCheck())
                {
                    SetResourceRootFolder(selectedFolder);
                }

                EditorGUILayout.HelpBox(
                    "只需选择一个 Assets 下的总父目录；所有子目录会递归扫描。" +
                    "窗口打开和资源导入后会自动刷新索引；资源名只参与视觉" +
                    "证据接近时的稳定决胜。",
                    MessageType.None);
                using (new EditorGUI.DisabledScope(
                           resourceRootFolder == null))
                {
                    if (GUILayout.Button("重新扫描资源"))
                    {
                        ScheduleResourceRescan("手动重新扫描");
                    }
                }
            }

            IUIEffectProjectAdapter adapter =
                UIEffectProjectAdapterRegistry.Active;
            // Framework settings belong to the host project. A standalone install
            // only exposes design input, resource matching and Prefab output.
            if (!adapter.SupportsScriptGeneration && !adapter.SupportsLayerSelection)
            {
                return;
            }

            EditorGUILayout.LabelField(
                "Prefab 适配器",
                adapter.DisplayName);
            if (adapter.SupportsScriptGeneration)
            {
                int scriptTypeIndex =
                    scriptType == UIEffectScriptType.Lua ? 1 : 0;
                scriptTypeIndex = EditorGUILayout.Popup(
                    "脚本类型",
                    scriptTypeIndex,
                    ScriptTypeNames);
                scriptType = scriptTypeIndex == 1
                    ? UIEffectScriptType.Lua
                    : UIEffectScriptType.CSharp;
                using (new EditorGUI.DisabledScope(
                           scriptType != UIEffectScriptType.CSharp))
                {
                    codeNamespace = EditorGUILayout.TextField(
                        "C# 命名空间",
                        codeNamespace);
                    logicClassName = EditorGUILayout.TextField(
                        "逻辑类名",
                        logicClassName);
                    scriptFolder = EditorGUILayout.TextField(
                        "脚本目录",
                        scriptFolder);
                }
            }
            if (adapter.SupportsLayerSelection)
            {
                uiLayer = (UIEffectLayer)EditorGUILayout.EnumPopup(
                    "UI 层级",
                    uiLayer);
            }
        }

        /// <summary>
        /// 执行BeginFullFidelityGeneration相关逻辑。
        /// </summary>
        private void BeginFullFidelityGeneration()
        {
            if (!CheckTextEnvironment()) return;
            if (referenceImage == null)
            {
                requestStatus = "请先导入或选择效果图。";
                return;
            }

            string referenceAssetPath =
                AssetDatabase.GetAssetPath(referenceImage);
            if (string.IsNullOrWhiteSpace(referenceAssetPath))
            {
                requestStatus = "效果图必须是当前项目中的资源。";
                return;
            }

            string safePanelId = GetSafePanelId();
            string projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            string referenceAbsolutePath =
                UIEffectEditorUtility.ToAbsolutePath(referenceAssetPath);
            string schemaAssetPath =
                $"{SchemaFolder}/{safePanelId}.json";
            string normalizedPrefabFolder = (GetPrefabOutputFolder() ?? string.Empty)
                .Trim()
                .TrimEnd('/', '\\')
                .Replace('\\', '/');
            if (normalizedPrefabFolder != "Assets" && !normalizedPrefabFolder.StartsWith(
                    "Assets/",
                    StringComparison.OrdinalIgnoreCase))
            {
                requestStatus = "Prefab 目录必须位于 Assets 下。";
                return;
            }
            prefabFolder = normalizedPrefabFolder;
            int sourceWidth;
            int sourceHeight;
            string referenceImageHash;
            try
            {
                GetReferenceSourceSize(
                    referenceAssetPath,
                    referenceImage,
                    out sourceWidth,
                    out sourceHeight);
                referenceImageHash = ComputeReferenceImageHash(
                    referenceAssetPath);
            }
            catch (Exception exception)
            {
                requestStatus =
                    "无法读取效果图原始信息：" + exception.Message;
                Debug.LogException(exception);
                return;
            }

            EnsureAssetFolder(SchemaFolder);
            string schemaAbsolutePath =
                UIEffectEditorUtility.ToAbsolutePath(schemaAssetPath);
            bool schemaExists = File.Exists(schemaAbsolutePath);
            if (schemaExists && TryReuseMatchingSchema(
                    schemaAssetPath,
                    safePanelId,
                    referenceAssetPath,
                    sourceWidth,
                    sourceHeight,
                    referenceImageHash,
                    true))
            {
                return;
            }

            string temporaryFolder = Path.Combine(
                projectRoot,
                "Library",
                "LxyUIEffectGenerator",
                "UIEffectCodex");
            string outputSchemaPath = Path.Combine(
                temporaryFolder,
                "FullFidelityResponse.schema.json");
            string outputPath = Path.Combine(
                temporaryFolder,
                safePanelId + "_Full_" +
                Guid.NewGuid().ToString("N") + ".json");

            try
            {
                Directory.CreateDirectory(temporaryFolder);
                DeleteCodexImageInputs();
                UIEffectGenerationTiming.BeginWorkflow(safePanelId);
                UIEffectGenerationTiming.BeginStage("效果图准备");
                string[] designImagePaths = CreateCodexImageInputs(
                    referenceAbsolutePath,
                    temporaryFolder,
                    safePanelId);
                var spriteCatalog = new CodexSpriteCatalog();
                string[] codexImagePaths = designImagePaths;
                pendingCodexImageInputs.AddRange(codexImagePaths);
                File.WriteAllText(
                    outputSchemaPath,
                    BuildFullFidelityOutputSchema(),
                    new System.Text.UTF8Encoding(false));
                string selectedCommand = string.IsNullOrWhiteSpace(
                    codexCommand)
                    ? "codex"
                    : codexCommand.Trim();
                codexCommand = selectedCommand;
                EditorPrefs.SetString(
                    CodexCommandEditorPrefsKey,
                    selectedCommand);
                pendingSchemaAssetPath = schemaAssetPath;
                pendingReferenceAssetPath = referenceAssetPath;
                pendingReferenceWidth = sourceWidth;
                pendingReferenceHeight = sourceHeight;
                pendingReferenceImageHash = referenceImageHash;
                pendingCodexOutputPath = outputPath;
                pendingSchemaExisted = schemaExists;
                pendingAutoGenerate = true;
                pendingCodexTask = CodexTaskKind.FullFidelity;
                lastCodexUsage = null;
                awaitingStructure = true;
                pendingNodeEvidence = null;
                pendingGenerationOptions = CreateGenerationOptions();

                UIEffectGenerationTiming.BeginStage("AI 结构分析（1/2）");

                codexRunner ??= new UIEffectCodexRunner();
                codexRunner.Start(new UIEffectCodexRequest
                {
                    provider = "Codex 结构分析 1/2",
                    codexCommand = selectedCommand,
                    projectRoot = projectRoot,
                    imagePaths = codexImagePaths,
                    outputSchemaPath = outputSchemaPath,
                    outputPath = outputPath,
                    requiresUnityMcp = false,
                    allowWorkspaceWrite = false,
                    reasoningEffort = "xhigh",
                    prompt = "你是第一阶段结构分析器。只从效果图量取结构；本阶段禁止填写 " +
                        "resource/resourceCandidates/font/fontMaterial，不猜资源。" +
                        "不确定的连续底图与叠层用 mayMerge/mayLayer 标记。\n" + BuildFullFidelityPrompt(
                        safePanelId,
                        referenceAssetPath,
                        sourceWidth,
                        sourceHeight,
                        designImagePaths.Length,
                        spriteCatalog, true),
                });
                StartNativePixelWarmup();
                requestStatus =
                    $"Codex 正在分析 {safePanelId} 的布局与视觉层级（1/2）…";
            }
            catch (Exception exception)
            {
                pendingCodexTask = CodexTaskKind.None;
                requestStatus =
                    "无法启动 Codex 高精度分析：" + exception.Message;
                Debug.LogException(exception);
                DeleteCodexOutputFile();
            }
        }

        /// <summary>
        /// 创建Codex图片Inputs。
        /// </summary>
        private static string[] CreateCodexImageInputs(
            string sourcePath,
            string temporaryFolder,
            string safePanelId)
        {
            var source = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);
            try
            {
                byte[] bytes = File.ReadAllBytes(sourcePath);
                if (!ImageConversion.LoadImage(source, bytes, false) ||
                    source.width <= 0 ||
                    source.height <= 0)
                {
                    throw new InvalidOperationException(
                        "无法把效果图转换为 Codex 视觉输入。 ");
                }

                string runId = Guid.NewGuid().ToString("N");
                var paths = new List<string>();
                string overviewPath = Path.Combine(
                    temporaryFolder,
                    safePanelId + "_" + runId + "_Overview.jpg");
                WriteCodexJpeg(
                    source,
                    new RectInt(0, 0, source.width, source.height),
                    overviewPath,
                    2048);
                paths.Add(overviewPath);

                if (source.width > 2048 || source.height > 2048)
                {
                    int leftWidth = source.width / 2;
                    int rightWidth = source.width - leftWidth;
                    int bottomHeight = source.height / 2;
                    int topHeight = source.height - bottomHeight;
                    var regions = new[]
                    {
                        new RectInt(
                            0,
                            bottomHeight,
                            leftWidth,
                            topHeight),
                        new RectInt(
                            leftWidth,
                            bottomHeight,
                            rightWidth,
                            topHeight),
                        new RectInt(0, 0, leftWidth, bottomHeight),
                        new RectInt(
                            leftWidth,
                            0,
                            rightWidth,
                            bottomHeight),
                    };
                    string[] suffixes =
                    {
                        "TopLeft",
                        "TopRight",
                        "BottomLeft",
                        "BottomRight",
                    };
                    for (int index = 0; index < regions.Length; index++)
                    {
                        string cropPath = Path.Combine(
                            temporaryFolder,
                            safePanelId + "_" + runId + "_" +
                            suffixes[index] + ".jpg");
                        WriteCodexJpeg(
                            source,
                            regions[index],
                            cropPath,
                            2048);
                        paths.Add(cropPath);
                    }
                }

                return paths.ToArray();
            }
            finally
            {
                DestroyImmediate(source);
            }
        }


        /// <summary>
        /// 执行写入CodexJpeg相关逻辑。
        /// </summary>
        private static void WriteCodexJpeg(
            Texture2D source,
            RectInt sourceRegion,
            string outputPath,
            int maximumEdge)
        {
            Texture2D regionTexture = null;
            Texture2D outputTexture = null;
            RenderTexture renderTexture = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                bool isFullImage = sourceRegion.x == 0 &&
                                   sourceRegion.y == 0 &&
                                   sourceRegion.width == source.width &&
                                   sourceRegion.height == source.height;
                regionTexture = isFullImage
                    ? source
                    : new Texture2D(
                        sourceRegion.width,
                        sourceRegion.height,
                        TextureFormat.RGB24,
                        false);
                if (!isFullImage)
                {
                    regionTexture.SetPixels(source.GetPixels(
                        sourceRegion.x,
                        sourceRegion.y,
                        sourceRegion.width,
                        sourceRegion.height));
                    regionTexture.Apply(false, false);
                }

                float scale = Mathf.Min(
                    1f,
                    maximumEdge /
                    (float)Mathf.Max(
                        regionTexture.width,
                        regionTexture.height));
                int outputWidth = Mathf.Max(
                    1,
                    Mathf.RoundToInt(regionTexture.width * scale));
                int outputHeight = Mathf.Max(
                    1,
                    Mathf.RoundToInt(regionTexture.height * scale));
                renderTexture = RenderTexture.GetTemporary(
                    outputWidth,
                    outputHeight,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);
                Graphics.Blit(regionTexture, renderTexture);
                RenderTexture.active = renderTexture;
                outputTexture = new Texture2D(
                    outputWidth,
                    outputHeight,
                    TextureFormat.RGB24,
                    false);
                outputTexture.ReadPixels(
                    new Rect(0, 0, outputWidth, outputHeight),
                    0,
                    0,
                    false);
                outputTexture.Apply(false, false);
                File.WriteAllBytes(
                    outputPath,
                    outputTexture.EncodeToJPG(94));
            }
            finally
            {
                RenderTexture.active = previous;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                if (outputTexture != null)
                {
                    DestroyImmediate(outputTexture);
                }

                if (regionTexture != null && regionTexture != source)
                {
                    DestroyImmediate(regionTexture);
                }
            }
        }

        /// <summary>
        /// 执行BeginCodexAnalysis相关逻辑。
        /// </summary>
        private void BeginCodexAnalysis()
        {
            if (!CheckTextEnvironment()) return;
            if (referenceImage == null)
            {
                requestStatus = "请先导入或选择效果图。";
                return;
            }

            string referenceAssetPath =
                AssetDatabase.GetAssetPath(referenceImage);
            if (string.IsNullOrWhiteSpace(referenceAssetPath))
            {
                requestStatus = "效果图必须是当前项目中的资源。";
                return;
            }

            int sourceWidth;
            int sourceHeight;
            string referenceImageHash;
            try
            {
                GetReferenceSourceSize(
                    referenceAssetPath,
                    referenceImage,
                    out sourceWidth,
                    out sourceHeight);
                referenceImageHash = ComputeReferenceImageHash(
                    referenceAssetPath);
            }
            catch (Exception exception)
            {
                requestStatus =
                    "无法读取效果图原始信息：" + exception.Message;
                Debug.LogException(exception);
                return;
            }

            string safePanelId = GetSafePanelId();
            EnsureAssetFolder(SchemaFolder);
            string schemaAssetPath =
                $"{SchemaFolder}/{safePanelId}.json";
            string schemaAbsolutePath =
                UIEffectEditorUtility.ToAbsolutePath(schemaAssetPath);
            bool schemaExists = File.Exists(schemaAbsolutePath);
            if (schemaExists && TryReuseMatchingSchema(
                    schemaAssetPath,
                    safePanelId,
                    referenceAssetPath,
                    sourceWidth,
                    sourceHeight,
                    referenceImageHash,
                    autoGenerateAfterCodex))
            {
                return;
            }

            if (schemaExists &&
                !EditorUtility.DisplayDialog(
                    "更新 UISchema",
                    schemaAssetPath +
                    " 已存在。是否允许本次 AI 分析更新它？",
                    "允许更新",
                    "取消"))
            {
                requestStatus = "已取消 AI 分析。";
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            string referenceAbsolutePath =
                UIEffectEditorUtility.ToAbsolutePath(referenceAssetPath);
            string temporaryFolder = Path.Combine(
                projectRoot,
                "Library",
                "LxyUIEffectGenerator",
                "UIEffectCodex");
            string outputSchemaPath = Path.Combine(
                temporaryFolder,
                "UISchemaResponse.schema.json");
            string outputPath = Path.Combine(
                temporaryFolder,
                safePanelId + "_" +
                Guid.NewGuid().ToString("N") + ".json");

            try
            {
                Directory.CreateDirectory(temporaryFolder);
                File.WriteAllText(
                    outputSchemaPath,
                    BuildCodexOutputSchema(),
                    new System.Text.UTF8Encoding(false));

                string selectedCommand = codexCommand;
                if (string.IsNullOrWhiteSpace(selectedCommand))
                {
                    selectedCommand = "codex";
                }

                codexCommand = selectedCommand;
                EditorPrefs.SetString(
                    CodexCommandEditorPrefsKey,
                    codexCommand);

                EditorPrefs.SetBool(
                    CodexAutoGenerateEditorPrefsKey,
                    autoGenerateAfterCodex);

                pendingSchemaAssetPath = schemaAssetPath;
                pendingReferenceAssetPath = referenceAssetPath;
                pendingCodexOutputPath = outputPath;
                pendingReferenceWidth = sourceWidth;
                pendingReferenceHeight = sourceHeight;
                pendingReferenceImageHash = referenceImageHash;
                pendingSchemaExisted = schemaExists;
                pendingAutoGenerate = autoGenerateAfterCodex;
                lastCodexUsage = null;
                pendingCodexTask = CodexTaskKind.LocalImage;

                if (pendingAutoGenerate)
                {
                    UIEffectGenerationTiming.BeginWorkflow(safePanelId);
                    UIEffectGenerationTiming.BeginStage("AI 结构分析");
                }

                codexRunner ??= new UIEffectCodexRunner();
                codexRunner.Start(new UIEffectCodexRequest
                {
                    provider = "Codex",
                    codexCommand = selectedCommand,
                    projectRoot = projectRoot,
                    imagePath = referenceAbsolutePath,
                    outputSchemaPath = outputSchemaPath,
                    outputPath = outputPath,
                    prompt = BuildCodexPrompt(
                        safePanelId,
                        referenceAssetPath,
                        schemaAssetPath,
                        sourceWidth,
                        sourceHeight),
                });
                requestStatus = "Codex 已启动，正在分析效果图…";
            }
            catch (Exception exception)
            {
                pendingCodexTask = CodexTaskKind.None;
                requestStatus =
                    "无法启动 Codex 分析：" + exception.Message;
                Debug.LogException(exception);
                DeleteCodexOutputFile();
            }
        }

        /// <summary>
        /// 尝试ReuseMatching结构定义，并返回是否成功。
        /// </summary>
        private bool TryReuseMatchingSchema(
            string schemaAssetPath,
            string safePanelId,
            string referenceAssetPath,
            int sourceWidth,
            int sourceHeight,
            string referenceImageHash,
            bool generatePrefab)
        {
            if (!reuseSchemaForUnchangedReference)
            {
                return false;
            }

            try
            {
                string absolutePath =
                    UIEffectEditorUtility.ToAbsolutePath(schemaAssetPath);
                UIEffectSchema schema = UIEffectSchemaUtility.Parse(
                    File.ReadAllText(absolutePath));
                bool sameIdentity = string.Equals(
                                        schema.name,
                                        safePanelId,
                                        StringComparison.Ordinal) &&
                                    string.Equals(
                                        NormalizeProjectAssetPath(
                                            schema.referenceImage),
                                        NormalizeProjectAssetPath(
                                            referenceAssetPath),
                                        StringComparison.OrdinalIgnoreCase) &&
                                    Mathf.Approximately(
                                        schema.designWidth,
                                        sourceWidth) &&
                                    Mathf.Approximately(
                                        schema.designHeight,
                                        sourceHeight);
                if (!sameIdentity)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(
                        schema.referenceImageHash) &&
                    !string.Equals(
                        schema.referenceImageHash,
                        referenceImageHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                bool stampedLegacyHash = string.IsNullOrWhiteSpace(
                    schema.referenceImageHash);
                if (stampedLegacyHash)
                {
                    schema.referenceImageHash = referenceImageHash;
                    File.WriteAllText(
                        absolutePath,
                        UIEffectSchemaUtility.ToCompactJson(schema),
                        new System.Text.UTF8Encoding(false));
                }

                AssetDatabase.ImportAsset(
                    schemaAssetPath,
                    stampedLegacyHash
                        ? ImportAssetOptions.ForceSynchronousImport |
                          ImportAssetOptions.ForceUpdate
                        : ImportAssetOptions.ForceSynchronousImport);
                schemaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                    schemaAssetPath);
                Selection.activeObject = schemaAsset;
                EditorGUIUtility.PingObject(schemaAsset);
                requestStatus = stampedLegacyHash
                    ? "已为现有 UISchema 记录效果图哈希并稳定复用：" +
                      schemaAssetPath
                    : "效果图未变化，已稳定复用 UISchema：" +
                      schemaAssetPath;
                Debug.Log(
                    "[UIPrefabGenerator] " + requestStatus +
                    "\n未重新调用 AI。",
                    schemaAsset);

                if (generatePrefab)
                {
                    GeneratePrefab();
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[UIPrefabGenerator] 现有 UISchema 无法稳定复用，" +
                    "将进入重新分析流程：" + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// 执行PollCodex执行器相关逻辑。
        /// </summary>
        private void PollCodexRunner()
        {
            if (codexRunner == null)
            {
                return;
            }

            bool changed = false;
            while (codexRunner.TryDequeueProgress(out string progress))
            {
                requestStatus = progress;
                changed = true;
            }

            if (codexRunner.TryTakeResult(
                    out UIEffectCodexRunResult result))
            {
                StopNativePixelWarmup();
                UIEffectGenerationTiming.BeginStage("结果校验与整理");
                changed = true;
                CodexTaskKind task = pendingCodexTask;
                pendingCodexTask = CodexTaskKind.None;
                if (task == CodexTaskKind.FigmaMcp)
                {
                    HandleFigmaMcpResult(result);
                }
                else if (task == CodexTaskKind.FullFidelity)
                {
                    HandleFullFidelityResult(result);
                }
                else
                {
                    HandleCodexResult(result);
                }
            }

            if (UIEffectGenerationTiming.IsRunning && !IsBusy && pendingCodexTask == CodexTaskKind.None)
            {
                UIEffectGenerationTiming.Finish(requestStatus.Contains("取消") ? "已取消" : "失败");
                changed = true;
            }

            if (awaitingStructure && pendingCodexTask == CodexTaskKind.FullFidelity && codexRunner.IsRunning)
                TickNativePixelWarmup();

            if (changed)
            {
                Repaint();
            }
        }

        /// <summary>
        /// 处理Codex结果。
        /// </summary>
        private void HandleCodexResult(UIEffectCodexRunResult result)
        {
            try
            {
                if (result == null)
                {
                    throw new InvalidOperationException(
                        "Codex 没有返回运行结果。");
                }

                lastCodexUsage = result.usage;

                if (result.canceled)
                {
                    requestStatus = "已取消 AI 分析。";
                    return;
                }

                if (!result.succeeded)
                {
                    string detail = GetShortError(result.error);
                    requestStatus =
                        $"Codex 分析失败（退出码 {result.exitCode}）" +
                        (detail.Length == 0 ? "。" : "：" + detail);
                    Debug.LogError(requestStatus);
                    return;
                }

                UIEffectCodexResponse response =
                    ParseLocalAiResponse(result.outputJson);
                if (response == null ||
                    string.IsNullOrWhiteSpace(response.schemaJson))
                {
                    throw new InvalidOperationException(
                        "Codex 返回结果中缺少 schemaJson。");
                }

                UIEffectSchema schema = ParseGeneratedAiSchema(
                    response.schemaJson,
                    "Codex 轻量模式");
                string expectedPanelId = Path.GetFileNameWithoutExtension(
                    pendingSchemaAssetPath);
                if (!string.Equals(
                        schema.name,
                        expectedPanelId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"UISchema.name 应为 {expectedPanelId}，" +
                        $"实际为 {schema.name}。");
                }

                if (!Mathf.Approximately(
                        schema.designWidth,
                        pendingReferenceWidth) ||
                    !Mathf.Approximately(
                        schema.designHeight,
                        pendingReferenceHeight))
                {
                    throw new InvalidOperationException(
                        "AI 返回的设计尺寸与效果图不一致：" +
                        $"期望 {pendingReferenceWidth}x" +
                        $"{pendingReferenceHeight}，实际 " +
                        $"{schema.designWidth}x{schema.designHeight}。");
                }

                string currentReferenceHash =
                    ComputeReferenceImageHash(
                        pendingReferenceAssetPath);
                if (!string.Equals(
                        currentReferenceHash,
                        pendingReferenceImageHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "AI 分析期间效果图内容发生变化，已拒绝写入过期的 " +
                        "UISchema，请重新执行。");
                }

                schema.referenceImage = pendingReferenceAssetPath;
                schema.referenceImageHash = currentReferenceHash;
                schema.useReferenceImageAsVisual = false;
                int inferredHierarchyNodes =
                    UIEffectSchemaUtility.InferContainedVisualHierarchy(
                        schema);
                int inferredRuntimeTemplateNodes =
                    UIEffectSchemaUtility.InferRuntimeTemplateGroups(
                        schema);
                int collapsedNodes =
                    UIEffectSchemaUtility.OptimizeRepeatedNodes(schema);
                string schemaAbsolutePath =
                    UIEffectEditorUtility.ToAbsolutePath(
                        pendingSchemaAssetPath);
                if (!pendingSchemaExisted &&
                    File.Exists(schemaAbsolutePath) &&
                    !EditorUtility.DisplayDialog(
                        "UISchema 已出现",
                        pendingSchemaAssetPath +
                        " 在分析期间被创建。是否覆盖？",
                        "覆盖",
                        "取消"))
                {
                    requestStatus =
                        "AI 分析完成，但没有覆盖新出现的 UISchema。";
                    return;
                }

                File.WriteAllText(
                    schemaAbsolutePath,
                    UIEffectSchemaUtility.ToCompactJson(schema),
                    new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(
                    pendingSchemaAssetPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                schemaAsset =
                    AssetDatabase.LoadAssetAtPath<TextAsset>(
                        pendingSchemaAssetPath);
                Selection.activeObject = schemaAsset;
                EditorGUIUtility.PingObject(schemaAsset);

                string summary = string.IsNullOrWhiteSpace(response.summary)
                    ? "无附加说明"
                    : response.summary.Trim();
                requestStatus =
                    "AI 已生成 UISchema：" +
                    pendingSchemaAssetPath;
                Debug.Log(
                    "[UIPrefabGenerator/Codex] UISchema：" +
                    pendingSchemaAssetPath +
                    $"\n自动归入视觉父节点：{inferredHierarchyNodes}" +
                    $"\n自动标记运行时模板候选：{inferredRuntimeTemplateNodes}" +
                    $"\n自动合并重复节点：{collapsedNodes}" +
                    "\n说明：" + summary,
                    schemaAsset);

                if (pendingAutoGenerate)
                {
                    GeneratePrefab();
                }
            }
            catch (Exception exception)
            {
                requestStatus =
                    "处理 Codex 结果失败：" + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                DeleteCodexOutputFile();
            }
        }

        /// <summary>
        /// 处理FullFidelity结果。
        /// </summary>
        private void HandleFullFidelityResult(
            UIEffectCodexRunResult result)
        {
            bool startedNextStage = false;
            try
            {
                if (result == null)
                {
                    throw new InvalidOperationException(
                        "Codex 高精度分析没有返回运行结果。");
                }

                AccumulateFidelityUsage(result.usage);
                if (result.canceled)
                {
                    requestStatus = "已取消高精度分析。";
                    return;
                }

                if (!result.succeeded)
                {
                    string detail = GetShortError(result.error);
                    requestStatus =
                        $"Codex 高精度分析失败（退出码 {result.exitCode}）" +
                        (detail.Length == 0 ? "。" : "：" + detail);
                    Debug.LogError(requestStatus);
                    return;
                }

                UIEffectFullFidelityResponse response =
                    JsonUtility.FromJson<UIEffectFullFidelityResponse>(
                        NormalizeAiJson(result.outputJson));
                if (response == null ||
                    string.IsNullOrWhiteSpace(response.schemaJson))
                {
                    throw new InvalidOperationException(
                        "Codex 返回结果中缺少完整 schemaJson。");
                }

                UIEffectSchema schema = ParseGeneratedAiSchema(
                    response.schemaJson,
                    awaitingStructure ? "Codex 结构分析" : "Codex 逐节点校正");
                string expectedPanelId = Path.GetFileNameWithoutExtension(
                    pendingSchemaAssetPath);
                if (!string.Equals(
                        schema.name,
                        expectedPanelId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"UISchema.name 应为 {expectedPanelId}，" +
                        $"实际为 {schema.name}。");
                }

                if (!Mathf.Approximately(
                        schema.designWidth,
                        pendingReferenceWidth) ||
                    !Mathf.Approximately(
                        schema.designHeight,
                        pendingReferenceHeight))
                {
                    throw new InvalidOperationException(
                        "Codex 返回的设计尺寸与效果图不一致：" +
                        $"期望 {pendingReferenceWidth}x" +
                        $"{pendingReferenceHeight}，实际 " +
                        $"{schema.designWidth}x{schema.designHeight}。");
                }

                string currentReferenceHash = ComputeReferenceImageHash(
                    pendingReferenceAssetPath);
                if (!string.Equals(
                        currentReferenceHash,
                        pendingReferenceImageHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Codex 分析期间效果图内容发生变化，已拒绝写入过期的 " +
                        "UISchema，请重新执行。");
                }

                schema.referenceImage = pendingReferenceAssetPath;
                schema.referenceImageHash = currentReferenceHash;
                schema.useReferenceImageAsVisual = false;
                if (awaitingStructure)
                {
                    DeleteCodexOutputFile(true);
                    BeginResourceCorrection(schema);
                    startedNextStage = true;
                    return;
                }
                MergeNodeEvidence(schema, pendingNodeEvidence);
                int inferredHierarchyNodes =
                    UIEffectSchemaUtility.InferContainedVisualHierarchy(
                        schema);
                int inferredRuntimeTemplateNodes =
                    UIEffectSchemaUtility.InferRuntimeTemplateGroups(
                        schema);
                int collapsedNodes =
                    UIEffectSchemaUtility.OptimizeRepeatedNodes(schema);
                string schemaAbsolutePath =
                    UIEffectEditorUtility.ToAbsolutePath(
                        pendingSchemaAssetPath);
                File.WriteAllText(
                    schemaAbsolutePath,
                    UIEffectSchemaUtility.ToCompactJson(schema),
                    new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(
                    pendingSchemaAssetPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                schemaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                    pendingSchemaAssetPath);

                string summary = string.IsNullOrWhiteSpace(response.summary)
                    ? "逐节点校正 UISchema 已完成"
                    : response.summary.Trim();
                requestStatus =
                    "高精度 UISchema 已生成，正在由 Unity Builder 创建 Prefab…";
                Debug.Log(
                    "[UIPrefabGenerator/Node Correction] UISchema：" +
                    pendingSchemaAssetPath +
                    $"\n自动归入视觉父节点：{inferredHierarchyNodes}" +
                    $"\n自动标记运行时模板候选：{inferredRuntimeTemplateNodes}" +
                    $"\n自动合并重复节点：{collapsedNodes}" +
                    "\n摘要：" + summary,
                    schemaAsset);
                GeneratePrefab();
            }
            catch (Exception exception)
            {
                requestStatus =
                    "处理 Codex 高精度结果失败：" +
                    exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                if (!startedNextStage)
                {
                    DeleteCodexOutputFile();
                    pendingNodeEvidence = null;
                    pendingGenerationOptions = null;
                }
            }
        }

        /// <summary>
        /// 构建FullFidelityPrompt。
        /// </summary>
        private string BuildFullFidelityPrompt(
            string safePanelId,
            string referenceAssetPath,
            int sourceWidth,
            int sourceHeight,
            int designImageCount,
            CodexSpriteCatalog spriteCatalog,
            bool structureOnly = false)
        {
            return
                "你是 Unity UI 的高精度视觉分析器。只使用本提示和已附加" +
                "图片，一次性输出完整 UISchema；禁止调用终端、文件、Skill、MCP " +
                "或任何其它工具，禁止要求下一轮补充信息。Unity Editor 会在收到" +
                "结果后执行本地校验，并在最终阶段使用项目现有 Builder 保存 Prefab。\n\n" +
                $"Panel ID：{safePanelId}\n" +
                $"效果图：{referenceAssetPath}\n" +
                $"原始设计尺寸：{sourceWidth}x{sourceHeight}\n" +
                $"前 {designImageCount} 张附件属于效果图：第一张是最长边 " +
                "2048 的完整概览；若共 5 张，后四张依次为 TopLeft、" +
                "TopRight、BottomLeft、BottomRight 高清象限。象限只用于看清" +
                "细节，全部坐标必须换算回原始设计尺寸。\n" +
                (structureOnly ? "本轮只量取结构，不处理资源路径、候选政策、字体路径或资源验证。\n" : "之后的 " +
                $"{spriteCatalog.ImagePaths.Length} 张附件是节点候选联系图与原始裁片，" +
                "顺序以 manifest 的 page/attachment 为准（该编号从效果图之后重新计数）。\n") +
                $"效果图 SHA-256：{pendingReferenceImageHash}\n" +
                "\n" + spriteCatalog.Manifest + "\n" +
                "严格还原规则：\n" +
                "1. UISchema 根只写 version=2.0、name、designWidth、designHeight、" +
                "children；name 和尺寸必须与上面完全一致。节点 type 只能是 " +
                "Container/Image/Text/Button/Toggle/ToggleGroup/ScrollRect。节点可写 " +
                "name,type,semantic,x,y,width,height,text,fontSize,characterSpacing," +
                "alignment,bold,color,intentionalColor," +
                (structureOnly ? string.Empty : "resource,resourceCandidates,resourcePolicy,") +
                "lineSpacing,noWrap,isOn,allowSwitchOff,scrollDirection," +
                "preserveAspect,sliced," +
                "raycastTarget,mayMerge,mayLayer,mayUseFullCanvasSprite,textMode," +
                "visualKind,binding,runtimeTemplateGroup,runtimeTemplateVariant,children，" +
                "省略默认值和空字段。禁止 useReferenceImageAsVisual 和整张效果图底图。\n" +
                "binding 只允许 Auto、Yes、No，通常省略；binding 不是字段名，" +
                "禁止写 lowerCamelCase 或其它业务标识。需要暴露组件时写 Yes，" +
                "具体绑定名称始终由 node.name 和项目统一前缀生成。\n" +
                "所有最终可绑定节点的 name 必须全局唯一；不同固定面板里语义" +
                "相同的 Value、Label、AddButton 等需要加最近父级语义前缀。非运行时" +
                "访问的装饰或静态 Text 不要写 binding=Yes。\n" +
                "若效果图是叠加在其它页面上的模态弹窗：只输出模态遮罩和弹窗" +
                "自身完整视觉子树；遮罩下仍可见的地图、导航、货币栏、入口按钮、" +
                "列表或 HUD 都属于被覆盖的底层界面，禁止写入 UISchema。遮罩与" +
                "弹窗即使原本嵌在底层容器中也提升为根节点，并保持完整画布绝对" +
                "坐标不变。只有没有大面积遮罩和独立弹窗前景时才按全屏界面输出。\n" +
                "2. 先按完整画布量取每个节点的绝对整数像素矩形，再建立视觉" +
                "所有权，最后用 child.x=child.left-parent.left、child.y=" +
                "child.top-parent.top 转局部坐标。输出前递归累加祖先坐标，检查" +
                "所有边缘、居中、等宽高和间距。兄弟节点顺序必须是从后到前的" +
                "实际绘制顺序。\n" +
                "3. 有独立可见边界的背景、牌、字段底、名字条、分数条、徽章、" +
                "旗帜和按钮必须是最近视觉父节点，并拥有内部 Text/Icon；禁止把 " +
                "GuildNamePlate 与 GuildName、Field 与 Label/Value 平铺。连续边框、" +
                "底纹和转角属于同一 Sprite 时只写一个 Image，Header/Content 可用" +
                "无 Graphic Container 分组，不得把完整背景拆成多个语义色块。\n" +
                (structureOnly ? "4. 为视觉节点写稳定通用 semantic（background/panel/header/bar/field/" +
                    "icon/emblem/badge/crest/flag/banner/avatar/portrait/overlay），资源由下一阶段验证。\n" :
                "4. 对每个视觉节点同时比较效果图、联系图外观、源尺寸、Border、" +
                "透明轮廓、颜色和父子上下文。只有视觉证据成立时才从 manifest " +
                "逐字复制精确 resource；名称只能弱辅助，不能单独决定。Border 非零" +
                "且用于可拉伸表面时写 sliced=true。联系图中没有可靠候选时留空 " +
                "resource，并写稳定通用 semantic（background/panel/header/bar/field/" +
                "icon/emblem/badge/crest/flag/banner/avatar/portrait/overlay），让本地" +
                "完整资源解析器匹配；不得猜不存在的路径。\n") +
                "5. 书法、Logo、发光或描边标题优先写 Text 加 textMode=ArtText、" +
                "visualKind=ArtText；疑似两层写 mayLayer=true。疑似已经烘焙进父" +
                "Sprite 的短排名/数字写 textMode=PossiblyBaked。真正可编辑文本保持" +
                "Text，不得为了静态相似度烘焙进背景。\n" +
                "本轮没有提供字体或字体材质清单，禁止输出 font/fontMaterial，禁止猜测任何字体路径；" +
                "Unity 会使用当前项目选择的默认 TMP 字体。" +
                "单行文字写 " +
                "noWrap=true；多行保留原始换行，可用 lineSpacing 调整。勿用缩小字号" +
                "掩盖错误文字矩形。" +
                (structureOnly ? string.Empty : "图标匹配后用完整 Sprite Rect（含透明留边）" +
                "复核 Image 矩形，不把不透明轮廓的紧包围框当成整张 Sprite 大小。" +
                "资源有歧义时写 Candidate + resourceCandidates；" +
                "Verified 只是请求，必须由本地独立高置信赢家验证；只有已证明纯色" +
                "的节点才能写 ColorFallback。\n") +
                "6. 先观察全部重复实例以校验第一项的边界和层级。数据驱动且结构" +
                "同构的 Card/Item/Row/Cell/业务 Panel 最终只保留视觉顺序第一份" +
                "完整模板；若红绿蓝等变体的正确保留项依赖 Sprite，完整写候选并" +
                "使用同一 runtimeTemplateGroup 和不同 runtimeTemplateVariant，交给" +
                "Builder 匹配后只留一个。固定常驻、左右布局、Button/Toggle 或结构" +
                "不同的面板不得去重。去重不得移动、拉伸、改名或改变 children。\n" +
                "排名/名次榜的 FirstPlace/SecondPlace/ThirdPlace 也是运行时数据模板，" +
                "不能仅因展示前三名就当作固定常驻领奖台。徽标或头像的原始比例不同" +
                "不改变模板身份；保留各自原始矩形供匹配，使用相同候选组后只生成一份。\n" +
                "7. 纯色只在区域内部确实均匀、没有纹理/边框/透明转角时写 " +
                "intentionalColor=true。无法证明资源时允许色块兜底，但不得漏掉联系" +
                "图中已经视觉命中的 Sprite。\n\n" +
                "最终只返回符合响应 Schema 的 JSON，不要 Markdown。schemaJson 必须" +
                "是转义后的完整单行 UISchema JSON 字符串；summary 只写一句完成说明。";
        }

        /// <summary>
        /// 构建FullFidelity输出结构定义。
        /// </summary>
        private static string BuildFullFidelityOutputSchema()
        {
            return
                "{\n" +
                "  \"type\": \"object\",\n" +
                "  \"properties\": {\n" +
                "    \"schemaJson\": {\"type\": \"string\"},\n" +
                "    \"summary\": {\"type\": \"string\"}\n" +
                "  },\n" +
                "  \"required\": [\"schemaJson\", \"summary\"],\n" +
                "  \"additionalProperties\": false\n" +
                "}\n";
        }

        /// <summary>
        /// 构建CodexPrompt。
        /// </summary>
        private string BuildCodexPrompt(
            string safePanelId,
            string referenceAssetPath,
            string schemaAssetPath,
            int sourceWidth,
            int sourceHeight)
        {
            string visualResourceInstruction = resourceMatchMode ==
                UIEffectResourceMatchMode.ColorBlocks
                ? "所有 Image、Button、Toggle 都只生成 UGUI 纯色块：" +
                  "禁止输出 resource、resourceCandidates 或 " +
                  "reference://crop；必须设置 intentionalColor=true，" +
                  "并用 color 近似效果图中的主色。"
                : "不要猜测或搜索项目资源名，也不要输出 resource、" +
                  "resourceCandidates 或 reference://crop；准确输出视觉节点" +
                  "的层级和矩形即可，Unity C# 会在用户选择的资源父目录中" +
                  "做本地像素特征匹配。对视觉角色可辨认的 Image、Button、" +
                  "Toggle 填写简短稳定的通用 semantic，例如 background、" +
                  "panel、header、bar、field、icon、emblem、badge、crest、" +
                  "flag、banner、avatar、portrait 或 overlay；semantic 只描述" +
                  "视觉类别，不猜项目路径、资源文件名或业务数据，确实无法" +
                  "判断时才省略。只有确实是无纹理纯色的节点才设置 " +
                  "intentionalColor=true 并填写 color；其他 Image、Button、" +
                  "Toggle 不要设置 intentionalColor。";
            return
                "你是 Unity UGUI 布局分析器。本提示包含完整规则，禁止调用 Skill、MCP、终端或搜索项目。\n\n" +
                "Unity Editor 低 Token 模式：只做视觉推理并返回精简的 " +
                "UISchema 2.0；除读取已给定效果图外，不执行项目搜索或文件操作。\n\n" +
                "禁止输出 font/fontMaterial 路径；字体由 Unity 当前项目的默认 TMP 字体配置决定。\n" +
                $"Panel ID：{safePanelId}\n" +
                $"效果图项目路径：{referenceAssetPath}\n" +
                $"效果图绝对路径：" +
                $"{UIEffectEditorUtility.ToAbsolutePath(referenceAssetPath)}\n" +
                $"目标 Schema 路径：{schemaAssetPath}\n" +
                $"源文件原始尺寸：{sourceWidth}x{sourceHeight}\n" +
                $"Unity 导入预览尺寸：{referenceImage.width}x" +
                $"{referenceImage.height}\n\n" +
                "只读取 Skill 的 references/compact-blueprint.md。" +
                "附件预览可能因 Unity maxTextureSize 或模型视觉输入而缩放，" +
                "designWidth/designHeight 和所有矩形必须换算回上述源文件原始尺寸。" +
                "必须先按完整效果图完成所有可见区域的几何、层级和独立资源边界" +
                "分析，再做业务模板省略；还原效果图和识别 Sprite 边界的优先级" +
                "高于减少节点数量。" +
                "画布原点永远是源图片最外层左上角 (0,0)，不得裁掉留白、" +
                "不得以第一个可见控件重新建立原点。输出前至少复核顶部页签、" +
                "内容区和右侧详情面板三组绝对矩形。" +
                "禁止设置 useReferenceImageAsVisual，禁止把语义组件变成透明层。" +
                "资源边界按连续边框、底纹和转角判断，不按 Header、Body、" +
                "Content 等语义分区切割；弹窗底图连续跨过标题区和内容区时，" +
                "必须用一个覆盖完整外框且排在内容之前的 Image，不能拆成 " +
                "DialogHeader/DialogBody 色块。卡片底图被内容遮挡时仍保留完整" +
                "背景 Image。若同一张连续底图横跨父节点全宽，但可见内容只在" +
                "中间区域，背景 Image 仍必须按完整资源边界覆盖父级；标题区、" +
                "内容区等语义分组改用无 Graphic 的 Container 作为其子节点，" +
                "不能各自生成 Image 色块。旗面与中央徽标轮廓可分辨时必须拆成" +
                "两个 Image。" +
                "反过来，某段底色、纹理、转角若与父 Sprite 连续且没有独立" +
                "边界，即使其上有标题文字，也不能为了语义分组额外创建 " +
                "Plate/Backdrop/Header Image；应把 Text 直接挂到已经包含该视觉的" +
                "父节点。只有独立描边、接缝、转角、透明轮廓或明显不同纹理能" +
                "证明它是单独视觉资源时，才创建嵌套 Image。" +
                "禁止把同一区域的背景、标题、标签和值全部平铺为同层节点。" +
                "背景条、标题牌、字段底、统计格、按钮或页签只要定义了明确" +
                "可见区域，就必须作为其内部文字、图标和字段的直接或最近" +
                "视觉父节点；优先选择完整包含子矩形且面积最小的先绘制节点。" +
                "每一条在截图中可见边缘、纹理、渐变或色带的字段底、名字条、" +
                "分数条都必须输出一个 Image，即使纹理很弱或被文字覆盖；同一条" +
                "上的 Label、Value、Bonus 等文字必须全部放进这个 Image.children。" +
                "禁止在非视觉 Container 下直接平铺这些 Text 后省略承载 Image。" +
                "例如 GuildName 必须是 GuildNamePlate.children，LevelLabel 和 " +
                "LevelValue 必须是 LevelField.children，并把子节点 x/y 改成" +
                "相对父节点左上角的局部坐标。输出前递归检查每个 Text 的最近" +
                "视觉承载父节点和累计绝对矩形，不能只保证画面坐标正确。" +
                "对相同画面使用稳定的规范化节点命名：同一视觉结构不得在 " +
                "Plate/Field/Panel/Backdrop 等近义词之间随机切换；矩形一律按" +
                "最近整数像素取整，children 按从后到前的绘制顺序稳定输出。" +
                visualResourceInstruction +
                "正常可编辑文字保留为可见 Text；美术字也先用 Text 表达其" +
                "可见矩形和内容，再通过 textMode 标记给 C# 校正。" +
                "输出时省略所有默认字段并使用单行 JSON。Card、Row、" +
                "ListItem 等业务数据列表只保留一个无编号模板：例如 " +
                "CityCard01~04 只输出 CityCard，DefenseRow01~04 只输出 " +
                "DefenseRow；不要写 repeatCount、repeatOffset 或 variants，" +
                "名次 FirstPlace/SecondPlace/ThirdPlace 属于运行时排名数据，" +
                "只生成一个模板；不同徽标的原始大小不代表不同模板。" +
                "同一业务面板仅用颜色、阵营或状态前缀区分且子树同构时，" +
                "若每个实例的独立视觉资源也完全一致，可只保留效果图中" +
                "最先出现的模板。若正确保留项取决于颜色、阵营、选中态等" +
                "项目 Sprite 证据，先完整输出所有候选，并在每个候选根节点" +
                "写相同 runtimeTemplateGroup 和各自简短稳定的 " +
                "runtimeTemplateVariant（例如 red/green/blue）；不要写 " +
                "repeatCount、位移或 variants。Unity C# 会先匹配所有候选的" +
                "独立 Sprite，再按变体与已命中资源的一致性、资源完整度、" +
                "视觉分和原始绘制顺序确定性地只保留一个。这个候选标记只" +
                "用于实际只需一个运行时模板的同构业务实例，不得用于效果图" +
                "中必须同时常驻的不同面板、按钮或 Toggle。省略或裁剪后续" +
                "实例只是最终 Prefab 的输出投影，" +
                "不得重测、回流、拉伸、缩放、改名、改父子关系或合并保留" +
                "模板及其任何资源承载节点；保留模板必须维持它在完整效果图中" +
                "的原始绝对矩形和完整内部层级。被省略实例所在像素不得算进" +
                "弹窗或相邻背景的资源边界。不确定是否真正同构时保留该实例，" +
                "不能以破坏视觉还原为代价去重；" +
                "结构存在资源相关歧义时输出通用校正提示：连续区域可能属于" +
                "同一张图时给承载 Image 设置 mayMerge=true；mayMerge 同时表示" +
                "允许 C# 在像素证据充分时折叠该视觉节点，因此字段底、名字条、" +
                "分数条等具有独立边界的 Image 不得设置 mayMerge。背景疑似横跨父级" +
                "或完整画布时设置 mayUseFullCanvasSprite=true；可见文字明显是" +
                "书法、Logo 或带发光描边的标题美术字时仍先输出 Text，但设置 " +
                "textMode=\"ArtText\"、visualKind=\"ArtText\"，可能由多张图" +
                "叠加时再设置 mayLayer=true；数字、排名或短标签可能已经烘焙" +
                "在父图中时设置 textMode=\"PossiblyBaked\"。正常运行时文字" +
                "保持默认 Auto，只有确认必须始终可编辑且绝不能被父图吸收时" +
                "才设置 textMode=\"Editable\"。这些字段只表达视觉证据，" +
                "禁止借此猜资源名或路径。" +
                "运行时代码会循环生成。只有固定常驻的页签或装饰重复才" +
                "允许使用 repeatCount。referenceImage 可省略，Unity 会注入。" +
                "锚点推导、色块兜底、Schema 压缩、" +
                "Prefab 和代码生成全部由 C# 完成。summary 只报告看不清的" +
                "文字和无法确认的交互，不要列资源搜索结果。最终只输出一行" +
                "严格 JSON：{\"schemaJson\":\"转义后的单行UISchema JSON\"," +
                "\"summary\":\"中文摘要\"}。不要输出 Markdown、代码块或解释。";
        }

        /// <summary>
        /// 构建Codex输出结构定义。
        /// </summary>
        private static string BuildCodexOutputSchema()
        {
            return
                "{\n" +
                "  \"type\": \"object\",\n" +
                "  \"properties\": {\n" +
                "    \"schemaJson\": {\n" +
                "      \"type\": \"string\",\n" +
                "      \"description\": \"单行精简 UISchema 2.0 JSON 字符串\"\n" +
                "    },\n" +
                "    \"summary\": {\n" +
                "      \"type\": \"string\",\n" +
                "      \"description\": \"中文生成摘要\"\n" +
                "    }\n" +
                "  },\n" +
                "  \"required\": [\"schemaJson\", \"summary\"],\n" +
                "  \"additionalProperties\": false\n" +
                "}\n";
        }

        /// <summary>
        /// 构建FigmaMcpPrompt。
        /// </summary>
        private static string BuildFigmaMcpPrompt(
            string fileKey,
            string nodeId,
            string requestedPanelId)
        {
            string safePanelId = UIEffectEditorUtility.SanitizeTypeName(
                requestedPanelId);
            return
                "通过当前已授权的官方 Figma MCP 读取一个 Design 节点，" +
                "并只返回结构化结果。禁止调用 Figma REST、Web、Shell，" +
                "禁止读取或写入项目文件，禁止请求或输出任何 Token。\n\n" +
                $"fileKey：{fileKey}\n" +
                $"nodeId：{nodeId}\n" +
                $"请求 Panel ID：{safePanelId}\n\n" +
                "不要加载 figma-design-to-code，也不要调用 get_design_context。" +
                "只调用一次 get_metadata 获取 XML 节点树，再调用一次 " +
                "download_assets 导出根节点；使用 defaultFormat=PNG、defaultScale=1，" +
                "只选择该根节点的 export render URL，不要选择 raw source image URL。" +
                "不要调用 get_screenshot、get_code_connect 或任何其它 Figma 工具。\n\n" +
                "把 metadata 转换为精简 UISchema 2.0：保留 Frame 的设计" +
                "宽高、父子层级、原始 sibling 顺序和相对父节点左上角坐标。" +
                "TEXT 使用 Text，文本内容优先取 metadata 的节点名；独立执行" +
                "动作的节点使用 Button；页签、分类、模式等互斥选择项必须使用 " +
                "Toggle，并放在共同的 ToggleGroup 父节点下；" +
                "滚动容器使用 ScrollRect；简单纯色形状使用 Image/color；" +
                "其余结构使用 Container。第一版只允许 Container、Image、" +
                "Text、Button、Toggle、ToggleGroup、ScrollRect。可见的默认选中项" +
                "设置 isOn=true；默认 ToggleGroup 不允许全部取消，只有设计明确" +
                "允许时才设置 allowSwitchOff=true。整个 schema 最多保留 7 层" +
                "（根节点计一层）；" +
                "children 不得为空；如果 metadata 确实没有可拆分子节点，输出一个" +
                "覆盖设计尺寸、resource=\"figma://crop\" 的 FrameVisual Image。" +
                "连续的装饰性 Group/Frame 不要原样嵌套，必须折叠为无 children 的 " +
                "Image/Button/Toggle crop 节点，以避免 Unity 序列化深度限制。\n\n" +
                "不能由 UGUI 原生准确表达、且可以作为一个整体展示的纯视觉" +
                "子树，折叠成无 children 的 Image、Button 或 Toggle，并设置 " +
                "resource=\"figma://crop\"。Unity 会依据该节点累计后的 Frame " +
                "坐标从整张截图裁切 Sprite。不要给根节点使用 crop；不要让 " +
                "crop 节点保留 children；不要在 resource 中填写临时 URL。" +
                "可编辑文字尽量保留为 Text；如果按钮必须整体裁切才能保持视觉，" +
                "可将其作为无 children 的 Button 或 Toggle。Card、Row、ListItem 等" +
                "业务数据列表只输出一个无编号模板，不使用 repeatCount、" +
                "repeatOffset 或 variants；运行时代码负责循环生成。" +
                "业务面板仅以颜色、阵营或状态前缀区分且子树同构时，也只" +
                "保留第一个原名模板，例如 Red/Green/BlueFactionPanel 只输出 " +
                "RedFactionPanel。必须先分析完整画面，再把后续实例从最终 Schema" +
                "省略；不得因此重测、缩放、改名、改父子关系或合并保留模板。" +
                "保留模板维持完整效果图中的原始绝对矩形和全部资源承载层级，" +
                "视觉还原优先；不能确认同构时就保留。只有固定" +
                "常驻的页签或装饰重复才允许 repeatCount。\n\n" +
                "frameName 返回 Figma 根节点原名；metadata 未提供的颜色和字体" +
                "属性不要臆测，复杂视觉统一使用 crop。两个 MCP 工具都成功且 " +
                "download_assets 确实返回根节点 export render 的 HTTPS URL 时，" +
                "mcpSucceeded 才能为 true，mcpError 为空；否则 mcpSucceeded=false，" +
                "mcpError 写原始失败原因，schemaJson 和 referenceImageUrl 都返回空串，" +
                "严禁编造 URL 或 Schema。referenceImageUrl 只返回直接 HTTPS URL，" +
                "不要返回 Markdown、curl 命令或 base64。schemaJson 必须是单行 JSON 字符串。" +
                "summary 只写需要人工复核的兼容性问题。最终只输出一行严格" +
                "JSON，字段只能是 mcpSucceeded、mcpError、frameName、schemaJson、" +
                "referenceImageUrl、summary；不要输出 Markdown、代码块或解释。";
        }

        /// <summary>
        /// 构建FigmaMcp输出结构定义。
        /// </summary>
        private static string BuildFigmaMcpOutputSchema()
        {
            return
                "{\n" +
                "  \"type\": \"object\",\n" +
                "  \"properties\": {\n" +
                "    \"mcpSucceeded\": {\"type\": \"boolean\"},\n" +
                "    \"mcpError\": {\"type\": \"string\"},\n" +
                "    \"frameName\": {\"type\": \"string\"},\n" +
                "    \"schemaJson\": {\"type\": \"string\"},\n" +
                "    \"referenceImageUrl\": {\"type\": \"string\"},\n" +
                "    \"summary\": {\"type\": \"string\"}\n" +
                "  },\n" +
                "  \"required\": [\"mcpSucceeded\", \"mcpError\", " +
                "\"frameName\", \"schemaJson\", \"referenceImageUrl\", " +
                "\"summary\"],\n" +
                "  \"additionalProperties\": false\n" +
                "}\n";
        }

        /// <summary>
        /// 获取Short错误。
        /// </summary>
        private static string GetShortError(string value)
        {
            string text = (value ?? string.Empty).Trim();
            const int maximumLength = 1200;
            return text.Length <= maximumLength
                ? text
                : "…" + text.Substring(text.Length - maximumLength);
        }

        /// <summary>
        /// 解析GeneratedAi结构定义。
        /// </summary>
        private static UIEffectSchema ParseGeneratedAiSchema(
            string schemaJson,
            string source)
        {
            UIEffectSchema schema = UIEffectSchemaUtility.ParseGenerated(
                NormalizeAiJson(schemaJson),
                out List<string> repairs);
            UIEffectTypography.RemoveUnevidencedAiFonts(schema.children, repairs);
            var modalExtractionNotes = new List<string>();
            int excludedUnderlyingNodes =
                UIEffectSchemaUtility.ExtractModalForeground(
                    schema,
                    modalExtractionNotes);
            if (repairs.Count > 0)
            {
                Debug.LogWarning(
                    "[UIPrefabGenerator/AI Schema Repair] " + source +
                    " 返回了可安全归一化的字段值，已继续生成：\n" +
                    string.Join("\n", repairs.Take(20)) +
                    (repairs.Count > 20
                        ? "\n…另有 " + (repairs.Count - 20) + " 项"
                        : string.Empty));
            }

            if (excludedUnderlyingNodes > 0)
            {
                Debug.Log(
                    "[UIPrefabGenerator/Modal Foreground] " + source +
                    " 被识别为模态弹窗；已从 Schema 排除底层界面节点 " +
                    excludedUnderlyingNodes + " 个。只保留遮罩与弹窗内容：\n" +
                    string.Join("\n", modalExtractionNotes));
            }

            return schema;
        }

        /// <summary>
        /// 尝试ExtractSafeHttps地址，并返回是否成功。
        /// </summary>
        private static bool TryExtractSafeHttpsUrl(
            string value,
            out Uri uri)
        {
            uri = null;
            string text = (value ?? string.Empty)
                .Trim()
                .Replace("\\/", "/");
            if (TryCreateSafeHttpsUri(text, out uri))
            {
                return true;
            }

            int searchIndex = 0;
            while (searchIndex < text.Length)
            {
                int start = text.IndexOf(
                    "https://",
                    searchIndex,
                    StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    return false;
                }

                int end = start;
                while (end < text.Length &&
                       !IsWrappedUrlTerminator(text[end]))
                {
                    end++;
                }

                string candidate = text.Substring(start, end - start)
                    .TrimEnd('.', ',', ';');
                if (TryCreateSafeHttpsUri(candidate, out uri))
                {
                    return true;
                }

                searchIndex = start + "https://".Length;
            }

            return false;
        }

        /// <summary>
        /// 尝试创建SafeHttpsUri，并返回是否成功。
        /// </summary>
        private static bool TryCreateSafeHttpsUri(
            string value,
            out Uri uri)
        {
            if (Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out Uri candidate) &&
                string.Equals(
                    candidate.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) &&
                !candidate.IsLoopback &&
                !string.IsNullOrWhiteSpace(candidate.Host))
            {
                uri = candidate;
                return true;
            }

            uri = null;
            return false;
        }

        /// <summary>
        /// 执行判断是否WrappedUrlTerminator相关逻辑。
        /// </summary>
        private static bool IsWrappedUrlTerminator(char character)
        {
            return char.IsWhiteSpace(character) ||
                   character == '"' ||
                   character == '\'' ||
                   character == '<' ||
                   character == '>' ||
                   character == ')' ||
                   character == ']' ||
                   character == '}';
        }

        /// <summary>
        /// 执行LooksLikeMcpFailure文本相关逻辑。
        /// </summary>
        private static bool LooksLikeMcpFailureText(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            string[] indicators =
            {
                "no figma mcp",
                "figma mcp tool is not available",
                "figma mcp tool is unavailable",
                "get_screenshot is not available",
                "download_assets is not available",
                "could not call",
                "unable to call",
                "needs authentication",
                "authentication required",
                "not authenticated",
            };
            return indicators.Any(indicator =>
                text.IndexOf(
                    indicator,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 执行DescribeReference图片值相关逻辑。
        /// </summary>
        private static string DescribeReferenceImageValue(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.StartsWith(
                    "data:image/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "内联图片/base64";
            }

            if (LooksLikeMcpFailureText(text))
            {
                return "MCP 错误文本";
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out Uri uri))
            {
                return uri.Scheme + " URI";
            }

            return text.IndexOf(
                       "https://",
                       StringComparison.OrdinalIgnoreCase) >= 0
                ? "格式异常的 HTTPS 文本"
                : "非 URL 文本";
        }

        /// <summary>
        /// 执行规范化AiJson相关逻辑。
        /// </summary>
        private static string NormalizeAiJson(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                int firstLineEnd = text.IndexOf('\n');
                if (firstLineEnd >= 0)
                {
                    text = text.Substring(firstLineEnd + 1).Trim();
                }

                if (text.EndsWith("```", StringComparison.Ordinal))
                {
                    text = text.Substring(0, text.Length - 3).Trim();
                }
            }

            int firstObject = text.IndexOf('{');
            if (firstObject >= 0)
            {
                bool inString = false;
                bool escaped = false;
                int depth = 0;
                for (int index = firstObject; index < text.Length; index++)
                {
                    char character = text[index];
                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (character == '\\')
                        {
                            escaped = true;
                        }
                        else if (character == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    if (character == '"')
                    {
                        inString = true;
                    }
                    else if (character == '{')
                    {
                        depth++;
                    }
                    else if (character == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return text.Substring(
                                firstObject,
                                index - firstObject + 1);
                        }
                    }
                }
            }

            return text;
        }

        /// <summary>
        /// 解析本地Ai响应。
        /// </summary>
        private static UIEffectCodexResponse ParseLocalAiResponse(
            string rawOutput)
        {
            string json = NormalizeAiJson(rawOutput);
            ThrowIfPlanModeResponse(json);
            try
            {
                UIEffectCodexResponse response =
                    JsonUtility.FromJson<UIEffectCodexResponse>(json);
                if (response != null &&
                    !string.IsNullOrWhiteSpace(response.schemaJson))
                {
                    return response;
                }
            }
            catch (ArgumentException)
            {
                // Keep compatibility with direct UISchema output.
            }

            if (json.IndexOf("\"version\"", StringComparison.Ordinal) >= 0 &&
                json.IndexOf("\"children\"", StringComparison.Ordinal) >= 0)
            {
                return new UIEffectCodexResponse
                {
                    schemaJson = json,
                    summary = string.Empty,
                };
            }

            throw new InvalidOperationException(
                "AI 返回内容不是有效的 UISchema 响应。内容开头：" +
                GetShortError(json));
        }

        /// <summary>
        /// 解析FigmaAi响应。
        /// </summary>
        private static UIEffectFigmaMcpResponse ParseFigmaAiResponse(
            string rawOutput)
        {
            string json = NormalizeAiJson(rawOutput);
            ThrowIfPlanModeResponse(json);
            try
            {
                UIEffectFigmaMcpResponse response =
                    JsonUtility.FromJson<UIEffectFigmaMcpResponse>(json);
                if (response != null)
                {
                    return response;
                }
            }
            catch (ArgumentException)
            {
                // Convert the low-level parser error into a useful message.
            }

            throw new InvalidOperationException(
                "AI 返回内容不是有效的 Figma 响应。内容开头：" +
                GetShortError(json));
        }

        /// <summary>
        /// 执行ThrowIfPlanModeResponse相关逻辑。
        /// </summary>
        private static void ThrowIfPlanModeResponse(string value)
        {
            string text = value ?? string.Empty;
            if (text.IndexOf(
                    "计划模式",
                    StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf(
                    "plan mode",
                    StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf(
                    "ExitPlanMode",
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            throw new InvalidOperationException(
                "AI CLI 被 Plan Mode 拦截，未生成 UISchema。" +
                "后台分析必须使用非交互的只读模式；" +
                "请确认未在自定义 CLI 包装命令中强制 plan 后重试。" +
                "返回内容开头：" + GetShortError(text));
        }

        /// <summary>
        /// 执行DeleteCodex输出文件相关逻辑。
        /// </summary>
        private void DeleteCodexOutputFile(bool keepImageInputs = false)
        {
            string path = pendingCodexOutputPath;
            pendingCodexOutputPath = string.Empty;
            if (!keepImageInputs) DeleteCodexImageInputs();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    ".."));
                string temporaryRoot = Path.GetFullPath(Path.Combine(
                    projectRoot,
                    "Library",
                    "LxyUIEffectGenerator",
                    "UIEffectCodex"));
                string fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(
                        temporaryRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                File.Delete(fullPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "无法清理 Codex 临时输出：" + exception.Message);
            }
        }

        /// <summary>
        /// 执行DeleteCodex图片Inputs相关逻辑。
        /// </summary>
        private void DeleteCodexImageInputs()
        {
            if (pendingCodexImageInputs.Count == 0)
            {
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            string temporaryRoot = Path.GetFullPath(Path.Combine(
                projectRoot,
                "Library",
                "LxyUIEffectGenerator",
                "UIEffectCodex"));
            foreach (string path in pendingCodexImageInputs)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (fullPath.StartsWith(
                            temporaryRoot + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "无法清理 Codex 视觉输入副本：" +
                        exception.Message);
                }
            }

            pendingCodexImageInputs.Clear();
        }

        /// <summary>
        /// 执行BeginFigmaImportAndGenerate相关逻辑。
        /// </summary>
        private void BeginFigmaImportAndGenerate()
        {
            if (!CheckTextEnvironment()) return;
            if (!UIEffectFigmaImporter.TryParseNodeUrl(
                    sourceUrl,
                    out string fileKey,
                    out string nodeId,
                    out string parseError))
            {
                requestStatus = parseError;
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            string temporaryFolder = Path.Combine(
                projectRoot,
                "Library",
                "LxyUIEffectGenerator",
                "UIEffectCodex");
            string outputSchemaPath = Path.Combine(
                temporaryFolder,
                "FigmaMcpResponse.schema.json");
            string outputPath = Path.Combine(
                temporaryFolder,
                "Figma_" + Guid.NewGuid().ToString("N") + ".json");

            try
            {
                Directory.CreateDirectory(temporaryFolder);
                File.WriteAllText(
                    outputSchemaPath,
                    BuildFigmaMcpOutputSchema(),
                    new System.Text.UTF8Encoding(false));

                string selectedCommand = codexCommand;
                if (string.IsNullOrWhiteSpace(selectedCommand))
                {
                    selectedCommand = "codex";
                }

                codexCommand = selectedCommand;
                EditorPrefs.SetString(
                    CodexCommandEditorPrefsKey,
                    codexCommand);

                pendingFigmaRequestedPanelId = panelId;
                pendingCodexOutputPath = outputPath;
                pendingCodexTask = CodexTaskKind.FigmaMcp;
                lastCodexUsage = null;

                UIEffectGenerationTiming.BeginWorkflow(GetSafePanelId());
                UIEffectGenerationTiming.BeginStage("Figma 节点读取");

                codexRunner ??= new UIEffectCodexRunner();
                codexRunner.Start(new UIEffectCodexRequest
                {
                    provider = "Codex",
                    codexCommand = selectedCommand,
                    projectRoot = projectRoot,
                    requiresFigmaMcp = true,
                    outputSchemaPath = outputSchemaPath,
                    outputPath = outputPath,
                    prompt = BuildFigmaMcpPrompt(
                        fileKey,
                        nodeId,
                        pendingFigmaRequestedPanelId),
                });
                requestStatus =
                    "正在通过 Codex 读取 Figma MCP Frame…";
            }
            catch (Exception exception)
            {
                pendingCodexTask = CodexTaskKind.None;
                requestStatus =
                    "无法启动 Figma MCP 读取：" + exception.Message;
                Debug.LogException(exception);
                DeleteCodexOutputFile();
            }
        }

        /// <summary>
        /// 执行ImportLocalReference相关逻辑。
        /// </summary>
        private void ImportLocalReference()
        {
            string path = EditorUtility.OpenFilePanel(
                "选择 UI 效果图",
                string.Empty,
                "png,jpg,jpeg");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                SaveReferenceImage(File.ReadAllBytes(path), "Local");
            }
            catch (Exception exception)
            {
                requestStatus = "导入效果图失败：" + exception.Message;
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 处理FigmaMcp结果。
        /// </summary>
        private void HandleFigmaMcpResult(
            UIEffectCodexRunResult result)
        {
            try
            {
                if (result == null)
                {
                    throw new InvalidOperationException(
                        "Codex 没有返回 Figma MCP 运行结果。");
                }

                lastCodexUsage = result.usage;
                if (result.canceled)
                {
                    requestStatus = "已取消 Figma MCP 读取。";
                    return;
                }

                if (!result.succeeded)
                {
                    string detail = GetShortError(result.error);
                    requestStatus =
                        $"Figma MCP 读取失败（退出码 {result.exitCode}）" +
                        (detail.Length == 0 ? "。" : "：" + detail);
                    Debug.LogError(requestStatus);
                    return;
                }

                UIEffectFigmaMcpResponse response =
                    ParseFigmaAiResponse(result.outputJson);
                if (response == null)
                {
                    throw new InvalidOperationException(
                        "AI 没有返回 Figma MCP 结构化结果。");
                }

                if (!response.mcpSucceeded ||
                    LooksLikeMcpFailureText(response.referenceImageUrl))
                {
                    string detail = string.IsNullOrWhiteSpace(response.mcpError)
                        ? response.summary
                        : response.mcpError;
                    const string setup =
                        "请执行 codex mcp login figma 完成 OAuth 后重试。";
                    throw new InvalidOperationException(
                        "Codex 未实际完成 Figma MCP 调用。" +
                        setup +
                        (string.IsNullOrWhiteSpace(detail)
                            ? string.Empty
                            : "\nMCP 详情：" + GetShortError(detail)));
                }

                if (string.IsNullOrWhiteSpace(response.schemaJson) ||
                    string.IsNullOrWhiteSpace(
                        response.referenceImageUrl))
                {
                    var missingFields = new List<string>();
                    if (string.IsNullOrWhiteSpace(response.schemaJson))
                    {
                        missingFields.Add("schemaJson");
                    }

                    if (string.IsNullOrWhiteSpace(
                            response.referenceImageUrl))
                    {
                        missingFields.Add("referenceImageUrl");
                    }

                    string summary = string.IsNullOrWhiteSpace(response.summary)
                        ? string.Empty
                        : "\nAI 摘要：" + GetShortError(response.summary);
                    throw new InvalidOperationException(
                        "Figma MCP 返回结果缺少：" +
                        string.Join(", ", missingFields) + "。" + summary);
                }

                if (!TryExtractSafeHttpsUrl(
                        response.referenceImageUrl,
                        out Uri screenshotUri))
                {
                    throw new InvalidOperationException(
                        "Figma MCP 没有返回可下载的 HTTPS 导出地址" +
                        "（收到：" + DescribeReferenceImageValue(
                            response.referenceImageUrl) + "）。" +
                        "生成器已要求使用 download_assets 的根节点 export render；" +
                        "请检查 MCP 授权后重试。");
                }

                UIEffectSchema schema = ParseGeneratedAiSchema(
                    response.schemaJson,
                    "Figma MCP");
                int schemaDepth = UIEffectFigmaImporter.GetMaxDepth(schema);
                if (schemaDepth > 7)
                {
                    throw new InvalidOperationException(
                        "Figma MCP 返回的 UISchema 层级为 " + schemaDepth +
                        "，超过 Unity 安全上限 7。请重试，或让装饰性子树折叠为 crop。" );
                }
                string resolvedPanelId =
                    UIEffectFigmaImporter.ResolvePanelId(
                        pendingFigmaRequestedPanelId,
                        response.frameName);
                schema.name = resolvedPanelId;
                UIEffectSchemaUtility.Validate(schema);

                string schemaPath =
                    $"{SchemaFolder}/{resolvedPanelId}.json";
                if (File.Exists(
                        UIEffectEditorUtility.ToAbsolutePath(schemaPath)) &&
                    !EditorUtility.DisplayDialog(
                        "更新 UISchema",
                        schemaPath +
                        " 已存在。是否允许 Figma 导入覆盖它并重新生成 Prefab？",
                        "允许更新",
                        "取消"))
                {
                    requestStatus =
                        "已取消 Figma 导入，没有修改 UISchema 或 Prefab。";
                    return;
                }

                string previousPanelId = panelId;
                panelId = resolvedPanelId;
                if (string.IsNullOrWhiteSpace(logicClassName) ||
                    string.Equals(
                        logicClassName,
                        UIEffectEditorUtility.SanitizeTypeName(previousPanelId),
                        StringComparison.Ordinal))
                {
                    logicClassName = resolvedPanelId;
                }

                string referenceAssetPath =
                    UIEffectFigmaImporter.GetReferenceAssetPath(
                        resolvedPanelId);
                schema.referenceImage = referenceAssetPath;
                requestStatus =
                    "Figma MCP 读取完成，正在下载临时 Frame 截图…";
                BeginFigmaScreenshotDownload(
                    screenshotUri.AbsoluteUri,
                    bytes => CompleteFigmaMcpImport(
                        bytes,
                        schema,
                        schemaPath,
                        referenceAssetPath,
                        resolvedPanelId,
                        response.summary));
            }
            catch (Exception exception)
            {
                requestStatus =
                    "处理 Figma MCP 结果失败：" + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                DeleteCodexOutputFile();
            }
        }

        /// <summary>
        /// 执行CompleteFigmaMcpImport相关逻辑。
        /// </summary>
        private void CompleteFigmaMcpImport(
            byte[] screenshotBytes,
            UIEffectSchema schema,
            string schemaPath,
            string referenceAssetPath,
            string resolvedPanelId,
            string summary)
        {
            var screenshot = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);
            try
            {
                if (!screenshot.LoadImage(screenshotBytes, false))
                {
                    throw new InvalidOperationException(
                        "Figma MCP 截图不是有效的 PNG/JPG 图片。");
                }

                bool usedFullFrameFallback =
                    UIEffectFigmaImporter.EnsureMinimumVisualNode(schema);
                int inferredCropNodeCount =
                    UIEffectFigmaImporter.MarkImplicitLeafCropNodes(schema);
                IReadOnlyList<UIEffectFigmaCropAsset> cropAssets =
                    UIEffectFigmaImporter.CreateCropAssets(
                        schema,
                        screenshot,
                        resolvedPanelId);
                WriteImportedImage(
                    referenceAssetPath,
                    screenshot.EncodeToPNG(),
                    false);
                schema.referenceImageHash =
                    ComputeReferenceImageHash(referenceAssetPath);
                referenceImage =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        referenceAssetPath);

                foreach (UIEffectFigmaCropAsset asset in cropAssets)
                {
                    WriteImportedImage(
                        asset.RuntimeAssetPath,
                        asset.PngBytes,
                        true);
                }

                EnsureAssetFolder(SchemaFolder);
                int collapsedNodes =
                    UIEffectSchemaUtility.OptimizeRepeatedNodes(schema);
                File.WriteAllText(
                    UIEffectEditorUtility.ToAbsolutePath(schemaPath),
                    UIEffectSchemaUtility.ToCompactJson(schema),
                    new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(
                    schemaPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                schemaAsset =
                    AssetDatabase.LoadAssetAtPath<TextAsset>(schemaPath);
                Selection.activeObject = schemaAsset;

                string detail = string.IsNullOrWhiteSpace(summary)
                    ? "无"
                    : summary.Trim();
                Debug.Log(
                    "[UIPrefabGenerator/Figma MCP]" +
                    $"\nUISchema：{schemaPath}" +
                    $"\n裁切资源：{cropAssets.Count}" +
                    $"\n自动推断裁切节点：{inferredCropNodeCount}" +
                    "\n空 Schema 兜底：" +
                    (usedFullFrameFallback ? "FrameVisual" : "未触发") +
                    $"\n合并重复节点：{collapsedNodes}" +
                    "\n兼容性提示：" + detail,
                    schemaAsset);

                requestStatus =
                    $"Figma 已生成 UISchema：{schemaPath}，正在生成 Prefab…";
                GeneratePrefab();
            }
            finally
            {
                DestroyImmediate(screenshot);
            }
        }

        /// <summary>
        /// 执行写入Imported图片相关逻辑。
        /// </summary>
        private static void WriteImportedImage(
            string assetPath,
            byte[] bytes,
            bool asSprite)
        {
            string folder = Path.GetDirectoryName(assetPath)
                ?.Replace('\\', '/');
            EnsureAssetFolder(folder);
            WriteBytesIfChanged(
                UIEffectEditorUtility.ToAbsolutePath(assetPath),
                bytes);
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            if (asSprite)
            {
                ConfigureFigmaSpriteImporter(assetPath);
            }
            else
            {
                ConfigureReferenceImageImporter(assetPath);
            }
        }

        /// <summary>
        /// 执行写入BytesIfChanged相关逻辑。
        /// </summary>
        private static void WriteBytesIfChanged(
            string absolutePath,
            byte[] bytes)
        {
            bytes ??= Array.Empty<byte>();
            if (File.Exists(absolutePath))
            {
                byte[] existing = File.ReadAllBytes(absolutePath);
                if (existing.SequenceEqual(bytes))
                {
                    return;
                }
            }

            File.WriteAllBytes(absolutePath, bytes);
        }

        /// <summary>
        /// 执行ComputeReference图片哈希相关逻辑。
        /// </summary>
        private static string ComputeReferenceImageHash(
            string assetPath)
        {
            string absolutePath =
                UIEffectEditorUtility.ToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    "找不到效果图文件。",
                    absolutePath);
            }

            using (FileStream stream = File.OpenRead(absolutePath))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(stream);
                return "sha256:" + BitConverter.ToString(digest)
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        /// <summary>
        /// 执行规范化项目资源路径相关逻辑。
        /// </summary>
        private static string NormalizeProjectAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// 执行BeginFigmaScreenshot下载相关逻辑。
        /// </summary>
        private void BeginFigmaScreenshotDownload(
            string url,
            Action<byte[]> onSuccess)
        {
            BeginNodeScreenshotDownload(url, onSuccess);
        }

        /// <summary>
        /// 执行Begin节点Screenshot下载相关逻辑。
        /// </summary>
        private void BeginNodeScreenshotDownload(
            string url,
            Action<byte[]> onSuccess)
        {
            CancelNodeDownload();
            string projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            string folder = Path.Combine(
                projectRoot,
                "Library",
                "LxyUIEffectGenerator",
                "UIEffectCodex");
            string outputPath = Path.Combine(
                folder,
                "FigmaScreenshot_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Directory.CreateDirectory(folder);
                string node = UIEffectCodexRunner.ResolveExecutable("node");
                const string script =
                    "const fs=require('fs'),https=require('https')," +
                    "u=require('url');" +
                    "function get(target,depth){" +
                    "if(depth>5) throw new Error('too many redirects');" +
                    "const req=https.get(target,{rejectUnauthorized:false," +
                    "headers:{'User-Agent':'Mozilla/5.0'}},res=>{" +
                    "if(res.statusCode>=300&&res.statusCode<400&&res.headers.location){" +
                    "res.resume();return get(new u.URL(res.headers.location,target),depth+1);}" +
                    "if(res.statusCode<200||res.statusCode>=300){res.resume();" +
                    "throw new Error('HTTP '+res.statusCode);}" +
                    "const out=fs.createWriteStream(process.argv[2]);" +
                    "res.pipe(out);out.on('finish',()=>out.close());" +
                    "});req.setTimeout(30000,()=>req.destroy(new Error('timeout')));" +
                    "req.on('error',e=>{console.error(e.stack||e);process.exit(1);});}" +
                    "try{get(process.argv[1],0);}catch(e){console.error(e.stack||e);" +
                    "process.exit(1);}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = node,
                    Arguments = "-e " + QuoteProcessArgument(script) +
                                " " + QuoteProcessArgument(url) +
                                " " + QuoteProcessArgument(outputPath),
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                figmaNodeDownloadProcess = Process.Start(startInfo);
                if (figmaNodeDownloadProcess == null)
                {
                    throw new InvalidOperationException(
                        "无法启动 Node 下载进程。");
                }

                figmaNodeDownloadPath = outputPath;
                figmaNodeDownloadSuccess = onSuccess;
                requestInProgress = true;
                requestStatus = "正在使用 Node HTTPS 下载 Figma 截图…";
                EditorApplication.update += PollNodeScreenshotDownload;
            }
            catch (Exception exception)
            {
                requestInProgress = false;
                requestStatus = "Node 下载 Figma 截图失败：" + exception.Message;
                Debug.LogError("[UIPrefabGenerator/Figma] " + requestStatus);
                CancelNodeDownload();
            }
        }

        /// <summary>
        /// 执行Poll节点Screenshot下载相关逻辑。
        /// </summary>
        private void PollNodeScreenshotDownload()
        {
            Process process = figmaNodeDownloadProcess;
            if (process == null || !process.HasExited)
            {
                return;
            }

            EditorApplication.update -= PollNodeScreenshotDownload;
            string outputPath = figmaNodeDownloadPath;
            Action<byte[]> onSuccess = figmaNodeDownloadSuccess;
            string error = string.Empty;
            try
            {
                error = process.StandardError.ReadToEnd();
                int exitCode = process.ExitCode;
                if (exitCode != 0 || !File.Exists(outputPath))
                {
                    requestStatus = "Node 下载 Figma 截图失败（退出码 " +
                                    exitCode + "）：" + GetShortError(error);
                    Debug.LogError("[UIPrefabGenerator/Figma] " + requestStatus);
                    return;
                }

                byte[] bytes = File.ReadAllBytes(outputPath);
                if (bytes.Length == 0)
                {
                    requestStatus = "Node 下载的 Figma 截图为空。";
                    Debug.LogError("[UIPrefabGenerator/Figma] " + requestStatus);
                    return;
                }

                requestStatus = "Figma 截图下载完成，正在生成 Prefab…";
                onSuccess?.Invoke(bytes);
            }
            catch (Exception exception)
            {
                requestStatus = "处理 Node 下载结果失败：" + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                requestInProgress = false;
                try { process.Dispose(); } catch { }
                figmaNodeDownloadProcess = null;
                figmaNodeDownloadSuccess = null;
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                }
                figmaNodeDownloadPath = string.Empty;
                Repaint();
            }
        }

        /// <summary>
        /// 取消Node下载。
        /// </summary>
        private void CancelNodeDownload()
        {
            EditorApplication.update -= PollNodeScreenshotDownload;
            Process process = figmaNodeDownloadProcess;
            figmaNodeDownloadProcess = null;
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.Dispose(); } catch { }
            }

            if (!string.IsNullOrWhiteSpace(figmaNodeDownloadPath) &&
                File.Exists(figmaNodeDownloadPath))
            {
                try { File.Delete(figmaNodeDownloadPath); } catch { }
            }

            figmaNodeDownloadPath = string.Empty;
            figmaNodeDownloadSuccess = null;
        }

        /// <summary>
        /// 执行引用流程参数相关逻辑。
        /// </summary>
        private static string QuoteProcessArgument(string value)
        {
            value ??= string.Empty;
            if (value.Length > 0 &&
                value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0)
            {
                return value;
            }

            var builder = new System.Text.StringBuilder();
            builder.Append('"');
            int backslashCount = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append('"');
                    backslashCount = 0;
                    continue;
                }

                builder.Append('\\', backslashCount);
                backslashCount = 0;
                builder.Append(character);
            }

            builder.Append('\\', backslashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// 保存引用图片。
        /// </summary>
        private void SaveReferenceImage(
            byte[] sourceBytes,
            string sourceName)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(sourceBytes, false))
                {
                    throw new InvalidOperationException(
                        "返回内容不是可识别的 PNG/JPG 图片。" );
                }

                EnsureAssetFolder(ReferenceFolder);
                string safePanelId = GetSafePanelId();
                string assetPath =
                    $"{ReferenceFolder}/{safePanelId}_{sourceName}.png";
                WriteImportedImage(
                    assetPath,
                    texture.EncodeToPNG(),
                    false);
                referenceImage =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                requestStatus = "效果图已保存：" + assetPath;
                Selection.activeObject = referenceImage;
            }
            finally
            {
                DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// 创建Starter结构定义。
        /// </summary>
        private void CreateStarterSchema()
        {
            if (referenceImage == null)
            {
                return;
            }

            string safePanelId = GetSafePanelId();
            string imagePath =
                AssetDatabase.GetAssetPath(referenceImage);
            GetReferenceSourceSize(
                imagePath,
                referenceImage,
                out int sourceWidth,
                out int sourceHeight);
            var schema = new UIEffectSchema
            {
                name = safePanelId,
                designWidth = sourceWidth,
                designHeight = sourceHeight,
                referenceImage = imagePath,
                referenceImageHash =
                    ComputeReferenceImageHash(imagePath),
            };
            schema.children.Add(new UIEffectNode
            {
                name = "Background",
                type = "Image",
                semantic = "background",
                anchor = "StretchAll",
                x = 0f,
                y = 0f,
                width = sourceWidth,
                height = sourceHeight,
                color = "#303744FF",
                intentionalColor = true,
            });
            schema.children.Add(new UIEffectNode
            {
                name = "Title",
                type = "Text",
                semantic = "title",
                anchor = "TopCenter",
                x = sourceWidth * 0.25f,
                y = sourceHeight * 0.06f,
                width = sourceWidth * 0.5f,
                height = sourceHeight * 0.08f,
                text = "请在 UI预制体生成器窗口中分析效果图，或编辑此 UISchema",
                fontSize = Mathf.Max(24f, sourceWidth * 0.03f),
                alignment = "Center",
                color = "#FFFFFFFF",
                bold = true,
            });

            EnsureAssetFolder(SchemaFolder);
            string assetPath =
                $"{SchemaFolder}/{safePanelId}.json";
            if (File.Exists(UIEffectEditorUtility.ToAbsolutePath(assetPath)) &&
                !EditorUtility.DisplayDialog(
                    "UISchema 已存在",
                    assetPath + " 已存在，是否覆盖？",
                    "覆盖",
                    "取消"))
            {
                return;
            }

            File.WriteAllText(
                UIEffectEditorUtility.ToAbsolutePath(assetPath),
                UIEffectSchemaUtility.ToCompactJson(schema),
                new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport);
            schemaAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            Selection.activeObject = schemaAsset;
            requestStatus = "基础 UISchema 已创建：" + assetPath;
        }

        /// <summary>
        /// 创建Generation选项。
        /// </summary>
        private UIEffectPrefabGenerationOptions CreateGenerationOptions()
        {
            string resolvedPanelId = GetSafePanelId();
            return new UIEffectPrefabGenerationOptions
            {
                panelId = resolvedPanelId,
                prefabFolder = GetPrefabOutputFolder(),
                scriptType = scriptType,
                codeNamespace = codeNamespace,
                logicClassName = string.IsNullOrWhiteSpace(
                    logicClassName)
                    ? resolvedPanelId
                    : logicClassName,
                scriptFolder = scriptFolder,
                defaultFont = defaultFont,
                uiLayer = uiLayer,
                resourceMatchMode = resourceMatchMode,
                resourceSearchRoots = ParseSearchRoots(
                    resourceSearchRoots),
            };
        }

        /// <summary>
        /// 校验运行时TemplateProjection。
        /// </summary>
        private static void ValidateRuntimeTemplateProjection(
            Transform generated,
            List<UIEffectNode> schemaNodes)
        {
            if (generated == null || schemaNodes == null)
            {
                return;
            }

            string[] prefabNames = generated
                .GetComponentsInChildren<Transform>(true)
                .Select(item => item.name)
                .ToArray();
            ValidateRuntimeTemplateProjection(schemaNodes, prefabNames);
        }

        /// <summary>
        /// 校验运行时TemplateProjection。
        /// </summary>
        private static void ValidateRuntimeTemplateProjection(
            List<UIEffectNode> schemaNodes,
            string[] prefabNames)
        {
            List<IGrouping<string, UIEffectNode>> groups = schemaNodes
                .Where(node =>
                    node != null &&
                    !string.IsNullOrWhiteSpace(
                        node.runtimeTemplateGroup))
                .GroupBy(
                    node => node.runtimeTemplateGroup.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToList();
            foreach (IGrouping<string, UIEffectNode> group in groups)
            {
                HashSet<string> candidateNames = new HashSet<string>(
                    group.Select(node => node.name),
                    StringComparer.OrdinalIgnoreCase);
                int projectedCount = prefabNames.Count(prefabName =>
                    candidateNames.Contains(prefabName) ||
                    candidateNames.Any(candidateName =>
                        prefabName.EndsWith(
                            "_" + candidateName,
                            StringComparison.OrdinalIgnoreCase)));
                if (projectedCount > 1)
                {
                    throw new InvalidOperationException(
                        $"运行时模板组 {group.Key} 在 Prefab 中仍有 " +
                        $"{projectedCount} 个候选：" +
                        string.Join(
                            ", ",
                            group.Select(node => node.name)) +
                        "。完整还原不能把同构动态 Item 全部固化。");
                }
            }

            foreach (UIEffectNode node in schemaNodes)
            {
                if (node != null)
                {
                    ValidateRuntimeTemplateProjection(
                        node.children,
                        prefabNames);
                }
            }
        }

        /// <summary>
        /// 执行Generate预制体相关逻辑。
        /// </summary>
        private void GeneratePrefab()
        {
            try
            {
                UIEffectPrefabGenerationResult result =
                    UIEffectPrefabBuilder.Generate(
                        schemaAsset.text,
                        pendingGenerationOptions ?? CreateGenerationOptions(),
                        true);
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        result.PrefabPath);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);

                string used = result.UsedResources.Count == 0
                    ? "无"
                    : string.Join("\n", result.UsedResources);
                string missing = result.MissingResources.Count == 0
                    ? "无"
                    : string.Join("\n", result.MissingResources);
                Debug.Log(
                    $"[UIPrefabGenerator] Prefab：{result.PrefabPath}\n" +
                    $"Generated：{result.GeneratedObjectCount} 个对象，" +
                    $"{result.GeneratedComponentCount} 个组件\n" +
                    $"已匹配资源：\n{used}\n" +
                    $"缺失资源：\n{missing}",
                    prefab);
                requestStatus =
                    $"生成完成：{result.PrefabPath}；" +
                    $"Generated {result.GeneratedObjectCount} 个对象/" +
                    $"{result.GeneratedComponentCount} 个组件；" +
                    $"已匹配 {result.UsedResources.Count} 个资源，" +
                    $"缺失 {result.MissingResources.Count} 个资源。";
                if (result.Warnings.Count > 0)
                    requestStatus += $"有 {result.Warnings.Count} 项资源或字体提示，请查看 Console 或还原检查报告。";
                if (!string.IsNullOrEmpty(result.FidelityReportPath))
                {
                    lastFidelityReportPath = result.FidelityReportPath;
                    requestStatus += $"视觉检查：{result.FidelityReviewCount} 个节点待复核。";
                    Debug.Log("[UIPrefabGenerator/Visual Audit] " + result.FidelityReportPath +
                        "\n报告与实际预览已保存；像素差异分数只覆盖可比较区域，不代表整图还原率。", prefab);
                }
            }
            catch (OperationCanceledException)
            {
                requestStatus = "已取消生成。";
            }
            catch (Exception exception)
            {
                requestStatus = "生成失败：" + exception.Message;
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 校验生成选中项结构定义。
        /// </summary>
        [MenuItem(
            "Assets/UI工具/根据选中的UISchema生成Prefab",
            true)]
        private static bool ValidateGenerateSelectedSchema()
        {
            return Selection.activeObject is TextAsset &&
                   AssetDatabase.GetAssetPath(
                           Selection.activeObject)
                       .EndsWith(
                           ".json",
                           StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 执行GenerateSelected结构定义相关逻辑。
        /// </summary>
        [MenuItem("Assets/UI工具/根据选中的UISchema生成Prefab")]
        private static void GenerateSelectedSchema()
        {
            string schemaPath =
                AssetDatabase.GetAssetPath(Selection.activeObject);
            AssetDatabase.ImportAsset(
                schemaPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            string schemaJson = File.ReadAllText(
                UIEffectEditorUtility.ToAbsolutePath(schemaPath),
                System.Text.Encoding.UTF8);
            UIEffectSchema schema =
                UIEffectSchemaUtility.Parse(schemaJson);
            UIEffectPrefabGenerationResult result =
                UIEffectPrefabBuilder.Generate(
                    schemaJson,
                    new UIEffectPrefabGenerationOptions
                    {
                        panelId = schema.name,
                        logicClassName =
                            UIEffectEditorUtility.SanitizeTypeName(
                                schema.name),
                    },
                    true);
            Selection.activeObject =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    result.PrefabPath);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        /// <summary>
        /// 校验Optimize选中项结构定义。
        /// </summary>
        [MenuItem("Assets/UI工具/压缩选中的UISchema（低Token）", true)]
        private static bool ValidateOptimizeSelectedSchema()
        {
            return ValidateGenerateSelectedSchema();
        }

        /// <summary>
        /// 执行优化Selected结构定义相关逻辑。
        /// </summary>
        [MenuItem("Assets/UI工具/压缩选中的UISchema（低Token）")]
        private static void OptimizeSelectedSchema()
        {
            string assetPath =
                AssetDatabase.GetAssetPath(Selection.activeObject);
            OptimizeSchemaAsset(assetPath, out int collapsedNodes);
            Debug.Log(
                $"[UIPrefabGenerator] 已压缩 {assetPath}，" +
                $"合并重复节点 {collapsedNodes} 个。",
                Selection.activeObject);
        }

        /// <summary>
        /// 执行优化结构定义资源相关逻辑。
        /// </summary>
        private static void OptimizeSchemaAsset(
            string assetPath,
            out int collapsedNodes)
        {
            TextAsset asset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"找不到 UISchema：{assetPath}");
            }

            UIEffectSchema schema =
                UIEffectSchemaUtility.Parse(asset.text);
            collapsedNodes =
                UIEffectSchemaUtility.OptimizeRepeatedNodes(schema);
            File.WriteAllText(
                UIEffectEditorUtility.ToAbsolutePath(assetPath),
                UIEffectSchemaUtility.ToCompactJson(schema),
                new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private void LoadPrefabFolderPreference()
        {
            string savedPath = AssetDatabase.GUIDToAssetPath(
                EditorPrefs.GetString(PrefabFolderPreferenceKey, string.Empty));
            if (IsPrefabOutputFolder(savedPath)) prefabFolder = savedPath;
            prefabOutputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(prefabFolder);
        }

        private static bool IsPrefabOutputFolder(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) &&
                   AssetDatabase.IsValidFolder(path);
        }

        private string GetPrefabOutputFolder()
        {
            if (prefabOutputFolder != null)
                prefabFolder = AssetDatabase.GetAssetPath(prefabOutputFolder);
            return prefabFolder;
        }

        private void DrawPrefabFolder()
        {
            if (prefabOutputFolder == null && IsPrefabOutputFolder(prefabFolder))
                prefabOutputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(prefabFolder);
            using (new EditorGUI.DisabledScope(IsBusy))
            {
                EditorGUI.BeginChangeCheck();
                var selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    new GUIContent("Prefab 目录", "拖入或选择 Assets 下的输出文件夹；清空后恢复项目默认目录。"),
                    prefabOutputFolder, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck()) SetPrefabOutputFolder(selectedFolder);
            }
            EditorGUILayout.LabelField("输出路径", GetPrefabOutputFolder(), EditorStyles.miniLabel);
            if (prefabOutputFolder == null)
                EditorGUILayout.LabelField("目录尚不存在，将在生成时自动创建。", EditorStyles.miniLabel);
        }

        private void SetPrefabOutputFolder(DefaultAsset folder)
        {
            if (folder == null)
            {
                prefabFolder = GetProjectDefaults().prefabFolder;
                prefabOutputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(prefabFolder);
                EditorPrefs.DeleteKey(PrefabFolderPreferenceKey);
                return;
            }

            string path = AssetDatabase.GetAssetPath(folder);
            if (!IsPrefabOutputFolder(path))
            {
                requestStatus = "Prefab 目录必须是当前项目 Assets 下的文件夹。";
                return;
            }

            prefabOutputFolder = folder;
            prefabFolder = path;
            EditorPrefs.SetString(PrefabFolderPreferenceKey, AssetDatabase.AssetPathToGUID(path));
        }

        /// <summary>
        /// 设置资源根节点目录。
        /// </summary>
        private void SetResourceRootFolder(DefaultAsset folder)
        {
            if (folder == null)
            {
                resourceRootFolder = null;
                resourceSearchRoots = string.Empty;
                EditorPrefs.DeleteKey(ResourceRootGuidEditorPrefsKey);
                UIEffectResourceResolver.ClearCaches();
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(folder)
                .Replace('\\', '/')
                .TrimEnd('/');
            if (!assetPath.StartsWith(
                    "Assets/",
                    StringComparison.OrdinalIgnoreCase) ||
                !AssetDatabase.IsValidFolder(assetPath))
            {
                requestStatus =
                    "资源总目录必须是当前项目 Assets 下的文件夹。";
                return;
            }

            resourceRootFolder = folder;
            resourceSearchRoots = assetPath;
            EditorPrefs.SetString(
                ResourceRootGuidEditorPrefsKey,
                AssetDatabase.AssetPathToGUID(assetPath));
            UIEffectResourceResolver.ClearCaches();
            ScheduleResourceRescan("资源目录已更改");
        }

        /// <summary>
        /// 执行调度资源Rescan相关逻辑。
        /// </summary>
        private void ScheduleResourceRescan(string reason)
        {
            if (resourceRescanScheduled ||
                resourceMatchMode !=
                UIEffectResourceMatchMode.VisualSimilarity)
            {
                return;
            }

            resourceRescanScheduled = true;
            if (!IsBusy)
            {
                requestStatus =
                    reason + "，正在刷新并扫描 Sprite 资源…";
            }
            EditorApplication.delayCall += RebuildResourceCache;
        }

        /// <summary>
        /// 执行重建资源缓存相关逻辑。
        /// </summary>
        private void RebuildResourceCache()
        {
            EditorApplication.delayCall -= RebuildResourceCache;
            resourceRescanScheduled = false;
            if (this == null)
            {
                return;
            }

            try
            {
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
                string[] roots = ParseSearchRoots(resourceSearchRoots)
                    .Where(AssetDatabase.IsValidFolder)
                    .ToArray();
                if (roots.Length == 0)
                {
                    roots = new[]
                    {
                        GetDefaultResourceSearchRoot(),
                    };
                }

                int spriteCount =
                    UIEffectResourceResolver.RebuildCaches(roots);
                if (awaitingStructure && pendingCodexTask == CodexTaskKind.FullFidelity &&
                    codexRunner != null && codexRunner.IsRunning)
                    StartNativePixelWarmup();
                if (!IsBusy)
                {
                    requestStatus =
                        $"资源索引已重新扫描：{spriteCount} 个 Sprite。";
                }
            }
            catch (Exception exception)
            {
                if (!IsBusy)
                {
                    requestStatus =
                        "重新扫描资源失败：" + exception.Message;
                }
                Debug.LogException(exception);
            }

            Repaint();
        }

        /// <summary>
        /// 获取Safe面板Id。
        /// </summary>
        private string GetSafePanelId()
        {
            string value = string.IsNullOrWhiteSpace(panelId)
                ? "UIEffectPanel"
                : panelId.Trim();
            return UIEffectEditorUtility.SanitizeTypeName(value);
        }

        /// <summary>
        /// 解析SearchRoots。
        /// </summary>
        private static string[] ParseSearchRoots(string value)
        {
            return (value ?? string.Empty)
                .Split(
                    new[] { '\r', '\n', ';', ',' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToArray();
        }

        /// <summary>
        /// 确保资源目录。
        /// </summary>
        private static void EnsureAssetFolder(string assetFolder)
        {
            string normalized = assetFolder
                .Replace('\\', '/')
                .TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] segments = normalized.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(
                        current,
                        segments[index]);
                }

                current = next;
            }
        }

        /// <summary>
        /// 执行配置Reference图片Importer相关逻辑。
        /// </summary>
        private static void ConfigureReferenceImageImporter(
            string assetPath)
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 8192;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 获取引用源数据尺寸。
        /// </summary>
        private static void GetReferenceSourceSize(
            string assetPath,
            Texture2D fallback,
            out int width,
            out int height)
        {
            width = fallback != null ? fallback.width : 0;
            height = fallback != null ? fallback.height : 0;
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.GetSourceTextureWidthAndHeight(
                    out int sourceWidth,
                    out int sourceHeight);
                if (sourceWidth > 0 && sourceHeight > 0)
                {
                    width = sourceWidth;
                    height = sourceHeight;
                }
            }

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(
                    "无法读取效果图源文件尺寸：" + assetPath);
            }
        }

        /// <summary>
        /// 执行配置Figma精灵图Importer相关逻辑。
        /// </summary>
        private static void ConfigureFigmaSpriteImporter(
            string assetPath)
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 8192;
            importer.SaveAndReimport();
        }
    }
}
#endif
