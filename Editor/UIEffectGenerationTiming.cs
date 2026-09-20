#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    [Serializable]
    internal sealed class UIEffectGenerationStageTiming
    {
        public string name;
        public double seconds;
    }

    [Serializable]
    internal sealed class UIEffectGenerationTimingRecord
    {
        public string panel;
        public string completedAt;
        public string status;
        public double totalSeconds;
        public double buildSeconds;
        public List<UIEffectGenerationStageTiming> stages = new List<UIEffectGenerationStageTiming>();
    }

    [FilePath("Library/LxyUIEffectGenerator/GenerationHistory.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class UIEffectGenerationTiming : ScriptableSingleton<UIEffectGenerationTiming>
    {
        [SerializeField] private List<UIEffectGenerationTimingRecord> history = new List<UIEffectGenerationTimingRecord>();
        private static Stopwatch workflow;
        private static Stopwatch build;
        private static string panel;
        private static readonly List<UIEffectGenerationStageTiming> stages = new List<UIEffectGenerationStageTiming>();
        private static string currentStage;
        private static double stageStarted;

        internal static bool IsRunning => workflow != null;
        internal static double ElapsedSeconds => workflow?.Elapsed.TotalSeconds ?? 0;
        internal static double BuildSeconds => build?.Elapsed.TotalSeconds ?? 0;
        internal IReadOnlyList<UIEffectGenerationTimingRecord> History => history;
        internal static string CurrentStage => currentStage;
        internal static double CurrentStageSeconds => IsRunning ? ElapsedSeconds - stageStarted : 0;
        internal static IReadOnlyList<UIEffectGenerationStageTiming> Stages => stages;

        internal static void BeginWorkflow(string panelId)
        {
            if (IsRunning) Finish("已中断");
            panel = panelId;
            stages.Clear();
            currentStage = null;
            stageStarted = 0;
            workflow = Stopwatch.StartNew();
        }

        internal static void BeginStage(string name)
        {
            if (!IsRunning) return;
            double now = ElapsedSeconds;
            if (!string.IsNullOrEmpty(currentStage))
                stages.Add(new UIEffectGenerationStageTiming { name = currentStage, seconds = now - stageStarted });
            currentStage = name;
            stageStarted = now;
        }

        internal static void BeginBuild(string panelId)
        {
            if (!IsRunning) BeginWorkflow(panelId);
            panel = panelId;
            build = Stopwatch.StartNew();
            BeginStage("Unity 构建、匹配与检查");
        }

        internal static void Finish(string status, string panelId = null)
        {
            if (!IsRunning) return;
            BeginStage(null);
            workflow.Stop();
            build?.Stop();
            var record = new UIEffectGenerationTimingRecord
            {
                panel = panelId ?? panel, completedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                status = status, totalSeconds = ElapsedSeconds, buildSeconds = BuildSeconds,
                stages = new List<UIEffectGenerationStageTiming>(stages),
            };
            workflow = null;
            build = null;
            instance.history.Insert(0, record);
            if (instance.history.Count > 20) instance.history.RemoveRange(20, instance.history.Count - 20);
            instance.Save(true);
        }

        internal static string Format(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (time.TotalHours >= 1) return $"{(int)time.TotalHours} 小时 {time.Minutes:00} 分 {time.Seconds:00} 秒";
            if (time.TotalMinutes >= 1) return $"{(int)time.TotalMinutes} 分 {time.Seconds:00}.{time.Milliseconds / 100} 秒";
            return $"{time.TotalSeconds:F2} 秒";
        }
    }

    public sealed partial class UIEffectPrefabGeneratorWindow
    {
        [SerializeField] private bool showGenerationHistory;
        private GUIStyle elapsedTimeStyle;
        private Vector2 generationHistoryScroll;

        private void DrawElapsedTime(string label, double seconds)
        {
            elapsedTimeStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.boldLabel, GUILayout.Width(240));
                GUILayout.Label(UIEffectGenerationTiming.Format(seconds), elapsedTimeStyle);
            }
        }

        private void DrawGenerationTiming()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (UIEffectGenerationTiming.IsRunning)
                {
                    EditorGUILayout.LabelField("生成耗时", "生成中，完成后显示总耗时");
                    return;
                }
                var records = UIEffectGenerationTiming.instance.History;
                if (records.Count == 0)
                {
                    EditorGUILayout.LabelField("生成耗时", "生成完成后显示总耗时");
                    return;
                }
                var latest = records[0];
                DrawElapsedTime("生成总耗时 · " + latest.status, latest.totalSeconds);
                EditorGUILayout.LabelField(latest.panel + "  ·  " + latest.completedAt, EditorStyles.miniLabel);
                showGenerationHistory = EditorGUILayout.Foldout(showGenerationHistory,
                    $"最近 {records.Count} 次记录（总耗时包含 AI 分析）", true);
                if (showGenerationHistory)
                {
                    generationHistoryScroll = EditorGUILayout.BeginScrollView(generationHistoryScroll,
                        GUILayout.Height(Mathf.Min(records.Count * 38, 180)));
                    foreach (var record in records)
                    {
                        EditorGUILayout.LabelField(record.completedAt + "  " + record.panel, EditorStyles.miniLabel);
                        EditorGUILayout.LabelField(record.status + "  ·  总耗时 " + UIEffectGenerationTiming.Format(record.totalSeconds),
                            EditorStyles.wordWrappedMiniLabel);
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }
    }
}
#endif
