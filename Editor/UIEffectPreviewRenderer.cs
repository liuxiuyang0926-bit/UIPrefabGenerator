#if UNITY_EDITOR
using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Lxy.UIEffectGenerator.Editor
{
    // Owns an isolated preview scene. No scene objects, active camera, selection
    // or user's Canvas settings are changed by candidate/final rendering.
    internal sealed class UIEffectPreviewRenderer : IDisposable
    {
        private readonly Scene scene;
        private readonly Camera camera;
        private readonly RenderTexture target;
        internal readonly RectTransform Root;
        private GameObject content;

        internal UIEffectPreviewRenderer(float width, float height, int maximumSize = 2048,
            float referencePixelsPerUnit = 100f)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            scene = EditorSceneManager.NewPreviewScene();
            var rootObject = new GameObject("UIEffectPreview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            Root = rootObject.GetComponent<RectTransform>();
            Root.sizeDelta = new Vector2(width, height);
            Canvas canvas = rootObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.referencePixelsPerUnit = referencePixelsPerUnit;
            var cameraObject = new GameObject("UIEffectPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = height * .5f;
            camera.aspect = width / height;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 20f;
            camera.transform.position = new Vector3(0, 0, -10);
            canvas.worldCamera = camera;
            float scale = Mathf.Min(1, maximumSize / Mathf.Max(width, height));
            target = new RenderTexture(Mathf.Max(1, Mathf.RoundToInt(width * scale)),
                Mathf.Max(1, Mathf.RoundToInt(height * scale)), 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.antiAliasing = 1;
            camera.targetTexture = target;
        }

        internal RectTransform SetGenerated(RectTransform generated)
        {
            ClearContent();
            content = UnityEngine.Object.Instantiate(generated.gameObject, Root, false);
            content.SetActive(true);
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = Root.sizeDelta;
            return rect;
        }

        internal Texture2D RenderSprite(Sprite sprite, Color tint, bool targetSize)
        {
            ClearContent();
            content = new GameObject("Candidate", typeof(RectTransform), typeof(Image));
            content.transform.SetParent(Root, false);
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.sizeDelta = Root.sizeDelta;
            Image image = content.GetComponent<Image>();
            image.sprite = sprite;
            image.color = tint;
            image.type = targetSize && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = !targetSize;
            return Render();
        }

        internal Texture2D Render()
        {
            Canvas.ForceUpdateCanvases();
            RenderTexture previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                // Camera RGB is premultiplied against the clear target. PNG and
                // contact-sheet composition require straight-alpha colors.
                Color[] pixels = image.GetPixels();
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color value = pixels[i];
                    if (value.a <= .001f) { pixels[i] = Color.clear; continue; }
                    if (linear)
                    {
                        value.r = Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(value.r) / value.a));
                        value.g = Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(value.g) / value.a));
                        value.b = Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(value.b) / value.a));
                    }
                    else
                    {
                        value.r = Mathf.Clamp01(value.r / value.a);
                        value.g = Mathf.Clamp01(value.g / value.a);
                        value.b = Mathf.Clamp01(value.b / value.a);
                    }
                    pixels[i] = value;
                }
                image.SetPixels(pixels);
                image.Apply();
                return image;
            }
            catch
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                throw;
            }
            finally { RenderTexture.active = previous; }
        }

        private void ClearContent()
        {
            if (content != null) UnityEngine.Object.DestroyImmediate(content);
        }

        public void Dispose()
        {
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
