# 최적화와 비용 제어

최적화는 에디터 반복 검사와 런타임 이벤트의 불필요한 작업을 줄이는 관점으로 진행했습니다. 아래는 공개 소스에서 확인할 수 있는 구현이며 FPS나 실행 시간 개선 실험을 뜻하지 않습니다.

| 대상 | 구현 | 의도와 한계 |
|---|---|---|
| 공간 검사 | RoomEditorWindow의 _cachedSpaceCheck와 _nextSpaceCheck | 같은 루트의 검사 결과를 일정 시간 재사용. 강제 검사와 무효화 경로는 별도로 유지 |
| 편집 상태 관찰 | _nextObserve로 0.2초 간격 제한 | 매 프레임 동일 관찰을 수행하는 비용을 제한. 주기 기반 갱신에 따른 지연 가능 |
| 셀 범위 확정 | HashSet 추가·삭제 및 SetEquals 비교 | 중복 좌표 방지와 no-op 조기 반환. HashSet 구성·목록 변환 비용은 남음 |
| 미리보기 | 확정 전 실제 데이터와 Undo 변경 분리 | 드래그 도중 반복적인 데이터 변경을 피하고 확정당 Undo 한 번 적용 |
| 영역 이벤트 | Collider→액터 추적, 액터별 중첩 수 관리 | 다중 Collider가 동일 액터의 이벤트를 중복 실행하는 문제 억제 |
| 머무름 이벤트 | stayInterval 및 lastStay | 실행 빈도 제한과 같은 물리 스텝의 중복 호출 방지. 모든 추적 비용 제거는 아님 |
| 조건부 생성 | initialized 가드와 HashSet 대상 중복 검사 | 방 인스턴스당 한 번 초기화하고 동일 대상 중복 처리 방지 |
| 검사 복잡도 | RoomSpaceChecker의 조각 수 제한과 미확정 결과 | 과도한 계산을 제한하면서 검사하지 못한 대상을 성공으로 처리하지 않음 |

## 코드를 확인할 위치

- [RoomEditorWindow.cs](../Source/Editor/RoomEditorWindow.cs): 검사 캐시, 상태 관찰, CommitCellRange
- [RoomSpaceChecker.cs](../Source/Editor/RoomSpaceChecker.cs): 공간 검사와 복잡도 제한
- [RoomZone.cs](../Source/Runtime/RoomZone.cs): 중복 이벤트와 실행 간격 제어
- [RoomConditionalSpawn.cs](../Source/Runtime/RoomConditionalSpawn.cs): 1회 초기화와 대상 중복 처리

## 기능 안정성과 성능을 구분한 부분

조건부 생성은 별도 System.Random을 사용해 기존 생성기의 난수 상태를 추가 소비하지 않습니다. 이는 재현성과 기존 생성 결과의 안정성을 위한 결정이며, 별도 난수 사용 자체를 속도 개선으로 주장하지 않습니다.

생성 회귀 40/40 PASS는 배치 결과와 상태의 기능 검증입니다. 100,000셀 상한은 입력 제한입니다. 두 수치를 성능 벤치마크로 사용하지 않습니다.

## 후속 측정 계획

완성 씬 성능 검사는 후순위로 두었습니다. 다음에는 동일 시드와 데이터셋에서 방 수·셀 수·Collider 수를 단계적으로 늘려 다음 값을 기록할 계획입니다.

1. 편집 중 공간 검사 시간과 UI 응답 지연
2. 범위 확정의 CPU 시간과 GC 할당
3. 다중 Collider 영역의 이벤트 호출 수와 처리 시간
4. 맵 생성 시간과 메모리 사용량

비교 전후의 환경과 입력을 고정하고, 측정 결과가 확보된 항목만 정량적 최적화 성과로 추가할 예정입니다.
