# SDx Execution Log 수정 패치 v1.1

## 적용 대상
`SDx_ExecutionLog_v1_0`을 설치한 Unity 2022.3 프로젝트용 후속 패치입니다.
이전 로그 패치를 제거하지 말고 이 패치만 추가로 설치하세요.
LLM 자동 실행/모델 접근 권한 수정 패치를 다시 설치할 필요가 없습니다.

## 변경 사항
- 로그 본문 배경의 불투명도(alpha)를 0.84에서 **0.45**로 낮춥니다. 글자는 흐리게 하지 않습니다.
- 제목줄은 alpha **0.60**으로 표시하며, 본문 배경과 중복으로 칠하지 않습니다.
- 우측 상단 고정 위치를 유지하면서 **왼쪽 테두리**, **아래쪽 테두리**,
  **왼쪽 아래 모서리의 점무늬 손잡이**를 마우스 왼쪽 버튼으로 드래그해 크기를 바꿀 수 있습니다.
- 최소화/최대화에서 복원하면 직접 조절한 크기로 돌아옵니다.
- 최소 크기는 1920×1080 기준 420×200이며, 화면/왼쪽 Tools 영역에 맞춰 제한됩니다.
- 크기는 해당 Play 세션 안에서 유지됩니다. 새 Play에서는 기본 780×390으로 시작합니다.
- 시간/구분/내용, 누적 기록, 최신 보기, 스크롤, 2,000건 보관 정책은 유지합니다.
- 키보드, 한글 입력, Enter, 복사/붙여넣기 이벤트를 로그창이 변경하거나 소비하지 않도록 했습니다.
- 기존 GUI를 보호하기 위해 로그창은 기존 Tools/Prompt 뒤에 그려집니다.
- 로그창의 실제 마우스 조작만 처리하며, 다른 GUI의 키보드 포커스를 해제하지 않습니다.
- 로그 조작 후 버튼을 놓거나 창 포커스를 잃으면 로그창이 소유한 마우스 입력만 해제합니다.

## 설치
1. Play를 멈추고 Scene을 저장한 후 **Unity Editor를 완전히 종료**하세요.
   Unity Hub는 실행 중이어도 됩니다.
2. ZIP 파일을 다운로드 폴더에 저장합니다.
3. 다음 명령을 실행합니다. `sudo`는 사용하지 않습니다.

```bash
DOWNLOAD_DIR="$(xdg-user-dir DOWNLOAD)"

unzip -o "$DOWNLOAD_DIR/SDx_ExecutionLog_Fix_v1_1.zip" -d "$DOWNLOAD_DIR" &&
python3 "$DOWNLOAD_DIR/SDx_ExecutionLog_Fix_v1_1/install.py" \
  --project "$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
```

완료 메시지:
```text
INSTALL COMPLETE: SDx_ExecutionLog_Fix_v1_1
```

프로젝트를 다시 열고 스크립트 컴파일이 끝나면 기존 Scene에서 Play하세요.
컴포넌트를 새로 붙이거나 Scene 준비/카메라 생성 메뉴를 다시 실행하지 마세요.

## 실제 변경 범위
아래 4개 파일만 수정합니다.

```text
Assets/SDxExecutionLog/Runtime/SDxExecutionLogWindow.cs
Assets/SDxExecutionLog/Runtime/SDxLogWindowLayoutState.cs
Assets/SDxExecutionLog/Editor/SDxExecutionLogSelfChecks.cs
Assets/SDxExecutionLog/README_KO.md
```

기존 `.meta`, Scene, Tools 패널 및 한글 입력기, 카메라 제어기, 로봇/문/책 제어,
LLM 실행 코드와 `Tools/SDxLLMGateway/play_settings.json`은 수정하지 않습니다.
최근 상태를 기록하는 데이터 저장소와 시나리오 연결 코드도 그대로 사용합니다.

설치기는 기존 파일의 SHA-256을 확인하고, 설치 전 파일을 프로젝트의 Assets 바깥에
백업합니다. 원본과 다른 사용자 수정이 발견되면 전체 설치를 중단합니다.
강제 덮어쓰기는 제공하지 않습니다. 같은 패치를 다시 설치하면 변경 없이 끝납니다.

## 설치 후 확인
- Front/Left/Right/Back/Free 버튼, 각 그룹 접기/펼치기가 작동하는지 확인합니다.
- Send Command 또는 Enter로 Prompt를 열고 한글/영문 입력, 지우기, 제출을 확인합니다.
- `상황1` 직접 입력 및 기존 자연어 명령을 각각 확인합니다.
- Gist Folder Path/Instructions 입력, 폴더 선택, 드롭다운 선택을 확인합니다.
- 로그창 모서리/테두리를 드래그하고 놓은 뒤 다시 Tools 버튼을 눌러 봅니다.
- 최소화/최대화/복원, 최신 보기, 마우스 휠과 스크롤바 드래그를 확인합니다.
- 로그 위에서 스크롤할 때 Free 카메라가 함께 움직이지 않는지 확인합니다.

기존 ROS Functions의 Run 등 원래 조건부로 비활성인 버튼을 강제로 활성화하지는 않습니다.
이 패치는 기존 연결/실행 조건을 변경하지 않습니다.
Prompt나 폴더 창/드롭다운이 입력을 점유한 동안에는 기존 GUI를 우선합니다.

## 복원
설치 성공 시 `Backup:` 및 `Restore:`에 실제 경로가 출력됩니다.
Unity Editor를 닫고 출력된 `Restore: python3 '.../restore.py'` 명령을 실행하면
이번 v1.1 수정만 되돌립니다. 이는 이전 v1.0 로그창 코드로 돌아가는 것이므로
사용자가 보고한 입력 문제도 다시 나타날 수 있습니다.

## 검증 범위
`VALIDATION_KO.md`를 확인하세요.
이 환경에서는 Unity Editor의 실제 컴파일·Game 화면·마우스/IME 입력은 실행하지 못했습니다.
설치/복원 검사와 소스 검사를 실제 Unity 동작 검증으로 표현하지 않습니다.
