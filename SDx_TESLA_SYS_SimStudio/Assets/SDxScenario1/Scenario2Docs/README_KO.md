# SDx Scenario 2 v1.0 — 책을 든 채 닫힌 미닫이문 열기

## 이번 패치

현재 정상 작동하는 상황1 제어기를 확장합니다. 기존 SDx_Scenario1 객체와 GUI의 Command 연결을 그대로 사용하며, 두 번째 로봇 제어기를 만들지 않습니다.

- `상황1`: 기존처럼 두 문을 열린 상태로 리셋하고 책을 운반합니다.
- `상황2`: 두 문을 닫고 로봇·책·관절·속도를 초기화합니다. 왼팔로 책을 잡아 운반하다 오른팔로 001 문의 손잡이를 잡고, 본체를 레일과 나란히 이동시켜 문을 엽니다. 손잡이를 놓고 오른팔을 복귀한 뒤 기존 경로로 왼쪽 책장 중앙 칸에 책을 배치합니다.
- 실행 중/완료 후 다른 명령을 입력해도 기존 실행을 중복 생성하지 않고 리셋 후 해당 시나리오를 시작합니다.
- Play만으로 작업은 시작하지 않습니다. Play 초기 대기에서는 기존처럼 문이 열려 있을 수 있으며, 상황2 명령이 들어오면 두 문이 닫힙니다.
- 상황2에서 실제로 여는 문은 `aws_robomaker_residential_FoldingDoor_01_FoldingDoor_01_001_Sliding`입니다.
- `aws_robomaker_residential_FoldingDoor_01_FoldingDoor_01_002_Sliding`은 닫힌 상태로 남습니다.

대상 환경: Ubuntu 22.04, Unity 2022.3.64f1, 기존 MuJoCo 3.16.0, Fixed Timestep 0.001.

## 먼저 알아둘 검증 범위

실제 사용자 제공 libmujoco.so 3.16.0을 사용한 재구성 모델에서 상황2를 두 번 완료했고, 이어 상황1로 전환한 회귀 시험도 완료했습니다. 책/문 접촉, 리셋, 설치 절차를 검사했습니다.

**Unity Editor에서 C# 컴파일하거나 사용자의 최종 MainScene을 Play한 것은 아닙니다.** 이 환경에 Unity/C# 컴파일러가 없습니다. 네이티브 시험과 Unity 통합 실행 검증을 혼동하지 마세요. 수치와 검사 범위는 `VALIDATION_KO.md` 및 패키지 최상위 Validation 폴더에 있습니다.

## 설치

1. 현재 Scene을 저장하고 Unity를 완전히 종료합니다.
2. ZIP을 Downloads에 저장합니다.
3. 아래 명령을 한 블록으로 실행합니다.

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
export DOWNLOAD_DIR="$(xdg-user-dir DOWNLOAD)"
export SCENARIO_ZIP="$DOWNLOAD_DIR/SDx_Scenario2_v1_0.zip"

if [ -f "$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt" ] && \
   [ -f "$SCENARIO_ZIP" ]; then
    unzip -o "$SCENARIO_ZIP" -d "$DOWNLOAD_DIR" && \
    python3 "$DOWNLOAD_DIR/SDx_Scenario2_v1_0/Tools/install.py" \
      --project "$UNITY_PROJECT"
else
    echo "Check the Unity project path and downloaded ZIP path."
fi
```

정상 완료 메시지: `INSTALL COMPLETE: Scenario 2 v1.0.0`

이 설치 프로그램은 추가 Python 패키지나 ROS2를 요구하지 않습니다. 기존 파일을 해시로 확인한 후 모든 검사를 통과해야 복사를 시작합니다. 사용자가 별도로 변경한 코드가 발견되면 덮어쓰지 않고 중단합니다.

원본 파일은 프로젝트 밖 `~/SDx_Scenario2_Backups/<시각>/`에 백업합니다. 기존 .meta, Scene, .asmdef, Scenario1Plan, GUI 파일, MuJoCo 라이브러리와 모델은 유지합니다. 이전 `SDxScenario1/Tools/install.py`를 다시 실행하지 마세요. 이전 코드로 되돌아갈 수 있습니다.

## Unity 적용

1. Unity를 다시 열고 컴파일을 확인합니다.
2. 상황1이 성공한 현재 MainScene을 엽니다.
3. **Tools > SDx > Scenario 2 > Validate Current Setup**을 실행합니다.
4. Console의 `[Scenario 2] SETUP CHECK PASSED.`를 확인합니다.
5. Play > Send Command > `상황2` > Enter.

**Scenario 1의 Prepare Current Scene을 다시 실행하지 않습니다.**
**전체 World 또는 문을 다시 Import하지 않습니다.**
**기존 SDx_Scenario1 객체를 지우거나 새 Scenario2 객체를 만들지 않습니다.**
기존 객체 이름이 SDx_Scenario1인 것은 정상이며, 그 제어기가 두 명령을 처리합니다.

설정 검사는 참조와 구성을 확인할 뿐 실제 로봇의 파지/주행을 검사하는 기능은 아닙니다.

## 동작 순서

1. 두 문 닫기와 실험 리셋.
2. 상황1과 동일한 왼팔의 책 접근/파지/인출/운반 자세.
3. 기존 경로의 첫 두 경유점까지 이동.
4. 닫힌 001 문 앞에서 정지하고, 오른팔이 손잡이에 접근할 수 있게 본체를 정렬.
5. 오른쪽 그리퍼를 열고 손잡이에 접근한 뒤 닫기.
6. 오른팔의 파지 자세와 왼팔의 책을 유지하면서 본체를 레일과 나란히 뒤로 이동.
7. 문이 열린 상태에서 그리퍼를 열고 오른팔을 빼서 준비 자세로 복귀.
8. 원래 통로로 돌아와 상황1의 나머지 경로를 진행.
9. 왼쪽 책장 중앙 칸(위에서 2번째, 왼쪽에서 2번째)에 책 배치.
10. 그리퍼를 열고 왼팔을 복귀. 본체는 책장 앞에 머무름.

문 이동 거리가 최대 2.4m이므로 본체를 고정하고 팔만 끝까지 뻗는 방식이 아닙니다. 문 조작 구간에서만 본체 정렬과 레일 방향 이동이 추가됩니다. 그 외 경유점과 책 파지/배치 계획은 기존 Scenario1Plan을 그대로 사용합니다.

## 물리 처리

- 책/손잡이와 그리퍼의 실제 접촉을 사용합니다.
- 문에 Actuator를 추가하거나 문 위치를 애니메이션으로 바꾸지 않습니다.
- 책/문을 로봇의 자식으로 붙이거나 Weld로 강제 고정하지 않습니다.
- 로봇 베이스에 외력을 직접 가하지 않고 기존 바퀴 Actuator를 사용합니다.
- 실제 qpos를 쓰는 것은 명령 시작 리셋뿐입니다. IK는 별도 scratch mjData에서 계산합니다.
- 001/002 판별은 DoorSlides 배열 순서가 아니라 해당 문 루트의 이름으로 합니다.
- 002 문은 리셋 때 닫고 이후 구동하지 않습니다.
- 기존 상황1의 접촉/그리퍼 튜닝을 공유하며, 새로운 전역 물리 설정 변경은 추가하지 않습니다.
- 기존에 꺼진 HouseWallB 외벽 충돌을 새로 켜거나 다른 충돌을 끄지 않습니다.

이번 모델 배치/물리 파라미터에 맞춘 고정 실험입니다. 테이블·목표 책장·문 위치, 문 레일 방향이나 손잡이 형상을 변경하면 이번 검증 조건과 달라집니다. 이를 확인하는 진단 검사는 유지하지만, 자동 위치 보정이나 순간이동으로 성공을 가장하는 처리는 없습니다.

## UI와 카메라

기존 한글 입력, Camera 그룹, Front/Left/Right/Back 및 현재 선택 시점은 유지됩니다. 카메라 Bridge에는 상황2 리셋 명령에도 RequestSnap을 호출하는 조건만 추가했습니다. Camera Rig 자체는 교체하지 않습니다.

## 로그

상황1: `Application.persistentDataPath/SDxScenario1_LastRun.txt`
상황2: `Application.persistentDataPath/SDxScenario2_LastRun.txt`

상황2 로그에는 단계, 책 위치, 문001/002 개방량과 마지막 책 위치 오차가 기록됩니다. 최종 저장 경로는 Console에 출력됩니다.

## 변경 파일

교체:
- Assets/SDxScenario1/Core/SDxScenario1Controller.cs
- Assets/SDxRobotCameras/Runtime/SDxRobotCameraUiBridge.cs (카메라 설치 시에만)

추가:
- Assets/SDxScenario1/Core/SDxScenario2DoorSequence.cs
- Assets/SDxScenario1/Core/Scenario2Plan.cs
- Assets/SDxScenario1/Editor/SDxScenario2Tools.cs
- Assets/SDxScenario1/Scenario2Docs/ 문서

SDxScenario1Controller는 partial class로 확장했습니다. 실제 컴포넌트 클래스와 파일의 기존 GUID를 유지하므로 Scene의 저장된 참조를 다시 연결할 필요가 없습니다. 이전 int/ulong 컴파일 수정도 포함되어 있습니다.

## 복원

Unity를 종료하고 이번 설치의 백업 폴더를 지정하여 실행합니다.

```bash
python3 "$HOME/Downloads/SDx_Scenario2_v1_0/Tools/restore.py" \
  --backup "$HOME/SDx_Scenario2_Backups/실제_백업_폴더명"
```

위 마지막 경로는 설치 시 출력된 실제 백업 경로로 바꾸세요. 복원 도구는 설치 이후 다시 편집된 파일을 발견하면 중단합니다. 백업 파일은 삭제하지 않습니다. Scene과 기존 물리 모델은 이번 패치가 변경하지 않았으므로 전체 Scene을 되돌릴 필요는 없습니다.
