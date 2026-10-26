# SDx Robot Follow Cameras v1.0

현재 SDx_TESLA_SYS_SimStudio 프로젝트에 전·후·좌·우 로봇 추적 카메라를 추가합니다.
기존 Tools GUI v1.2의 스타일과 한글 입력, 상황1 시나리오는 유지합니다.

## 적용 범위

- 실제 이동하는 Husky `base_link`를 추적합니다. 정지한 Import 최상위 객체를 추적하지 않습니다.
- 앞/뒤 범퍼 위치로 로봇의 전방 축을 확인합니다. 첨부 프로젝트의 전방은 로컬 +X입니다.
- Front: 로봇 앞에서 로봇을 바라봅니다. 전방 주행 방향으로 보는 1인칭 카메라가 아닙니다.
- Left/Right: 로봇 자신의 왼쪽/오른쪽에서 로봇을 바라봅니다. 화면 기준 좌우가 아닙니다.
- Back: 로봇 뒤에서 로봇을 바라봅니다.
- 모두 위쪽에 위치하여 아래로 비스듬히 로봇 중심을 바라봅니다.
- 로봇이 회전하면 네 카메라의 기준 방향도 함께 회전합니다. 차체 pitch/roll은 따라하지 않습니다.
- 기본 시작 시점은 Front입니다. 전환은 즉시 이루어지고 선택 버튼은 파란색으로 표시합니다.
- 카메라 4개가 생성되지만 선택한 하나만 렌더링합니다. Audio Listener도 하나만 활성화됩니다.
- 영상 파일 저장이나 4화면 동시 출력, 인식 센서 기능은 추가하지 않습니다.
- 로봇과 World의 물리 설정, Actuator, 상황1 경로·파지·리셋 코드는 수정하지 않습니다.

## 1. 설치

Play 종료 → Scene 저장 → Unity 종료 후 설치합니다.
ZIP을 Downloads에 저장했다는 기준입니다.

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
export DOWNLOAD_DIR="$(xdg-user-dir DOWNLOAD)"
export CAMERA_ZIP="$DOWNLOAD_DIR/SDx_Robot_Follow_Cameras_v1_0.zip"

if [ -f "$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt" ] && \
   [ -f "$CAMERA_ZIP" ]; then
    unzip -o "$CAMERA_ZIP" -d "$DOWNLOAD_DIR" && \
    python3 "$DOWNLOAD_DIR/SDx_Robot_Follow_Cameras_v1_0/Tools/install.py" \
      --project "$UNITY_PROJECT"
else
    echo "Check the Unity project path and downloaded ZIP path."
fi
```

추가 Python 패키지, ROS, Cinemachine, Recorder는 필요하지 않습니다.

설치 스크립트는 기존 GUI가 이번 대화의 v1.2 파일과 일치하는지 확인합니다.
다르면 사용자 수정사항을 덮어쓰지 않고 중단합니다.
파일 백업은 `~/SDx_Camera_Backups/<timestamp>/`에 저장됩니다.

### 변경되는 기존 파일

`Assets/SDxToolsUI/Runtime/SDxToolsPanel.cs` 하나만 교체합니다.
GUI 버전 문자열은 1.3.0이 됩니다. Camera 그룹과 이벤트·뷰포트 정보만 추가했습니다.
기존 한글 조합과 Prompt 제출 관련 메서드 본문은 유지했습니다.
기존 `.meta` 파일은 보존됩니다. **SDxToolsUI 폴더 전체를 삭제하지 마세요.**

### 추가되는 폴더

`Assets/SDxRobotCameras/`

기존 `Assets/SDxScenario1`(컴파일 수정판 포함), `SDxDoorInteraction`,
`Packages`, `ProjectSettings`는 변경하지 않습니다.
이 설치 스크립트 자체는 Scene 파일을 바꾸지 않습니다.

## 2. 현재 Scene에 카메라 생성

Unity를 열고 컴파일이 끝나면, 현재 정상 동작하는 MainScene을 연 상태에서 다음 메뉴를 실행합니다.

**Tools → SDx → Cameras → Create or Update Robot Follow Cameras**

- Play가 꺼져 있어야 합니다.
- 확인창에서 Create / Update를 누릅니다.
- Scene 복사본이 `Assets/SDxRobotCameras/Backups/`에 저장됩니다.
- `base_link`, 범퍼 두 개, 기존 Tools UI, Main Camera를 찾아 연결합니다.
- 동일 Scene의 기존 카메라 리그가 있다면 갱신하며, 새 카메라 4개를 중복 생성하지 않습니다.
- 완료 후 Ctrl+S로 현재 Scene을 저장합니다.

결과 계층:

```text
MainScene
├── Main Camera                  # 삭제하지 않음
├── SDx_Tools_UI                  # 기존 객체 유지
├── SDx_Scenario1                 # 기존 객체 유지
├── dual_fr3_husky_unity390       # 수정하지 않음
└── SDx_RobotCameras
    ├── Camera_Front
    ├── Camera_Left
    ├── Camera_Right
    ├── Camera_Back
    └── CameraAudioListener
```

**SDx_Scenario1의 Prepare Current Scene을 다시 실행할 필요는 없습니다.**
**World·로봇을 다시 MJCF Import할 필요도 없습니다.**

## 3. 사용

Play → Game 탭을 열면 Front 카메라가 기본 표시됩니다.

```text
Prompt ▼
[ Send Command ]

Camera ▼
[ Front ] [ Left  ]
[ Right ] [ Back  ]
```

기존 Send Command에서 `상황1`을 입력하면 기존 시나리오가 그대로 실행됩니다.
로봇이 이동·회전하면 선택된 시점이 따라갑니다.
시점 전환은 로봇 동작을 중단하거나 초기화하지 않습니다.
`상황1` 리셋 입력 시 카메라 보간만 잠시 해제하여 초기 로봇 위치에 바로 맞춥니다.

Left/Right는 로봇을 뒤에서 바라볼 때의 로봇 기준 좌/우입니다.
Front를 선택하면 화면에서 보이는 로봇의 좌우와 버튼 이름의 좌우가 반대로 느껴질 수 있습니다.

기존 Main Camera는 편집 모드에서 유지됩니다. Play 중에만 렌더링을 중지하며,
Play 종료 또는 추적 리그 비활성화 시 기존 카메라와 리스너의 활성 상태를 복원합니다.
같은 Scene·같은 Display에 직접 출력하는 다른 카메라가 활성화되어 있으면 함께 일시 중지합니다.
RenderTexture를 쓰는 센서 카메라나 다른 Display의 카메라는 중지하지 않습니다.

## 4. 구도 조절

`SDx_RobotCameras`를 선택하고 Inspector에서 조절합니다.

| 항목 | 기본값 | 의미 |
|---|---:|---|
| Initial View | Front | Play 시작 시 선택할 카메라 |
| Horizontal Distance | 2.10m | 로봇 중심에서 수평 거리 |
| Look Height | 0.80m | base_link 기준 바라보는 중심점 높이 |
| Height Above Look Point | 1.40m | 중심점보다 높은 카메라 위치 |
| Field Of View | 60° | 수직 시야각 |
| Near Clip Plane | 0.05m | 가까운 물체의 잘림 거리 |
| Position Damping | 12 | 위치 추적 보간 강도, 0이면 보간 없음 |
| Heading Damping | 12 | 회전 추적 보간 강도, 0이면 보간 없음 |
| Azimuth Offset Degrees | 0° | 네 시점에 더하는 수평 방향 각도 |
| Reserve Tools Panel Space | ON | Tools 패널 오른쪽 장면 영역을 기준으로 중앙 정렬 |
| Show Camera Guides | OFF | Scene에서 카메라 위치 안내선 |

촬영 구도는 스크린샷을 참고한 기본 프리셋이며, 모든 스크린샷과 픽셀 단위로 같은 구도는 아닙니다.
위치와 화각은 Game 화면의 해상도·비율에 따라 보이는 범위가 달라집니다.

- 크게 보려면 Horizontal Distance를 줄이세요.
- 팔·책·테이블까지 넓게 보려면 Horizontal Distance 또는 Field Of View를 늘리세요.
- 더 위에서 내려다보려면 Height Above Look Point를 늘리세요.
- Play 중 조절한 값은 Play 종료 후 원복되므로, 최종 값은 Play 종료 후 입력하고 Scene을 저장하세요.

개별 Camera Transform을 직접 수정해도 Play 중에는 추적 코드가 다시 계산합니다.
구도는 위 리그 설정을 통해 조절해야 합니다.

## 5. 검증 메뉴

- **Tools → SDx → Cameras → Run Camera Math Self Checks**
  - 실제 C# 코드의 4방향 기준, 24개 회전각, 높이·거리·주시 방향 등을 검사합니다.
  - 카메라 생성 전에도 실행할 수 있습니다.
- **Tools → SDx → Cameras → Validate Current Camera Rig**
  - 추적 대상·카메라·GUI 연결을 검사합니다.
  - Play 중에는 선택 카메라 하나 및 Audio Listener 하나의 활성 상태도 검사합니다.
  - 실제 장면의 가림 여부나 로봇 물리를 검사하는 메뉴는 아닙니다.

확인 순서: Front 기본 시점 → 4버튼 전환 → 상황1 주행·회전 추적 → 상황1 재시작 →
Tools 최소화/복원 → Play 종료 후 원래 Main Camera 확인.

## 6. 범위와 제한

이번 카메라는 게임 화면을 위한 일반 Unity Camera입니다.
자동 벽 회피, 벽 투명화, MuJoCo 기반 카메라 충돌 회피는 넣지 않았습니다.
카메라와 로봇 사이에 벽·책장·로봇 상부 프레임이 있으면 가려질 수 있습니다.
그때는 다른 시점으로 전환하거나 거리·높이를 조절하세요.
이를 해결하려고 World 충돌을 끄거나 로봇 구조물을 숨기지는 않습니다.

이 환경에서는 프로젝트 소스·로봇 축·Scene 구조 분석, 수학 참조 검사,
GUI 보존 검사, 설치·재설치·메타데이터 보존 검사를 수행했습니다.
Unity Editor 및 C# 컴파일러를 사용할 수 없어 실제 Unity 컴파일과 Game 화면 렌더링은 미검증입니다.
상세한 실제 검사 결과는 `Docs/VALIDATION.md`를 참조하세요.

## 7. 제거

Play 종료 후 **Tools → SDx → Cameras → Remove Follow Cameras (Keep Main Camera)**.
추가한 리그만 제거하고 Camera 그룹을 숨깁니다. Main Camera와 상황1은 유지됩니다.
Scene을 저장하세요. 되돌리려면 Undo 또는 백업 Scene을 사용할 수 있습니다.
기존 GUI v1.2 자체로 되돌려야 한다면 파일 백업의 SDxToolsPanel.cs를 원래 경로에 복원합니다.
이때 기존 `.meta`는 유지하세요.

## 기술 참고

- Unity 2022.3 MonoBehaviour.LateUpdate:
  https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.LateUpdate.html
- Camera.rect:
  https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera-rect.html
- Camera.CopyFrom:
  https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera.CopyFrom.html
- Camera.main:
  https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera-main.html
- Audio Listener:
  https://docs.unity3d.com/2022.3/Documentation/Manual/class-AudioListener.html
