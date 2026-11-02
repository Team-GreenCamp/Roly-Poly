# Battle-Royal

Unity 6 (6000.3.x) + Netcode for GameObjects 멀티플레이 파티 게임.

## Unity 에디터 조작

- 에디터 조작(씬·프리팹·에셋 수정, C# 실행, 빌드·테스트)은 **Unity CLI**(`unity`)를 기본으로 사용한다.
  - `unity status`로 Battle-Royal 에디터가 `ready`인지 먼저 확인한다.
  - `unity command`로 노출된 명령을 확인하고, `unity command eval '<C#>'`으로 코드를 실행한다.
  - CLI는 프로젝트의 `com.unity.pipeline` 패키지(에디터 포트 7800)를 통해 에디터에 연결된다.
- `eval` 코드는 메서드 본문으로 감싸져 실행되므로 `using`을 쓸 수 없다. `UnityEngine.Object.FindObjectsByType<...>`처럼 전체 이름을 쓴다.
- 플레이 모드 중에는 `EditorSceneManager.OpenScene`이 실패한다. 플레이 모드를 사용자 대신 끄지 말고, 사용자에게 알린다.
- 에디터가 연결돼 있으면 `.unity`/`.prefab`/`.asset` YAML을 직접 고치지 말고 에디터를 통해 수정한다.
- CLI로 안 되는 경우에만 UnityMCP를 쓴다. UnityMCP에는 다른 프로젝트와 MPPM 클론도 연결돼 있으니, 호출 전에 Battle-Royal 인스턴스를 `set_active_instance`로 지정한다.
- 검증은 컴파일 에러 확인까지만 한다. 플레이테스트는 사용자가 직접 한다(요청할 때만 플레이 모드 검증).

## 게임 모드와 맵

- 경쟁 모드는 `SurvivalGameManager`(SGM, `Assets/Scripts/Gameplay/Survival/`)가 공통 프레임워크(탈락·승자·관전·시상대)이고, 모드 하나 = 씬 하나다.
  - 서바이벌(`Survival Arena`): `timedCoinSurvival` — 사라졌다 재생성되는 발판, 범퍼·폭탄, 2분 후 코인 점수로 순위 결정.
  - 장애물 레이스(`Obstacle Race`): `raceMode` — 제한시간 240초, 체크포인트 리스폰.
  - 떨어지는 바닥(`Falling Floors`): 4층 바닥 붕괴, 폭탄.
- 맵 등록은 세 곳을 함께 맞춘다(chapterID 문자열로 연결).
  1. Lobby Scene › `Chapters Panel`의 `ChapterManager.chapters` (제목·설명·배경 이미지)
  2. Lobby Scene › `Lobby Canvas`의 `LobbyUIController.mapSelections` (chapterId → 씬 이름)
  3. `ProjectSettings/EditorBuildSettings.asset` (씬 경로)
- 맵 선택 화면의 제목·설명은 Heat UI 로컬라이즈(`Assets/External Assets/Heat - Complete Modern UI/Localization/Languages/`의 한국어·영어 파일, `MapSurvival`/`MapSumo`/`MapFalling` 키와 `…Desc` 키)에서 나온다. 설명 칸은 폭 880px이므로 문장 단위로 `\n` 줄바꿈을 넣는다.

## 네트워크 주의점

- 플레이어는 Owner 권한이다. 다른 플레이어에게 주는 효과는 공격자가 감지 → ServerRpc → 피해자 소유자 대상 ClientRpc로 적용한다. 스케일 같은 비주얼 상태는 서버 write `NetworkVariable`로 동기화한다.
- 씬에 `Assets/Scripts/Interactable/`의 NetworkBehaviour를 추가하면 NGO가 NetworkObject를 자동으로 붙인다. 반드시 씬을 저장해야 한다. 저장하지 않으면 `GlobalObjectIdHash` 0 충돌로 클라이언트가 씬을 전환하지 못한다.
