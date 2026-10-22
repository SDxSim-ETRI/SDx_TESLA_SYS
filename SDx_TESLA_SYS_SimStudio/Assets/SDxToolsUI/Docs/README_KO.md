# SDx Tools UI v1.1.0 — 실행 버튼·한 줄 입력창·한글 입력 수정

## 1. 변경 내용

기존 GUI의 색상, Tools 제목, 최소화·최대화, 그룹과 기존 이벤트는 유지합니다.

- ROS Functions 오른쪽에 `Run` 버튼을 추가했습니다. 시작할 때 비활성화됩니다.
  `Connect` 클릭만으로 활성화하지 않으며, 실제 연결 성공 후 외부 코드에서
  `SetRosConnected(true)`를 호출해야 합니다. 연결 해제 시에는 `false`를 전달합니다.
  이번 패키지에는 실제 ROS/Teleop 구현이 들어 있지 않습니다.
- Gist Folder Path는 직접 편집 가능한 한 줄 입력창입니다.
- Instructions도 한 줄 입력창으로 변경했습니다.
- 경로·지시문·하단 Command의 입력창 높이와 스타일을 통일했습니다.
  높이는 기준 해상도에서 32이며 Interface Scale에 따라 함께 조절됩니다.
- Linux에서 사용할 수 있는 내부 두벌식 한글 입력을 추가했습니다.
  이는 운영체제 IME의 수리가 아니라 GUI 내부의 영문 키→한글 조합 기능입니다.

## 2. 기존 프로젝트에 덮어쓰기

**Play를 종료하고 Scene을 저장한 뒤 Unity Editor를 닫으세요.**
기존 `Assets/SDxToolsUI` 폴더를 삭제하지 말고, ZIP의 같은 폴더를 덮어씁니다.
기존 `.meta` 파일을 유지하므로 현재 Scene의 SDx_Tools_UI를 다시 만들 필요가 없습니다.
새 스크립트도 필요하므로 SDxToolsPanel.cs 하나만 교체하지 마세요.

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
export DOWNLOAD_DIR="$(xdg-user-dir DOWNLOAD)"
export GUI_ZIP="$DOWNLOAD_DIR/SDx_Tools_UI_Unity2022_v1_1.zip"

if [ -f "$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt" ] && \
   [ -d "$UNITY_PROJECT/Assets/SDxToolsUI" ] && \
   [ -f "$GUI_ZIP" ]; then
    BACKUP="$DOWNLOAD_DIR/SDxToolsUI_backup_$(date +%Y%m%d_%H%M%S)"
    if cp -a "$UNITY_PROJECT/Assets/SDxToolsUI" "$BACKUP"; then
        unzip -o "$GUI_ZIP" -d "$UNITY_PROJECT/Assets"
    fi
else
    echo "Check the project path, existing SDxToolsUI folder and ZIP path."
fi
```

다운로드 파일명이 브라우저에 의해 변경되었다면 GUI_ZIP을 실제 경로로 수정하세요.
Unity를 다시 열고 컴파일을 기다립니다. 기존 SDx_Tools_UI를 선택하여 Play로 확인하세요.
`Tools > SDx > Create Tools UI`를 다시 실행할 필요는 없습니다.

이 업데이트는 기존 Scene, MuJoCo, Packages, ProjectSettings, 모델 파일을 교체하지 않습니다.
기존 패키지의 .cs 파일 이름을 바꿔 복사본을 Assets 안에 남기지 마세요.
같은 클래스가 두 번 정의되어 컴파일 오류가 발생할 수 있습니다.

## 3. 한글 입력 방법 — Ubuntu + Unity 2022.3

Inspector에서 `Text Input Backend = Auto`를 유지하면 Linux에서는 내부 두벌식을 사용합니다.
운영체제의 입력 소스는 **영어(EN)**로 둡니다.
GUI의 `EN` 버튼을 눌러 `KR`로 바꾼 다음 한글 자판처럼 입력하세요.

EN/KR 버튼 위치:
- Scene 그룹의 Instructions 제목 오른쪽
- 하단 Command 제목 표시줄 오른쪽

입력창에 포커스가 있을 때 **F2**로 전환할 수도 있습니다.
Shift+Space도 처리하지만 운영체제가 이 조합을 먼저 가로챌 수 있으므로 F2를 우선 사용하세요.
한/영 전용 키의 동작은 운영체제와 Unity의 키 전달 방식에 따라 달라 여기서는 보장하지 않습니다.

- EN: 영문 입력
- KR: 표준 두벌식 한글 입력
- `gksrmf` 자판 순서 → `한글`
- `cordmf wlqdj wntpdy` 자판 순서 → `책을 집어 주세요`
- 입력 후 Enter → 마지막 조합 글자를 포함해 제출하고 하단 창을 닫음
- Esc 또는 x → 입력창 닫기
- 복사·붙여넣기와 일반 커서/선택 편집은 Unity TextField를 사용
- 조합 중 Backspace는 마지막 조합 키를 취소
- 방향키·마우스 이동·단축키·포커스 전환은 현재 조합을 확정

이 방식은 현대 두벌식 한글만 대상으로 합니다. 세벌식, 한자 변환, 자동완성,
운영체제 입력기 설정 동기화는 포함하지 않습니다.
OS 입력 소스를 영어로 유지한 상태에서 GUI의 EN/KR로 전환해야 중복 조합을 피할 수 있습니다.

처음부터 KR로 시작하려면 **Play가 아닌 상태에서**
SDx_Tools_UI의 `Start In Korean Mode`를 켜고 Scene을 저장한 뒤 다시 Play하세요.
현재 모드는 위 두 EN/KR 버튼이 공유하며 경로·지시문·Command에 적용됩니다.
따라서 영문 경로를 직접 입력할 때는 EN으로 전환하세요.

`System Ime`는 OS IME를 사용하는 선택지입니다. Ubuntu/Unity 2022.3에서는
그 옵션만으로 한글 입력 문제가 해결된다고 보장하지 않으므로 Auto를 권장합니다.

## 4. 글자가 네모로 표시될 때

입력 조합과 글꼴은 별개입니다. Console에 한글이 정상 전달되는데 GUI가 네모라면
`Use Installed Font Fallback`을 켜고 PC의 한국어 글꼴을 확인하세요.

Ubuntu에서는 필요할 때 다음으로 Noto CJK 글꼴을 설치한 뒤 Unity를 완전히 재시작할 수 있습니다.

```bash
sudo apt update
sudo apt install fonts-noto-cjk
```

패키지는 실행 PC의 Noto Sans CJK KR, Noto Sans KR, NanumGothic 등의 글꼴을 시도합니다.
프로젝트가 사용 가능한 한글 Font 자산을 보유했다면 `Interface Font`에 직접 지정해도 됩니다.
**이 ZIP에는 폰트 파일을 포함하지 않았습니다.**

## 5. 실행 버튼 연동

실제 ROS 연결 성공을 확인한 이후 Unity 메인 스레드에서:

```csharp
toolsPanel.SetRosConnected(true);
```

연결이 끊기면:

```csharp
toolsPanel.SetRosConnected(false);
```

Run 클릭 시 `On Ros Function Requested` 이벤트에 ROS/로봇/기능 문자열을 전달합니다.
실제 ROS 연결이 없는 현재 상태에서는 Run이 계속 회색이어야 정상입니다.
공개 `ExecuteSelectedRosFunction()` 메서드에도 연결 상태 검사를 넣었습니다.
Disconnect 클릭 및 컴포넌트 비활성화 시에는 안전하게 연결 상태를 해제합니다.

`Docs/IntegrationExample.cs.txt`는 추후 연동 예제이며 자동 컴파일되지 않습니다.
네트워크 수신 스레드에서 UnityEvent를 직접 실행하지 말고 Unity 메인 스레드로 전달하세요.

## 6. 폴더·지시문 입력

폴더 선택 버튼을 그대로 사용할 수 있고, 바로 아래 입력창에 경로를 직접 써도 됩니다.
길어진 경로는 한 줄 입력창 내부에서 가로로 이동해 편집합니다.
직접 입력한 경로는 Enter, 다른 텍스트 입력창으로 포커스 이동, Generate 시 확인합니다.
잘못된 경로를 입력해도 텍스트를 자동 삭제하지 않으며 Generate를 막고 메시지를 표시합니다.
`~/...` 형태를 인식합니다. 셸 변수나 명령을 실행하는 기능은 없습니다.

지시문은 한 줄이며 Enter로 줄바꿈하지 않습니다. 실행은 Generate 버튼을 사용합니다.
여러 줄을 붙여넣으면 줄바꿈과 탭을 공백으로 정리합니다.
하단 Command는 Enter로 제출·숨김 처리하고 실제 로봇이나 AI에 자동 실행하지 않습니다.

## 7. 확인 순서

1. Unity 컴파일 오류가 없는지 확인합니다.
2. `Tools > SDx > Run Text Input Self Checks`로 실제 C# 로직을 검사합니다.
3. Play 후 ROS Functions 오른쪽 Run이 비활성화되어 있는지 확인합니다.
4. 경로와 Instructions가 같은 높이의 흰색 한 줄 입력창인지 확인합니다.
5. Send Command를 누르고, OS 영어 상태에서 GUI EN→KR 또는 F2로 전환합니다.
6. `책을 집어 주세요`를 입력하고 Enter를 눌러 하단 창이 사라지는지 확인합니다.
7. Console의 `[SDx Tools] Prompt submitted: ...`에 마지막 글자까지 있는지 확인합니다.
8. 영문 혼합, Backspace, 커서 이동, 선택 덮어쓰기, Ctrl+V도 실제 화면에서 확인합니다.

## 8. 검증 범위

이 환경에서 실행한 것은 소스 정적 검사, 레이아웃 수치 검사, C#의 문자 매핑을 사용한
별도 Python 참조 조합 검사입니다. Unity Editor 및 C# 컴파일러는 없어 실제 C# 컴파일,
Unity 렌더링, Ubuntu 키보드 입력 전달까지 실행한 것은 아닙니다.

패키지에 포함한 Unity 메뉴 검사는 실제 C# 조합기를 사용해 모든 현대 한글 음절
11,172개와 주요 입력·연결 상태 사례를 검사하도록 작성했습니다.
이 메뉴 검사 자체도 여기서는 실행하지 못했습니다. 자세한 범위는 TEST_STATUS.md를 확인하세요.

실제 ROS, Teleop, 지시문 기반 Scene 생성, 로봇 명령 실행은 이번 수정 범위에 없습니다.


## v1.2: Linux 한글 낱자 분리 수정

- Unity 2022.3 Linux의 IMGUI에서 운영체제 IME가 Unicode 호환 자모(`ㅎ`, `ㅏ`) 또는
  현대 조합 자모를 전달하는 경우, 이전 영문 물리 키 전용 두벌식 입력기가 이를 처리하지 못할 수 있었습니다.
- `SDxHangulComposer.cs`가 영문 키 입력과 Unicode 자모 입력을 모두 해석합니다.
- IMGUI 내부가 이미 분리된 자모 문자열을 입력한 경우를 대비해 `SDxKoreanTextField.cs`가
  한글 음절로 복원하고 커서/선택 위치를 가능한 범위에서 보정합니다.
- Linux Unity 2022.3에서 사용하는 내부 두벌식 방식으로, **OS IME 자체를 대체하거나
  Unity Editor 버그를 전역적으로 수정하지는 않습니다.**
- 검사: `Tools > SDx > Run Text Input Self Checks`를 다시 실행하십시오.
- 현재 PC에서는 **OS 입력 소스를 English로 둔 뒤, 게임 GUI의 KR 버튼을 켜는 방식**을
  우선 권장합니다. OS 한글 입력이 이미 켜진 상태에서도 자모를 보정하도록 작성했지만,
  모든 IBus/Fcitx/Unity 이벤트 조합을 실기기에서 테스트한 것은 아닙니다.
- GUI 외형/크기/ROS Run 연결 정책, MuJoCo 프로젝트 설정은 그대로 유지합니다.
