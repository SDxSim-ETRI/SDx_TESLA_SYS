# Unity Play에 맞춰 LLM 자동 실행·종료 — v1.0

기준 프로젝트: SDx_TESLA_SYS_SimStudio(2).zip, Unity 2022.3.64f1 / Ubuntu 22.04.

## 1. 평소 사용 방법

1. 처음 한 번만 Ollama와 `qwen3:4b`를 별도로 설치·다운로드해 둡니다. 기존 PC에 이미 준비되어 있으면 재설치하지 않습니다.
2. 예전에 수동으로 실행한 `start_gateway.sh` 터미널이 열려 있으면 Ctrl+C로 닫습니다.
3. Unity에서 기존 MainScene을 열고 **Play**를 누릅니다.
4. Prompt 결과 영역에 **LLM 준비 완료 (qwen3:4b)**가 나타나면 자연어 명령을 입력합니다.
5. **Play를 끄면 이번 Play가 시작한 Gateway와 Ollama 프로세스를 함께 정리**합니다.

이후에는 별도 터미널이나 `.sh` 실행이 필요 없습니다. 시작 스크립트는 프로젝트 `Tools/SDxLLMGateway`에 포함되어 있습니다. Scene에 새 컴포넌트를 붙이거나 기존 준비 메뉴를 재실행할 필요가 없습니다.

`준비 완료`는 서비스 연결과 설치된 모델의 메타데이터 확인이 끝났다는 뜻입니다. 첫 자연어 요청 때 모델을 GPU/RAM에 불러오므로 첫 응답은 더 오래 걸릴 수 있습니다. Play 준비 중 입력한 자연어는 자동 실행 예약하지 않으며, 준비 완료 후 다시 입력합니다. 직접 `상황1` 입력은 기존처럼 LLM 준비 여부와 무관하게 동작합니다.

## 2. 기본 실행 구조와 종료 범위

| 구성 | 기본 주소 | Play 시작 | Play 종료 |
| --- | --- | --- | --- |
| 프로젝트 Gateway | `127.0.0.1:8765` | 자동 시작 | 이번 Play가 시작했다면 종료 |
| Play 전용 Ollama | `127.0.0.1:11435` | 설치된 실행 파일로 별도 `ollama serve` 시작 | 이번 Play가 시작했다면 하위 추론 프로세스까지 정리 |
| 기존 Ollama systemd 서비스 | 보통 `127.0.0.1:11434` | 설치 상태 유지 | 자동으로 종료·재설정하지 않음 |

전용 Ollama를 사용하므로 기존 시스템 서비스를 관리자 권한으로 껐다 켤 필요가 없습니다. 다른 프로그램이 사용하는 서비스도 영향을 받지 않습니다. `systemctl is-active ollama`는 기존 systemd 서비스의 상태이며, 이 Play 전용 서비스의 상태를 뜻하지 않습니다.

기존 수동 Gateway가 **같은 모델·정상 인증·로컬 공급자**로 실행 중이면 기본 설정상 재사용할 수 있습니다. 이때 해당 외부 Gateway와 그 Ollama는 Play의 소유가 아니므로 종료하지 않습니다. **Play 전용 시작/종료를 사용하려면 기존 수동 Gateway 터미널을 한 번 닫은 뒤 Play**하세요. 외부 Gateway의 모델이 다르거나 포트만 차지하는 프로그램이 있으면 오류를 표시하고 중단합니다.

종료는 Unity 화면을 기다리게 하지 않고 백그라운드에서 처리합니다. 일반적으로 곧 종료되며, 응답하지 않는 소유 프로세스에는 기본 5초 유예 뒤 강제 종료가 적용됩니다. 바로 다시 Play하면 이전 정리가 끝날 때까지 잠시 기다린 뒤 새로 시작합니다. 자식 프로세스를 이름으로 찾아 일괄 종료하는 명령은 사용하지 않습니다.

Play 종료, Editor 종료, 스크립트 도메인 재로드에서 정리를 요청합니다. Unity가 비정상 종료되어도 표준입력 연결 종료와 부모 프로세스 확인으로 정리를 요청합니다. OS 강제 전원 차단처럼 프로그램 자체가 실행되지 못하는 상황을 처리한다는 뜻은 아닙니다.

## 3. 별도로 필요한 설치

- Ubuntu의 `bash`, Python 3.10 이상.
- Ollama 실행 파일.
- Ollama에 다운로드되어 있는 로컬 모델 `qwen3:4b`.

현재 사용 중인 PC에서 Qwen3 4B가 이미 동작했다면 기존 설치를 그대로 사용합니다. 이 패치는 `apt`, `pip`, Ollama 설치, 모델 다운로드를 자동 실행하지 않습니다. API 키 입력이나 외부 API 연결도 추가하지 않습니다.

설치 여부는 일반 터미널에서 `ollama --version`, 기존 Ollama 서비스에 대한 `ollama list`로 확인할 수 있습니다. 모델이 없는 경우에만, 별도 설치 과정에서 기존 서비스가 실행된 상태로 `ollama pull qwen3:4b`를 실행하세요. Play 전용 서버는 모델을 다운로드하지 않습니다.

## 4. 설치된 모델 탐색과 원본 보호

기본적으로 다음 순서로 읽을 수 있는 기존 모델 파일을 찾습니다.

1. `play_settings.json`의 `models_path`가 있으면 그 경로.
2. 환경변수 `OLLAMA_MODELS`가 있으면 그 경로.
3. 위 두 값이 없으면 `~/.ollama/models`, `/usr/share/ollama/.ollama/models`, `/var/lib/ollama/.ollama/models`, `/var/lib/ollama/models` 순서.

선택 모델의 manifest와 연결된 blob 파일을 실제로 열 수 있는지 확인합니다. 전용 서버에는 임시 모델 디렉터리를 주며, 작은 manifest만 복사하고 대용량 가중치는 기존 파일에 대한 심볼릭 링크로 읽습니다. 모델을 다시 내려받거나 수 GB의 가중치를 복사하지 않습니다. 임시 디렉터리는 해당 서비스 종료 후 정리합니다.

`OLLAMA_NOPRUNE=1`과 전용 모델 디렉터리로 원본 모델 저장소에 대한 서버의 정리·이전 작업을 분리합니다. 원본 디렉터리 권한을 자동 변경하지 않습니다. 권한 때문에 읽지 못하면 자신의 계정에서 읽을 수 있는 기존 저장소를 `models_path`에 지정하거나, 아래 `reuse` 모드를 명시적으로 선택할 수 있습니다.

## 5. 설정 파일

`Tools/SDxLLMGateway/play_settings.json`을 수정하고 다음 Play에서 적용합니다. 기본 사용에는 수정이 필요 없습니다.

```json
{
  "model": "qwen3:4b",
  "ollama_mode": "private",
  "gateway_port": 8765,
  "private_ollama_port": 11435,
  "shared_ollama_port": 11434,
  "ollama_executable": "",
  "models_path": "",
  "reuse_external_gateway": true,
  "startup_timeout_seconds": 90,
  "shutdown_timeout_seconds": 5,
  "lock_wait_seconds": 15,
  "keep_alive_seconds": 300
}
```

| 설정 | 의미 |
| --- | --- |
| `model` | Play에서 사용할 설치된 모델. 기본값 `qwen3:4b`가 이전 터미널의 모델 환경변수보다 우선합니다. |
| `ollama_mode` | `private`: 전용 Ollama 시작·종료. `reuse`: 11434의 기존 Ollama를 재사용하고, 없으면 해당 포트에 직접 시작합니다. 직접 시작한 프로세스만 종료합니다. |
| `ollama_executable` | Ollama 위치 자동 탐색이 실패할 때 실제 실행 파일의 절대 경로. 예: `/usr/local/bin/ollama`. |
| `models_path` | 설치된 Ollama 모델 디렉터리. `blobs`, `manifests`가 바로 아래 있는 폴더입니다. |
| `reuse_external_gateway` | 검증된 외부 수동 Gateway 재사용 허용. `false`이면 실행 중인 Gateway가 있을 때 중단합니다. |
| `startup_timeout_seconds` | Ollama 시작 단계와 Gateway 시작 단계 각각의 제한 시간. 실제 명령 추론 제한 시간과는 별개입니다. |
| `shutdown_timeout_seconds` | 소유 프로세스에 종료 신호를 보낸 후 강제 종료 전 유예 시간. |
| `lock_wait_seconds` | 빠른 재-Play 때 이전 Play 정리를 기다리는 시간. |
| `keep_alive_seconds` | Play 소유 Ollama가 응답 뒤 모델을 메모리에 유지하는 시간. 기본 300초. Play 종료 시에는 서비스도 종료됩니다. 기존 공유 Ollama를 재사용할 때 새 Gateway 요청은 0초로 설정합니다. |

전용 포트가 이미 사용 중이면 `private_ollama_port`를 비어 있는 다른 로컬 포트로 바꿀 수 있습니다. Gateway 주소는 자동 생성된 사용자 설정 파일로 Unity에 전달됩니다.

## 6. 로그와 진단

Unity 메뉴:

- **Tools > SDx > LLM > Open Play Service Log**: 이번 Play 로그 또는 로그 폴더 열기.
- **Tools > SDx > LLM > Show Play Service Status**: 현재 서비스 상태와 소유 여부 표시.
- 기존 **Validate Current Setup**, **Run Local Policy Self Checks**도 사용 가능합니다.

로그 위치: `<프로젝트>/Logs/SDxLLM/`.

- `<시각>_<세션ID>.log`: 감독 스크립트·Ollama·Gateway의 실행 로그.
- 같은 이름의 `.json`: 시작/준비/오류/종료 상태와 소유 PID. 인증 토큰은 포함하지 않습니다.
- 같은 이름의 `.stop`: Play 종료 요청 표식.

여러 Play의 로그를 구분하기 위해 세션별로 보존합니다. 오래된 로그는 Play가 정지된 상태에서 정리할 수 있습니다. 인증 토큰 파일은 기존 사용자 설정 위치인 `~/.config/sdx-simstudio/intent-gateway.json`이며 소스나 Scene에 저장하지 않습니다. Play가 만든 토큰 파일은 해당 세션이 종료될 때만 삭제합니다.

| 오류 | 확인할 내용 |
| --- | --- |
| `ollama_not_installed` | `ollama --version`, 실행 파일 위치, `ollama_executable`. |
| `ollama_model_not_installed` | Qwen3 4B 설치 여부와 `models_path`. 모델 목록에 보이더라도 현재 사용자에게 파일 읽기 권한이 없으면 전용 서비스가 읽지 못할 수 있습니다. |
| `model_store_permission` | 설치 모델 폴더 읽기 권한 또는 기존 서비스의 `reuse` 모드. |
| `gateway_port_in_use` | 이전 `start_gateway.sh` 터미널을 Ctrl+C로 종료했는지, 같은 모델인지 확인. |
| `gateway_config_in_use` | 같은 사용자 설정 파일을 다른 포트의 실행 중인 Gateway가 사용 중. 해당 실행을 먼저 정리하거나 별도 설정 경로 사용. |
| `ollama_private_port_in_use` | 11435를 사용하는 외부 프로그램 또는 다른 프로젝트. 기존 프로세스를 자동 종료하지 않으므로 포트나 실행 상태를 확인. |
| `another_play_session` | 다른 Unity 프로젝트/Editor의 Play가 같은 로컬 설정을 사용하는지 확인. |
| `ollama_start_failed`, `gateway_start_failed` | `.log`에서 구체적인 설치·모델·드라이버·포트 오류 확인. |

오류 시 자연어를 임의의 `상황1` 실행으로 바꾸지 않습니다. 기존 시나리오의 승인 조건과 직접 `상황1` 경로는 유지합니다.

## 7. 사용자 PC에서 확인할 순서

1. 이전 수동 Gateway 터미널을 닫고 Play합니다.
2. `LLM 준비 완료 (qwen3:4b)`를 확인합니다.
3. `테이블 위에 있는 책을 서재에 있는 책꽂이에 꽂아라`를 입력하여 기존 시나리오 실행을 확인합니다.
4. Play를 종료하고 `.json`의 `status: stopped`와 기록된 소유 프로세스 종료를 확인합니다.
5. Play를 다시 켜서 준비 완료와 입력을 확인합니다.

실제 모델의 기존 10문장 평가가 필요하면 **Play 중** 아래를 실행합니다. probe는 Unity에 명령을 보내지 않으며 로봇을 움직이지 않습니다.

```bash
UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
python3 "$UNITY_PROJECT/Tools/SDxLLMGateway/probe.py" --health
python3 "$UNITY_PROJECT/Tools/SDxLLMGateway/probe.py" --live-eval \
  --report "$HOME/Downloads/SDx_Ollama_Play_Evaluation.json"
```

테스트 때문에 Play와 probe가 동시에 모델 분류 요청을 보내면 `busy` 응답이 나올 수 있으므로 평가 중에는 GUI에서 새 자연어 요청을 제출하지 않습니다.

## 8. 지원 범위와 검증 구분

주 대상은 **Ubuntu 22.04의 Unity Editor Play**입니다. Linux standalone도 실행 파일 옆에 `Tools/SDxLLMGateway`를 별도로 배치하면 같은 경로를 사용할 수 있도록 작성했지만, 빌드 복사 자동화와 실제 빌드 실행 검증은 이번 패치 범위에 포함하지 않습니다. Windows/macOS 자동 실행은 제공하지 않습니다.

이 패치를 만든 환경에는 Unity Editor와 실제 Ollama/Qwen3 모델이 없습니다. Python·Bash 검사, 모의 Ollama를 이용한 실제 프로세스 시작/종료 검사, 설치/백업/복원 검사를 실제 Unity 컴파일·MuJoCo 물리·Qwen3 의미 정확도 검증과 구분합니다. 패키지의 `VALIDATION_KO.md`에 수행 결과를 기록합니다.

## 공식 참고자료

- Ollama 환경변수·모델 저장 위치: https://docs.ollama.com/faq
- Linux 설치/서비스: https://docs.ollama.com/linux
- Unity 도메인 재로드 설정: https://docs.unity3d.com/2022.3/Documentation/Manual/DomainReloading.html
- Unity Play 상태 이벤트: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorApplication-playModeStateChanged.html

