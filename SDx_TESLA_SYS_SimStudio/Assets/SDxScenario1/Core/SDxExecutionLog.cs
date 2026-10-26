using System;
using System.Collections.Generic;
using System.Globalization;

namespace SDx.SimStudio.Logging
{
    public enum SDxLogLevel { Info, Warning, Error }

    // Immutable rows let IMGUI retain one snapshot throughout Layout/Repaint.
    public sealed class SDxLogEntry
    {
        public readonly long Sequence;
        public readonly DateTime Timestamp;
        public readonly string Source, EventCode, Message, CorrelationId, RequestId, RawStatus;
        public readonly int RunId;
        public readonly SDxLogLevel Level;

        internal SDxLogEntry(long sequence, string source, string code, string message,
            SDxLogLevel level, string correlation, int run, string requestId, string rawStatus)
        {
            Sequence = sequence; Timestamp = DateTime.Now;
            Source = source; EventCode = code; Message = message; Level = level;
            CorrelationId = correlation; RunId = run; RequestId = requestId; RawStatus = rawStatus;
        }
    }

    // Pure managed data. No Unity API, I/O, physics reads, event listeners, or
    // callbacks occur here. Logging failure must never reach a controller.
    public static class SDxExecutionLog
    {
        public const int Capacity = 2000;
        static readonly object gate = new object();
        static readonly Queue<SDxLogEntry> rows = new Queue<SDxLogEntry>(Capacity);
        static readonly Queue<string> commandOrder = new Queue<string>(Capacity);
        static readonly Dictionary<string, int> commandRuns = new Dictionary<string, int>();
        static long sequence, revision, droppedCount, commandSequence;
        static int activeRun, lastStage = -1, lastDoorStep = -1, finishedRun = -1;
        static string activeCorrelation = "";
        static volatile bool overlayActive;

        public static bool OverlayActive { get { return overlayActive; } set { overlayActive = value; } }
        public static long Revision { get { lock (gate) return revision; } }
        public static long DroppedCount { get { lock (gate) return droppedCount; } }
        public static int Count { get { lock (gate) return rows.Count; } }

        // Called exactly once at Play initialization, including domain reload off.
        // A new command or a scenario reset never clears the accumulated rows.
        public static void ResetForPlay()
        {
            try
            {
                lock (gate)
                {
                    rows.Clear(); commandOrder.Clear(); commandRuns.Clear();
                    sequence = commandSequence = droppedCount = 0;
                    activeRun = 0; activeCorrelation = "";
                    lastStage = lastDoorStep = finishedRun = -1;
                    overlayActive = false; ++revision;
                }
            }
            catch (Exception) { }
        }

        public static SDxLogEntry[] Snapshot()
        {
            long ignored; return Snapshot(out ignored);
        }

        public static SDxLogEntry[] Snapshot(out long snapshotRevision)
        {
            try
            {
                lock (gate) { snapshotRevision = revision; return rows.ToArray(); }
            }
            catch (Exception) { snapshotRevision = -1; return new SDxLogEntry[0]; }
        }

        static string Clip(string value, int limit)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= limit ? value : value.Substring(0, limit) + "…";
        }

        // Caller holds gate. Only this method mutates the bounded row queue.
        static void Append(string source, string code, string message, SDxLogLevel level,
            string correlation, int run, string requestId = null, string rawStatus = null)
        {
            var row = new SDxLogEntry(++sequence, Clip(source, 40), Clip(code, 100),
                Clip(message, 1800), level, Clip(correlation, 80), run,
                Clip(requestId, 80), Clip(rawStatus, 300));
            if (rows.Count == Capacity) { rows.Dequeue(); ++droppedCount; }
            rows.Enqueue(row); ++revision;
        }

        static int CommandRun(string correlation)
        {
            int run;
            return !string.IsNullOrEmpty(correlation) && commandRuns.TryGetValue(correlation, out run) ? run : 0;
        }

        public static void Add(string source, string eventCode, string message,
            SDxLogLevel level = SDxLogLevel.Info, string correlationId = null,
            string requestId = null, string rawStatus = null)
        {
            try
            {
                lock (gate)
                {
                    bool scenario = source == "Scenario" && string.IsNullOrEmpty(correlationId);
                    Append(source, eventCode, message, level, scenario ? activeCorrelation : correlationId,
                        scenario ? activeRun : CommandRun(correlationId), requestId, rawStatus);
                }
            }
            catch (Exception) { }
        }

        public static string BeginCommand(string raw)
        {
            try
            {
                lock (gate)
                {
                    string id = "C" + (++commandSequence).ToString("D5", CultureInfo.InvariantCulture);
                    if (commandOrder.Count == Capacity) commandRuns.Remove(commandOrder.Dequeue());
                    commandOrder.Enqueue(id); commandRuns[id] = 0;
                    Append("Command", "input", "입력: " + Clip(raw, 1000), SDxLogLevel.Info, id, 0);
                    return id;
                }
            }
            catch (Exception) { return ""; }
        }

        // Called only after the controller has accepted a reset/start request.
        // New, rejected, and cancelled commands never alter this run context.
        public static int BeginRun(string correlationId = null)
        {
            try
            {
                lock (gate)
                {
                    ++activeRun; activeCorrelation = Clip(correlationId, 80);
                    lastStage = lastDoorStep = finishedRun = -1;
                    if (!string.IsNullOrEmpty(activeCorrelation) && commandRuns.ContainsKey(activeCorrelation))
                        commandRuns[activeCorrelation] = activeRun;
                    return activeRun;
                }
            }
            catch (Exception) { return 0; }
        }

        public static void AttachCommandToRun(string correlationId)
        {
            try
            {
                lock (gate)
                {
                    if (activeRun <= 0 || string.IsNullOrEmpty(correlationId)) return;
                    activeCorrelation = Clip(correlationId, 80);
                    if (commandRuns.ContainsKey(activeCorrelation)) commandRuns[activeCorrelation] = activeRun;
                }
            }
            catch (Exception) { }
        }

        public static void CommandDecision(string correlation, string requestId,
            string reason, string message, bool error)
        {
            try
            {
                lock (gate)
                {
                    bool technical = reason == "gateway_unavailable" || reason == "ollama_timeout" ||
                        reason == "provider_error" || reason == "invalid_model_result" || reason == "llm_start_failed";
                    Append(string.IsNullOrEmpty(requestId) ? "Command" : "LLM", reason,
                        message, !error ? SDxLogLevel.Info : technical ? SDxLogLevel.Error : SDxLogLevel.Warning,
                        correlation, CommandRun(correlation), requestId);
                }
            }
            catch (Exception) { }
        }

        public static void CommandCancelled(string correlation, string requestId)
        {
            try
            {
                lock (gate)
                    Append(string.IsNullOrEmpty(requestId) ? "Command" : "LLM", "cancelled",
                        "새 명령 입력 또는 세션 종료로 이전 요청이 취소되었습니다. 이 판정으로 새 실행을 시작하지 않았습니다.",
                        SDxLogLevel.Info, correlation, CommandRun(correlation), requestId);
            }
            catch (Exception) { }
        }

        public static void ServiceStateChanged(string state, string message, string error)
        {
            try
            {
                lock (gate)
                    Append("LLM", "service_" + state.ToLowerInvariant(), message,
                        state == "Failed" ? SDxLogLevel.Error : SDxLogLevel.Info, "", 0,
                        null, string.IsNullOrEmpty(error) ? state : state + ": " + error);
            }
            catch (Exception) { }
        }

        public static void ControllerReady()
        {
            try
            {
                lock (gate) Append("Scenario", "controller_ready",
                    "로봇 제어 준비 완료. 상황1 명령을 기다립니다.", SDxLogLevel.Info, "", 0);
            }
            catch (Exception) { }
        }

        public static void ScenarioRequested(bool restarting)
        {
            try
            {
                lock (gate)
                {
                    BeginRun();
                    Append("Scenario", "scenario_requested", restarting
                        ? "상황1 재시작 요청을 받았습니다. 다음 물리 단계에서 초기 상태를 복원합니다."
                        : "상황1 시작 요청을 받았습니다. 다음 물리 단계에서 초기 상태를 복원합니다.",
                        SDxLogLevel.Info, activeCorrelation, activeRun);
                }
            }
            catch (Exception) { }
        }

        public static void PhysicsResetCompleted(bool starting)
        {
            if (!starting) return;
            Add("Scenario", "reset_complete", "초기화 완료. 로봇·책 상태를 복원하고 문 001/002를 닫았습니다. 자세를 안정화합니다.");
        }

        public static void ScenarioStage(int stage, string raw)
        {
            if (stage < 1 || stage >= 17) return; // Finish owns the one terminal row.
            try
            {
                lock (gate)
                {
                    if (lastStage == stage) return;
                    lastStage = stage;
                    Append("Scenario", "stage", Translate(raw), SDxLogLevel.Info,
                        activeCorrelation, activeRun, null, "stage=" + stage + " | " + raw);
                }
            }
            catch (Exception) { }
        }

        public static void DoorStage(int step, string raw)
        {
            try
            {
                lock (gate)
                {
                    if (lastDoorStep == step) return;
                    lastDoorStep = step;
                    Append("Scenario", "door_step", Translate(raw), SDxLogLevel.Info,
                        activeCorrelation, activeRun, null, "door_step=" + step + " | " + raw);
                }
            }
            catch (Exception) { }
        }

        public static void NavigationMilestone(string label)
        {
            try
            {
                if (label == null || !label.StartsWith("Waypoint ", StringComparison.Ordinal)) return;
                int waypoint;
                if (!int.TryParse(label.Substring(9), out waypoint)) return;
                Add("Scenario", "waypoint", "이동 경로 지점 " + (waypoint + 1) + "에 도달했습니다.",
                    SDxLogLevel.Info, null, null, label);
            }
            catch (Exception) { }
        }

        public static void ScenarioFinished(bool verified, float positionError,
            float angleError, bool doorsVerified, int completedRuns)
        {
            try
            {
                lock (gate)
                {
                    if (finishedRun == activeRun) return;
                    finishedRun = activeRun;
                    string detail = "위치 오차 " + positionError.ToString("F4", CultureInfo.InvariantCulture) +
                        " m / 각도 오차 " + angleError.ToString("F2", CultureInfo.InvariantCulture) +
                        "° / 문 상태 검증 " + (doorsVerified ? "통과" : "미통과");
                    Append("Scenario", verified ? "completed" : "placement_warning", verified
                        ? "상황1 완료: 책 배치와 문 상태가 허용 오차 안에 있습니다. " + detail + " / 검증 완료 " + completedRuns + "회"
                        : "배치 동작이 끝났지만 최종 상태를 확인해야 합니다. " + detail,
                        verified ? SDxLogLevel.Info : SDxLogLevel.Warning, activeCorrelation, activeRun);
                }
            }
            catch (Exception) { }
        }

        public static void ScenarioFault(Exception error)
        {
            try
            {
                Add("Scenario", "faulted", "상황1 오류: " + (error == null ? "알 수 없는 오류" : error.Message), SDxLogLevel.Error);
            }
            catch (Exception) { }
        }

        static string Translate(string raw)
        {
            switch (raw)
            {
                case "Above book": return "왼팔을 책 위쪽으로 이동합니다.";
                case "Approach book": return "왼손을 책에 접근시킵니다.";
                case "Grasp pose": return "책을 잡을 자세로 정렬합니다.";
                case "Close physical gripper": return "왼손 그리퍼를 닫아 책을 잡습니다.";
                case "Lift book": return "잡은 책을 들어 올립니다.";
                case "Carry pose": return "책 운반 자세로 왼팔을 이동합니다.";
                case "Hold book": return "책을 잡은 자세를 안정화합니다.";
                case "Navigate fixed route": return "준비된 경로를 따라 이동합니다.";
                case "Settle at shelf": return "책장 앞에서 로봇을 안정화합니다.";
                case "Align with center cell": return "책을 책장 중앙 칸에 맞춥니다.";
                case "Insert into center cell": return "책을 책장 중앙 칸으로 넣습니다.";
                case "Lower onto shelf": return "책을 선반 위로 내려놓습니다.";
                case "Release book": return "왼손 그리퍼를 열어 책을 놓습니다.";
                case "Withdraw gripper": return "책장에서 왼손 그리퍼를 뺍니다.";
                case "Return arm to home": return "왼팔을 초기 자세로 되돌립니다.";
                case "Final settle": return "최종 자세를 안정화하고 결과 검증을 준비합니다.";
                case "Door 001: stop before closed passage": return "닫힌 문 001 앞에서 정지합니다.";
                case "Door 001: turn parallel to rail": return "로봇을 문 레일과 평행하게 회전합니다.";
                case "Door 001: settle for alignment": return "문 정렬을 위해 로봇 자세를 안정화합니다.";
                case "Door 001: straight reverse alignment": return "짧게 직선 후진하여 문 손잡이와 정렬합니다.";
                case "Door 001: alignment settle": return "후진 정렬을 마치고 로봇을 안정화합니다.";
                case "Door 001: square base to rail": return "로봇 방향을 문 레일에 맞춥니다.";
                case "Door 001: hold base": return "문 파지를 위해 로봇 위치를 유지합니다.";
                case "Door 001: right hand approach": return "오른손을 문 001 손잡이에 접근시킵니다.";
                case "Door 001: right hand at handle": return "오른손을 문 손잡이 파지 자세로 정렬합니다.";
                case "Door 001: close physical right gripper": return "오른손 그리퍼를 닫아 문 손잡이를 잡습니다.";
                case "Door 001: pull with right hand and mobile base": return "오른손과 이동 베이스로 문 001을 레일을 따라 엽니다.";
                case "Door 001: hold open, left hand holds book": return "문 열린 상태를 유지합니다. 왼손은 책을 계속 잡습니다.";
                case "Door 001: release handle": return "문 열림 위치를 확인하고 오른손에서 손잡이를 놓습니다.";
                case "Door 001: withdraw right gripper": return "문 손잡이에서 오른손을 뺍니다.";
                case "Door 001: return right arm home": return "오른팔을 초기 자세로 되돌립니다.";
                case "Door 001: return to original corridor": return "기존 통로로 복귀합니다.";
                case "Door 001: face open passage": return "열린 통로 방향으로 로봇을 돌립니다.";
                case "Navigate fixed route through opened door 001": return "문 001 개방 단계를 마치고 기존 이동 경로를 재개합니다.";
                default: return raw ?? "";
            }
        }
    }
}
