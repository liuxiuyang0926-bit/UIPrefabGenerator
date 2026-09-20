#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    [Serializable]
    public sealed class UIEffectSchema
    {
        /// <summary>
        /// 公开的版本数据。
        /// </summary>
        public string version = "2.0";
        /// <summary>
        /// 公开的名称数据。
        /// </summary>
        public string name = "UIExample";
        /// <summary>
        /// 公开的design宽度数据。
        /// </summary>
        public float designWidth = 1080f;
        /// <summary>
        /// 公开的design高度数据。
        /// </summary>
        public float designHeight = 1920f;
        /// <summary>
        /// 公开的引用图片数据。
        /// </summary>
        public string referenceImage = string.Empty;
        /// <summary>
        /// 公开的引用图片哈希数据。
        /// </summary>
        public string referenceImageHash = string.Empty;
        /// <summary>
        /// 公开的使用引用图片AsVisual数据。
        /// </summary>
        public bool useReferenceImageAsVisual;
        /// <summary>
        /// 公开的children数据。
        /// </summary>
        public List<UIEffectNode> children =
            new List<UIEffectNode>();
    }

    [Serializable]
    public sealed class UIEffectNode
    {
        /// <summary>
        /// 公开的名称数据。
        /// </summary>
        public string name = "Node";
        /// <summary>
        /// 公开的类型数据。
        /// </summary>
        public string type = "Container";
        /// <summary>
        /// 公开的semantic数据。
        /// </summary>
        public string semantic = string.Empty;
        /// <summary>
        /// 公开的visualKind数据。
        /// </summary>
        public string visualKind = string.Empty;
        /// <summary>
        /// 公开的文本模式数据。
        /// </summary>
        public string textMode = "Auto";
        /// <summary>
        /// 公开的anchor数据。
        /// </summary>
        public string anchor = "Auto";
        /// <summary>
        /// 公开的x数据。
        /// </summary>
        public float x;
        /// <summary>
        /// 公开的y数据。
        /// </summary>
        public float y;
        /// <summary>
        /// 公开的宽度数据。
        /// </summary>
        public float width = 100f;
        /// <summary>
        /// 公开的高度数据。
        /// </summary>
        public float height = 100f;
        /// <summary>
        /// 公开的文本数据。
        /// </summary>
        public string text = string.Empty;
        /// <summary>
        /// 公开的font尺寸数据。
        /// </summary>
        public float fontSize = 32f;
        /// <summary>
        /// 公开的characterSpacing数据。
        /// </summary>
        public float characterSpacing;
        /// <summary>
        /// 公开的alignment数据。
        /// </summary>
        public string alignment = "Center";
        /// <summary>
        /// 公开的bold数据。
        /// </summary>
        public bool bold;
        /// <summary>
        /// 公开的颜色数据。
        /// </summary>
        public string color = string.Empty;
        /// <summary>
        /// 公开的资源数据。
        /// </summary>
        public string resource = string.Empty;
        public string resourcePolicy = "Auto";
        public string font = string.Empty;
        public string fontMaterial = string.Empty;
        public float lineSpacing;
        public bool noWrap;
        /// <summary>
        /// 公开的资源Candidates数据。
        /// </summary>
        public List<string> resourceCandidates =
            new List<string>();
        /// <summary>
        /// 公开的intentional颜色数据。
        /// </summary>
        public bool intentionalColor;
        /// <summary>
        /// 公开的preserveAspect数据。
        /// </summary>
        public bool preserveAspect;
        /// <summary>
        /// 公开的sliced数据。
        /// </summary>
        public bool sliced;
        /// <summary>
        /// 公开的raycast目标数据。
        /// </summary>
        public bool raycastTarget;
        /// <summary>
        /// 公开的mayMerge数据。
        /// </summary>
        public bool mayMerge;
        /// <summary>
        /// 公开的may层级数据。
        /// </summary>
        public bool mayLayer;
        /// <summary>
        /// 公开的may使用FullCanvasSprite数据。
        /// </summary>
        public bool mayUseFullCanvasSprite;
        /// <summary>
        /// 公开的是否为On数据。
        /// </summary>
        public bool isOn;
        /// <summary>
        /// 公开的允许SwitchOff数据。
        /// </summary>
        public bool allowSwitchOff;
        /// <summary>
        /// 公开的scrollDirection数据。
        /// </summary>
        public string scrollDirection = "Vertical";
        /// <summary>
        /// 公开的绑定数据。
        /// </summary>
        public string binding = "Auto";
        /// <summary>
        /// 公开的运行时TemplateGroup数据。
        /// </summary>
        public string runtimeTemplateGroup = string.Empty;
        /// <summary>
        /// 公开的运行时TemplateVariant数据。
        /// </summary>
        public string runtimeTemplateVariant = string.Empty;
        /// <summary>
        /// 公开的repeat数量数据。
        /// </summary>
        public int repeatCount = 1;
        /// <summary>
        /// 公开的repeat偏移X数据。
        /// </summary>
        public float repeatOffsetX;
        /// <summary>
        /// 公开的repeat偏移Y数据。
        /// </summary>
        public float repeatOffsetY;
        /// <summary>
        /// 公开的repeat名称Digits数据。
        /// </summary>
        public int repeatNameDigits = 2;
        /// <summary>
        /// 公开的variants数据。
        /// </summary>
        public List<UIEffectNodeVariant> variants =
            new List<UIEffectNodeVariant>();
        /// <summary>
        /// 公开的children数据。
        /// </summary>
        public List<UIEffectNode> children =
            new List<UIEffectNode>();
    }

    [Serializable]
    public sealed class UIEffectNodeVariant
    {
        /// <summary>
        /// 公开的索引数据。
        /// </summary>
        public int index;
        /// <summary>
        /// 公开的文本数据。
        /// </summary>
        public string text;
        /// <summary>
        /// 公开的颜色数据。
        /// </summary>
        public string color;
        /// <summary>
        /// 公开的semantic数据。
        /// </summary>
        public string semantic;
        /// <summary>
        /// 公开的资源数据。
        /// </summary>
        public string resource;
        /// <summary>
        /// 公开的资源Candidates数据。
        /// </summary>
        public List<string> resourceCandidates;
    }

    public static class UIEffectSchemaUtility
    {
        private static bool SameTypography(UIEffectNode first, UIEffectNode second)
        {
            return string.Equals(first.font, second.font, StringComparison.Ordinal) &&
                   string.Equals(first.fontMaterial, second.fontMaterial, StringComparison.Ordinal) &&
                   Mathf.Approximately(first.lineSpacing, second.lineSpacing) &&
                   first.noWrap == second.noWrap;
        }

        private static readonly HashSet<string> SupportedTypes =
            new HashSet<string>(
                new[]
                {
                    "Container",
                    "Image",
                    "Text",
                    "Button",
                    "Toggle",
                    "ToggleGroup",
                    "ScrollRect",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> SupportedAnchors =
            new HashSet<string>(
                new[]
                {
                    "Auto",
                    "TopLeft",
                    "TopCenter",
                    "TopRight",
                    "MiddleLeft",
                    "MiddleCenter",
                    "MiddleRight",
                    "BottomLeft",
                    "BottomCenter",
                    "BottomRight",
                    "StretchHorizontal",
                    "StretchVertical",
                    "StretchAll",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> SupportedTextModes =
            new HashSet<string>(
                new[]
                {
                    "Auto",
                    "Editable",
                    "PossiblyBaked",
                    "ArtText",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> SupportedScrollDirections =
            new HashSet<string>(
                new[]
                {
                    "Vertical",
                    "Horizontal",
                    "Both",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> SupportedBindings =
            new HashSet<string>(
                new[]
                {
                    "Auto",
                    "Yes",
                    "No",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> RuntimeTemplateRootKinds =
            new HashSet<string>(
                new[]
                {
                    "Card",
                    "Item",
                    "Row",
                    "Cell",
                    "Entry",
                    "Panel",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> RuntimeTemplateIdentityTokens =
            new HashSet<string>(
                new[]
                {
                    "Faction",
                    "Team",
                    "Guild",
                    "Player",
                    "Member",
                    "Rank",
                    "Reward",
                    "Record",
                    "Data",
                    "Slot",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> RuntimeTemplateVariantTokens =
            new HashSet<string>(
                new[]
                {
                    "Red",
                    "Green",
                    "Blue",
                    "Yellow",
                    "Orange",
                    "Purple",
                    "Violet",
                    "Cyan",
                    "Magenta",
                    "Black",
                    "White",
                    "Gray",
                    "Grey",
                    "Gold",
                    "Silver",
                    "Bronze",
                    "Hong",
                    "Lv",
                    "Lan",
                    "Huang",
                    "Zi",
                    "Bai",
                    "Hei",
                    "Wei",
                    "Shu",
                    "Wu",
                    "Qun",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> LayoutVariantTokens =
            new HashSet<string>(
                new[]
                {
                    "Left",
                    "Right",
                    "Center",
                    "Middle",
                    "Top",
                    "Bottom",
                    "Upper",
                    "Lower",
                    "First",
                    "Last",
                },
                StringComparer.OrdinalIgnoreCase);

        private static readonly Regex RuntimeTemplateNameTokenPattern =
            new Regex(
                @"[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+|[\p{L}]+",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private sealed class ModalNodeLocation
        {
            /// <summary>
            /// 公开的Node数据。
            /// </summary>
            public UIEffectNode Node;
            /// <summary>
            /// 公开的Parent数据。
            /// </summary>
            public ModalNodeLocation Parent;
            /// <summary>
            /// 公开的Sibling索引数据。
            /// </summary>
            public int SiblingIndex;
            /// <summary>
            /// 公开的AbsoluteX数据。
            /// </summary>
            public float AbsoluteX;
            /// <summary>
            /// 公开的AbsoluteY数据。
            /// </summary>
            public float AbsoluteY;
        }

        /// <summary>
        /// 执行解析相关逻辑。
        /// </summary>
        public static UIEffectSchema Parse(string json)
        {
            UIEffectSchema schema = Deserialize(json);
            Validate(schema);
            return schema;
        }

        /// <summary>
        /// 解析Generated。
        /// </summary>
        public static UIEffectSchema ParseGenerated(
            string json,
            out List<string> repairs)
        {
            UIEffectSchema schema = Deserialize(json);
            repairs = new List<string>();
            RepairGeneratedNodes(
                schema.children,
                schema.name,
                repairs);
            Validate(schema);
            return schema;
        }

        /// <summary>
        /// 执行反序列化相关逻辑。
        /// </summary>
        private static UIEffectSchema Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(
                    "UISchema JSON 不能为空。");
            }

            UIEffectSchema schema =
                JsonUtility.FromJson<UIEffectSchema>(json);
            if (schema == null)
            {
                throw new InvalidOperationException(
                    "无法解析 UISchema JSON。");
            }

            return schema;
        }

        /// <summary>
        /// 执行修复生成结果节点相关逻辑。
        /// </summary>
        private static void RepairGeneratedNodes(
            List<UIEffectNode> nodes,
            string parentPath,
            List<string> repairs)
        {
            if (nodes == null)
            {
                return;
            }

            foreach (UIEffectNode node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                string nodeName = string.IsNullOrWhiteSpace(node.name)
                    ? "<Unnamed>"
                    : node.name.Trim();
                string nodePath = string.IsNullOrWhiteSpace(parentPath)
                    ? nodeName
                    : parentPath + "/" + nodeName;
                string binding = node.binding?.Trim() ?? string.Empty;
                if (binding.Length > 0 &&
                    !SupportedBindings.Contains(binding))
                {
                    string repairedBinding =
                        NormalizeGeneratedBinding(binding);
                    node.binding = repairedBinding;
                    repairs.Add(
                        nodePath + ": binding \"" + binding +
                        "\" -> \"" + repairedBinding + "\"");
                }

                RepairGeneratedNodes(
                    node.children,
                    nodePath,
                    repairs);
            }
        }

        /// <summary>
        /// 执行规范化生成结果绑定相关逻辑。
        /// </summary>
        private static string NormalizeGeneratedBinding(string value)
        {
            if (string.Equals(value, "false",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "none",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "disabled",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "off",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "No";
            }

            if (string.Equals(value, "true",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "bind",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "bound",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "required",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "enabled",
                    StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(
                    value,
                    @"^[A-Za-z_][A-Za-z0-9_]*$",
                    RegexOptions.CultureInvariant))
            {
                // AI models sometimes put a desired member name in `binding`.
                // Preserve the binding intent; the generated member name still
                // comes from the node's canonical name and project prefix rules.
                return "Yes";
            }

            return "Auto";
        }

        /// <summary>
        /// 确保Unique绑定名称。
        /// </summary>
        public static int EnsureUniqueBindingNames(
            UIEffectSchema schema,
            List<string> repairs = null)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            var usedBindingNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            return EnsureUniqueBindingNames(
                schema.children,
                schema.name,
                new List<string>(),
                usedBindingNames,
                repairs);
        }

        /// <summary>
        /// 确保Unique绑定名称。
        /// </summary>
        private static int EnsureUniqueBindingNames(
            List<UIEffectNode> nodes,
            string parentPath,
            List<string> ancestorNames,
            HashSet<string> usedBindingNames,
            List<string> repairs)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return 0;
            }

            var siblingNames = new HashSet<string>(
                nodes.Where(node => node != null)
                    .Select(node => node.name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            int repairedCount = 0;
            foreach (UIEffectNode node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                string originalNodeName = node.name ?? string.Empty;
                string originalPath = string.IsNullOrWhiteSpace(parentPath)
                    ? originalNodeName
                    : parentPath + "/" + originalNodeName;
                if (ShouldBindNode(node))
                {
                    string originalBindingName = GetBindingName(node);
                    if (!usedBindingNames.Add(originalBindingName))
                    {
                        siblingNames.Remove(originalNodeName);
                        node.name = BuildUniqueBoundNodeName(
                            node,
                            ancestorNames,
                            siblingNames,
                            usedBindingNames);
                        siblingNames.Add(node.name);
                        string repairedBindingName = GetBindingName(node);
                        usedBindingNames.Add(repairedBindingName);
                        repairedCount++;
                        repairs?.Add(
                            originalPath + ": " + originalBindingName +
                            " -> " + repairedBindingName);
                    }
                }

                ancestorNames.Add(node.name);
                string currentPath = string.IsNullOrWhiteSpace(parentPath)
                    ? node.name
                    : parentPath + "/" + node.name;
                repairedCount += EnsureUniqueBindingNames(
                    node.children,
                    currentPath,
                    ancestorNames,
                    usedBindingNames,
                    repairs);
                ancestorNames.RemoveAt(ancestorNames.Count - 1);
            }

            return repairedCount;
        }

        /// <summary>
        /// 构建UniqueBoundNode名称。
        /// </summary>
        private static string BuildUniqueBoundNodeName(
            UIEffectNode node,
            List<string> ancestorNames,
            HashSet<string> siblingNames,
            HashSet<string> usedBindingNames)
        {
            string cleanNodeName = UIEffectEditorUtility.SanitizeTypeName(
                node.name);
            string accumulatedPrefix = string.Empty;
            for (int index = ancestorNames.Count - 1;
                 index >= 0;
                 index--)
            {
                accumulatedPrefix =
                    UIEffectEditorUtility.SanitizeTypeName(
                        ancestorNames[index]) + accumulatedPrefix;
                string candidate = accumulatedPrefix + cleanNodeName;
                if (IsAvailableBoundNodeName(
                        node,
                        candidate,
                        siblingNames,
                        usedBindingNames))
                {
                    return candidate;
                }
            }

            string numberedBase = accumulatedPrefix.Length > 0
                ? accumulatedPrefix + cleanNodeName
                : cleanNodeName;
            for (int suffix = 2; suffix < int.MaxValue; suffix++)
            {
                string candidate = numberedBase + suffix;
                if (IsAvailableBoundNodeName(
                        node,
                        candidate,
                        siblingNames,
                        usedBindingNames))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "无法为重复绑定节点生成唯一名称：" + node.name);
        }

        /// <summary>
        /// 执行判断是否AvailableBound节点名称相关逻辑。
        /// </summary>
        private static bool IsAvailableBoundNodeName(
            UIEffectNode node,
            string candidate,
            HashSet<string> siblingNames,
            HashSet<string> usedBindingNames)
        {
            return !siblingNames.Contains(candidate) &&
                   !usedBindingNames.Contains(
                       GetBindingName(node.type, candidate));
        }

        /// <summary>
        /// 获取绑定名称。
        /// </summary>
        private static string GetBindingName(UIEffectNode node)
        {
            return GetBindingName(node.type, node.name);
        }

        /// <summary>
        /// 获取绑定名称。
        /// </summary>
        private static string GetBindingName(
            string nodeType,
            string nodeName)
        {
            return GetGeneratedNodeName(nodeType, nodeName, true);
        }

        internal static string GetGeneratedNodeName(UIEffectNode node)
        {
            return GetGeneratedNodeName(node.type, node.name, ShouldBindNode(node));
        }

        private static readonly string[] VisualNamePrefixes = { "Bnt_", "Btn_", "Img_", "Txt_", "Tmp_" };

        private static string GetGeneratedNodeName(string nodeType, string nodeName, bool bind)
        {
            string prefix;
            switch ((nodeType ?? string.Empty).ToLowerInvariant())
            {
                case "image":
                    prefix = "Img_";
                    break;
                case "text":
                    prefix = "Txt_";
                    break;
                case "button":
                    prefix = "Bnt_";
                    break;
                case "toggle":
                    prefix = bind ? "tgl_" : string.Empty;
                    break;
                case "scrollrect":
                    prefix = bind ? "scroll_" : string.Empty;
                    break;
                default:
                    prefix = bind ? "rt_" : string.Empty;
                    break;
            }

            // Normalize existing visual prefixes before sanitizing (which removes underscores).
            // This also handles Image <-> Text conversions made by resource matching.
            string cleanName = (nodeName ?? string.Empty).Trim();
            if (prefix == "Img_" || prefix == "Txt_" || prefix == "Bnt_")
            {
                bool stripped;
                do
                {
                    stripped = false;
                    foreach (string existingPrefix in VisualNamePrefixes)
                    {
                        if (!cleanName.StartsWith(existingPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        cleanName = cleanName.Substring(existingPrefix.Length);
                        stripped = true;
                        break;
                    }
                } while (stripped);
            }
            return prefix + UIEffectEditorUtility.SanitizeTypeName(cleanName);
        }

        /// <summary>
        /// 执行判断是否绑定节点相关逻辑。
        /// </summary>
        private static bool ShouldBindNode(UIEffectNode node)
        {
            if (node == null)
            {
                return false;
            }

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

            return string.Equals(
                       node.type,
                       "Button",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       node.type,
                       "Toggle",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       node.type,
                       "ScrollRect",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 执行ExtractModalForeground相关逻辑。
        /// </summary>
        public static int ExtractModalForeground(
            UIEffectSchema schema,
            List<string> notes = null)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (schema.children == null || schema.children.Count < 2 ||
                schema.designWidth <= 0f || schema.designHeight <= 0f)
            {
                return 0;
            }

            var locations = new List<ModalNodeLocation>();
            CollectModalNodeLocations(
                schema.children,
                null,
                0f,
                0f,
                locations);
            float canvasArea = schema.designWidth * schema.designHeight;
            List<ModalNodeLocation> scrims = locations
                .Where(location =>
                    IsModalScrim(location.Node) &&
                    GetNodeArea(location.Node) >= canvasArea * 0.25f)
                .OrderByDescending(location => GetNodeArea(location.Node))
                .ThenBy(location => location.SiblingIndex)
                .ToList();
            foreach (ModalNodeLocation scrim in scrims)
            {
                List<ModalNodeLocation> candidates = locations
                    .Where(location =>
                        !ReferenceEquals(location, scrim) &&
                        ReferenceEquals(location.Parent, scrim.Parent) &&
                        location.SiblingIndex > scrim.SiblingIndex &&
                        IsModalContentCandidate(
                            location.Node,
                            canvasArea) &&
                        IsContainedBy(location, scrim, 2f))
                    .OrderByDescending(location =>
                        HasExplicitModalIdentity(location.Node))
                    .ThenByDescending(location =>
                        CountNodes(location.Node))
                    .ThenByDescending(location =>
                        GetNodeArea(location.Node))
                    .ThenBy(location => location.SiblingIndex)
                    .ToList();
                if (candidates.Count == 0)
                {
                    continue;
                }

                ModalNodeLocation modal = candidates[0];
                int previousNodeCount = CountNodes(schema.children);
                scrim.Node.x = scrim.AbsoluteX;
                scrim.Node.y = scrim.AbsoluteY;
                modal.Node.x = modal.AbsoluteX;
                modal.Node.y = modal.AbsoluteY;
                schema.children = new List<UIEffectNode>
                {
                    scrim.Node,
                    modal.Node,
                };
                int removedNodeCount = Math.Max(
                    0,
                    previousNodeCount - CountNodes(schema.children));
                if (removedNodeCount > 0)
                {
                    notes?.Add(
                        "保留模态遮罩 " + scrim.Node.name +
                        " 与弹窗 " + modal.Node.name +
                        "，排除底层界面节点 " + removedNodeCount + " 个");
                }

                return removedNodeCount;
            }

            return 0;
        }

        /// <summary>
        /// 执行收集Modal节点Locations相关逻辑。
        /// </summary>
        private static void CollectModalNodeLocations(
            List<UIEffectNode> nodes,
            ModalNodeLocation parent,
            float parentX,
            float parentY,
            List<ModalNodeLocation> output)
        {
            if (nodes == null)
            {
                return;
            }

            for (int index = 0; index < nodes.Count; index++)
            {
                UIEffectNode node = nodes[index];
                if (node == null)
                {
                    continue;
                }

                var location = new ModalNodeLocation
                {
                    Node = node,
                    Parent = parent,
                    SiblingIndex = index,
                    AbsoluteX = parentX + node.x,
                    AbsoluteY = parentY + node.y,
                };
                output.Add(location);
                CollectModalNodeLocations(
                    node.children,
                    location,
                    location.AbsoluteX,
                    location.AbsoluteY,
                    output);
            }
        }

        /// <summary>
        /// 执行判断是否ModalScrim相关逻辑。
        /// </summary>
        private static bool IsModalScrim(UIEffectNode node)
        {
            if (node == null ||
                (!string.Equals(node.type, "Image",
                     StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(node.type, "Container",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string identity = ((node.name ?? string.Empty) + " " +
                               (node.semantic ?? string.Empty))
                .ToLowerInvariant();
            return identity.Contains("overlay") ||
                   identity.Contains("dimmer") ||
                   identity.Contains("dimlayer") ||
                   identity.Contains("scrim") ||
                   identity.Contains("modalmask") ||
                   identity.Contains("遮罩") ||
                   identity.Contains("蒙层");
        }

        /// <summary>
        /// 执行判断是否ModalContentCandidate相关逻辑。
        /// </summary>
        private static bool IsModalContentCandidate(
            UIEffectNode node,
            float canvasArea)
        {
            if (node == null ||
                (!string.Equals(node.type, "Image",
                     StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(node.type, "Container",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            float area = GetNodeArea(node);
            if (area < canvasArea * 0.02f || area > canvasArea * 0.85f)
            {
                return false;
            }

            return HasExplicitModalIdentity(node) ||
                   (node.children != null && node.children.Count >= 2);
        }

        /// <summary>
        /// 执行判断是否ExplicitModalIdentity相关逻辑。
        /// </summary>
        private static bool HasExplicitModalIdentity(UIEffectNode node)
        {
            string identity = ((node?.name ?? string.Empty) + " " +
                               (node?.semantic ?? string.Empty))
                .ToLowerInvariant();
            return identity.Contains("popup") ||
                   identity.Contains("dialog") ||
                   identity.Contains("modal") ||
                   identity.Contains("弹窗") ||
                   identity.Contains("对话框");
        }

        /// <summary>
        /// 执行判断是否ContainedBy相关逻辑。
        /// </summary>
        private static bool IsContainedBy(
            ModalNodeLocation child,
            ModalNodeLocation parent,
            float tolerance)
        {
            return child.AbsoluteX >= parent.AbsoluteX - tolerance &&
                   child.AbsoluteY >= parent.AbsoluteY - tolerance &&
                   child.AbsoluteX + child.Node.width <=
                       parent.AbsoluteX + parent.Node.width + tolerance &&
                   child.AbsoluteY + child.Node.height <=
                       parent.AbsoluteY + parent.Node.height + tolerance;
        }

        /// <summary>
        /// 获取NodeArea。
        /// </summary>
        private static float GetNodeArea(UIEffectNode node)
        {
            return node == null
                ? 0f
                : Math.Max(0f, node.width) * Math.Max(0f, node.height);
        }

        /// <summary>
        /// 执行数量节点相关逻辑。
        /// </summary>
        private static int CountNodes(List<UIEffectNode> nodes)
        {
            if (nodes == null)
            {
                return 0;
            }

            int count = 0;
            foreach (UIEffectNode node in nodes)
            {
                count += CountNodes(node);
            }

            return count;
        }

        /// <summary>
        /// 执行数量节点相关逻辑。
        /// </summary>
        private static int CountNodes(UIEffectNode node)
        {
            return node == null
                ? 0
                : 1 + CountNodes(node.children);
        }

        /// <summary>
        /// 执行校验相关逻辑。
        /// </summary>
        public static void Validate(UIEffectSchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            schema.name = schema.name?.Trim() ?? string.Empty;
            schema.version = schema.version?.Trim() ?? string.Empty;
            schema.referenceImage =
                schema.referenceImage?.Trim() ?? string.Empty;
            schema.referenceImageHash =
                schema.referenceImageHash?.Trim() ?? string.Empty;
            schema.children ??= new List<UIEffectNode>();

            if (schema.version != "1.0" && schema.version != "2.0")
            {
                throw new InvalidOperationException(
                    $"不支持的 UISchema 版本：{schema.version}。");
            }

            if (schema.name.Length == 0)
            {
                throw new InvalidOperationException(
                    "UISchema.name 不能为空。");
            }

            if (schema.designWidth <= 0f ||
                schema.designHeight <= 0f)
            {
                throw new InvalidOperationException(
                    "UISchema 的设计宽高必须大于 0。");
            }

            var paths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0;
                 index < schema.children.Count;
                 index++)
            {
                ValidateNode(
                    schema.children[index],
                    schema.name,
                    paths,
                    schema.version == "1.0");
            }
        }

        /// <summary>
        /// 执行ExpandRepeats相关逻辑。
        /// </summary>
        public static UIEffectSchema ExpandRepeats(UIEffectSchema source)
        {
            Validate(source);
            var expanded = new UIEffectSchema
            {
                version = source.version,
                name = source.name,
                designWidth = source.designWidth,
                designHeight = source.designHeight,
                referenceImage = source.referenceImage,
                referenceImageHash = source.referenceImageHash,
                useReferenceImageAsVisual =
                    source.useReferenceImageAsVisual,
                children = new List<UIEffectNode>(),
            };

            int expandedNodeCount = 0;
            foreach (UIEffectNode node in source.children)
            {
                ExpandNode(
                    node,
                    string.Empty,
                    0,
                    expanded.children,
                    ref expandedNodeCount);
            }

            Validate(expanded);
            return expanded;
        }

        /// <summary>
        /// 执行推断Contained视觉Hierarchy相关逻辑。
        /// </summary>
        public static int InferContainedVisualHierarchy(
            UIEffectSchema schema)
        {
            Validate(schema);
            int movedNodeCount = InferContainedVisualHierarchy(
                schema.children,
                schema.designWidth,
                schema.designHeight);
            Validate(schema);
            return movedNodeCount;
        }

        /// <summary>
        /// 执行推断Contained视觉Hierarchy相关逻辑。
        /// </summary>
        private static int InferContainedVisualHierarchy(
            List<UIEffectNode> children,
            float parentWidth,
            float parentHeight)
        {
            if (children == null || children.Count < 2)
            {
                int nestedCount = 0;
                foreach (UIEffectNode child in
                         children ?? new List<UIEffectNode>())
                {
                    nestedCount += InferContainedVisualHierarchy(
                        child.children,
                        child.width,
                        child.height);
                }

                return nestedCount;
            }

            var original = new List<UIEffectNode>(children);
            var ownerIndices = new int[original.Count];
            var originalX = new float[original.Count];
            var originalY = new float[original.Count];
            for (int index = 0; index < ownerIndices.Length; index++)
            {
                ownerIndices[index] = -1;
                originalX[index] = original[index]?.x ?? 0f;
                originalY[index] = original[index]?.y ?? 0f;
            }

            int movedCount = 0;
            for (int childIndex = 1;
                 childIndex < original.Count;
                 childIndex++)
            {
                UIEffectNode child = original[childIndex];
                if (child == null || child.width <= 0f ||
                    child.height <= 0f)
                {
                    continue;
                }

                int bestOwnerIndex = -1;
                float bestOwnerArea = float.MaxValue;
                for (int ownerIndex = 0;
                     ownerIndex < childIndex;
                     ownerIndex++)
                {
                    UIEffectNode owner = original[ownerIndex];
                    if (!CanOwnContainedNode(
                            owner,
                            parentWidth,
                            parentHeight) ||
                        !ContainsNode(owner, child))
                    {
                        continue;
                    }

                    float ownerArea = owner.width * owner.height;
                    float childArea = child.width * child.height;
                    float maximumAreaRatio = IsTextOrActionNode(child)
                        ? 64f
                        : 8f;
                    if (ownerArea > childArea * maximumAreaRatio)
                    {
                        continue;
                    }

                    if (ownerArea < bestOwnerArea ||
                        Mathf.Approximately(ownerArea, bestOwnerArea) &&
                        ownerIndex > bestOwnerIndex)
                    {
                        bestOwnerArea = ownerArea;
                        bestOwnerIndex = ownerIndex;
                    }
                }

                if (bestOwnerIndex >= 0)
                {
                    ownerIndices[childIndex] = bestOwnerIndex;
                    movedCount++;
                }
            }

            if (movedCount > 0)
            {
                children.Clear();
                for (int index = 0; index < original.Count; index++)
                {
                    int ownerIndex = ownerIndices[index];
                    UIEffectNode node = original[index];
                    if (ownerIndex < 0)
                    {
                        children.Add(node);
                        continue;
                    }

                    UIEffectNode owner = original[ownerIndex];
                    node.x -= originalX[ownerIndex];
                    node.y -= originalY[ownerIndex];
                    owner.children.Add(node);
                }
            }

            foreach (UIEffectNode child in children)
            {
                movedCount += InferContainedVisualHierarchy(
                    child.children,
                    child.width,
                    child.height);
            }

            return movedCount;
        }

        /// <summary>
        /// 执行判断能否OwnContained节点相关逻辑。
        /// </summary>
        private static bool CanOwnContainedNode(
            UIEffectNode node,
            float parentWidth,
            float parentHeight)
        {
            if (node == null || node.width <= 0f || node.height <= 0f ||
                string.Equals(
                    node.type,
                    "Text",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalizedName =
                (node.name ?? string.Empty).ToLowerInvariant();
            if (normalizedName.Contains("overlay") ||
                normalizedName.Contains("dimmer") ||
                normalizedName.Contains("dimlayer"))
            {
                return false;
            }

            bool fillsParent = parentWidth > 0f && parentHeight > 0f &&
                               node.width >= parentWidth * 0.98f &&
                               node.height >= parentHeight * 0.98f;
            return !fillsParent ||
                   string.Equals(
                       node.type,
                       "Container",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 执行判断是否包含节点相关逻辑。
        /// </summary>
        private static bool ContainsNode(
            UIEffectNode owner,
            UIEffectNode child)
        {
            const float tolerance = 2f;
            return child.x >= owner.x - tolerance &&
                   child.y >= owner.y - tolerance &&
                   child.x + child.width <=
                   owner.x + owner.width + tolerance &&
                   child.y + child.height <=
                   owner.y + owner.height + tolerance;
        }

        /// <summary>
        /// 执行判断是否文本OrAction节点相关逻辑。
        /// </summary>
        private static bool IsTextOrActionNode(UIEffectNode node)
        {
            return string.Equals(
                       node.type,
                       "Text",
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
        /// 执行优化Repeated节点相关逻辑。
        /// </summary>
        public static int OptimizeRepeatedNodes(UIEffectSchema schema)
        {
            Validate(schema);
            int collapsedNodeCount = OptimizeChildren(schema.children);
            schema.version = "2.0";
            Validate(schema);
            return collapsedNodeCount;
        }

        /// <summary>
        /// Conservatively annotates visually variant runtime data templates.
        /// It never removes a node. Actual selection still happens in the
        /// Prefab builder after every candidate has completed resource matching.
        /// </summary>
        public static int InferRuntimeTemplateGroups(UIEffectSchema schema)
        {
            Validate(schema);
            int annotatedNodeCount = InferRuntimeTemplateGroups(
                schema.children);
            Validate(schema);
            return annotatedNodeCount;
        }

        /// <summary>
        /// 执行推断运行时模板Groups相关逻辑。
        /// </summary>
        private static int InferRuntimeTemplateGroups(
            List<UIEffectNode> siblings)
        {
            if (siblings == null || siblings.Count == 0)
            {
                return 0;
            }

            int annotatedNodeCount = 0;
            var candidatesBySignature =
                new SortedDictionary<string, List<RuntimeTemplateCandidate>>(
                    StringComparer.Ordinal);
            foreach (UIEffectNode node in siblings)
            {
                if (!CanInferRuntimeTemplateRoot(node) ||
                    !TryTokenizeRuntimeTemplateName(
                        node.name,
                        out List<string> tokens))
                {
                    continue;
                }

                string rootKind = tokens[tokens.Count - 1];
                if (!RuntimeTemplateRootKinds.Contains(rootKind))
                {
                    continue;
                }

                for (int variantIndex = 0;
                     variantIndex < tokens.Count - 1;
                     variantIndex++)
                {
                    string variant = tokens[variantIndex];
                    if (variant.Length == 0)
                    {
                        continue;
                    }

                    var signature = new StringBuilder(
                        node.type.ToLowerInvariant());
                    signature.Append(':');
                    for (int tokenIndex = 0;
                         tokenIndex < tokens.Count;
                         tokenIndex++)
                    {
                        if (tokenIndex > 0)
                        {
                            signature.Append('|');
                        }

                        signature.Append(tokenIndex == variantIndex
                            ? "*"
                            : tokens[tokenIndex].ToLowerInvariant());
                    }

                    string key = signature.ToString();
                    if (!candidatesBySignature.TryGetValue(
                            key,
                            out List<RuntimeTemplateCandidate> candidates))
                    {
                        candidates = new List<RuntimeTemplateCandidate>();
                        candidatesBySignature.Add(key, candidates);
                    }

                    candidates.Add(new RuntimeTemplateCandidate(
                        node,
                        tokens,
                        variantIndex));
                }
            }

            foreach (KeyValuePair<string, List<RuntimeTemplateCandidate>> pair
                     in candidatesBySignature)
            {
                List<RuntimeTemplateCandidate> candidates = pair.Value;
                if (!CanInferRuntimeTemplateGroup(candidates))
                {
                    continue;
                }

                RuntimeTemplateCandidate first = candidates[0];
                bool rankedTemplate = IsRankedRuntimeTemplateGroup(candidates);
                bool sameShape = true;
                for (int index = 1; index < candidates.Count; index++)
                {
                    RuntimeTemplateCandidate candidate = candidates[index];
                    if (!HaveRuntimeTemplateShape(
                            first.Node,
                            candidate.Node,
                            first.Variant,
                            candidate.Variant,
                            false,
                            rankedTemplate))
                    {
                        sameShape = false;
                        break;
                    }
                }

                if (!sameShape)
                {
                    continue;
                }

                string groupName = BuildRuntimeTemplateGroupName(first);
                foreach (RuntimeTemplateCandidate candidate in candidates)
                {
                    candidate.Node.runtimeTemplateGroup = groupName;
                    candidate.Node.runtimeTemplateVariant =
                        candidate.Variant.ToLowerInvariant();
                    annotatedNodeCount++;
                }
            }

            foreach (UIEffectNode node in siblings)
            {
                if (node != null)
                {
                    annotatedNodeCount += InferRuntimeTemplateGroups(
                        node.children);
                }
            }

            return annotatedNodeCount;
        }

        /// <summary>
        /// 执行判断能否推断运行时模板根节点相关逻辑。
        /// </summary>
        private static bool CanInferRuntimeTemplateRoot(UIEffectNode node)
        {
            return node != null &&
                   string.IsNullOrWhiteSpace(node.runtimeTemplateGroup) &&
                   node.repeatCount <= 1 &&
                   (string.Equals(
                        node.type,
                        "Container",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        node.type,
                        "Image",
                        StringComparison.OrdinalIgnoreCase)) &&
                   CountRuntimeTemplateNodes(node) >= 3;
        }

        /// <summary>
        /// 执行数量运行时模板节点相关逻辑。
        /// </summary>
        private static int CountRuntimeTemplateNodes(UIEffectNode node)
        {
            if (node == null)
            {
                return 0;
            }

            int count = 1;
            foreach (UIEffectNode child in node.children)
            {
                count += CountRuntimeTemplateNodes(child);
            }

            return count;
        }

        /// <summary>
        /// 执行判断能否推断运行时模板Group相关逻辑。
        /// </summary>
        private static bool CanInferRuntimeTemplateGroup(
            List<RuntimeTemplateCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 2)
            {
                return false;
            }

            var nodes = new HashSet<UIEffectNode>();
            var variants = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            bool allKnownVariants = true;
            bool rankedTemplate = IsRankedRuntimeTemplateGroup(candidates);
            bool hasRuntimeIdentity = false;
            RuntimeTemplateCandidate first = candidates[0];
            string rootKind = first.Tokens[first.Tokens.Count - 1];
            for (int tokenIndex = 0;
                 tokenIndex < first.Tokens.Count - 1;
                 tokenIndex++)
            {
                if (tokenIndex != first.VariantIndex &&
                    RuntimeTemplateIdentityTokens.Contains(
                        first.Tokens[tokenIndex]))
                {
                    hasRuntimeIdentity = true;
                    break;
                }
            }

            foreach (RuntimeTemplateCandidate candidate in candidates)
            {
                if (!nodes.Add(candidate.Node) ||
                    !variants.Add(candidate.Variant) ||
                    (LayoutVariantTokens.Contains(candidate.Variant) && !rankedTemplate) ||
                    !string.IsNullOrWhiteSpace(
                        candidate.Node.runtimeTemplateGroup))
                {
                    return false;
                }

                allKnownVariants &= RuntimeTemplateVariantTokens.Contains(
                    candidate.Variant);
            }

            if (nodes.Count != candidates.Count ||
                variants.Count != candidates.Count)
            {
                return false;
            }

            if (string.Equals(
                    rootKind,
                    "Panel",
                    StringComparison.OrdinalIgnoreCase) &&
                !hasRuntimeIdentity && !rankedTemplate)
            {
                return false;
            }

            return rankedTemplate || (candidates.Count >= 3
                ? allKnownVariants || hasRuntimeIdentity
                : allKnownVariants);
        }

        private static bool IsRankedRuntimeTemplateGroup(List<RuntimeTemplateCandidate> candidates)
        {
            // First/Last remain layout tokens outside an explicitly ranked data item.
            return candidates.Count >= 2 && candidates.All(candidate =>
                candidate.Tokens.Where((token, index) => index != candidate.VariantIndex)
                    .Any(token => string.Equals(token, "Rank", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(token, "Place", StringComparison.OrdinalIgnoreCase)) &&
                new[] { "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth" }
                    .Contains(candidate.Variant, StringComparer.OrdinalIgnoreCase));
        }

        private static bool IsRuntimeArtworkSlot(UIEffectNode node)
        {
            return string.Equals(node.type, "Image", StringComparison.OrdinalIgnoreCase) &&
                   node.children.Count == 0 && !node.raycastTarget &&
                   new[] { "icon", "emblem", "crest", "avatar", "portrait" }
                       .Contains(node.semantic, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 执行Have运行时模板Shape相关逻辑。
        /// </summary>
        private static bool HaveRuntimeTemplateShape(
            UIEffectNode first,
            UIEffectNode candidate,
            string firstVariant,
            string candidateVariant,
            bool comparePosition,
            bool rankedTemplate = false)
        {
            bool artworkGeometry = rankedTemplate && first != null && candidate != null &&
                IsRuntimeArtworkSlot(first) && IsRuntimeArtworkSlot(candidate) &&
                string.Equals(first.semantic, candidate.semantic, StringComparison.OrdinalIgnoreCase);
            if (first == null || candidate == null ||
                !string.Equals(
                    first.type,
                    candidate.type,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    NormalizeRuntimeTemplateName(first.name, firstVariant),
                    NormalizeRuntimeTemplateName(
                        candidate.name,
                        candidateVariant),
                    StringComparison.OrdinalIgnoreCase) ||
                (comparePosition && !artworkGeometry &&
                 (!ApproximatelyRuntimeTemplateValue(first.x, candidate.x) ||
                  !ApproximatelyRuntimeTemplateValue(first.y, candidate.y))) ||
                (!artworkGeometry && (!ApproximatelyRuntimeTemplateValue(
                    first.width,
                    candidate.width) ||
                !ApproximatelyRuntimeTemplateValue(
                    first.height,
                    candidate.height))) ||
                first.children.Count != candidate.children.Count ||
                first.repeatCount != candidate.repeatCount ||
                !string.Equals(
                    first.anchor,
                    candidate.anchor,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    first.visualKind,
                    candidate.visualKind,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    first.textMode,
                    candidate.textMode,
                    StringComparison.OrdinalIgnoreCase) ||
                !ApproximatelyRuntimeTemplateValue(
                    first.fontSize,
                    candidate.fontSize) ||
                !ApproximatelyRuntimeTemplateValue(
                    first.characterSpacing,
                    candidate.characterSpacing) ||
                !string.Equals(
                    first.alignment,
                    candidate.alignment,
                    StringComparison.OrdinalIgnoreCase) ||
                first.bold != candidate.bold ||
                !SameTypography(first, candidate) ||
                first.raycastTarget != candidate.raycastTarget ||
                first.mayMerge != candidate.mayMerge ||
                first.mayLayer != candidate.mayLayer ||
                first.mayUseFullCanvasSprite !=
                candidate.mayUseFullCanvasSprite ||
                first.isOn != candidate.isOn ||
                first.allowSwitchOff != candidate.allowSwitchOff ||
                !string.Equals(
                    first.scrollDirection,
                    candidate.scrollDirection,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    first.binding,
                    candidate.binding,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int index = 0; index < first.children.Count; index++)
            {
                if (!HaveRuntimeTemplateShape(
                        first.children[index],
                        candidate.children[index],
                        firstVariant,
                        candidateVariant,
                        true,
                        rankedTemplate && FitsRuntimeArtworkSlot(first.children[index], first) &&
                        FitsRuntimeArtworkSlot(candidate.children[index], candidate)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FitsRuntimeArtworkSlot(UIEffectNode node, UIEffectNode parent)
        {
            return !IsRuntimeArtworkSlot(node) || (node.x >= 0 && node.y >= 0 &&
                node.width > 0 && node.height > 0 && node.x + node.width <= parent.width + 1 &&
                node.y + node.height <= parent.height + 1);
        }

        /// <summary>
        /// 执行Approximately运行时模板值相关逻辑。
        /// </summary>
        private static bool ApproximatelyRuntimeTemplateValue(
            float first,
            float candidate)
        {
            return Mathf.Abs(first - candidate) <= 1f;
        }

        /// <summary>
        /// 执行规范化运行时模板名称相关逻辑。
        /// </summary>
        private static string NormalizeRuntimeTemplateName(
            string name,
            string variant)
        {
            if (!TryTokenizeRuntimeTemplateName(
                    name,
                    out List<string> tokens))
            {
                return name?.Trim() ?? string.Empty;
            }

            var normalized = new StringBuilder();
            foreach (string token in tokens)
            {
                if (!string.Equals(
                        token,
                        variant,
                        StringComparison.OrdinalIgnoreCase))
                {
                    normalized.Append(token.ToLowerInvariant());
                }
            }

            return normalized.ToString();
        }

        /// <summary>
        /// 构建运行时Template分组名称。
        /// </summary>
        private static string BuildRuntimeTemplateGroupName(
            RuntimeTemplateCandidate candidate)
        {
            var result = new StringBuilder();
            for (int index = 0; index < candidate.Tokens.Count; index++)
            {
                if (index != candidate.VariantIndex)
                {
                    result.Append(candidate.Tokens[index]);
                }
            }

            return result.Length == 0
                ? "RuntimeTemplate"
                : result.ToString();
        }

        /// <summary>
        /// 尝试Tokenize运行时Template名称，并返回是否成功。
        /// </summary>
        private static bool TryTokenizeRuntimeTemplateName(
            string name,
            out List<string> tokens)
        {
            tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string[] segments = Regex.Split(name.Trim(), @"[_\-\s]+");
            foreach (string segment in segments)
            {
                MatchCollection matches =
                    RuntimeTemplateNameTokenPattern.Matches(segment);
                foreach (Match match in matches)
                {
                    if (match.Success && match.Length > 0)
                    {
                        tokens.Add(match.Value);
                    }
                }
            }

            return tokens.Count >= 2;
        }

        private sealed class RuntimeTemplateCandidate
        {
            /// <summary>
            /// 创建运行时TemplateCandidate实例。
            /// </summary>
            public RuntimeTemplateCandidate(
                UIEffectNode node,
                List<string> tokens,
                int variantIndex)
            {
                Node = node;
                Tokens = tokens;
                VariantIndex = variantIndex;
                Variant = tokens[variantIndex];
            }

            /// <summary>
            /// 向调用方提供Node。
            /// </summary>
            public UIEffectNode Node { get; }

            /// <summary>
            /// 向调用方提供Tokens。
            /// </summary>
            public List<string> Tokens { get; }

            /// <summary>
            /// 向调用方提供VariantIndex。
            /// </summary>
            public int VariantIndex { get; }

            /// <summary>
            /// 向调用方提供Variant。
            /// </summary>
            public string Variant { get; }
        }

        /// <summary>
        /// 执行转换为CompactJson相关逻辑。
        /// </summary>
        public static string ToCompactJson(UIEffectSchema schema)
        {
            Validate(schema);
            var writer = new CompactJsonWriter();
            writer.WriteSchema(schema);
            return writer.ToString();
        }

        /// <summary>
        /// 执行Expand节点相关逻辑。
        /// </summary>
        private static void ExpandNode(
            UIEffectNode source,
            string inheritedSuffix,
            int inheritedVariantIndex,
            List<UIEffectNode> destination,
            ref int expandedNodeCount)
        {
            int repeatCount = Mathf.Max(1, source.repeatCount);
            for (int repeatIndex = 0;
                 repeatIndex < repeatCount;
                 repeatIndex++)
            {
                string suffix = inheritedSuffix;
                int variantIndex = inheritedVariantIndex;
                if (repeatCount > 1)
                {
                    suffix += (repeatIndex + 1).ToString(
                        new string('0', source.repeatNameDigits),
                        CultureInfo.InvariantCulture);
                    variantIndex = repeatIndex + 1;
                }

                UIEffectNode node = CloneNode(source);
                node.name += suffix;
                node.x += source.repeatOffsetX * repeatIndex;
                node.y += source.repeatOffsetY * repeatIndex;
                node.repeatCount = 1;
                node.repeatOffsetX = 0f;
                node.repeatOffsetY = 0f;
                node.variants.Clear();
                node.children.Clear();
                ApplyVariant(node, source.variants, variantIndex);

                expandedNodeCount++;
                if (expandedNodeCount > 5000)
                {
                    throw new InvalidOperationException(
                        "UISchema 展开后的节点数不能超过 5000。");
                }

                foreach (UIEffectNode child in source.children)
                {
                    ExpandNode(
                        child,
                        suffix,
                        variantIndex,
                        node.children,
                        ref expandedNodeCount);
                }

                destination.Add(node);
            }
        }

        /// <summary>
        /// 执行克隆节点相关逻辑。
        /// </summary>
        private static UIEffectNode CloneNode(UIEffectNode source)
        {
            return new UIEffectNode
            {
                name = source.name,
                type = source.type,
                semantic = source.semantic,
                visualKind = source.visualKind,
                textMode = source.textMode,
                anchor = source.anchor,
                x = source.x,
                y = source.y,
                width = source.width,
                height = source.height,
                text = source.text,
                fontSize = source.fontSize,
                characterSpacing = source.characterSpacing,
                alignment = source.alignment,
                bold = source.bold,
                color = source.color,
                resource = source.resource,
                resourcePolicy = source.resourcePolicy,
                font = source.font,
                fontMaterial = source.fontMaterial,
                lineSpacing = source.lineSpacing,
                noWrap = source.noWrap,
                resourceCandidates = new List<string>(
                    source.resourceCandidates ?? new List<string>()),
                intentionalColor = source.intentionalColor,
                preserveAspect = source.preserveAspect,
                sliced = source.sliced,
                raycastTarget = source.raycastTarget,
                mayMerge = source.mayMerge,
                mayLayer = source.mayLayer,
                mayUseFullCanvasSprite =
                    source.mayUseFullCanvasSprite,
                isOn = source.isOn,
                allowSwitchOff = source.allowSwitchOff,
                scrollDirection = source.scrollDirection,
                binding = source.binding,
                runtimeTemplateGroup = source.runtimeTemplateGroup,
                runtimeTemplateVariant = source.runtimeTemplateVariant,
                repeatCount = source.repeatCount,
                repeatOffsetX = source.repeatOffsetX,
                repeatOffsetY = source.repeatOffsetY,
                repeatNameDigits = source.repeatNameDigits,
                variants = new List<UIEffectNodeVariant>(
                    source.variants ?? new List<UIEffectNodeVariant>()),
                children = new List<UIEffectNode>(
                    source.children ?? new List<UIEffectNode>()),
            };
        }

        /// <summary>
        /// 应用Variant。
        /// </summary>
        private static void ApplyVariant(
            UIEffectNode node,
            List<UIEffectNodeVariant> variants,
            int variantIndex)
        {
            if (variantIndex <= 0 || variants == null)
            {
                return;
            }

            UIEffectNodeVariant variant = variants.Find(
                item => item != null && item.index == variantIndex);
            if (variant == null)
            {
                return;
            }

            if (variant.text != null)
            {
                node.text = variant.text;
            }

            if (variant.color != null)
            {
                node.color = variant.color;
            }

            if (variant.semantic != null)
            {
                node.semantic = variant.semantic;
            }

            if (variant.resource != null)
            {
                node.resource = variant.resource;
            }

            if (variant.resourceCandidates != null &&
                variant.resourceCandidates.Count > 0)
            {
                node.resourceCandidates = new List<string>(
                    variant.resourceCandidates);
            }
        }

        /// <summary>
        /// 执行优化Children相关逻辑。
        /// </summary>
        private static int OptimizeChildren(List<UIEffectNode> children)
        {
            if (children == null || children.Count == 0)
            {
                return 0;
            }

            int collapsedNodeCount = 0;
            foreach (UIEffectNode child in children)
            {
                collapsedNodeCount += OptimizeChildren(child.children);
            }

            for (int start = 0; start < children.Count; start++)
            {
                UIEffectNode first = children[start];
                if (first == null || first.repeatCount > 1 ||
                    !TryGetRepeatName(first.name, out string baseName,
                        out int firstIndex, out int digits) ||
                    firstIndex != 1)
                {
                    continue;
                }

                var group = new List<UIEffectNode> { first };
                int end = start + 1;
                while (end < children.Count &&
                       TryGetRepeatName(children[end].name,
                           out string nextBase, out int nextIndex,
                           out int nextDigits) &&
                       string.Equals(baseName, nextBase,
                           StringComparison.Ordinal) &&
                       nextDigits == digits &&
                       nextIndex == group.Count + 1 &&
                       HaveRepeatShape(first, children[end],
                           firstIndex, nextIndex))
                {
                    group.Add(children[end]);
                    end++;
                }

                if (group.Count < 2 || !TryGetRepeatOffset(
                        group, out float offsetX, out float offsetY))
                {
                    continue;
                }

                CollectVariants(first, group, firstIndex);
                StripRepeatSuffix(first, firstIndex);
                first.repeatCount = group.Count;
                first.repeatOffsetX = offsetX;
                first.repeatOffsetY = offsetY;
                first.repeatNameDigits = digits;
                children.RemoveRange(start + 1, group.Count - 1);
                collapsedNodeCount += group.Count - 1;
            }

            return collapsedNodeCount;
        }

        /// <summary>
        /// 执行HaveRepeatShape相关逻辑。
        /// </summary>
        private static bool HaveRepeatShape(
            UIEffectNode first,
            UIEffectNode candidate,
            int firstIndex,
            int candidateIndex)
        {
            if (first == null || candidate == null ||
                !string.Equals(first.type, candidate.type,
                    StringComparison.OrdinalIgnoreCase) ||
                first.children.Count != candidate.children.Count ||
                !Mathf.Approximately(first.width, candidate.width) ||
                !Mathf.Approximately(first.height, candidate.height) ||
                !string.Equals(first.anchor, candidate.anchor,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(first.visualKind, candidate.visualKind,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(first.textMode, candidate.textMode,
                    StringComparison.OrdinalIgnoreCase) ||
                !Mathf.Approximately(first.fontSize, candidate.fontSize) ||
                !Mathf.Approximately(
                    first.characterSpacing,
                    candidate.characterSpacing) ||
                !string.Equals(first.alignment, candidate.alignment,
                    StringComparison.OrdinalIgnoreCase) ||
                first.bold != candidate.bold ||
                first.intentionalColor != candidate.intentionalColor ||
                !SameTypography(first, candidate) ||
                !string.Equals(first.resourcePolicy, candidate.resourcePolicy, StringComparison.OrdinalIgnoreCase) ||
                first.preserveAspect != candidate.preserveAspect ||
                first.sliced != candidate.sliced ||
                first.raycastTarget != candidate.raycastTarget ||
                first.mayMerge != candidate.mayMerge ||
                first.mayLayer != candidate.mayLayer ||
                first.mayUseFullCanvasSprite !=
                candidate.mayUseFullCanvasSprite ||
                !string.Equals(first.scrollDirection,
                    candidate.scrollDirection,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(first.binding,
                    candidate.binding,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(first.runtimeTemplateGroup,
                    candidate.runtimeTemplateGroup,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(first.runtimeTemplateVariant,
                    candidate.runtimeTemplateVariant,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int index = 0; index < first.children.Count; index++)
            {
                UIEffectNode firstChild = first.children[index];
                UIEffectNode candidateChild = candidate.children[index];
                if (!NamesMatchRepeat(
                        firstChild.name,
                        candidateChild.name,
                        firstIndex,
                        candidateIndex) ||
                    !Mathf.Approximately(firstChild.x, candidateChild.x) ||
                    !Mathf.Approximately(firstChild.y, candidateChild.y) ||
                    !HaveRepeatShape(
                        firstChild,
                        candidateChild,
                        firstIndex,
                        candidateIndex))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 尝试获取Repeat偏移，并返回是否成功。
        /// </summary>
        private static bool TryGetRepeatOffset(
            List<UIEffectNode> group,
            out float offsetX,
            out float offsetY)
        {
            offsetX = (group[group.Count - 1].x - group[0].x) /
                      (group.Count - 1);
            offsetY = (group[group.Count - 1].y - group[0].y) /
                      (group.Count - 1);
            for (int index = 1; index < group.Count; index++)
            {
                float expectedX = group[0].x + offsetX * index;
                float expectedY = group[0].y + offsetY * index;
                if (Mathf.Abs(group[index].x - expectedX) > 3f ||
                    Mathf.Abs(group[index].y - expectedY) > 3f)
                {
                    return false;
                }
            }

            offsetX = Mathf.Round(offsetX * 1000f) / 1000f;
            offsetY = Mathf.Round(offsetY * 1000f) / 1000f;
            return true;
        }

        /// <summary>
        /// 执行收集Variants相关逻辑。
        /// </summary>
        private static void CollectVariants(
            UIEffectNode template,
            List<UIEffectNode> instances,
            int templateIndex)
        {
            for (int index = 1; index < instances.Count; index++)
            {
                UIEffectNode candidate = instances[index];
                var variant = new UIEffectNodeVariant
                {
                    index = index + 1,
                };
                bool changed = false;
                changed |= SetVariant(
                    template.text, candidate.text,
                    value => variant.text = value);
                changed |= SetVariant(
                    template.color, candidate.color,
                    value => variant.color = value);
                changed |= SetVariant(
                    template.semantic, candidate.semantic,
                    value => variant.semantic = value);
                changed |= SetVariant(
                    template.resource, candidate.resource,
                    value => variant.resource = value);
                if (!ListsEqual(
                        template.resourceCandidates,
                        candidate.resourceCandidates))
                {
                    variant.resourceCandidates = new List<string>(
                        candidate.resourceCandidates);
                    changed = true;
                }

                if (changed)
                {
                    template.variants.Add(variant);
                }
            }

            for (int childIndex = 0;
                 childIndex < template.children.Count;
                 childIndex++)
            {
                var childInstances = new List<UIEffectNode>();
                foreach (UIEffectNode instance in instances)
                {
                    childInstances.Add(instance.children[childIndex]);
                }

                CollectVariants(
                    template.children[childIndex],
                    childInstances,
                    templateIndex);
            }
        }

        /// <summary>
        /// 设置Variant。
        /// </summary>
        private static bool SetVariant(
            string template,
            string candidate,
            Action<string> setter)
        {
            if (string.Equals(template, candidate,
                    StringComparison.Ordinal))
            {
                return false;
            }

            setter(candidate ?? string.Empty);
            return true;
        }

        /// <summary>
        /// 执行ListsEqual相关逻辑。
        /// </summary>
        private static bool ListsEqual(
            List<string> first,
            List<string> second)
        {
            first ??= new List<string>();
            second ??= new List<string>();
            if (first.Count != second.Count)
            {
                return false;
            }

            for (int index = 0; index < first.Count; index++)
            {
                if (!string.Equals(first[index], second[index],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 执行StripRepeatSuffix相关逻辑。
        /// </summary>
        private static void StripRepeatSuffix(
            UIEffectNode node,
            int repeatIndex)
        {
            if (TryGetRepeatName(node.name, out string baseName,
                    out int nodeIndex, out _) &&
                nodeIndex == repeatIndex)
            {
                node.name = baseName;
            }

            foreach (UIEffectNode child in node.children)
            {
                StripRepeatSuffix(child, repeatIndex);
            }
        }

        /// <summary>
        /// 执行名称匹配Repeat相关逻辑。
        /// </summary>
        private static bool NamesMatchRepeat(
            string first,
            string second,
            int firstIndex,
            int secondIndex)
        {
            return TryGetRepeatName(first, out string firstBase,
                       out int parsedFirstIndex, out _) &&
                   TryGetRepeatName(second, out string secondBase,
                       out int parsedSecondIndex, out _) &&
                   parsedFirstIndex == firstIndex &&
                   parsedSecondIndex == secondIndex &&
                   string.Equals(firstBase, secondBase,
                       StringComparison.Ordinal);
        }

        /// <summary>
        /// 尝试获取Repeat名称，并返回是否成功。
        /// </summary>
        private static bool TryGetRepeatName(
            string value,
            out string baseName,
            out int index,
            out int digits)
        {
            index = 0;
            Match match = Regex.Match(
                value ?? string.Empty,
                "^(.*?)([0-9]{2,})$");
            if (!match.Success ||
                !int.TryParse(match.Groups[2].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out index))
            {
                baseName = string.Empty;
                digits = 0;
                return false;
            }

            baseName = match.Groups[1].Value;
            digits = match.Groups[2].Value.Length;
            return baseName.Length > 0;
        }

        /// <summary>
        /// 校验Node。
        /// </summary>
        private static void ValidateNode(
            UIEffectNode node,
            string parentPath,
            HashSet<string> paths,
            bool legacyAutoBinding)
        {
            if (node == null)
            {
                throw new InvalidOperationException(
                    $"{parentPath} 包含空 UI 节点。");
            }

            node.name = node.name?.Trim() ?? string.Empty;
            node.type = node.type?.Trim() ?? string.Empty;
            node.semantic = node.semantic?.Trim() ?? string.Empty;
            node.visualKind = node.visualKind?.Trim() ?? string.Empty;
            node.textMode = string.IsNullOrWhiteSpace(node.textMode)
                ? "Auto"
                : node.textMode.Trim();
            node.resourcePolicy = string.IsNullOrWhiteSpace(node.resourcePolicy)
                ? "Auto" : node.resourcePolicy.Trim();
            if (!new[] { "Auto", "Candidate", "Verified", "ColorFallback" }
                    .Contains(node.resourcePolicy, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("不支持的 resourcePolicy：" + node.resourcePolicy);
            if (float.IsNaN(node.lineSpacing) || float.IsInfinity(node.lineSpacing))
                throw new InvalidOperationException("lineSpacing 必须是有限数值。");
            node.anchor = string.IsNullOrWhiteSpace(node.anchor)
                ? "Auto"
                : node.anchor.Trim();
            node.text ??= string.Empty;
            node.alignment = string.IsNullOrWhiteSpace(node.alignment)
                ? "Center"
                : node.alignment.Trim();
            node.color = node.color?.Trim() ?? string.Empty;
            node.resource = node.resource?.Trim() ?? string.Empty;
            node.scrollDirection =
                string.IsNullOrWhiteSpace(node.scrollDirection)
                    ? "Vertical"
                    : node.scrollDirection.Trim();
            node.binding = string.IsNullOrWhiteSpace(node.binding)
                ? "Auto"
                : node.binding.Trim();
            node.runtimeTemplateGroup =
                node.runtimeTemplateGroup?.Trim() ?? string.Empty;
            node.runtimeTemplateVariant =
                node.runtimeTemplateVariant?.Trim() ?? string.Empty;
            if (legacyAutoBinding &&
                string.Equals(
                    node.binding,
                    "Auto",
                    StringComparison.OrdinalIgnoreCase))
            {
                node.binding = "Yes";
            }
            node.resourceCandidates ??= new List<string>();
            node.variants ??= new List<UIEffectNodeVariant>();
            node.children ??= new List<UIEffectNode>();

            if (node.repeatCount <= 0)
            {
                node.repeatCount = 1;
            }

            if (node.repeatNameDigits <= 0)
            {
                node.repeatNameDigits = 2;
            }

            if (node.fontSize <= 0f)
            {
                node.fontSize = 32f;
            }

            if (node.width <= 0f)
            {
                node.width = 1f;
            }

            if (node.height <= 0f)
            {
                node.height = 1f;
            }

            if (node.name.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{parentPath} 包含未命名 UI 节点。");
            }

            if (!SupportedTypes.Contains(node.type))
            {
                throw new InvalidOperationException(
                    $"节点 {parentPath}/{node.name} 使用了不支持的类型：" +
                    node.type);
            }

            if (!SupportedAnchors.Contains(node.anchor))
            {
                throw new InvalidOperationException(
                    $"节点 {parentPath}/{node.name} 使用了不支持的锚点：" +
                    node.anchor);
            }

            if (!SupportedTextModes.Contains(node.textMode))
            {
                throw new InvalidOperationException(
                    $"节点 {parentPath}/{node.name} 使用了不支持的 textMode：" +
                    node.textMode);
            }

            if (node.type.Equals(
                    "ScrollRect",
                    StringComparison.OrdinalIgnoreCase) &&
                !SupportedScrollDirections.Contains(
                    node.scrollDirection))
            {
                throw new InvalidOperationException(
                    $"节点 {parentPath}/{node.name} 使用了不支持的滚动方向：" +
                    node.scrollDirection);
            }

            if (!SupportedBindings.Contains(node.binding))
            {
                throw new InvalidOperationException(
                    $"节点 {parentPath}/{node.name} 使用了不支持的绑定策略：" +
                    node.binding);
            }

            if (node.repeatCount > 100)
            {
                throw new InvalidOperationException(
                    $"节点 {parentPath}/{node.name} 的 repeatCount 不能超过 100。");
            }

            var variantIndices = new HashSet<int>();
            var uniqueVariants = new List<UIEffectNodeVariant>();
            for (int index = node.variants.Count - 1;
                 index >= 0;
                 index--)
            {
                UIEffectNodeVariant variant = node.variants[index];
                if (variant == null ||
                    variant.index <= 0 ||
                    variant.index > 100)
                {
                    // Variants are optional visual overrides. Figma/Codex
                    // can emit a placeholder index (0) or an out-of-range
                    // index; ignore that entry instead of aborting the UI.
                    continue;
                }

                // Codex/Figma responses can repeat an index. Keep the last
                // declaration because it is the most specific correction,
                // while retaining the schema's original order.
                if (!variantIndices.Add(variant.index))
                {
                    continue;
                }

                if (variant.resourceCandidates != null &&
                    variant.resourceCandidates.Count == 0)
                {
                    variant.resourceCandidates = null;
                }

                uniqueVariants.Add(variant);
            }

            uniqueVariants.Reverse();
            node.variants = uniqueVariants;

            string path = parentPath + "/" + node.name;
            if (!paths.Add(path))
            {
                // Figma commonly reuses names such as “按钮” for sibling
                // layers.  A Unity hierarchy may contain those names, but
                // binding names and crop paths must remain deterministic.
                // Preserve the first node's name and disambiguate later
                // siblings instead of rejecting an otherwise valid frame.
                string baseName = node.name;
                int suffix = 2;
                do
                {
                    node.name = baseName + "_" + suffix.ToString(
                        CultureInfo.InvariantCulture);
                    path = parentPath + "/" + node.name;
                    suffix++;
                }
                while (!paths.Add(path));
            }

            for (int index = 0;
                 index < node.resourceCandidates.Count;
                 index++)
            {
                node.resourceCandidates[index] =
                    node.resourceCandidates[index]?.Trim() ??
                    string.Empty;
            }

            for (int index = 0;
                 index < node.children.Count;
                 index++)
            {
                ValidateNode(
                    node.children[index],
                    path,
                    paths,
                    legacyAutoBinding);
            }
        }

        private sealed class CompactJsonWriter
        {
            private readonly StringBuilder builder = new StringBuilder();

            /// <summary>
            /// 生成当前实例的字符串表示。
            /// </summary>
            public override string ToString()
            {
                return builder.ToString();
            }

            /// <summary>
            /// 执行写入结构定义相关逻辑。
            /// </summary>
            public void WriteSchema(UIEffectSchema schema)
            {
                builder.Append('{');
                bool first = true;
                WriteString(ref first, "version", schema.version);
                WriteString(ref first, "name", schema.name);
                WriteNumber(ref first, "designWidth", schema.designWidth);
                WriteNumber(ref first, "designHeight", schema.designHeight);
                if (!string.IsNullOrWhiteSpace(schema.referenceImage))
                {
                    WriteString(
                        ref first,
                        "referenceImage",
                        schema.referenceImage);
                }

                if (!string.IsNullOrWhiteSpace(
                        schema.referenceImageHash))
                {
                    WriteString(
                        ref first,
                        "referenceImageHash",
                        schema.referenceImageHash);
                }

                WriteTrue(
                    ref first,
                    "useReferenceImageAsVisual",
                    schema.useReferenceImageAsVisual);

                WriteNodes(ref first, "children", schema.children);
                builder.Append('}');
            }

            /// <summary>
            /// 执行写入节点相关逻辑。
            /// </summary>
            private void WriteNode(UIEffectNode node)
            {
                builder.Append('{');
                bool first = true;
                WriteString(ref first, "name", node.name);
                WriteString(ref first, "type", node.type);
                if (!string.IsNullOrWhiteSpace(node.semantic))
                {
                    WriteString(ref first, "semantic", node.semantic);
                }

                if (!string.IsNullOrWhiteSpace(node.visualKind))
                {
                    WriteString(ref first, "visualKind", node.visualKind);
                }

                if (!string.Equals(
                        node.textMode,
                        "Auto",
                        StringComparison.OrdinalIgnoreCase))
                {
                    WriteString(ref first, "textMode", node.textMode);
                }

                if (!string.Equals(
                        node.anchor,
                        "Auto",
                        StringComparison.OrdinalIgnoreCase))
                {
                    WriteString(ref first, "anchor", node.anchor);
                }

                if (!Mathf.Approximately(node.x, 0f))
                {
                    WriteNumber(ref first, "x", node.x);
                }

                if (!Mathf.Approximately(node.y, 0f))
                {
                    WriteNumber(ref first, "y", node.y);
                }

                WriteNumber(ref first, "width", node.width);
                WriteNumber(ref first, "height", node.height);
                if (!string.IsNullOrEmpty(node.text))
                {
                    WriteString(ref first, "text", node.text);
                }

                if (!Mathf.Approximately(node.fontSize, 32f))
                {
                    WriteNumber(ref first, "fontSize", node.fontSize);
                }

                if (!Mathf.Approximately(node.characterSpacing, 0f))
                {
                    WriteNumber(
                        ref first,
                        "characterSpacing",
                        node.characterSpacing);
                }

                if (!string.Equals(
                        node.alignment,
                        "Center",
                        StringComparison.OrdinalIgnoreCase))
                {
                    WriteString(ref first, "alignment", node.alignment);
                }

                WriteTrue(ref first, "bold", node.bold);
                if (!string.IsNullOrWhiteSpace(node.font)) WriteString(ref first, "font", node.font);
                if (!string.IsNullOrWhiteSpace(node.fontMaterial)) WriteString(ref first, "fontMaterial", node.fontMaterial);
                if (!Mathf.Approximately(node.lineSpacing, 0f)) WriteNumber(ref first, "lineSpacing", node.lineSpacing);
                WriteTrue(ref first, "noWrap", node.noWrap);
                if (!string.Equals(node.resourcePolicy, "Auto", StringComparison.OrdinalIgnoreCase))
                    WriteString(ref first, "resourcePolicy", node.resourcePolicy);
                if (!string.IsNullOrWhiteSpace(node.color))
                {
                    WriteString(ref first, "color", node.color);
                }

                if (!string.IsNullOrWhiteSpace(node.resource))
                {
                    WriteString(ref first, "resource", node.resource);
                }

                if (node.resourceCandidates != null &&
                    node.resourceCandidates.Count > 0)
                {
                    WriteStrings(
                        ref first,
                        "resourceCandidates",
                        node.resourceCandidates);
                }

                WriteTrue(
                    ref first,
                    "intentionalColor",
                    node.intentionalColor);
                WriteTrue(
                    ref first,
                    "preserveAspect",
                    node.preserveAspect);
                WriteTrue(ref first, "sliced", node.sliced);
                WriteTrue(
                    ref first,
                    "raycastTarget",
                    node.raycastTarget);
                WriteTrue(ref first, "mayMerge", node.mayMerge);
                WriteTrue(ref first, "mayLayer", node.mayLayer);
                WriteTrue(
                    ref first,
                    "mayUseFullCanvasSprite",
                    node.mayUseFullCanvasSprite);
                WriteTrue(ref first, "isOn", node.isOn);
                WriteTrue(
                    ref first,
                    "allowSwitchOff",
                    node.allowSwitchOff);
                if (!string.Equals(
                        node.scrollDirection,
                        "Vertical",
                        StringComparison.OrdinalIgnoreCase))
                {
                    WriteString(
                        ref first,
                        "scrollDirection",
                        node.scrollDirection);
                }

                if (!string.Equals(
                        node.binding,
                        "Auto",
                        StringComparison.OrdinalIgnoreCase))
                {
                    WriteString(ref first, "binding", node.binding);
                }

                if (!string.IsNullOrWhiteSpace(
                        node.runtimeTemplateGroup))
                {
                    WriteString(
                        ref first,
                        "runtimeTemplateGroup",
                        node.runtimeTemplateGroup);
                }

                if (!string.IsNullOrWhiteSpace(
                        node.runtimeTemplateVariant))
                {
                    WriteString(
                        ref first,
                        "runtimeTemplateVariant",
                        node.runtimeTemplateVariant);
                }

                if (node.repeatCount > 1)
                {
                    WriteInteger(
                        ref first,
                        "repeatCount",
                        node.repeatCount);
                    if (!Mathf.Approximately(node.repeatOffsetX, 0f))
                    {
                        WriteNumber(
                            ref first,
                            "repeatOffsetX",
                            node.repeatOffsetX);
                    }

                    if (!Mathf.Approximately(node.repeatOffsetY, 0f))
                    {
                        WriteNumber(
                            ref first,
                            "repeatOffsetY",
                            node.repeatOffsetY);
                    }

                    if (node.repeatNameDigits != 2)
                    {
                        WriteInteger(
                            ref first,
                            "repeatNameDigits",
                            node.repeatNameDigits);
                    }
                }

                if (node.variants != null && node.variants.Count > 0)
                {
                    WriteVariants(ref first, node.variants);
                }

                if (node.children != null && node.children.Count > 0)
                {
                    WriteNodes(ref first, "children", node.children);
                }

                builder.Append('}');
            }

            /// <summary>
            /// 执行写入Variants相关逻辑。
            /// </summary>
            private void WriteVariants(
                ref bool first,
                List<UIEffectNodeVariant> variants)
            {
                WritePropertyName(ref first, "variants");
                builder.Append('[');
                for (int index = 0; index < variants.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    UIEffectNodeVariant variant = variants[index];
                    builder.Append('{');
                    bool firstProperty = true;
                    WriteInteger(
                        ref firstProperty,
                        "index",
                        variant.index);
                    WriteNullableString(
                        ref firstProperty,
                        "text",
                        variant.text);
                    WriteNullableString(
                        ref firstProperty,
                        "color",
                        variant.color);
                    WriteNullableString(
                        ref firstProperty,
                        "semantic",
                        variant.semantic);
                    WriteNullableString(
                        ref firstProperty,
                        "resource",
                        variant.resource);
                    if (variant.resourceCandidates != null &&
                        variant.resourceCandidates.Count > 0)
                    {
                        WriteStrings(
                            ref firstProperty,
                            "resourceCandidates",
                            variant.resourceCandidates);
                    }

                    builder.Append('}');
                }

                builder.Append(']');
            }

            /// <summary>
            /// 执行写入节点相关逻辑。
            /// </summary>
            private void WriteNodes(
                ref bool first,
                string name,
                List<UIEffectNode> nodes)
            {
                WritePropertyName(ref first, name);
                builder.Append('[');
                for (int index = 0; index < nodes.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    WriteNode(nodes[index]);
                }

                builder.Append(']');
            }

            /// <summary>
            /// 执行写入Strings相关逻辑。
            /// </summary>
            private void WriteStrings(
                ref bool first,
                string name,
                List<string> values)
            {
                WritePropertyName(ref first, name);
                builder.Append('[');
                for (int index = 0; index < values.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    WriteEscaped(values[index] ?? string.Empty);
                }

                builder.Append(']');
            }

            /// <summary>
            /// 执行写入字符串相关逻辑。
            /// </summary>
            private void WriteString(
                ref bool first,
                string name,
                string value)
            {
                WritePropertyName(ref first, name);
                WriteEscaped(value ?? string.Empty);
            }

            /// <summary>
            /// 执行写入Nullable字符串相关逻辑。
            /// </summary>
            private void WriteNullableString(
                ref bool first,
                string name,
                string value)
            {
                if (value != null)
                {
                    WriteString(ref first, name, value);
                }
            }

            /// <summary>
            /// 执行写入Number相关逻辑。
            /// </summary>
            private void WriteNumber(
                ref bool first,
                string name,
                float value)
            {
                WritePropertyName(ref first, name);
                builder.Append(value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));
            }

            /// <summary>
            /// 执行写入Integer相关逻辑。
            /// </summary>
            private void WriteInteger(
                ref bool first,
                string name,
                int value)
            {
                WritePropertyName(ref first, name);
                builder.Append(value.ToString(
                    CultureInfo.InvariantCulture));
            }

            /// <summary>
            /// 执行写入True相关逻辑。
            /// </summary>
            private void WriteTrue(
                ref bool first,
                string name,
                bool value)
            {
                if (!value)
                {
                    return;
                }

                WritePropertyName(ref first, name);
                builder.Append("true");
            }

            /// <summary>
            /// 执行写入Property名称相关逻辑。
            /// </summary>
            private void WritePropertyName(
                ref bool first,
                string name)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                WriteEscaped(name);
                builder.Append(':');
            }

            /// <summary>
            /// 执行写入Escaped相关逻辑。
            /// </summary>
            private void WriteEscaped(string value)
            {
                builder.Append('"');
                foreach (char character in value)
                {
                    switch (character)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (character < 0x20)
                            {
                                builder.Append("\\u");
                                builder.Append(((int)character).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(character);
                            }

                            break;
                    }
                }

                builder.Append('"');
            }
        }
    }
}
#endif
