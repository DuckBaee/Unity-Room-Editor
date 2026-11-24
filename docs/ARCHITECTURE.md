# 구조와 코드 지도

```text
Room Editor (UI Toolkit + SceneView)
├ 예약 셀
└ RoomRoot
   ├ 아트
   ├ 포털
   └ 규칙 컴포넌트
        ↓ 검사·저장
BlockData + 방 아트 프리팹
        ↓ 기존 생성기
LevelRuntime → RoomRuntime → BlockInstance
                             └ 외형·규칙 초기화
```

| 구성 | 코드 | 책임 |
|---|---|---|
| 편집기 | Source/Editor/RoomEditorWindow.cs, .uxml, .uss | 패널, SceneView 입력, 편집 세션, 셀과 아트 저장 |
| 공간 검사 | Source/Editor/RoomSpaceChecker.cs | Collider와 예약 영역 검사 |
| 저장 | Source/Editor/RoomAssetSaveTransaction.cs | 데이터·아트 저장 시 기존 바이트 보존과 실패 처리 |
| 포털 | Source/Runtime/RoomPortalAuthoring.cs, PortalData.cs | 편집 설정과 저장 정보 |
| 규칙 UI | Source/Editor/RoomInteractionPanel.cs, RoomZonePanel.cs, RoomConditionalSpawnPanel.cs | 컴포넌트 발견과 필드 편집 |
| 규칙 실행 | Source/Runtime/RoomInteraction.cs, RoomZone.cs, RoomConditionalSpawn.cs | 상호작용, 영역 이벤트, 확률 활성화 |
| 실행 상태 | Source/Runtime/LevelRuntime.cs, BlockInstance.cs | 방별 상태 분리와 초기화 |

BlockData는 공간·포털·아트 참조를 저장합니다. 프리팹은 모델과 규칙 설정을 저장하며 방 차단 상태는 RoomRuntime, 실행 여부와 접촉 목록은 생성된 컴포넌트 인스턴스가 관리합니다. 동일 프리팹을 여러 번 사용해도 상태가 섞이지 않도록 구분했습니다.

상호작용 입력과 전투 완료 판정은 게임 코드가 연결합니다. 방 차단은 bool이며 여러 독립 목표의 잠금 토큰을 관리하는 시스템은 아닙니다.

Source는 원본 핵심 파일의 검토용 복사본입니다. 원본 개발 이력, Unity .meta와 씬, 외부 패키지 및 모든 보조 파일은 포함하지 않아 독립 빌드가 보장되지 않습니다.
