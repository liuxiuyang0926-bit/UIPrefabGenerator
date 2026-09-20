#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    internal sealed partial class UIEffectResourceResolver
    {
        internal static bool EnableParallelScoring { get; set; } = true;
        private static readonly ParallelOptions ScoringOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Math.Min(8, Environment.ProcessorCount - 2)),
        };

        // Workers only read managed pixel arrays and numeric metadata. All Unity object access,
        // rendering, collection changes, ranking and tie-breaking remain on the editor thread.
        // Each candidate keeps its original pixel accumulation order, including float rounding.
        private static void ScoreCandidates(int count, Action<int> score)
        {
            if (EnableParallelScoring && count >= 32 && ScoringOptions.MaxDegreeOfParallelism > 1)
                Parallel.For(0, count, ScoringOptions, score);
            else
                for (int i = 0; i < count; i++) score(i);
        }

        private static void ScoreQuickCandidates(VisualDescriptor source,
            List<VisualCandidate> candidates, bool textCovered)
        {
            ScoreCandidates(candidates.Count, i =>
            {
                VisualCandidate candidate = candidates[i];
                float score = GetQuickVisualScore(source, candidate.Descriptor, candidate.AspectSimilarity);
                if (textCovered)
                    score = Mathf.Max(score, GetTextCoveredQuickScore(source,
                        candidate.Descriptor, candidate.AspectSimilarity));
                candidate.QuickScore = score + candidate.SemanticBonus - candidate.SlicedScalePenalty;
            });
        }

        private static void ScoreRefinementCandidates(VisualDescriptor source,
            List<VisualCandidate> candidates, bool preferBorder, bool textCovered)
        {
            ScoreCandidates(candidates.Count, i =>
            {
                VisualCandidate candidate = candidates[i];
                float compositeScore = CompareCompositeDescriptors(source, candidate.Descriptor,
                    candidate.AspectSimilarity, preferBorder, textCovered, out Color candidateColor);
                compositeScore -= GetTintTransformationPenalty(candidateColor);
                float authoredScore = CompareDescriptors(source, candidate.Descriptor,
                    candidate.AspectSimilarity, preferBorder);
                if (source.HasSpatialReferenceBackdrop && HasAlphaVariation(candidate.Descriptor))
                    authoredScore = Mathf.Min(authoredScore, compositeScore);
                candidate.AuthoredScore = Mathf.Clamp01(authoredScore + candidate.SemanticBonus - candidate.SlicedScalePenalty);
                candidate.AuthoredColorDistance = GetMeanColorDistance(source, candidate.Descriptor, preferBorder);
                candidate.CompositeScore = Mathf.Clamp01(compositeScore + candidate.SemanticBonus - candidate.SlicedScalePenalty);
                if (authoredScore > compositeScore)
                {
                    candidate.Score = candidate.AuthoredScore;
                    candidate.MatchedColor = Color.white;
                }
                else
                {
                    candidate.Score = candidate.CompositeScore;
                    candidate.MatchedColor = candidateColor;
                }
                candidate.UsesMaterialTint = candidate.CompositeScore > candidate.AuthoredScore &&
                    GetTintTransformationDistance(candidateColor) >= MaterialTintDistance;
            });
        }
    }
}
#endif
