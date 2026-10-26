# SDx Scenario 1 — 책 운반 실험

## 적용 범위

업로드된 `SDx_TESLA_SYS_SimStudio.tar.gz`와 이후 시작 위치 스크린샷을 기준으로 만든 추가 패키지입니다.
기존 GUI, MuJoCo 플러그인, libmujoco.so, World 원본을 교체하지 않습니다.
Unity 2022.3.64f1 / MuJoCo 3.16.0 / Ubuntu 22.04 / Fixed Timestep 0.001 기준입니다.

`Send Command`에서 **상황1**을 입력하고 Enter를 누르면:
리셋 → 테이블 책 파지 → 운반 자세 → 고정 우회 경로 → 왼쪽 책장 중앙 칸 삽입 → 손 빼기 → 팔 복귀를 실행합니다.
다시 입력하면 실행 도중이든 완료 후든 리셋 후 처음부터 재시작합니다.
Play만으로 책 운반을 시작하지는 않습니다. 초기 물리 자세 유지 제어는 Play부터 동작합니다.

## 설치

1. Scene을 저장하고 Unity를 종료합니다.
2. ZIP을 Downloads에 압축 해제합니다. `SDxScenario1` 폴더가 생깁니다.
3. 아래 명령을 실행합니다.

```bash
export UNITY_PROJECT="$HOME/Desktop/work/SDx_TESLA_SYS/SDx_TESLA_SYS_SimStudio"
export DOWNLOAD_DIR="$(xdg-user-dir DOWNLOAD)"
python3 "$DOWNLOAD_DIR/SDxScenario1/Tools/install.py" --project "$UNITY_PROJECT"
```

설치 프로그램은 `Assets/SDxScenario1`만 추가합니다. 기존 같은 폴더가 있으면 프로젝트 밖에 백업합니다.
설치된 MuJoCo Runtime assembly 이름도 확인하여 이 패키지의 참조에 반영합니다.
전역 Allow unsafe code 설정은 바꾸지 않습니다. Core assembly에만 unsafe 사용이 허용됩니다.

## 현재 Scene에 적용

1. Unity에서 현재 MainScene을 엽니다. Play는 종료합니다.
2. **Tools → SDx → Scenario 1 → Prepare Current Scene**을 실행합니다.
3. 확인창을 승인합니다. 먼저 현재 Scene을 `Assets/SDxScenario1/Backups/`에 복사합니다.
4. 준비가 완료되면 `SDx_Scenario1` 객체가 선택됩니다. **Ctrl+S**로 Scene을 저장합니다.
5. Play → Send Command → **상황1** → Enter.

기존 SDx_Tools_UI를 재생성하지 않습니다. 전체 AWS World도 다시 Import하지 않습니다.
기존 GUI의 이벤트에는 추가 Bridge가 구독하므로 Inspector에서 이벤트를 수동으로 연결할 필요가 없습니다.

## Prepare가 변경하는 내용

- 로봇 루트 `dual_fr3_husky_unity390`: Unity 위치 `(0.581, 0, 1.635)`, 회전 0.
- 책 `book_01306 (1)`: Books_Left에서 독립시켜 테이블 위에 배치합니다. 새 책을 복제하지 않습니다.
- 책 루트 위치: `(1.588, 0.15111069, 1.644)`, 회전 0.
- 사진의 책 Y=0.1488보다 **2.31 mm 위**입니다. 초기 테이블 관통을 피하는 조정입니다.
- 실제 책 Body 중심은 내부 Y=0.35 오프셋 때문에 `(1.588, 0.50111069, 1.644)`입니다.
- `bookshelf_right136`, `Books_Right` 비활성화.
- 두 개의 교체된 Sliding Door를 완전 개방. 원래 고정 문은 계속 비활성 상태.
- 목적지 책장 `bookshelf_left486`의 배치는 변경하지 않습니다.
- `SDx_Scenario1`에 제어기와 GUI 연결 컴포넌트 추가.

현재 중앙 칸에 있던 해당 책을 테이블로 옮기므로 목표 칸을 비워두게 됩니다.
다른 책을 중앙 칸에 새로 놓거나 경로 중간 가구를 이동하면 이번에 검증한 배치와 달라집니다.

## 리셋 및 물리 동작

상황1마다 모든 동적 객체의 초기 qpos를 복원하고 속도, 제어 입력, 이전 계산 상태를 초기화합니다.
대상 책뿐 아니라 주변 책도 실험 초기 상태로 돌아갑니다. 로봇 양팔은 검증한 준비 관절 자세로 복원됩니다.
진행 중 다시 입력하면 중복 실행이 아니라 재시작입니다.

왼쪽 FR3로 책을 잡습니다. 로봇은 기존 바퀴/팔/그리퍼 Actuator로 움직입니다.
책을 그리퍼에 붙이는 Parenting/Weld, 로봇/책 Transform 애니메이션, 충돌 비활성화로 통과시키는 처리는 없습니다.
qpos 직접 변경은 리셋 시에만 이루어집니다. 역기구학은 별도 scratch mjData에서 계산합니다.

실험에 필요한 접촉 튜닝을 Play 중 네이티브 모델에 적용합니다:
elliptic friction cone, impratio 10, 그리퍼 유지력/감쇠 보강, 대상 책 마찰 및 접촉 강성 조정.
기존 원본 모델 파일이나 Physics/Time 설정 파일을 덮어쓰지 않습니다.
구체적인 값은 `Docs/DESIGN.md`와 코드에 있습니다. 실제 로봇 하드웨어의 제원 검증값은 아닙니다.

## 목적지와 경로

왼쪽 책장의 위에서 두 번째 행 / 왼쪽에서 두 번째 열, 즉 중앙 칸입니다.
선반 지지면 높이 약 0.790 m, 책 중심 목표 Unity `(5.23, 0.924364, -4.5)`입니다.
원하는 주행 경로를 새로 검색하는 범용 계획기가 아니라, 영상의 우회 방향을 참고해 실제 충돌 형상에 맞춰 정한 경유점을 사용합니다.
책장 앞 실제 정지 오차와 운반 중 책의 작은 자세 변화를 시뮬레이터 상태로 읽어 최종 삽입 자세만 보정합니다.
카메라나 ROS2 연결은 필요하지 않습니다.

## 결과 확인

오른쪽 위 상태 표시와 Console에 단계가 출력됩니다.
완료 시 책은 중앙 칸에 남고, 그리퍼는 열린 채로 빠져나온 뒤 팔이 준비 자세로 복귀합니다.
본체는 책장 앞에 머뭅니다. 테이블 앞으로 돌아가는 작업은 포함하지 않았습니다.
마지막 실행 로그는 `Application.persistentDataPath/SDxScenario1_LastRun.txt`에 저장됩니다.
경로는 완료 로그에 출력됩니다.

## 실제 검증과 미검증 구분

- 제공된 libmujoco.so 3.16.0을 사용하여 네이티브 물리 실험을 수행했습니다.
- 업로드 Scene의 충돌 형상/배치와 사진의 변경사항을 재구성한 모델을 사용했습니다.
- 최종 보정 후 두 번의 동일 실험에서 파지, 운반, 배치, 팔 복귀 및 초기화 반복을 확인했습니다.
- 0.2초 간격의 744개 상태에서 로봇/운반책의 허용되지 않은 접촉을 별도 검사했습니다.
- 수치 결과는 `Validation/native_validation.json`에 있습니다.
- 네이티브 시험은 계산 상태를 보존한 구간 실행으로 수행했습니다.
- **이 환경에 Unity Editor와 C# 컴파일러가 없어 실제 Unity 컴파일/Play는 실행 검증하지 못했습니다.**
- 재구성 모델은 Unity가 현재 Scene에서 생성한 최종 모델 자체가 아니므로 Unity에서 같은 결과가 보장된다고 표현하지 않습니다.

이 패키지는 실제 실행 코드와 시험 결과를 포함하지만, 사용자 PC의 Unity 통합 실행 확인은 별도로 필요합니다.
코드가 임의 좌표로 순간이동시켜 성공을 가장하는 보정은 하지 않습니다.

## 복원

Play를 종료하고 `Backups`의 Scene 복사본을 열면 준비 이전 상태로 돌아갈 수 있습니다.
SDxScenario1 코드를 제거하려면 우선 Scene의 `SDx_Scenario1` 객체를 삭제하고 저장한 뒤 패키지를 삭제합니다.
기존 GUI, World, 로봇 원본 및 MuJoCo 라이브러리는 변경하지 않았습니다.
