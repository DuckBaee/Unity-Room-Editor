# 검증 결과와 남은 범위

2026년 11월 16일의 셀 조작 및 규칙 재검증 기록을 요약했습니다. 공개 저장소 생성 과정에서 전체 검사를 다시 실행한 것은 아닙니다.

| 범위 | 기록된 결과 | 근거 파일 |
|---|---|---|
| 셀 범위·Undo·Redo·no-op·취소 | 8 PASS | evidence/cell-api-results.txt |
| Unity GUI 이벤트 범위 흐름 | 14 PASS | evidence/gui-event-results.txt |
| 영역 이벤트 런타임 | 16 PASS | evidence/zone-regression.txt |
| 조건부 생성 런타임 | 10 PASS | evidence/spawn-regression.txt |
| 확률·시드·비활성 초기화·대상 정리 | 10 PASS | evidence/extended-runtime.txt |
| Physics.Simulate Trigger | 2 PASS | evidence/physics-results.txt |
| 30방 데이터 및 10시드 생성 | 40 PASS | evidence/generation-results.txt |
| 편집 높이 제거 후 면 판정 등 | 12 PASS | evidence/face-height-results.txt |

## 실패를 보존하고 재검증한 사례

생성 검사는 처음에 13 PASS / 27 FAIL이었습니다. Play 중 편집 자동 편입이 런타임 생성에 간섭한 것이 원인이었습니다. Play 격리 후 40 PASS / 0 FAIL을 확인했습니다. 이전 결과는 evidence/generation-before-play-guard.txt에 보존했습니다.

바깥 MouseUp이 전달되지 않는 경우도 검사 중 발견하여 MouseLeaveWindow 취소 처리를 추가했습니다. 내부 GUI 이벤트 검사와 실제 물리 입력 검사를 혼동하지 않도록 기록을 구분했습니다.

## 해석 범위

- 위 결과는 정해진 테스트 입력에 대한 기존 실행 기록이며 모든 환경에서의 정상 동작 보장이 아닙니다.
- 내부 API·GUI 이벤트 검사가 모든 OS 입력 조합을 검증했다는 의미는 아닙니다.
- 캐릭터 통행, 완성 씬 성능, 신규 규칙 조합의 독립 Player 전체 검증은 후속 범위입니다.
- 매뉴얼 기반 사용성 점검은 이번 주 계획이며 아직 결과가 없습니다.

증거 파일의 로컬 절대 경로는 공개본에서 <PROJECT>로 대체할 수 있습니다. 판정 결과는 변경하지 않습니다. 외부 에셋과 전체 테스트 실행 환경을 포함하지 않아 이 저장소만으로 재실행할 수는 없습니다.
