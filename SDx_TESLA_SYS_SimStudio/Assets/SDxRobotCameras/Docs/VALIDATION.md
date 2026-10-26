# 검증 범위 — SDx Robot Follow Cameras v1.0

## 사용한 실제 자료

- 대화에 첨부된 `SDx_TESLA_SYS_SimStudio.tar.gz`
- 같은 프로젝트의 `Assets/Scenes/MainScene.unity`
- 같은 프로젝트의 `Assets/Models/FR3_Husky_Dual/dual_fr3_husky_unity.xml`
- `SDx_Tools_UI_Unity2022_v1_2_HangulFix.zip`
- 성공 보고된 Scenario 1 패키지와 컴파일 수정판 소스 (인터페이스 확인용; 변경하지 않음)
- 사용자가 제공한 전·후·좌·우 참고 스크린샷

최신의 상황1 준비 이후 Scene 전체는 다시 업로드되지 않았으므로,
파일 안의 오래된 카메라 위치를 현재 위치라고 가정하지 않았습니다.
설치 시 현재 Scene에서 base_link와 앞/뒤 범퍼를 찾고 GUI와 연결합니다.

## 직접 확인한 내용

### 로봇 추적 축

업로드된 모델 및 Unity Scene에서:
- base_link local position: (0, 0.13228, 0)
- front_bumper_link local position: (+0.48, 0.091, 0)
- rear_bumper_link local position: (-0.48, 0.091, 0)

따라서 이 모델의 전방은 로컬 +X입니다.
초기 회전이 0인 경우 Front=+X, Left=+Z, Right=-Z, Back=-X입니다.
카메라는 회전 후에도 이 로봇 기준 관계를 유지하도록 구성했습니다.

### GUI 보존

업로드 프로젝트와 GUI v1.2 ZIP의 SDxToolsPanel.cs는 SHA-256이 같습니다.

`ace27099231afce72a0a7f5dc6219c250b6b6f30f6c2d3e9b7df8f8e20a90ecd`

기존 메서드 중 DrawPanel을 제외한 52개 메서드 본문이 그대로인지 비교했습니다.
DrawPanel에는 Camera 그룹과 그 높이 계산을 추가했습니다.
한글 조합·텍스트 처리·Prompt 제출·ROS 관련 메서드는 변경하지 않았습니다.
한글 보조 소스 파일은 패키지의 덮어쓰기 대상에 포함되어 있지 않습니다.

### 코드·수치 검사

- C# 문자열·주석을 제외한 괄호 및 블록 짝 정적 검사.
- 로봇/책/문 물리 API 호출이나 제어 입력 변경 코드가 새 카메라 Runtime에 없는지 검사.
- 기본 4방향 및 5도 간격 회전각에서 수평 거리, 높이, 주시 방향, 수평선의 독립 Python 참조 계산.
- 1920×1080부터 640×360까지 UI/뷰포트 수치 확인.
- Camera 그룹이 Prompt 다음 순서인지 확인.
- 선택한 카메라만 렌더링하는 분기 및 원래 카메라/리스너 복원 경로 정적 확인.
- Scene 변경 메뉴가 delayCall에서 실행되는지 확인.

이는 C# 컴파일·Unity 렌더링·이벤트 클릭 검증을 대체하지 않습니다.
전체 자동 검사 목록은 validation_results.json에 있습니다.

### 설치 도구 검사

사용자 프로젝트가 아닌 격리된 임시 프로젝트에서 실제 install.py를 실행했습니다.
- dry-run 실행 시 파일 변경 없음.
- 실제 설치 시 GUI 한 파일과 새 카메라 폴더만 추가/갱신.
- 기존 GUI .meta 보존.
- Scenario 1, 문, 한글 조합, 설정, Scene에 둔 시험 파일 보존.
- 설치 전 GUI 파일 백업 생성.
- 동일 패키지 재설치 성공.
- 사용자가 수정한 것으로 간주되는 알 수 없는 GUI 내용은 덮어쓰기 거부.
- 최종 ZIP CRC 및 manifest SHA-256 검사.

## 사용자 Unity에서 제공되는 검사

1. Tools > SDx > Cameras > Run Camera Math Self Checks
   - 실제 C# 계산 코드로 391개 단언을 수행하도록 작성했습니다.
   - 이 환경에서 이 메뉴를 실행한 것은 아닙니다.
2. Tools > SDx > Cameras > Validate Current Camera Rig
   - 실제 Scene 참조 및 Play 중 Camera/Audio Listener 활성 수를 검사합니다.
3. Play에서 4개 버튼, 상황1 추적, 재시작, 최소화/최대화를 직접 확인합니다.

## 아직 실행하지 못한 검증

이 환경에는 Unity Editor와 C# 컴파일러가 없습니다.
따라서 아래 작업은 **미검증**입니다.
- Unity 2022.3의 실제 C# 컴파일
- 실제 Game 화면 렌더링과 버튼 클릭
- 현재 사용자 MainScene에서 상황1 동작 중 추적 영상
- 카메라와 환경 사이 가림 없는 구도 보장

수학 계산과 파일 보존 결과를 Unity 실행 성공으로 표현하지 않았습니다.
물리 시뮬레이션이나 시나리오 경로를 새로 시험했다는 뜻도 아닙니다.

## 의도적으로 포함하지 않은 기능

- 동영상 파일 자동 녹화
- 네 카메라의 동시 렌더링 / 분할 화면
- 인식 센서, ROS 이미지 송신
- 벽·책장 투명화
- MuJoCo 장애물을 고려하는 카메라 자동 회피
