#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Lxy.UIEffectGenerator.Editor
{
    [Serializable]
    internal sealed class UIEffectFidelityReport
    {
        public string prefab;
        public string referenceHash;
        public string preview;
        public string imageOnlyPreview;
        public string difference;
        public string scope = "Generated 中可见且不透明的图像像素；排除文字排版区域。分数不是整图还原率。文字另行检查。";
        public int reviewCount;
        public List<string> warnings = new List<string>();
        public List<UIEffectFidelityNodeReport> nodes = new List<UIEffectFidelityNodeReport>();
    }

    [Serializable]
    internal sealed class UIEffectFidelityNodeReport
    {
        public string path;
        public string type;
        public Rect rect;
        public string resource;
        public float colorError;
        public float edgeError;
        public int samples;
        public string issue;
    }

    internal static class UIEffectFidelityAudit
    {
        internal static string Create(string prefabPath, UIEffectSchema schema, out int reviewCount,
            IReadOnlyList<string> generationWarnings = null)
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Library/LxyUIEffectGenerator/Reports", UIEffectEditorUtility.SanitizeTypeName(schema.name)));
            Directory.CreateDirectory(folder);
            var report = new UIEffectFidelityReport { prefab = prefabPath, referenceHash = schema.referenceImageHash,
                preview = Path.Combine(folder, "Preview.png"), imageOnlyPreview = Path.Combine(folder, "Images.png"),
                difference = Path.Combine(folder, "Difference.png") };
            if (generationWarnings != null) report.warnings.AddRange(generationWarnings);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            RectTransform generated = prefab.transform.Find("Generated") as RectTransform;
            if (generated == null) throw new InvalidOperationException("视觉审计找不到 Generated。");
            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D full = null, images = null, difference = null;
            try
            {
                if (!reference.LoadImage(File.ReadAllBytes(UIEffectEditorUtility.ToAbsolutePath(schema.referenceImage))))
                    throw new InvalidOperationException("视觉审计参考图无效。");
                File.WriteAllBytes(Path.Combine(folder, "Reference.png"), reference.EncodeToPNG());
                Canvas sourceCanvas = prefab.GetComponent<Canvas>();
                using (var renderer = new UIEffectPreviewRenderer(schema.designWidth, schema.designHeight, 2048,
                    sourceCanvas == null ? 100 : sourceCanvas.referencePixelsPerUnit))
                {
                    RectTransform root = renderer.SetGenerated(generated);
                    TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(false);
                    foreach (TMP_Text text in texts) text.ForceMeshUpdate();
                    full = renderer.Render();
                    File.WriteAllBytes(report.preview, full.EncodeToPNG());
                    var textRects = new List<Rect>();
                    foreach (TMP_Text text in texts)
                    {
                        Rect rect = DesignRect(root, text.rectTransform, schema);
                        textRects.Add(rect);
                        string issue = string.Empty;
                        if (text.font == null || (!string.IsNullOrWhiteSpace(text.text) &&
                            text.textInfo.characterCount == 0)) issue = "字体缺失或没有生成字形";
                        else if (text.preferredHeight > text.rectTransform.rect.height + 3 ||
                            (!text.enableWordWrapping && text.preferredWidth > text.rectTransform.rect.width + 3))
                            issue = "文字超出排版矩形，请复核字体、字号、换行和基线";
                        report.nodes.Add(new UIEffectFidelityNodeReport { path = PathOf(root, text.transform),
                            type = "Text", rect = rect, resource = AssetDatabase.GetAssetPath(text.font), issue = issue });
                        text.enabled = false;
                    }
                    images = renderer.Render();
                    File.WriteAllBytes(report.imageOnlyPreview, images.EncodeToPNG());
                    difference = new Texture2D(images.width, images.height, TextureFormat.RGB24, false);
                    difference.SetPixels(new Color[images.width * images.height]);
                    Image[] graphics = root.GetComponentsInChildren<Image>(false);
                    for (int i = 0; i < graphics.Length; i++)
                    {
                        Image graphic = graphics[i];
                        if (!graphic.enabled || graphic.color.a <= .001f) continue;
                        Rect rect = DesignRect(root, graphic.rectTransform, schema);
                        var excluded = new List<Rect>(textRects);
                        for (int later = i + 1; later < graphics.Length; later++)
                            if (graphics[later].enabled && graphics[later].color.a > .01f)
                                excluded.Add(DesignRect(root, graphics[later].rectTransform, schema));
                        UIEffectFidelityNodeReport item = CompareRegion(reference, images, difference, rect,
                            excluded, schema.designWidth, schema.designHeight);
                        item.path = PathOf(root, graphic.transform);
                        item.type = "Image";
                        item.resource = graphic.sprite == null ? string.Empty : AssetDatabase.GetAssetPath(graphic.sprite);
                        report.nodes.Add(item);
                    }
                }
                difference.Apply();
                File.WriteAllBytes(report.difference, difference.EncodeToPNG());
                report.reviewCount = report.nodes.Count(item => !string.IsNullOrEmpty(item.issue));
                reviewCount = report.reviewCount;
                string reportPath = Path.Combine(folder, "Report.json");
                File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
                WriteComparisonHtml(folder, report);
                return reportPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(reference);
                if (full != null) UnityEngine.Object.DestroyImmediate(full);
                if (images != null) UnityEngine.Object.DestroyImmediate(images);
                if (difference != null) UnityEngine.Object.DestroyImmediate(difference);
            }
        }

        internal static UIEffectFidelityNodeReport CompareRegion(Texture2D reference, Texture2D actual,
            Texture2D heatmap, Rect rect, List<Rect> excluded, float designWidth, float designHeight)
        {
            var item = new UIEffectFidelityNodeReport { rect = rect };
            float sx = actual.width / designWidth, sy = actual.height / designHeight;
            int left = Mathf.Clamp(Mathf.FloorToInt(rect.xMin * sx), 0, actual.width);
            int right = Mathf.Clamp(Mathf.CeilToInt(rect.xMax * sx), 0, actual.width);
            int top = Mathf.Clamp(Mathf.FloorToInt(rect.yMin * sy), 0, actual.height);
            int bottom = Mathf.Clamp(Mathf.CeilToInt(rect.yMax * sy), 0, actual.height);
            int stride = Mathf.Max(1, Mathf.FloorToInt(Mathf.Sqrt((right - left) * (bottom - top) / 24000f)));
            int edgeSamples = 0;
            for (int y = top; y < bottom; y += stride)
                for (int x = left; x < right; x += stride)
                {
                    var point = new Vector2((x + .5f) / sx, (y + .5f) / sy);
                    if (ContainsPoint(excluded, point)) continue;
                    Color pixel = actual.GetPixel(x, actual.height - y - 1);
                    if (pixel.a < .98f) continue; // Unknown host pixels cannot be graded honestly.
                    Color expected = reference.GetPixelBilinear(point.x / designWidth, 1 - point.y / designHeight);
                    float error = (Mathf.Abs(pixel.r - expected.r) + Mathf.Abs(pixel.g - expected.g) +
                                   Mathf.Abs(pixel.b - expected.b)) / 3f;
                    item.colorError += error;
                    item.samples++;
                    bool edge = x - left < 4 || right - x <= 4 || y - top < 4 || bottom - y <= 4;
                    if (edge) { item.edgeError += error; edgeSamples++; }
                    if (heatmap != null) heatmap.SetPixel(x, actual.height - y - 1, new Color(Mathf.Clamp01(error * 4), 0, 0));
                }
            if (item.samples > 0) item.colorError /= item.samples;
            if (edgeSamples > 0) item.edgeError /= edgeSamples;
            if (item.samples < 16) item.issue = "可见不透明样本不足，需目视复核";
            else if (item.colorError > .075f || item.edgeError > .12f)
                item.issue = "像素或边缘差异偏大，请复核资源、颜色与矩形";
            return item;
        }

        private static bool ContainsPoint(List<Rect> regions, Vector2 point)
        {
            for (int i = 0; i < regions.Count; i++)
                if (regions[i].Contains(point)) return true;
            return false;
        }

        private static void WriteComparisonHtml(string folder, UIEffectFidelityReport report)
        {
            var html = new StringBuilder("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\">" +
                "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>UI 还原检查</title>" +
                "<style>body{font:15px/1.6 system-ui;background:#171b23;color:#edf0f5;margin:24px}" +
                ".images{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}" +
                "figure{margin:0}img{width:100%;background:repeating-conic-gradient(#333 0% 25%,#444 0% 50%) 0/16px 16px}" +
                "table{border-collapse:collapse;width:100%;margin-top:24px}td,th{padding:8px;text-align:left;border-bottom:1px solid #444}" +
                "a{color:#99caff}@media(max-width:900px){.images{grid-template-columns:1fr}}</style>");
            html.Append("<h1>UI 还原检查</h1><p>").Append(WebUtility.HtmlEncode(report.prefab))
                .Append("</p><p>待复核：").Append(report.reviewCount).Append(" 个节点。")
                .Append(WebUtility.HtmlEncode(report.scope)).Append("</p>");
            if (report.warnings.Count > 0)
            {
                html.Append("<h2>资源与字体提示</h2><ul>");
                foreach (string warning in report.warnings)
                    html.Append("<li>").Append(WebUtility.HtmlEncode(warning)).Append("</li>");
                html.Append("</ul>");
            }
            html.Append("<div class=\"images\">");
            foreach (var item in new[] { new[] { "Reference.png", "原始效果图" },
                new[] { "Preview.png", "实际生成结果（包含文字）" }, new[] { "Difference.png", "可比较区域差异（红色越亮差异越大）" } })
                html.Append("<figure><figcaption>").Append(item[1]).Append("</figcaption><a href=\"")
                    .Append(item[0]).Append("\"><img src=\"").Append(item[0]).Append("\"></a></figure>");
            html.Append("</div><p><a href=\"Images.png\">仅图像预览</a> · <a href=\"Report.json\">完整节点数据</a></p>")
                .Append("<table><tr><th>节点</th><th>需要复核</th></tr>");
            foreach (UIEffectFidelityNodeReport item in report.nodes.Where(node => !string.IsNullOrEmpty(node.issue)))
                html.Append("<tr><td>").Append(WebUtility.HtmlEncode(item.path)).Append("</td><td>")
                    .Append(WebUtility.HtmlEncode(item.issue)).Append("</td></tr>");
            html.Append("</table></html>");
            File.WriteAllText(Path.Combine(folder, "Index.html"), html.ToString());
        }

        private static Rect DesignRect(RectTransform root, RectTransform rect, UIEffectSchema schema)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 min = root.InverseTransformPoint(corners[0]);
            Vector3 max = root.InverseTransformPoint(corners[2]);
            return new Rect(min.x + schema.designWidth * .5f, schema.designHeight * .5f - max.y,
                max.x - min.x, max.y - min.y);
        }

        private static string PathOf(Transform root, Transform target)
        {
            string path = target.name;
            while (target.parent != null && target.parent != root)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }
    }
}
#endif
