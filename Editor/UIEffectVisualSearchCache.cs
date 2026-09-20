#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    internal sealed partial class UIEffectResourceResolver
    {
        // Cache only complete, unfiltered searches. A changed crop, mask, backdrop,
        // candidate order, resource index or scoring option must run the full scorer.
        private sealed class VisualSearchResult
        {
            internal byte[] Input;
            internal List<SpriteEntry> Index;
            internal bool Accepted;
            internal SpriteEntry Best;
            internal float Score, SecondScore;
            internal Color Color;
            internal List<VisualCandidate> Candidates;
        }

        private static readonly Dictionary<string, VisualSearchResult> VisualSearchCache =
            new Dictionary<string, VisualSearchResult>(StringComparer.Ordinal);
        internal static int VisualSearchCacheHits { get; private set; }
        internal static int VisualSearchCacheMisses { get; private set; }
        internal static bool EnableVisualSearchCache { get; set; } = true;

        private static void ClearVisualSearchCache()
        {
            VisualSearchCache.Clear();
            VisualSearchCacheHits = VisualSearchCacheMisses = 0;
        }

        private bool TryFindBestVisualMatch(VisualDescriptor source, bool preferBorder,
            bool requireBorder, bool textCovered, bool compactGraphic, bool shapeSensitive,
            string semanticHint, SpritePreviewRenderer renderer, out SpriteEntry best,
            out float bestScore, out float secondScore, out Color bestColor,
            Func<SpriteEntry, bool> candidateFilter = null, List<VisualCandidate> rankedCandidates = null,
            IReadOnlyList<string> preferredResources = null)
        {
            // Delegate closures may contain arbitrary mutable restrictions, so never memoize them.
            if (!EnableVisualSearchCache || candidateFilter != null)
                return FindBestVisualMatchUncached(source, preferBorder, requireBorder, textCovered,
                    compactGraphic, shapeSensitive, semanticHint, renderer, out best, out bestScore,
                    out secondScore, out bestColor, candidateFilter, rankedCandidates, preferredResources);

            byte[] input = GetVisualSearchInput(source, preferBorder, requireBorder, textCovered,
                compactGraphic, shapeSensitive, semanticHint, excludedReferencePath, preferredResources);
            string key;
            using (var hash = SHA256.Create()) key = Convert.ToBase64String(hash.ComputeHash(input));
            if (VisualSearchCache.TryGetValue(key, out VisualSearchResult cached) &&
                ReferenceEquals(cached.Index, sprites) && cached.Input.SequenceEqual(input))
            {
                VisualSearchCacheHits++;
                best = cached.Best;
                bestScore = cached.Score;
                secondScore = cached.SecondScore;
                bestColor = cached.Color;
                if (rankedCandidates != null) rankedCandidates.AddRange(cached.Candidates.Select(CloneVisualCandidate));
                return cached.Accepted;
            }

            VisualSearchCacheMisses++;
            var candidates = new List<VisualCandidate>();
            bool accepted = FindBestVisualMatchUncached(source, preferBorder, requireBorder, textCovered,
                compactGraphic, shapeSensitive, semanticHint, renderer, out best, out bestScore,
                out secondScore, out bestColor, null, candidates, preferredResources);
            // Bounded lifetime/memory; texture imports and explicit rescans also clear this cache.
            if (VisualSearchCache.Count >= 128) VisualSearchCache.Clear();
            VisualSearchCache[key] = new VisualSearchResult { Input = input, Index = sprites,
                Accepted = accepted, Best = best, Score = bestScore, SecondScore = secondScore,
                Color = bestColor, Candidates = candidates };
            if (rankedCandidates != null) rankedCandidates.AddRange(candidates.Select(CloneVisualCandidate));
            return accepted;
        }

        private static VisualCandidate CloneVisualCandidate(VisualCandidate value)
        {
            return new VisualCandidate(value.Entry, value.Descriptor, value.AspectSimilarity,
                value.QuickScore, value.SemanticBonus, value.SlicedScalePenalty)
            {
                AuthoredScore = value.AuthoredScore, AuthoredColorDistance = value.AuthoredColorDistance,
                CompositeScore = value.CompositeScore, Score = value.Score,
                MatchedColor = value.MatchedColor, UsesMaterialTint = value.UsesMaterialTint,
            };
        }

        private static byte[] GetVisualSearchInput(VisualDescriptor source, bool preferBorder,
            bool requireBorder, bool textCovered, bool compactGraphic, bool shapeSensitive,
            string semanticHint, string referencePath, IReadOnlyList<string> preferredResources)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(ResourceRevision);
                writer.Write((int)QualitySettings.activeColorSpace);
                writer.Write(preferBorder); writer.Write(requireBorder); writer.Write(textCovered);
                writer.Write(compactGraphic); writer.Write(shapeSensitive);
                WriteSearchString(writer, semanticHint); WriteSearchString(writer, referencePath);
                writer.Write(preferredResources?.Count ?? -1);
                if (preferredResources != null)
                    foreach (string path in preferredResources) WriteSearchString(writer, path);
                writer.Write(source.Aspect); writer.Write(source.Width); writer.Write(source.Height);
                writer.Write(source.IsSliced); writer.Write(source.HasReferenceBackdrop);
                writer.Write(source.HasSpatialReferenceBackdrop);
                WriteSearchColor(writer, source.ReferenceBackdrop);
                WriteSearchPixels(writer, source.Pixels);
                WriteSearchPixels(writer, source.ReferenceBackdropPixels);
                writer.Write(source.Mask?.Length ?? -1);
                if (source.Mask != null) foreach (bool value in source.Mask) writer.Write(value);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteSearchString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null) writer.Write(value);
        }

        private static void WriteSearchPixels(BinaryWriter writer, Color[] values)
        {
            writer.Write(values?.Length ?? -1);
            if (values != null) foreach (Color value in values) WriteSearchColor(writer, value);
        }

        private static void WriteSearchColor(BinaryWriter writer, Color value)
        {
            writer.Write(value.r); writer.Write(value.g); writer.Write(value.b); writer.Write(value.a);
        }
    }
}
#endif
