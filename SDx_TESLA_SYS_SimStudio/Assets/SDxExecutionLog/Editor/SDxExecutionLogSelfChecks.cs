using System;
using System.Threading;
using SDx.SimStudio.Logging;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

// The same assertions run in Unity's Editor and in the external pure-C# check.
// They never create a scene object, send a command, or read/write robot state.
public static class SDxExecutionLogSelfChecks
{
    static int assertions;

    static void Check(bool condition, string description)
    {
        ++assertions;
        if (!condition) throw new InvalidOperationException("Execution log check: " + description);
    }

    public static int RunAssertions()
    {
        assertions = 0;
        try
        {
            CheckCumulativeHistory();
            CheckCapacityAndSnapshots();
            CheckConcurrentAppends();
            CheckWindowTransitionsAndBounds();
            CheckResizing();
            return assertions;
        }
        finally { SDxExecutionLog.ResetForPlay(); }
    }

    static void CheckCumulativeHistory()
    {
        SDxExecutionLog.ResetForPlay();
        string accepted = SDxExecutionLog.BeginCommand("책을 서재에 꽂아라");
        SDxExecutionLog.ScenarioRequested(false);
        SDxExecutionLog.AttachCommandToRun(accepted);
        SDxExecutionLog.CommandDecision(accepted, "request-first", "accepted", "승인", false);
        SDxExecutionLog.PhysicsResetCompleted(true);
        SDxExecutionLog.ScenarioStage(1, "Above book");
        int count = SDxExecutionLog.Count;
        for (int i = 0; i < 100; ++i) SDxExecutionLog.ScenarioStage(1, "Above book");
        Check(SDxExecutionLog.Count == count, "one row per scenario stage, not per physics step");
        SDxExecutionLog.DoorStage(0, "Door 001: stop before closed passage");
        count = SDxExecutionLog.Count;
        for (int i = 0; i < 100; ++i) SDxExecutionLog.DoorStage(0, "Door 001: stop before closed passage");
        Check(SDxExecutionLog.Count == count, "one row per door transition");

        string rejected = SDxExecutionLog.BeginCommand("문만 열어줘");
        SDxExecutionLog.CommandDecision(rejected, "", "unsupported", "거부", true);
        string cancelled = SDxExecutionLog.BeginCommand("책을 서재에 꽂아라");
        SDxExecutionLog.CommandCancelled(cancelled, "request-cancelled");
        SDxExecutionLog.ScenarioStage(2, "Approach book");
        var entries = SDxExecutionLog.Snapshot();
        var active = entries[entries.Length - 1];
        Check(active.RunId == 1 && active.CorrelationId == accepted,
            "rejected/cancelled command cannot take over the active scenario's correlation");
        Check(entries[entries.Length - 2].RunId == 0 && entries[entries.Length - 2].RequestId == "request-cancelled",
            "cancelled language request stays separate from accepted run");
        Check(entries[entries.Length - 4].RunId == 0 && entries[entries.Length - 4].Level == SDxLogLevel.Warning,
            "ordinary rejected command has warning level and no run");

        SDxExecutionLog.ScenarioFinished(true, 0.001f, 0.2f, true, 1);
        count = SDxExecutionLog.Count;
        SDxExecutionLog.ScenarioStage(17, "Final settle");
        SDxExecutionLog.ScenarioFinished(true, 0.001f, 0.2f, true, 1);
        Check(SDxExecutionLog.Count == count, "one final result per run");
        string next = SDxExecutionLog.BeginCommand("상황1");
        SDxExecutionLog.ScenarioRequested(true);
        SDxExecutionLog.AttachCommandToRun(next);
        SDxExecutionLog.ScenarioStage(1, "Above book");
        entries = SDxExecutionLog.Snapshot();
        Check(entries.Length > count, "a new run appends without clearing preceding runs");
        Check(entries[0].CorrelationId == accepted, "first command remains in cumulative history");
        Check(entries[entries.Length - 1].RunId == 2 && entries[entries.Length - 1].CorrelationId == next,
            "new accepted run has a distinct run number and command correlation");
        SDxExecutionLog.CommandDecision(next, "request-second", "invalid_model_result", "검증 실패", true);
        entries = SDxExecutionLog.Snapshot();
        Check(entries[entries.Length - 1].Level == SDxLogLevel.Error,
            "technical language failures are distinguished from command refusals");
        Check(entries[entries.Length - 1].RequestId == "request-second", "request ID is preserved separately");
    }

    static void CheckCapacityAndSnapshots()
    {
        SDxExecutionLog.ResetForPlay();
        long previousRevision = SDxExecutionLog.Revision;
        SDxExecutionLog.Add("QA", "first", "처음");
        long snapshotRevision;
        var snapshot = SDxExecutionLog.Snapshot(out snapshotRevision);
        SDxExecutionLog.Add("QA", "second", "다음");
        Check(snapshot.Length == 1 && snapshot[0].Message == "처음", "retained snapshot is stable after an append");
        Check(SDxExecutionLog.Revision > snapshotRevision && snapshotRevision > previousRevision,
            "snapshot revision reflects the exact snapshot generation");
        snapshot[0] = null;
        Check(SDxExecutionLog.Snapshot()[0] != null, "callers cannot mutate the internal row collection");
        foreach (var field in typeof(SDxLogEntry).GetFields())
            Check(field.IsInitOnly, "snapshot entries expose readonly data: " + field.Name);

        SDxExecutionLog.ResetForPlay();
        for (int i = 0; i < SDxExecutionLog.Capacity + 17; ++i)
            SDxExecutionLog.Add("QA", "bounded", i.ToString());
        var rows = SDxExecutionLog.Snapshot();
        Check(rows.Length == SDxExecutionLog.Capacity && SDxExecutionLog.Count == rows.Length,
            "history is bounded during long sessions");
        Check(SDxExecutionLog.DroppedCount == 17, "discarded old row count is explicit");
        Check(rows[0].Sequence == 18 && rows[rows.Length - 1].Sequence == SDxExecutionLog.Capacity + 17,
            "capacity eviction retains newest rows in sequence");
        for (int i = 1; i < rows.Length; ++i)
            Check(rows[i].Sequence > rows[i - 1].Sequence, "row ordering remains strict after eviction");
        SDxExecutionLog.OverlayActive = true;
        SDxExecutionLog.ResetForPlay();
        Check(SDxExecutionLog.Count == 0 && SDxExecutionLog.DroppedCount == 0 && !SDxExecutionLog.OverlayActive,
            "a new Play clears the preceding Play's transient history and overlay flag");
        SDxExecutionLog.Add("QA", "restart", "다시 시작");
        Check(SDxExecutionLog.Snapshot()[0].Sequence == 1, "new Play starts a new sequence");
    }

    static void CheckConcurrentAppends()
    {
        SDxExecutionLog.ResetForPlay();
        var workers = new Thread[4];
        for (int t = 0; t < workers.Length; ++t)
        {
            int worker = t;
            workers[t] = new Thread(() =>
            {
                for (int i = 0; i < 100; ++i) SDxExecutionLog.Add("QA", "thread", worker + ":" + i);
            });
            workers[t].Start();
        }
        foreach (var worker in workers) worker.Join();
        var rows = SDxExecutionLog.Snapshot();
        Check(rows.Length == 400, "concurrent appenders do not lose rows");
        for (int i = 0; i < rows.Length; ++i)
            Check(rows[i].Sequence == i + 1, "concurrent rows have unique ordered sequence numbers");
    }

    static void CheckWindowTransitionsAndBounds()
    {
        var layout = new SDxLogWindowLayoutState();
        var normal = layout.Calculate(1920, 1080, 400);
        Check(normal.Width >= 600 && normal.Height >= 300, "normal window provides a readable table area");
        layout.ToggleMaximize();
        var maximized = layout.Calculate(1920, 1080, 400);
        Check(maximized.Width > normal.Width && maximized.Height > normal.Height && maximized.X >= 400,
            "maximized window expands while preserving the Tools area");
        layout.ToggleMinimize();
        var minimized = layout.Calculate(1920, 1080, 400);
        Check(layout.Mode == SDxLogWindowMode.Minimized && minimized.Height < 60,
            "minimized mode retains only a compact title bar");
        layout.ToggleMinimize();
        Check(layout.Mode == SDxLogWindowMode.Maximized, "unminimizing restores the prior maximized mode");
        layout.ToggleMaximize();
        Check(layout.Mode == SDxLogWindowMode.Normal, "maximize toggle returns to normal");
        layout.ToggleMinimize(); layout.ToggleMaximize();
        Check(layout.Mode == SDxLogWindowMode.Maximized, "maximize from minimized opens a usable table");
        layout.Restore();
        Check(layout.Mode == SDxLogWindowMode.Normal, "new Play/reset restores normal layout");

        float[,] screens = { {1,1}, {2,2}, {64,32}, {320,240}, {800,600}, {1920,1080}, {3840,2160} };
        for (int mode = 0; mode < 3; ++mode)
        {
            layout.Restore();
            if (mode == 1) layout.ToggleMinimize();
            if (mode == 2) layout.ToggleMaximize();
            for (int index = 0; index < screens.GetLength(0); ++index)
            {
                float width = screens[index, 0], height = screens[index, 1];
                foreach (float left in new[] { 0f, width * 0.35f, width - 1f })
                {
                    var bounds = layout.Calculate(width, height, left);
                    Check(bounds.X >= 0 && bounds.Y >= 0 && bounds.Width > 0 && bounds.Height > 0,
                        "window has valid positive bounds at all supported sizes");
                    Check(bounds.X + bounds.Width <= width + 0.001f && bounds.Y + bounds.Height <= height + 0.001f,
                        "window remains inside small/narrow/large screens");
                    Check(bounds.Contains(bounds.X + bounds.Width * .5f, bounds.Y + bounds.Height * .5f),
                        "camera shield contains window center");
                    Check(!bounds.Contains(bounds.X - 1f, bounds.Y - 1f), "camera shield excludes outside pointer");
                }
            }
        }
        var invalid = layout.Calculate(float.NaN, float.PositiveInfinity, float.NegativeInfinity);
        Check(!float.IsNaN(invalid.X) && !float.IsInfinity(invalid.Height), "uninitialized/invalid dimensions stay finite");
    }

    static void CheckResizing()
    {
        var layout = new SDxLogWindowLayoutState();
        var initial = layout.Calculate(1920, 1080, 400);
        layout.ResizeFrom(initial, -120, 80, true, true, 1920, 1080, 400);
        var resized = layout.Calculate(1920, 1080, 400);
        Check(Math.Abs(resized.Width - 900f) < .01f && Math.Abs(resized.Height - 470f) < .01f,
            "corner drag grows width leftwards and height downwards");
        Check(Math.Abs(initial.X + initial.Width - resized.X - resized.Width) < .01f && initial.Y == resized.Y,
            "resize keeps the top-right anchor");
        layout.ToggleMinimize(); layout.ToggleMinimize();
        var restored = layout.Calculate(1920, 1080, 400);
        Check(restored.Width == resized.Width && restored.Height == resized.Height,
            "minimum toggle retains user dimensions");
        layout.ToggleMaximize(); layout.ToggleMaximize();
        restored = layout.Calculate(1920, 1080, 400);
        Check(restored.Width == resized.Width && restored.Height == resized.Height,
            "maximum toggle retains user dimensions");
        layout.ResizeFrom(resized, 9999, -9999, true, true, 1920, 1080, 400);
        var minimum = layout.Calculate(1920, 1080, 400);
        Check(minimum.Width == SDxLogWindowLayoutState.MinimumWidth &&
              minimum.Height == SDxLogWindowLayoutState.MinimumHeight, "minimum readable size is enforced");
        layout.ResizeFrom(minimum, -9999, 9999, true, true, 1920, 1080, 400);
        var maximum = layout.Calculate(1920, 1080, 400);
        Check(maximum.X >= 400 && maximum.X + maximum.Width <= 1920 && maximum.Y + maximum.Height <= 1080,
            "drag cannot cover the Tools viewport or leave the screen");
        layout.Restore();
        layout.ResizeFrom(initial, -100, 60, true, false, 1920, 1080, 400);
        var leftOnly = layout.Calculate(1920, 1080, 400);
        Check(leftOnly.Width == 880f && leftOnly.Height == initial.Height, "left edge changes width only");
        layout.ResizeFrom(leftOnly, -100, 60, false, true, 1920, 1080, 400);
        var bottomOnly = layout.Calculate(1920, 1080, 400);
        Check(bottomOnly.Width == leftOnly.Width && bottomOnly.Height == 450f, "bottom edge changes height only");
        layout.ToggleMaximize();
        layout.SetNormalSize(500, 300, 1920, 1080, 400);
        layout.ToggleMaximize();
        var unchanged = layout.Calculate(1920, 1080, 400);
        Check(unchanged.Width == bottomOnly.Width && unchanged.Height == bottomOnly.Height,
            "maximized mode rejects accidental resizing of the saved normal dimensions");
        layout.SetNormalSize(float.NaN, float.PositiveInfinity, 1920, 1080, 400);
        var finite = layout.Calculate(1920, 1080, 400);
        Check(!float.IsNaN(finite.Width) && !float.IsInfinity(finite.Height), "non-finite resize deltas are ignored");
        foreach (float sw in new[] { 1f, 64f, 320f, 800f, 1920f, 3840f })
        foreach (float sh in new[] { 1f, 48f, 240f, 600f, 1080f, 2160f })
        {
            layout.Restore();
            layout.SetNormalSize(100000f, 100000f, sw, sh, sw * .25f);
            var bounds = layout.Calculate(sw, sh, sw * .25f);
            Check(bounds.Width > 0 && bounds.Height > 0 && bounds.X >= 0 && bounds.Y >= 0 &&
                bounds.X + bounds.Width <= sw + .001f && bounds.Y + bounds.Height <= sh + .001f,
                "resize clamps on narrow, small and high-resolution screens");
        }
        layout.Restore();
        var reset = layout.Calculate(1920, 1080, 400);
        Check(reset.Width == 780f && reset.Height == 390f, "new Play restores the default size");
    }

#if UNITY_EDITOR
    [MenuItem("Tools/SDx/Execution Log/Run Data and Layout Self Checks")]
    public static void RunMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Execution log checks", "Play를 종료한 뒤 실행하세요. 검사 중 실제 실행 기록은 초기화됩니다.", "OK");
            return;
        }
        int count = RunAssertions();
        Debug.Log("[SDx Execution Log] DATA/LAYOUT CHECKS PASSED: " + count +
            ". No scene changes, HTTP requests, or robot movement. This is not a rendered Game view or physics test.");
    }
#endif
}
