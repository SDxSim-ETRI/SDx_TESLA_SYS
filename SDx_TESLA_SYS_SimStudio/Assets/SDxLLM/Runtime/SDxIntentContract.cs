using System;
using System.Linq;
using System.Text;
using SDx.Scenario1;

namespace SDx.SimStudio.Commands
{
    [Serializable] public sealed class SDxIntentRequest
    {
        public int protocol_version = 1;
        public string request_id;
        public string text;
    }
    [Serializable] public sealed class SDxIntentResponse
    {
        public int protocol_version;
        public string request_id, decision, scenario_id, reason_code, reply, source, model;
        public string object_evidence, destination_evidence, action_evidence;
    }
    [Serializable] public sealed class SDxGatewayConfig
    {
        public int protocol_version;
        public string endpoint;
        // LOCAL gateway session token generated automatically; not an API key.
        // Optional protocol-1 extension for slower local CPU inference. Zero means default.
        public int request_timeout_seconds;
        // Never serialize credentials or a session token into a Scene.
        public string token;
    }
    public enum SDxInputDisposition { DirectScenario1, RetiredScenario, Reject, NeedsLlm }

    public static class SDxIntentContract
    {
        public const int MaxCharacters = 1000;
        public const string ScenarioId = "scenario_1";
        static readonly string[] Destinations = { "\uC11C\uC7AC", "\uCC45\uC7A5", "\uCC45\uAF42\uC774", "bookshelf", "bookcase", "study" };
        static readonly string[] Objects = { "\uCC45", "\uC11C\uC801", "\uB3C4\uC11C", "book" };
        public static string Normalize(string text) { return (text ?? "").Normalize(NormalizationForm.FormC).Trim(); }
        static bool ContainsAny(string text, string[] words)
        {
            return words.Any(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public static bool HasDestination(string text) { return ContainsAny(text ?? "", Destinations); }
        public static SDxInputDisposition Preflight(string text, out string normalized, out string reason)
        {
            normalized = Normalize(text); reason = "";
            if (normalized.Length == 0) { reason = "empty"; return SDxInputDisposition.Reject; }
            if (normalized.Length > MaxCharacters) { reason = "too_long"; return SDxInputDisposition.Reject; }
            if (SDxSingleScenarioCommand.IsScenario1(normalized)) return SDxInputDisposition.DirectScenario1;
            if (SDxSingleScenarioCommand.IsRetiredScenario2(normalized)) { reason = "retired_scenario"; return SDxInputDisposition.RetiredScenario; }
            if (!HasDestination(normalized)) { reason = "missing_destination"; return SDxInputDisposition.Reject; }
            if (!ContainsAny(normalized, Objects)) { reason = "missing_object"; return SDxInputDisposition.Reject; }
            return SDxInputDisposition.NeedsLlm;
        }
        static bool IsQuote(string original, string quote)
        {
            return !string.IsNullOrWhiteSpace(quote) && quote.Length <= 160 &&
                original.IndexOf(quote, StringComparison.Ordinal) >= 0;
        }
        public static bool CanExecute(SDxIntentResponse r, string expectedId, string original)
        {
            if (r == null || r.protocol_version != 1 || r.request_id != expectedId ||
                r.decision != "execute" || r.scenario_id != ScenarioId || r.reason_code != "accepted" ||
                r.source != "llm" || string.IsNullOrWhiteSpace(r.model)) return false;
            string text, reason;
            if (Preflight(original, out text, out reason) != SDxInputDisposition.NeedsLlm) return false;
            return IsQuote(text, r.object_evidence) && ContainsAny(r.object_evidence, Objects) &&
                IsQuote(text, r.destination_evidence) && HasDestination(r.destination_evidence) &&
                IsQuote(text, r.action_evidence);
        }
        public static bool IsValidLocalConfig(SDxGatewayConfig c)
        {
            Uri uri;
            if (c == null || c.protocol_version != 1 || !Uri.TryCreate(c.endpoint, UriKind.Absolute, out uri)) return false;
            if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port < 1024 ||
                uri.AbsolutePath != "/v1/interpret" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0) return false;
            return c.token != null && c.token.Length >= 32 && c.token.Length <= 128 &&
                c.token.All(x => (x >= 'a' && x <= 'z') || (x >= 'A' && x <= 'Z') || (x >= '0' && x <= '9') || x == '-' || x == '_');
        }
        public static string Reply(string reason)
        {
            switch (reason)
            {
                case "accepted": return "\uBA85\uB839\uC744 \uC774\uD574\uD588\uC2B5\uB2C8\uB2E4. \uC0C1\uD6691(\uBB38 \uC5F4\uAE30\u00B7\uCC45\uC7A5 \uBC30\uCE58)\uC744 \uC2DC\uC791\uD569\uB2C8\uB2E4.";
                case "direct": return "\uC0C1\uD6691\uC744 \uB9AC\uC14B\uD558\uACE0 \uC2DC\uC791\uD569\uB2C8\uB2E4. \uBB38\uC744 \uC5F4\uACE0 \uCC45\uC744 \uCC45\uC7A5\uC5D0 \uAF42\uB294 \uC2DC\uB098\uB9AC\uC624\uC785\uB2C8\uB2E4.";
                case "missing_destination": return "\uBA85\uB839\uC744 \uC774\uD574\uD558\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4. \uBAA9\uC801\uC9C0(\uC11C\uC7AC/\uCC45\uC7A5/\uCC45\uAF42\uC774)\uB97C \uBA85\uC2DC\uD574 \uC8FC\uC138\uC694. \uC2E4\uD589\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4.";
                case "missing_object": return "\uC62E\uAE38 \uB300\uC0C1\uC774 \uBA85\uD655\uD558\uC9C0 \uC54A\uC2B5\uB2C8\uB2E4. \uCC45\uC744 \uC11C\uC7AC\uC758 \uCC45\uC7A5\uC5D0 \uB123\uC73C\uB77C\uB294 \uBA85\uB839\uC73C\uB85C \uC785\uB825\uD574 \uC8FC\uC138\uC694.";
                case "retired_scenario": return "\uC0C1\uD6692 \uBA85\uB839\uC740 \uC0AD\uC81C\uB418\uC5C8\uC2B5\uB2C8\uB2E4. \uBB38\uC744 \uC5F4\uACE0 \uCC45\uC744 \uBC30\uCE58\uD558\uB294 \uB3D9\uC791\uC740 \uC0C1\uD6691\uB85C \uC2E4\uD589\uD558\uC138\uC694.";
                case "empty": return "\uBA85\uB839\uC744 \uC785\uB825\uD574 \uC8FC\uC138\uC694.";
                case "too_long": return "\uBA85\uB839\uC740 1000\uC790 \uC774\uD558\uB85C \uC785\uB825\uD574 \uC8FC\uC138\uC694.";
                case "thinking": return "LLM\uC774 \uBA85\uB839\uC744 \uD655\uC778\uD558\uACE0 \uC788\uC2B5\uB2C8\uB2E4. \uC544\uC9C1 \uC0C8 \uC2E4\uD589\uC744 \uC2DC\uC791\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4.";
                case "gateway_unavailable": return "LLM Gateway 연결이 끊겨 실행하지 않았습니다. Unity Console과 Tools > SDx > LLM > Open Play Service Log를 확인한 뒤 다시 Play해 주세요.";
                case "provider_error": return "로컬 Ollama 응답을 받지 못해 실행하지 않았습니다. Unity Console과 Play 서비스 로그에서 모델 상태를 확인해 주세요.";
                case "ollama_timeout": return "\ub85c\uceec LLM \uc751\ub2f5 \uc2dc\uac04\uc744 \ucd08\uacfc\ud588\uc2b5\ub2c8\ub2e4. \uc0c8 \uc2e4\ud589\uc744 \uc2dc\uc791\ud558\uc9c0 \uc54a\uc558\uc2b5\ub2c8\ub2e4. \uba54\ubaa8\ub9ac\uc640 Gateway \uc124\uc815\uc744 \ud655\uc778\ud574 \uc8fc\uc138\uc694.";
                case "ollama_unavailable": return "로컬 Ollama에 연결하지 못해 새 실행을 시작하지 않았습니다. Play 서비스 로그를 확인한 뒤 다시 Play해 주세요.";
                case "ollama_model_not_installed": return "로컬 모델이 없습니다. ollama pull qwen3:4b로 별도 설치하고 Play 설정의 models_path를 확인해 주세요. 실행하지 않았습니다.";
                case "not_ready": return "MuJoCo \uC81C\uC5B4\uAE30\uAC00 \uC544\uC9C1 \uC900\uBE44\uB418\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4. Console\uC744 \uD655\uC778\uD574 \uC8FC\uC138\uC694. \uC2E4\uD589\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4.";
                case "busy": return "LLM\uC774 \uC774\uC804 \uC694\uCCAD\uC744 \uCC98\uB9AC \uC911\uC785\uB2C8\uB2E4. \uC0C8 \uC2E4\uD589\uC740 \uC2DC\uC791\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4.";
                case "not_command": return "\uC989\uC2DC \uC2E4\uD589\uD560 \uB3D9\uC791 \uC9C0\uC2DC\uB85C \uC774\uD574\uD558\uC9C0 \uBABB\uD574 \uC2E4\uD589\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4.";
                case "unsupported": return "\uD604\uC7AC\uB294 \uD14C\uC774\uBE14\uC758 \uC9C0\uC815\uB41C \uCC45\uC744 \uC11C\uC7AC \uCC45\uC7A5 \uC911\uC559 \uCE78\uC5D0 \uB123\uB294 \uC0C1\uD6691\uB9CC \uC9C0\uC6D0\uD569\uB2C8\uB2E4. \uC0C8 \uC2E4\uD589\uC740 \uC2DC\uC791\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4.";
                default: return "\uBA85\uB839\uC744 \uBA85\uD655\uD788 \uC774\uD574\uD558\uC9C0 \uBABB\uD574 \uC2E4\uD589\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4. \uCC45\uC744 \uC11C\uC7AC\uC758 \uCC45\uC7A5\uC5D0 \uB123\uC73C\uB77C\uB294 \uB73B\uC744 \uAD6C\uCCB4\uC801\uC73C\uB85C \uC801\uC5B4 \uC8FC\uC138\uC694.";
            }
        }
    }
}
