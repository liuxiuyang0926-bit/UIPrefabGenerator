#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    internal sealed class UIEffectFigmaCropAsset
    {
        /// <summary>
        /// 向调用方提供Node路径。
        /// </summary>
        public string NodePath { get; set; }
        /// <summary>
        /// 向调用方提供运行时资源路径。
        /// </summary>
        public string RuntimeAssetPath { get; set; }
        /// <summary>
        /// 向调用方提供Png字节数。
        /// </summary>
        public byte[] PngBytes { get; set; }
    }

    /// <summary>
    /// Handles deterministic work after the connected Figma MCP has returned
    /// a compact UISchema and a rendered Frame screenshot. No Figma
    /// credential is read or stored by Unity.
    /// </summary>
    internal static class UIEffectFigmaImporter
    {
        public static string RuntimeAssetRoot
        {
            get
            {
                UIEffectProjectDefaults defaults =
                    UIEffectProjectAdapterRegistry.Active.CreateDefaults() ??
                    new UIEffectProjectDefaults();
                return defaults.figmaSpriteFolder;
            }
        }

        /// <summary>
        /// 公开的Crop资源Marker数据。
        /// </summary>
        public const string CropResourceMarker = "figma://crop";

        /// <summary>
        /// 公开的引用Crop资源Marker数据。
        /// </summary>
        public const string ReferenceCropResourceMarker =
            "reference://crop";

        /// <summary>
        /// 尝试解析Node地址，并返回是否成功。
        /// </summary>
        public static bool TryParseNodeUrl(
            string url,
            out string fileKey,
            out string nodeId,
            out string error)
        {
            fileKey = string.Empty;
            nodeId = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri))
            {
                error = "请输入带 node-id 的 Figma Frame/Component 链接。";
                return false;
            }

            bool isFigmaHost = string.Equals(
                                   uri.Host,
                                   "figma.com",
                                   StringComparison.OrdinalIgnoreCase) ||
                               uri.Host.EndsWith(
                                   ".figma.com",
                                   StringComparison.OrdinalIgnoreCase);
            if (!isFigmaHost)
            {
                error = "链接不是有效的 figma.com 地址。";
                return false;
            }

            string[] segments = uri.AbsolutePath.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || !IsSupportedFileType(segments[0]))
            {
                error =
                    "无法从链接提取 Figma file key。请复制 Design 节点链接。";
                return false;
            }

            string keySegment = segments.Length >= 4 &&
                                string.Equals(
                                    segments[2],
                                    "branch",
                                    StringComparison.OrdinalIgnoreCase)
                ? segments[3]
                : segments[1];
            fileKey = Uri.UnescapeDataString(keySegment);

            string query = uri.Query.TrimStart('?');
            foreach (string pair in query.Split('&'))
            {
                int separator = pair.IndexOf('=');
                string key = separator >= 0
                    ? pair.Substring(0, separator)
                    : pair;
                if (!string.Equals(
                        key,
                        "node-id",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = separator >= 0
                    ? pair.Substring(separator + 1)
                    : string.Empty;
                nodeId = Uri.UnescapeDataString(value.Replace("+", " "))
                    .Replace('-', ':');
                break;
            }

            if (fileKey.Length == 0 || nodeId.Length == 0)
            {
                error =
                    "链接缺少 node-id。请在 Figma 中复制目标 Frame/Component 的链接。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 解析面板Id。
        /// </summary>
        public static string ResolvePanelId(
            string requestedPanelId,
            string frameName)
        {
            string value = requestedPanelId?.Trim() ?? string.Empty;
            if (value.Length == 0 ||
                string.Equals(
                    value,
                    "UIExample",
                    StringComparison.OrdinalIgnoreCase))
            {
                value = frameName;
            }

            return UIEffectEditorUtility.SanitizeTypeName(value);
        }

        /// <summary>
        /// 获取引用资源路径。
        /// </summary>
        public static string GetReferenceAssetPath(string panelId)
        {
            UIEffectProjectDefaults defaults =
                UIEffectProjectAdapterRegistry.Active.CreateDefaults() ??
                new UIEffectProjectDefaults();
            string folder = defaults.referenceFolder.TrimEnd('/');
            return folder + "/" +
                   GetSafeAssetSegment(panelId) + "_Figma.png";
        }

        /// <summary>
        /// 获取最大值Depth。
        /// </summary>
        public static int GetMaxDepth(UIEffectSchema schema)
        {
            if (schema == null)
            {
                return 0;
            }

            int maximum = 1;
            if (schema.children == null)
            {
                return maximum;
            }

            foreach (UIEffectNode child in schema.children)
            {
                maximum = Mathf.Max(maximum, GetNodeDepth(child, 2));
            }

            return maximum;
        }

        /// <summary>
        /// 确保MinimumVisualNode。
        /// </summary>
        public static bool EnsureMinimumVisualNode(UIEffectSchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            schema.children ??= new List<UIEffectNode>();
            if (schema.children.Count > 0)
            {
                return false;
            }

            schema.children.Add(new UIEffectNode
            {
                name = "FrameVisual",
                type = "Image",
                width = Mathf.Max(1f, schema.designWidth),
                height = Mathf.Max(1f, schema.designHeight),
                resource = CropResourceMarker,
            });
            return true;
        }

        /// <summary>
        /// 执行标记ImplicitLeafCrop节点相关逻辑。
        /// </summary>
        public static int MarkImplicitLeafCropNodes(UIEffectSchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            return MarkImplicitLeafCropNodes(
                schema.children ?? new List<UIEffectNode>());
        }

        /// <summary>
        /// 执行标记ImplicitLeafCrop节点相关逻辑。
        /// </summary>
        private static int MarkImplicitLeafCropNodes(
            IReadOnlyList<UIEffectNode> siblings)
        {
            int markedCount = 0;
            for (int index = 0; index < siblings.Count; index++)
            {
                UIEffectNode node = siblings[index];
                if (node == null)
                {
                    continue;
                }

                if (CanInferLeafCrop(node, siblings, index))
                {
                    node.resource = ReferenceCropResourceMarker;
                    markedCount++;
                }

                markedCount += MarkImplicitLeafCropNodes(
                    node.children ?? new List<UIEffectNode>());
            }

            return markedCount;
        }

        /// <summary>
        /// 执行判断能否推断LeafCrop相关逻辑。
        /// </summary>
        private static bool CanInferLeafCrop(
            UIEffectNode node,
            IReadOnlyList<UIEffectNode> siblings,
            int nodeIndex)
        {
            if (!string.Equals(
                    node.type,
                    "Image",
                    StringComparison.OrdinalIgnoreCase) ||
                (node.children != null && node.children.Count > 0) ||
                !string.IsNullOrWhiteSpace(node.resource) ||
                !string.IsNullOrWhiteSpace(node.color) ||
                node.intentionalColor ||
                (node.resourceCandidates != null &&
                 node.resourceCandidates.Count > 0))
            {
                return false;
            }

            for (int index = 0; index < siblings.Count; index++)
            {
                if (index == nodeIndex || siblings[index] == null)
                {
                    continue;
                }

                if (RectanglesOverlap(node, siblings[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 执行RectanglesOverlap相关逻辑。
        /// </summary>
        private static bool RectanglesOverlap(
            UIEffectNode first,
            UIEffectNode second)
        {
            return first.x < second.x + second.width &&
                   first.x + first.width > second.x &&
                   first.y < second.y + second.height &&
                   first.y + first.height > second.y;
        }

        /// <summary>
        /// 获取NodeDepth。
        /// </summary>
        private static int GetNodeDepth(UIEffectNode node, int depth)
        {
            if (node == null || node.children == null ||
                node.children.Count == 0)
            {
                return depth;
            }

            int maximum = depth;
            foreach (UIEffectNode child in node.children)
            {
                maximum = Mathf.Max(maximum, GetNodeDepth(child, depth + 1));
            }

            return maximum;
        }

        public static IReadOnlyList<UIEffectFigmaCropAsset>
            CreateCropAssets(
                UIEffectSchema schema,
                Texture2D screenshot,
                string panelId,
                string runtimeAssetRoot = null)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (screenshot == null ||
                screenshot.width <= 0 ||
                screenshot.height <= 0)
            {
                throw new InvalidOperationException(
                    "Figma MCP 没有返回有效的 Frame 截图。");
            }

            UIEffectSchemaUtility.Validate(schema);
            if (string.IsNullOrWhiteSpace(runtimeAssetRoot))
            {
                runtimeAssetRoot = RuntimeAssetRoot;
            }
            bool axesSwapped = Mathf.Abs(
                    screenshot.width / schema.designHeight -
                    screenshot.height / schema.designWidth) <=
                Mathf.Max(
                    screenshot.width / schema.designHeight,
                    screenshot.height / schema.designWidth) * 0.02f;
            if (axesSwapped)
            {
                // Some Figma metadata responses invert the root width/height
                // for portrait-oriented UI. The actual screenshot dimensions
                // are authoritative; node coordinates are already in the
                // Frame's landscape coordinate space, so normalize the root
                // schema dimensions before calculating crop scales.
                Debug.LogWarning(
                    "[UIPrefabGenerator/Figma] UISchema 宽高轴与截图互换，" +
                    $"已自动纠正为 {screenshot.width}x{screenshot.height}。");
                schema.designWidth = screenshot.width;
                schema.designHeight = screenshot.height;
            }

            float scaleX = screenshot.width / schema.designWidth;
            float scaleY = screenshot.height / schema.designHeight;
            if (Mathf.Abs(scaleX - scaleY) >
                Mathf.Max(scaleX, scaleY) * 0.02f)
            {
                throw new InvalidOperationException(
                    "Figma 截图宽高比与 UISchema 不一致：" +
                    $"截图 {screenshot.width}x{screenshot.height}，" +
                    $"Schema {schema.designWidth}x{schema.designHeight}。");
            }

            Color32[] sourcePixels = screenshot.GetPixels32();
            var assets = new List<UIEffectFigmaCropAsset>();
            foreach (UIEffectNode child in schema.children)
            {
                CollectCropAssets(
                    child,
                    0f,
                    0f,
                    schema.name,
                    panelId,
                    screenshot.width,
                    screenshot.height,
                    scaleX,
                    scaleY,
                    sourcePixels,
                    runtimeAssetRoot,
                    assets);
            }

            return assets;
        }

        /// <summary>
        /// 执行收集Crop资源相关逻辑。
        /// </summary>
        private static void CollectCropAssets(
            UIEffectNode node,
            float parentX,
            float parentY,
            string parentPath,
            string panelId,
            int sourceWidth,
            int sourceHeight,
            float scaleX,
            float scaleY,
            Color32[] sourcePixels,
            string runtimeAssetRoot,
            List<UIEffectFigmaCropAsset> assets)
        {
            if (node == null)
            {
                return;
            }

            float absoluteX = parentX + node.x;
            float absoluteY = parentY + node.y;
            string nodePath = parentPath + "/" + node.name;
            if (IsCropResourceMarker(node.resource))
            {
                if (node.children != null && node.children.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"裁切节点 {nodePath} 仍包含子节点。" +
                        "请让 Figma MCP 将纯视觉子树折叠为一个无子节点的 " +
                        "Image/Button/Toggle。");
                }

                int left = Mathf.Clamp(
                    Mathf.FloorToInt(absoluteX * scaleX),
                    0,
                    sourceWidth - 1);
                int right = Mathf.Clamp(
                    Mathf.CeilToInt((absoluteX + node.width) * scaleX),
                    left + 1,
                    sourceWidth);
                int top = Mathf.Clamp(
                    Mathf.FloorToInt(absoluteY * scaleY),
                    0,
                    sourceHeight - 1);
                int bottom = Mathf.Clamp(
                    Mathf.CeilToInt(
                        (absoluteY + node.height) * scaleY),
                    top + 1,
                    sourceHeight);
                int cropWidth = right - left;
                int cropHeight = bottom - top;
                int sourceBottom = sourceHeight - bottom;

                Color32[] cropPixels = new Color32[
                    cropWidth * cropHeight];
                for (int row = 0; row < cropHeight; row++)
                {
                    Array.Copy(
                        sourcePixels,
                        (sourceBottom + row) * sourceWidth + left,
                        cropPixels,
                        row * cropWidth,
                        cropWidth);
                }

                var crop = new Texture2D(
                    cropWidth,
                    cropHeight,
                    TextureFormat.RGBA32,
                    false);
                byte[] pngBytes;
                try
                {
                    crop.SetPixels32(cropPixels);
                    crop.Apply(false, false);
                    pngBytes = crop.EncodeToPNG();
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(crop);
                }

                string runtimePath = runtimeAssetRoot.TrimEnd('/') + "/" +
                                     GetSafeAssetSegment(panelId) + "/" +
                                     GetSafeAssetSegment(node.name) + "_" +
                                     GetStableHash(nodePath) + ".png";
                node.resource = runtimePath;
                node.intentionalColor = false;
                assets.Add(new UIEffectFigmaCropAsset
                {
                    NodePath = nodePath,
                    RuntimeAssetPath = runtimePath,
                    PngBytes = pngBytes,
                });
                return;
            }

            if (node.resource != null &&
                node.resource.StartsWith(
                    "figma://",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"节点 {nodePath} 使用未知的 Figma 资源标记：" +
                    node.resource);
            }

            foreach (UIEffectNode child in
                     node.children ?? Enumerable.Empty<UIEffectNode>())
            {
                CollectCropAssets(
                    child,
                    absoluteX,
                    absoluteY,
                    nodePath,
                    panelId,
                    sourceWidth,
                    sourceHeight,
                    scaleX,
                    scaleY,
                    sourcePixels,
                    runtimeAssetRoot,
                    assets);
            }
        }

        /// <summary>
        /// 执行判断是否ReferenceCrop资源相关逻辑。
        /// </summary>
        public static bool IsReferenceCropResource(string resource)
        {
            return string.Equals(
                resource,
                ReferenceCropResourceMarker,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 执行判断是否Crop资源Marker相关逻辑。
        /// </summary>
        private static bool IsCropResourceMarker(string resource)
        {
            return string.Equals(
                       resource,
                       CropResourceMarker,
                       StringComparison.OrdinalIgnoreCase) ||
                   IsReferenceCropResource(resource);
        }

        /// <summary>
        /// 执行判断是否Supported文件类型相关逻辑。
        /// </summary>
        private static bool IsSupportedFileType(string value)
        {
            return string.Equals(
                       value,
                       "design",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       value,
                       "file",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       value,
                       "proto",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取Safe资源Segment。
        /// </summary>
        private static string GetSafeAssetSegment(string value)
        {
            string safe = UIEffectEditorUtility.SanitizeTypeName(value);
            return safe.Length == 0 ? "UIFigma" : safe;
        }

        /// <summary>
        /// 获取Stable哈希。
        /// </summary>
        private static string GetStableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619;
                }

                return hash.ToString("X8", CultureInfo.InvariantCulture);
            }
        }
    }
}
#endif
