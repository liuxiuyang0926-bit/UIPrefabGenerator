#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    public sealed partial class UIEffectPrefabGeneratorWindow
    {
        private bool awaitingStructure;
        private List<UIEffectNodeEvidence> pendingNodeEvidence;
        private UIEffectPrefabGenerationOptions pendingGenerationOptions;
        private string lastFidelityReportPath;

        private void AccumulateFidelityUsage(UIEffectCodexUsage usage)
        {
            if (usage == null) return;
            lastCodexUsage ??= new UIEffectCodexUsage();
            lastCodexUsage.input_tokens += usage.input_tokens;
            lastCodexUsage.cached_input_tokens += usage.cached_input_tokens;
            lastCodexUsage.output_tokens += usage.output_tokens;
            lastCodexUsage.reasoning_output_tokens += usage.reasoning_output_tokens;
        }

        private void BeginResourceCorrection(UIEffectSchema draft)
        {
            if (lastCodexUsage != null && lastCodexUsage.TotalTokens >= 235000)
                throw new InvalidOperationException("已达到后续分析 Token 门槛，未启动第二阶段。");
            awaitingStructure = false;
            requestStatus = "正在按节点矩形检索资源库，准备局部对照图（2/2）…";
            // First-stage paths have no asset evidence; even if the model ignored
            // its contract they must not bypass the independent local search.
            MergeNodeEvidence(draft, null);
            UIEffectGenerationTiming.BeginStage("全库候选检索");
            var resolver = new UIEffectResourceResolver(pendingGenerationOptions.resourceSearchRoots,
                UIEffectResourceMatchMode.VisualSimilarity);
            pendingNodeEvidence = resolver.AnalyzeCandidates(draft);
            UIEffectGenerationTiming.BeginStage("候选对照图准备");
            string folder = Path.Combine(Application.dataPath, "../Library/LxyUIEffectGenerator/UIEffectCodex");
            Directory.CreateDirectory(folder);
            // The first pass already created these exact full-resolution inputs.
            // Keep them alive until the entire workflow completes, fails or is canceled.
            string[] designs = pendingCodexImageInputs.ToArray();
            CodexSpriteCatalog catalog = CreateNodeCatalog(draft, pendingNodeEvidence, folder);
            pendingCodexImageInputs.AddRange(catalog.ImagePaths);
            string outputSchema = Path.Combine(folder, "FullFidelityResponse.schema.json");
            File.WriteAllText(outputSchema, BuildFullFidelityOutputSchema(), new UTF8Encoding(false));
            pendingCodexOutputPath = Path.Combine(folder, draft.name + "_Correction_" + Guid.NewGuid().ToString("N") + ".json");
            pendingCodexTask = CodexTaskKind.FullFidelity;
            UIEffectGenerationTiming.BeginStage("AI 资源与结构校正（2/2）");
            codexRunner.Start(new UIEffectCodexRequest
            {
                provider = "Codex 资源与结构校正 2/2", codexCommand = codexCommand,
                projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                imagePaths = pendingCodexImageInputs.ToArray(), outputSchemaPath = outputSchema,
                outputPath = pendingCodexOutputPath, reasoningEffort = "high",
                prompt = "你是第二阶段资源与结构校正器。下方附有第一阶段草案与节点专属证据。" +
                    "联系图的每行第一列是参考裁片，后续列是对应候选；布局以 manifest 为准。" +
                    "保持正确区域，依据独立边框、透明留边和完整轮廓修正矩形及层级。" +
                    "可合并同一连续底图的错误分片，也可从 Container probe 补回遗漏的 Image。" +
                    "路径只表示候选，不能因为局部颜色相似就锁定；无证据不删节点。" +
                    "修改父矩形或重新挂接时维持所有正确后代的绝对坐标。" +
                    "参考裁片只能用于观察，禁止当作 resource。\n" +
                    BuildFullFidelityPrompt(draft.name, pendingReferenceAssetPath,
                        pendingReferenceWidth, pendingReferenceHeight, designs.Length, catalog) +
                    "\n第一阶段 UISchema：\n" + UIEffectSchemaUtility.ToCompactJson(draft),
            });
            requestStatus = $"正在用 {pendingNodeEvidence.Count} 个节点的局部证据校正资源与结构（2/2）…";
        }

        internal static void MergeNodeEvidence(UIEffectSchema schema, List<UIEffectNodeEvidence> evidence)
        {
            MergeNodeEvidence(schema.children, 0, 0, string.Empty, evidence ?? new List<UIEffectNodeEvidence>());
        }

        private static void MergeNodeEvidence(List<UIEffectNode> nodes, float x, float y,
            string parentPath, List<UIEffectNodeEvidence> evidence)
        {
            foreach (UIEffectNode node in nodes)
            {
                string path = parentPath.Length == 0 ? node.name : parentPath + "/" + node.name;
                var rect = new Rect(x + node.x, y + node.y, node.width, node.height);
                UIEffectNodeEvidence local = evidence.FirstOrDefault(item => item.Path == path &&
                    SameEvidenceRect(item.Rect, rect));
                // Paths may change when the second pass corrects visual ownership.
                if (local == null)
                {
                    UIEffectNodeEvidence[] matches = evidence.Where(item => item.Node.name == node.name &&
                        SameEvidenceRect(item.Rect, rect)).Take(2).ToArray();
                    if (matches.Length == 1) local = matches[0];
                }
                bool colorFallback = string.Equals(node.resourcePolicy, "ColorFallback", StringComparison.OrdinalIgnoreCase);
                bool verified = string.Equals(node.resourcePolicy, "Verified", StringComparison.OrdinalIgnoreCase) &&
                    local != null && !local.RequiresReview && local.Accepted && local.Candidates.Count > 0 &&
                    local.ResourceRevision == UIEffectResourceResolver.ResourceRevision &&
                    string.Equals(local.Candidates[0].Resource, node.resource, StringComparison.Ordinal);
                if (colorFallback)
                {
                    node.resource = string.Empty;
                    node.resourceCandidates.Clear();
                    node.intentionalColor = true;
                }
                else if (!verified)
                {
                    var candidates = new List<string>();
                    if (!string.IsNullOrWhiteSpace(node.resource)) candidates.Add(node.resource);
                    candidates.AddRange(node.resourceCandidates);
                    if (local != null) candidates.AddRange(local.Candidates.Select(item => item.Resource));
                    node.resourceCandidates = candidates.Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.Ordinal).Take(24).ToList();
                    node.resource = string.Empty;
                    node.resourcePolicy = node.resourceCandidates.Count > 0 ? "Candidate" : "Auto";
                }
                else
                {
                    node.color = "#" + ColorUtility.ToHtmlStringRGBA(local.Candidates[0].Tint);
                    node.sliced = local.Candidates[0].Border.sqrMagnitude > 0;
                    if (node.sliced) node.preserveAspect = false;
                }
                MergeNodeEvidence(node.children, rect.x, rect.y, path, evidence);
            }
        }

        private static bool SameEvidenceRect(Rect first, Rect second)
        {
            return Mathf.Abs(first.x - second.x) < 0.5f && Mathf.Abs(first.y - second.y) < 0.5f &&
                   Mathf.Abs(first.width - second.width) < 0.5f && Mathf.Abs(first.height - second.height) < 0.5f;
        }

        private static CodexSpriteCatalog CreateNodeCatalog(UIEffectSchema draft,
            List<UIEffectNodeEvidence> evidence, string folder)
        {
            const int rowsPerPage = 4;
            var paths = new List<string>();
            var manifest = new StringBuilder("NODE EVIDENCE: 6 columns, up to 4 rows per page. " +
                "Column 1 = reference crop; columns 2..6 = candidate Sprite native aspect. " +
                "Each candidate cell: upper half = native Sprite, lower half = target-size UGUI rendering. " +
                "Transparent areas show the checkerboard; do not count it as part of the art.\n" +
                "Candidate rows are pipe-separated: rank|column (- = not shown)|exact resource path|" +
                "score|source WxH|border L,T,R,B|tint RGBA. All ranked candidates are retained.\n");
            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!reference.LoadImage(File.ReadAllBytes(UIEffectEditorUtility.ToAbsolutePath(draft.referenceImage))))
                    throw new InvalidOperationException("无法解码参考图。");
                for (int first = 0; first < evidence.Count; first += rowsPerPage)
                {
                    int page = first / rowsPerPage + 1;
                    List<UIEffectNodeEvidence> rows = evidence.Skip(first).Take(rowsPerPage).ToList();
                    string path = Path.Combine(folder, draft.name + "_NodeEvidence_" + page.ToString("00") + ".png");
                    WriteNodeEvidencePage(reference, rows, path);
                    paths.Add(path);
                    for (int row = 0; row < rows.Count; row++)
                    {
                        UIEffectNodeEvidence entry = rows[row];
                        manifest.AppendFormat(CultureInfo.InvariantCulture,
                            "page={0},row={1},path={2},rectXYWH={3},{4},{5},{6},probe={7},review={8}\n",
                            page, row + 1, entry.Path, entry.Rect.x, entry.Rect.y, entry.Rect.width,
                            entry.Rect.height, entry.ContainerProbe, entry.RequiresReview);
                        for (int c = 0; c < entry.Candidates.Count; c++)
                        {
                            UIEffectSpriteEvidence candidate = entry.Candidates[c];
                            manifest.AppendFormat(CultureInfo.InvariantCulture,
                                "{0}|{1}|{2}|{3:F4}|{4}x{5}|{6},{7},{8},{9}|#{10}\n", c + 1,
                                c < (entry.RequiresReview ? 5 : 1) ? (c + 2).ToString() : "-",
                                candidate.Resource, candidate.Score, candidate.SourceSize.x, candidate.SourceSize.y,
                                candidate.Border.x, candidate.Border.w, candidate.Border.z, candidate.Border.y,
                                ColorUtility.ToHtmlStringRGBA(candidate.Tint));
                        }
                    }
                }
                // Small, uncertain details additionally retain their exact source pixels.
                foreach (UIEffectNodeEvidence entry in evidence.Where(item => item.RequiresReview &&
                    item.Rect.width <= 1024 && item.Rect.height <= 1024)
                    .OrderBy(item => item.Candidates.Count == 0 ? 0 : item.Candidates[0].Score)
                    .ThenBy(item => item.Path, StringComparer.Ordinal).Take(12))
                {
                    RectInt crop = GetEvidenceCrop(reference, entry.Rect);
                    if (crop.width < 1 || crop.height < 1) continue;
                    var texture = new Texture2D(crop.width, crop.height, TextureFormat.RGB24, false);
                    try
                    {
                        texture.SetPixels(reference.GetPixels(crop.x, crop.y, crop.width, crop.height));
                        texture.Apply();
                        string path = Path.Combine(folder, draft.name + "_Detail_" + paths.Count.ToString("00") + ".png");
                        File.WriteAllBytes(path, texture.EncodeToPNG());
                        paths.Add(path);
                        manifest.AppendLine($"attachment={paths.Count},native-reference-only,path={entry.Path}," +
                            $"originXY={crop.x},{reference.height - crop.yMax},size={crop.width}x{crop.height}");
                    }
                    finally { DestroyImmediate(texture); }
                }
            }
            finally { DestroyImmediate(reference); }

            manifest.AppendLine("Available TMP fonts (exact paths; material must use the same font atlas):");
            foreach (string path in AssetDatabase.FindAssets("t:TMP_FontAsset")
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(item => item, StringComparer.Ordinal))
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null) continue;
                manifest.AppendLine("font=" + path + ",family=" + font.faceInfo.familyName +
                    ",style=" + font.faceInfo.styleName + ",material=" + AssetDatabase.GetAssetPath(font.material));
            }
            return new CodexSpriteCatalog { Manifest = manifest.ToString(), ImagePaths = paths.ToArray(),
                SpriteCount = evidence.Sum(item => Mathf.Min(item.RequiresReview ? 5 : 1, item.Candidates.Count)) };
        }

        private static RectInt GetEvidenceCrop(Texture2D reference, Rect rect)
        {
            int left = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, reference.width);
            int right = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), left, reference.width);
            int top = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, reference.height);
            int bottom = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), top, reference.height);
            return new RectInt(left, reference.height - bottom, right - left, bottom - top);
        }

        private static void WriteNodeEvidencePage(Texture2D reference,
            List<UIEffectNodeEvidence> rows, string path)
        {
            const int cell = 320;
            var sheet = new Texture2D(cell * 6, cell * rows.Count, TextureFormat.RGB24, false);
            var pixels = new Color[sheet.width * sheet.height];
            for (int y = 0; y < sheet.height; y++)
                for (int x = 0; x < sheet.width; x++)
                    pixels[y * sheet.width + x] = ((x / 12 + y / 12) % 2 == 0) ? new Color(.18f,.18f,.18f) : new Color(.3f,.3f,.3f);
            sheet.SetPixels(pixels);
            try
            {
                for (int row = 0; row < rows.Count; row++)
                {
                    UIEffectNodeEvidence entry = rows[row];
                    RectInt crop = GetEvidenceCrop(reference, entry.Rect);
                    BlitEvidence(sheet, reference, new Rect(crop.x, crop.y, crop.width, crop.height),
                        new Rect(4, sheet.height - (row + 1) * cell + 4, cell - 8, cell - 8));
                    if (entry.Candidates.Count == 0) continue;
                    using (var preview = new UIEffectPreviewRenderer(entry.Rect.width, entry.Rect.height, 320))
                    {
                        for (int c = 0; c < Mathf.Min(entry.RequiresReview ? 5 : 1, entry.Candidates.Count); c++)
                        {
                            UIEffectSpriteEvidence candidate = entry.Candidates[c];
                            Texture2D target = preview.RenderSprite(candidate.Sprite, candidate.Tint, true);
                            Texture2D native = preview.RenderSprite(candidate.Sprite, Color.white, false);
                            try
                            {
                                int bottom = sheet.height - (row + 1) * cell;
                                BlitEvidence(sheet, native, new Rect(0,0,native.width,native.height),
                                    new Rect((c + 1) * cell + 4,bottom + cell / 2 + 4,cell - 8,cell / 2 - 8));
                                BlitEvidence(sheet, target, new Rect(0,0,target.width,target.height),
                                    new Rect((c + 1) * cell + 4,bottom + 4,cell - 8,cell / 2 - 8));
                            }
                            finally { DestroyImmediate(target); DestroyImmediate(native); }
                        }
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(path, sheet.EncodeToPNG());
            }
            finally { DestroyImmediate(sheet); }
        }

        private static void BlitEvidence(Texture2D destination, Texture2D source, Rect sourceRect, Rect target)
        {
            if (sourceRect.width <= 0 || sourceRect.height <= 0) return;
            float scale = Mathf.Min(target.width / sourceRect.width, target.height / sourceRect.height);
            int width = Mathf.Max(1, Mathf.RoundToInt(sourceRect.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(sourceRect.height * scale));
            int left = Mathf.RoundToInt(target.center.x - width * .5f);
            int bottom = Mathf.RoundToInt(target.center.y - height * .5f);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color value = source.GetPixelBilinear((sourceRect.x + (x + .5f) * sourceRect.width / width) / source.width,
                        (sourceRect.y + (y + .5f) * sourceRect.height / height) / source.height);
                    Color backdrop = destination.GetPixel(left + x, bottom + y);
                    destination.SetPixel(left + x, bottom + y, Color.Lerp(backdrop, value, value.a));
                }
        }
    }
}
#endif
