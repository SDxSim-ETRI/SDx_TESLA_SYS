using System;
// SDX_EXECUTION_LOG_BEGIN
using SDx.SimStudio.Logging;
// SDX_EXECUTION_LOG_END
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using SDx.Scenario1;
using SDx.SimStudio.UI;
using SDx.SimStudio.Commands;

// Same class/file/GUID as the existing prepared component. No scene recreation.
[DefaultExecutionOrder(-4000)]
[DisallowMultipleComponent]
public sealed class SDxScenario1CommandBridge : MonoBehaviour
{
    public SDxToolsPanel ToolsUI;
    public SDxScenario1Controller Controller;
    [Header("Local Ollama intent routing (no API key required)")]
    [Range(10, 600)] public float RequestTimeoutSeconds = 210;
    public string LastDecision { get; private set; }
    public bool IsClassifying { get { return pending != null; } }
    public static event Action ScenarioStartAccepted;

    SDxIntentHttpClient client;
    Task<SDxIntentHttpResult> pending;
    CancellationTokenSource cancellation;
    string queued, requestId, requestText;
    bool hasQueued, subscribed;
    int generation, requestGeneration, serviceRevision = -1;
    float requestedAt, activeRequestTimeoutSeconds;
    // SDX_EXECUTION_LOG_BEGIN
    string logQueuedCommandId, logCommandId, logRequestId;
    // SDX_EXECUTION_LOG_END

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        ResetLlmSessionForPlay();
    }
    // Called explicitly on Play too: scene/domain reload can both be disabled.
    // Only transient command I/O is reset; no robot or Scene state is changed.
    public void ResetLlmSessionForPlay()
    {
        if (!Application.isPlaying) return;
        hasQueued = false; ++generation; serviceRevision = -1; LastDecision = null;
        CancelPending();
        if (client != null) { client.Dispose(); client = null; }
        subscribed = false;
        if (ToolsUI != null) ToolsUI.onPromptSubmitted.RemoveListener(OnSubmitted);
        if (ToolsUI == null || Controller == null)
        {
            Debug.LogError("[SDx LLM] Existing ToolsUI/controller references are missing.", this); return;
        }
        client = new SDxIntentHttpClient();
        ToolsUI.onPromptSubmitted.RemoveListener(OnSubmitted);
        ToolsUI.onPromptSubmitted.AddListener(OnSubmitted);
        subscribed = true;
    }
    void OnDisable()
    {
        if (ToolsUI != null) ToolsUI.onPromptSubmitted.RemoveListener(OnSubmitted);
        subscribed = false; hasQueued = false; ++generation;
        CancelPending();
        if (client != null) { client.Dispose(); client = null; }
    }
    void OnSubmitted(string command)
    {
        // Event is raised during IMGUI; queue work rather than modifying GUI layout
        // or invoking network / state changes inside the same GUI event.
        // SDX_EXECUTION_LOG_BEGIN
        if (hasQueued && !string.IsNullOrEmpty(logQueuedCommandId))
            SDxExecutionLog.CommandCancelled(logQueuedCommandId, null);
        logQueuedCommandId = SDxExecutionLog.BeginCommand(command);
        // SDX_EXECUTION_LOG_END
        queued = command; hasQueued = true; ++generation;
    }
    void CancelPending()
    {
        // SDX_EXECUTION_LOG_BEGIN
        if (pending != null) SDxExecutionLog.CommandCancelled(logCommandId, logRequestId);
        // SDX_EXECUTION_LOG_END
        if (cancellation != null) { cancellation.Cancel(); cancellation.Dispose(); cancellation = null; }
        pending = null;
    }
    void Update()
    {
        if (!Application.isPlaying || !subscribed) return;
        RefreshServiceNotice();
        if (hasQueued)
        {
            string text = queued; hasQueued = false; CancelPending();
            // SDX_EXECUTION_LOG_BEGIN
            logCommandId = logQueuedCommandId; logQueuedCommandId = null; logRequestId = null;
            // SDX_EXECUTION_LOG_END
            Handle(text); return;
        }
        if (pending == null || !pending.IsCompleted) return;
        var finished = pending; pending = null;
        if (cancellation != null) { cancellation.Dispose(); cancellation = null; }
        if (requestGeneration != generation || Time.realtimeSinceStartup - requestedAt > activeRequestTimeoutSeconds + 1)
        { Reply("ambiguous", true); return; }
        // A completed HTTP reply cannot start a scenario after the Play-owned
        // service has failed or stopped in the meantime.
        if (!SDxLlmPlaySession.IsReady) { ReplyServiceNotReady(); return; }
        if (finished.IsCanceled || finished.IsFaulted) { Reply("gateway_unavailable", true); return; }
        SDxIntentHttpResult http = finished.Result;
        if (!http.Ok)
        {
            Debug.LogWarning("[SDx LLM] " + http.ErrorCode + ". No new scenario started.", this);
            Reply(http.ErrorCode == "timeout_or_cancelled" ? "ollama_timeout" : "gateway_unavailable", true); return;
        }
        SDxIntentResponse response;
        try { response = JsonUtility.FromJson<SDxIntentResponse>(http.Body); }
        catch (Exception) { Reply("invalid_model_result", true); return; }
        if (response == null || response.protocol_version != 1 || response.request_id != requestId)
        { Reply("invalid_model_result", true); return; }
        if (SDxIntentContract.CanExecute(response, requestId, requestText))
        { Execute(false); return; }
        if (response.decision == "execute") { Reply("invalid_model_result", true); return; }
        Reply(response.reason_code, true);
    }
    void RefreshServiceNotice()
    {
        int revision = SDxLlmPlaySession.Revision;
        if (serviceRevision == revision) return;
        serviceRevision = revision;
        // A late ready/error notice must never replace a submitted command's
        // decision or an active robot scenario result.
        if (hasQueued || pending != null || (!string.IsNullOrEmpty(LastDecision) &&
            !LastDecision.StartsWith("llm_", StringComparison.Ordinal))) return;
        LastDecision = SDxLlmPlaySession.IsReady ? "llm_ready" :
            (SDxLlmPlaySession.IsStarting ? "llm_starting" : "llm_start_failed");
        if (ToolsUI != null) ToolsUI.SetCommandReply(SDxLlmPlaySession.UserMessage,
            SDxLlmPlaySession.State == SDxLlmPlaySession.ServiceState.Failed);
    }
    void Handle(string text)
    {
        string normalized, reason;
        SDxInputDisposition disposition;
        try { disposition = SDxIntentContract.Preflight(text, out normalized, out reason); }
        catch (ArgumentException) { Reply("ambiguous", true); return; }
        if (disposition == SDxInputDisposition.DirectScenario1) { Execute(true); return; }
        if (disposition != SDxInputDisposition.NeedsLlm) { Reply(reason, true); return; }
        if (Controller == null || !Controller.IsReadyForCommand) { Reply("not_ready", true); return; }
        if (!SDxLlmPlaySession.IsReady)
        {
            ReplyServiceNotReady();
            return;
        }
        SDxGatewayConfig config;
        try
        {
            string path = SDxIntentHttpClient.ConfigPath;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 16384) { Reply("gateway_unavailable", true); return; }
            config = JsonUtility.FromJson<SDxGatewayConfig>(File.ReadAllText(path));
            if (!SDxIntentContract.IsValidLocalConfig(config)) { Reply("gateway_unavailable", true); return; }
        }
        catch (Exception) { Reply("gateway_unavailable", true); return; }
        requestId = Guid.NewGuid().ToString("N"); requestText = normalized;
        // SDX_EXECUTION_LOG_BEGIN
        logRequestId = requestId;
        // SDX_EXECUTION_LOG_END
        requestGeneration = generation; requestedAt = Time.realtimeSinceStartup;
        cancellation = new CancellationTokenSource();
        // An existing Scene can still contain the old 40-second serialized default.
        // Use the local gateway's verified timeout extension to cover cold model loading.
        activeRequestTimeoutSeconds = Mathf.Clamp(Mathf.Max(RequestTimeoutSeconds,
            config.request_timeout_seconds > 0 ? config.request_timeout_seconds : 210), 10, 600);
        cancellation.CancelAfter(TimeSpan.FromSeconds(activeRequestTimeoutSeconds));
        string json = JsonUtility.ToJson(new SDxIntentRequest { request_id = requestId, text = requestText });
        pending = client.SendAsync(config, json, cancellation.Token);
        Reply("thinking", false);
    }
    void ReplyServiceNotReady()
    {
        LastDecision = SDxLlmPlaySession.IsStarting ? "llm_starting" : "llm_start_failed";
        string message = SDxLlmPlaySession.UserMessage + " 준비 완료 후 명령을 다시 입력해 주세요.";
        // SDX_EXECUTION_LOG_BEGIN
        SDxExecutionLog.CommandDecision(logCommandId,logRequestId,LastDecision,message,!SDxLlmPlaySession.IsStarting);
        // SDX_EXECUTION_LOG_END
        if (ToolsUI != null) ToolsUI.SetCommandReply(message, !SDxLlmPlaySession.IsStarting);
        Debug.Log("[SDx LLM] " + LastDecision + " | " + message, this);
    }
    void Execute(bool direct)
    {
        if (Controller == null || !Controller.RequestScenario1()) { Reply("not_ready", true); return; }
        // SDX_EXECUTION_LOG_BEGIN
        SDxExecutionLog.AttachCommandToRun(logCommandId);
        // SDX_EXECUTION_LOG_END
        Reply(direct ? "direct" : "accepted", false);
        Action handler = ScenarioStartAccepted;
        if (handler != null)
        {
            foreach (Action listener in handler.GetInvocationList())
            {
                try { listener(); } catch (Exception ex) { Debug.LogException(ex, this); }
            }
        }
    }
    void Reply(string reason, bool error)
    {
        LastDecision = reason ?? "ambiguous";
        string text = SDxIntentContract.Reply(LastDecision);
        // SDX_EXECUTION_LOG_BEGIN
        SDxExecutionLog.CommandDecision(logCommandId,logRequestId,LastDecision,text,error);
        // SDX_EXECUTION_LOG_END
        if (ToolsUI != null) ToolsUI.SetCommandReply(text, error);
        Debug.Log("[SDx LLM] " + LastDecision + " | " + text, this);
    }
}
