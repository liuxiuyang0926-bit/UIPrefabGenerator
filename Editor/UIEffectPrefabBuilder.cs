#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Lxy.UIEffectGenerator.Editor
{
    public enum UIEffectResourceMatchMode
    {
        NameAndSemantic,
        VisualSimilarity,
        ColorBlocks,
    }

    [Serializable]
    public sealed class UIEffectPrefabGenerationOptions
    {
        /// <summary>
        /// 公开的面板标识数据。
        /// </summary>
        public string panelId = string.Empty;
        /// <summary>
        /// 公开的prefabFolder数据。
        /// </summary>
        public string prefabFolder = string.Empty;
        /// <summary>
        /// 公开的脚本类型数据。
        /// </summary>
        public UIEffectScriptType scriptType =
            UIEffectScriptType.ProjectDefault;
        /// <summary>
        /// 公开的codeNamespace数据。
        /// </summary>
        public string codeNamespace = string.Empty;
        /// <summary>
        /// 公开的logic类型名称数据。
        /// </summary>
        public string logicClassName = string.Empty;
        /// <summary>
        /// 公开的脚本Folder数据。
        /// </summary>
        public string scriptFolder = string.Empty;
        public TMP_FontAsset defaultFont;
        /// <summary>
        /// 公开的ui层级数据。
        /// </summary>
        public UIEffectLayer uiLayer = UIEffectLayer.Auto;
        /// <summary>
        /// 公开的资源Match模式数据。
        /// </summary>
        public UIEffectResourceMatchMode resourceMatchMode =
            UIEffectResourceMatchMode.NameAndSemantic;
        /// <summary>
        /// 公开的资源SearchRoots数据。
        /// </summary>
        public string[] resourceSearchRoots = Array.Empty<string>();

        /// <summary>
        /// 本次 Generated 子树中允许绑定的对象，仅在 AfterBuildGeneratedTree 回调期间有效。
        /// 适配器应使用此集合区分组件命名与 Schema 的 binding 策略。
        /// </summary>
        public IReadOnlyCollection<GameObject> GeneratedBindingObjects { get; internal set; }
    }

    public sealed class UIEffectPrefabGenerationResult
    {
        /// <summary>
        /// 向调用方提供Prefab路径。
        /// </summary>
        public string PrefabPath { get; internal set; }
        /// <summary>
        /// 向调用方提供Schema名称。
        /// </summary>
        public string SchemaName { get; internal set; }
        /// <summary>
        /// 向调用方提供UsedResources。
        /// </summary>
        public IReadOnlyList<string> UsedResources { get; internal set; }
        /// <summary>
        /// 向调用方提供MissingResources。
        /// </summary>
        public IReadOnlyList<string> MissingResources { get; internal set; }
        /// <summary>
        /// 当前GeneratedObject的数量。
        /// </summary>
        public int GeneratedObjectCount { get; internal set; }
        /// <summary>
        /// 当前GeneratedComponent的数量。
        /// </summary>
        public int GeneratedComponentCount { get; internal set; }
        public string FidelityReportPath { get; internal set; }
        public int FidelityReviewCount { get; internal set; }
        public IReadOnlyList<string> Warnings { get; internal set; } = Array.Empty<string>();
        public double ElapsedSeconds { get; internal set; }
        public double TotalElapsedSeconds { get; internal set; }
    }

    /// <summary>
    /// Deterministically compiles a reviewed UISchema into the project's
    /// standard UI Prefab shape. Visual interpretation stays outside this
    /// class so regeneration is repeatable and reviewable.
    /// </summary>
    public static class UIEffectPrefabBuilder
    {
        private const string GeneratedRootName = "Generated";

        /// <summary>
        /// 执行Generate从结构定义路径相关逻辑。
        /// </summary>
        public static UIEffectPrefabGenerationResult GenerateFromSchemaPath(
            string schemaAssetPath,
            UIEffectPrefabGenerationOptions options = null,
            bool promptForExistingPrefab = true)
        {
            schemaAssetPath = NormalizeAssetPath(schemaAssetPath);
            string absolutePath =
                UIEffectEditorUtility.ToAbsolutePath(schemaAssetPath);
            if (!File.Exists(absolutePath))
            {
                throw new InvalidOperationException(
                    $"找不到 UISchema：{schemaAssetPath}");
            }

            AssetDatabase.ImportAsset(
                schemaAssetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            return Generate(
                File.ReadAllText(absolutePath, Encoding.UTF8),
                options,
                promptForExistingPrefab);
        }

        /// <summary>
        /// 执行Generate相关逻辑。
        /// </summary>
        public static UIEffectPrefabGenerationResult Generate(
            string schemaJson,
            UIEffectPrefabGenerationOptions options = null,
            bool promptForExistingPrefab = true)
        {
            UIEffectGenerationTiming.BeginBuild(options?.panelId ?? "UISchema");
            try
            {
                UIEffectPrefabGenerationResult result = GenerateCore(schemaJson, options, promptForExistingPrefab);
                result.ElapsedSeconds = UIEffectGenerationTiming.BuildSeconds;
                result.TotalElapsedSeconds = UIEffectGenerationTiming.ElapsedSeconds;
                UIEffectGenerationTiming.Finish("成功", result.SchemaName);
                Debug.Log($"[UIPrefabGenerator/Timing] {result.SchemaName}：总耗时 " +
                    UIEffectGenerationTiming.Format(result.TotalElapsedSeconds) + "，Unity 构建 " +
                    UIEffectGenerationTiming.Format(result.ElapsedSeconds));
                return result;
            }
            catch (OperationCanceledException)
            {
                UIEffectGenerationTiming.Finish("已取消");
                throw;
            }
            catch
            {
                UIEffectGenerationTiming.Finish("失败");
                throw;
            }
        }

        private static UIEffectPrefabGenerationResult GenerateCore(
            string schemaJson, UIEffectPrefabGenerationOptions options, bool promptForExistingPrefab)
        {
            UIEffectSchema compactSchema =
                UIEffectSchemaUtility.Parse(schemaJson);
            options ??= new UIEffectPrefabGenerationOptions();
            var modalExtractionNotes = new List<string>();
            int excludedUnderlyingNodes =
                UIEffectSchemaUtility.ExtractModalForeground(
                    compactSchema,
                    modalExtractionNotes);
            if (excludedUnderlyingNodes > 0)
            {
                Debug.Log(
                    "[UIPrefabGenerator/Modal Foreground] 检测到模态弹窗，" +
                    "仅生成遮罩与弹窗前景；效果图中被覆盖的底层界面不进入 " +
                    "Prefab：\n" + string.Join("\n", modalExtractionNotes));
            }
            UIEffectSchemaUtility.InferContainedVisualHierarchy(
                compactSchema);
            int inferredRuntimeTemplateNodes =
                UIEffectSchemaUtility.InferRuntimeTemplateGroups(
                    compactSchema);
            if (inferredRuntimeTemplateNodes > 0)
            {
                Debug.Log(
                    "[UIPrefabGenerator] 严格同构的颜色/阵营运行时" +
                    $"模板候选已自动标记：{inferredRuntimeTemplateNodes} 个节点。" +
                    "资源匹配完成后只保留证据最完整的一项。");
            }
            UIEffectSchema schema =
                UIEffectSchemaUtility.ExpandRepeats(compactSchema);
            if (options.resourceMatchMode ==
                UIEffectResourceMatchMode.ColorBlocks)
            {
                ConvertVisualNodesToColorBlocks(schema);
            }
            else if (options.resourceMatchMode ==
                     UIEffectResourceMatchMode.VisualSimilarity)
            {
                PrepareReferenceCropNodesForVisualMatching(
                    schema.children);
            }
            else
            {
                ConvertReferenceCropNodesToColorBlocks(schema.children);
            }

            if (schema.useReferenceImageAsVisual)
            {
                PrepareReferenceVisualSchema(schema);
            }

            if (schema.children.Count == 0)
            {
                throw new InvalidOperationException(
                    "UISchema 没有任何 UI 节点，已停止生成空 Prefab。");
            }

            IUIEffectProjectAdapter projectAdapter =
                UIEffectProjectAdapterRegistry.Active;
            NormalizeOptions(options, schema, projectAdapter);
            List<string> warnings = UIEffectTypography.Prepare(schema, options.defaultFont);
            if (warnings.Count > 0)
                Debug.LogWarning("[UIPrefabGenerator/Typography] 已处理字体兼容性问题：\n" +
                    string.Join("\n", warnings));

            UIEffectPrefabHostResult initialResult =
                projectAdapter.CreateOrUpdatePrefab(
                    options,
                    promptForExistingPrefab);

            GameObject prefabAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    initialResult.PrefabPath);
            if (prefabAsset == null)
            {
                throw new InvalidOperationException(
                    $"项目适配器未生成 Prefab：{initialResult.PrefabPath}");
            }

            var resolver = new UIEffectResourceResolver(
                options.resourceSearchRoots,
                options.resourceMatchMode);
            resolver.MatchVisualResources(schema);
            var bindingNameRepairs = new List<string>();
            int repairedBindingNames =
                UIEffectSchemaUtility.EnsureUniqueBindingNames(
                    schema,
                    bindingNameRepairs);
            if (repairedBindingNames > 0)
            {
                Debug.LogWarning(
                    "[UIPrefabGenerator/Binding Repair] 最终 UI 树存在 " +
                    "重复绑定名，已按最近父级路径确定性命名；视觉节点、层级、" +
                    "坐标和资源均未改变：\n" +
                    string.Join("\n", bindingNameRepairs));
            }
            BuildGeneratedTree(
                initialResult.PrefabPath,
                schema,
                resolver,
                projectAdapter,
                options);
            warnings.AddRange(resolver.ResourceWarnings);
            warnings.AddRange(resolver.MissingResources.Select(item => "未匹配 Sprite：" + item));

            // The active adapter configured the Prefab root before the
            // Generated subtree was rebuilt. Save and import that exact asset
            // once so a stale AssetDatabase object cannot overwrite the new
            // hierarchy.
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                initialResult.PrefabPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            PrefabContentStats contentStats = ValidateGeneratedPrefab(
                initialResult.PrefabPath,
                schema);

            string fidelityReport = string.Empty;
            int fidelityReviewCount = 0;
            if (options.resourceMatchMode == UIEffectResourceMatchMode.VisualSimilarity)
            {
                fidelityReport = UIEffectFidelityAudit.Create(initialResult.PrefabPath,
                    schema, out fidelityReviewCount, warnings);
            }

            return new UIEffectPrefabGenerationResult
            {
                PrefabPath = initialResult.PrefabPath,
                SchemaName = schema.name,
                UsedResources = resolver.UsedResources.ToArray(),
                MissingResources = resolver.MissingResources.ToArray(),
                GeneratedObjectCount = contentStats.ObjectCount,
                GeneratedComponentCount = contentStats.ComponentCount,
                FidelityReportPath = fidelityReport,
                FidelityReviewCount = fidelityReviewCount,
                Warnings = warnings.ToArray(),
            };
        }

        /// <summary>
        /// 构建GeneratedTree。
        /// </summary>
        private static void BuildGeneratedTree(
            string prefabPath,
            UIEffectSchema schema,
            UIEffectResourceResolver resolver,
            IUIEffectProjectAdapter projectAdapter,
            UIEffectPrefabGenerationOptions options)
        {
            GameObject root =
                PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                throw new InvalidOperationException(
                    $"无法加载 Prefab 内容：{prefabPath}");
            }

            try
            {
                Transform previous = root.transform.Find(GeneratedRootName);
                if (previous != null)
                {
                    projectAdapter.BeforeReplaceGeneratedTree(
                        root,
                        previous);
                    UnityEngine.Object.DestroyImmediate(
                        previous.gameObject);
                }

                RectTransform generated = CreateRectTransform(
                    GeneratedRootName,
                    root.transform);
                ConfigureDesignRoot(generated, schema);

                var bindingNames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                var bindingObjects = new HashSet<GameObject>();
                for (int index = 0;
                     index < schema.children.Count;
                     index++)
                {
                    BuildNode(
                        schema.children[index],
                        generated,
                        schema.designWidth,
                        schema.designHeight,
                        resolver,
                        bindingNames,
                        bindingObjects,
                        schema.name);
                }

                options.GeneratedBindingObjects = bindingObjects;
                try
                {
                    projectAdapter.AfterBuildGeneratedTree(root, options);
                }
                finally
                {
                    options.GeneratedBindingObjects = null;
                }

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    prefabPath,
                    out bool savedSuccessfully);
                if (!savedSuccessfully || savedPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"保存生成后的 Prefab 失败：{prefabPath}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.ImportAsset(
                prefabPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// 校验Generated预制体。
        /// </summary>
        private static PrefabContentStats ValidateGeneratedPrefab(
            string prefabPath,
            UIEffectSchema schema)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                throw new InvalidOperationException(
                    $"无法验证生成后的 Prefab：{prefabPath}");
            }

            try
            {
                Transform generated = root.transform.Find(GeneratedRootName);
                if (generated == null)
                {
                    throw new InvalidOperationException(
                        $"Prefab {prefabPath} 缺少 Generated 子树，生成结果无效。");
                }

                int expectedTopLevelCount = schema.children.Count;
                int expectedNodeCount = CountSchemaNodes(schema.children);
                int expectedUiComponentCount =
                    CountSchemaUiComponents(schema.children);
                int objectCount = Mathf.Max(
                    0,
                    generated.GetComponentsInChildren<Transform>(true).Length - 1);
                int componentCount = Mathf.Max(
                    0,
                    generated.GetComponentsInChildren<Component>(true).Length - 1);
                int uiComponentCount =
                    generated.GetComponentsInChildren<Image>(true).Length +
                    generated.GetComponentsInChildren<TextMeshProUGUI>(true).Length +
                    generated.GetComponentsInChildren<Button>(true).Length +
                    generated.GetComponentsInChildren<Toggle>(true).Length +
                    generated.GetComponentsInChildren<ToggleGroup>(true).Length +
                    generated.GetComponentsInChildren<ScrollRect>(true).Length;
                int expectedToggleCount = CountSchemaNodesOfType(
                    schema.children,
                    "Toggle");
                int expectedToggleGroupCount = CountSchemaNodesOfType(
                    schema.children,
                    "ToggleGroup");
                int toggleCount =
                    generated.GetComponentsInChildren<Toggle>(true).Length;
                int toggleGroupCount =
                    generated.GetComponentsInChildren<ToggleGroup>(true).Length;
                RectTransform generatedRect =
                    generated.GetComponent<RectTransform>();
                bool designRootMatches =
                    generatedRect != null &&
                    Approximately(
                        generatedRect.rect.width,
                        schema.designWidth) &&
                    Approximately(
                        generatedRect.rect.height,
                        schema.designHeight) &&
                    Approximately(
                        generatedRect.localScale.x,
                        1f) &&
                    Approximately(
                        generatedRect.localScale.y,
                        1f) &&
                    Approximately(
                        generatedRect.localScale.z,
                        1f);
                bool allGeneratedScalesAreOne =
                    generated.GetComponentsInChildren<RectTransform>(true)
                        .All(rect =>
                            Approximately(rect.localScale.x, 1f) &&
                            Approximately(rect.localScale.y, 1f) &&
                            Approximately(rect.localScale.z, 1f));

                if (generated.childCount != expectedTopLevelCount ||
                    objectCount < expectedNodeCount ||
                    uiComponentCount < expectedUiComponentCount ||
                    toggleCount != expectedToggleCount ||
                    toggleGroupCount != expectedToggleGroupCount ||
                    !designRootMatches ||
                    !allGeneratedScalesAreOne)
                {
                    throw new InvalidOperationException(
                        $"Prefab 内容校验失败：Schema {expectedNodeCount} 个节点/" +
                        $"{expectedTopLevelCount} 个顶层节点，实际 Generated 子树 " +
                        $"{objectCount} 个对象/{generated.childCount} 个顶层节点，" +
                        $"UI 组件 {uiComponentCount}/{expectedUiComponentCount}，" +
                        $"Toggle {toggleCount}/{expectedToggleCount}，" +
                        $"ToggleGroup {toggleGroupCount}/{expectedToggleGroupCount}，" +
                        $"设计根 {schema.designWidth}x{schema.designHeight} @ " +
                        $"Scale 1：{designRootMatches}，" +
                        $"全部节点 Scale 1：{allGeneratedScalesAreOne}。" +
                        "已停止报告生成成功，请重新生成并检查 Console。");
                }

                ValidateToggleGroupAssignments(generated);

                return new PrefabContentStats(
                    objectCount,
                    componentCount);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 执行数量结构定义节点相关逻辑。
        /// </summary>
        private static int CountSchemaNodes(
            IEnumerable<UIEffectNode> nodes)
        {
            int count = 0;
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                count++;
                count += CountSchemaNodes(node.children);
            }

            return count;
        }

        /// <summary>
        /// 准备引用Visual结构定义。
        /// </summary>
        private static void PrepareReferenceVisualSchema(
            UIEffectSchema schema)
        {
            string referencePath = NormalizeAssetPath(
                schema.referenceImage);
            if (!(referencePath == "Assets" ||
                  referencePath.StartsWith(
                      "Assets/",
                      StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "useReferenceImageAsVisual 只能引用 " +
                    "Assets 下的运行时 Sprite，当前路径：" +
                    referencePath);
            }

            if (AssetDatabase.LoadAssetAtPath<Sprite>(referencePath) == null)
            {
                throw new InvalidOperationException(
                    "参考图视觉模式需要将图片导入为 Sprite：" +
                    referencePath);
            }

            schema.children.Insert(0, new UIEffectNode
            {
                name = "ReferenceVisual",
                type = "Image",
                width = schema.designWidth,
                height = schema.designHeight,
                resource = referencePath,
                binding = "No",
            });
            UIEffectSchemaUtility.Validate(schema);
        }

        /// <summary>
        /// 转换VisualNodesTo颜色Blocks。
        /// </summary>
        internal static int ConvertVisualNodesToColorBlocks(
            UIEffectSchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            return ConvertNodesToColorBlocks(schema.children, true);
        }

        /// <summary>
        /// 转换引用CropNodesTo颜色Blocks。
        /// </summary>
        private static int ConvertReferenceCropNodesToColorBlocks(
            IEnumerable<UIEffectNode> nodes)
        {
            return ConvertNodesToColorBlocks(nodes, false);
        }

        /// <summary>
        /// 准备引用CropNodesForVisualMatching。
        /// </summary>
        private static void PrepareReferenceCropNodesForVisualMatching(
            IEnumerable<UIEffectNode> nodes)
        {
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                if (UIEffectFigmaImporter.IsReferenceCropResource(
                        node.resource))
                {
                    node.resource = string.Empty;
                    node.intentionalColor = false;
                }

                PrepareReferenceCropNodesForVisualMatching(node.children);
            }
        }

        /// <summary>
        /// 转换NodesTo颜色Blocks。
        /// </summary>
        private static int ConvertNodesToColorBlocks(
            IEnumerable<UIEffectNode> nodes,
            bool convertAllVisualNodes)
        {
            int convertedCount = 0;
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                bool isVisualNode =
                    string.Equals(
                        node.type,
                        "Image",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        node.type,
                        "Button",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        node.type,
                        "Toggle",
                        StringComparison.OrdinalIgnoreCase);
                bool isReferenceCrop =
                    UIEffectFigmaImporter.IsReferenceCropResource(
                        node.resource);
                if (isVisualNode &&
                    (convertAllVisualNodes || isReferenceCrop))
                {
                    node.resource = string.Empty;
                    node.resourceCandidates?.Clear();
                    node.intentionalColor = true;
                    node.preserveAspect = false;
                    node.sliced = false;
                    if (string.IsNullOrWhiteSpace(node.color))
                    {
                        node.color = "#" + ColorUtility.ToHtmlStringRGBA(
                            GetPlaceholderColor(node));
                    }

                    convertedCount++;
                }

                convertedCount += ConvertNodesToColorBlocks(
                    node.children,
                    convertAllVisualNodes);
            }

            return convertedCount;
        }

        /// <summary>
        /// 执行配置Design根节点相关逻辑。
        /// </summary>
        private static void ConfigureDesignRoot(
            RectTransform rect,
            UIEffectSchema schema)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(
                schema.designWidth,
                schema.designHeight);
            rect.localScale = Vector3.one;
        }

        /// <summary>
        /// 执行Approximately相关逻辑。
        /// </summary>
        private static bool Approximately(float first, float second)
        {
            return Mathf.Abs(first - second) <= 0.01f;
        }

        /// <summary>
        /// 执行数量结构定义Ui组件相关逻辑。
        /// </summary>
        private static int CountSchemaUiComponents(
            IEnumerable<UIEffectNode> nodes)
        {
            int count = 0;
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                if (!string.Equals(
                        node.type,
                        "Container",
                        StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }

                count += CountSchemaUiComponents(node.children);
            }

            return count;
        }

        /// <summary>
        /// 执行数量结构定义节点Of类型相关逻辑。
        /// </summary>
        private static int CountSchemaNodesOfType(
            IEnumerable<UIEffectNode> nodes,
            string nodeType)
        {
            int count = 0;
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                if (string.Equals(
                        node.type,
                        nodeType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }

                count += CountSchemaNodesOfType(node.children, nodeType);
            }

            return count;
        }

        /// <summary>
        /// 校验开关分组Assignments。
        /// </summary>
        private static void ValidateToggleGroupAssignments(
            Transform generated)
        {
            Toggle[] toggles = generated.GetComponentsInChildren<Toggle>(true);
            foreach (Toggle toggle in toggles)
            {
                ToggleGroup nearestGroup = toggle.transform.parent == null
                    ? null
                    : toggle.transform.parent.GetComponentInParent<ToggleGroup>(
                        true);
                if (nearestGroup != null && toggle.group != nearestGroup)
                {
                    throw new InvalidOperationException(
                        $"Toggle {toggle.name} 未加入其父级 ToggleGroup " +
                        $"{nearestGroup.name}，Prefab 生成结果无效。");
                }
            }
        }

        private readonly struct PrefabContentStats
        {
            /// <summary>
            /// 创建PrefabContentStats实例。
            /// </summary>
            public PrefabContentStats(
                int objectCount,
                int componentCount)
            {
                ObjectCount = objectCount;
                ComponentCount = componentCount;
            }

            /// <summary>
            /// 当前Object的数量。
            /// </summary>
            public int ObjectCount { get; }
            /// <summary>
            /// 当前Component的数量。
            /// </summary>
            public int ComponentCount { get; }
        }

        /// <summary>
        /// 构建Node。
        /// </summary>
        private static RectTransform BuildNode(
            UIEffectNode node,
            RectTransform parent,
            float parentWidth,
            float parentHeight,
            UIEffectResourceResolver resolver,
            HashSet<string> bindingNames,
            HashSet<GameObject> bindingObjects,
            string nodePath)
        {
            string bindingName = GetBindingName(node);
            if (ShouldBind(node) && !bindingNames.Add(bindingName))
            {
                throw new InvalidOperationException(
                    $"节点绑定名重复：{bindingName}。" +
                    "请让 UISchema 中可绑定节点使用唯一名称。");
            }

            RectTransform rect;
            RectTransform childParent;
            float childParentWidth = node.width;
            float childParentHeight = node.height;
            switch (node.type.ToLowerInvariant())
            {
                case "image":
                    rect = CreateImageNode(
                        bindingName,
                        node,
                        parent,
                        resolver,
                        nodePath);
                    childParent = rect;
                    break;
                case "text":
                    rect = CreateTextNode(
                        bindingName,
                        node,
                        parent);
                    childParent = rect;
                    break;
                case "button":
                    rect = CreateButtonNode(
                        bindingName,
                        node,
                        parent,
                        resolver,
                        nodePath);
                    childParent = rect;
                    break;
                case "toggle":
                    rect = CreateToggleNode(
                        bindingName,
                        node,
                        parent,
                        resolver,
                        nodePath);
                    childParent = rect;
                    break;
                case "togglegroup":
                    rect = CreateToggleGroupNode(
                        bindingName,
                        node,
                        parent);
                    childParent = rect;
                    break;
                case "scrollrect":
                    rect = CreateScrollNode(
                        bindingName,
                        node,
                        parent,
                        out childParent);
                    ConfigureScrollContent(
                        node,
                        childParent,
                        out childParentWidth,
                        out childParentHeight);
                    break;
                default:
                    rect = CreateRectTransform(bindingName, parent);
                    childParent = rect;
                    break;
            }

            ApplyLayout(
                rect,
                node,
                parentWidth,
                parentHeight);
            if (ShouldBind(node)) bindingObjects.Add(rect.gameObject);

            string currentPath = nodePath + "/" + node.name;
            for (int index = 0;
                 index < node.children.Count;
                 index++)
            {
                BuildNode(
                    node.children[index],
                    childParent,
                    childParentWidth,
                    childParentHeight,
                    resolver,
                    bindingNames,
                    bindingObjects,
                    currentPath);
            }

            ToggleGroup toggleGroup = rect.GetComponent<ToggleGroup>();
            if (toggleGroup != null)
            {
                ConfigureToggleGroup(toggleGroup, node);
            }

            return rect;
        }

        /// <summary>
        /// 创建图片Node。
        /// </summary>
        private static RectTransform CreateImageNode(
            string name,
            UIEffectNode node,
            Transform parent,
            UIEffectResourceResolver resolver,
            string nodePath)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            ApplyImage(
                image,
                node,
                resolver,
                nodePath + "/" + node.name,
                out _);

            return rect;
        }

        /// <summary>
        /// 创建文本Node。
        /// </summary>
        private static RectTransform CreateTextNode(
            string name,
            UIEffectNode node,
            Transform parent)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            TextMeshProUGUI text =
                rect.gameObject.AddComponent<TextMeshProUGUI>();
            ConfigureText(text, node);
            return rect;
        }

        /// <summary>
        /// 创建按钮Node。
        /// </summary>
        private static RectTransform CreateButtonNode(
            string name,
            UIEffectNode node,
            Transform parent,
            UIEffectResourceResolver resolver,
            string nodePath)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            ApplyImage(
                image,
                node,
                resolver,
                nodePath + "/" + node.name,
                out _);
            Button button = rect.gameObject.AddComponent<Button>();
            image.raycastTarget = true;
            button.targetGraphic = image;

            bool hasTextChild = node.children.Any(child =>
                child != null &&
                string.Equals(
                    child.type,
                    "Text",
                    StringComparison.OrdinalIgnoreCase));
            if (!hasTextChild)
            {
                string label = node.text;
                if (!string.IsNullOrWhiteSpace(label))
                {
                    AddButtonLabel(rect, node, label);
                }
            }

            return rect;
        }

        /// <summary>
        /// 创建开关Node。
        /// </summary>
        private static RectTransform CreateToggleNode(
            string name,
            UIEffectNode node,
            Transform parent,
            UIEffectResourceResolver resolver,
            string nodePath)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            ApplyImage(
                image,
                node,
                resolver,
                nodePath + "/" + node.name,
                out _);
            Toggle toggle = rect.gameObject.AddComponent<Toggle>();
            image.raycastTarget = true;
            toggle.targetGraphic = image;
            toggle.group = parent.GetComponentInParent<ToggleGroup>(true);
            toggle.SetIsOnWithoutNotify(node.isOn);

            bool hasTextChild = node.children.Any(child =>
                child != null &&
                string.Equals(
                    child.type,
                    "Text",
                    StringComparison.OrdinalIgnoreCase));
            if (!hasTextChild)
            {
                string label = node.text;
                if (!string.IsNullOrWhiteSpace(label))
                {
                    AddButtonLabel(rect, node, label);
                }
            }

            return rect;
        }

        /// <summary>
        /// 创建开关分组Node。
        /// </summary>
        private static RectTransform CreateToggleGroupNode(
            string name,
            UIEffectNode node,
            Transform parent)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            ToggleGroup toggleGroup = rect.gameObject.AddComponent<ToggleGroup>();
            toggleGroup.allowSwitchOff = node.allowSwitchOff;
            return rect;
        }

        /// <summary>
        /// 执行配置开关Group相关逻辑。
        /// </summary>
        private static void ConfigureToggleGroup(
            ToggleGroup toggleGroup,
            UIEffectNode groupNode)
        {
            List<Toggle> toggles = toggleGroup
                .GetComponentsInChildren<Toggle>(true)
                .Where(toggle => toggle.group == toggleGroup)
                .ToList();
            if (toggles.Count == 0)
            {
                return;
            }

            var requestedStates = new List<bool>();
            CollectOwnedToggleStates(groupNode.children, requestedStates);
            int selectedIndex = requestedStates.FindIndex(value => value);
            if (selectedIndex >= toggles.Count)
            {
                selectedIndex = -1;
            }

            bool allowSwitchOff = toggleGroup.allowSwitchOff;
            toggleGroup.allowSwitchOff = true;
            foreach (Toggle toggle in toggles)
            {
                toggle.SetIsOnWithoutNotify(false);
            }

            toggleGroup.allowSwitchOff = allowSwitchOff;
            if (selectedIndex >= 0)
            {
                toggles[selectedIndex].SetIsOnWithoutNotify(true);
            }
            else if (!allowSwitchOff)
            {
                toggles[0].SetIsOnWithoutNotify(true);
            }
        }

        /// <summary>
        /// 执行收集Owned开关状态相关逻辑。
        /// </summary>
        private static void CollectOwnedToggleStates(
            IEnumerable<UIEffectNode> nodes,
            ICollection<bool> states)
        {
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                if (string.Equals(
                        node.type,
                        "Toggle",
                        StringComparison.OrdinalIgnoreCase))
                {
                    states.Add(node.isOn);
                    continue;
                }

                if (string.Equals(
                        node.type,
                        "ToggleGroup",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CollectOwnedToggleStates(node.children, states);
            }
        }

        /// <summary>
        /// 创建滚动Node。
        /// </summary>
        private static RectTransform CreateScrollNode(
            string name,
            UIEffectNode node,
            Transform parent,
            out RectTransform content)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            Image background = rect.gameObject.AddComponent<Image>();
            background.color = ParseColor(
                node.color,
                new Color(0.08f, 0.1f, 0.13f, 0.35f));
            background.raycastTarget = node.raycastTarget;

            RectTransform viewport = CreateRectTransform(
                "Img_Viewport",
                rect);
            StretchToParent(viewport);
            Image viewportImage =
                viewport.gameObject.AddComponent<Image>();
            viewportImage.color = Color.white;
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<Mask>()
                .showMaskGraphic = false;

            content = CreateRectTransform("Content", viewport);
            StretchToParent(content);
            content.pivot = new Vector2(0.5f, 1f);

            ScrollRect scroll = rect.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal =
                string.Equals(
                    node.scrollDirection,
                    "Horizontal",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    node.scrollDirection,
                    "Both",
                    StringComparison.OrdinalIgnoreCase);
            scroll.vertical =
                !string.Equals(
                    node.scrollDirection,
                    "Horizontal",
                    StringComparison.OrdinalIgnoreCase);
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return rect;
        }

        /// <summary>
        /// 应用图片。
        /// </summary>
        private static void ApplyImage(
            Image image,
            UIEffectNode node,
            UIEffectResourceResolver resolver,
            string nodePath,
            out bool placeholder)
        {
            Sprite sprite = node.intentionalColor
                ? null
                : resolver.Resolve(node);
            placeholder = sprite == null && !node.intentionalColor;
            image.sprite = sprite;
            image.preserveAspect = node.preserveAspect;
            image.raycastTarget = node.raycastTarget;
            image.color = sprite != null
                ? ParseColor(node.color, Color.white)
                : node.intentionalColor
                    ? ParseColor(node.color, Color.white)
                    : GetPlaceholderColor(node);
            image.type = node.sliced &&
                         sprite != null &&
                         sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : Image.Type.Simple;

            if (placeholder)
            {
                resolver.RecordMissing(nodePath, node);
            }
        }

        /// <summary>
        /// 执行配置文本相关逻辑。
        /// </summary>
        private static void ConfigureText(
            TextMeshProUGUI text,
            UIEffectNode node)
        {
            text.text = node.text ?? string.Empty;
            text.fontSize = Mathf.Max(1f, node.fontSize);
            text.characterSpacing = node.characterSpacing;
            text.color = ParseColor(node.color, Color.white);
            text.alignment = ParseAlignment(node.alignment);
            text.fontStyle = node.bold
                ? FontStyles.Bold
                : FontStyles.Normal;
            text.enableAutoSizing = false;
            text.enableWordWrapping = !node.noWrap;
            text.lineSpacing = node.lineSpacing;
            if (!string.IsNullOrWhiteSpace(node.font))
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(node.font);
                if (font == null) throw new InvalidOperationException("找不到 TMP 字体：" + node.font);
                text.font = font;
            }
            if (!string.IsNullOrWhiteSpace(node.fontMaterial))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(node.fontMaterial);
                if (material == null || text.font == null ||
                    material.mainTexture != text.font.material.mainTexture)
                    throw new InvalidOperationException("文字材质与字体图集不兼容：" + node.fontMaterial);
                text.fontSharedMaterial = material;
            }
            text.raycastTarget = node.raycastTarget;
            // Screenshot-authored text rects are measured to visible glyph
            // bounds. TMP font metrics can exceed that rect by a fraction of
            // a pixel (for example 36.1 px preferred height in a 36 px rect),
            // and Ellipsis then suppresses the entire line with zero vertices.
            // Preserve the authored font size and visibility; geometry review
            // remains responsible for preventing meaningful overflow.
            text.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>
        /// 添加按钮Label。
        /// </summary>
        private static void AddButtonLabel(
            RectTransform parent,
            UIEffectNode source,
            string label)
        {
            RectTransform rect = CreateRectTransform("Txt_Label", parent);
            StretchToParent(rect);
            TextMeshProUGUI text =
                rect.gameObject.AddComponent<TextMeshProUGUI>();
            var labelNode = new UIEffectNode
            {
                text = label,
                fontSize = source.fontSize,
                characterSpacing = source.characterSpacing,
                font = source.font,
                fontMaterial = source.fontMaterial,
                lineSpacing = source.lineSpacing,
                noWrap = source.noWrap,
                alignment = source.alignment,
                bold = source.bold,
                // Button.color tints its background. Use an explicit Text
                // child when the label needs a non-white text color.
                color = "#FFFFFFFF",
            };
            ConfigureText(text, labelNode);
            text.raycastTarget = false;
        }

        /// <summary>
        /// 创建矩形变换。
        /// </summary>
        private static RectTransform CreateRectTransform(
            string name,
            Transform parent)
        {
            var gameObject = new GameObject(
                name,
                typeof(RectTransform));
            RectTransform rect =
                gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>
        /// 应用布局。
        /// </summary>
        private static void ApplyLayout(
            RectTransform rect,
            UIEffectNode node,
            float parentWidth,
            float parentHeight)
        {
            float left = node.x;
            float top = node.y;
            float right = parentWidth - node.x - node.width;
            float bottom = parentHeight - node.y - node.height;
            string anchor = ResolveAnchor(
                    node,
                    parentWidth,
                    parentHeight)
                .ToLowerInvariant();

            if (anchor == "stretchall")
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = new Vector2(left, bottom);
                rect.offsetMax = new Vector2(-right, -top);
                return;
            }

            if (anchor == "stretchhorizontal")
            {
                float centerY =
                    parentHeight * 0.5f -
                    (node.y + node.height * 0.5f);
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = new Vector2(
                    left,
                    centerY - node.height * 0.5f);
                rect.offsetMax = new Vector2(
                    -right,
                    centerY + node.height * 0.5f);
                return;
            }

            if (anchor == "stretchvertical")
            {
                float centerX =
                    node.x + node.width * 0.5f -
                    parentWidth * 0.5f;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = new Vector2(
                    centerX - node.width * 0.5f,
                    bottom);
                rect.offsetMax = new Vector2(
                    centerX + node.width * 0.5f,
                    -top);
                return;
            }

            Vector2 anchorPoint = GetAnchorPoint(anchor);
            rect.anchorMin = anchorPoint;
            rect.anchorMax = anchorPoint;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(node.width, node.height);

            Vector2 desiredCenter = new Vector2(
                node.x + node.width * 0.5f - parentWidth * 0.5f,
                parentHeight * 0.5f -
                node.y - node.height * 0.5f);
            Vector2 anchorPosition = new Vector2(
                (anchorPoint.x - 0.5f) * parentWidth,
                (anchorPoint.y - 0.5f) * parentHeight);
            rect.anchoredPosition = desiredCenter - anchorPosition;
        }

        /// <summary>
        /// 获取锚点点。
        /// </summary>
        private static Vector2 GetAnchorPoint(string anchor)
        {
            switch (anchor)
            {
                case "topleft":
                    return new Vector2(0f, 1f);
                case "topcenter":
                    return new Vector2(0.5f, 1f);
                case "topright":
                    return new Vector2(1f, 1f);
                case "middleleft":
                    return new Vector2(0f, 0.5f);
                case "middleright":
                    return new Vector2(1f, 0.5f);
                case "bottomleft":
                    return new Vector2(0f, 0f);
                case "bottomcenter":
                    return new Vector2(0.5f, 0f);
                case "bottomright":
                    return new Vector2(1f, 0f);
                default:
                    return new Vector2(0.5f, 0.5f);
            }
        }

        /// <summary>
        /// 解析锚点。
        /// </summary>
        private static string ResolveAnchor(
            UIEffectNode node,
            float parentWidth,
            float parentHeight)
        {
            if (!string.IsNullOrWhiteSpace(node.anchor) &&
                !string.Equals(
                    node.anchor,
                    "Auto",
                    StringComparison.OrdinalIgnoreCase))
            {
                return node.anchor;
            }

            float horizontalCoverage = node.width / parentWidth;
            float verticalCoverage = node.height / parentHeight;
            if (horizontalCoverage >= 0.9f &&
                verticalCoverage >= 0.9f)
            {
                return "StretchAll";
            }

            if (horizontalCoverage >= 0.8f)
            {
                return "StretchHorizontal";
            }

            if (verticalCoverage >= 0.8f)
            {
                return "StretchVertical";
            }

            float normalizedCenterX =
                (node.x + node.width * 0.5f) / parentWidth;
            float normalizedCenterY =
                (node.y + node.height * 0.5f) / parentHeight;
            string vertical = normalizedCenterY < 0.34f
                ? "Top"
                : normalizedCenterY > 0.66f
                    ? "Bottom"
                    : "Middle";
            string horizontal = normalizedCenterX < 0.34f
                ? "Left"
                : normalizedCenterX > 0.66f
                    ? "Right"
                    : "Center";
            return vertical + horizontal;
        }

        /// <summary>
        /// 执行Stretch转换为Parent相关逻辑。
        /// </summary>
        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 执行配置滚动Content相关逻辑。
        /// </summary>
        private static void ConfigureScrollContent(
            UIEffectNode node,
            RectTransform content,
            out float contentWidth,
            out float contentHeight)
        {
            float requiredWidth = node.width;
            float requiredHeight = node.height;
            foreach (UIEffectNode child in node.children)
            {
                if (child == null)
                {
                    continue;
                }

                requiredWidth = Mathf.Max(
                    requiredWidth,
                    child.x + child.width);
                requiredHeight = Mathf.Max(
                    requiredHeight,
                    child.y + child.height);
            }

            bool horizontal = string.Equals(
                                  node.scrollDirection,
                                  "Horizontal",
                                  StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(
                                  node.scrollDirection,
                                  "Both",
                                  StringComparison.OrdinalIgnoreCase);
            bool vertical = !string.Equals(
                node.scrollDirection,
                "Horizontal",
                StringComparison.OrdinalIgnoreCase);
            contentWidth = horizontal
                ? requiredWidth
                : node.width;
            contentHeight = vertical
                ? requiredHeight
                : node.height;

            if (horizontal && vertical)
            {
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(0f, 1f);
                content.pivot = new Vector2(0f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(
                    contentWidth,
                    contentHeight);
                return;
            }

            if (horizontal)
            {
                content.anchorMin = new Vector2(0f, 0f);
                content.anchorMax = new Vector2(0f, 1f);
                content.pivot = new Vector2(0f, 0.5f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(contentWidth, 0f);
                return;
            }

            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, contentHeight);
        }

        /// <summary>
        /// 获取绑定名称。
        /// </summary>
        private static string GetBindingName(UIEffectNode node)
        {
            return UIEffectSchemaUtility.GetGeneratedNodeName(node);
        }

        /// <summary>
        /// 执行判断是否绑定相关逻辑。
        /// </summary>
        private static bool ShouldBind(UIEffectNode node)
        {
            if (string.Equals(
                    node.binding,
                    "Yes",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(
                    node.binding,
                    "No",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return node.type.Equals(
                       "Button",
                       StringComparison.OrdinalIgnoreCase) ||
                   node.type.Equals(
                       "Toggle",
                       StringComparison.OrdinalIgnoreCase) ||
                   node.type.Equals(
                       "ScrollRect",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取Placeholder颜色。
        /// </summary>
        private static Color GetPlaceholderColor(UIEffectNode node)
        {
            string semantic = GetSemanticName(node).ToLowerInvariant();
            if (node.type.Equals(
                    "Button",
                    StringComparison.OrdinalIgnoreCase) ||
                node.type.Equals(
                    "Toggle",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new Color32(92, 191, 114, 255);
            }

            if (semantic.Contains("avatar") ||
                semantic.Contains("portrait") ||
                semantic.Contains("头像"))
            {
                return new Color32(174, 111, 212, 255);
            }

            if (semantic.Contains("icon") ||
                semantic.Contains("图标"))
            {
                return new Color32(239, 203, 77, 255);
            }

            if (semantic.Contains("background") ||
                semantic.Contains("bg") ||
                semantic.Contains("背景"))
            {
                return new Color32(125, 132, 143, 255);
            }

            if (semantic.Contains("unknown") ||
                semantic.Contains("未知"))
            {
                return new Color32(220, 88, 88, 255);
            }

            if (node.type.Equals(
                    "Image",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new Color32(86, 160, 221, 255);
            }

            return new Color32(220, 88, 88, 255);
        }

        /// <summary>
        /// 解析颜色。
        /// </summary>
        private static Color ParseColor(
            string value,
            Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                ColorUtility.TryParseHtmlString(
                    value.Trim(),
                    out Color color))
            {
                return color;
            }

            return fallback;
        }

        /// <summary>
        /// 解析Alignment。
        /// </summary>
        private static TextAlignmentOptions ParseAlignment(
            string alignment)
        {
            // AI/Figma commonly use the RectTransform-style Middle prefix;
            // TMP calls the same vertical alignment simply Left/Center/Right.
            if (string.Equals(alignment, "MiddleLeft", StringComparison.OrdinalIgnoreCase)) return TextAlignmentOptions.Left;
            if (string.Equals(alignment, "MiddleCenter", StringComparison.OrdinalIgnoreCase)) return TextAlignmentOptions.Center;
            if (string.Equals(alignment, "MiddleRight", StringComparison.OrdinalIgnoreCase)) return TextAlignmentOptions.Right;
            if (Enum.TryParse(
                    alignment,
                    true,
                    out TextAlignmentOptions result))
            {
                return result;
            }

            return TextAlignmentOptions.Center;
        }

        /// <summary>
        /// 获取Semantic名称。
        /// </summary>
        private static string GetSemanticName(UIEffectNode node)
        {
            return string.IsNullOrWhiteSpace(node.semantic)
                ? node.name
                : node.semantic;
        }

        /// <summary>
        /// 执行规范化选项相关逻辑。
        /// </summary>
        private static void NormalizeOptions(
            UIEffectPrefabGenerationOptions options,
            UIEffectSchema schema,
            IUIEffectProjectAdapter projectAdapter)
        {
            UIEffectProjectDefaults defaults =
                projectAdapter.CreateDefaults() ??
                new UIEffectProjectDefaults();
            options.panelId = string.IsNullOrWhiteSpace(options.panelId)
                ? schema.name
                : options.panelId.Trim();
            options.logicClassName =
                string.IsNullOrWhiteSpace(options.logicClassName)
                    ? UIEffectEditorUtility.SanitizeTypeName(options.panelId)
                    : options.logicClassName.Trim();
            options.prefabFolder =
                NormalizeAssetPath(
                    string.IsNullOrWhiteSpace(options.prefabFolder)
                        ? defaults.prefabFolder
                        : options.prefabFolder);
            options.scriptFolder =
                NormalizeAssetPath(
                    string.IsNullOrWhiteSpace(options.scriptFolder)
                        ? defaults.scriptFolder
                        : options.scriptFolder);
            options.codeNamespace =
                string.IsNullOrWhiteSpace(options.codeNamespace)
                    ? defaults.codeNamespace
                    : options.codeNamespace.Trim();
            if (options.scriptType == UIEffectScriptType.ProjectDefault)
            {
                options.scriptType = defaults.scriptType;
            }
            options.resourceSearchRoots =
                (options.resourceSearchRoots ?? Array.Empty<string>())
                .Select(NormalizeAssetPath)
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    AssetDatabase.IsValidFolder(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (options.resourceSearchRoots.Length == 0)
            {
                string defaultResourceRoot = NormalizeAssetPath(
                    defaults.resourceSearchRoot);
                if (!AssetDatabase.IsValidFolder(defaultResourceRoot))
                {
                    defaultResourceRoot = "Assets";
                }

                options.resourceSearchRoots = new[]
                {
                    defaultResourceRoot,
                };
            }
        }

        /// <summary>
        /// 执行规范化资源路径相关逻辑。
        /// </summary>
        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/').TrimEnd('/');
        }
    }

    internal sealed partial class UIEffectResourceResolver
    {
        private const int MinimumMatchScore = 440;
        private const int VisualSampleSize = 24;
        private const float MinimumAspectSimilarity = 0.6f;
        private const float MinimumVisualSimilarity = 0.68f;
        private const float MinimumVisualMargin = 0.035f;
        private const float ConfidentVisualSimilarity = 0.85f;
        private const float ConfidentVisualMargin = 0.015f;
        private const float StrongVisualSimilarity = 0.9f;
        private const float StrongVisualMargin = 0.015f;
        private const float CompactGraphicVisualSimilarity = 0.84f;
        private const float TextCoveredConfidentSimilarity = 0.85f;
        private const float TextCoveredConfidentMargin = 0.015f;
        private const float TextCoveredStrongSimilarity = 0.9f;
        private const float TextCoveredStrongMargin = 0.002f;
        private const float CompositeSlicedSimilarity = 0.78f;
        private const float CompositeSlicedMargin = 0.01f;
        private const float OccludedVisualSimilarity = 0.78f;
        private const float OccludedVisualMargin = 0.03f;
        private const float OccludedConfidentVisualSimilarity = 0.8f;
        private const float OccludedConfidentVisualMargin = 0.012f;
        private const float StrongAuthoredVisualSimilarity = 0.92f;
        private const float SpatialAuthoredVisualSimilarity = 0.78f;
        private const float MaximumAuthoredColorDistance = 0.08f;
        private const float MinimumTintOverrideLead = 0.025f;
        private const float TintDistanceLeadScale = 0.02f;
        private const float MaterialTintDistance = 0.08f;
        private const float IntegratedParentVisualSimilarity = 0.93f;
        private const float IntegratedParentVisualLead = 0.02f;
        private const float InferredIntegratedParentVisualSimilarity = 0.88f;
        private const float InferredIntegratedParentVisualLead = 0.04f;
        private const float InferredIntegratedParentSizeSimilarity = 0.94f;
        private const float FullCanvasVisualSimilarity = 0.84f;
        private const float SplitFullWidthMinimumCoverage = 0.38f;
        private const float RepeatedSurfaceConsensusSimilarity = 0.8f;
        private const float RepeatedSurfaceConsensusMargin = 0.004f;
        private const float RepeatedSurfaceLocalSimilarity = 0.76f;
        private const float RuntimeTemplateConsensusSimilarity = 0.8f;
        private const float RuntimeTemplateConsensusMargin = 0.004f;
        private const float RuntimeTemplateLocalSimilarity = 0.74f;
        private const float RuntimeTemplateSingleSourceSimilarity = 0.86f;
        private const float AlignedSlicedVisualSimilarity = 0.78f;
        private const float AlignedSlicedVisualMargin = 0.006f;
        private const float AlignedTintedSlicedVisualSimilarity = 0.66f;
        private const float AlignedTintedSlicedVisualMargin = 0.02f;
        private const float AlignedSlicedStructuralFit = 0.9f;
        private const float GeneratedTextSurfaceSimilarity = 0.88f;
        private const float GeneratedTextSurfaceMargin = 0.002f;
        private const float ArtworkVisualSimilarity = 0.8f;
        private const float ArtworkVisualMargin = 0.006f;
        private const float ArtworkStrongVisualSimilarity = 0.86f;
        private const float LayeredArtVisualSimilarity = 0.84f;
        private const float LayeredArtImprovement = 0.018f;
        private const float ExplicitBakedTextVisualSimilarity = 0.84f;
        private const float BakedTextVisualSimilarity = 0.91f;
        private const int MinimumVisibleSamples = VisualSampleSize;
        private static readonly Dictionary<string, List<SpriteEntry>>
            SpriteIndexCache =
                new Dictionary<string, List<SpriteEntry>>(
                    StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, VisualDescriptor>
            VisualDescriptorCache =
                new Dictionary<string, VisualDescriptor>(
                    StringComparer.OrdinalIgnoreCase);
        private static readonly string[][] RuntimeTemplateVariantAliases =
        {
            new[] { "red", "hong", "红" },
            new[] { "green", "lv", "绿" },
            new[] { "blue", "lan", "蓝" },
            new[] { "yellow", "huang", "黄" },
            new[] { "purple", "zi", "紫" },
            new[] { "orange", "cheng", "橙" },
            new[] { "black", "hei", "黑" },
            new[] { "white", "bai", "白" },
            new[] { "gold", "jin", "金" },
            new[] { "silver", "yin", "银" },
            new[] { "selected", "active", "on", "选中", "激活" },
            new[] { "normal", "inactive", "off", "普通", "未选中" },
            new[] { "win", "victory", "sheng", "胜" },
            new[] { "lose", "defeat", "fu", "负", "败" },
        };

        private readonly List<SpriteEntry> sprites;
        private readonly List<string> usedResources = new List<string>();
        private readonly List<string> missingResources = new List<string>();
        private readonly List<string> resourceWarnings = new List<string>();
        private readonly UIEffectResourceMatchMode matchMode;
        private readonly Dictionary<UIEffectNode, string> visualFailures =
            new Dictionary<UIEffectNode, string>();
        private readonly HashSet<UIEffectNode> explicitVisualResources =
            new HashSet<UIEffectNode>();
        private readonly Dictionary<UIEffectNode, VisualParentContext>
            visualParentContexts =
                new Dictionary<UIEffectNode, VisualParentContext>();
        private readonly Dictionary<UIEffectNode, VisualMatchEvidence>
            visualMatchEvidence =
                new Dictionary<UIEffectNode, VisualMatchEvidence>();
        private bool visualProgressShown;

        /// <summary>
        /// 创建UIEffect资源Resolver实例。
        /// </summary>
        public UIEffectResourceResolver(
            string[] searchRoots,
            UIEffectResourceMatchMode matchMode)
        {
            this.matchMode = matchMode;
            sprites = matchMode == UIEffectResourceMatchMode.ColorBlocks
                ? new List<SpriteEntry>()
                : GetOrBuildSpriteIndex(searchRoots);
            if (matchMode != UIEffectResourceMatchMode.ColorBlocks && sprites.Count == 0)
                WarnResource("资源索引为空。搜索目录：" + string.Join("、", searchRoots ?? Array.Empty<string>()) +
                    "。请确认资源总目录覆盖图片所在目录，且图片已导入为 Sprite (2D and UI)；仅存在 Texture2D 文件不能直接赋给 UGUI Image。");
        }

        /// <summary>
        /// 向调用方提供UsedResources。
        /// </summary>
        public IReadOnlyList<string> UsedResources => usedResources;
        /// <summary>
        /// 向调用方提供MissingResources。
        /// </summary>
        public IReadOnlyList<string> MissingResources => missingResources;
        internal IReadOnlyList<string> ResourceWarnings => resourceWarnings;

        private void WarnResource(string message)
        {
            if (resourceWarnings.Contains(message)) return;
            resourceWarnings.Add(message);
            Debug.LogWarning("[UIPrefabGenerator] " + message);
        }

        /// <summary>
        /// 清空Caches。
        /// </summary>
        public static void ClearCaches()
        {
            ResourceRevision++;
            SpriteIndexCache.Clear();
            VisualDescriptorCache.Clear();
            ClearVisualSearchCache();
            ClearNativePixelCache();
        }

        /// <summary>
        /// 执行重建缓存相关逻辑。
        /// </summary>
        public static int RebuildCaches(string[] searchRoots)
        {
            ClearCaches();
            return GetOrBuildSpriteIndex(searchRoots).Count;
        }

        /// <summary>
        /// 执行匹配视觉资源相关逻辑。
        /// </summary>
        public void MatchVisualResources(UIEffectSchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (matchMode != UIEffectResourceMatchMode.VisualSimilarity)
            {
                SelectRuntimeTemplateCandidates(schema.children);
                return;
            }

            string referencePath = (schema.referenceImage ?? string.Empty)
                .Trim()
                .Replace('\\', '/');
            excludedReferencePath = referencePath;
            if (referencePath.Length == 0)
            {
                throw new InvalidOperationException(
                    "视觉资源匹配需要 UISchema.referenceImage。");
            }

            string absolutePath =
                UIEffectEditorUtility.ToAbsolutePath(referencePath);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    "找不到视觉资源匹配使用的效果图。",
                    referencePath);
            }

            var reference = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);
            try
            {
                explicitVisualResources.Clear();
                visualParentContexts.Clear();
                visualMatchEvidence.Clear();
                PrepareCandidatePolicies(schema.children);
                CollectExplicitVisualResources(schema.children);
                if (!reference.LoadImage(
                        File.ReadAllBytes(absolutePath),
                        false))
                {
                    throw new InvalidOperationException(
                        "视觉资源匹配使用的效果图不是有效 PNG/JPG：" +
                        referencePath);
                }

                using (var renderer = new SpritePreviewRenderer(
                           VisualSampleSize))
                {
                    int expandedBackgrounds =
                        ReconcileFullCanvasBackgrounds(
                            schema.children,
                            0f,
                            0f,
                            schema.designWidth,
                            schema.designHeight,
                            schema,
                            reference,
                            renderer);
                    CoalesceBackgroundRegions(
                        schema.children,
                        0f,
                        0f,
                        schema,
                        reference,
                        renderer);
                    int generatedTextSurfaces =
                        MaterializeMissingTextSurfaces(
                            schema.children,
                            0f,
                            0f,
                            schema.designWidth,
                            schema.designHeight,
                            false,
                            schema,
                            reference,
                            renderer);
                    MatchVisualNodes(
                        schema.children,
                        0f,
                        0f,
                        schema.designWidth,
                        schema.designHeight,
                        schema,
                        reference,
                        renderer,
                        Array.Empty<Rect>(),
                        Array.Empty<Rect>(),
                        string.Empty,
                        null);
                    int convertedVisualWrappers =
                        ConvertParentIntegratedVisualWrappersToContainers(
                            schema.children,
                            0f,
                            0f,
                            schema,
                            reference,
                            renderer,
                            null);
                    int propagatedVisuals =
                        ReconcileRepeatedVisualResources(
                            schema.children,
                            0f,
                            0f,
                            schema,
                            reference,
                            renderer,
                            null);
                    int propagatedTemplateVisuals =
                        ReconcileRuntimeTemplateResources(
                            schema.children,
                            0f,
                            0f,
                            schema,
                            reference,
                            renderer,
                            null);
                    int collapsedVisuals =
                        CollapseParentIntegratedTextWrappers(
                            schema.children,
                            0f,
                            0f,
                            schema,
                            reference,
                            renderer,
                            null);
                    int convertedTextArt = ReconcileTextArtwork(
                        schema.children,
                        0f,
                        0f,
                        schema,
                        reference,
                        renderer);
                    int suppressedBakedText = SuppressBakedTextNodes(
                        schema.children,
                        0f,
                        0f,
                        schema,
                        reference,
                        renderer,
                        null);
                    int prunedRuntimeTemplates =
                        SelectRuntimeTemplateCandidates(schema.children);
                    if (expandedBackgrounds > 0 ||
                        generatedTextSurfaces > 0 ||
                        convertedVisualWrappers > 0 ||
                        propagatedVisuals > 0 ||
                        propagatedTemplateVisuals > 0 ||
                        convertedTextArt > 0 ||
                        suppressedBakedText > 0 ||
                        prunedRuntimeTemplates > 0)
                    {
                        Debug.Log(
                            "[UIPrefabGenerator] 资源感知结构校正：" +
                            $"整幅背景 {expandedBackgrounds}，" +
                            $"文字承载背景 {generatedTextSurfaces}，" +
                            $"父图内置分组 {convertedVisualWrappers}，" +
                            $"同构视觉补全 {propagatedVisuals}，" +
                            $"模板通用视觉补全 {propagatedTemplateVisuals}，" +
                            $"艺术字 {convertedTextArt}，" +
                            $"烘焙文字抑制 {suppressedBakedText}，" +
                            $"运行时模板候选裁剪 {prunedRuntimeTemplates}。");
                    }

                    if (collapsedVisuals > 0)
                    {
                        Debug.Log(
                            $"[UIPrefabGenerator] 父 Sprite 已包含局部视觉，" +
                            $"自动折叠冗余 Image：{collapsedVisuals}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                UnityEngine.Object.DestroyImmediate(reference);
            }
        }

        /// <summary>
        /// 执行Select运行时模板Candidates相关逻辑。
        /// </summary>
        private int SelectRuntimeTemplateCandidates(
            List<UIEffectNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return 0;
            }

            int removed = 0;
            List<IGrouping<string, UIEffectNode>> groups = nodes
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
                List<RuntimeTemplateEvidence> candidates = group
                    .Select(node => EvaluateRuntimeTemplateCandidate(
                        node,
                        nodes.IndexOf(node)))
                    .OrderByDescending(item => item.VariantAffinity)
                    .ThenByDescending(item => item.ResolvedVisualCount)
                    .ThenBy(item => item.FailedVisualCount)
                    .ThenByDescending(item => item.AverageScore)
                    .ThenByDescending(item => item.AverageMargin)
                    .ThenBy(item => item.SiblingIndex)
                    .ToList();
                RuntimeTemplateEvidence selected = candidates[0];
                foreach (RuntimeTemplateEvidence candidate in candidates)
                {
                    if (ReferenceEquals(candidate.Node, selected.Node))
                    {
                        continue;
                    }

                    if (nodes.Remove(candidate.Node))
                    {
                        removed++;
                    }
                }

                Debug.Log(
                    $"[UIPrefabGenerator] 运行时模板组 {group.Key} " +
                    $"保留 {selected.Node.name}；候选证据：" +
                    string.Join(
                        "，",
                        candidates.Select(item => item.ToLogString())));
            }

            foreach (UIEffectNode node in nodes.Where(item => item != null))
            {
                removed += SelectRuntimeTemplateCandidates(node.children);
            }

            return removed;
        }

        /// <summary>
        /// 执行评估运行时模板Candidate相关逻辑。
        /// </summary>
        private RuntimeTemplateEvidence EvaluateRuntimeTemplateCandidate(
            UIEffectNode node,
            int siblingIndex)
        {
            var result = new RuntimeTemplateEvidence(
                node,
                siblingIndex);
            CollectRuntimeTemplateEvidence(
                node,
                node.runtimeTemplateVariant,
                result);
            return result;
        }

        /// <summary>
        /// 执行收集运行时模板Evidence相关逻辑。
        /// </summary>
        private void CollectRuntimeTemplateEvidence(
            UIEffectNode node,
            string variant,
            RuntimeTemplateEvidence result)
        {
            if (node == null)
            {
                return;
            }

            if (IsVisualNode(node))
            {
                result.VisualCount++;
                if (!string.IsNullOrWhiteSpace(node.resource))
                {
                    result.ResolvedVisualCount++;
                    result.VariantAffinity +=
                        GetRuntimeVariantResourceAffinity(
                            variant,
                            node.resource);
                }
                else if (visualFailures.ContainsKey(node))
                {
                    result.FailedVisualCount++;
                }

                if (visualMatchEvidence.TryGetValue(
                        node,
                        out VisualMatchEvidence evidence) &&
                    string.Equals(
                        evidence.Resource,
                        node.resource,
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.ScoreTotal += evidence.Score;
                    result.MarginTotal += evidence.Margin;
                    result.ScoredVisualCount++;
                }
            }

            foreach (UIEffectNode child in
                     node.children ?? new List<UIEffectNode>())
            {
                CollectRuntimeTemplateEvidence(child, variant, result);
            }
        }

        /// <summary>
        /// 获取运行时Variant资源Affinity。
        /// </summary>
        private static int GetRuntimeVariantResourceAffinity(
            string variant,
            string resource)
        {
            int familyIndex = GetRuntimeVariantFamily(variant);
            if (familyIndex < 0 || string.IsNullOrWhiteSpace(resource))
            {
                return 0;
            }

            HashSet<string> resourceTokens = GetResourceTokens(resource);
            if (RuntimeTemplateVariantAliases[familyIndex]
                .Any(resourceTokens.Contains))
            {
                return 2;
            }

            for (int index = 0;
                 index < RuntimeTemplateVariantAliases.Length;
                 index++)
            {
                if (index != familyIndex &&
                    RuntimeTemplateVariantAliases[index]
                        .Any(resourceTokens.Contains))
                {
                    return -2;
                }
            }

            return 0;
        }

        /// <summary>
        /// 获取运行时VariantFamily。
        /// </summary>
        private static int GetRuntimeVariantFamily(string value)
        {
            HashSet<string> tokens = GetResourceTokens(value);
            for (int index = 0;
                 index < RuntimeTemplateVariantAliases.Length;
                 index++)
            {
                if (RuntimeTemplateVariantAliases[index]
                    .Any(tokens.Contains))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// 获取资源Tokens。
        /// </summary>
        private static HashSet<string> GetResourceTokens(string value)
        {
            var result = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var token = new StringBuilder();
            foreach (char character in
                     (value ?? string.Empty).ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    token.Append(character);
                    continue;
                }

                if (token.Length > 0)
                {
                    result.Add(token.ToString());
                    token.Clear();
                }
            }

            if (token.Length > 0)
            {
                result.Add(token.ToString());
            }

            return result;
        }

        /// <summary>
        /// 执行记录视觉匹配Evidence相关逻辑。
        /// </summary>
        private void RecordVisualMatchEvidence(
            UIEffectNode node,
            SpriteEntry entry,
            float score,
            float margin)
        {
            if (node == null || entry == null)
            {
                return;
            }

            visualMatchEvidence[node] = new VisualMatchEvidence(
                GetSpriteResourcePath(entry),
                score,
                margin);
        }

        /// <summary>
        /// 执行解析相关逻辑。
        /// </summary>
        public Sprite Resolve(UIEffectNode node)
        {
            // Candidate paths are evidence, never an escape hatch after pixel rejection.
            if (matchMode == UIEffectResourceMatchMode.VisualSimilarity)
            {
                Sprite resolved = string.IsNullOrWhiteSpace(node.resource)
                    ? null : LoadSpriteAtPath(node.resource);
                if (resolved != null) RecordUsed(node.resource, resolved);
                return resolved;
            }

            List<string> candidates = GetCandidates(node);
            for (int index = 0; index < candidates.Count; index++)
            {
                string candidate = candidates[index];
                if (!IsAssetResourcePath(candidate))
                {
                    continue;
                }

                Sprite explicitSprite = LoadSpriteAtPath(candidate);
                if (explicitSprite != null)
                {
                    RecordUsed(candidate, explicitSprite);
                    return explicitSprite;
                }
            }

            SpriteEntry best = null;
            int bestScore = 0;
            foreach (SpriteEntry entry in sprites)
            {
                foreach (string candidate in candidates)
                {
                    int score = GetMatchScore(candidate, entry);
                    if (score > bestScore)
                    {
                        best = entry;
                        bestScore = score;
                    }
                }
            }

            if (best == null || bestScore < MinimumMatchScore)
            {
                return null;
            }

            RecordUsed(best.AssetPath, best.Sprite);
            return best.Sprite;
        }

        /// <summary>
        /// 执行记录Missing相关逻辑。
        /// </summary>
        public void RecordMissing(string nodePath, UIEffectNode node)
        {
            string semantic = node == null ||
                              string.IsNullOrWhiteSpace(node.semantic)
                ? node?.name ?? "Image"
                : node.semantic.Trim();
            string value = $"{nodePath} -> {semantic}";
            if (node != null &&
                visualFailures.TryGetValue(node, out string reason) &&
                !string.IsNullOrWhiteSpace(reason))
            {
                value += $"（{reason}）";
            }

            if (!missingResources.Contains(value))
            {
                missingResources.Add(value);
            }
        }

        /// <summary>
        /// 执行记录Used相关逻辑。
        /// </summary>
        private void RecordUsed(string assetPath, Sprite sprite)
        {
            string value = assetPath;
            if (assetPath.IndexOf('#') < 0 && !string.Equals(
                    sprite.name,
                    Path.GetFileNameWithoutExtension(assetPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                value += "#" + sprite.name;
            }

            if (!usedResources.Contains(value))
            {
                usedResources.Add(value);
            }
        }

        /// <summary>
        /// 获取Or构建精灵图索引。
        /// </summary>
        private static List<SpriteEntry> GetOrBuildSpriteIndex(
            string[] searchRoots)
        {
            string cacheKey = string.Join(
                "|",
                (searchRoots ?? Array.Empty<string>())
                .Select(path => (path ?? string.Empty)
                    .Trim()
                    .Replace('\\', '/'))
                .Where(path => path.Length > 0)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            if (SpriteIndexCache.TryGetValue(
                    cacheKey,
                    out List<SpriteEntry> cached))
            {
                return cached;
            }

            List<SpriteEntry> result = BuildSpriteIndex(searchRoots);
            SpriteIndexCache[cacheKey] = result;
            return result;
        }

        /// <summary>
        /// 构建精灵图索引。
        /// </summary>
        private static List<SpriteEntry> BuildSpriteIndex(
            string[] searchRoots)
        {
            var result = new List<SpriteEntry>();
            string[] stableRoots = (searchRoots ?? Array.Empty<string>())
                .Select(path => (path ?? string.Empty)
                    .Trim()
                    .Replace('\\', '/'))
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
            string[] guids = AssetDatabase.FindAssets(
                "t:Sprite",
                stableRoots);
            foreach (string path in guids
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(
                             path => path,
                             StringComparer.OrdinalIgnoreCase)
                             .ThenBy(path => path, StringComparer.Ordinal))
            {
                GetSourceTextureScale(
                    path,
                    out float sourceScaleX,
                    out float sourceScaleY);
                Sprite[] assetSprites = AssetDatabase.LoadAllAssetsAtPath(path)
                             .OfType<Sprite>()
                             .OrderBy(
                                 item => item.name,
                                 StringComparer.Ordinal)
                             .ThenBy(item => item.rect.x)
                             .ThenBy(item => item.rect.y)
                             .ThenBy(item => item.rect.width)
                             .ThenBy(item => item.rect.height).ToArray();
                foreach (Sprite sprite in assetSprites)
                {
                    result.Add(new SpriteEntry(
                        path,
                        sprite,
                        sourceScaleX,
                        sourceScaleY,
                        assetSprites.Length > 1));
                }
            }

            return result;
        }

        /// <summary>
        /// 获取源数据Texture缩放。
        /// </summary>
        private static void GetSourceTextureScale(
            string assetPath,
            out float scaleX,
            out float scaleY)
        {
            scaleX = 1f;
            scaleY = 1f;
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Texture2D imported =
                AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (importer == null || imported == null ||
                imported.width <= 0 || imported.height <= 0)
            {
                return;
            }

            importer.GetSourceTextureWidthAndHeight(
                out int sourceWidth,
                out int sourceHeight);
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return;
            }

            // Sprite.rect is expressed in the imported texture's pixel
            // space. maxTextureSize and platform overrides may downscale that
            // texture, but screenshot geometry and authored asset dimensions
            // remain in source-file pixels.
            scaleX = sourceWidth / (float) imported.width;
            scaleY = sourceHeight / (float) imported.height;
        }

        /// <summary>
        /// 加载精灵图At路径。
        /// </summary>
        private static Sprite LoadSpriteAtPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;
            assetPath = assetPath.Trim().Replace('\\', '/');
            string spriteName = string.Empty;
            int separator = assetPath.LastIndexOf('#');
            if (separator >= 0)
            {
                spriteName = assetPath.Substring(separator + 1);
                assetPath = assetPath.Substring(0, separator);
            }

            if (!IsAssetResourcePath(assetPath)) return null;
            if (separator < 0)
            {
                Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (direct != null) return direct;
            }

            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .FirstOrDefault(sprite =>
                    spriteName.Length == 0 ||
                    string.Equals(
                        sprite.name,
                        spriteName,
                        StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 获取Candidates。
        /// </summary>
        private static List<string> GetCandidates(UIEffectNode node)
        {
            var candidates = new List<string>();
            AddCandidate(candidates, node.resource);
            foreach (string candidate in node.resourceCandidates)
            {
                AddCandidate(candidates, candidate);
            }

            AddCandidate(candidates, node.semantic);
            AddCandidate(candidates, node.name);
            return candidates;
        }

        /// <summary>
        /// 添加Candidate。
        /// </summary>
        private static void AddCandidate(
            List<string> candidates,
            string value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                !candidates.Contains(value.Trim()))
            {
                candidates.Add(value.Trim());
            }
        }

        /// <summary>
        /// 获取MatchScore。
        /// </summary>
        private static int GetMatchScore(
            string candidate,
            SpriteEntry entry)
        {
            string normalizedCandidate = NormalizeKey(candidate);
            if (normalizedCandidate.Length < 3)
            {
                return 0;
            }

            if (normalizedCandidate == entry.NameKey)
            {
                return 1000;
            }

            if (normalizedCandidate == entry.FileKey)
            {
                return 950;
            }

            int score = 0;
            if (entry.NameKey.Contains(normalizedCandidate) ||
                normalizedCandidate.Contains(entry.NameKey))
            {
                score = Math.Max(score, 650);
            }

            if (entry.PathKey.Contains(normalizedCandidate))
            {
                score = Math.Max(score, 520);
            }

            string[] tokens = Tokenize(candidate);
            if (tokens.Length > 0)
            {
                int matches = tokens.Count(token =>
                    entry.PathKey.Contains(token));
                score = Math.Max(
                    score,
                    matches * 500 / tokens.Length);
            }

            return score;
        }

        /// <summary>
        /// 执行Tokenize相关逻辑。
        /// </summary>
        private static string[] Tokenize(string value)
        {
            return value
                .Split(
                    new[]
                    {
                        ' ', '_', '-', '.', '/', '\\',
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeKey)
                .Where(token => token.Length >= 3)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// 执行规范化Key相关逻辑。
        /// </summary>
        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string fileName = Path.GetFileNameWithoutExtension(value);
            var builder = new StringBuilder(fileName.Length);
            foreach (char character in fileName.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 执行ReconcileFull画布Backgrounds相关逻辑。
        /// </summary>
        private int ReconcileFullCanvasBackgrounds(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            float parentWidth,
            float parentHeight,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer)
        {
            if (nodes == null)
            {
                return 0;
            }

            int corrected = 0;
            foreach (UIEffectNode node in nodes.Where(item => item != null))
            {
                corrected += ReconcileFullCanvasBackgrounds(
                    node.children,
                    parentX + node.x,
                    parentY + node.y,
                    node.width,
                    node.height,
                    schema,
                    reference,
                    renderer);
            }

            // Work from the leaves upward. A flattened screenshot often
            // makes the schema author split one authored background into
            // several sibling panels. Those parts must be reconciled before
            // their wrapper can be tested against a larger parent Sprite.
            corrected += ReconcileSplitFullWidthBackgrounds(
                nodes,
                parentX,
                parentY,
                parentWidth,
                schema,
                reference,
                renderer);
            foreach (UIEffectNode node in nodes.Where(item => item != null))
            {
                if (TryExpandFullWidthBackground(
                        node,
                        parentX,
                        parentY,
                        parentWidth,
                        schema,
                        reference,
                        renderer))
                {
                    corrected++;
                }
            }

            return corrected;
        }

        /// <summary>
        /// 执行ReconcileSplitFullWidthBackgrounds相关逻辑。
        /// </summary>
        private int ReconcileSplitFullWidthBackgrounds(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            float parentWidth,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer)
        {
            if (nodes == null || nodes.Count < 2 || parentWidth <= 1f)
            {
                return 0;
            }

            int corrected = 0;
            for (int index = 0; index < nodes.Count - 1; index++)
            {
                UIEffectNode first = nodes[index];
                UIEffectNode second = nodes[index + 1];
                if (!CanProposeSplitFullWidthBackground(first) ||
                    !CanProposeSplitFullWidthBackground(second) ||
                    !TryGetAdjacentUnion(first, second, out Rect union) ||
                    Mathf.Abs(first.y - second.y) > 2f ||
                    Mathf.Abs(first.height - second.height) >
                    Mathf.Max(3f, union.height * 0.02f) ||
                    union.width < parentWidth *
                    SplitFullWidthMinimumCoverage ||
                    union.width >= parentWidth * 0.96f ||
                    union.height <= 32f ||
                    !(first.mayMerge || second.mayMerge ||
                      IsBackgroundSemantic(first) ||
                      IsBackgroundSemantic(second)))
                {
                    continue;
                }

                var occlusions = new List<Rect>();
                CollectVisualCoverage(
                    first.children,
                    parentX + first.x,
                    parentY + first.y,
                    occlusions);
                CollectVisualCoverage(
                    second.children,
                    parentX + second.x,
                    parentY + second.y,
                    occlusions);
                for (int siblingIndex = index + 2;
                     siblingIndex < nodes.Count;
                     siblingIndex++)
                {
                    CollectVisualCoverage(
                        nodes[siblingIndex],
                        parentX,
                        parentY,
                        occlusions);
                }

                VisualDescriptor source = CreateReferenceDescriptor(
                    reference,
                    schema.designWidth,
                    schema.designHeight,
                    parentX,
                    parentY + union.y,
                    parentWidth,
                    union.height,
                    occlusions);
                bool textCovered =
                    ContainsTextDescendant(first.children) ||
                    ContainsTextDescendant(second.children);
                SpriteEntry best = null;
                float bestScore = 0f;
                float secondScore = 0f;
                Color matchedColor = Color.white;
                bool matched = source != null &&
                               TryFindBestVisualMatch(
                                   source,
                                   false,
                                   false,
                                   textCovered,
                                   false,
                                   true,
                                   "background panel",
                                   renderer,
                                   out best,
                                   out bestScore,
                                   out secondScore,
                                   out matchedColor,
                                   entry =>
                                       !entry.HasBorder &&
                                       GetAspectSimilarity(
                                           parentWidth / union.height,
                                           entry.Aspect) >= 0.94f &&
                                       GetDimensionSimilarity(
                                           parentWidth,
                                           entry.AuthoredWidth) >= 0.9f &&
                                       GetDimensionSimilarity(
                                           union.height,
                                           entry.AuthoredHeight) >= 0.9f);
                if (!matched || best == null ||
                    bestScore < FullCanvasVisualSimilarity ||
                    bestScore - secondScore <= 0f)
                {
                    continue;
                }

                float previousFirstX = first.x;
                float previousFirstY = first.y;
                foreach (UIEffectNode child in first.children)
                {
                    child.x += previousFirstX;
                    child.y += previousFirstY - union.y;
                }

                foreach (UIEffectNode child in second.children)
                {
                    child.x += second.x;
                    child.y += second.y - union.y;
                    first.children.Add(child);
                }

                first.x = 0f;
                first.y = union.y;
                first.width = parentWidth;
                first.height = union.height;
                first.semantic = "background";
                first.resource = GetSpriteResourcePath(best);
                first.resourceCandidates.Clear();
                first.color = "#" +
                              ColorUtility.ToHtmlStringRGBA(matchedColor);
                first.intentionalColor = false;
                first.preserveAspect = false;
                first.sliced = false;
                first.mayMerge = false;
                first.mayUseFullCanvasSprite = false;
                visualFailures.Remove(first);
                visualFailures.Remove(second);
                RecordVisualMatchEvidence(
                    first,
                    best,
                    bestScore,
                    bestScore - secondScore);
                nodes.RemoveAt(index + 1);
                corrected++;
                Debug.Log(
                    $"[UIPrefabGenerator] 相邻背景 {first.name} 与 " +
                    $"{second.name} 还原为单张父级宽图 " +
                    $"{GetSpriteResourcePath(best)}（{bestScore:F3}/" +
                    $"{bestScore - secondScore:F3}）。");
                index--;
            }

            return corrected;
        }

        /// <summary>
        /// 执行判断能否ProposeSplitFullWidth背景相关逻辑。
        /// </summary>
        private static bool CanProposeSplitFullWidthBackground(
            UIEffectNode node)
        {
            return node != null &&
                   string.Equals(
                       node.type,
                       "Image",
                       StringComparison.OrdinalIgnoreCase) &&
                   !node.intentionalColor &&
                   !HasExplicitAssetResource(node) &&
                   node.width > 1f &&
                   node.height > 1f;
        }

        /// <summary>
        /// 尝试ExpandFull宽度背景，并返回是否成功。
        /// </summary>
        private bool TryExpandFullWidthBackground(
            UIEffectNode owner,
            float parentX,
            float parentY,
            float parentWidth,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer)
        {
            if (TryExpandDirectFullWidthBackground(
                    owner,
                    parentX,
                    parentY,
                    parentWidth,
                    schema,
                    reference,
                    renderer))
            {
                return true;
            }

            if (owner == null || parentWidth <= 1f ||
                !string.Equals(
                    owner.type,
                    "Container",
                    StringComparison.OrdinalIgnoreCase) ||
                owner.children == null || owner.children.Count == 0 ||
                owner.width >= parentWidth * 0.94f ||
                owner.width < parentWidth * 0.25f ||
                owner.height <= 32f)
            {
                return false;
            }

            List<UIEffectNode> backgroundParts =
                GetFullWidthBackgroundParts(owner);
            if (backgroundParts.Count == 0)
            {
                return false;
            }

            UIEffectNode background = backgroundParts[0];
            var backgroundPartSet = new HashSet<UIEffectNode>(
                backgroundParts);

            float absoluteY = parentY + owner.y;
            var occlusions = new List<Rect>();
            foreach (UIEffectNode child in owner.children)
            {
                if (child == null)
                {
                    continue;
                }

                if (backgroundPartSet.Contains(child))
                {
                    CollectVisualCoverage(
                        child.children,
                        parentX + owner.x + child.x,
                        absoluteY + child.y,
                        occlusions);
                }
                else
                {
                    CollectVisualCoverage(
                        child,
                        parentX + owner.x,
                        absoluteY,
                        occlusions);
                }
            }

            VisualDescriptor source = CreateReferenceDescriptor(
                reference,
                schema.designWidth,
                schema.designHeight,
                parentX,
                absoluteY,
                parentWidth,
                owner.height,
                occlusions);
            if (source == null)
            {
                return false;
            }

            float proposalAspect = parentWidth / owner.height;
            bool matched = TryFindBestVisualMatch(
                source,
                false,
                false,
                ContainsTextDescendant(owner.children),
                false,
                true,
                "background panel",
                renderer,
                out SpriteEntry best,
                out float bestScore,
                out float secondScore,
                out Color matchedColor,
                entry =>
                    !entry.HasBorder &&
                    GetAspectSimilarity(
                        proposalAspect,
                        entry.Aspect) >= 0.9f &&
                    GetDimensionSimilarity(
                        parentWidth,
                        entry.AuthoredWidth) >= 0.8f &&
                    GetDimensionSimilarity(
                        owner.height,
                        entry.AuthoredHeight) >= 0.8f);
            if (!matched || best == null ||
                bestScore < FullCanvasVisualSimilarity ||
                bestScore <= secondScore)
            {
                return false;
            }

            float previousOwnerX = owner.x;
            owner.x = 0f;
            owner.width = parentWidth;
            var contentChildren = new List<UIEffectNode>();
            foreach (UIEffectNode child in owner.children)
            {
                if (child == null)
                {
                    continue;
                }

                if (backgroundPartSet.Contains(child))
                {
                    foreach (UIEffectNode descendant in child.children)
                    {
                        descendant.x += previousOwnerX + child.x;
                        descendant.y += child.y;
                        contentChildren.Add(descendant);
                    }

                    visualFailures.Remove(child);
                }
                else
                {
                    // Moving the owner to the parent origin must not move any
                    // of its existing content on the design canvas.
                    child.x += previousOwnerX;
                    contentChildren.Add(child);
                }
            }

            background.children.Clear();
            background.x = 0f;
            background.y = 0f;
            background.width = parentWidth;
            background.height = owner.height;
            background.semantic = "background";
            background.resource = GetSpriteResourcePath(best);
            background.resourceCandidates.Clear();
            background.color = "#" +
                               ColorUtility.ToHtmlStringRGBA(
                                   matchedColor);
            background.intentionalColor = false;
            background.preserveAspect = false;
            background.sliced = false;
            background.mayMerge = false;
            background.mayUseFullCanvasSprite = false;
            visualFailures.Remove(background);
            RecordVisualMatchEvidence(
                background,
                best,
                bestScore,
                bestScore - secondScore);

            // The matched full background now owns the visual region. Put
            // existing content beneath it so later parent-integration checks
            // can prove that a semantic wrapper or text is already present
            // in the Sprite. Coordinates stay unchanged because both the
            // owner and background use the same local origin.
            owner.children.Clear();
            owner.children.Add(background);
            background.children.AddRange(contentChildren);

            Debug.Log(
                $"[UIPrefabGenerator] {owner.name} 扩展为整幅背景，" +
                $"命中 {GetSpriteResourcePath(best)}（{bestScore:F3}/" +
                $"{bestScore - secondScore:F3}）。");
            return true;
        }

        /// <summary>
        /// 尝试ExpandDirectFull宽度背景，并返回是否成功。
        /// </summary>
        private bool TryExpandDirectFullWidthBackground(
            UIEffectNode owner,
            float parentX,
            float parentY,
            float parentWidth,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer)
        {
            if (owner == null || parentWidth <= 1f ||
                !string.Equals(
                    owner.type,
                    "Image",
                    StringComparison.OrdinalIgnoreCase) ||
                owner.intentionalColor ||
                HasExplicitAssetResource(owner) ||
                owner.width >= parentWidth * 0.94f ||
                owner.width < parentWidth * 0.25f ||
                owner.height <= 32f ||
                !(owner.mayUseFullCanvasSprite ||
                  IsBackgroundSemantic(owner)))
            {
                return false;
            }

            float absoluteY = parentY + owner.y;
            var occlusions = new List<Rect>();
            CollectVisualCoverage(
                owner.children,
                parentX + owner.x,
                absoluteY,
                occlusions);
            VisualDescriptor source = CreateReferenceDescriptor(
                reference,
                schema.designWidth,
                schema.designHeight,
                parentX,
                absoluteY,
                parentWidth,
                owner.height,
                occlusions);
            if (source == null)
            {
                return false;
            }

            float proposalAspect = parentWidth / owner.height;
            bool matched = TryFindBestVisualMatch(
                source,
                false,
                false,
                ContainsTextDescendant(owner.children),
                false,
                true,
                "background panel",
                renderer,
                out SpriteEntry best,
                out float bestScore,
                out float secondScore,
                out Color matchedColor,
                entry =>
                    !entry.HasBorder &&
                    GetAspectSimilarity(
                        proposalAspect,
                        entry.Aspect) >= 0.9f &&
                    GetDimensionSimilarity(
                        parentWidth,
                        entry.AuthoredWidth) >= 0.8f &&
                    GetDimensionSimilarity(
                        owner.height,
                        entry.AuthoredHeight) >= 0.8f);
            if (!matched || best == null ||
                bestScore < FullCanvasVisualSimilarity ||
                bestScore <= secondScore)
            {
                return false;
            }

            float previousX = owner.x;
            owner.x = 0f;
            owner.width = parentWidth;
            foreach (UIEffectNode child in
                     owner.children ?? new List<UIEffectNode>())
            {
                child.x += previousX;
            }

            owner.semantic = "background";
            owner.resource = GetSpriteResourcePath(best);
            owner.resourceCandidates.Clear();
            owner.color = "#" +
                          ColorUtility.ToHtmlStringRGBA(matchedColor);
            owner.intentionalColor = false;
            owner.preserveAspect = false;
            owner.sliced = false;
            owner.mayMerge = false;
            owner.mayUseFullCanvasSprite = false;
            visualFailures.Remove(owner);
            RecordVisualMatchEvidence(
                owner,
                best,
                bestScore,
                bestScore - secondScore);

            Debug.Log(
                $"[UIPrefabGenerator] {owner.name} 从局部可见矩形" +
                $"恢复为父级完整背景，命中 " +
                $"{GetSpriteResourcePath(best)}（{bestScore:F3}/" +
                $"{bestScore - secondScore:F3}）。");
            return true;
        }

        /// <summary>
        /// 获取Full宽度背景Parts。
        /// </summary>
        private static List<UIEffectNode> GetFullWidthBackgroundParts(
            UIEffectNode owner)
        {
            var result = new List<UIEffectNode>();
            if (owner?.children == null || owner.width <= 1f ||
                owner.height <= 1f)
            {
                return result;
            }

            float widthTolerance = Mathf.Max(3f, owner.width * 0.02f);
            float heightTolerance = Mathf.Max(3f, owner.height * 0.02f);
            UIEffectNode single = owner.children.FirstOrDefault(child =>
                IsVisualNode(child) &&
                !HasExplicitAssetResource(child) &&
                Mathf.Abs(child.x) <= 2f &&
                Mathf.Abs(child.y) <= 2f &&
                Mathf.Abs(child.width - owner.width) <= widthTolerance &&
                Mathf.Abs(child.height - owner.height) <=
                heightTolerance &&
                (owner.mayUseFullCanvasSprite ||
                 child.mayUseFullCanvasSprite ||
                 IsBackgroundSemantic(child)));
            if (single != null)
            {
                result.Add(single);
                return result;
            }

            List<UIEffectNode> adjacent = owner.children
                .Where(child =>
                    CanProposeSplitFullWidthBackground(child) &&
                    Mathf.Abs(child.y) <= 2f &&
                    Mathf.Abs(child.height - owner.height) <=
                    heightTolerance)
                .OrderBy(child => child.x)
                .ToList();
            if (adjacent.Count < 2 ||
                Mathf.Abs(adjacent[0].x) > widthTolerance ||
                Mathf.Abs(
                    adjacent[adjacent.Count - 1].x +
                    adjacent[adjacent.Count - 1].width - owner.width) >
                widthTolerance ||
                !(owner.mayUseFullCanvasSprite ||
                  adjacent.Any(child =>
                      child.mayUseFullCanvasSprite ||
                      child.mayMerge ||
                      IsBackgroundSemantic(child))))
            {
                return result;
            }

            for (int index = 1; index < adjacent.Count; index++)
            {
                float previousEnd = adjacent[index - 1].x +
                                    adjacent[index - 1].width;
                if (Mathf.Abs(adjacent[index].x - previousEnd) >
                    widthTolerance)
                {
                    return result;
                }
            }

            result.AddRange(adjacent);
            return result;
        }

        /// <summary>
        /// 执行判断是否背景Semantic相关逻辑。
        /// </summary>
        private static bool IsBackgroundSemantic(UIEffectNode node)
        {
            string hint = NormalizeSemanticPhrase(
                (node?.semantic ?? string.Empty) + " " +
                (node?.visualKind ?? string.Empty) + " " +
                (node?.name ?? string.Empty));
            return ContainsAny(
                hint,
                "background",
                "backdrop",
                "panel",
                "dialog",
                "背景",
                "底图");
        }

        /// <summary>
        /// 获取DimensionSimilarity。
        /// </summary>
        private static float GetDimensionSimilarity(
            float first,
            float second)
        {
            return first <= 0f || second <= 0f
                ? 0f
                : Mathf.Min(first, second) / Mathf.Max(first, second);
        }

        /// <summary>
        /// 执行Coalesce背景Regions相关逻辑。
        /// </summary>
        private void CoalesceBackgroundRegions(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            bool merged;
            do
            {
                merged = false;
                for (int firstIndex = 0;
                     firstIndex < nodes.Count - 1 && !merged;
                     firstIndex++)
                {
                    UIEffectNode first = nodes[firstIndex];
                    if (!CanCoalesceBackground(first))
                    {
                        continue;
                    }

                    for (int secondIndex = firstIndex + 1;
                         secondIndex < nodes.Count;
                         secondIndex++)
                    {
                        UIEffectNode second = nodes[secondIndex];
                        if (!CanCoalesceBackground(second) ||
                            !TryGetAdjacentUnion(first, second, out Rect union) ||
                            HasEarlierVisualOverlap(
                                nodes,
                                firstIndex,
                                parentX,
                                parentY,
                                union))
                        {
                            continue;
                        }

                        var occlusions = new List<Rect>();
                        CollectVisualCoverage(
                            first.children,
                            parentX + first.x,
                            parentY + first.y,
                            occlusions);
                        CollectVisualCoverage(
                            second.children,
                            parentX + second.x,
                            parentY + second.y,
                            occlusions);
                        for (int siblingIndex = firstIndex + 1;
                             siblingIndex < nodes.Count;
                             siblingIndex++)
                        {
                            if (siblingIndex == secondIndex)
                            {
                                continue;
                            }

                            CollectVisualCoverage(
                                nodes[siblingIndex],
                                parentX,
                                parentY,
                                occlusions);
                        }

                        VisualDescriptor source = CreateReferenceDescriptor(
                            reference,
                            schema.designWidth,
                            schema.designHeight,
                            parentX + union.x,
                            parentY + union.y,
                            union.width,
                            union.height,
                            occlusions);
                        bool allowOrdinarySprite =
                            first.mayMerge || second.mayMerge;
                        if (source == null)
                        {
                            continue;
                        }

                        bool matched = TryFindBestVisualMatch(
                                source,
                                 true,
                                 !allowOrdinarySprite,
                                 ContainsTextDescendant(first.children) ||
                                 ContainsTextDescendant(second.children),
                                 false,
                                 false,
                                 string.Empty,
                                renderer,
                                out SpriteEntry best,
                                out float bestScore,
                                out float secondScore,
                                out Color matchedColor,
                                entry => entry.HasBorder ||
                                    allowOrdinarySprite &&
                                    GetAspectSimilarity(
                                        union.width / union.height,
                                        entry.Aspect) >= 0.88f &&
                                    GetDimensionSimilarity(
                                        union.width,
                                        entry.AuthoredWidth) >= 0.72f &&
                                    GetDimensionSimilarity(
                                        union.height,
                                        entry.AuthoredHeight) >= 0.72f);
                        if (!matched ||
                            best == null ||
                            !best.HasBorder &&
                            (bestScore < FullCanvasVisualSimilarity ||
                             bestScore - secondScore < 0.015f))
                        {
                            continue;
                        }

                        float previousFirstX = first.x;
                        float previousFirstY = first.y;
                        foreach (UIEffectNode child in first.children)
                        {
                            child.x += previousFirstX - union.x;
                            child.y += previousFirstY - union.y;
                        }

                        foreach (UIEffectNode child in second.children)
                        {
                            child.x += second.x - union.x;
                            child.y += second.y - union.y;
                            first.children.Add(child);
                        }

                        first.x = union.x;
                        first.y = union.y;
                        first.width = union.width;
                        first.height = union.height;
                        first.resource = GetSpriteResourcePath(best);
                        first.resourceCandidates.Clear();
                        first.color = "#" +
                                      ColorUtility.ToHtmlStringRGBA(
                                          matchedColor);
                        first.intentionalColor = false;
                        first.preserveAspect = false;
                        first.sliced = best.HasBorder;
                        first.mayMerge = false;
                        nodes.RemoveAt(secondIndex);
                        merged = true;
                        break;
                    }
                }
            }
            while (merged);

            foreach (UIEffectNode node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                CoalesceBackgroundRegions(
                    node.children,
                    parentX + node.x,
                    parentY + node.y,
                    schema,
                    reference,
                    renderer);
            }
        }

        /// <summary>
        /// 执行判断能否Coalesce背景相关逻辑。
        /// </summary>
        private static bool CanCoalesceBackground(UIEffectNode node)
        {
            return node != null &&
                   string.Equals(
                       node.type,
                       "Image",
                       StringComparison.OrdinalIgnoreCase) &&
                   !node.intentionalColor &&
                   string.IsNullOrWhiteSpace(node.resource) &&
                   (node.resourceCandidates == null ||
                    node.resourceCandidates.Count == 0) &&
                   ((node.children == null || node.children.Count == 0) ||
                    node.mayMerge) &&
                   node.width > 1f &&
                   node.height > 1f;
        }

        /// <summary>
        /// 尝试获取AdjacentUnion，并返回是否成功。
        /// </summary>
        private static bool TryGetAdjacentUnion(
            UIEffectNode first,
            UIEffectNode second,
            out Rect union)
        {
            union = default;
            float tolerance = Mathf.Max(
                2f,
                Mathf.Min(
                    Mathf.Min(first.width, first.height),
                    Mathf.Min(second.width, second.height)) * 0.01f);
            bool vertical =
                Mathf.Abs(first.x - second.x) <= tolerance &&
                Mathf.Abs(first.width - second.width) <= tolerance &&
                (Mathf.Abs(first.y + first.height - second.y) <= tolerance ||
                 Mathf.Abs(second.y + second.height - first.y) <= tolerance);
            bool horizontal =
                Mathf.Abs(first.y - second.y) <= tolerance &&
                Mathf.Abs(first.height - second.height) <= tolerance &&
                (Mathf.Abs(first.x + first.width - second.x) <= tolerance ||
                 Mathf.Abs(second.x + second.width - first.x) <= tolerance);
            if (!vertical && !horizontal)
            {
                return false;
            }

            float left = Mathf.Min(first.x, second.x);
            float top = Mathf.Min(first.y, second.y);
            float right = Mathf.Max(
                first.x + first.width,
                second.x + second.width);
            float bottom = Mathf.Max(
                first.y + first.height,
                second.y + second.height);
            union = new Rect(left, top, right - left, bottom - top);
            return true;
        }

        /// <summary>
        /// 执行判断是否Earlier视觉Overlap相关逻辑。
        /// </summary>
        private static bool HasEarlierVisualOverlap(
            IReadOnlyList<UIEffectNode> nodes,
            int beforeIndex,
            float parentX,
            float parentY,
            Rect localUnion)
        {
            Rect absoluteUnion = new Rect(
                parentX + localUnion.x,
                parentY + localUnion.y,
                localUnion.width,
                localUnion.height);
            var coverage = new List<Rect>();
            for (int index = 0; index < beforeIndex; index++)
            {
                CollectVisualCoverage(
                    nodes[index],
                    parentX,
                    parentY,
                    coverage);
            }

            return coverage.Any(rect => rect.Overlaps(absoluteUnion));
        }

        /// <summary>
        /// 执行收集视觉Coverage相关逻辑。
        /// </summary>
        private static void CollectVisualCoverage(
            IEnumerable<UIEffectNode> nodes,
            float parentX,
            float parentY,
            ICollection<Rect> coverage)
        {
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                CollectVisualCoverage(node, parentX, parentY, coverage);
            }
        }

        /// <summary>
        /// 执行收集视觉Coverage相关逻辑。
        /// </summary>
        private static void CollectVisualCoverage(
            UIEffectNode node,
            float parentX,
            float parentY,
            ICollection<Rect> coverage)
        {
            if (node == null)
            {
                return;
            }

            float absoluteX = parentX + node.x;
            float absoluteY = parentY + node.y;
            if (IsRenderedGraphicNode(node))
            {
                coverage.Add(new Rect(
                    absoluteX,
                    absoluteY,
                    node.width,
                    node.height));
                return;
            }

            CollectVisualCoverage(
                node.children,
                absoluteX,
                absoluteY,
                coverage);
        }

        /// <summary>
        /// 执行判断是否RenderedGraphic节点相关逻辑。
        /// </summary>
        private static bool IsRenderedGraphicNode(UIEffectNode node)
        {
            return IsVisualNode(node) ||
                   string.Equals(
                       node.type,
                       "ScrollRect",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 执行收集文本Coverage相关逻辑。
        /// </summary>
        private static void CollectTextCoverage(
            IEnumerable<UIEffectNode> nodes,
            float parentX,
            float parentY,
            ICollection<Rect> coverage)
        {
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                CollectTextCoverage(node, parentX, parentY, coverage);
            }
        }

        /// <summary>
        /// 执行收集文本Coverage相关逻辑。
        /// </summary>
        private static void CollectTextCoverage(
            UIEffectNode node,
            float parentX,
            float parentY,
            ICollection<Rect> coverage)
        {
            if (node == null)
            {
                return;
            }

            float absoluteX = parentX + node.x;
            float absoluteY = parentY + node.y;
            if (string.Equals(
                    node.type,
                    "Text",
                    StringComparison.OrdinalIgnoreCase))
            {
                // A Text RectTransform describes layout, not the glyph
                // pixels that actually cover the screenshot. Keep it as a
                // soft occlusion so background matching can use robust
                // outlier rejection instead of discarding the whole area.
                coverage.Add(new Rect(
                    absoluteX,
                    absoluteY,
                    node.width,
                    node.height));
                return;
            }

            CollectTextCoverage(
                node.children,
                absoluteX,
                absoluteY,
                coverage);
        }

        /// <summary>
        /// 执行MaterializeMissing文本Surfaces相关逻辑。
        /// </summary>
        private int MaterializeMissingTextSurfaces(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            float parentWidth,
            float parentHeight,
            bool parentIsVisual,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return 0;
            }

            int created = 0;
            if (!parentIsVisual)
            {
                for (int index = 0; index < nodes.Count; index++)
                {
                    UIEffectNode seed = nodes[index];
                    if (!CanReceiveGeneratedTextSurface(seed))
                    {
                        continue;
                    }

                    string groupKey = GetTextSurfaceGroupKey(seed);
                    var groupIndices = new List<int> { index };
                    for (int candidateIndex = index + 1;
                         candidateIndex < nodes.Count;
                         candidateIndex++)
                    {
                        UIEffectNode candidate = nodes[candidateIndex];
                        if (!CanReceiveGeneratedTextSurface(candidate) ||
                            groupKey.Length == 0 ||
                            !string.Equals(
                                groupKey,
                                GetTextSurfaceGroupKey(candidate),
                                StringComparison.OrdinalIgnoreCase) ||
                            !AreTextsOnSameRow(seed, candidate))
                        {
                            continue;
                        }

                        groupIndices.Add(candidateIndex);
                    }

                    List<UIEffectNode> textNodes = groupIndices
                        .Select(groupIndex => nodes[groupIndex])
                        .ToList();
                    if (!TryCreateTextSurface(
                            textNodes,
                            nodes,
                            parentX,
                            parentY,
                            parentWidth,
                            parentHeight,
                            schema,
                            reference,
                            renderer,
                            out UIEffectNode surface,
                            out float matchScore,
                            out float matchMargin))
                    {
                        continue;
                    }

                    for (int removeIndex = groupIndices.Count - 1;
                         removeIndex >= 0;
                         removeIndex--)
                    {
                        nodes.RemoveAt(groupIndices[removeIndex]);
                    }

                    nodes.Insert(index, surface);
                    created++;
                    Debug.Log(
                        $"[UIPrefabGenerator] 为文字组 {surface.name} " +
                        $"补建 Sprite 承载节点 {surface.resource}" +
                        $"（{matchScore:F3}/{matchMargin:F3}）。");
                }
            }

            foreach (UIEffectNode node in nodes
                         .Where(item => item != null)
                         .ToList())
            {
                created += MaterializeMissingTextSurfaces(
                    node.children,
                    parentX + node.x,
                    parentY + node.y,
                    node.width,
                    node.height,
                    IsVisualNode(node),
                    schema,
                    reference,
                    renderer);
            }

            return created;
        }

        /// <summary>
        /// 执行判断能否Receive生成结果文本Surface相关逻辑。
        /// </summary>
        private static bool CanReceiveGeneratedTextSurface(
            UIEffectNode node)
        {
            return node != null &&
                   string.Equals(
                       node.type,
                       "Text",
                       StringComparison.OrdinalIgnoreCase) &&
                   !LooksLikeArtworkText(node) &&
                   !string.IsNullOrWhiteSpace(node.text) &&
                   node.width > 8f &&
                   node.height > 8f;
        }

        /// <summary>
        /// 执行AreTextsOnSameRow相关逻辑。
        /// </summary>
        private static bool AreTextsOnSameRow(
            UIEffectNode first,
            UIEffectNode second)
        {
            float firstCenter = first.y + first.height * 0.5f;
            float secondCenter = second.y + second.height * 0.5f;
            return Mathf.Abs(firstCenter - secondCenter) <=
                   Mathf.Max(first.height, second.height) * 0.35f;
        }

        /// <summary>
        /// 获取文本Surface分组键。
        /// </summary>
        private static string GetTextSurfaceGroupKey(UIEffectNode node)
        {
            string key = NormalizeKey(node?.name ?? string.Empty);
            string[] suffixes =
            {
                "label",
                "value",
                "bonus",
                "amount",
                "number",
                "text",
                "title",
                "name",
            };
            bool removed;
            do
            {
                removed = false;
                foreach (string suffix in suffixes)
                {
                    if (key.Length <= suffix.Length ||
                        !key.EndsWith(
                            suffix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    key = key.Substring(0, key.Length - suffix.Length);
                    removed = true;
                    break;
                }
            }
            while (removed);
            return key;
        }

        /// <summary>
        /// 尝试创建文本Surface，并返回是否成功。
        /// </summary>
        private bool TryCreateTextSurface(
            IReadOnlyList<UIEffectNode> textNodes,
            IReadOnlyList<UIEffectNode> siblings,
            float parentX,
            float parentY,
            float parentWidth,
            float parentHeight,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            out UIEffectNode surface,
            out float matchScore,
            out float matchMargin)
        {
            surface = null;
            matchScore = 0f;
            matchMargin = 0f;
            if (textNodes == null || textNodes.Count == 0 ||
                parentWidth <= 1f || parentHeight <= 1f)
            {
                return false;
            }

            float left = textNodes.Min(node => node.x);
            float top = textNodes.Min(node => node.y);
            float right = textNodes.Max(node => node.x + node.width);
            float bottom = textNodes.Max(node => node.y + node.height);
            float contentWidth = right - left;
            float contentHeight = bottom - top;
            float centerX = (left + right) * 0.5f;
            float centerY = (top + bottom) * 0.5f;
            float[] widthScales = { 1.04f, 1.16f, 1.28f, 1.5f };
            SpriteEntry selected = null;
            Color selectedColor = Color.white;
            Rect selectedRect = default;
            string groupKey = GetTextSurfaceGroupKey(textNodes[0]);
            Rect[] textRects = textNodes.Select(text => new Rect(parentX + text.x, parentY + text.y,
                text.width, text.height)).ToArray();

            foreach (float widthScale in widthScales)
            {
                float targetWidth = Mathf.Clamp(
                    Mathf.Max(contentWidth + 8f, contentWidth * widthScale),
                    contentWidth,
                    parentWidth);
                float targetHeight = Mathf.Clamp(
                    Mathf.Max(contentHeight + 4f, contentHeight * 1.05f),
                    contentHeight,
                    parentHeight);
                float targetX = Mathf.Clamp(
                    centerX - targetWidth * 0.5f,
                    0f,
                    Mathf.Max(0f, parentWidth - targetWidth));
                float targetY = Mathf.Clamp(
                    centerY - targetHeight * 0.5f,
                    0f,
                    Mathf.Max(0f, parentHeight - targetHeight));
                // A high Sprite score alone is not evidence that a separate plate
                // exists. Smooth parent backgrounds often resemble a stretched bar.
                var targetRect = new Rect(parentX + targetX, parentY + targetY, targetWidth, targetHeight);
                if (!HasIndependentTextSurfaceBoundary(reference, schema, targetRect, textRects))
                    continue;
                VisualDescriptor source = CreateReferenceDescriptor(
                    reference,
                    schema.designWidth,
                    schema.designHeight,
                    parentX + targetX,
                    parentY + targetY,
                    targetWidth,
                    targetHeight,
                    Array.Empty<Rect>());
                SpriteEntry best = null;
                float bestScore = 0f;
                float secondScore = 0f;
                Color matchedColor = Color.white;
                bool matched = source != null &&
                               TryFindBestVisualMatch(
                                   source,
                                   true,
                                   true,
                                   true,
                                   false,
                                   false,
                                   "field bar plate background " + groupKey,
                                   renderer,
                                   out best,
                                   out bestScore,
                                   out secondScore,
                                   out matchedColor,
                                   entry => entry.HasBorder);
                float margin = bestScore - secondScore;
                if (!matched || best == null ||
                    bestScore < GeneratedTextSurfaceSimilarity ||
                    margin < GeneratedTextSurfaceMargin ||
                    (selected != null && bestScore <= matchScore))
                {
                    continue;
                }

                selected = best;
                selectedColor = matchedColor;
                selectedRect = new Rect(
                    targetX,
                    targetY,
                    targetWidth,
                    targetHeight);
                matchScore = bestScore;
                matchMargin = margin;
            }

            if (selected == null)
            {
                return false;
            }

            string surfaceName = GetUniqueSiblingName(
                siblings,
                textNodes[0].name + "Background");
            surface = new UIEffectNode
            {
                name = surfaceName,
                type = "Image",
                semantic = "field",
                visualKind = "GeneratedTextSurface",
                x = selectedRect.x,
                y = selectedRect.y,
                width = selectedRect.width,
                height = selectedRect.height,
                color = "#" +
                        ColorUtility.ToHtmlStringRGBA(selectedColor),
                resource = GetSpriteResourcePath(selected),
                sliced = true,
                binding = "No",
            };
            foreach (UIEffectNode textNode in textNodes)
            {
                textNode.x -= selectedRect.x;
                textNode.y -= selectedRect.y;
                surface.children.Add(textNode);
            }

            visualFailures.Remove(surface);
            return true;
        }

        private static bool HasIndependentTextSurfaceBoundary(Texture2D reference, UIEffectSchema schema,
            Rect rect, IReadOnlyList<Rect> textRects)
        {
            // Require a coherent pair of opposite edges. Compare the cross-edge
            // jump with nearby continuation so gradients and texture are not plates.
            var edges = new bool[4];
            var canvas = new Rect(0f, 0f, schema.designWidth, schema.designHeight);
            const float distance = 1.5f;
            for (int side = 0; side < 4; side++)
            {
                int visible = 0, supported = 0;
                float totalJump = 0f;
                for (int sample = 0; sample < 16; sample++)
                {
                    float t = (sample + .5f) / 16f;
                    Vector2 point = side < 2
                        ? new Vector2(side == 0 ? rect.xMin : rect.xMax, Mathf.Lerp(rect.yMin, rect.yMax, t))
                        : new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, t), side == 2 ? rect.yMin : rect.yMax);
                    Vector2 inward = side == 0 ? Vector2.right : side == 1 ? Vector2.left :
                        side == 2 ? Vector2.up : Vector2.down;
                    if (!TrySampleTextSurfaceEdge(reference, schema, canvas, textRects, point, inward,
                            distance, out float jump, out bool supportsEdge))
                        continue;
                    visible++;
                    if (supportsEdge)
                    {
                        supported++;
                        totalJump += jump;
                    }
                }
                edges[side] = visible >= 6 && supported >= Mathf.CeilToInt(visible * .6f) &&
                    totalJump / Mathf.Max(1, supported) >= .025f;
                if (!edges[side]) continue;

                // An ancestor's long edges also cross this rectangle. A newly
                // inferred plate needs edges which end near its own corners.
                int extended = 0;
                foreach (float t in new[] { -.4f, -.2f, 1.2f, 1.4f })
                {
                    Vector2 point = side < 2
                        ? new Vector2(side == 0 ? rect.xMin : rect.xMax, Mathf.LerpUnclamped(rect.yMin, rect.yMax, t))
                        : new Vector2(Mathf.LerpUnclamped(rect.xMin, rect.xMax, t), side == 2 ? rect.yMin : rect.yMax);
                    Vector2 inward = side == 0 ? Vector2.right : side == 1 ? Vector2.left :
                        side == 2 ? Vector2.up : Vector2.down;
                    if (TrySampleTextSurfaceEdge(reference, schema, canvas, textRects, point, inward,
                            distance, out _, out bool supportsEdge) && supportsEdge)
                        extended++;
                }
                if (extended >= 2) edges[side] = false;
            }
            return edges[0] && edges[1] || edges[2] && edges[3];
        }

        private static bool TrySampleTextSurfaceEdge(Texture2D reference, UIEffectSchema schema,
            Rect canvas, IReadOnlyList<Rect> textRects, Vector2 point, Vector2 inward, float distance,
            out float jump, out bool supportsEdge)
        {
            jump = 0f;
            supportsEdge = false;
            Vector2 inner = point + inward * distance;
            Vector2 outer = point - inward * distance;
            Vector2 innerFar = point + inward * (distance * 3f);
            Vector2 outerFar = point - inward * (distance * 3f);
            if (!canvas.Contains(innerFar) || !canvas.Contains(outerFar) ||
                textRects.Any(text => text.Contains(inner) || text.Contains(outer) || text.Contains(outerFar)))
                return false;
            Color a = SampleReferencePoint(reference, schema, inner);
            Color b = SampleReferencePoint(reference, schema, outer);
            jump = GetColorError(a, b);
            float continuation = GetColorError(b, SampleReferencePoint(reference, schema, outerFar));
            if (!textRects.Any(text => text.Contains(innerFar)))
                continuation = Mathf.Max(continuation,
                    GetColorError(a, SampleReferencePoint(reference, schema, innerFar)));
            supportsEdge = jump >= .02f && jump > continuation * 1.8f + .01f;
            return true;
        }

        private static Color SampleReferencePoint(Texture2D reference, UIEffectSchema schema, Vector2 point)
        {
            return reference.GetPixelBilinear(point.x / schema.designWidth, 1f - point.y / schema.designHeight);
        }

        /// <summary>
        /// 执行匹配视觉节点相关逻辑。
        /// </summary>
        private void MatchVisualNodes(
            IEnumerable<UIEffectNode> nodes,
            float parentX,
            float parentY,
            float parentWidth,
            float parentHeight,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            IReadOnlyCollection<Rect> inheritedOcclusions,
            IReadOnlyCollection<Rect> inheritedTextOcclusions,
            string parentSemanticHint,
            VisualParentContext visualParent)
        {
            List<UIEffectNode> nodeList =
                (nodes ?? Enumerable.Empty<UIEffectNode>())
                .Where(node => node != null)
                .ToList();
            var laterOcclusions = new List<Rect>(
                inheritedOcclusions ?? Array.Empty<Rect>());
            var laterTextOcclusions = new List<Rect>(
                inheritedTextOcclusions ?? Array.Empty<Rect>());
            for (int nodeIndex = nodeList.Count - 1;
                 nodeIndex >= 0;
                 nodeIndex--)
            {
                UIEffectNode node = nodeList[nodeIndex];
                float absoluteX = parentX + node.x;
                float absoluteY = parentY + node.y;
                var sourceOcclusions = new List<Rect>(laterOcclusions);
                CollectVisualCoverage(
                    node.children,
                    absoluteX,
                    absoluteY,
                    sourceOcclusions);
                var sourceTextOcclusions =
                    new List<Rect>(laterTextOcclusions);
                CollectTextCoverage(
                    node.children,
                    absoluteX,
                    absoluteY,
                    sourceTextOcclusions);
                IReadOnlyCollection<Rect> backdropOcclusions =
                    BuildBackdropOcclusions(
                        nodeList,
                        node,
                        parentX,
                        parentY,
                        inheritedOcclusions,
                        inheritedTextOcclusions);
                VisualDescriptor nodeSource = null;
                bool containerProbe = collectNodeEvidence && string.Equals(
                    node.type, "Container", StringComparison.OrdinalIgnoreCase);
                bool artworkProbe = collectNodeEvidence && LooksLikeArtworkText(node);
                if ((IsVisualNode(node) || containerProbe || artworkProbe) &&
                    !HasExplicitAssetResource(node) &&
                    !string.Equals(node.resourcePolicy, "ColorFallback", StringComparison.OrdinalIgnoreCase) &&
                    !ShouldKeepIntentionalColor(
                        node,
                        schema,
                        reference,
                        absoluteX,
                        absoluteY))
                {
                    bool wasIntentionalColor = node.intentionalColor;
                    nodeSource = CreateReferenceDescriptor(
                        reference,
                        schema.designWidth,
                        schema.designHeight,
                        absoluteX,
                        absoluteY,
                        node.width,
                        node.height,
                        sourceOcclusions,
                        backdropOcclusions);
                    ApplyVisualParentBackdrop(
                        nodeSource,
                        absoluteX,
                        absoluteY,
                        node.width,
                        node.height,
                        visualParent);
                    VisualDescriptor source = nodeSource;
                    SpriteEntry best = null;
                    float bestScore = 0f;
                    float secondScore = 0f;
                    Rect nodeRect = new Rect(
                        absoluteX,
                        absoluteY,
                        node.width,
                        node.height);
                    bool hasOverlappingOcclusions =
                        sourceOcclusions.Any(rect => rect.Overlaps(nodeRect));
                    bool hasOverlappingText =
                        sourceTextOcclusions.Any(
                            rect => rect.Overlaps(nodeRect));
                    // Baked rank digits are evidence for the badge Sprite, not text noise.
                    if (IsShapeSensitiveVisualNode(node, schema) && node.children.Count > 0 &&
                        node.children.All(child => string.Equals(child.type, "Text", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(child.textMode, "PossiblyBaked", StringComparison.OrdinalIgnoreCase)))
                        hasOverlappingText = false;
                    Color matchedColor = Color.white;
                    string semanticHint = GetVisualSemanticHint(node);
                    string contextualSemanticHint = semanticHint + " " +
                                                    parentSemanticHint;
                    bool compactGraphic = IsCompactGraphicNode(
                        node,
                        schema);
                    var rankedEvidence = collectNodeEvidence
                        ? new List<VisualCandidate>() : null;
                    bool matched = source != null &&
                                   TryFindBestVisualMatch(
                                       source,
                                       hasOverlappingOcclusions ||
                                       hasOverlappingText,
                                        false,
                                        hasOverlappingText,
                                        compactGraphic,
                                        IsShapeSensitiveVisualNode(
                                            node,
                                            schema),
                                        contextualSemanticHint,
                                        renderer,
                                       out best,
                                       out bestScore,
                                       out secondScore,
                                       out matchedColor,
                                       candidateFilter: artworkProbe
                                           ? entry => IsArtworkCandidate(entry, source, renderer) : (Func<SpriteEntry, bool>)null,
                                       rankedCandidates: rankedEvidence,
                                       preferredResources: node.resourceCandidates);
                    if (collectNodeEvidence)
                    {
                        CaptureNodeEvidence(node, nodeRect, rankedEvidence, matched,
                            compactGraphic || artworkProbe, containerProbe);
                    }
                    if (!matched && compactGraphic && !collectNodeEvidence)
                    {
                        matched = TryFindExpandedCompactGraphicMatch(
                            node,
                            parentX,
                            parentY,
                            parentWidth,
                            parentHeight,
                            schema,
                            reference,
                            renderer,
                            sourceOcclusions,
                            backdropOcclusions,
                            visualParent,
                            contextualSemanticHint,
                            ref best,
                            ref bestScore,
                            ref secondScore,
                            ref matchedColor,
                            out Rect expandedRect,
                            out VisualDescriptor expandedSource);
                        if (matched)
                        {
                            Debug.Log($"[UIPrefabGenerator] 图形范围恢复 {node.name}：" +
                                $"{node.width:F1}x{node.height:F1} -> {expandedRect.width:F1}x{expandedRect.height:F1}，" +
                                "已保持后代绝对位置。");
                            float deltaX = node.x - expandedRect.x;
                            float deltaY = node.y - expandedRect.y;
                            foreach (UIEffectNode child in node.children)
                            {
                                child.x += deltaX;
                                child.y += deltaY;
                            }
                            node.x = expandedRect.x;
                            node.y = expandedRect.y;
                            node.width = expandedRect.width;
                            node.height = expandedRect.height;
                            absoluteX = parentX + node.x;
                            absoluteY = parentY + node.y;
                            source = nodeSource = expandedSource;
                        }
                    }
                    if (matched && !containerProbe && !artworkProbe)
                    {
                        node.resource = GetSpriteResourcePath(best);
                        node.resourceCandidates.Clear();
                        node.intentionalColor = false;
                        node.color = "#" +
                                     ColorUtility.ToHtmlStringRGBA(
                                         matchedColor);
                        node.sliced = best.HasBorder;
                        if (best.HasBorder)
                        {
                            node.preserveAspect = false;
                        }

                        visualFailures.Remove(node);
                        RecordVisualMatchEvidence(
                            node,
                            best,
                            bestScore,
                            bestScore - secondScore);
                        Debug.Log(
                            $"[UIPrefabGenerator] 视觉资源 {node.name} -> " +
                            $"{node.resource}（分数 {bestScore:F3}，" +
                            $"领先 {bestScore - secondScore:F3}，背景 " +
                            (source.HasSpatialReferenceBackdrop
                                ? visualParent?.ResolvedDescriptor != null
                                    ? "已匹配祖先逐点重建"
                                    : "排除相邻组件的环形逐点采样"
                                : "单色回退") +
                            "）。");
                    }
                    else if (wasIntentionalColor)
                    {
                        // VisualSimilarity mode gives an authored Sprite the
                        // first chance even when AI classified the flattened
                        // screenshot region as a pure color. If no candidate
                        // is confident, keep the explicit color unchanged.
                        visualFailures.Remove(node);
                    }
                    else if (source == null)
                    {
                        visualFailures[node] =
                            "可用的未遮挡像素不足，未执行视觉匹配";
                    }
                    else
                    {
                        string candidate = best == null
                            ? "无候选"
                            : GetSpriteResourcePath(best);
                        visualFailures[node] =
                            $"视觉最佳 {candidate}，分数 {bestScore:F3}，" +
                            $"领先 {bestScore - secondScore:F3}";
                    }
                }

                VisualParentContext childVisualParent =
                    TryCreateResolvedVisualParentContext(
                        node,
                        absoluteX,
                        absoluteY,
                        schema,
                        reference,
                        renderer,
                        sourceOcclusions,
                        backdropOcclusions,
                        nodeSource,
                        visualParent) ?? visualParent;
                if (childVisualParent != null &&
                    ReferenceEquals(childVisualParent.Node, node))
                {
                    visualParentContexts[node] = childVisualParent;
                }

                MatchVisualNodes(
                    node.children,
                    absoluteX,
                    absoluteY,
                    node.width,
                    node.height,
                    schema,
                    reference,
                    renderer,
                    laterOcclusions,
                    laterTextOcclusions,
                    GetVisualSemanticHint(node),
                    childVisualParent);
                CollectVisualCoverage(
                    node,
                    parentX,
                    parentY,
                    laterOcclusions);
                CollectTextCoverage(
                    node,
                    parentX,
                    parentY,
                    laterTextOcclusions);
            }
        }

        /// <summary>
        /// 构建BackdropOcclusions。
        /// </summary>
        private static IReadOnlyCollection<Rect> BuildBackdropOcclusions(
            IEnumerable<UIEffectNode> siblings,
            UIEffectNode current,
            float parentX,
            float parentY,
            IReadOnlyCollection<Rect> inheritedOcclusions,
            IReadOnlyCollection<Rect> inheritedTextOcclusions)
        {
            var result = new List<Rect>();
            if (inheritedOcclusions != null)
            {
                result.AddRange(inheritedOcclusions);
            }

            if (inheritedTextOcclusions != null)
            {
                result.AddRange(inheritedTextOcclusions);
            }

            foreach (UIEffectNode sibling in
                     siblings ?? Enumerable.Empty<UIEffectNode>())
            {
                if (sibling == null || ReferenceEquals(sibling, current))
                {
                    continue;
                }

                CollectVisualCoverage(
                    sibling,
                    parentX,
                    parentY,
                    result);
                CollectTextCoverage(
                    sibling,
                    parentX,
                    parentY,
                    result);
            }

            return result;
        }

        /// <summary>
        /// 尝试创建ResolvedVisualParent上下文，并返回是否成功。
        /// </summary>
        private VisualParentContext TryCreateResolvedVisualParentContext(
            UIEffectNode node,
            float absoluteX,
            float absoluteY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            IReadOnlyCollection<Rect> sourceOcclusions,
            IReadOnlyCollection<Rect> backdropOcclusions,
            VisualDescriptor source,
            VisualParentContext visualParent)
        {
            if (!IsVisualNode(node) ||
                string.IsNullOrWhiteSpace(node.resource))
            {
                return null;
            }

            SpriteEntry entry = TryGetSpriteEntry(node.resource);
            if (entry == null)
            {
                return null;
            }

            if (source == null)
            {
                source = CreateReferenceDescriptor(
                    reference,
                    schema.designWidth,
                    schema.designHeight,
                    absoluteX,
                    absoluteY,
                    node.width,
                    node.height,
                    sourceOcclusions,
                    backdropOcclusions);
                ApplyVisualParentBackdrop(
                    source,
                    absoluteX,
                    absoluteY,
                    node.width,
                    node.height,
                    visualParent);
            }

            if (source == null)
            {
                return new VisualParentContext(
                    node,
                    entry,
                    absoluteX,
                    absoluteY);
            }

            VisualDescriptor candidate = GetSpriteDescriptor(
                entry,
                source,
                renderer);
            if (candidate == null)
            {
                return new VisualParentContext(
                    node,
                    entry,
                    absoluteX,
                    absoluteY);
            }

            Color imageColor = Color.white;
            if (!string.IsNullOrWhiteSpace(node.color) &&
                ColorUtility.TryParseHtmlString(
                    node.color,
                    out Color parsedImageColor))
            {
                imageColor = parsedImageColor;
            }

            var resolvedPixels = new Color[candidate.Pixels.Length];
            var resolvedMask = new bool[candidate.Pixels.Length];
            Vector3 tint = ToVector(imageColor);
            for (int index = 0;
                 index < candidate.Pixels.Length;
                 index++)
            {
                Vector3 backdrop = GetReferenceBackdrop(
                    source,
                    index,
                    ToVector(source.ReferenceBackdrop));
                resolvedPixels[index] = CompositeCandidate(
                    candidate.Pixels[index],
                    backdrop,
                    tint);
                resolvedMask[index] = true;
            }

            var resolved = new VisualDescriptor(
                resolvedPixels,
                resolvedMask,
                node.height <= 0f ? 0f : node.width / node.height,
                node.width,
                node.height,
                entry.HasBorder);
            return new VisualParentContext(
                node,
                entry,
                absoluteX,
                absoluteY,
                resolved);
        }

        /// <summary>
        /// 应用VisualParentBackdrop。
        /// </summary>
        private static void ApplyVisualParentBackdrop(
            VisualDescriptor source,
            float absoluteX,
            float absoluteY,
            float width,
            float height,
            VisualParentContext visualParent)
        {
            if (source == null || visualParent?.ResolvedDescriptor == null ||
                visualParent.Node == null ||
                visualParent.Node.width <= 0f ||
                visualParent.Node.height <= 0f)
            {
                return;
            }

            float localX = absoluteX - visualParent.AbsoluteX;
            float localY = absoluteY - visualParent.AbsoluteY;
            const float tolerance = 1f;
            if (localX < -tolerance || localY < -tolerance ||
                localX + width > visualParent.Node.width + tolerance ||
                localY + height > visualParent.Node.height + tolerance)
            {
                return;
            }

            var backdropPixels = new Color[source.Pixels.Length];
            Color total = Color.clear;
            for (int sampleY = 0; sampleY < VisualSampleSize; sampleY++)
            {
                float normalizedY = GetVisualSampleCoordinate(sampleY);
                float parentY = 1f - Mathf.Clamp01(
                    (localY + height * (1f - normalizedY)) /
                    visualParent.Node.height);
                for (int sampleX = 0;
                     sampleX < VisualSampleSize;
                     sampleX++)
                {
                    float normalizedX =
                        GetVisualSampleCoordinate(sampleX);
                    float parentX = Mathf.Clamp01(
                        (localX + width * normalizedX) /
                        visualParent.Node.width);
                    int index = sampleY * VisualSampleSize + sampleX;
                    Color sample = SampleVisualDescriptor(
                        visualParent.ResolvedDescriptor,
                        parentX,
                        parentY);
                    sample.a = 1f;
                    backdropPixels[index] = sample;
                    total += sample;
                }
            }

            source.ReferenceBackdropPixels = backdropPixels;
            Color representativeBackdrop = total / backdropPixels.Length;
            representativeBackdrop.a = 1f;
            source.ReferenceBackdrop = representativeBackdrop;
            source.HasReferenceBackdrop = true;
            source.HasSpatialReferenceBackdrop = true;
        }

        /// <summary>
        /// 执行Sample视觉Descriptor相关逻辑。
        /// </summary>
        private static Color SampleVisualDescriptor(
            VisualDescriptor descriptor,
            float normalizedX,
            float normalizedY)
        {
            FindVisualSampleBracket(
                normalizedX,
                out int left,
                out int right,
                out float horizontal);
            FindVisualSampleBracket(
                normalizedY,
                out int bottom,
                out int top,
                out float vertical);
            Color lower = Color.Lerp(
                descriptor.Pixels[bottom * VisualSampleSize + left],
                descriptor.Pixels[bottom * VisualSampleSize + right],
                horizontal);
            Color upper = Color.Lerp(
                descriptor.Pixels[top * VisualSampleSize + left],
                descriptor.Pixels[top * VisualSampleSize + right],
                horizontal);
            return Color.Lerp(lower, upper, vertical);
        }

        /// <summary>
        /// 查找VisualSampleBracket。
        /// </summary>
        private static void FindVisualSampleBracket(
            float normalized,
            out int lower,
            out int upper,
            out float interpolation)
        {
            normalized = Mathf.Clamp01(normalized);
            lower = 0;
            upper = 0;
            interpolation = 0f;
            for (int index = 1; index < VisualSampleSize; index++)
            {
                float coordinate = GetVisualSampleCoordinate(index);
                if (normalized > coordinate)
                {
                    lower = index;
                    upper = index;
                    continue;
                }

                upper = index;
                float lowerCoordinate =
                    GetVisualSampleCoordinate(lower);
                interpolation = Mathf.InverseLerp(
                    lowerCoordinate,
                    coordinate,
                    normalized);
                return;
            }

            lower = VisualSampleSize - 1;
            upper = lower;
        }

        /// <summary>
        /// 尝试查找ExpandedCompactGraphicMatch，并返回是否成功。
        /// </summary>
        private bool TryFindExpandedCompactGraphicMatch(
            UIEffectNode node,
            float parentX,
            float parentY,
            float parentWidth,
            float parentHeight,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            IReadOnlyCollection<Rect> occlusions,
            IReadOnlyCollection<Rect> backdropOcclusions,
            VisualParentContext visualParent,
            string semanticHint,
            ref SpriteEntry selected,
            ref float selectedScore,
            ref float selectedSecondScore,
            ref Color selectedColor,
            out Rect selectedRect,
            out VisualDescriptor selectedSource)
        {
            selectedRect = default;
            selectedSource = null;
            if (node == null || parentWidth <= 1f || parentHeight <= 1f)
            {
                return false;
            }

            float centerX = node.x + node.width * 0.5f;
            float centerY = node.y + node.height * 0.5f;
            var proposals = new List<Vector2>
            {
                new Vector2(node.width * 1.15f, node.height * 1.15f),
                new Vector2(node.width * 1.3f, node.height * 1.3f),
                new Vector2(node.width * 1.45f, node.height * 1.45f),
            };
            float squareEdge = Mathf.Max(node.width, node.height);
            // AI commonly measures the opaque silhouette, while the Sprite is a
            // square with transparent padding. Restore both axes, not the cropped aspect.
            foreach (float scale in new[] { 1.1f, 1.3f, 1.4f, 1.5f })
                proposals.Add(new Vector2(squareEdge * scale, squareEdge * scale));
            bool accepted = false;
            foreach (Vector2 proposal in proposals)
            {
                float sampleWidth = Mathf.Min(proposal.x, parentWidth);
                float sampleHeight = Mathf.Min(proposal.y, parentHeight);
                float localX = Mathf.Clamp(
                    centerX - sampleWidth * 0.5f,
                    0f,
                    parentWidth - sampleWidth);
                float localY = Mathf.Clamp(
                    centerY - sampleHeight * 0.5f,
                    0f,
                    parentHeight - sampleHeight);
                VisualDescriptor expandedSource = CreateReferenceDescriptor(
                    reference,
                    schema.designWidth,
                    schema.designHeight,
                    parentX + localX,
                    parentY + localY,
                    sampleWidth,
                    sampleHeight,
                    occlusions,
                    backdropOcclusions);
                ApplyVisualParentBackdrop(expandedSource, parentX + localX, parentY + localY,
                    sampleWidth, sampleHeight, visualParent);
                if (expandedSource == null ||
                    !TryFindBestVisualMatch(
                        expandedSource,
                        false,
                        false,
                        false,
                        true,
                        true,
                        semanticHint,
                        renderer,
                        out SpriteEntry best,
                        out float bestScore,
                        out float secondScore,
                        out Color matchedColor))
                {
                    continue;
                }

                if (accepted && bestScore <= selectedScore)
                {
                    continue;
                }

                selected = best;
                selectedScore = bestScore;
                selectedSecondScore = secondScore;
                selectedColor = matchedColor;
                selectedRect = new Rect(localX, localY, sampleWidth, sampleHeight);
                selectedSource = expandedSource;
                accepted = true;
            }

            return accepted;
        }

        /// <summary>
        /// 执行ReconcileRepeated视觉资源相关逻辑。
        /// </summary>
        private int ReconcileRepeatedVisualResources(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            VisualParentContext visualParent)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return 0;
            }

            int propagated = 0;
            List<UIEffectNode> reusableSurfaces = nodes
                .Where(IsReusableTextSurface)
                .ToList();
            var visited = new HashSet<UIEffectNode>();
            foreach (UIEffectNode seed in reusableSurfaces)
            {
                if (!visited.Add(seed))
                {
                    continue;
                }

                List<UIEffectNode> group = reusableSurfaces
                    .Where(peer => HaveEquivalentSurfaceGeometry(seed, peer))
                    .ToList();
                foreach (UIEffectNode member in group)
                {
                    visited.Add(member);
                }

                if (group.Count < 2)
                {
                    continue;
                }

                List<SpriteEntry> peerEntries = group
                    .Where(peer =>
                        !string.IsNullOrWhiteSpace(peer.resource))
                    .Select(peer => TryGetSpriteEntry(peer.resource))
                    .Where(entry => entry != null)
                    .GroupBy(
                        entry => GetSpriteResourcePath(entry),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(
                        entry => entry.AssetPath,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        entry => entry.Sprite.name,
                        StringComparer.Ordinal)
                    .ToList();
                if (peerEntries.Count == 0)
                {
                    continue;
                }

                List<RepeatedSurfaceCandidateScore> evaluations =
                    peerEntries
                        .Select(entry => ScoreRepeatedSurfaceCandidate(
                            group,
                            entry,
                            parentX,
                             parentY,
                             schema,
                             reference,
                             renderer,
                             nodes,
                             visualParent))
                        .Where(score => score.SampleCount == group.Count)
                        .OrderByDescending(score => score.AverageScore)
                        .ThenBy(
                            score => score.Entry.AssetPath,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            score => score.Entry.Sprite.name,
                            StringComparer.Ordinal)
                        .ToList();
                if (evaluations.Count == 0)
                {
                    continue;
                }

                RepeatedSurfaceCandidateScore selected = evaluations[0];
                float consensusMargin = evaluations.Count <= 1
                    ? selected.AverageScore
                    : selected.AverageScore -
                      evaluations[1].AverageScore;
                bool hasConflict = peerEntries.Count > 1;
                if (selected.AverageScore <
                    (hasConflict
                        ? RepeatedSurfaceConsensusSimilarity
                        : FullCanvasVisualSimilarity) ||
                    hasConflict &&
                    consensusMargin < RepeatedSurfaceConsensusMargin)
                {
                    if (hasConflict)
                    {
                        Debug.Log(
                            $"[UIPrefabGenerator] 同构视觉组 " +
                            $"{string.Join(", ", group.Select(item => item.name))} " +
                            $"保留局部结果：去背景后最佳均值 " +
                            $"{selected.AverageScore:F3}，领先 " +
                            $"{consensusMargin:F3}，未达到组纠正规则。");
                    }

                    continue;
                }

                string selectedPath = GetSpriteResourcePath(
                    selected.Entry);
                foreach (UIEffectNode target in group)
                {
                    if (explicitVisualResources.Contains(target) ||
                        string.Equals(
                            target.resource,
                            selectedPath,
                            StringComparison.OrdinalIgnoreCase) ||
                        !selected.Scores.TryGetValue(
                            target,
                            out float localScore) ||
                        localScore < RepeatedSurfaceLocalSimilarity)
                    {
                        continue;
                    }

                    float currentScore = 0f;
                    RepeatedSurfaceCandidateScore currentEvaluation =
                        evaluations.FirstOrDefault(score =>
                            string.Equals(
                                GetSpriteResourcePath(score.Entry),
                                target.resource,
                                StringComparison.OrdinalIgnoreCase));
                    if (currentEvaluation != null)
                    {
                        currentEvaluation.Scores.TryGetValue(
                            target,
                            out currentScore);
                    }

                    // Group evidence may correct a close local false
                    // positive, but must not flatten a genuinely distinct
                    // variant whose own screenshot region is much stronger.
                    if (hasConflict &&
                        currentScore - localScore > 0.06f)
                    {
                        continue;
                    }

                    ApplyRepeatedSurfaceResource(
                        target,
                        selected);
                    propagated++;
                    Debug.Log(
                        $"[UIPrefabGenerator] 同构视觉 {target.name} " +
                        (hasConflict
                            ? "纠正为组内一致 Sprite "
                            : "复用同级已验证 Sprite ") +
                        $"{selectedPath}（局部 {localScore:F3}，" +
                        $"组均值 {selected.AverageScore:F3}/" +
                        $"{consensusMargin:F3}）。");
                }
            }

            foreach (UIEffectNode node in nodes.Where(item => item != null))
            {
                VisualParentContext childVisualParent =
                    visualParentContexts.TryGetValue(
                        node,
                        out VisualParentContext resolvedContext)
                        ? resolvedContext
                        : visualParent;
                propagated += ReconcileRepeatedVisualResources(
                    node.children,
                    parentX + node.x,
                    parentY + node.y,
                    schema,
                    reference,
                    renderer,
                    childVisualParent);
            }

            return propagated;
        }

        /// <summary>
        /// 执行Reconcile运行时模板资源相关逻辑。
        /// </summary>
        private int ReconcileRuntimeTemplateResources(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            VisualParentContext visualParent)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return 0;
            }

            int propagated = 0;
            var groupedRoots = new HashSet<UIEffectNode>();
            List<IGrouping<string, UIEffectNode>> groups = nodes
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
                List<RuntimeTemplateNodeLocation> locations = group
                    .Select(node => new RuntimeTemplateNodeLocation(
                        node,
                        nodes,
                        parentX,
                        parentY,
                        visualParent,
                        string.Empty,
                        node.runtimeTemplateVariant))
                    .ToList();
                foreach (RuntimeTemplateNodeLocation location in locations)
                {
                    groupedRoots.Add(location.Node);
                }

                propagated += ReconcileEquivalentRuntimeTemplateNodes(
                    locations,
                    schema,
                    reference,
                    renderer,
                    false);
            }

            foreach (UIEffectNode node in nodes.Where(item => item != null))
            {
                if (groupedRoots.Contains(node))
                {
                    continue;
                }

                float absoluteX = parentX + node.x;
                float absoluteY = parentY + node.y;
                VisualParentContext childVisualParent =
                    TryCreateVisualParentContext(
                        node,
                        absoluteX,
                        absoluteY) ?? visualParent;
                propagated += ReconcileRuntimeTemplateResources(
                    node.children,
                    absoluteX,
                    absoluteY,
                    schema,
                    reference,
                    renderer,
                    childVisualParent);
            }

            return propagated;
        }

        /// <summary>
        /// 执行ReconcileEquivalent运行时模板节点相关逻辑。
        /// </summary>
        private int ReconcileEquivalentRuntimeTemplateNodes(
            IReadOnlyList<RuntimeTemplateNodeLocation> locations,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            bool compareLocalPosition)
        {
            if (locations == null || locations.Count < 2 ||
                !HaveEquivalentRuntimeTemplateGeometry(
                    locations,
                    compareLocalPosition))
            {
                return 0;
            }

            int propagated = 0;
            if (locations.All(location => IsVisualNode(location.Node)))
            {
                propagated += ReconcileRuntimeTemplateVisualSlot(
                    locations,
                    schema,
                    reference,
                    renderer);
            }

            int childCount = locations[0].Node.children?.Count ?? 0;
            for (int childIndex = 0;
                 childIndex < childCount;
                 childIndex++)
            {
                var childLocations =
                    new List<RuntimeTemplateNodeLocation>(locations.Count);
                bool complete = true;
                foreach (RuntimeTemplateNodeLocation location in locations)
                {
                    if (location.Node.children == null ||
                        location.Node.children.Count <= childIndex ||
                        location.Node.children[childIndex] == null)
                    {
                        complete = false;
                        break;
                    }

                    float absoluteX = location.AbsoluteX;
                    float absoluteY = location.AbsoluteY;
                    VisualParentContext childVisualParent =
                        TryCreateVisualParentContext(
                            location.Node,
                            absoluteX,
                            absoluteY) ?? location.VisualParent;
                    childLocations.Add(new RuntimeTemplateNodeLocation(
                        location.Node.children[childIndex],
                        location.Node.children,
                        absoluteX,
                        absoluteY,
                        childVisualParent,
                        GetVisualSemanticHint(location.Node),
                        location.RootVariant));
                }

                if (complete)
                {
                    propagated += ReconcileEquivalentRuntimeTemplateNodes(
                        childLocations,
                        schema,
                        reference,
                        renderer,
                        true);
                }
            }

            return propagated;
        }

        /// <summary>
        /// 执行Reconcile运行时模板视觉Slot相关逻辑。
        /// </summary>
        private int ReconcileRuntimeTemplateVisualSlot(
            IReadOnlyList<RuntimeTemplateNodeLocation> locations,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer)
        {
            if (locations.Any(location =>
                    explicitVisualResources.Contains(location.Node) ||
                    IsShapeSensitiveVisualNode(location.Node, schema)) ||
                IsVariantSpecificRuntimeTemplateSlot(locations))
            {
                return 0;
            }

            List<SpriteEntry> entries = locations
                .Select(location =>
                    TryGetSpriteEntry(location.Node.resource))
                .Where(entry => entry != null &&
                                IsRuntimeTemplateNeutralResource(
                                    entry,
                                    locations))
                .GroupBy(
                    GetSpriteResourcePath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(
                    entry => entry.AssetPath,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Sprite.name, StringComparer.Ordinal)
                .ToList();
            if (entries.Count == 0)
            {
                return 0;
            }

            List<RepeatedSurfaceCandidateScore> evaluations = entries
                .Select(entry => ScoreRuntimeTemplateVisualCandidate(
                    locations,
                    entry,
                    schema,
                    reference,
                    renderer))
                .Where(score => score.SampleCount == locations.Count)
                .OrderByDescending(score => score.AverageScore)
                .ThenBy(
                    score => score.Entry.AssetPath,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    score => score.Entry.Sprite.name,
                    StringComparer.Ordinal)
                .ToList();
            if (evaluations.Count == 0)
            {
                return 0;
            }

            RepeatedSurfaceCandidateScore selected = evaluations[0];
            float consensusMargin = evaluations.Count == 1
                ? selected.AverageScore
                : selected.AverageScore - evaluations[1].AverageScore;
            string selectedPath = GetSpriteResourcePath(selected.Entry);
            int sourceCount = locations.Count(location =>
                string.Equals(
                    location.Node.resource,
                    selectedPath,
                    StringComparison.OrdinalIgnoreCase));
            float requiredSimilarity = sourceCount >= 2
                ? RuntimeTemplateConsensusSimilarity
                : RuntimeTemplateSingleSourceSimilarity;
            if (selected.AverageScore < requiredSimilarity ||
                evaluations.Count > 1 &&
                consensusMargin < RuntimeTemplateConsensusMargin)
            {
                return 0;
            }

            int propagated = 0;
            foreach (RuntimeTemplateNodeLocation location in locations)
            {
                UIEffectNode target = location.Node;
                if (string.Equals(
                        target.resource,
                        selectedPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !selected.Scores.TryGetValue(
                        target,
                        out float localScore) ||
                    localScore < RuntimeTemplateLocalSimilarity)
                {
                    continue;
                }

                RepeatedSurfaceCandidateScore current = evaluations
                    .FirstOrDefault(score => string.Equals(
                        GetSpriteResourcePath(score.Entry),
                        target.resource,
                        StringComparison.OrdinalIgnoreCase));
                float currentScore = 0f;
                if (current != null)
                {
                    current.Scores.TryGetValue(target, out currentScore);
                }

                if (current != null && currentScore - localScore > 0.06f)
                {
                    continue;
                }

                ApplyRepeatedSurfaceResource(target, selected);
                RecordVisualMatchEvidence(
                    target,
                    selected.Entry,
                    localScore,
                    consensusMargin);
                visualParentContexts[target] = new VisualParentContext(
                    target,
                    selected.Entry,
                    location.AbsoluteX,
                    location.AbsoluteY);
                propagated++;
                Debug.Log(
                    $"[UIPrefabGenerator] 运行时模板通用视觉 " +
                    $"{target.name} -> {selectedPath}（局部 " +
                    $"{localScore:F3}，组均值 " +
                    $"{selected.AverageScore:F3}/" +
                    $"{consensusMargin:F3}，来源 {sourceCount}）。");
            }

            return propagated;
        }

        private RepeatedSurfaceCandidateScore
            ScoreRuntimeTemplateVisualCandidate(
                IReadOnlyList<RuntimeTemplateNodeLocation> locations,
                SpriteEntry candidate,
                UIEffectSchema schema,
                Texture2D reference,
                SpritePreviewRenderer renderer)
        {
            var result = new RepeatedSurfaceCandidateScore(candidate);
            foreach (RuntimeTemplateNodeLocation location in locations)
            {
                UIEffectNode node = location.Node;
                var sourceOcclusions = new List<Rect>();
                CollectVisualCoverage(
                    node.children,
                    location.AbsoluteX,
                    location.AbsoluteY,
                    sourceOcclusions);
                var textOcclusions = new List<Rect>();
                CollectTextCoverage(
                    node.children,
                    location.AbsoluteX,
                    location.AbsoluteY,
                    textOcclusions);
                IReadOnlyCollection<Rect> backdropOcclusions =
                    BuildBackdropOcclusions(
                        location.Siblings,
                        node,
                        location.ParentX,
                        location.ParentY,
                        Array.Empty<Rect>(),
                        Array.Empty<Rect>());
                VisualDescriptor source = CreateReferenceDescriptor(
                    reference,
                    schema.designWidth,
                    schema.designHeight,
                    location.AbsoluteX,
                    location.AbsoluteY,
                    node.width,
                    node.height,
                    sourceOcclusions,
                    backdropOcclusions);
                if (source == null)
                {
                    continue;
                }

                ApplyVisualParentBackdrop(
                    source,
                    location.AbsoluteX,
                    location.AbsoluteY,
                    node.width,
                    node.height,
                    location.VisualParent);
                bool hasVisualOcclusion = sourceOcclusions.Count > 0;
                bool hasTextOcclusion = textOcclusions.Count > 0;
                TryFindBestVisualMatch(
                    source,
                    hasVisualOcclusion || hasTextOcclusion,
                    false,
                    hasTextOcclusion,
                    IsCompactGraphicNode(node, schema),
                    IsShapeSensitiveVisualNode(node, schema),
                    GetVisualSemanticHint(node) + " " +
                    location.ParentSemanticHint,
                    renderer,
                    out SpriteEntry best,
                    out float bestScore,
                    out _,
                    out Color matchedColor,
                    entry => ReferenceEquals(entry, candidate));
                if (ReferenceEquals(best, candidate))
                {
                    result.Add(node, bestScore, matchedColor);
                }
            }

            return result;
        }

        /// <summary>
        /// 执行HaveEquivalent运行时模板Geometry相关逻辑。
        /// </summary>
        private static bool HaveEquivalentRuntimeTemplateGeometry(
            IReadOnlyList<RuntimeTemplateNodeLocation> locations,
            bool compareLocalPosition)
        {
            UIEffectNode first = locations[0].Node;
            int firstChildCount = first.children?.Count ?? 0;
            for (int index = 1; index < locations.Count; index++)
            {
                UIEffectNode candidate = locations[index].Node;
                if (!string.Equals(
                        first.type,
                        candidate.type,
                        StringComparison.OrdinalIgnoreCase) ||
                    firstChildCount != (candidate.children?.Count ?? 0) ||
                    !AreTemplateDimensionsEquivalent(
                        first.width,
                        candidate.width) ||
                    !AreTemplateDimensionsEquivalent(
                        first.height,
                        candidate.height) ||
                    compareLocalPosition &&
                    (!AreTemplateDimensionsEquivalent(
                         first.x,
                         candidate.x,
                         2f) ||
                     !AreTemplateDimensionsEquivalent(
                         first.y,
                         candidate.y,
                         2f)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 执行Are模板DimensionsEquivalent相关逻辑。
        /// </summary>
        private static bool AreTemplateDimensionsEquivalent(
            float first,
            float second,
            float absoluteTolerance = 1f)
        {
            return Mathf.Abs(first - second) <= Mathf.Max(
                absoluteTolerance,
                Mathf.Max(Mathf.Abs(first), Mathf.Abs(second)) * 0.015f);
        }

        /// <summary>
        /// 执行判断是否VariantSpecific运行时模板Slot相关逻辑。
        /// </summary>
        private static bool IsVariantSpecificRuntimeTemplateSlot(
            IEnumerable<RuntimeTemplateNodeLocation> locations)
        {
            return locations.Any(location =>
                GetRuntimeVariantResourceAffinity(
                    location.RootVariant,
                    location.Node.resource) != 0);
        }

        /// <summary>
        /// 执行判断是否运行时模板Neutral资源相关逻辑。
        /// </summary>
        private static bool IsRuntimeTemplateNeutralResource(
            SpriteEntry entry,
            IEnumerable<RuntimeTemplateNodeLocation> locations)
        {
            string resource = GetSpriteResourcePath(entry);
            return locations.All(location =>
                GetRuntimeVariantResourceAffinity(
                    location.RootVariant,
                    resource) == 0);
        }

        /// <summary>
        /// 执行ScoreRepeatedSurfaceCandidate相关逻辑。
        /// </summary>
        private RepeatedSurfaceCandidateScore ScoreRepeatedSurfaceCandidate(
            IReadOnlyCollection<UIEffectNode> group,
            SpriteEntry candidate,
            float parentX,
            float parentY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            IReadOnlyCollection<UIEffectNode> siblings,
            VisualParentContext visualParent)
        {
            var result = new RepeatedSurfaceCandidateScore(candidate);
            foreach (UIEffectNode node in group)
            {
                IReadOnlyCollection<Rect> backdropOcclusions =
                    BuildBackdropOcclusions(
                        siblings,
                        node,
                        parentX,
                        parentY,
                        Array.Empty<Rect>(),
                        Array.Empty<Rect>());
                VisualDescriptor source = CreateReferenceDescriptor(
                    reference,
                    schema.designWidth,
                    schema.designHeight,
                    parentX + node.x,
                    parentY + node.y,
                    node.width,
                    node.height,
                    Array.Empty<Rect>(),
                    backdropOcclusions);
                if (source == null)
                {
                    continue;
                }

                ApplyVisualParentBackdrop(
                    source,
                    parentX + node.x,
                    parentY + node.y,
                    node.width,
                    node.height,
                    visualParent);

                TryFindBestVisualMatch(
                    source,
                    true,
                    false,
                    true,
                    false,
                    false,
                    GetVisualSemanticHint(node),
                    renderer,
                    out SpriteEntry best,
                    out float bestScore,
                    out _,
                    out Color matchedColor,
                    entry => ReferenceEquals(entry, candidate));
                if (!ReferenceEquals(best, candidate))
                {
                    continue;
                }

                result.Add(node, bestScore, matchedColor);
            }

            return result;
        }

        /// <summary>
        /// 应用RepeatedSurface资源。
        /// </summary>
        private void ApplyRepeatedSurfaceResource(
            UIEffectNode target,
            RepeatedSurfaceCandidateScore selected)
        {
            target.resource = GetSpriteResourcePath(selected.Entry);
            target.resourceCandidates.Clear();
            target.color = "#" +
                           ColorUtility.ToHtmlStringRGBA(
                               selected.Colors[target]);
            target.intentionalColor = false;
            target.sliced = selected.Entry.HasBorder;
            if (selected.Entry.HasBorder)
            {
                target.preserveAspect = false;
            }

            visualFailures.Remove(target);
        }

        /// <summary>
        /// 执行判断是否Reusable文本Surface相关逻辑。
        /// </summary>
        private static bool IsReusableTextSurface(UIEffectNode node)
        {
            if (node == null ||
                !string.Equals(
                    node.type,
                    "Image",
                    StringComparison.OrdinalIgnoreCase) ||
                !ContainsTextDescendant(node.children) ||
                ContainsNonTextGraphicDescendant(node.children))
            {
                return false;
            }

            string hint = NormalizeSemanticPhrase(
                (node.semantic ?? string.Empty) + " " +
                (node.visualKind ?? string.Empty) + " " +
                (node.name ?? string.Empty));
            return ContainsAny(
                hint,
                "field",
                "bar",
                "plate",
                "row",
                "cell",
                "字段",
                "条",
                "格");
        }

        /// <summary>
        /// 执行HaveEquivalentSurfaceGeometry相关逻辑。
        /// </summary>
        private static bool HaveEquivalentSurfaceGeometry(
            UIEffectNode first,
            UIEffectNode second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            string firstSemantic = NormalizeSemanticPhrase(
                first.semantic ?? string.Empty);
            string secondSemantic = NormalizeSemanticPhrase(
                second.semantic ?? string.Empty);
            return string.Equals(
                       firstSemantic,
                       secondSemantic,
                       StringComparison.OrdinalIgnoreCase) &&
                   GetDimensionSimilarity(
                       first.width,
                       second.width) >= 0.97f &&
                   GetDimensionSimilarity(
                       first.height,
                       second.height) >= 0.97f;
        }

        /// <summary>
        /// 尝试查找BestVisualMatch，并返回是否成功。
        /// </summary>
        private bool FindBestVisualMatchUncached(
            VisualDescriptor source,
            bool preferBorder,
            bool requireBorder,
            bool textCovered,
            bool compactGraphic,
            bool shapeSensitive,
            string semanticHint,
            SpritePreviewRenderer renderer,
            out SpriteEntry best,
            out float bestScore,
            out float secondScore,
            out Color bestColor,
            Func<SpriteEntry, bool> candidateFilter = null,
            List<VisualCandidate> rankedCandidates = null,
            IReadOnlyList<string> preferredResources = null)
        {
            best = null;
            bestScore = 0f;
            secondScore = 0f;
            bestColor = Color.white;
            var shortlist = new List<VisualCandidate>();
            for (int spriteIndex = 0;
                 spriteIndex < sprites.Count;
                 spriteIndex++)
            {
                if (!visualProgressShown &&
                    sprites.Count > 256 &&
                    spriteIndex % 128 == 0 &&
                    EditorUtility.DisplayCancelableProgressBar(
                        "效果图资源匹配",
                        $"正在本地比对 Sprite " +
                        $"{spriteIndex + 1}/{sprites.Count}",
                        (spriteIndex + 1f) / sprites.Count))
                {
                    throw new OperationCanceledException(
                        "已取消本地 Sprite 视觉匹配。");
                }

                SpriteEntry entry = sprites[spriteIndex];
                if (string.Equals(entry.AssetPath, excludedReferencePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (candidateFilter != null &&
                    !candidateFilter(entry))
                {
                    continue;
                }

                if (requireBorder && !entry.HasBorder)
                {
                    continue;
                }

                float aspectSimilarity = GetAspectSimilarity(
                    source.Aspect,
                    entry.Aspect);
                if (aspectSimilarity < MinimumAspectSimilarity &&
                    (!entry.HasBorder || shapeSensitive))
                {
                    continue;
                }

                if (entry.HasBorder && !shapeSensitive)
                {
                    // A bordered Sprite is designed to take the target
                    // RectTransform size. Its source aspect is not evidence
                    // against a match for stretchable surfaces. Shape-
                    // sensitive graphics still keep their authored aspect so
                    // a thin nine-slice bar cannot compete with an emblem.
                    aspectSimilarity = 1f;
                }

                VisualDescriptor candidate =
                    GetSpriteDescriptor(entry, source, renderer);
                if (candidate == null)
                {
                    continue;
                }


                float semanticBonus = GetVisualSemanticBonus(
                    semanticHint,
                    entry);
                float slicedScalePenalty =
                    GetSlicedTargetScalePenalty(
                        source,
                        entry,
                        shapeSensitive);
                shortlist.Add(new VisualCandidate(
                    entry,
                    candidate,
                    aspectSimilarity,
                    0f,
                    semanticBonus,
                    slicedScalePenalty));
            }

            visualProgressShown = true;
            EditorUtility.ClearProgressBar();
            ScoreQuickCandidates(source, shortlist, textCovered);

            // Exact copies are one visual choice. Otherwise duplicate files can fill
            // the shortlist and give even a perfect match a zero confidence margin.
            // Similar-looking variants must still compete independently.
            shortlist = DistinctVisualCandidates(shortlist
                .OrderByDescending(item => item.QuickScore)
                .ThenBy(item => GetSpriteResourcePath(item.Entry), StringComparer.Ordinal));

            var scoredCandidates = new List<VisualCandidate>();
            var refinementCandidates = shortlist
                         .OrderByDescending(item => item.QuickScore)
                         .ThenBy(
                             item => item.Entry.AssetPath,
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(
                             item => item.Entry.AssetPath,
                             StringComparer.Ordinal)
                         .ThenBy(
                             item => item.Entry.Sprite.name,
                             StringComparer.Ordinal)
                         .Take(128).ToList();
            // Reserve independent source-geometry recall for layers whose crop
            // is dominated by text or by another overlaid Sprite.
            foreach (VisualCandidate candidate in shortlist
                .OrderByDescending(item => item.Entry.HasBorder
                    ? 1f - GetSlicedTargetScalePenalty(source, item.Entry, shapeSensitive)
                    : GetAspectSimilarity(source.Width / Mathf.Max(1f, source.Height), item.Entry.Aspect) *
                      Mathf.Min(source.Width, item.Entry.AuthoredWidth) / Mathf.Max(source.Width, item.Entry.AuthoredWidth) *
                      Mathf.Min(source.Height, item.Entry.AuthoredHeight) / Mathf.Max(source.Height, item.Entry.AuthoredHeight))
                .ThenBy(item => GetSpriteResourcePath(item.Entry), StringComparer.Ordinal).Take(16))
            {
                if (!refinementCandidates.Contains(candidate)) refinementCandidates.Add(candidate);
            }
            // Every supplied exact candidate gets a full pixel comparison, even
            // when its quick score missed the global shortlist.
            if (preferredResources != null && preferredResources.Count > 0)
            {
                foreach (VisualCandidate candidate in shortlist)
                {
                    if (preferredResources.Contains(GetSpriteResourcePath(candidate.Entry)) &&
                        !refinementCandidates.Contains(candidate))
                        refinementCandidates.Add(candidate);
                }
            }
            ScoreRefinementCandidates(source, refinementCandidates, preferBorder, textCovered);
            scoredCandidates.AddRange(refinementCandidates);

            float strongestAuthoredScore = scoredCandidates.Count == 0
                ? 0f
                : scoredCandidates
                    .Where(item =>
                        item.AuthoredColorDistance <=
                        MaximumAuthoredColorDistance ||
                        source.HasSpatialReferenceBackdrop &&
                        HasAlphaVariation(item.Descriptor) &&
                        !IsTintable(item.Descriptor))
                    .Select(item => item.AuthoredScore)
                    .DefaultIfEmpty(0f)
                    .Max();
            float authoredProtectionThreshold =
                textCovered && source.HasSpatialReferenceBackdrop
                    ? SpatialAuthoredVisualSimilarity
                    : StrongAuthoredVisualSimilarity;
            if (strongestAuthoredScore >= authoredProtectionThreshold)
            {
                foreach (VisualCandidate candidate in scoredCandidates)
                {
                    float requiredTintLead = MinimumTintOverrideLead +
                        GetTintTransformationDistance(
                            candidate.MatchedColor) *
                        TintDistanceLeadScale;
                    if (!candidate.UsesMaterialTint ||
                        candidate.Score - strongestAuthoredScore >=
                        requiredTintLead)
                    {
                        continue;
                    }

                    // A neutral mask may reproduce any flat color after a
                    // large tint. When an authored-color candidate already
                    // explains the screenshot strongly, keep the mask's
                    // untinted score unless it has a clear structural lead.
                    candidate.Score = candidate.AuthoredScore;
                    candidate.MatchedColor = Color.white;
                    candidate.UsesMaterialTint = false;
                }
            }

            if (rankedCandidates != null)
            {
                rankedCandidates.AddRange(scoredCandidates
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => GetSpriteResourcePath(item.Entry), StringComparer.Ordinal)
                    .Take(12));
            }

            foreach (VisualCandidate candidate in scoredCandidates
                         .OrderByDescending(item => item.Score)
                         .ThenBy(
                             item => item.Entry.AssetPath,
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(
                             item => item.Entry.AssetPath,
                             StringComparer.Ordinal)
                         .ThenBy(
                             item => item.Entry.Sprite.name,
                             StringComparer.Ordinal))
            {
                if (best == null)
                {
                    best = candidate.Entry;
                    bestScore = candidate.Score;
                    bestColor = candidate.MatchedColor;
                    continue;
                }

                secondScore = candidate.Score;
                break;
            }

            VisualCandidate bestCandidate = scoredCandidates
                .OrderByDescending(item => item.Score)
                .ThenBy(
                    item => item.Entry.AssetPath,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    item => item.Entry.AssetPath,
                    StringComparer.Ordinal)
                .ThenBy(
                    item => item.Entry.Sprite.name,
                    StringComparer.Ordinal)
                .FirstOrDefault();
            if (bestCandidate != null && bestCandidate.UsesMaterialTint)
            {
                string alternatives = string.Join(
                    "；",
                    scoredCandidates
                        .OrderByDescending(item => item.Score)
                        .ThenBy(
                            item => item.Entry.AssetPath,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            item => item.Entry.AssetPath,
                            StringComparer.Ordinal)
                        .ThenBy(
                            item => item.Entry.Sprite.name,
                            StringComparer.Ordinal)
                        .Take(3)
                        .Select(item =>
                            $"{item.Entry.Sprite.name}:" +
                            $"{item.Score:F3}/原色{item.AuthoredScore:F3}/" +
                            $"合成{item.CompositeScore:F3}/" +
                            $"色差{item.AuthoredColorDistance:F3}/" +
                            $"染色{item.UsesMaterialTint}"));
                Debug.Log(
                    $"[UIPrefabGenerator] 着色候选复核 " +
                    $"{semanticHint}：{alternatives}。");
            }

            if (best == null)
            {
                return false;
            }

            float margin = bestScore - secondScore;
            if (compactGraphic &&
                bestScore < CompactGraphicVisualSimilarity)
            {
                // A small, nearly square, textless graphic is usually an icon
                // or emblem. Its silhouette and internal structure must be
                // stronger than the generic background threshold; otherwise
                // a flat panel can be stretched into a plausible color block
                // and incorrectly accepted as the icon.
                return false;
            }

            if (requireBorder)
            {
                // Structural coalescing already requires two adjacent,
                // aligned background slices and a bordered Sprite.
                // Similar nine-slice panels often share most of their center
                // pixels, so their visual margin is naturally smaller.
                return bestScore >= CompositeSlicedSimilarity &&
                       margin >= CompositeSlicedMargin;
            }

            if (preferBorder && textCovered && best.HasBorder &&
                GetSlicedStructuralFit(source, best) >=
                AlignedSlicedStructuralFit &&
                (bestScore >= AlignedSlicedVisualSimilarity &&
                 margin >= AlignedSlicedVisualMargin ||
                 source.HasSpatialReferenceBackdrop &&
                 GetTintTransformationDistance(bestColor) >=
                 MaterialTintDistance &&
                 bestScore >= AlignedTintedSlicedVisualSimilarity &&
                 margin >= AlignedTintedSlicedVisualMargin))
            {
                // Text may cover most of a thin authored bar or a tinted
                // rounded field. When the candidate's non-stretched axis and
                // border bands fit the target, a stable positive lead is more
                // informative than the generic background threshold.
                return true;
            }

            if (preferBorder &&
                (bestScore >= OccludedVisualSimilarity &&
                 margin >= OccludedVisualMargin ||
                 bestScore >= OccludedConfidentVisualSimilarity &&
                 margin >= OccludedConfidentVisualMargin))
            {
                // A large background may only expose narrow edge/texture
                // bands after its descendants are removed. A clear lead in
                // those surviving samples is sufficient even when the
                // absolute score is lower than an unobstructed icon match.
                return true;
            }

            if (textCovered &&
                (bestScore >= TextCoveredConfidentSimilarity &&
                 margin >= TextCoveredConfidentMargin ||
                 bestScore >= TextCoveredStrongSimilarity &&
                 margin >= TextCoveredStrongMargin))
            {
                // Robust text rejection and backdrop-aware compositing have
                // stronger structural evidence than the raw color matcher.
                // Closely related sliced backgrounds may only differ at a
                // narrow edge, so accept a smaller but still positive margin.
                return true;
            }

            return bestScore >= MinimumVisualSimilarity &&
                   (margin >= MinimumVisualMargin ||
                    bestScore >= ConfidentVisualSimilarity &&
                    margin >= ConfidentVisualMargin ||
                    bestScore >= StrongVisualSimilarity &&
                    margin >= StrongVisualMargin);
        }

        /// <summary>
        /// 转换ParentIntegratedVisualWrappersToContainers。
        /// </summary>
        private int ConvertParentIntegratedVisualWrappersToContainers(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            VisualParentContext visualParent)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return 0;
            }

            int converted = 0;
            foreach (UIEffectNode node in nodes.Where(item => item != null))
            {
                float absoluteX = parentX + node.x;
                float absoluteY = parentY + node.y;
                float parentScore = 0f;
                float ownScore = 0f;
                bool convertedCurrent = visualParent != null &&
                    CanConvertParentIntegratedVisualWrapper(node) &&
                    TryGetParentIntegratedVisualScore(
                        node,
                        absoluteX,
                        absoluteY,
                        schema,
                        reference,
                        renderer,
                        visualParent,
                        out parentScore,
                        out ownScore,
                        true) &&
                    parentScore >= IntegratedParentVisualSimilarity &&
                    parentScore - ownScore >=
                    IntegratedParentVisualLead;
                if (convertedCurrent)
                {
                    node.type = "Container";
                    node.resource = string.Empty;
                    node.resourceCandidates.Clear();
                    node.color = string.Empty;
                    node.intentionalColor = false;
                    node.preserveAspect = false;
                    node.sliced = false;
                    node.raycastTarget = false;
                    node.mayMerge = false;
                    node.mayUseFullCanvasSprite = false;
                    visualFailures.Remove(node);
                    visualMatchEvidence.Remove(node);
                    visualParentContexts.Remove(node);
                    converted++;
                    Debug.Log(
                        $"[UIPrefabGenerator] {node.name} 的底图已由父 " +
                        $"Sprite 完整表达，保留层级并转为 Container" +
                        $"（父图 {parentScore:F3}，独立资源 " +
                        $"{ownScore:F3}）。");
                }

                VisualParentContext childVisualParent = convertedCurrent
                    ? visualParent
                    : TryCreateVisualParentContext(
                          node,
                          absoluteX,
                          absoluteY) ?? visualParent;
                converted +=
                    ConvertParentIntegratedVisualWrappersToContainers(
                        node.children,
                        absoluteX,
                        absoluteY,
                        schema,
                        reference,
                        renderer,
                        childVisualParent);
            }

            return converted;
        }

        /// <summary>
        /// 执行判断能否转换ParentIntegrated视觉Wrapper相关逻辑。
        /// </summary>
        private bool CanConvertParentIntegratedVisualWrapper(
            UIEffectNode node)
        {
            if (node == null)
            {
                return false;
            }

            string hint = NormalizeSemanticPhrase(
                (node.name ?? string.Empty) + " " +
                (node.semantic ?? string.Empty) + " " +
                (node.visualKind ?? string.Empty));
            bool layoutWrapper = ContainsAny(
                hint,
                "panel",
                "section",
                "content",
                "region",
                "wrapper",
                "layout",
                "header",
                "footer",
                "面板",
                "分区",
                "内容区",
                "标题区");
            bool independentSurface = ContainsAny(
                hint,
                "field",
                "bar",
                "plate",
                "badge",
                "flag",
                "emblem",
                "icon",
                "button",
                "toggle",
                "tile",
                "cell",
                "card",
                "row",
                "字段",
                "条",
                "牌",
                "徽章",
                "旗",
                "图标",
                "按钮");
            return layoutWrapper &&
                   !independentSurface &&
                   !explicitVisualResources.Contains(node) &&
                   string.Equals(
                       node.type,
                       "Image",
                       StringComparison.OrdinalIgnoreCase) &&
                   node.children != null &&
                   node.children.Count > 0 &&
                   !string.IsNullOrWhiteSpace(node.resource) &&
                   !node.raycastTarget &&
                   !node.mayLayer;
        }

        /// <summary>
        /// 执行CollapseParentIntegrated文本Wrappers相关逻辑。
        /// </summary>
        private int CollapseParentIntegratedTextWrappers(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            VisualParentContext visualParent)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return 0;
            }

            int collapsed = 0;
            for (int index = 0; index < nodes.Count; index++)
            {
                UIEffectNode node = nodes[index];
                if (node == null)
                {
                    continue;
                }

                float absoluteX = parentX + node.x;
                float absoluteY = parentY + node.y;
                bool explicitMerge =
                    CanCollapseParentIntegratedTextWrapper(node);
                bool inferredMerge = visualParent != null &&
                    CanInferParentIntegratedTextWrapper(
                        node,
                        visualParent);
                float requiredSimilarity = inferredMerge
                    ? InferredIntegratedParentVisualSimilarity
                    : IntegratedParentVisualSimilarity;
                float requiredLead = inferredMerge
                    ? InferredIntegratedParentVisualLead
                    : IntegratedParentVisualLead;
                if (visualParent != null &&
                    (explicitMerge || inferredMerge) &&
                    TryGetParentIntegratedVisualScore(
                        node,
                        absoluteX,
                        absoluteY,
                        schema,
                        reference,
                        renderer,
                        visualParent,
                        out float parentScore,
                        out float ownScore,
                        true) &&
                    parentScore >= requiredSimilarity &&
                    parentScore - ownScore >= requiredLead)
                {
                    List<UIEffectNode> children = node.children
                        .Where(child => child != null)
                        .ToList();
                    foreach (UIEffectNode child in children)
                    {
                        child.x += node.x;
                        child.y += node.y;
                    }

                    nodes.RemoveAt(index);
                    nodes.InsertRange(index, children);
                    visualFailures.Remove(node);
                    collapsed++;
                    Debug.Log(
                        $"[UIPrefabGenerator] 折叠父图已包含的视觉节点 " +
                        $"{node.name}（父图局部分数 {parentScore:F3}，" +
                        $"节点自身分数 {ownScore:F3}，" +
                        (inferredMerge
                            ? "由父图原始尺寸与像素证据推断"
                            : "Schema mayMerge 授权") +
                        "）。");
                    index--;
                    continue;
                }

                VisualParentContext childVisualParent =
                    TryCreateVisualParentContext(
                        node,
                        absoluteX,
                        absoluteY) ?? visualParent;
                collapsed += CollapseParentIntegratedTextWrappers(
                    node.children,
                    absoluteX,
                    absoluteY,
                    schema,
                    reference,
                    renderer,
                    childVisualParent);
            }

            return collapsed;
        }

        /// <summary>
        /// 执行Reconcile文本Artwork相关逻辑。
        /// </summary>
        private int ReconcileTextArtwork(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            VisualParentContext visualParent = null)
        {
            if (nodes == null)
            {
                return 0;
            }

            int converted = 0;
            for (int index = 0; index < nodes.Count; index++)
            {
                UIEffectNode node = nodes[index];
                if (node == null)
                {
                    continue;
                }

                float absoluteX = parentX + node.x;
                float absoluteY = parentY + node.y;
                if (LooksLikeArtworkText(node))
                {
                    bool matched = TryFindBestArtworkMatch(
                        node,
                        absoluteX,
                        absoluteY,
                        schema,
                        reference,
                        renderer,
                        visualParent,
                        out VisualDescriptor source,
                        out SpriteEntry foreground,
                        out float foregroundScore,
                        out float foregroundMargin);
                    if (matched && foreground != null)
                    {
                        SpriteEntry back = null;
                        SpriteEntry front = foreground;
                        float layeredScore = foregroundScore;
                        bool hasLayer = node.mayLayer &&
                                        TryFindSupportingArtLayer(
                                            source,
                                            foreground,
                                            foregroundScore,
                                            renderer,
                                            out back,
                                            out front,
                                            out layeredScore);
                        ConvertTextNodeToImage(node, front);
                        if (hasLayer)
                        {
                            UIEffectNode backdrop = CreateArtworkLayer(
                                node,
                                GetUniqueSiblingName(
                                    nodes,
                                    node.name + "Backdrop"),
                                back);
                            nodes.Insert(index, backdrop);
                            index++;
                            Debug.Log(
                                $"[UIPrefabGenerator] {node.name} 识别为" +
                                $"双层艺术字：{GetSpriteResourcePath(back)} + " +
                                $"{GetSpriteResourcePath(front)}" +
                                $"（{layeredScore:F3}）。");
                        }
                        else
                        {
                            Debug.Log(
                                $"[UIPrefabGenerator] {node.name} 由 Text " +
                                $"校正为 Sprite：" +
                                GetSpriteResourcePath(front) +
                                $"（{foregroundScore:F3}/" +
                                $"{foregroundMargin:F3}）");
                        }

                        visualFailures.Remove(node);
                        converted++;
                        continue;
                    }
                }

                converted += ReconcileTextArtwork(
                    node.children,
                    absoluteX,
                    absoluteY,
                    schema,
                    reference,
                    renderer,
                    visualParentContexts.TryGetValue(node, out VisualParentContext resolvedParent)
                        ? resolvedParent : visualParent);
            }

            return converted;
        }

        /// <summary>
        /// 尝试查找BestArtworkMatch，并返回是否成功。
        /// </summary>
        private bool TryFindBestArtworkMatch(
            UIEffectNode node,
            float absoluteX,
            float absoluteY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            VisualParentContext visualParent,
            out VisualDescriptor selectedSource,
            out SpriteEntry selected,
            out float selectedScore,
            out float selectedMargin)
        {
            selectedSource = null;
            selected = null;
            selectedScore = 0f;
            selectedMargin = 0f;
            float centerX = absoluteX + node.width * 0.5f;
            float centerY = absoluteY + node.height * 0.5f;
            float[] expansionFactors = { 0f, 0.08f, 0.16f };
            foreach (float expansion in expansionFactors)
            {
                float sampleWidth = node.width * (1f + expansion);
                float sampleHeight = node.height *
                                     (1f + expansion * 0.25f);
                VisualDescriptor source = CreateReferenceDescriptor(
                    reference,
                    schema.designWidth,
                    schema.designHeight,
                    centerX - sampleWidth * 0.5f,
                    centerY - sampleHeight * 0.5f,
                    sampleWidth,
                    sampleHeight,
                    Array.Empty<Rect>());
                ApplyVisualParentBackdrop(source, centerX - sampleWidth * 0.5f,
                    centerY - sampleHeight * 0.5f, sampleWidth, sampleHeight, visualParent);
                SpriteEntry best = null;
                float bestScore = 0f;
                float secondScore = 0f;
                bool acceptedByGenericMatcher = source != null &&
                                                TryFindBestVisualMatch(
                                                    source,
                                                    false,
                                                    false,
                                                    false,
                                                    false,
                                                    true,
                                                    "title art logo",
                                                    renderer,
                                                    out best,
                                                    out bestScore,
                                                    out secondScore,
                                                    out _,
                                                    entry => IsArtworkCandidate(entry, source, renderer),
                                                    preferredResources: node.resourceCandidates);
                float margin = bestScore - secondScore;
                bool acceptedAsArtwork = best != null &&
                                         (acceptedByGenericMatcher ||
                                          bestScore >=
                                          ArtworkVisualSimilarity &&
                                          margin >= ArtworkVisualMargin ||
                                          bestScore >=
                                          ArtworkStrongVisualSimilarity &&
                                          margin > 0f);
                if (!acceptedAsArtwork ||
                    selected != null && bestScore <= selectedScore)
                {
                    continue;
                }

                selectedSource = source;
                selected = best;
                selectedScore = bestScore;
                selectedMargin = margin;
            }

            return selected != null;
        }

        private static bool IsArtworkCandidate(SpriteEntry entry, VisualDescriptor source,
            SpritePreviewRenderer renderer)
        {
            return source != null && !entry.HasBorder &&
                GetAspectSimilarity(source.Aspect, entry.Aspect) >= 0.68f &&
                GetDimensionSimilarity(source.Width, entry.AuthoredWidth) >= 0.58f &&
                GetDimensionSimilarity(source.Height, entry.AuthoredHeight) >= 0.58f &&
                HasAlphaVariation(GetSpriteDescriptor(entry, source, renderer));
        }

        /// <summary>
        /// 执行LooksLikeArtwork文本相关逻辑。
        /// </summary>
        private static bool LooksLikeArtworkText(UIEffectNode node)
        {
            if (node == null ||
                !string.Equals(
                    node.type,
                    "Text",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    node.textMode,
                    "Editable",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsArtTextMode(node))
            {
                return true;
            }

            string hint = NormalizeSemanticPhrase(
                (node.name ?? string.Empty) + " " +
                (node.semantic ?? string.Empty) + " " +
                (node.visualKind ?? string.Empty));
            int textLength = (node.text ?? string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim()
                .Length;
            return node.fontSize >= 48f && textLength > 0 &&
                   textLength <= 16 &&
                   (ContainsAny(
                        hint,
                        "title",
                        "logo",
                        "arttext",
                        "标题",
                        "题字",
                        "艺术字") ||
                    (node.text ?? string.Empty).Contains("\n"));
        }

        /// <summary>
        /// 执行判断是否Art文本Mode相关逻辑。
        /// </summary>
        private static bool IsArtTextMode(UIEffectNode node)
        {
            return node != null &&
                   (string.Equals(
                        node.textMode,
                        "ArtText",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        node.visualKind,
                        "ArtText",
                        StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 尝试查找SupportingArt层级，并返回是否成功。
        /// </summary>
        private bool TryFindSupportingArtLayer(
            VisualDescriptor source,
            SpriteEntry seed,
            float seedScore,
            SpritePreviewRenderer renderer,
            out SpriteEntry back,
            out SpriteEntry front,
            out float bestLayeredScore)
        {
            back = null;
            front = seed;
            bestLayeredScore = seedScore;
            VisualDescriptor seedDescriptor = GetSpriteDescriptor(
                seed,
                source,
                renderer);
            if (seedDescriptor == null)
            {
                return false;
            }

            foreach (SpriteEntry candidate in sprites)
            {
                if (ReferenceEquals(candidate, seed) ||
                    candidate.HasBorder ||
                    GetAspectSimilarity(
                        seed.Aspect,
                        candidate.Aspect) < 0.72f ||
                    GetDimensionSimilarity(
                        seed.AuthoredWidth,
                        candidate.AuthoredWidth) < 0.65f ||
                    GetDimensionSimilarity(
                        seed.AuthoredHeight,
                        candidate.AuthoredHeight) < 0.65f)
                {
                    continue;
                }

                VisualDescriptor candidateDescriptor =
                    GetSpriteDescriptor(candidate, source, renderer);
                if (candidateDescriptor == null ||
                    !HasAlphaVariation(candidateDescriptor))
                {
                    continue;
                }

                VisualDescriptor candidateBehind = CompositeLayers(
                    candidateDescriptor,
                    seedDescriptor,
                    source);
                float behindScore = GetVisualExplanationScore(
                    source,
                    candidateBehind,
                    false);
                if (behindScore > bestLayeredScore)
                {
                    bestLayeredScore = behindScore;
                    back = candidate;
                    front = seed;
                }

                VisualDescriptor candidateInFront = CompositeLayers(
                    seedDescriptor,
                    candidateDescriptor,
                    source);
                float frontScore = GetVisualExplanationScore(
                    source,
                    candidateInFront,
                    false);
                if (frontScore > bestLayeredScore)
                {
                    bestLayeredScore = frontScore;
                    back = seed;
                    front = candidate;
                }
            }

            return back != null &&
                   bestLayeredScore >= LayeredArtVisualSimilarity &&
                   bestLayeredScore - seedScore >= LayeredArtImprovement;
        }

        /// <summary>
        /// 执行CompositeLayers相关逻辑。
        /// </summary>
        private static VisualDescriptor CompositeLayers(
            VisualDescriptor back,
            VisualDescriptor front,
            VisualDescriptor geometry)
        {
            var pixels = new Color[geometry.Pixels.Length];
            var mask = new bool[pixels.Length];
            for (int index = 0; index < pixels.Length; index++)
            {
                Color backColor = back.Pixels[index];
                Color frontColor = front.Pixels[index];
                float backAlpha = Mathf.Clamp01(backColor.a);
                float frontAlpha = Mathf.Clamp01(frontColor.a);
                float alpha = frontAlpha +
                              backAlpha * (1f - frontAlpha);
                if (alpha <= 0.001f)
                {
                    pixels[index] = Color.clear;
                    continue;
                }

                float backWeight = backAlpha * (1f - frontAlpha);
                pixels[index] = new Color(
                    (frontColor.r * frontAlpha +
                     backColor.r * backWeight) / alpha,
                    (frontColor.g * frontAlpha +
                     backColor.g * backWeight) / alpha,
                    (frontColor.b * frontAlpha +
                     backColor.b * backWeight) / alpha,
                    alpha);
                mask[index] = alpha >= 0.05f;
            }

            return new VisualDescriptor(
                pixels,
                mask,
                geometry.Aspect,
                geometry.Width,
                geometry.Height,
                false);
        }

        /// <summary>
        /// 转换文本NodeTo图片。
        /// </summary>
        private static void ConvertTextNodeToImage(
            UIEffectNode node,
            SpriteEntry entry)
        {
            ApplyAuthoredArtworkSize(node, entry);
            node.type = "Image";
            node.semantic = "titleArt";
            node.visualKind = "ArtText";
            node.textMode = "Auto";
            node.text = string.Empty;
            node.fontSize = 32f;
            node.characterSpacing = 0f;
            node.alignment = "Center";
            node.bold = false;
            node.resource = GetSpriteResourcePath(entry);
            node.resourceCandidates.Clear();
            node.color = "#FFFFFFFF";
            node.intentionalColor = false;
            node.preserveAspect = false;
            node.sliced = false;
            node.raycastTarget = false;
            node.mayLayer = false;
        }

        /// <summary>
        /// 创建Artwork层级。
        /// </summary>
        private static UIEffectNode CreateArtworkLayer(
            UIEffectNode source,
            string name,
            SpriteEntry entry)
        {
            var layer = new UIEffectNode
            {
                name = name,
                type = "Image",
                semantic = "titleArt",
                visualKind = "ArtText",
                anchor = source.anchor,
                x = source.x,
                y = source.y,
                width = source.width,
                height = source.height,
                color = "#FFFFFFFF",
                resource = GetSpriteResourcePath(entry),
                binding = "No",
            };
            ApplyAuthoredArtworkSize(layer, entry);
            return layer;
        }

        /// <summary>
        /// 应用AuthoredArtwork尺寸。
        /// </summary>
        private static void ApplyAuthoredArtworkSize(
            UIEffectNode node,
            SpriteEntry entry)
        {
            float authoredWidth = entry.AuthoredWidth;
            float authoredHeight = entry.AuthoredHeight;
            if (GetDimensionSimilarity(
                    node.width,
                    authoredWidth) < 0.65f ||
                GetDimensionSimilarity(
                    node.height,
                    authoredHeight) < 0.65f)
            {
                return;
            }

            float centerX = node.x + node.width * 0.5f;
            float centerY = node.y + node.height * 0.5f;
            node.width = authoredWidth;
            node.height = authoredHeight;
            node.x = centerX - authoredWidth * 0.5f;
            node.y = centerY - authoredHeight * 0.5f;
        }

        /// <summary>
        /// 获取UniqueSibling名称。
        /// </summary>
        private static string GetUniqueSiblingName(
            IEnumerable<UIEffectNode> siblings,
            string preferredName)
        {
            var names = new HashSet<string>(
                (siblings ?? Enumerable.Empty<UIEffectNode>())
                .Where(node => node != null)
                .Select(node => node.name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(preferredName))
            {
                return preferredName;
            }

            int suffix = 2;
            string candidate;
            do
            {
                candidate = preferredName + suffix;
                suffix++;
            }
            while (names.Contains(candidate));
            return candidate;
        }

        /// <summary>
        /// 执行SuppressBaked文本节点相关逻辑。
        /// </summary>
        private int SuppressBakedTextNodes(
            List<UIEffectNode> nodes,
            float parentX,
            float parentY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            VisualParentContext visualParent)
        {
            if (nodes == null)
            {
                return 0;
            }

            int suppressed = 0;
            for (int index = 0; index < nodes.Count; index++)
            {
                UIEffectNode node = nodes[index];
                if (node == null)
                {
                    continue;
                }

                float absoluteX = parentX + node.x;
                float absoluteY = parentY + node.y;
                bool shouldCheckBakedText = visualParent != null &&
                    ShouldCheckBakedText(node, visualParent.Node);
                float parentScore = 0f;
                float ownScore = 0f;
                bool evaluatedBakedText = shouldCheckBakedText &&
                    TryGetParentIntegratedVisualScore(
                        node,
                        absoluteX,
                        absoluteY,
                        schema,
                        reference,
                        renderer,
                        visualParent,
                        out parentScore,
                        out ownScore);
                float bakedTextThreshold = string.Equals(
                    node.textMode,
                    "PossiblyBaked",
                    StringComparison.OrdinalIgnoreCase)
                        ? ExplicitBakedTextVisualSimilarity
                        : BakedTextVisualSimilarity;
                if (evaluatedBakedText &&
                    parentScore >= bakedTextThreshold &&
                    parentScore - ownScore >= IntegratedParentVisualLead)
                {
                    nodes.RemoveAt(index);
                    index--;
                    suppressed++;
                    Debug.Log(
                        $"[UIPrefabGenerator] 删除父 Sprite 已烘焙的文字 " +
                        $"{node.name}（局部分数 {parentScore:F3}）。");
                    continue;
                }

                if (shouldCheckBakedText)
                {
                    Debug.Log(
                        $"[UIPrefabGenerator] 烘焙文字复核 {node.name}：" +
                        (evaluatedBakedText
                            ? $"父图局部 {parentScore:F3}，节点自身 " +
                              $"{ownScore:F3}，门槛 " +
                              $"{bakedTextThreshold:F3}"
                            : "无法取得有效父图局部样本") +
                        "。");
                }

                VisualParentContext childVisualParent =
                    TryCreateVisualParentContext(
                        node,
                        absoluteX,
                        absoluteY) ?? (IsVisualNode(node) ? null : visualParent);
                suppressed += SuppressBakedTextNodes(
                    node.children,
                    absoluteX,
                    absoluteY,
                    schema,
                    reference,
                    renderer,
                    childVisualParent);
            }

            return suppressed;
        }

        /// <summary>
        /// 执行判断是否检查Baked文本相关逻辑。
        /// </summary>
        private static bool ShouldCheckBakedText(
            UIEffectNode textNode,
            UIEffectNode visualParent)
        {
            if (textNode == null || visualParent == null ||
                !string.Equals(
                    textNode.type,
                    "Text",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    textNode.textMode,
                    "Editable",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    textNode.binding,
                    "Yes",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(
                    textNode.textMode,
                    "PossiblyBaked",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string parentHint = NormalizeSemanticPhrase(
                (visualParent.name ?? string.Empty) + " " +
                (visualParent.semantic ?? string.Empty) + " " +
                (visualParent.visualKind ?? string.Empty));
            string text = (textNode.text ?? string.Empty).Trim();
            return text.Length > 0 && text.Length <= 4 &&
                   ContainsAny(
                       parentHint,
                       "badge",
                       "emblem",
                       "crest",
                       "rank",
                       "medal",
                       "徽章",
                       "排名");
        }

        /// <summary>
        /// 执行判断能否CollapseParentIntegrated文本Wrapper相关逻辑。
        /// </summary>
        private bool CanCollapseParentIntegratedTextWrapper(
            UIEffectNode node)
        {
            return node != null &&
                   node.mayMerge &&
                   !explicitVisualResources.Contains(node) &&
                   string.Equals(
                       node.type,
                       "Image",
                       StringComparison.OrdinalIgnoreCase) &&
                   node.children != null &&
                   node.children.Count > 0 &&
                   ContainsTextDescendant(node.children) &&
                   !ContainsNonTextGraphicDescendant(node.children);
        }

        /// <summary>
        /// 执行判断能否推断ParentIntegrated文本Wrapper相关逻辑。
        /// </summary>
        private bool CanInferParentIntegratedTextWrapper(
            UIEffectNode node,
            VisualParentContext visualParent)
        {
            if (node == null || visualParent == null ||
                node.mayMerge ||
                IsIndependentTextSurface(node) ||
                explicitVisualResources.Contains(node) ||
                !string.Equals(
                    node.type,
                    "Image",
                    StringComparison.OrdinalIgnoreCase) ||
                node.children == null || node.children.Count == 0 ||
                !ContainsTextDescendant(node.children) ||
                ContainsNonTextGraphicDescendant(node.children) ||
                node.raycastTarget || node.mayLayer ||
                visualParent.Entry == null ||
                visualParent.Entry.HasBorder ||
                !visualMatchEvidence.TryGetValue(
                    visualParent.Node,
                    out VisualMatchEvidence parentEvidence) ||
                parentEvidence.Score < OccludedVisualSimilarity)
            {
                return false;
            }

            float widthSimilarity = Mathf.Min(
                visualParent.Node.width,
                visualParent.Entry.AuthoredWidth) /
                Mathf.Max(
                    0.0001f,
                    Mathf.Max(
                        visualParent.Node.width,
                        visualParent.Entry.AuthoredWidth));
            float heightSimilarity = Mathf.Min(
                visualParent.Node.height,
                visualParent.Entry.AuthoredHeight) /
                Mathf.Max(
                    0.0001f,
                    Mathf.Max(
                        visualParent.Node.height,
                        visualParent.Entry.AuthoredHeight));
            return (widthSimilarity + heightSimilarity) * 0.5f >=
                   InferredIntegratedParentSizeSimilarity;
        }

        private static bool IsIndependentTextSurface(UIEffectNode node)
        {
            // Similar average colors are not proof that a field's edge/gradient is baked
            // into its ancestor. Keep declared data surfaces unless mayMerge authorizes it.
            string hint = NormalizeSemanticPhrase((node.name ?? string.Empty) + " " +
                (node.semantic ?? string.Empty));
            return ContainsAny(hint, "field", "bar", "plate", "badge", "tile", "cell",
                "card", "row", "button", "toggle", "字段", "条", "牌");
        }

        /// <summary>
        /// 执行判断是否包含文本Descendant相关逻辑。
        /// </summary>
        private static bool ContainsTextDescendant(
            IEnumerable<UIEffectNode> nodes)
        {
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                if (string.Equals(
                        node.type,
                        "Text",
                        StringComparison.OrdinalIgnoreCase) ||
                    ContainsTextDescendant(node.children))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 执行判断是否包含Non文本GraphicDescendant相关逻辑。
        /// </summary>
        private static bool ContainsNonTextGraphicDescendant(
            IEnumerable<UIEffectNode> nodes)
        {
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                if (IsRenderedGraphicNode(node) ||
                    ContainsNonTextGraphicDescendant(node.children))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 尝试获取ParentIntegratedVisualScore，并返回是否成功。
        /// </summary>
        private bool TryGetParentIntegratedVisualScore(
            UIEffectNode node,
            float absoluteX,
            float absoluteY,
            UIEffectSchema schema,
            Texture2D reference,
            SpritePreviewRenderer renderer,
            VisualParentContext visualParent,
            out float parentScore,
            out float ownScore,
            bool excludeDescendants = false)
        {
            parentScore = 0f;
            ownScore = 0f;
            float localX = absoluteX - visualParent.AbsoluteX;
            float localY = absoluteY - visualParent.AbsoluteY;
            const float containmentTolerance = 1f;
            if (localX < -containmentTolerance ||
                localY < -containmentTolerance ||
                localX + node.width >
                visualParent.Node.width + containmentTolerance ||
                localY + node.height >
                visualParent.Node.height + containmentTolerance)
            {
                return false;
            }

            var occlusions = new List<Rect>();
            if (excludeDescendants)
            {
                CollectVisualCoverage(
                    node.children,
                    absoluteX,
                    absoluteY,
                    occlusions);
                CollectTextCoverage(
                    node.children,
                    absoluteX,
                    absoluteY,
                    occlusions);
            }

            VisualDescriptor source = CreateReferenceDescriptor(
                reference,
                schema.designWidth,
                schema.designHeight,
                absoluteX,
                absoluteY,
                node.width,
                node.height,
                occlusions);
            VisualDescriptor parentRegion = renderer.RenderRegion(
                visualParent.Entry.Sprite,
                visualParent.Node.width,
                visualParent.Node.height,
                new Rect(localX, localY, node.width, node.height),
                visualParent.Entry.HasBorder);
            if (source == null || parentRegion == null)
            {
                return false;
            }

            parentScore = GetVisualExplanationScore(
                source,
                parentRegion,
                false);
            SpriteEntry ownEntry = TryGetSpriteEntry(node.resource);
            if (ownEntry != null)
            {
                VisualDescriptor ownDescriptor = GetSpriteDescriptor(
                    ownEntry,
                    source,
                    renderer);
                if (ownDescriptor != null)
                {
                    ownScore = GetVisualExplanationScore(
                        source,
                        ownDescriptor,
                        false);
                }
            }

            return true;
        }

        /// <summary>
        /// 获取VisualExplanationScore。
        /// </summary>
        private static float GetVisualExplanationScore(
            VisualDescriptor source,
            VisualDescriptor candidate,
            bool preferBorder)
        {
            float aspectSimilarity = GetAspectSimilarity(
                source.Aspect,
                candidate.Aspect);
            float authoredScore = CompareDescriptors(
                source,
                candidate,
                aspectSimilarity,
                preferBorder);
            float compositeScore = CompareCompositeDescriptors(
                source,
                candidate,
                aspectSimilarity,
                preferBorder,
                true,
                out Color imageColor);
            compositeScore -= GetTintTransformationPenalty(imageColor);
            return Mathf.Max(authoredScore, compositeScore);
        }

        /// <summary>
        /// 尝试创建VisualParent上下文，并返回是否成功。
        /// </summary>
        private VisualParentContext TryCreateVisualParentContext(
            UIEffectNode node,
            float absoluteX,
            float absoluteY)
        {
            if (!IsVisualNode(node) ||
                string.IsNullOrWhiteSpace(node.resource))
            {
                return null;
            }

            if (visualParentContexts.TryGetValue(node, out VisualParentContext resolved) &&
                Mathf.Approximately(resolved.AbsoluteX, absoluteX) &&
                Mathf.Approximately(resolved.AbsoluteY, absoluteY) &&
                string.Equals(GetSpriteResourcePath(resolved.Entry), node.resource,
                    StringComparison.OrdinalIgnoreCase))
                return resolved;

            SpriteEntry entry = TryGetSpriteEntry(node.resource);
            return entry == null
                ? null
                : new VisualParentContext(
                    node,
                    entry,
                    absoluteX,
                    absoluteY);
        }

        /// <summary>
        /// 尝试获取精灵图Entry，并返回是否成功。
        /// </summary>
        private SpriteEntry TryGetSpriteEntry(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource))
            {
                return null;
            }

            return sprites.FirstOrDefault(candidate =>
                string.Equals(
                    GetSpriteResourcePath(candidate),
                    resource,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 执行收集Explicit视觉资源相关逻辑。
        /// </summary>
        private void CollectExplicitVisualResources(
            IEnumerable<UIEffectNode> nodes)
        {
            foreach (UIEffectNode node in
                     nodes ?? Enumerable.Empty<UIEffectNode>())
            {
                if (node == null)
                {
                    continue;
                }

                if (IsVisualNode(node) &&
                    HasExplicitAssetResource(node))
                {
                    explicitVisualResources.Add(node);
                }

                CollectExplicitVisualResources(node.children);
            }
        }

        /// <summary>
        /// 获取TintTransformationPenalty。
        /// </summary>
        private static float GetTintTransformationPenalty(Color color)
        {
            return GetTintTransformationDistance(color) * 0.06f;
        }

        /// <summary>
        /// 获取TintTransformationDistance。
        /// </summary>
        private static float GetTintTransformationDistance(Color color)
        {
            return
                (1f - Mathf.Clamp01(color.r) +
                 1f - Mathf.Clamp01(color.g) +
                 1f - Mathf.Clamp01(color.b)) / 3f;
        }

        /// <summary>
        /// 获取VisualSemanticHint。
        /// </summary>
        private static string GetVisualSemanticHint(UIEffectNode node)
        {
            return node == null
                ? string.Empty
                : (node.semantic + " " + node.visualKind + " " +
                   node.name).Trim();
        }

        /// <summary>
        /// 执行判断是否KeepIntentional颜色相关逻辑。
        /// </summary>
        private static bool ShouldKeepIntentionalColor(
            UIEffectNode node,
            UIEffectSchema schema,
            Texture2D reference,
            float absoluteX,
            float absoluteY)
        {
            if (ShouldKeepIntentionalOverlay(node, schema))
            {
                return true;
            }

            if (node == null || schema == null || reference == null ||
                !node.intentionalColor ||
                node.children?.Count > 0 ||
                node.width <= 1f || node.height <= 1f ||
                schema.designWidth <= 0f || schema.designHeight <= 0f ||
                !ColorUtility.TryParseHtmlString(
                    node.color,
                    out Color declaredColor) ||
                declaredColor.a < 0.98f)
            {
                return false;
            }

            const int samplesPerAxis = 8;
            Color total = Color.clear;
            float minimumRed = 1f;
            float minimumGreen = 1f;
            float minimumBlue = 1f;
            float maximumRed = 0f;
            float maximumGreen = 0f;
            float maximumBlue = 0f;
            int count = 0;
            for (int sampleY = 0; sampleY < samplesPerAxis; sampleY++)
            {
                float normalizedY = Mathf.Lerp(
                    0.1f,
                    0.9f,
                    (sampleY + 0.5f) / samplesPerAxis);
                for (int sampleX = 0;
                     sampleX < samplesPerAxis;
                     sampleX++)
                {
                    float normalizedX = Mathf.Lerp(
                        0.1f,
                        0.9f,
                        (sampleX + 0.5f) / samplesPerAxis);
                    Color sample = reference.GetPixelBilinear(
                        Mathf.Clamp01(
                            (absoluteX + node.width * normalizedX) /
                            schema.designWidth),
                        1f - Mathf.Clamp01(
                            (absoluteY + node.height * normalizedY) /
                            schema.designHeight));
                    total += sample;
                    minimumRed = Mathf.Min(minimumRed, sample.r);
                    minimumGreen = Mathf.Min(minimumGreen, sample.g);
                    minimumBlue = Mathf.Min(minimumBlue, sample.b);
                    maximumRed = Mathf.Max(maximumRed, sample.r);
                    maximumGreen = Mathf.Max(maximumGreen, sample.g);
                    maximumBlue = Mathf.Max(maximumBlue, sample.b);
                    count++;
                }
            }

            Color mean = total / Mathf.Max(1, count);
            float range = Mathf.Max(
                maximumRed - minimumRed,
                Mathf.Max(
                    maximumGreen - minimumGreen,
                    maximumBlue - minimumBlue));
            return range <= 0.025f &&
                   GetColorError(mean, declaredColor) <= 0.035f;
        }

        /// <summary>
        /// 执行判断是否KeepIntentionalOverlay相关逻辑。
        /// </summary>
        private static bool ShouldKeepIntentionalOverlay(
            UIEffectNode node,
            UIEffectSchema schema)
        {
            if (node == null || schema == null ||
                !node.intentionalColor ||
                node.width <= 0f || node.height <= 0f ||
                schema.designWidth <= 0f || schema.designHeight <= 0f)
            {
                return false;
            }

            string hint = GetVisualSemanticHint(node).ToLowerInvariant();
            bool isOverlay = hint.Contains("overlay") ||
                             hint.Contains("dimmer") ||
                             hint.Contains("dimlayer") ||
                             hint.Contains("scrim") ||
                             hint.Contains("遮罩");
            if (!isOverlay ||
                node.width * node.height <
                schema.designWidth * schema.designHeight * 0.25f)
            {
                return false;
            }

            return ColorUtility.TryParseHtmlString(
                       node.color,
                       out Color color) &&
                   color.a < 0.98f;
        }

        /// <summary>
        /// 执行判断是否CompactGraphic节点相关逻辑。
        /// </summary>
        private static bool IsCompactGraphicNode(
            UIEffectNode node,
            UIEffectSchema schema)
        {
            if (node == null ||
                schema == null ||
                !IsVisualNode(node) ||
                ContainsTextDescendant(node.children) ||
                node.width <= 0f ||
                node.height <= 0f)
            {
                return false;
            }

            float aspect = node.width / node.height;
            float designShortEdge = Mathf.Min(
                schema.designWidth,
                schema.designHeight);
            return aspect >= 0.7f &&
                   aspect <= 1.43f &&
                   Mathf.Max(node.width, node.height) <=
                   designShortEdge * 0.1f;
        }

        /// <summary>
        /// 执行判断是否ShapeSensitive视觉节点相关逻辑。
        /// </summary>
        private static bool IsShapeSensitiveVisualNode(
            UIEffectNode node,
            UIEffectSchema schema)
        {
            if (IsCompactGraphicNode(node, schema))
            {
                return true;
            }

            string hint = NormalizeSemanticPhrase(
                GetVisualSemanticHint(node));
            return ContainsAny(
                hint,
                "icon",
                "emblem",
                "badge",
                "crest",
                "logo",
                "avatar",
                "portrait",
                "medal",
                "seal",
                "flag",
                "banner",
                "symbol",
                "ornament",
                "图标",
                "徽标",
                "徽章",
                "头像",
                "旗帜",
                "勋章",
                "印章");
        }

        /// <summary>
        /// 获取VisualSemanticBonus。
        /// </summary>
        private static float GetVisualSemanticBonus(
            string semanticHint,
            SpriteEntry entry)
        {
            if (string.IsNullOrWhiteSpace(semanticHint) || entry == null)
            {
                return 0f;
            }

            string hint = NormalizeSemanticPhrase(semanticHint);
            string candidate = entry.PathKey;
            float bonus = 0f;
            if (ContainsAny(
                    hint,
                    "icon",
                    "emblem",
                    "badge",
                    "crest") &&
                ContainsAny(
                    candidate,
                    "icon",
                    "emblem",
                    "badge",
                    "crest"))
            {
                bonus += 0.03f;
            }

            if (ContainsAny(hint, "flag", "banner") &&
                ContainsAny(candidate, "flag", "banner"))
            {
                bonus += 0.025f;
            }

            if (entry.HasBorder &&
                ContainsAny(
                    hint,
                    "field",
                    "bar",
                    "plate",
                    "row",
                    "cell",
                    "字段",
                    "条",
                    "格"))
            {
                // Sprite Border is independent structural evidence that an
                // asset was authored for a stretchable surface. Keep the
                // bonus small so it only resolves already-close pixel
                // candidates and cannot rescue a visibly wrong nine-slice.
                bonus += 0.012f;
            }

            // Independent semantic evidence is cumulative. For example an
            // emblem nested under a flag should prefer a flag icon over an
            // unrelated ranking icon, while the cap prevents filenames from
            // overpowering visibly contradictory screenshot pixels.
            return Mathf.Min(0.05f, bonus);
        }

        /// <summary>
        /// 获取Sliced目标缩放Penalty。
        /// </summary>
        private static float GetSlicedTargetScalePenalty(
            VisualDescriptor source,
            SpriteEntry entry,
            bool shapeSensitive)
        {
            if (source == null || entry == null ||
                !entry.HasBorder || shapeSensitive ||
                source.Width <= 0f || source.Height <= 0f ||
                entry.AuthoredWidth <= 0f ||
                entry.AuthoredHeight <= 0f)
            {
                return 0f;
            }

            float similarity;
            if (source.Aspect >= 1.8f)
            {
                // Horizontal nine-slices are expected to stretch in width;
                // their authored height remains useful independent evidence.
                similarity = Mathf.Min(
                    source.Height,
                    entry.AuthoredHeight) /
                    Mathf.Max(source.Height, entry.AuthoredHeight);
            }
            else if (source.Aspect <= 0.55f)
            {
                // Vertical nine-slices have the symmetric constraint.
                similarity = Mathf.Min(
                    source.Width,
                    entry.AuthoredWidth) /
                    Mathf.Max(source.Width, entry.AuthoredWidth);
            }
            else
            {
                // A near-square target may stretch on both axes, but a very
                // thin authored strip is still weaker evidence than a source
                // with comparable overall proportions.
                similarity = GetAspectSimilarity(
                    source.Aspect,
                    entry.Aspect);
            }

            float borderWidth = entry.BorderLeft + entry.BorderRight;
            float borderHeight = entry.BorderBottom + entry.BorderTop;
            float borderWidthFit = borderWidth <= 0.0001f
                ? 1f
                : Mathf.Clamp01(source.Width / borderWidth);
            float borderHeightFit = borderHeight <= 0.0001f
                ? 1f
                : Mathf.Clamp01(source.Height / borderHeight);
            float collapsedBorderPenalty =
                (1f - Mathf.Min(borderWidthFit, borderHeightFit)) *
                0.12f;

            // Nine-slice dimensions never hard-filter a candidate. These are
            // bounded structural tie-breakers: preserve the non-stretched
            // thickness, and penalize a target so small that Unity must crush
            // the authored border bands into each other.
            return (1f - Mathf.Clamp01(similarity)) * 0.07f +
                   collapsedBorderPenalty;
        }

        /// <summary>
        /// 获取SlicedStructuralFit。
        /// </summary>
        private static float GetSlicedStructuralFit(
            VisualDescriptor source,
            SpriteEntry entry)
        {
            if (source == null || entry == null || !entry.HasBorder ||
                source.Width <= 0f || source.Height <= 0f)
            {
                return 0f;
            }

            float thicknessFit = 1f;
            if (source.Aspect >= 1.8f)
            {
                thicknessFit = Mathf.Min(
                    source.Height,
                    entry.AuthoredHeight) /
                    Mathf.Max(source.Height, entry.AuthoredHeight);
            }
            else if (source.Aspect <= 0.55f)
            {
                thicknessFit = Mathf.Min(
                    source.Width,
                    entry.AuthoredWidth) /
                    Mathf.Max(source.Width, entry.AuthoredWidth);
            }

            float borderWidth = entry.BorderLeft + entry.BorderRight;
            float borderHeight = entry.BorderBottom + entry.BorderTop;
            float borderWidthFit = borderWidth <= 0.0001f
                ? 1f
                : Mathf.Clamp01(source.Width / borderWidth);
            float borderHeightFit = borderHeight <= 0.0001f
                ? 1f
                : Mathf.Clamp01(source.Height / borderHeight);
            return Mathf.Min(
                thicknessFit,
                Mathf.Min(borderWidthFit, borderHeightFit));
        }

        /// <summary>
        /// 执行规范化SemanticPhrase相关逻辑。
        /// </summary>
        private static string NormalizeSemanticPhrase(string value)
        {
            var builder = new StringBuilder(value?.Length ?? 0);
            foreach (char character in
                     (value ?? string.Empty).ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 执行判断是否包含Any相关逻辑。
        /// </summary>
        private static bool ContainsAny(
            string value,
            params string[] tokens)
        {
            return tokens.Any(token =>
                value.IndexOf(
                    token,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 获取精灵图Descriptor。
        /// </summary>
        private static VisualDescriptor GetSpriteDescriptor(
            SpriteEntry entry,
            VisualDescriptor source,
            SpritePreviewRenderer renderer)
        {
            string cacheKey = entry.AssetPath + "#" +
                              entry.Sprite.name + "|" +
                              entry.DependencyHash +
                              "|" + QualitySettings.activeColorSpace +
                              (entry.HasBorder
                                  ? $"|sliced:{Mathf.RoundToInt(source.Width)}x" +
                                    Mathf.RoundToInt(source.Height)
                                  : "|simple");
            if (VisualDescriptorCache.TryGetValue(
                    cacheKey,
                    out VisualDescriptor cached))
            {
                return cached;
            }

            VisualDescriptor result = renderer.Render(
                entry.Sprite,
                source.Width,
                source.Height,
                entry.HasBorder,
                entry.AuthoredWidth,
                entry.AuthoredHeight);
            if (result != null)
            {
                VisualDescriptorCache[cacheKey] = result;
            }

            return result;
        }

        /// <summary>
        /// 获取QuickVisualScore。
        /// </summary>
        private static float GetQuickVisualScore(
            VisualDescriptor source,
            VisualDescriptor candidate,
            float aspectSimilarity)
        {
            if (!TryGetIntersectionStats(
                    source,
                    candidate,
                    out Vector3 sourceMean,
                    out Vector3 candidateMean,
                    out float sourceEdge,
                    out float candidateEdge))
            {
                return 0f;
            }

            float colorDistance =
                (Mathf.Abs(sourceMean.x - candidateMean.x) +
                 Mathf.Abs(sourceMean.y - candidateMean.y) +
                 Mathf.Abs(sourceMean.z - candidateMean.z)) / 3f;
            float colorSimilarity = 1f - Mathf.Clamp01(colorDistance);
            float edgeSimilarity = 1f - Mathf.Clamp01(
                Mathf.Abs(sourceEdge - candidateEdge) * 2f);
            return colorSimilarity * 0.55f +
                   edgeSimilarity * 0.13f +
                   Mathf.Clamp01(aspectSimilarity) * 0.22f +
                   GetSizeSimilarity(source, candidate) * 0.1f;
        }

        /// <summary>
        /// 获取文本CoveredQuickScore。
        /// </summary>
        private static float GetTextCoveredQuickScore(
            VisualDescriptor source,
            VisualDescriptor candidate,
            float aspectSimilarity)
        {
            float structuralPrior = candidate.IsSliced ? 0.62f : 0.48f;
            if (IsTintable(candidate))
            {
                // A neutral Sprite can reproduce the screenshot color via
                // Image.color. Do not let its white source pixels remove it
                // from the shortlist before tint fitting runs.
                structuralPrior = 0.76f;
            }
            else if (HasAlphaVariation(candidate))
            {
                // Semi-transparent overlays are compared after compositing
                // over an inferred local backdrop.
                structuralPrior = Mathf.Max(structuralPrior, 0.68f);
            }

            float score = structuralPrior +
                          Mathf.Clamp01(aspectSimilarity) * 0.14f +
                          GetSizeSimilarity(source, candidate) * 0.1f;
            if (source.HasSpatialReferenceBackdrop &&
                HasAlphaVariation(candidate) &&
                !IsTintable(candidate))
            {
                // Keep correct translucent authored-color candidates in the
                // expensive shortlist even when text covers much of the bar.
                // A trimmed composite hint is deterministic and only nudges
                // the structural prior; final acceptance still uses the full
                // robust comparison and margin gates.
                score = score * 0.84f +
                        GetBackdropAwareQuickScore(source, candidate) * 0.16f;
            }

            return score;
        }

        /// <summary>
        /// 获取BackdropAwareQuickScore。
        /// </summary>
        private static float GetBackdropAwareQuickScore(
            VisualDescriptor source,
            VisualDescriptor candidate)
        {
            var errors = new List<float>();
            Vector3 fallback = ToVector(source.ReferenceBackdrop);
            for (int index = 0; index < source.Pixels.Length; index++)
            {
                if (!source.Mask[index])
                {
                    continue;
                }

                Color predicted = CompositeCandidate(
                    candidate.Pixels[index],
                    GetReferenceBackdrop(source, index, fallback),
                    Vector3.one);
                errors.Add(GetColorError(source.Pixels[index], predicted));
            }

            if (errors.Count < MinimumVisibleSamples)
            {
                return 0f;
            }

            errors.Sort();
            int keepCount = Mathf.Max(
                MinimumVisibleSamples,
                Mathf.FloorToInt(errors.Count * 0.82f));
            float total = 0f;
            for (int index = 0; index < keepCount; index++)
            {
                total += errors[index];
            }

            return 1f - Mathf.Clamp01(
                total / Mathf.Max(1, keepCount) / 0.32f);
        }

        /// <summary>
        /// 执行比较CompositeDescriptors相关逻辑。
        /// </summary>
        private static float CompareCompositeDescriptors(
            VisualDescriptor source,
            VisualDescriptor candidate,
            float aspectSimilarity,
            bool preferBorder,
            bool rejectTextOutliers,
            out Color imageColor)
        {
            bool tintable = IsTintable(candidate);
            var robustMask = new bool[source.Pixels.Length];
            for (int index = 0; index < robustMask.Length; index++)
            {
                robustMask[index] = source.Mask[index];
            }

            FitCompositeModel(
                source,
                candidate,
                robustMask,
                preferBorder,
                tintable,
                out Vector3 backdrop,
                out Vector3 tint);

            var residuals = new List<float>();
            var residualByIndex = new float[source.Pixels.Length];
            for (int index = 0; index < source.Pixels.Length; index++)
            {
                if (!source.Mask[index])
                {
                    continue;
                }

                Color predicted = CompositeCandidate(
                    candidate.Pixels[index],
                    GetReferenceBackdrop(source, index, backdrop),
                    tint);
                float residual = GetColorError(
                    source.Pixels[index],
                    predicted);
                residualByIndex[index] = residual;
                residuals.Add(residual);
            }

            if (residuals.Count < MinimumVisibleSamples)
            {
                imageColor = Color.white;
                return 0f;
            }

            if (rejectTextOutliers)
            {
                residuals.Sort();
                int keepIndex = Mathf.Clamp(
                    Mathf.FloorToInt(
                        (residuals.Count - 1) * 0.88f),
                    0,
                    residuals.Count - 1);
                float residualLimit =
                    residuals[keepIndex] + 0.0001f;
                for (int index = 0;
                     index < robustMask.Length;
                     index++)
                {
                    robustMask[index] = source.Mask[index] &&
                                        (residualByIndex[index] <=
                                         residualLimit ||
                                         IsProtectedSurfaceEdgeSample(
                                             index));
                }
            }

            FitCompositeModel(
                source,
                candidate,
                robustMask,
                preferBorder,
                tintable,
                out backdrop,
                out tint);

            float colorError = 0f;
            float colorWeight = 0f;
            float edgeError = 0f;
            float edgeWeight = 0f;
            for (int y = 0; y < VisualSampleSize; y++)
            {
                for (int x = 0; x < VisualSampleSize; x++)
                {
                    int index = y * VisualSampleSize + x;
                    if (!robustMask[index])
                    {
                        continue;
                    }

                    float weight = GetSampleWeight(index, preferBorder);
                    Color predicted = CompositeCandidate(
                        candidate.Pixels[index],
                        GetReferenceBackdrop(source, index, backdrop),
                        tint);
                    colorError += GetColorError(
                        source.Pixels[index],
                        predicted) * weight;
                    colorWeight += weight;

                    CompareCompositeEdge(
                        source,
                        candidate,
                        robustMask,
                        index,
                        x + 1 < VisualSampleSize ? index + 1 : -1,
                        backdrop,
                        tint,
                        weight,
                        ref edgeError,
                        ref edgeWeight);
                    CompareCompositeEdge(
                        source,
                        candidate,
                        robustMask,
                        index,
                        y + 1 < VisualSampleSize
                            ? index + VisualSampleSize
                            : -1,
                        backdrop,
                        tint,
                        weight,
                        ref edgeError,
                        ref edgeWeight);
                }
            }

            if (colorWeight < MinimumVisibleSamples)
            {
                imageColor = Color.white;
                return 0f;
            }

            float colorSimilarity = 1f - Mathf.Clamp01(
                colorError / (colorWeight * 0.32f));
            float edgeSimilarity = edgeWeight <= 0f
                ? colorSimilarity
                : 1f - Mathf.Clamp01(
                    edgeError / (edgeWeight * 0.32f));
            imageColor = tintable
                ? new Color(tint.x, tint.y, tint.z, 1f)
                : Color.white;
            float score = colorSimilarity * 0.55f +
                          edgeSimilarity * 0.22f +
                          Mathf.Clamp01(aspectSimilarity) * 0.13f +
                          GetSizeSimilarity(source, candidate) * 0.1f;
            if (source.HasSpatialReferenceBackdrop &&
                HasAlphaVariation(candidate) &&
                !tintable)
            {
                float foregroundStructure =
                    CompareForegroundStructure(
                        source,
                        candidate,
                        robustMask,
                        tint);
                score = score * 0.82f +
                        foregroundStructure * 0.18f;
            }

            return score;
        }

        /// <summary>
        /// 执行判断是否ProtectedSurfaceEdgeSample相关逻辑。
        /// </summary>
        private static bool IsProtectedSurfaceEdgeSample(int index)
        {
            int x = index % VisualSampleSize;
            int y = index / VisualSampleSize;
            const int protectedBand = 2;
            return x < protectedBand ||
                   y < protectedBand ||
                   x >= VisualSampleSize - protectedBand ||
                   y >= VisualSampleSize - protectedBand;
        }

        /// <summary>
        /// 执行FitCompositeModel相关逻辑。
        /// </summary>
        private static void FitCompositeModel(
            VisualDescriptor source,
            VisualDescriptor candidate,
            IReadOnlyList<bool> mask,
            bool preferBorder,
            bool tintable,
            out Vector3 backdrop,
            out Vector3 tint)
        {
            backdrop = Vector3.zero;
            tint = Vector3.one;
            for (int channel = 0; channel < 3; channel++)
            {
                if (source.HasReferenceBackdrop)
                {
                    float representativeBackdrop = GetChannel(
                        source.ReferenceBackdrop,
                        channel);
                    float tintNumerator = 0f;
                    float tintDenominator = 0f;
                    for (int index = 0;
                         index < source.Pixels.Length;
                         index++)
                    {
                        if (!mask[index])
                        {
                            continue;
                        }

                        float weight = GetSampleWeight(
                            index,
                            preferBorder);
                        Color candidateColor = candidate.Pixels[index];
                        float alpha = Mathf.Clamp01(candidateColor.a);
                        float sampleBackdrop = GetChannel(
                            GetReferenceBackdrop(
                                source,
                                index,
                                ToVector(source.ReferenceBackdrop)),
                            channel);
                        float q = alpha * GetChannel(
                            candidateColor,
                            channel);
                        float sourceWithoutBackdrop =
                            GetChannel(source.Pixels[index], channel) -
                            (1f - alpha) * sampleBackdrop;
                        tintNumerator += weight * q *
                                         sourceWithoutBackdrop;
                        tintDenominator += weight * q * q;
                    }

                    float backdropAwareTint = tintable &&
                                              tintDenominator > 0.00001f
                        ? tintNumerator / tintDenominator
                        : 1f;
                    SetChannel(
                        ref backdrop,
                        channel,
                        representativeBackdrop);
                    SetChannel(
                        ref tint,
                        channel,
                        Mathf.Clamp01(backdropAwareTint));
                    continue;
                }

                float pp = 0f;
                float pq = 0f;
                float qq = 0f;
                float ps = 0f;
                float qs = 0f;
                for (int index = 0; index < source.Pixels.Length; index++)
                {
                    if (!mask[index])
                    {
                        continue;
                    }

                    float weight = GetSampleWeight(index, preferBorder);
                    Color candidateColor = candidate.Pixels[index];
                    float alpha = Mathf.Clamp01(candidateColor.a);
                    float p = 1f - alpha;
                    float q = alpha * GetChannel(candidateColor, channel);
                    float s = GetChannel(source.Pixels[index], channel);
                    pp += weight * p * p;
                    pq += weight * p * q;
                    qq += weight * q * q;
                    ps += weight * p * s;
                    qs += weight * q * s;
                }

                float fittedBackdrop = 0f;
                float fittedTint = 1f;
                if (tintable)
                {
                    float determinant = pp * qq - pq * pq;
                    if (Mathf.Abs(determinant) > 0.00001f)
                    {
                        fittedBackdrop =
                            (ps * qq - qs * pq) / determinant;
                        fittedTint =
                            (qs * pp - ps * pq) / determinant;
                    }
                    else if (qq > 0.00001f)
                    {
                        fittedTint = qs / qq;
                    }
                }
                else if (pp > 0.00001f)
                {
                    // Keep authored colors intact, but infer the flattened
                    // screenshot color behind translucent Sprite pixels.
                    fittedBackdrop = (ps - pq) / pp;
                }

                fittedBackdrop = Mathf.Clamp01(fittedBackdrop);
                fittedTint = Mathf.Clamp01(fittedTint);
                SetChannel(ref backdrop, channel, fittedBackdrop);
                SetChannel(ref tint, channel, fittedTint);
            }
        }

        /// <summary>
        /// 执行比较CompositeEdge相关逻辑。
        /// </summary>
        private static void CompareCompositeEdge(
            VisualDescriptor source,
            VisualDescriptor candidate,
            IReadOnlyList<bool> mask,
            int from,
            int to,
            Vector3 backdrop,
            Vector3 tint,
            float weight,
            ref float error,
            ref float totalWeight)
        {
            if (to < 0 || !mask[from] || !mask[to])
            {
                return;
            }

            float sourceDelta =
                GetLuma(source.Pixels[to]) -
                GetLuma(source.Pixels[from]);
            float candidateDelta =
                GetLuma(CompositeCandidate(
                    candidate.Pixels[to],
                    GetReferenceBackdrop(source, to, backdrop),
                    tint)) -
                GetLuma(CompositeCandidate(
                    candidate.Pixels[from],
                    GetReferenceBackdrop(source, from, backdrop),
                    tint));
            error += Mathf.Abs(sourceDelta - candidateDelta) * weight;
            totalWeight += weight;
        }

        /// <summary>
        /// 执行CompositeCandidate相关逻辑。
        /// </summary>
        private static Color CompositeCandidate(
            Color candidate,
            Vector3 backdrop,
            Vector3 tint)
        {
            float alpha = Mathf.Clamp01(candidate.a);
            return new Color(
                backdrop.x * (1f - alpha) +
                candidate.r * tint.x * alpha,
                backdrop.y * (1f - alpha) +
                candidate.g * tint.y * alpha,
                backdrop.z * (1f - alpha) +
                candidate.b * tint.z * alpha,
                1f);
        }

        /// <summary>
        /// 获取引用Backdrop。
        /// </summary>
        private static Vector3 GetReferenceBackdrop(
            VisualDescriptor source,
            int index,
            Vector3 fallback)
        {
            if (source?.ReferenceBackdropPixels == null ||
                index < 0 ||
                index >= source.ReferenceBackdropPixels.Length)
            {
                return fallback;
            }

            return ToVector(source.ReferenceBackdropPixels[index]);
        }

        /// <summary>
        /// 执行比较ForegroundStructure相关逻辑。
        /// </summary>
        private static float CompareForegroundStructure(
            VisualDescriptor source,
            VisualDescriptor candidate,
            IReadOnlyList<bool> mask,
            Vector3 tint)
        {
            float score = 0f;
            float totalWeight = 0f;
            Vector3 fallback = ToVector(source.ReferenceBackdrop);
            for (int index = 0; index < source.Pixels.Length; index++)
            {
                if (!mask[index])
                {
                    continue;
                }

                Color candidateColor = candidate.Pixels[index];
                float alpha = Mathf.Clamp01(candidateColor.a);
                if (alpha < 0.04f)
                {
                    continue;
                }

                Vector3 backdrop = GetReferenceBackdrop(
                    source,
                    index,
                    fallback);
                Vector3 sourceForeground =
                    ToVector(source.Pixels[index]) - backdrop;
                Vector3 candidateForeground = alpha *
                    (Vector3.Scale(ToVector(candidateColor), tint) -
                     backdrop);
                float sourceMagnitude = sourceForeground.magnitude;
                float candidateMagnitude = candidateForeground.magnitude;
                if (sourceMagnitude < 0.012f &&
                    candidateMagnitude < 0.012f)
                {
                    score += alpha;
                    totalWeight += alpha;
                    continue;
                }

                float direction = sourceMagnitude <= 0.0001f ||
                                  candidateMagnitude <= 0.0001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector3.Dot(
                            sourceForeground,
                            candidateForeground) /
                        (sourceMagnitude * candidateMagnitude));
                float magnitude = 1f - Mathf.Clamp01(
                    Mathf.Abs(sourceMagnitude - candidateMagnitude) /
                    0.35f);
                float weight = Mathf.Max(0.1f, alpha);
                score += (direction * 0.65f + magnitude * 0.35f) *
                         weight;
                totalWeight += weight;
            }

            return totalWeight < MinimumVisibleSamples * 0.25f
                ? 0.5f
                : Mathf.Clamp01(score / totalWeight);
        }

        /// <summary>
        /// 获取颜色错误。
        /// </summary>
        private static float GetColorError(Color first, Color second)
        {
            return (Mathf.Abs(first.r - second.r) +
                    Mathf.Abs(first.g - second.g) +
                    Mathf.Abs(first.b - second.b)) / 3f;
        }

        /// <summary>
        /// 执行判断是否Tintable相关逻辑。
        /// </summary>
        private static bool IsTintable(VisualDescriptor candidate)
        {
            float chroma = 0f;
            float weight = 0f;
            for (int index = 0; index < candidate.Pixels.Length; index++)
            {
                Color color = candidate.Pixels[index];
                if (color.a < 0.25f)
                {
                    continue;
                }

                float value = Mathf.Max(color.r, color.g, color.b);
                if (value < 0.08f)
                {
                    continue;
                }

                chroma += (value -
                           Mathf.Min(color.r, color.g, color.b)) /
                          value * color.a;
                weight += color.a;
            }

            return weight >= MinimumVisibleSamples &&
                   chroma / weight <= 0.08f;
        }

        /// <summary>
        /// 执行判断是否AlphaVariation相关逻辑。
        /// </summary>
        private static bool HasAlphaVariation(
            VisualDescriptor candidate)
        {
            if (candidate == null) return false;
            float minimum = 1f;
            float maximum = 0f;
            for (int index = 0; index < candidate.Pixels.Length; index++)
            {
                float alpha = candidate.Pixels[index].a;
                minimum = Mathf.Min(minimum, alpha);
                maximum = Mathf.Max(maximum, alpha);
            }

            return maximum - minimum >= 0.12f;
        }

        /// <summary>
        /// 获取Channel。
        /// </summary>
        private static float GetChannel(Color color, int channel)
        {
            return channel == 0
                ? color.r
                : channel == 1
                    ? color.g
                    : color.b;
        }

        /// <summary>
        /// 获取Channel。
        /// </summary>
        private static float GetChannel(Vector3 color, int channel)
        {
            return channel == 0
                ? color.x
                : channel == 1
                    ? color.y
                    : color.z;
        }

        /// <summary>
        /// 设置Channel。
        /// </summary>
        private static void SetChannel(
            ref Vector3 value,
            int channel,
            float channelValue)
        {
            if (channel == 0)
            {
                value.x = channelValue;
            }
            else if (channel == 1)
            {
                value.y = channelValue;
            }
            else
            {
                value.z = channelValue;
            }
        }

        /// <summary>
        /// 尝试获取IntersectionStats，并返回是否成功。
        /// </summary>
        private static bool TryGetIntersectionStats(
            VisualDescriptor source,
            VisualDescriptor candidate,
            out Vector3 sourceMean,
            out Vector3 candidateMean,
            out float sourceEdge,
            out float candidateEdge)
        {
            sourceMean = Vector3.zero;
            candidateMean = Vector3.zero;
            sourceEdge = 0f;
            candidateEdge = 0f;
            int count = 0;
            int edgeCount = 0;
            for (int index = 0; index < source.Pixels.Length; index++)
            {
                if (!source.Mask[index] || !candidate.Mask[index])
                {
                    continue;
                }

                sourceMean += ToVector(source.Pixels[index]);
                candidateMean += ToVector(candidate.Pixels[index]);
                count++;
                int x = index % VisualSampleSize;
                int y = index / VisualSampleSize;
                if (x + 1 < VisualSampleSize &&
                    source.Mask[index + 1] &&
                    candidate.Mask[index + 1])
                {
                    sourceEdge += Mathf.Abs(
                        GetLuma(source.Pixels[index + 1]) -
                        GetLuma(source.Pixels[index]));
                    candidateEdge += Mathf.Abs(
                        GetLuma(candidate.Pixels[index + 1]) -
                        GetLuma(candidate.Pixels[index]));
                    edgeCount++;
                }

                if (y + 1 < VisualSampleSize &&
                    source.Mask[index + VisualSampleSize] &&
                    candidate.Mask[index + VisualSampleSize])
                {
                    sourceEdge += Mathf.Abs(
                        GetLuma(source.Pixels[index + VisualSampleSize]) -
                        GetLuma(source.Pixels[index]));
                    candidateEdge += Mathf.Abs(
                        GetLuma(candidate.Pixels[index + VisualSampleSize]) -
                        GetLuma(candidate.Pixels[index]));
                    edgeCount++;
                }
            }

            if (count < MinimumVisibleSamples)
            {
                return false;
            }

            sourceMean /= count;
            candidateMean /= count;
            if (edgeCount > 0)
            {
                sourceEdge /= edgeCount;
                candidateEdge /= edgeCount;
            }

            return true;
        }

        /// <summary>
        /// 创建引用Descriptor。
        /// </summary>
        private static VisualDescriptor CreateReferenceDescriptor(
            Texture2D reference,
            float designWidth,
            float designHeight,
            float x,
            float y,
            float width,
            float height,
            IReadOnlyCollection<Rect> occlusions,
            IReadOnlyCollection<Rect> backdropOcclusions = null)
        {
            if (reference == null ||
                designWidth <= 0f ||
                designHeight <= 0f ||
                width <= 1f ||
                height <= 1f)
            {
                return null;
            }

            var pixels = new Color[VisualSampleSize * VisualSampleSize];
            var mask = new bool[pixels.Length];
            int visibleSamples = 0;
            for (int sampleY = 0;
                 sampleY < VisualSampleSize;
                 sampleY++)
            {
                float normalizedY =
                    GetVisualSampleCoordinate(sampleY);
                float designY = y + height * (1f - normalizedY);
                float textureV =
                    1f - Mathf.Clamp01(designY / designHeight);
                for (int sampleX = 0;
                     sampleX < VisualSampleSize;
                     sampleX++)
                {
                    float normalizedX =
                        GetVisualSampleCoordinate(sampleX);
                    float designX = x + width * normalizedX;
                    float textureU =
                        Mathf.Clamp01(designX / designWidth);
                    int index = sampleY * VisualSampleSize + sampleX;
                    pixels[index] = reference.GetPixelBilinear(
                        textureU,
                        textureV);
                    mask[index] = !IsOccluded(
                        designX,
                        designY,
                        occlusions);
                    if (mask[index])
                    {
                        visibleSamples++;
                    }
                }
            }

            if (visibleSamples < MinimumVisibleSamples)
            {
                return null;
            }

            var descriptor = new VisualDescriptor(
                pixels,
                mask,
                width / height,
                width,
                height,
                false);
            descriptor.ReferenceBackdrop = SampleReferenceBackdrop(
                reference,
                designWidth,
                designHeight,
                x,
                y,
                width,
                height,
                backdropOcclusions);
            descriptor.HasReferenceBackdrop = true;
            descriptor.ReferenceBackdropPixels =
                SampleReferenceBackdropProfile(
                    reference,
                    designWidth,
                    designHeight,
                    x,
                    y,
                    width,
                    height,
                    backdropOcclusions,
                    descriptor.ReferenceBackdrop);
            descriptor.HasSpatialReferenceBackdrop =
                descriptor.ReferenceBackdropPixels != null;
            return descriptor;
        }

        /// <summary>
        /// 获取VisualSampleCoordinate。
        /// </summary>
        private static float GetVisualSampleCoordinate(int sampleIndex)
        {
            float linear =
                (sampleIndex + 0.5f) / VisualSampleSize;
            // A cosine distribution keeps center information while placing
            // several deterministic samples inside very narrow outer bands.
            // Uniform 24x24 sampling can completely miss a 4-10 px accent on
            // a wide score bar and then select a visually flatter resource.
            return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * linear);
        }

        /// <summary>
        /// 执行SampleReferenceBackdrop相关逻辑。
        /// </summary>
        private static Color SampleReferenceBackdrop(
            Texture2D reference,
            float designWidth,
            float designHeight,
            float x,
            float y,
            float width,
            float height,
            IReadOnlyCollection<Rect> occlusions)
        {
            var red = new List<float>();
            var green = new List<float>();
            var blue = new List<float>();
            float offset = Mathf.Clamp(
                Mathf.Min(width, height) * 0.08f,
                2f,
                6f);
            const int sampleCount = 12;
            for (int index = 0; index < sampleCount; index++)
            {
                float normalized = (index + 0.5f) / sampleCount;
                AddReferenceSample(
                    reference,
                    designWidth,
                    designHeight,
                    x - offset,
                    y + height * normalized,
                    occlusions,
                    red,
                    green,
                    blue);
                AddReferenceSample(
                    reference,
                    designWidth,
                    designHeight,
                    x + width + offset,
                    y + height * normalized,
                    occlusions,
                    red,
                    green,
                    blue);
                AddReferenceSample(
                    reference,
                    designWidth,
                    designHeight,
                    x + width * normalized,
                    y - offset,
                    occlusions,
                    red,
                    green,
                    blue);
                AddReferenceSample(
                    reference,
                    designWidth,
                    designHeight,
                    x + width * normalized,
                    y + height + offset,
                    occlusions,
                    red,
                    green,
                    blue);
            }

            if (red.Count == 0)
            {
                return Color.black;
            }

            red.Sort();
            green.Sort();
            blue.Sort();
            int middle = red.Count / 2;
            return new Color(
                red[middle],
                green[middle],
                blue[middle],
                1f);
        }

        /// <summary>
        /// 添加引用Sample。
        /// </summary>
        private static void AddReferenceSample(
            Texture2D reference,
            float designWidth,
            float designHeight,
            float designX,
            float designY,
            IReadOnlyCollection<Rect> occlusions,
            ICollection<float> red,
            ICollection<float> green,
            ICollection<float> blue)
        {
            if (!TrySampleReferencePixel(
                    reference,
                    designWidth,
                    designHeight,
                    designX,
                    designY,
                    occlusions,
                    out Color color))
            {
                return;
            }

            red.Add(color.r);
            green.Add(color.g);
            blue.Add(color.b);
        }

        /// <summary>
        /// 执行SampleReferenceBackdropProfile相关逻辑。
        /// </summary>
        private static Color[] SampleReferenceBackdropProfile(
            Texture2D reference,
            float designWidth,
            float designHeight,
            float x,
            float y,
            float width,
            float height,
            IReadOnlyCollection<Rect> occlusions,
            Color fallback)
        {
            if (reference == null || designWidth <= 0f ||
                designHeight <= 0f)
            {
                return null;
            }

            var result = new Color[VisualSampleSize * VisualSampleSize];
            float offset = Mathf.Clamp(
                Mathf.Min(width, height) * 0.08f,
                2f,
                6f);
            int resolved = 0;
            for (int sampleY = 0; sampleY < VisualSampleSize; sampleY++)
            {
                float normalizedY = GetVisualSampleCoordinate(sampleY);
                float designY = y + height * (1f - normalizedY);
                for (int sampleX = 0;
                     sampleX < VisualSampleSize;
                     sampleX++)
                {
                    float normalizedX = GetVisualSampleCoordinate(sampleX);
                    float designX = x + width * normalizedX;
                    bool hasTop = TrySampleReferencePixel(
                        reference,
                        designWidth,
                        designHeight,
                        designX,
                        y - offset,
                        occlusions,
                        out Color top);
                    bool hasBottom = TrySampleReferencePixel(
                        reference,
                        designWidth,
                        designHeight,
                        designX,
                        y + height + offset,
                        occlusions,
                        out Color bottom);
                    Color sample;
                    bool hasSample = hasTop || hasBottom;
                    if (hasTop && hasBottom)
                    {
                        sample = Color.Lerp(
                            top,
                            bottom,
                            1f - normalizedY);
                    }
                    else if (hasTop)
                    {
                        sample = top;
                    }
                    else if (hasBottom)
                    {
                        sample = bottom;
                    }
                    else
                    {
                        bool hasLeft = TrySampleReferencePixel(
                            reference,
                            designWidth,
                            designHeight,
                            x - offset,
                            designY,
                            occlusions,
                            out Color left);
                        bool hasRight = TrySampleReferencePixel(
                            reference,
                            designWidth,
                            designHeight,
                            x + width + offset,
                            designY,
                            occlusions,
                            out Color right);
                        hasSample = hasLeft || hasRight;
                        if (hasLeft && hasRight)
                        {
                            sample = Color.Lerp(
                                left,
                                right,
                                normalizedX);
                        }
                        else
                        {
                            sample = hasLeft ? left : right;
                        }
                    }

                    int index = sampleY * VisualSampleSize + sampleX;
                    if (!hasSample)
                    {
                        result[index] = fallback;
                        continue;
                    }

                    result[index] = sample;
                    result[index].a = 1f;
                    resolved++;
                }
            }

            return resolved >= MinimumVisibleSamples ? result : null;
        }

        /// <summary>
        /// 尝试Sample引用Pixel，并返回是否成功。
        /// </summary>
        private static bool TrySampleReferencePixel(
            Texture2D reference,
            float designWidth,
            float designHeight,
            float designX,
            float designY,
            IReadOnlyCollection<Rect> occlusions,
            out Color color)
        {
            color = Color.black;
            if (reference == null || designX < 0f || designY < 0f ||
                designX > designWidth || designY > designHeight ||
                IsOccluded(designX, designY, occlusions))
            {
                return false;
            }

            color = reference.GetPixelBilinear(
                Mathf.Clamp01(designX / designWidth),
                1f - Mathf.Clamp01(designY / designHeight));
            color.a = 1f;
            return true;
        }

        /// <summary>
        /// 执行判断是否Occluded相关逻辑。
        /// </summary>
        private static bool IsOccluded(
            float designX,
            float designY,
            IReadOnlyCollection<Rect> occlusions)
        {
            if (occlusions == null)
            {
                return false;
            }

            var point = new Vector2(designX, designY);
            foreach (Rect rect in occlusions)
            {
                if (rect.Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 执行比较Descriptors相关逻辑。
        /// </summary>
        private static float CompareDescriptors(
            VisualDescriptor source,
            VisualDescriptor candidate,
            float aspectSimilarity,
            bool preferBorder)
        {
            float totalWeight = 0f;
            Vector3 sourceMean = Vector3.zero;
            Vector3 candidateMean = Vector3.zero;
            float sourceLumaMean = 0f;
            float candidateLumaMean = 0f;
            for (int index = 0; index < source.Pixels.Length; index++)
            {
                if (!source.Mask[index] || !candidate.Mask[index])
                {
                    continue;
                }

                float weight = GetSampleWeight(index, preferBorder);
                Color sourceColor = source.Pixels[index];
                Color candidateColor = candidate.Pixels[index];
                sourceMean += ToVector(sourceColor) * weight;
                candidateMean += ToVector(candidateColor) * weight;
                sourceLumaMean += GetLuma(sourceColor) * weight;
                candidateLumaMean += GetLuma(candidateColor) * weight;
                totalWeight += weight;
            }

            if (totalWeight < VisualSampleSize)
            {
                return 0f;
            }

            sourceMean /= totalWeight;
            candidateMean /= totalWeight;
            sourceLumaMean /= totalWeight;
            candidateLumaMean /= totalWeight;

            float colorDistance =
                (Mathf.Abs(sourceMean.x - candidateMean.x) +
                 Mathf.Abs(sourceMean.y - candidateMean.y) +
                 Mathf.Abs(sourceMean.z - candidateMean.z)) / 3f;
            float colorSimilarity = 1f - Mathf.Clamp01(colorDistance);
            float sizeSimilarity = GetSizeSimilarity(source, candidate);

            float structureError = 0f;
            float structureWeight = 0f;
            float edgeError = 0f;
            float edgeWeight = 0f;
            for (int y = 0; y < VisualSampleSize; y++)
            {
                for (int x = 0; x < VisualSampleSize; x++)
                {
                    int index = y * VisualSampleSize + x;
                    if (!source.Mask[index] || !candidate.Mask[index])
                    {
                        continue;
                    }

                    float weight = GetSampleWeight(index, preferBorder);
                    float sourceLuma = GetLuma(source.Pixels[index]);
                    float candidateLuma = GetLuma(
                        candidate.Pixels[index]);
                    structureError += Mathf.Abs(
                        (sourceLuma - sourceLumaMean) -
                        (candidateLuma - candidateLumaMean)) * weight;
                    structureWeight += weight;

                    CompareEdge(
                        source,
                        candidate,
                        index,
                        x + 1 < VisualSampleSize ? index + 1 : -1,
                        weight,
                        ref edgeError,
                        ref edgeWeight);
                    CompareEdge(
                        source,
                        candidate,
                        index,
                        y + 1 < VisualSampleSize
                            ? index + VisualSampleSize
                            : -1,
                        weight,
                        ref edgeError,
                        ref edgeWeight);
                }
            }

            float structureSimilarity = 1f - Mathf.Clamp01(
                structureError /
                Mathf.Max(0.001f, structureWeight * 0.5f));
            float edgeSimilarity = edgeWeight <= 0f
                ? structureSimilarity
                : 1f - Mathf.Clamp01(
                    edgeError / (edgeWeight * 0.5f));
            return colorSimilarity * 0.23f +
                   structureSimilarity * 0.3f +
                   edgeSimilarity * 0.24f +
                   Mathf.Clamp01(aspectSimilarity) * 0.15f +
                   sizeSimilarity * 0.08f;
        }

        /// <summary>
        /// 获取Mean颜色Distance。
        /// </summary>
        private static float GetMeanColorDistance(
            VisualDescriptor source,
            VisualDescriptor candidate,
            bool preferBorder)
        {
            float totalWeight = 0f;
            Vector3 sourceMean = Vector3.zero;
            Vector3 candidateMean = Vector3.zero;
            for (int index = 0; index < source.Pixels.Length; index++)
            {
                if (!source.Mask[index] || !candidate.Mask[index])
                {
                    continue;
                }

                float weight = GetSampleWeight(index, preferBorder);
                sourceMean += ToVector(source.Pixels[index]) * weight;
                candidateMean +=
                    ToVector(candidate.Pixels[index]) * weight;
                totalWeight += weight;
            }

            if (totalWeight < VisualSampleSize)
            {
                return 1f;
            }

            sourceMean /= totalWeight;
            candidateMean /= totalWeight;
            return (Mathf.Abs(sourceMean.x - candidateMean.x) +
                    Mathf.Abs(sourceMean.y - candidateMean.y) +
                    Mathf.Abs(sourceMean.z - candidateMean.z)) / 3f;
        }

        /// <summary>
        /// 执行比较Edge相关逻辑。
        /// </summary>
        private static void CompareEdge(
            VisualDescriptor source,
            VisualDescriptor candidate,
            int from,
            int to,
            float weight,
            ref float error,
            ref float totalWeight)
        {
            if (to < 0 ||
                !source.Mask[from] ||
                !source.Mask[to] ||
                !candidate.Mask[from] ||
                !candidate.Mask[to])
            {
                return;
            }

            float sourceDelta =
                GetLuma(source.Pixels[to]) -
                GetLuma(source.Pixels[from]);
            float candidateDelta =
                GetLuma(candidate.Pixels[to]) -
                GetLuma(candidate.Pixels[from]);
            error += Mathf.Abs(sourceDelta - candidateDelta) * weight;
            totalWeight += weight;
        }

        /// <summary>
        /// 获取SampleWeight。
        /// </summary>
        private static float GetSampleWeight(
            int index,
            bool preferBorder)
        {
            if (!preferBorder)
            {
                return 1f;
            }

            int x = index % VisualSampleSize;
            int y = index / VisualSampleSize;
            int border = VisualSampleSize / 4;
            return x < border ||
                   y < border ||
                   x >= VisualSampleSize - border ||
                   y >= VisualSampleSize - border
                ? 1f
                : 0.35f;
        }

        /// <summary>
        /// 执行转换为向量相关逻辑。
        /// </summary>
        private static Vector3 ToVector(Color value)
        {
            return new Vector3(value.r, value.g, value.b);
        }

        /// <summary>
        /// 获取Luma。
        /// </summary>
        private static float GetLuma(Color value)
        {
            return value.r * 0.2126f +
                   value.g * 0.7152f +
                   value.b * 0.0722f;
        }

        /// <summary>
        /// 获取AspectSimilarity。
        /// </summary>
        private static float GetAspectSimilarity(
            float source,
            float candidate)
        {
            if (source <= 0f || candidate <= 0f)
            {
                return 0f;
            }

            return Mathf.Min(source, candidate) /
                   Mathf.Max(source, candidate);
        }

        /// <summary>
        /// 获取尺寸Similarity。
        /// </summary>
        private static float GetSizeSimilarity(
            VisualDescriptor source,
            VisualDescriptor candidate)
        {
            if (candidate.IsSliced)
            {
                return 1f;
            }

            if (source.Width <= 0f ||
                source.Height <= 0f ||
                candidate.Width <= 0f ||
                candidate.Height <= 0f)
            {
                return 0f;
            }

            float widthSimilarity =
                Mathf.Min(source.Width, candidate.Width) /
                Mathf.Max(source.Width, candidate.Width);
            float heightSimilarity =
                Mathf.Min(source.Height, candidate.Height) /
                Mathf.Max(source.Height, candidate.Height);
            return (widthSimilarity + heightSimilarity) * 0.5f;
        }

        /// <summary>
        /// 执行判断是否视觉节点相关逻辑。
        /// </summary>
        private static bool IsVisualNode(UIEffectNode node)
        {
            return string.Equals(
                       node.type,
                       "Image",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       node.type,
                       "Button",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       node.type,
                       "Toggle",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 执行判断是否Explicit资源资源相关逻辑。
        /// </summary>
        private static bool HasExplicitAssetResource(UIEffectNode node)
        {
            return IsAssetResourcePath(node.resource);
        }

        private static bool IsAssetResourcePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase));
        }

        private static List<VisualCandidate> DistinctVisualCandidates(IEnumerable<VisualCandidate> candidates)
        {
            var result = new List<VisualCandidate>();
            var groups = new Dictionary<string, List<VisualCandidate>>(StringComparer.Ordinal);
            foreach (VisualCandidate candidate in candidates)
            {
                string key = candidate.Entry.VisualContentKey;
                if (!groups.TryGetValue(key, out List<VisualCandidate> group))
                    groups[key] = group = new List<VisualCandidate>();
                if (group.Any(other =>
                        other.Descriptor.Pixels.SequenceEqual(candidate.Descriptor.Pixels) &&
                        other.Descriptor.Mask.SequenceEqual(candidate.Descriptor.Mask)))
                    continue;
                group.Add(candidate);
                result.Add(candidate);
            }
            return result;
        }

        /// <summary>
        /// 获取精灵图资源路径。
        /// </summary>
        private static string GetSpriteResourcePath(SpriteEntry entry)
        {
            string value = entry.AssetPath;
            if (entry.IsSubSprite || !string.Equals(
                    entry.Sprite.name,
                    Path.GetFileNameWithoutExtension(entry.AssetPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                value += "#" + entry.Sprite.name;
            }

            return value;
        }

        private sealed class VisualDescriptor
        {
            /// <summary>
            /// 创建VisualDescriptor实例。
            /// </summary>
            public VisualDescriptor(
                Color[] pixels,
                bool[] mask,
                float aspect,
                float width,
                float height,
                bool isSliced)
            {
                Pixels = pixels;
                Mask = mask;
                Aspect = aspect;
                Width = width;
                Height = height;
                IsSliced = isSliced;
            }

            /// <summary>
            /// 向调用方提供Pixels。
            /// </summary>
            public Color[] Pixels { get; }
            /// <summary>
            /// 向调用方提供Mask。
            /// </summary>
            public bool[] Mask { get; }
            /// <summary>
            /// 向调用方提供Aspect。
            /// </summary>
            public float Aspect { get; }
            /// <summary>
            /// 向调用方提供Width。
            /// </summary>
            public float Width { get; }
            /// <summary>
            /// 向调用方提供Height。
            /// </summary>
            public float Height { get; }
            /// <summary>
            /// 指示Sliced是否成立。
            /// </summary>
            public bool IsSliced { get; }
            /// <summary>
            /// 向调用方提供引用Backdrop。
            /// </summary>
            public Color ReferenceBackdrop { get; set; }
            /// <summary>
            /// 指示是否具有引用Backdrop。
            /// </summary>
            public bool HasReferenceBackdrop { get; set; }
            /// <summary>
            /// 向调用方提供引用BackdropPixels。
            /// </summary>
            public Color[] ReferenceBackdropPixels { get; set; }
            /// <summary>
            /// 指示是否具有Spatial引用Backdrop。
            /// </summary>
            public bool HasSpatialReferenceBackdrop { get; set; }
        }

        private sealed class VisualMatchEvidence
        {
            /// <summary>
            /// 创建VisualMatchEvidence实例。
            /// </summary>
            public VisualMatchEvidence(
                string resource,
                float score,
                float margin)
            {
                Resource = resource;
                Score = score;
                Margin = margin;
            }

            /// <summary>
            /// 向调用方提供资源。
            /// </summary>
            public string Resource { get; }
            /// <summary>
            /// 向调用方提供Score。
            /// </summary>
            public float Score { get; }
            /// <summary>
            /// 向调用方提供Margin。
            /// </summary>
            public float Margin { get; }
        }

        private sealed class RuntimeTemplateEvidence
        {
            /// <summary>
            /// 创建运行时TemplateEvidence实例。
            /// </summary>
            public RuntimeTemplateEvidence(
                UIEffectNode node,
                int siblingIndex)
            {
                Node = node;
                SiblingIndex = siblingIndex;
            }

            /// <summary>
            /// 向调用方提供Node。
            /// </summary>
            public UIEffectNode Node { get; }
            /// <summary>
            /// 向调用方提供SiblingIndex。
            /// </summary>
            public int SiblingIndex { get; }
            /// <summary>
            /// 当前Visual的数量。
            /// </summary>
            public int VisualCount { get; set; }
            /// <summary>
            /// 当前ResolvedVisual的数量。
            /// </summary>
            public int ResolvedVisualCount { get; set; }
            /// <summary>
            /// 当前FailedVisual的数量。
            /// </summary>
            public int FailedVisualCount { get; set; }
            /// <summary>
            /// 当前ScoredVisual的数量。
            /// </summary>
            public int ScoredVisualCount { get; set; }
            /// <summary>
            /// 向调用方提供VariantAffinity。
            /// </summary>
            public int VariantAffinity { get; set; }
            /// <summary>
            /// 向调用方提供Score总数。
            /// </summary>
            public float ScoreTotal { get; set; }
            /// <summary>
            /// 向调用方提供Margin总数。
            /// </summary>
            public float MarginTotal { get; set; }
            /// <summary>
            /// 向调用方提供AverageScore。
            /// </summary>
            public float AverageScore => ScoredVisualCount == 0
                ? 0f
                : ScoreTotal / ScoredVisualCount;
            /// <summary>
            /// 向调用方提供AverageMargin。
            /// </summary>
            public float AverageMargin => ScoredVisualCount == 0
                ? 0f
                : MarginTotal / ScoredVisualCount;

            /// <summary>
            /// 执行转换为Log字符串相关逻辑。
            /// </summary>
            public string ToLogString()
            {
                return $"{Node.name}[变体证据 {VariantAffinity}，" +
                       $"资源 {ResolvedVisualCount}/{VisualCount}，" +
                       $"失败 {FailedVisualCount}，" +
                       $"均分 {AverageScore:F3}/" +
                       $"{AverageMargin:F3}]";
            }
        }

        private sealed class RuntimeTemplateNodeLocation
        {
            /// <summary>
            /// 创建运行时TemplateNodeLocation实例。
            /// </summary>
            public RuntimeTemplateNodeLocation(
                UIEffectNode node,
                IReadOnlyList<UIEffectNode> siblings,
                float parentX,
                float parentY,
                VisualParentContext visualParent,
                string parentSemanticHint,
                string rootVariant)
            {
                Node = node;
                Siblings = siblings;
                ParentX = parentX;
                ParentY = parentY;
                VisualParent = visualParent;
                ParentSemanticHint = parentSemanticHint ?? string.Empty;
                RootVariant = rootVariant ?? string.Empty;
            }

            /// <summary>
            /// 向调用方提供Node。
            /// </summary>
            public UIEffectNode Node { get; }
            /// <summary>
            /// 向调用方提供Siblings。
            /// </summary>
            public IReadOnlyList<UIEffectNode> Siblings { get; }
            /// <summary>
            /// 向调用方提供ParentX。
            /// </summary>
            public float ParentX { get; }
            /// <summary>
            /// 向调用方提供ParentY。
            /// </summary>
            public float ParentY { get; }
            /// <summary>
            /// 向调用方提供AbsoluteX。
            /// </summary>
            public float AbsoluteX => ParentX + Node.x;
            /// <summary>
            /// 向调用方提供AbsoluteY。
            /// </summary>
            public float AbsoluteY => ParentY + Node.y;
            /// <summary>
            /// 向调用方提供VisualParent。
            /// </summary>
            public VisualParentContext VisualParent { get; }
            /// <summary>
            /// 向调用方提供ParentSemanticHint。
            /// </summary>
            public string ParentSemanticHint { get; }
            /// <summary>
            /// 向调用方提供RootVariant。
            /// </summary>
            public string RootVariant { get; }
        }

        private sealed class VisualCandidate
        {
            /// <summary>
            /// 创建VisualCandidate实例。
            /// </summary>
            public VisualCandidate(
                SpriteEntry entry,
                VisualDescriptor descriptor,
                float aspectSimilarity,
                float quickScore,
                float semanticBonus,
                float slicedScalePenalty)
            {
                Entry = entry;
                Descriptor = descriptor;
                AspectSimilarity = aspectSimilarity;
                QuickScore = quickScore;
                SemanticBonus = semanticBonus;
                SlicedScalePenalty = slicedScalePenalty;
            }

            /// <summary>
            /// 向调用方提供Entry。
            /// </summary>
            public SpriteEntry Entry { get; }
            /// <summary>
            /// 向调用方提供Descriptor。
            /// </summary>
            public VisualDescriptor Descriptor { get; }
            /// <summary>
            /// 向调用方提供AspectSimilarity。
            /// </summary>
            public float AspectSimilarity { get; }
            /// <summary>
            /// 向调用方提供QuickScore。
            /// </summary>
            public float QuickScore { get; set; }
            /// <summary>
            /// 向调用方提供SemanticBonus。
            /// </summary>
            public float SemanticBonus { get; }
            /// <summary>
            /// 向调用方提供SlicedScalePenalty。
            /// </summary>
            public float SlicedScalePenalty { get; }
            /// <summary>
            /// 向调用方提供AuthoredScore。
            /// </summary>
            public float AuthoredScore { get; set; }
            /// <summary>
            /// 向调用方提供AuthoredColorDistance。
            /// </summary>
            public float AuthoredColorDistance { get; set; }
            /// <summary>
            /// 向调用方提供CompositeScore。
            /// </summary>
            public float CompositeScore { get; set; }
            /// <summary>
            /// 向调用方提供Score。
            /// </summary>
            public float Score { get; set; }
            /// <summary>
            /// 向调用方提供MatchedColor。
            /// </summary>
            public Color MatchedColor { get; set; }
            /// <summary>
            /// 向调用方提供UsesMaterialTint。
            /// </summary>
            public bool UsesMaterialTint { get; set; }
        }

        private sealed class RepeatedSurfaceCandidateScore
        {
            private float scoreTotal;

            /// <summary>
            /// 创建RepeatedSurfaceCandidateScore实例。
            /// </summary>
            public RepeatedSurfaceCandidateScore(SpriteEntry entry)
            {
                Entry = entry;
            }

            /// <summary>
            /// 向调用方提供Entry。
            /// </summary>
            public SpriteEntry Entry { get; }
            public Dictionary<UIEffectNode, float> Scores { get; } =
                new Dictionary<UIEffectNode, float>();
            public Dictionary<UIEffectNode, Color> Colors { get; } =
                new Dictionary<UIEffectNode, Color>();
            /// <summary>
            /// 当前Sample的数量。
            /// </summary>
            public int SampleCount => Scores.Count;
            /// <summary>
            /// 向调用方提供AverageScore。
            /// </summary>
            public float AverageScore => SampleCount == 0
                ? 0f
                : scoreTotal / SampleCount;

            /// <summary>
            /// 执行添加相关逻辑。
            /// </summary>
            public void Add(
                UIEffectNode node,
                float score,
                Color color)
            {
                Scores[node] = score;
                Colors[node] = color;
                scoreTotal += score;
            }
        }

        private sealed class VisualParentContext
        {
            /// <summary>
            /// 创建VisualParent上下文实例。
            /// </summary>
            public VisualParentContext(
                UIEffectNode node,
                SpriteEntry entry,
                float absoluteX,
                float absoluteY,
                VisualDescriptor resolvedDescriptor = null)
            {
                Node = node;
                Entry = entry;
                AbsoluteX = absoluteX;
                AbsoluteY = absoluteY;
                ResolvedDescriptor = resolvedDescriptor;
            }

            /// <summary>
            /// 向调用方提供Node。
            /// </summary>
            public UIEffectNode Node { get; }
            /// <summary>
            /// 向调用方提供Entry。
            /// </summary>
            public SpriteEntry Entry { get; }
            /// <summary>
            /// 向调用方提供AbsoluteX。
            /// </summary>
            public float AbsoluteX { get; }
            /// <summary>
            /// 向调用方提供AbsoluteY。
            /// </summary>
            public float AbsoluteY { get; }
            /// <summary>
            /// 向调用方提供ResolvedDescriptor。
            /// </summary>
            public VisualDescriptor ResolvedDescriptor { get; }
        }

        private sealed class SpritePreviewRenderer : IDisposable
        {
            private readonly int sampleSize;
            private readonly int renderSize;
            private readonly GameObject previewObject;
            private readonly GameObject cameraObject;
            private readonly SpriteRenderer spriteRenderer;
            private readonly Camera previewCamera;
            private readonly RenderTexture renderTexture;
            private readonly Texture2D readbackTexture;
            private readonly UnityEngine.SceneManagement.Scene previewScene;

            /// <summary>
            /// 创建SpritePreviewRenderer实例。
            /// </summary>
            public SpritePreviewRenderer(int sampleSize)
            {
                this.sampleSize = sampleSize;
                renderSize = Mathf.Max(256, sampleSize);
                previewObject = new GameObject(
                    "UIEffectSpritePreview",
                    typeof(SpriteRenderer));
                cameraObject = new GameObject(
                    "UIEffectSpritePreviewCamera",
                    typeof(Camera));
                previewScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(previewObject, previewScene);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                previewObject.hideFlags = HideFlags.HideAndDontSave;
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                previewObject.layer = 31;
                cameraObject.layer = 31;
                spriteRenderer = previewObject.GetComponent<SpriteRenderer>();
                spriteRenderer.color = Color.white;
                spriteRenderer.sortingOrder = 0;
                previewCamera = cameraObject.GetComponent<Camera>();
                previewCamera.scene = previewScene;
                previewCamera.enabled = false;
                previewCamera.orthographic = true;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = Color.clear;
                previewCamera.cullingMask = 1 << 31;
                previewCamera.nearClipPlane = 0.01f;
                previewCamera.farClipPlane = 10f;
                previewCamera.transform.position = new Vector3(0f, 0f, -1f);
                renderTexture = new RenderTexture(
                    renderSize,
                    renderSize,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                };
                readbackTexture = new Texture2D(
                    renderSize,
                    renderSize,
                    TextureFormat.RGBA32,
                    false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                previewCamera.targetTexture = renderTexture;
            }

            /// <summary>
            /// 执行渲染相关逻辑。
            /// </summary>
            public VisualDescriptor Render(
                Sprite sprite,
                float targetWidth,
                float targetHeight,
                bool sliced,
                float authoredWidth,
                float authoredHeight)
            {
                return RenderRegionInternal(
                    sprite,
                    targetWidth,
                    targetHeight,
                    new Rect(0f, 0f, targetWidth, targetHeight),
                    sliced,
                    authoredHeight <= 0f
                        ? 0f
                        : authoredWidth / authoredHeight,
                    authoredWidth,
                    authoredHeight);
            }

            /// <summary>
            /// 执行渲染Region相关逻辑。
            /// </summary>
            public VisualDescriptor RenderRegion(
                Sprite sprite,
                float targetWidth,
                float targetHeight,
                Rect targetRegion,
                bool sliced)
            {
                return RenderRegionInternal(
                    sprite,
                    targetWidth,
                    targetHeight,
                    targetRegion,
                    sliced,
                    targetRegion.height <= 0f
                        ? 0f
                        : targetRegion.width / targetRegion.height,
                    targetRegion.width,
                    targetRegion.height);
            }

            /// <summary>
            /// 执行渲染Region内部相关逻辑。
            /// </summary>
            private VisualDescriptor RenderRegionInternal(
                Sprite sprite,
                float targetWidth,
                float targetHeight,
                Rect targetRegion,
                bool sliced,
                float descriptorAspect,
                float descriptorWidth,
                float descriptorHeight)
            {
                if (sprite == null ||
                    sprite.bounds.size.x <= 0f ||
                    sprite.bounds.size.y <= 0f ||
                    targetWidth <= 0f ||
                    targetHeight <= 0f ||
                    targetRegion.width <= 0f ||
                    targetRegion.height <= 0f)
                {
                    return null;
                }

                spriteRenderer.sprite = sprite;
                // The screenshot rectangle includes the Sprite's transparent
                // padding. Tight-mesh bounds must not silently crop that padding.
                float ppu = Mathf.Max(.0001f, sprite.pixelsPerUnit);
                previewObject.transform.position = new Vector3(
                    (sprite.pivot.x - sprite.rect.width * .5f) / ppu,
                    (sprite.pivot.y - sprite.rect.height * .5f) / ppu, 0f);
                float aspect = sprite.rect.width / sprite.rect.height;
                previewCamera.aspect = aspect;
                previewCamera.orthographicSize =
                    sprite.rect.height / (2f * ppu);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    if (!TryGetNativePixels(sprite, renderSize, out Color32[] renderedPixels))
                    {
                        previewCamera.Render();
                        RenderTexture.active = renderTexture;
                        readbackTexture.ReadPixels(new Rect(0f, 0f, renderSize, renderSize), 0, 0, false);
                        readbackTexture.Apply(false, false);
                        renderedPixels = readbackTexture.GetPixels32();
                        CacheNativePixels(sprite, renderSize, renderedPixels);
                    }
                    PrepareNativeColorLookup();
                    var pixels =
                        new Color[sampleSize * sampleSize];
                    var mask = new bool[pixels.Length];
                    int visiblePixels = 0;
                    for (int sampleY = 0;
                         sampleY < sampleSize;
                         sampleY++)
                    {
                        float normalizedY =
                            UIEffectResourceResolver
                                .GetVisualSampleCoordinate(sampleY);
                        float targetY = targetHeight -
                                        targetRegion.y -
                                        targetRegion.height +
                                        targetRegion.height * normalizedY;
                        float sourceY = GetSourceCoordinate(
                            targetY,
                            targetHeight,
                            sprite.rect.height,
                            sprite.border.y,
                            sprite.border.w,
                            sliced);
                        for (int sampleX = 0;
                             sampleX < sampleSize;
                             sampleX++)
                        {
                            float normalizedX =
                                UIEffectResourceResolver
                                    .GetVisualSampleCoordinate(sampleX);
                            float targetX = targetRegion.x +
                                            targetRegion.width * normalizedX;
                            float sourceX = GetSourceCoordinate(
                                targetX,
                                targetWidth,
                                sprite.rect.width,
                                sprite.border.x,
                                sprite.border.z,
                                sliced);
                            int index = sampleY * sampleSize + sampleX;
                            pixels[index] = UnpremultiplyRenderedColor(
                                SampleRenderedSprite(
                                    renderedPixels,
                                    sourceX / sprite.rect.width,
                                    sourceY / sprite.rect.height));
                            mask[index] = pixels[index].a >= 0.05f;
                            if (mask[index])
                            {
                                visiblePixels++;
                            }
                        }
                    }

                    return visiblePixels < sampleSize
                        ? null
                        : new VisualDescriptor(
                            pixels,
                            mask,
                            descriptorAspect,
                            descriptorWidth,
                            descriptorHeight,
                            sliced);
                }
                finally
                {
                    RenderTexture.active = previous;
                    spriteRenderer.sprite = null;
                }
            }

            /// <summary>
            /// 获取源数据Coordinate。
            /// </summary>
            private float GetSourceCoordinate(
                float targetCoordinate,
                float targetSize,
                float sourceSize,
                float beforeBorder,
                float afterBorder,
                bool sliced)
            {
                if (!sliced || targetSize <= 0f)
                {
                    return Mathf.Clamp01(targetCoordinate / targetSize) *
                           sourceSize;
                }

                // Image.pixelsPerUnit = Sprite PPU / Canvas reference PPU.
                // Compare in imported Sprite pixels, just as UGUI does.
                float unitsToPixels = spriteRenderer.sprite.pixelsPerUnit / 100f;
                targetCoordinate *= unitsToPixels;
                targetSize *= unitsToPixels;

                targetCoordinate = Mathf.Clamp(
                    targetCoordinate,
                    0f,
                    targetSize);
                float totalBorder = beforeBorder + afterBorder;
                if (totalBorder > 0f && targetSize < totalBorder)
                {
                    float borderScale = targetSize / totalBorder;
                    float targetBefore = beforeBorder * borderScale;
                    if (targetCoordinate <= targetBefore)
                    {
                        return targetBefore <= 0f
                            ? 0f
                            : targetCoordinate / targetBefore *
                              beforeBorder;
                    }

                    float targetAfter = afterBorder * borderScale;
                    float distanceFromEnd = targetSize - targetCoordinate;
                    return sourceSize -
                           (targetAfter <= 0f
                               ? 0f
                               : distanceFromEnd / targetAfter *
                                 afterBorder);
                }

                if (targetCoordinate < beforeBorder)
                {
                    return targetCoordinate;
                }

                if (targetCoordinate > targetSize - afterBorder)
                {
                    return sourceSize -
                           (targetSize - targetCoordinate);
                }

                float targetCenter = Mathf.Max(
                    0.0001f,
                    targetSize - beforeBorder - afterBorder);
                float sourceCenter = Mathf.Max(
                    0f,
                    sourceSize - beforeBorder - afterBorder);
                return beforeBorder +
                       (targetCoordinate - beforeBorder) /
                       targetCenter * sourceCenter;
            }

            /// <summary>
            /// 执行SampleRendered精灵图相关逻辑。
            /// </summary>
            private Color SampleRenderedSprite(
                Color32[] pixels,
                float u,
                float v)
            {
                float x = Mathf.Clamp01(u) * (renderSize - 1);
                float y = Mathf.Clamp01(v) * (renderSize - 1);
                int x0 = Mathf.FloorToInt(x);
                int y0 = Mathf.FloorToInt(y);
                int x1 = Mathf.Min(x0 + 1, renderSize - 1);
                int y1 = Mathf.Min(y0 + 1, renderSize - 1);
                float tx = x - x0;
                float ty = y - y0;
                Color bottom = Color.Lerp(
                    DecodeNativeColor(pixels[y0 * renderSize + x0]),
                    DecodeNativeColor(pixels[y0 * renderSize + x1]),
                    tx);
                Color top = Color.Lerp(
                    DecodeNativeColor(pixels[y1 * renderSize + x0]),
                    DecodeNativeColor(pixels[y1 * renderSize + x1]),
                    tx);
                return Color.Lerp(bottom, top, ty);
            }

            /// <summary>
            /// 执行UnpremultiplyRendered颜色相关逻辑。
            /// </summary>
            private static Color UnpremultiplyRenderedColor(Color color)
            {
                if (color.a <= 0.001f)
                {
                    return Color.clear;
                }

                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    color.r = Mathf.LinearToGammaSpace(
                        Mathf.Clamp01(
                            Mathf.GammaToLinearSpace(color.r) /
                            color.a));
                    color.g = Mathf.LinearToGammaSpace(
                        Mathf.Clamp01(
                            Mathf.GammaToLinearSpace(color.g) /
                            color.a));
                    color.b = Mathf.LinearToGammaSpace(
                        Mathf.Clamp01(
                            Mathf.GammaToLinearSpace(color.b) /
                            color.a));
                }
                else
                {
                    color.r = Mathf.Clamp01(color.r / color.a);
                    color.g = Mathf.Clamp01(color.g / color.a);
                    color.b = Mathf.Clamp01(color.b / color.a);
                }

                return color;
            }

            /// <summary>
            /// 释放当前实例持有的资源。
            /// </summary>
            public void Dispose()
            {
                previewCamera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(readbackTexture);
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(previewObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private sealed class SpriteEntry
        {
            /// <summary>
            /// 创建SpriteEntry实例。
            /// </summary>
            public SpriteEntry(
                string assetPath,
                Sprite sprite,
                float sourceScaleX,
                float sourceScaleY,
                bool isSubSprite = false)
            {
                AssetPath = assetPath;
                Sprite = sprite;
                NameKey = NormalizeKey(sprite.name);
                FileKey = NormalizeKey(
                    Path.GetFileNameWithoutExtension(assetPath));
                PathKey = NormalizeKey(assetPath + " " + sprite.name);
                AuthoredWidth = sprite.rect.width *
                                Mathf.Max(0.0001f, sourceScaleX);
                AuthoredHeight = sprite.rect.height *
                                  Mathf.Max(0.0001f, sourceScaleY);
                BorderLeft = sprite.border.x *
                             Mathf.Max(0.0001f, sourceScaleX);
                BorderBottom = sprite.border.y *
                               Mathf.Max(0.0001f, sourceScaleY);
                BorderRight = sprite.border.z *
                              Mathf.Max(0.0001f, sourceScaleX);
                BorderTop = sprite.border.w *
                            Mathf.Max(0.0001f, sourceScaleY);
                Aspect = AuthoredHeight <= 0f
                    ? 0f
                    : AuthoredWidth / AuthoredHeight;
                HasBorder = sprite.border.sqrMagnitude > 0f;
                IsSubSprite = isSubSprite;
                Texture2D texture = sprite.texture;
                Hash128 contentHash = texture.imageContentsHash;
                // A missing hash cannot establish equivalence between separate textures.
                string textureIdentity = contentHash.isValid
                    ? contentHash.ToString() : "instance:" + texture.GetInstanceID();
                Texture2D alpha = sprite.associatedAlphaSplitTexture;
                string alphaIdentity = alpha == null ? string.Empty :
                    alpha.imageContentsHash.isValid ? alpha.imageContentsHash.ToString() :
                    "instance:" + alpha.GetInstanceID();
                VisualContentKey = FormattableString.Invariant(
                    $"{textureIdentity}|{alphaIdentity}|{texture.width}|{texture.height}|{texture.format}|{texture.filterMode}|") +
                    FormattableString.Invariant($"{sprite.rect.x:R}|{sprite.rect.y:R}|{sprite.rect.width:R}|{sprite.rect.height:R}|") +
                    FormattableString.Invariant($"{AuthoredWidth:R}|{AuthoredHeight:R}|{sprite.pixelsPerUnit:R}|") +
                    FormattableString.Invariant($"{sprite.border.x:R}|{sprite.border.y:R}|{sprite.border.z:R}|{sprite.border.w:R}");
                DependencyHash =
                    AssetDatabase.GetAssetDependencyHash(assetPath)
                        .ToString();
            }

            /// <summary>
            /// 向调用方提供资源路径。
            /// </summary>
            public string AssetPath { get; }
            /// <summary>
            /// 向调用方提供Sprite。
            /// </summary>
            public Sprite Sprite { get; }
            /// <summary>
            /// 向调用方提供名称Key。
            /// </summary>
            public string NameKey { get; }
            /// <summary>
            /// 向调用方提供文件Key。
            /// </summary>
            public string FileKey { get; }
            /// <summary>
            /// 向调用方提供路径Key。
            /// </summary>
            public string PathKey { get; }
            /// <summary>
            /// 向调用方提供AuthoredWidth。
            /// </summary>
            public float AuthoredWidth { get; }
            /// <summary>
            /// 向调用方提供AuthoredHeight。
            /// </summary>
            public float AuthoredHeight { get; }
            /// <summary>
            /// 向调用方提供BorderLeft。
            /// </summary>
            public float BorderLeft { get; }
            /// <summary>
            /// 向调用方提供BorderBottom。
            /// </summary>
            public float BorderBottom { get; }
            /// <summary>
            /// 向调用方提供BorderRight。
            /// </summary>
            public float BorderRight { get; }
            /// <summary>
            /// 向调用方提供BorderTop。
            /// </summary>
            public float BorderTop { get; }
            /// <summary>
            /// 向调用方提供Aspect。
            /// </summary>
            public float Aspect { get; }
            /// <summary>
            /// 指示是否具有Border。
            /// </summary>
            public bool HasBorder { get; }
            public bool IsSubSprite { get; }
            public string VisualContentKey { get; }
            /// <summary>
            /// 向调用方提供Dependency哈希。
            /// </summary>
            public string DependencyHash { get; }
        }
    }

    internal sealed class UIEffectResourceCachePostprocessor :
        AssetPostprocessor
    {
        private static readonly HashSet<string> SpriteSourceExtensions =
            new HashSet<string>(
                new[]
                {
                    ".png",
                    ".jpg",
                    ".jpeg",
                    ".psd",
                    ".tga",
                    ".tif",
                    ".tiff",
                    ".bmp",
                    ".exr",
                },
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 响应Postprocess全部资源事件。
        /// </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ContainsSpriteSource(importedAssets) ||
                ContainsSpriteSource(deletedAssets) ||
                ContainsSpriteSource(movedAssets) ||
                ContainsSpriteSource(movedFromAssetPaths))
            {
                UIEffectResourceResolver.ClearCaches();
            }
        }

        /// <summary>
        /// 执行判断是否包含精灵图源数据相关逻辑。
        /// </summary>
        private static bool ContainsSpriteSource(
            IEnumerable<string> assetPaths)
        {
            return (assetPaths ?? Array.Empty<string>()).Any(path =>
                SpriteSourceExtensions.Contains(
                    Path.GetExtension(path ?? string.Empty)));
        }
    }
}
#endif
