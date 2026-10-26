using System;
// SDX_EXECUTION_LOG_BEGIN
using SDx.SimStudio.Logging;
// SDX_EXECUTION_LOG_END
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SDx.SimStudio.Commands
{
    // Play owns a small supervisor, not a machine-wide service. The supervisor
    // owns its child processes and watches both stdin EOF and its parent PID.
    // No Scene/Prefab component, credential or process PID is serialized.
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-5000)]
    public sealed class SDxLlmPlaySession : MonoBehaviour
    {
        [Serializable]
        sealed class SessionStatus
        {
            public string session_id, status, message, error_code, model;
            public bool gateway_owned, ollama_owned;
        }

        public enum ServiceState { Stopped, Starting, Ready, Failed }
        public static ServiceState State { get; private set; } = ServiceState.Stopped;
        public static string UserMessage { get; private set; } = "LLM 서비스가 정지되어 있습니다.";
        public static string ErrorCode { get; private set; } = "";
        public static string LogPath { get; private set; } = "";
        public static string SessionId { get; private set; } = "";
        public static int Revision { get; private set; }
        public static bool IsReady { get { return State == ServiceState.Ready; } }
        public static bool IsStarting { get { return State == ServiceState.Starting; } }
        public static bool GatewayOwned { get; private set; }
        public static bool OllamaOwned { get; private set; }

        static bool active;
        static Process supervisor;
        static readonly List<Process> retiredProcesses = new List<Process>();
        static string statusPath, stopPath, lastStatusSignature;
        static double nextPollAt, startedAt;
        static SDxLlmPlaySession runtimeMonitor;

        public static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        public static string StartScriptPath
        {
            get { return Path.Combine(ProjectRoot, "Tools", "SDxLLMGateway", "start_play.sh"); }
        }

        public static string LogDirectory
        {
            get
            {
                return Application.isEditor
                    ? Path.Combine(ProjectRoot, "Logs", "SDxLLM")
                    : Path.Combine(Application.persistentDataPath, "Logs", "SDxLLM");
            }
        }

        static double MonotonicSeconds
        {
            get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            // Required when Enter Play Mode disables domain reload. No old
            // stdin lease may survive into a new Play session.
            StopForPlay();
            active = false;
            State = ServiceState.Stopped;
            UserMessage = "LLM 서비스를 시작할 준비 중입니다.";
            ErrorCode = "";
            SessionId = "";
            statusPath = stopPath = lastStatusSignature = null;
            GatewayOwned = OllamaOwned = false;
            nextPollAt = 0;
            ++Revision;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartAfterSceneLoad()
        {
            BeginForPlay();
        }

        // Also called by the Editor after EnteredPlayMode and after a script
        // domain reload during Play. These entry points intentionally coalesce.
        public static void BeginForPlay()
        {
            if (!Application.isPlaying || active) return;
            active = true;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
            ReapExitedProcesses();

            // OnEnable alone is insufficient when scene reload is disabled.
            // Reset only the command bridge's transient I/O/subscriptions.
            foreach (var bridge in UnityEngine.Object.FindObjectsOfType<SDxScenario1CommandBridge>(true))
                if (bridge.isActiveAndEnabled) bridge.ResetLlmSessionForPlay();

#if !UNITY_EDITOR
            if (runtimeMonitor == null)
            {
                var owner = new GameObject("SDx LLM Play Session (runtime)");
                owner.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(owner);
                runtimeMonitor = owner.AddComponent<SDxLlmPlaySession>();
            }
#endif
            if (Application.platform != RuntimePlatform.LinuxEditor &&
                Application.platform != RuntimePlatform.LinuxPlayer)
            {
                Fail("unsupported_platform", "자동 LLM 실행은 현재 Ubuntu/Linux용입니다. Unity Console을 확인해 주세요.");
                return;
            }

            SessionId = Guid.NewGuid().ToString("N");
            startedAt = MonotonicSeconds;
            nextPollAt = 0;
            lastStatusSignature = null;
            GatewayOwned = OllamaOwned = false;
            try
            {
                string script = StartScriptPath;
                if (!File.Exists(script))
                {
                    Fail("start_script_missing", "LLM 시작 스크립트가 없습니다. Tools/SDxLLMGateway 폴더와 패치 설치 경로를 확인해 주세요.");
                    return;
                }
                Directory.CreateDirectory(LogDirectory);
                string prefix = Path.Combine(LogDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + SessionId);
                statusPath = prefix + ".json";
                stopPath = prefix + ".stop";
                LogPath = prefix + ".log";
                int parentPid;
                using (var current = Process.GetCurrentProcess()) parentPid = current.Id;
                var info = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    // No bash -c: $(), quotes and spaces in a project path are
                    // passed as literal arguments, never evaluated as shell code.
                    Arguments = QuoteArgument(script) +
                        " --status-file " + QuoteArgument(statusPath) +
                        " --stop-file " + QuoteArgument(stopPath) +
                        " --log-path " + QuoteArgument(LogPath) +
                        " --session-id " + QuoteArgument(SessionId) +
                        " --parent-pid " + parentPid,
                    WorkingDirectory = Path.GetDirectoryName(script),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    // Python opens its own log before launching children. Do
                    // not leave redirected output pipes waiting for a reader.
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                supervisor = new Process { StartInfo = info };
                if (!supervisor.Start()) throw new InvalidOperationException("Could not start the LLM supervisor.");
                SetState(ServiceState.Starting, "설치된 로컬 LLM과 Gateway를 준비 중입니다. 준비 완료 후 자연어 명령을 입력해 주세요.", "");
                Debug.Log("[SDx LLM] Play 자동 시작. Model: qwen3:4b (default). Log: " + LogPath);
            }
            catch (Exception ex)
            {
                SignalStop();
                Fail("launcher_failed", "LLM 자동 실행에 실패했습니다. Python 3와 시작 스크립트 경로를 확인해 주세요.");
                Debug.LogWarning("[SDx LLM] Launcher: " + ex.Message);
            }
        }

        // Compatible with Unity's Mono ProcessStartInfo.Arguments parser. This
        // is argument quoting for Process.Start, not shell command escaping.
        static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        void Update() { Poll(); }

        void OnDestroy()
        {
            if (runtimeMonitor != this) return;
            runtimeMonitor = null;
            StopForPlay();
        }

        static void OnApplicationQuitting()
        {
            StopForPlay();
            ReleaseProcessHandles();
        }

        // EditorApplication.update calls this too, so readiness still updates
        // while the Editor is paused. All JSON/Unity work stays on the main thread.
        public static void Poll()
        {
            ReapExitedProcesses();
            if (!active || supervisor == null) return;
            double now = MonotonicSeconds;
            if (now < nextPollAt) return;
            nextPollAt = now + 0.25;

            ReadStatus();
            bool exited;
            try { exited = supervisor.HasExited; }
            catch (InvalidOperationException) { exited = true; }
            if (exited)
            {
                ReadStatus();
                if (State != ServiceState.Failed)
                    Fail("supervisor_exited", "LLM 서비스가 종료되었습니다. Unity Console과 Play 로그를 확인한 뒤 다시 Play해 주세요.");
                supervisor.Dispose();
                supervisor = null;
                return;
            }
            // The supervisor has shorter, configurable startup deadlines. This
            // only covers a broken launcher that never writes any final status.
            // Covers the largest allowed two startup stages (600s each), the
            // 60s lock wait, and cleanup margin. Normal default failure is much
            // earlier and is reported by the supervisor itself.
            if (State == ServiceState.Starting && now - startedAt > 1320)
            {
                SignalStop();
                Fail("startup_timeout", "LLM 준비 시간이 초과되었습니다. Play 로그에서 모델 경로와 실행 오류를 확인해 주세요.");
            }
        }

        static void ReadStatus()
        {
            if (string.IsNullOrEmpty(statusPath)) return;
            try
            {
                var file = new FileInfo(statusPath);
                if (!file.Exists || file.Length == 0 || file.Length > 32768) return;
                var status = JsonUtility.FromJson<SessionStatus>(File.ReadAllText(statusPath));
                if (status == null || status.session_id != SessionId) return;
                string signature = status.status + "|" + status.error_code + "|" + status.model +
                    "|" + status.gateway_owned + "|" + status.ollama_owned;
                if (signature == lastStatusSignature) return;
                lastStatusSignature = signature;
                GatewayOwned = status.gateway_owned;
                OllamaOwned = status.ollama_owned;
                switch (status.status)
                {
                    case "ready":
                        string model = string.IsNullOrEmpty(status.model) ? "qwen3:4b" : status.model;
                        SetState(ServiceState.Ready, "LLM 준비 완료 (" + model + "). 자연어 명령을 입력하세요.", "");
                        Debug.Log("[SDx LLM] READY. Model: " + model +
                            ", Play-owned Gateway: " + GatewayOwned + ", Play-owned Ollama: " + OllamaOwned +
                            ". First inference may load the model. Log: " + LogPath);
                        break;
                    case "error":
                        Fail(status.error_code, ErrorMessage(status.error_code));
                        if (!string.IsNullOrEmpty(status.message)) Debug.LogWarning("[SDx LLM] " + status.message);
                        break;
                    case "stopped":
                        if (State != ServiceState.Failed)
                            Fail("service_stopped", "LLM 서비스가 정지되었습니다. Play 로그를 확인한 뒤 다시 Play해 주세요.");
                        break;
                }
            }
            // The supervisor uses atomic replacement. Transient file access
            // failures during a replace never turn into a robot command.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }

        static string ErrorMessage(string code)
        {
            switch (code)
            {
                case "ollama_not_found":
                case "ollama_not_installed":
                case "ollama_executable_not_found":
                    return "Ollama 실행 파일을 찾지 못했습니다. Ollama를 별도로 설치하고 Play 설정의 실행 파일 경로를 확인해 주세요.";
                case "model_store_not_found":
                case "ollama_model_not_installed":
                case "model_not_found":
                    return "설치된 qwen3:4b 모델을 찾지 못했습니다. 모델 설치 여부와 Play 설정의 models_path를 확인해 주세요.";
                case "model_store_permission":
                    return "설치된 모델 폴더를 읽을 수 없습니다. Play 설정의 models_path와 현재 사용자의 읽기 권한을 확인해 주세요.";
                case "another_play_session":
                    return "이전 Play의 LLM 정리가 아직 끝나지 않았거나 다른 Unity가 사용 중입니다. 잠시 후 다시 Play해 주세요.";
                default:
                    return "LLM 자동 준비에 실패했습니다 (" + (string.IsNullOrEmpty(code) ? "unknown" : code) +
                        "). Unity Console과 Tools > SDx > LLM > Open Play Service Log를 확인해 주세요.";
            }
        }

        static void Fail(string code, string message)
        {
            SetState(ServiceState.Failed, message, code ?? "unknown");
            Debug.LogWarning("[SDx LLM] " + ErrorCode + " | " + UserMessage + " Log: " + LogPath);
        }

        static void SetState(ServiceState state, string message, string error)
        {
            if (State == state && UserMessage == message && ErrorCode == error) return;
            State = state;
            UserMessage = message;
            ErrorCode = error;
            ++Revision;
            // SDX_EXECUTION_LOG_BEGIN
            SDxExecutionLog.ServiceStateChanged(state.ToString(),message,error);
            // SDX_EXECUTION_LOG_END
        }

        public static void StopForPlay()
        {
            bool wasActive = active;
            active = false;
            SignalStop();
            if (wasActive)
            {
                SetState(ServiceState.Stopped, "Play 종료: 이번 Play에서 시작한 LLM 서비스를 정리 중입니다.", "");
                Debug.Log("[SDx LLM] Play 종료 요청. 이번 Play가 시작한 서비스만 종료합니다. Log: " + LogPath);
            }
            ReapExitedProcesses();
        }

        static void SignalStop()
        {
            // Stop marker remains usable even if Unity starts unloading the
            // managed domain immediately after this callback returns.
            if (!string.IsNullOrEmpty(stopPath))
            {
                try { File.WriteAllText(stopPath, SessionId ?? ""); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            var process = supervisor;
            supervisor = null;
            if (process == null) return;
            try { process.StandardInput.Close(); }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            // Never WaitForExit, Process.Kill, pkill or systemctl on Unity's
            // main thread. The supervisor completes its own bounded cleanup.
            retiredProcesses.Add(process);
        }

        static void ReapExitedProcesses()
        {
            for (int i = retiredProcesses.Count - 1; i >= 0; --i)
            {
                bool exited;
                try { exited = retiredProcesses[i].HasExited; }
                catch (InvalidOperationException) { exited = true; }
                if (!exited) continue;
                retiredProcesses[i].Dispose();
                retiredProcesses.RemoveAt(i);
            }
        }

        // Before assembly reload/Editor quit there may be no further update.
        // Closing these handles does not kill a process; stdin/marker already
        // handed cleanup to the supervisor, independently of the C# domain.
        public static void ReleaseProcessHandles()
        {
            SignalStop();
            foreach (var process in retiredProcesses) process.Dispose();
            retiredProcesses.Clear();
        }
    }
}
