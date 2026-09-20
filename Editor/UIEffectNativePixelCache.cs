#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    internal sealed partial class UIEffectResourceResolver
    {
        private sealed class NativePixels
        {
            internal Color32[] Pixels;
            internal int Size;
            internal ColorSpace ColorSpace;
        }

        // A sliced Sprite always renders the same native 256x256 image. Target-size slicing
        // happens afterwards in managed sampling, so different node sizes can share this image.
        // RGBA32 readback is lossless here; avoid retaining a four-times-larger Color[] per Sprite.
        private const long NativePixelBudget = 192L * 1024 * 1024;
        private static readonly Dictionary<Sprite, NativePixels> NativePixelCache = new Dictionary<Sprite, NativePixels>();
        private static readonly Queue<Sprite> NativePixelOrder = new Queue<Sprite>();
        internal static bool EnableNativePixelCache { get; set; } = true;
        internal static long NativePixelBytes { get; private set; }
        internal static int NativePixelCacheHits { get; private set; }
        private static Color[] nativeByteValues;
        private static ColorSpace nativeByteColorSpace;

        private static void PrepareNativeColorLookup()
        {
            if (nativeByteValues != null && nativeByteColorSpace == QualitySettings.activeColorSpace) return;
            var texture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            try
            {
                var bytes = new Color32[256];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = new Color32((byte)i, (byte)i, (byte)i, (byte)i);
                texture.SetPixels32(bytes);
                texture.Apply(false, false);
                // Unity's GetPixels conversion and Color32's implicit conversion differ by
                // a float rounding bit for some bytes. Preserve the original native conversion.
                nativeByteValues = texture.GetPixels();
                nativeByteColorSpace = QualitySettings.activeColorSpace;
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        internal static Color DecodeNativeColor(Color32 value)
        {
            if (nativeByteValues == null) PrepareNativeColorLookup();
            return new Color(nativeByteValues[value.r].r, nativeByteValues[value.g].g,
                nativeByteValues[value.b].b, nativeByteValues[value.a].a);
        }

        private static void ClearNativePixelCache()
        {
            NativePixelCache.Clear();
            NativePixelOrder.Clear();
            NativePixelBytes = 0;
            NativePixelCacheHits = 0;
        }

        private static bool TryGetNativePixels(Sprite sprite, int size, out Color32[] pixels)
        {
            pixels = null;
            if (!EnableNativePixelCache || !NativePixelCache.TryGetValue(sprite, out NativePixels cached) ||
                cached.Size != size || cached.ColorSpace != QualitySettings.activeColorSpace) return false;
            pixels = cached.Pixels;
            NativePixelCacheHits++;
            return true;
        }

        private static void CacheNativePixels(Sprite sprite, int size, Color32[] pixels)
        {
            // Simple Sprite descriptors already have a size-independent cache. Reserve this
            // larger cache for sliced sources that would otherwise be rendered for every size.
            if (!EnableNativePixelCache || sprite.border.sqrMagnitude <= 0) return;
            if (NativePixelCache.TryGetValue(sprite, out NativePixels previous))
            {
                NativePixelBytes -= previous.Pixels.LongLength * 4;
                NativePixelCache.Remove(sprite);
            }
            long bytes = pixels.LongLength * 4;
            if (bytes > NativePixelBudget) return;
            while (NativePixelBytes + bytes > NativePixelBudget && NativePixelOrder.Count > 0)
            {
                Sprite oldest = NativePixelOrder.Dequeue();
                if (!NativePixelCache.TryGetValue(oldest, out NativePixels removed)) continue;
                NativePixelBytes -= removed.Pixels.LongLength * 4;
                NativePixelCache.Remove(oldest);
            }
            NativePixelCache[sprite] = new NativePixels
            {
                Pixels = pixels, Size = size, ColorSpace = QualitySettings.activeColorSpace,
            };
            NativePixelOrder.Enqueue(sprite);
            NativePixelBytes += bytes;
        }

        internal sealed class NativePixelWarmup : IDisposable
        {
            private readonly SpriteEntry[] entries;
            private readonly int revision;
            private readonly ColorSpace colorSpace;
            private SpritePreviewRenderer renderer;
            internal int Prepared { get; private set; }
            internal int Total => entries.Length;
            internal bool IsCompleted { get; private set; }

            internal NativePixelWarmup(string[] roots)
            {
                entries = GetOrBuildSpriteIndex(roots).Where(entry => entry.HasBorder).ToArray();
                revision = ResourceRevision;
                colorSpace = QualitySettings.activeColorSpace;
            }

            // Editor-thread time slices overlap the remote AI request, never a background Unity API call.
            internal void Tick(double milliseconds = 8)
            {
                if (IsCompleted) return;
                if (revision != ResourceRevision || colorSpace != QualitySettings.activeColorSpace)
                {
                    Dispose();
                    return;
                }
                var watch = Stopwatch.StartNew();
                while (Prepared < entries.Length)
                {
                    SpriteEntry entry = entries[Prepared++];
                    if (entry.Sprite != null && !TryGetNativePixels(entry.Sprite, 256, out _))
                    {
                        renderer ??= new SpritePreviewRenderer(VisualSampleSize);
                        // Only native pixels are cached. Target-dependent descriptors are still
                        // computed later with the exact measured node rectangle and occlusion.
                        renderer.Render(entry.Sprite, entry.AuthoredWidth, entry.AuthoredHeight,
                            true, entry.AuthoredWidth, entry.AuthoredHeight);
                    }
                    if (watch.Elapsed.TotalMilliseconds >= milliseconds) break;
                }
                if (Prepared >= entries.Length) Dispose();
            }

            public void Dispose()
            {
                renderer?.Dispose();
                renderer = null;
                IsCompleted = true;
            }
        }
    }

    public sealed partial class UIEffectPrefabGeneratorWindow
    {
        private UIEffectResourceResolver.NativePixelWarmup nativePixelWarmup;

        private void StartNativePixelWarmup()
        {
            StopNativePixelWarmup();
            try { nativePixelWarmup = new UIEffectResourceResolver.NativePixelWarmup(pendingGenerationOptions.resourceSearchRoots); }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("UI 资源预计算未启动，将在匹配阶段正常计算：" + exception.Message);
            }
        }

        private void TickNativePixelWarmup()
        {
            if (nativePixelWarmup == null || nativePixelWarmup.IsCompleted) return;
            try { nativePixelWarmup.Tick(); }
            catch (Exception exception)
            {
                StopNativePixelWarmup();
                UnityEngine.Debug.LogWarning("UI 资源预计算已停止，将在匹配阶段正常计算：" + exception.Message);
            }
        }

        private void StopNativePixelWarmup()
        {
            nativePixelWarmup?.Dispose();
            nativePixelWarmup = null;
        }
    }
}
#endif
