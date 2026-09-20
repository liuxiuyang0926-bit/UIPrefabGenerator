#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    internal sealed class UIEffectNodeEvidence
    {
        public string Path;
        public UIEffectNode Node;
        public Rect Rect;
        public bool Accepted;
        public bool RequiresReview;
        public bool ContainerProbe;
        public int ResourceRevision;
        public readonly List<UIEffectSpriteEvidence> Candidates = new List<UIEffectSpriteEvidence>();
    }

    internal sealed class UIEffectSpriteEvidence
    {
        public Sprite Sprite;
        public string Resource;
        public float Score;
        public Color Tint;
        public Vector2 SourceSize;
        public Vector4 Border;
    }

    internal sealed partial class UIEffectResourceResolver
    {
        private bool collectNodeEvidence;
        internal static int ResourceRevision { get; private set; }
        private string excludedReferencePath;
        private readonly Dictionary<UIEffectNode, UIEffectNodeEvidence> nodeEvidence =
            new Dictionary<UIEffectNode, UIEffectNodeEvidence>();

        // This pass never writes a Prefab, prunes templates or rewrites the input.
        // It shares the final Builder's occlusion, backdrop and candidate scoring.
        internal List<UIEffectNodeEvidence> AnalyzeCandidates(UIEffectSchema input)
        {
            UIEffectSchema schema = UIEffectSchemaUtility.ExpandRepeats(
                UIEffectSchemaUtility.Parse(UIEffectSchemaUtility.ToCompactJson(input)));
            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                collectNodeEvidence = true;
                nodeEvidence.Clear();
                excludedReferencePath = (schema.referenceImage ?? string.Empty).Replace('\\', '/');
                if (!reference.LoadImage(File.ReadAllBytes(
                        UIEffectEditorUtility.ToAbsolutePath(schema.referenceImage))))
                    throw new InvalidOperationException("无法解码节点检索参考图。");
                PrepareCandidatePolicies(schema.children);
                using (var renderer = new SpritePreviewRenderer(VisualSampleSize))
                {
                    MatchVisualNodes(schema.children, 0, 0, schema.designWidth, schema.designHeight,
                        schema, reference, renderer, Array.Empty<Rect>(), Array.Empty<Rect>(),
                        string.Empty, null);
                }
                var result = new List<UIEffectNodeEvidence>();
                CollectOrderedEvidence(schema.children, string.Empty, result);
                return result;
            }
            finally
            {
                collectNodeEvidence = false;
                UnityEngine.Object.DestroyImmediate(reference);
                EditorUtility.ClearProgressBar();
            }
        }

        private void CollectOrderedEvidence(List<UIEffectNode> nodes, string parent,
            List<UIEffectNodeEvidence> result)
        {
            foreach (UIEffectNode node in nodes)
            {
                string path = parent.Length == 0 ? node.name : parent + "/" + node.name;
                if (nodeEvidence.TryGetValue(node, out UIEffectNodeEvidence evidence))
                {
                    evidence.Path = path;
                    result.Add(evidence);
                }
                CollectOrderedEvidence(node.children, path, result);
            }
        }

        private void CaptureNodeEvidence(UIEffectNode node, Rect rect,
            List<VisualCandidate> candidates, bool accepted, bool compact, bool probe)
        {
            var evidence = new UIEffectNodeEvidence
            {
                Node = node, Rect = rect, Accepted = accepted, ContainerProbe = probe,
                ResourceRevision = ResourceRevision,
                RequiresReview = !accepted || compact || probe || node.mayMerge || node.mayLayer ||
                    node.mayUseFullCanvasSprite || rect.width * rect.height > 160000f,
            };
            foreach (VisualCandidate candidate in candidates ?? new List<VisualCandidate>())
            {
                SpriteEntry entry = candidate.Entry;
                evidence.Candidates.Add(new UIEffectSpriteEvidence
                {
                    Sprite = entry.Sprite, Resource = GetSpriteResourcePath(entry),
                    Score = candidate.Score, Tint = candidate.MatchedColor,
                    SourceSize = new Vector2(entry.AuthoredWidth, entry.AuthoredHeight),
                    Border = new Vector4(entry.BorderLeft, entry.BorderBottom, entry.BorderRight, entry.BorderTop),
                });
            }
            if (evidence.Candidates.Count < 2 || evidence.Candidates[0].Score < 0.9f ||
                evidence.Candidates[0].Score - evidence.Candidates[1].Score < 0.035f)
                evidence.RequiresReview = true;
            nodeEvidence[node] = evidence;
        }

        private void PrepareCandidatePolicies(IEnumerable<UIEffectNode> nodes)
        {
            foreach (UIEffectNode node in nodes)
            {
                if (string.Equals(node.resourcePolicy, "Candidate", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(node.resource))
                    {
                        node.resourceCandidates.Remove(node.resource);
                        node.resourceCandidates.Insert(0, node.resource);
                    }
                    node.resource = string.Empty;
                }
                else if (string.Equals(node.resourcePolicy, "ColorFallback", StringComparison.OrdinalIgnoreCase))
                {
                    node.resource = string.Empty;
                    node.resourceCandidates.Clear();
                    node.intentionalColor = true;
                }
                else if (HasExplicitAssetResource(node))
                {
                    if (LoadSpriteAtPath(node.resource) == null)
                    {
                        // A path retained from another project or a moved asset is not
                        // a verified Sprite. Release the lock and search this project.
                        WarnResource($"{node.name} 的 Sprite 路径已失效：" +
                            node.resource + "；将重新执行本地视觉匹配。");
                        node.resource = string.Empty;
                        node.resourcePolicy = "Auto";
                    }
                    // An explicit Sprite must not be hidden by an old color fallback flag.
                    node.intentionalColor = false;
                }
                PrepareCandidatePolicies(node.children);
            }
        }
    }
}
#endif
