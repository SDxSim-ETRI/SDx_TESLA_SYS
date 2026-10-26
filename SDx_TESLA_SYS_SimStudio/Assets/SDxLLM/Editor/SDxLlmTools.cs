using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using SDx.Scenario1;
using SDx.SimStudio.Commands;

public static class SDxLlmTools
{
    [MenuItem("Tools/SDx/LLM/Validate Current Setup")]
    public static void Validate()
    {
        EditorApplication.delayCall += () =>
        {
            try
            {
                var active = SceneManager.GetActiveScene();
                var bridges = UnityEngine.Object.FindObjectsOfType<SDxScenario1CommandBridge>(true)
                    .Where(candidate => candidate.gameObject.scene == active && candidate.gameObject.activeInHierarchy).ToArray();
                if (bridges.Length != 1) throw new InvalidOperationException("Keep exactly one existing SDxScenario1CommandBridge in this scene.");
                var b = bridges[0];
                if (b.ToolsUI == null || b.Controller == null) throw new InvalidOperationException("Existing ToolsUI/controller references are missing. Do not create a second scenario controller.");
                string detail;
                if (!b.Controller.ValidateDoorScenarioSetup(out detail)) throw new InvalidOperationException(detail);
                if (b.ToolsUI.onPromptSubmitted.GetPersistentEventCount() != 0)
                    throw new InvalidOperationException("Remove manual persistent Prompt listeners after review. The bridge subscribes at runtime; duplicate listeners can bypass LLM routing.");
                if (!File.Exists(SDxLlmPlaySession.StartScriptPath))
                    throw new InvalidOperationException("Missing Tools/SDxLLMGateway/start_play.sh. Install the Play auto-start patch into the Unity project root.");
                if (!File.Exists(SDxIntentHttpClient.ConfigPath))
                    Debug.Log("[SDx LLM] Gateway config is created automatically during Play. No manual start_gateway.sh is needed.");
                else
                {
                    var config = JsonUtility.FromJson<SDxGatewayConfig>(File.ReadAllText(SDxIntentHttpClient.ConfigPath));
                    if (!SDxIntentContract.IsValidLocalConfig(config))
                        Debug.LogWarning("[SDx LLM] Existing gateway config is invalid. Auto-start will only replace it after safely acquiring the gateway port; inspect the Play log if startup fails.");
                }
                Selection.activeGameObject = b.gameObject;
                Debug.Log("[SDx LLM] SETUP CHECK PASSED. One public Scenario 1 (with door opening). This is a reference/config check, not live Ollama inference or physical validation.", b);
                EditorUtility.DisplayDialog("LLM setup", "Existing command bridge and door scenario are connected.\n\nPlay automatically starts the installed local LLM and gateway. No manual terminal or API key.\nInstall Ollama and qwen3:4b separately before the first Play.\nScenario 2 is disabled; Scenario 1 always opens door 001.\n\nNo scene was modified.", "OK");
            }
            catch (Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog("LLM setup check", ex.Message, "OK"); }
        };
    }

    [MenuItem("Tools/SDx/LLM/Run Local Policy Self Checks")]
    public static void SelfChecks()
    {
        int count = 0;
        Action<bool, string> check = (ok, title) => { ++count; if (!ok) throw new Exception("Policy check failed: " + title); };
        string normalized, reason;
        check(SDxSingleScenarioCommand.IsScenario1("\uC0C1\uD6691"), "direct one");
        check(SDxSingleScenarioCommand.IsScenario1(" \uC0C1\uD669 1\t"), "direct whitespace");
        check(!SDxSingleScenarioCommand.IsScenario1("\uC0C1\uD6692"), "retired two");
        check(!SDxSingleScenarioCommand.IsScenario1("\uC0C1\uD6691 \uC2E4\uD589"), "no partial direct match");
        check(SDxIntentContract.Preflight("\uCC45 \uAC00\uC838\uB2E4 \uAF3D\uC544\uC918", out normalized, out reason) == SDxInputDisposition.Reject && reason == "missing_destination", "missing destination one");
        check(SDxIntentContract.Preflight("\uCC45 \uB4E4\uACE0 \uC6C0\uC9C1\uC5EC\uC11C \uAF3D\uC544\uC918", out normalized, out reason) == SDxInputDisposition.Reject, "missing destination two");
        check(SDxIntentContract.Preflight("\uC0C1\uD6692", out normalized, out reason) == SDxInputDisposition.RetiredScenario, "removed public command");
        string text = "\uCC45\uC744 \uC11C\uC7AC\uC5D0 \uAF42\uC544\uC918";
        check(SDxIntentContract.Preflight(text, out normalized, out reason) == SDxInputDisposition.NeedsLlm, "language requires actual LLM");
        check(SDxIntentContract.Preflight("", out normalized, out reason) == SDxInputDisposition.Reject, "empty");
        check(SDxIntentContract.Preflight(new string('x', 1001), out normalized, out reason) == SDxInputDisposition.Reject, "max length");
        var r = new SDxIntentResponse { protocol_version = 1, request_id = "test", decision = "execute", scenario_id = "scenario_1", reason_code = "accepted", source = "llm", model = "fixture", object_evidence = "\uCC45", destination_evidence = "\uC11C\uC7AC", action_evidence = "\uAF42\uC544\uC918" };
        check(SDxIntentContract.CanExecute(r, "test", text), "schema/evidence fixture (NOT LLM evaluation)");
        check(!SDxIntentContract.CanExecute(r, "other", text), "request id mismatch");
        r.destination_evidence = "bookshelf";
        check(!SDxIntentContract.CanExecute(r, "test", text), "invented destination quote");
        r.destination_evidence = "\uC11C\uC7AC"; r.scenario_id = "scenario_2";
        check(!SDxIntentContract.CanExecute(r, "test", text), "only one permitted scenario");
        r.scenario_id = "scenario_1"; r.decision = "reject";
        check(!SDxIntentContract.CanExecute(r, "test", text), "reject cannot execute");
        var c = new SDxGatewayConfig { protocol_version = 1, endpoint = "http://127.0.0.1:8765/v1/interpret", token = new string('a', 43) };
        check(SDxIntentContract.IsValidLocalConfig(c), "loopback config");
        c.endpoint = "http://example.com:8765/v1/interpret";
        check(!SDxIntentContract.IsValidLocalConfig(c), "no remote credential forwarding");
        c.endpoint = "http://127.0.0.1:8765/v1/interpret?override=1";
        check(!SDxIntentContract.IsValidLocalConfig(c), "fixed endpoint");
        c.endpoint = "http://127.0.0.1:8765/v1/interpret"; c.token = "bad\r\nheader";
        check(!SDxIntentContract.IsValidLocalConfig(c), "invalid token");
        Debug.Log("[SDx LLM] LOCAL POLICY CHECKS PASSED: " + count + ". No API requests, no robot movement; this is NOT a live semantic accuracy test.");
    }
}
