using System;
using System.IO;
using System.Text;
using Dogfight.Ai;
using Dogfight.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Dogfight.EditorTools
{
    /// <summary>
    /// 批量对战窗口 —— 立项书要求的「AI 自动对战测试框架（批量对战 + 胜率统计）」的入口。
    ///
    /// W0 阶段跑的是**空对局**：不创建飞机、不做物理步进，只驱动 MatchLoop 的
    /// Tick / 计时 / 结算 / 统计这条链路。目的是先把"通道"打通并验证：
    ///   一局能被推进 → 能判定结束 → 结果能汇总 → 能落成 CSV
    /// 等行为树落地（⑤ 的后半段），这里换成真实飞机即可，接口不变。
    ///
    /// 用法：菜单 Dogfight → 批量对战。
    /// 输出：tools/ai-benchmark/results/batch-时间戳.csv
    /// </summary>
    public sealed class BatchBattleWindow : EditorWindow
    {
        const string ResultsRelativeDir = "tools/ai-benchmark/results";

        [SerializeField] int _matchCount = 100;
        [SerializeField] float _timeLimitSeconds = 180f;
        [SerializeField] uint _seedStart = 1u;
        [SerializeField] bool _stopOnAbort = true;

        string _lastSummary = "(还没跑过)";
        string _lastCsvPath = "";

        [MenuItem("Dogfight/批量对战", priority = 20)]
        public static void Open()
        {
            var window = GetWindow<BatchBattleWindow>(false, "批量对战", true);
            window.minSize = new Vector2(420f, 260f);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("AI 批量对战（W0：通道验证版）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "当前跑的是空对局：只验证「推进 → 结算 → 统计 → CSV」这条链路。\n" +
                "行为树与真实飞机会在后续接入，本窗口不用改。",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            _matchCount = EditorGUILayout.IntSlider("局数", _matchCount, 1, 2000);
            _timeLimitSeconds = EditorGUILayout.FloatField("单局时长上限（秒）", _timeLimitSeconds);
            _seedStart = (uint)Mathf.Max(0, EditorGUILayout.IntField("起始随机种子", (int)_seedStart));
            _stopOnAbort = EditorGUILayout.Toggle("出现中止局就停下", _stopOnAbort);

            EditorGUILayout.Space(8f);

            bool inPlayMode = EditorApplication.isPlaying;

            using (new EditorGUI.DisabledScope(inPlayMode))
            {
                if (GUILayout.Button(inPlayMode ? "播放模式下无法运行（请先退出播放）" : "开始批量对战", GUILayout.Height(32f)))
                {
                    RunBatch();
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("上次结果", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(_lastSummary, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(40f));

            if (!string.IsNullOrEmpty(_lastCsvPath))
            {
                EditorGUILayout.LabelField("CSV", _lastCsvPath, EditorStyles.miniLabel);
                if (GUILayout.Button("在文件管理器中显示"))
                {
                    EditorUtility.RevealInFinder(_lastCsvPath);
                }
            }
        }

        void RunBatch()
        {
            var report = new BatchBattleReport();

            // 临时物体：不保存、不污染场景
            var host = new GameObject("~BatchBattleHost");
            host.hideFlags = HideFlags.HideAndDontSave;
            MatchLoop loop = host.AddComponent<MatchLoop>();
            loop.SetManualPhysicsStep(false); // 编辑模式不步进物理
            loop.SetTimeLimit(_timeLimitSeconds);

            var step = loop.FixedStep;
            int maxTicks = Mathf.CeilToInt(_timeLimitSeconds / step);

            try
            {
                for (int i = 0; i < _matchCount; i++)
                {
                    uint seed = _seedStart + (uint)i;
                    loop.BeginMatch(seed);

                    int guard = 0;
                    while (loop.CurrentPhase == MatchLoop.Phase.Running && guard < maxTicks)
                    {
                        loop.StepOnce(step);
                        guard++;
                    }

                    MatchEndReason reason = loop.CurrentPhase == MatchLoop.Phase.Running
                        ? MatchEndReason.Aborted
                        : MatchEndReason.Timeout;

                    // 正常情况下空对局必然因超时结束；走到这里说明没结束 → 记为中止并暴露出来
                    int[] survivors = { 0, 0 };
                    var result = new MatchResult(reason, -1, loop.ElapsedSeconds, loop.TickCount, seed, survivors);
                    report.Add(result);

                    if (_stopOnAbort && reason == MatchEndReason.Aborted)
                    {
                        Debug.LogWarning("[批量对战] 第 " + i + " 局未在预期步数内结束，已停止。");
                        break;
                    }
                }
            }
            finally
            {
                DestroyImmediate(host);
            }

            _lastSummary = report.ToSummary();
            _lastCsvPath = WriteCsv(report);

            Debug.Log("[批量对战] " + _lastSummary + "\nCSV: " + _lastCsvPath);
            Repaint();
        }

        static string WriteCsv(BatchBattleReport report)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
            string dir = Path.Combine(projectRoot, ResultsRelativeDir);
            Directory.CreateDirectory(dir);

            string fileName = "batch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
            string fullPath = Path.Combine(dir, fileName);

            // 带 BOM，Excel 打开中文表头才不会乱码
            File.WriteAllText(fullPath, report.ToCsv(), new UTF8Encoding(true));
            AssetDatabase.Refresh();
            return fullPath;
        }
    }
}
