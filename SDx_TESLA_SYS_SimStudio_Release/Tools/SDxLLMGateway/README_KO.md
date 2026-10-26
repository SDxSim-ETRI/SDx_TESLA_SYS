# 현재 사용 안내: Play 자동 실행 패치 v1.0

2026-10-26 추가: 기본 모델은 **`qwen3:4b`**이며, Unity **Play 시작/종료에 맞추어 전용 Ollama와 Gateway를 자동 시작/정리**합니다. 설치와 모델 다운로드는 별도로 한 번만 합니다. 아래 본문은 이전 수동 실행 버전의 기록이므로, 현재 실행 절차는 프로젝트의 **`Tools/SDxLLMGateway/AUTO_START_KO.md`**를 우선 확인하세요.

---

## 이전 수동 실행 버전 기록

# SDx Local Ollama v1.0 — OpenAI 호출을 로컬 LLM으로 교체

## 핵심

이번 버전은 기존 SDx LLM Command v1.0 설치본에 적용하는 패치입니다.
로컬 Ollama + `qwen3:1.7b`를 사용하며 API Key, 계정 로그인, 결제 설정이 필요 없습니다.
`상황1`은 문을 열고 책을 서재 책장에 넣는 통합 시나리오이며 `상황2`는 계속 폐기된 명령입니다.
로봇 경로, 팔 제어, 문 모델, 카메라, GUI 디자인, 한글 입력은 변경하지 않습니다.
문고리 부근 회전·충돌은 이번에도 수정하지 않았습니다.

키 입력을 요구하던 기존 터미널은 Ctrl+C로 종료하세요. 입력 대기까지 진행한 것은 Ollama 설치에 영향을 주지 않습니다.
기존 설치용 SDx_LLM_Command_v1_0 폴더는 새 설치 프로그램이 프로젝트 밖에 보관합니다.
Assets/SDxLLM 자체를 삭제하면 GUI/시나리오 연결이 끊기므로 직접 지우지 마세요.

## 1. Unity 패치 설치

Scene 저장 → Play 종료 → Unity 종료 후 실행합니다. ZIP은 Downloads에 놓습니다.

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
export DOWNLOAD_DIR="$(xdg-user-dir DOWNLOAD)"
export PATCH_ZIP="$DOWNLOAD_DIR/SDx_Ollama_Local_v1_0.zip"

if [ -f "$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt" ] && [ -f "$PATCH_ZIP" ]; then
    unzip -o "$PATCH_ZIP" -d "$DOWNLOAD_DIR" && \
    python3 "$DOWNLOAD_DIR/SDx_Ollama_Local_v1_0/Tools/install.py" --project "$UNITY_PROJECT"
else
    echo "Check the Unity project and ZIP paths."
fi
```

성공: `INSTALL COMPLETE: Local Ollama v1.0.0`
백업: `~/SDx_Ollama_Backups/<설치시각>/`
새 패키지 자체는 Assets 안에 통째로 복사하지 마세요. 반드시 설치 프로그램을 사용하세요.

설치 프로그램은 기존 v1.0 파일 해시와 원본 계획 파일을 검사한 뒤 교체합니다.
임의로 수정한 파일이 발견되면 설치 전에 중단하며, --dry-run으로 변경 없이 검사할 수 있습니다.
이전 설치용 폴더가 다음 위치에 있으면 내부 manifest를 확인한 후 프로젝트 밖 백업에 이동합니다.

- <프로젝트>/SDx_LLM_Command_v1_0
- <프로젝트>/Assets/SDx_LLM_Command_v1_0
- <프로젝트>/Tools/SDx_LLM_Command_v1_0

원래 위치의 폴더 .meta도 함께 보관합니다. Downloads에 남은 기존 ZIP/설치본은 실행에 쓰지 않으므로 나중에 삭제해도 됩니다.
이름이 같지만 다른 내용인 폴더, 심볼릭 링크, 지원하지 않는 버전은 임의로 삭제하지 않습니다.

수정 대상은 Gateway Python 코드, Unity 명령 Bridge의 로컬 추론 대기시간, 안내 문구/검사 메뉴입니다.
Scene, 기존 .meta, .asmdef, Scenario1Plan, Scenario2Plan, Controller, MuJoCo, 카메라 Rig, GUI는 유지합니다.
Git 설정이나 GitHub는 건드리지 않습니다.

## 2. Ollama 설치 (인터넷이 연결된 상태에서 한 번)

Ubuntu 22.04의 일반 터미널에서 실행하세요. Docker/Conda 환경을 새로 만들 필요는 없습니다.
이미 Ollama가 설치되어 있으면 버전을 확인하고 설치 명령은 생략할 수 있습니다.

```bash
sudo apt update
sudo apt install -y curl ca-certificates python3 unzip zstd
curl -fsSL https://ollama.com/install.sh | sh
ollama --version
```

위 curl 명령은 Ollama 공식 설치 스크립트를 실행하며 시스템 설치 권한을 요청할 수 있습니다.
이 패키지는 Ollama 실행 파일이나 모델 가중치를 포함하지 않습니다.

## 3. 로컬 전용 Ollama 설정

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
sudo bash "$UNITY_PROJECT/Tools/SDxLLMGateway/configure_ollama_local.sh"
systemctl is-active ollama
```

`active`를 확인합니다. 이 스크립트는 다음 파일 하나를 추가/갱신합니다.
`/etc/systemd/system/ollama.service.d/90-sdx-local.conf`

설정: OLLAMA_HOST=127.0.0.1:11434, OLLAMA_NO_CLOUD=1, OLLAMA_NUM_PARALLEL=1, OLLAMA_MAX_LOADED_MODELS=1.
기존 SDx drop-in이 있으면 /var/backups 아래에 백업합니다. 다른 서비스 설정 파일은 덮어쓰지 않습니다.
Ollama 서비스는 재시작됩니다. 다른 앱이 Ollama를 쓰고 있다면 해당 작업을 먼저 종료하세요.
다른 drop-in에서 같은 값을 재정의했다면 `systemctl cat ollama.service`로 최종 설정을 확인하세요.

systemd 없이 수동 실행하는 환경에 한해서 별도 터미널에서 아래처럼 실행할 수 있습니다.
이미 서비스가 실행 중이면 중복 실행하지 마세요.

```bash
OLLAMA_NO_CLOUD=1 OLLAMA_HOST=127.0.0.1:11434 OLLAMA_NUM_PARALLEL=1 ollama serve
```

## 4. 모델 다운로드

```bash
ollama pull qwen3:1.7b
ollama list
curl --noproxy '*' -fsS http://127.0.0.1:11434/api/tags
```

qwen3:1.7b가 목록에 있어야 합니다. 공개 모델 다운로드와 로컬 추론에는 API 키가 필요하지 않습니다.
Ollama Cloud나 -cloud 모델, ollama signin은 이번 구성에서 사용하지 않습니다.
모델 다운로드가 완료된 뒤에는 이 LLM 연결의 실행에 외부 인터넷이 필요하지 않습니다.
Unity 설치/패키지 복원 등 다른 최초 준비 작업은 별도입니다.

## 5. 설치 상태 검사 (실제 생성 요청 아님)

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
python3 "$UNITY_PROJECT/Tools/SDxLLMGateway/check_environment.py"
```

성공: `OLLAMA ENVIRONMENT OK`.
Ollama 버전, 모델 digest, GGUF 형식, completion 지원과 로컬 모델 여부를 검사합니다.
모델 생성·설치·다운로드를 대신하지 않습니다. 실제 의미 분류 정확도 검사는 7번입니다.

## 6. Gateway 실행

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
bash "$UNITY_PROJECT/Tools/SDxLLMGateway/start_gateway.sh"
```

성공: `SDx Ollama Gateway READY on 127.0.0.1:8765`
API Key를 묻지 않습니다. 계속 키를 묻는다면 예전 start_gateway.sh를 실행한 것입니다.
이 터미널은 유지하고 다른 터미널이나 Unity를 사용합니다. Ctrl+C는 Gateway만 종료합니다.
Gateway를 sudo로 실행하지 마세요. Unity와 같은 일반 사용자 계정을 사용해야 합니다.

구조:
`Unity Send Command → 127.0.0.1:8765 Gateway → 127.0.0.1:11434 Ollama → 기존 상황1`

~/.config/sdx-simstudio/intent-gateway.json의 token은 자동 생성하는 로컬 통신용 토큰입니다.
OpenAI/Ollama API 키가 아니며 사용자가 발급받거나 직접 입력하지 않습니다.
XDG_CONFIG_HOME/SDX_LLM_CONFIG를 사용하는 경우 Unity와 Gateway의 설정 경로가 같아야 합니다.
키 요청이나 유료 클라우드 폴백은 없습니다.

## 7. Unity를 움직이지 않고 실제 모델 검사

새 터미널에서 실행합니다.

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
python3 "$UNITY_PROJECT/Tools/SDxLLMGateway/probe.py" --health
python3 "$UNITY_PROJECT/Tools/SDxLLMGateway/probe.py" --text "테이블 위에 있는 책을 서재에 있는 책꽂이에 꽂아라"
python3 "$UNITY_PROJECT/Tools/SDxLLMGateway/probe.py" --text "책 가져다 꼽아줘"
python3 "$UNITY_PROJECT/Tools/SDxLLMGateway/probe.py" --live-eval --report "$HOME/Downloads/SDx_Ollama_Evaluation.json"
```

명확한 명령: decision=execute, scenario_id=scenario_1 기대.
목적지 생략 명령: decision=reject, reason_code=missing_destination 기대.
이것은 기대 결과이며 실제 모델에서 검증됐다는 주장과 다릅니다.
`--live-eval`의 10개 예문을 확인하세요. 실패 항목이 있으면 보고서에 그대로 기록되며 성공으로 처리하지 않습니다.
probe는 Unity 제어기에 연결되지 않으므로 로봇을 움직이지 않습니다.

## 8. Unity 실행

- 정상 동작하던 MainScene을 엽니다. 객체를 다시 생성하지 않습니다.
- Tools → SDx → LLM → Validate Current Setup.
- Tools → SDx → LLM → Run Local Policy Self Checks.
- Play → Send Command → 자연어 문장 → Enter.
- Prompt 그룹에서 해석 결과를 확인합니다. 승인된 경우에만 기존 상황1이 리셋 후 실행됩니다.
- 직접 `상황1`은 Ollama/Gateway 없이도 사용 가능합니다. `상황2`는 여전히 실행되지 않습니다.
- 거부된 입력은 새 실행을 하지 않지만 이미 진행 중인 로봇의 정지 명령은 아닙니다.

Scenario 1 Prepare, 카메라 생성, World/MJCF Import는 다시 실행하지 않습니다.

## 9. 판정과 오프라인 사용

기존 목적지 필수 규칙과 원문 인용 검사를 유지합니다. 앞 문장이나 장면을 근거로 목적지를 추측하지 않습니다.
모델 응답은 JSON Schema로 제한하고 Gateway와 Unity에서 다시 검사합니다.
자연어를 규칙만으로 자동 승인하거나, 오류 시 상황1로 우회하지 않습니다.
작은 LLM의 한국어 의미 이해는 항상 정확하지 않습니다. 실제 live-eval과 작업용 예문으로 확인해야 합니다.

다운로드 완료 후 Wi-Fi/인터넷을 끊고 Gateway와 Ollama를 실행한 상태로 같은 probe와 Unity 명령을 시험할 수 있습니다.
외부망이 끊겨도 PC 내부 127.0.0.1 통신은 사용합니다. 네트워크 장치 전체를 비활성화할 필요는 없습니다.
LLM 요청에 따른 외부 API 토큰 요금은 없지만 PC의 CPU/GPU/RAM과 전력을 사용합니다.

## 10. 성능과 설정

기본 모델 qwen3:1.7b, think=false, temperature=0, num_ctx=8192, num_predict=768.
모델 호출은 한 번에 하나입니다. keep_alive=0으로 응답 후 모델 메모리를 해제하도록 요청합니다.
Unity의 물리 시뮬레이션을 고려한 기본값이며, 다음 요청의 모델 재로딩 시간은 늘 수 있습니다.
Unity 네트워크 요청은 비동기이며 CPU 추론 자체는 Unity와 PC 자원을 공유합니다.

설정을 바꾸려면 Gateway를 Ctrl+C로 종료하고 같은 터미널에서 다시 실행합니다.

```bash
# 예: 더 큰 로컬 모델을 명시적으로 시험 (별도 모델 다운로드가 선행되어야 함)
ollama pull qwen3:4b
export SDX_OLLAMA_MODEL="qwen3:4b"
bash "$UNITY_PROJECT/Tools/SDxLLMGateway/start_gateway.sh"
```

기본으로 복귀: `unset SDX_OLLAMA_MODEL` 후 재시작.
선택 환경변수: SDX_OLLAMA_TIMEOUT(기본 180초), SDX_OLLAMA_KEEP_ALIVE(0~300초), SDX_OLLAMA_THREADS(0=기본), SDX_OLLAMA_CONTEXT(4096~16384).
Gateway 설정의 요청 대기시간을 Unity가 읽으므로 이전 Scene에 저장된 40초 값 때문에 조기에 취소되는 문제를 줄였습니다.
새 명령이나 Play 종료 후 늦게 돌아온 이전 응답은 실행하지 않습니다. 취소해도 이미 시작된 Ollama 계산이 즉시 중단된다고 보장하지 않습니다.

## 11. 오류 확인

- API 키를 묻는 경우: 이전 스크립트/다른 프로젝트 경로인지 확인. 새 스크립트는 키 입력 코드가 없습니다.
- Address already in use/8765: 이전 Gateway 터미널 Ctrl+C. 다른 프로세스를 무조건 kill하지 마세요.
- ollama_unavailable: `systemctl status ollama --no-pager`, `curl --noproxy '*' http://127.0.0.1:11434/api/version`.
- ollama_model_not_installed: `ollama list` 및 `ollama pull qwen3:1.7b`.
- ollama_http_400: Ollama의 API/think/schema 지원 버전 확인. 공식 설치 명령으로 업데이트 검토.
- ollama_timeout: 시스템 RAM/VRAM과 CPU 사용 확인. `nvidia-smi`, `free -h`, `ollama ps`.
- 기타 모델 오류: `journalctl -u ollama -n 60 --no-pager`.
- BASELINE/Local edits 오류: 설치가 중단되었으므로 해시 검사를 지우거나 강제로 덮어쓰지 마세요.

GPU 드라이버를 이 패치에서 변경하지 않습니다. GPU 호환성은 Ollama 공식 문서를 확인하세요.

## 12. 복원 및 검증 한계

Gateway와 Unity를 종료한 뒤 설치 때 표시된 백업 경로를 지정합니다.

```bash
python3 "$HOME/Downloads/SDx_Ollama_Local_v1_0/Tools/restore.py" --backup "$HOME/SDx_Ollama_Backups/실제_백업폴더"
```

설치 후 추가 수정된 파일을 덮어쓰지 않도록 검사를 합니다. 복원하면 이전 OpenAI Gateway가 돌아올 수 있습니다.
시스템에 설치한 Ollama나 systemd 설정은 프로젝트 코드 복원과 별개이며 자동 삭제하지 않습니다.

이 패키지 제작 환경에서는 실제 Ollama 바이너리와 Qwen 모델을 다운로드/구동하지 못했습니다.
외부 다운로드 DNS/네트워크 접근이 실패했으며 Unity Editor/C# 컴파일러도 없습니다.
오프라인 테스트의 모의 모델 응답을 실제 한국어 정확도로 표현하지 않습니다.
실제 모델 평가는 동봉한 probe --live-eval로 수행합니다. 자세한 테스트 결과는 다운로드한 패키지의 Docs/VALIDATION_KO.md를 참조하세요.

## 공식 참고자료

- Linux 설치: https://docs.ollama.com/linux
- 로컬 API 인증: https://docs.ollama.com/api/authentication
- Chat API: https://docs.ollama.com/api/chat
- Structured Outputs: https://docs.ollama.com/capabilities/structured-outputs
- 클라우드 비활성화/메모리 관리: https://docs.ollama.com/faq
- 모델: https://ollama.com/library/qwen3:1.7b
- 하드웨어: https://docs.ollama.com/gpu

모델/프로그램 가중치·바이너리·폰트·API 키는 이 ZIP에 포함하지 않습니다.