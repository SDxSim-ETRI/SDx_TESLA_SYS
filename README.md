<div style="text-align: left;">

# SDx_TESLA_SYS

<img src="https://github.com/user-attachments/assets/c6bb8179-9350-4409-8569-ccb8081d3bad" width="70%" height="70%"/>

- Unity3D와 MuJoCo Unity Plugin의 연동 프로젝트
- MuJoCo Unity Plugin은 MuJoCo 엔진에서 처리한 물리 계산 데이터를 Unity로 전달하여 실시간으로 물체의 동작을 시각화하고 사용자와 상호작용할 수 있는 환경을 제공

---

# 프로젝트 실행 환경

- Ubuntu 20.04
- Unity Editor 2022.3.64f1
- Git
- Git LFS (Large File Storage)

> 본 프로젝트는 Unity Editor **2022.3.64f1** 기준으로 작성되었습니다.  
> 해당 버전이 설치되어 있지 않은 경우 Unity Hub에서 프로젝트를 열 때 필요한 Editor 버전을 확인하고 설치할 수 있습니다.

---

# 프로젝트 다운로드 및 실행

본 프로젝트는 Unity3D의 모델, 텍스처, EXR, FBX, STL, OBJ 등의 대용량 파일을 포함하고 있으므로  
**Git LFS (Large File Storage)** 를 사용합니다.

GitHub에서 프로젝트를 Clone한 후 Unity3D에서 실행하기 전에 반드시 Git LFS 파일을 다운로드해야 합니다.

## 1. Git LFS 설치

Ubuntu에서 다음 명령어를 실행합니다.

```bash
sudo apt update
sudo apt install git-lfs
```

Git LFS를 초기화합니다.

```bash
git lfs install
```

> `git lfs install`은 PC별로 최초 한 번만 실행하면 됩니다.

---

## 2. 프로젝트 Clone

다음 명령어로 GitHub Repository를 Clone합니다.

```bash
git clone https://github.com/SDxSim-ETRI/SDx_TESLA_SYS.git
```

Clone한 프로젝트 디렉터리로 이동합니다.

```bash
cd SDx_TESLA_SYS
```

---

## 3. Git LFS 파일 다운로드

다음 명령어를 실행하여 Git LFS로 관리되는 대용량 파일을 다운로드합니다.

```bash
git lfs pull
```

정상적으로 다운로드되었는지 확인하려면 다음 명령어를 사용할 수 있습니다.

```bash
git lfs ls-files
```

> **주의**
>
> `git lfs pull`이 정상적으로 완료되기 전에 Unity 프로젝트를 열지 않는 것을 권장합니다.
>
> Git LFS 파일이 다운로드되지 않은 경우 FBX, EXR, STL, OBJ, PNG 등의 파일 대신  
> Git LFS Pointer 파일만 존재할 수 있으며, 이 경우 Unity에서 모델 또는 텍스처가 정상적으로 Import되지 않을 수 있습니다.

---

# Unity3D 프로젝트 실행

Git LFS 파일 다운로드가 완료되면 Unity Hub를 실행합니다.

Unity Hub에서 **Open / Add project from disk**를 선택한 후 다음 디렉터리를 Unity 프로젝트로 등록합니다.

```text
SDx_TESLA_SYS/SDx_TESLA_SYS_ROS2
```

본 프로젝트는 다음 Unity Editor 버전을 사용합니다.

```text
2022.3.64f1
```

해당 Unity Editor 버전이 PC에 설치되어 있지 않은 경우 Unity Hub에서 프로젝트에 필요한 버전을 확인한 후 설치할 수 있습니다.

프로젝트 구조는 다음과 같습니다.

```text
SDx_TESLA_SYS
├── ROS
├── SDx_TESLA_SYS_ROS2
│   ├── Assets
│   ├── Packages
│   └── ProjectSettings
├── LICENSE
└── README.md
```

Unity 프로젝트를 처음 실행하면 Unity가 다음과 같은 로컬 데이터 폴더를 자동으로 생성합니다.

```text
Library
Logs
Temp
Obj
UserSettings
```

위 폴더들은 Git Repository에 포함되지 않으며 Unity에서 프로젝트를 Import할 때 자동으로 생성됩니다.

첫 실행 시 Assets 및 Packages Import로 인해 시간이 소요될 수 있습니다.

Unity의 Import 작업이 모두 완료된 후 프로젝트를 Build 또는 실행합니다.

---

# Git LFS 사용 시 주의사항

본 Repository의 일부 Unity Asset은 일반 Git이 아닌 Git LFS로 관리됩니다.

새로운 PC에서 Repository를 Clone할 경우 다음 과정이 필요합니다.

```bash
git lfs install
git clone https://github.com/SDxSim-ETRI/SDx_TESLA_SYS.git
cd SDx_TESLA_SYS
git lfs pull
```

이미 Repository를 Clone한 상태라면 다음 명령어만 실행하면 됩니다.

```bash
git lfs install
git lfs pull
```

그 후 Unity Hub에서 다음 프로젝트를 열어 사용합니다.

```text
SDx_TESLA_SYS_ROS2
```

---

# Repository 관리

Unity에서 자동으로 생성되는 다음 파일 및 디렉터리는 Git에 Commit하지 않습니다.

```text
Library/
Temp/
Obj/
Logs/
UserSettings/
Build/
Builds/

*.csproj
*.sln

.vs/
.vscode/
```

Unity 프로젝트 동작에 필요한 주요 데이터는 다음 디렉터리에서 관리합니다.

```text
SDx_TESLA_SYS_ROS2/Assets/
SDx_TESLA_SYS_ROS2/Packages/
SDx_TESLA_SYS_ROS2/ProjectSettings/
```

Unity Asset의 `.meta` 파일 역시 Unity의 GUID 및 Asset 참조 정보를 포함하고 있으므로 반드시 함께 Commit해야 합니다.

---

</div>
