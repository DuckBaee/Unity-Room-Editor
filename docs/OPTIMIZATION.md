# 최적화와 비용 제어

## 생성기: AABB Broad Phase

예약 박스의 쌍별 정밀 검사가 후보 재시도마다 반복됐습니다. 후보 월드 박스와 전체 AABB를 한 번 계산하고, 확정된 방의 worldBounds와 교차할 때만 AnyOverlap을 수행합니다. 통과한 후보는 이미 만든 박스와 경계를 PlacedBlock에 넘겨 중복 계산을 피합니다.

AABB는 빈 공간까지 포함하는 보수적인 필터입니다. ㄱ자 방의 빈 부분은 후속 정밀 검사로 판정합니다. BlockData.localBounds를 제작 시 캐싱하는 방식으로 설명하지 않습니다.

### 2026-10-21 당시 사용자 측정 집계

OFF/ON 각각 20개 Seed × 2회, 총 80회 보고입니다.

| 지표 | OFF | ON | 비교 |
|---|---:|---:|---:|
| 평균 Process 시간 | 약 6.595초 | 약 0.601초 | 약 90.9% 감소, 약 11배 |
| 평균 Overlaps 진입 | 약 44,425,694회 | 약 3,386,981회 | 약 92.4% 감소 |

출처는 원본 `PortfolioDocs/2026-10-21_AABB_REVIEW.md`에 보존된 사용자 측정 보고입니다. 개별 80회 로그·Seed 목록·장비·Editor/Player·로그 설정은 재확보하지 못했습니다. 이번 공개 작업에서 재측정하지 않았으며 독립 재현된 벤치마크로 제시하지 않습니다. Overlaps 진입은 모든 SAT 축 검사 횟수와 다릅니다. Process에는 동기 생성과 로그 등이 포함되며 프레임 렌더 비용 전체가 아닙니다.

동일 Seed의 최종 배치 동등성과 AABB를 끈 정밀 검사는 별도 확인 과제입니다. 단위·구간이 불명확한 초기 GC 수치는 성과에서 제외했습니다.

코드: [TemplateBaseBuilder](../Source/Runtime/TemplateBaseBuilder.cs), [BlockReservationBuilder](../Source/Runtime/BlockReservationBuilder.cs), [PlacedBlock](../Source/Runtime/PlacedBlock.cs), [ReservationOverlap](../Source/Runtime/ReservationOverlap.cs).

## 후속 편집기·런타임 비용 제어

아래는 공개 소스에서 확인 가능한 구현이며 각 항목의 실행 시간 개선 실험을 뜻하지 않습니다.

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
