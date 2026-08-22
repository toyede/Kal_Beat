# Noise Monster Design Guide

## 1. 목적

- 이 문서는 노이즈 몬스터 기획을 차트, 자산과 런타임 구현으로 옮길 때 필요한 정보를 정의한다.
- 새 노이즈 몬스터의 디자인 또는 구현을 시작할 때 먼저 확인한다.
- 필수 정보가 부족하면 이동이나 전투 문장 구조를 임의로 확정하지 않는다.
- 코드의 `Monster` 명칭은 당분간 유지하고 기획상 표현만 `노이즈 몬스터`로 통일한다.

---

## 2. 세계관에서의 역할

노이즈 몬스터는 악의적인 생명체가 아니라 모델링 AI가 은닉층 내부에서 스스로 제어하지 못하는 이상 파형을 전투 형태로 시각화한 존재다.

- 모델링 연산 중 제거하거나 정렬해야 할 노이즈다.
- 음악적 특징은 공격 리듬과 예비 동작으로 나타난다.
- 외형과 움직임은 해당 모델링 데이터에서 발생한 오류의 성질을 보여 준다.
- 유저의 타이밍 입력과 AI의 행동으로 노이즈를 억제하는 과정이 리듬 전투다.
- 곡을 완주하면 인터랙티브 디노이징이 끝나고 노이즈 몬스터는 사라진다.
- 노이즈 제거 후에는 선택한 데이터의 정체가 공개되고 완성 모델이 생성된다.

노이즈 몬스터를 설계할 때는 전투 기능뿐 아니라 다음 연결도 고려한다.

- 어떤 모델링 데이터에서 발생했는가?
- 어떤 음악적 특징을 이상 파형으로 표현하는가?
- 외형, 색, 움직임과 공격이 같은 노이즈 성질을 공유하는가?
- 소멸할 때 모델링 완료가 느껴지는가?

---

## 3. 현재 기술 전제

- 전투는 1대1 프로토타입 기준이다.
- 차트 시간의 정규 단위는 beat다.
- 런타임 동기화 기준은 FMOD timeline이다.
- 방향은 기본적으로 플레이어블 AI 시점 기준이다.
- 위치는 duel-local 좌표와 거리 band를 사용한다.
- 복잡한 위치 계산은 차트 저장 시 미리 시뮬레이션해 `ResolvedTimeline`에 저장한다.
- 플레이 중에는 미리 계산한 결과를 재생하는 방식을 우선한다.
- 성공과 실패는 피드백과 점수에는 영향을 주지만 다음 문장의 논리 상태는 가능하면 동일하게 유지한다.
- 현재는 차트에서 하나의 활성 몬스터 레인만 해석한다.

---

## 4. 용어 정리

| 기획 용어 | 코드 및 자산 용어 | 의미 |
| --- | --- | --- |
| 노이즈 몬스터 | `MonsterDefinition`, `MonsterCombatActor` | 전투 상대이자 이상 파형의 시각화 |
| 전투 문장 | `PatternDefinition` | 하나의 공격·회피·반격 흐름 |
| 문장 step | `PatternStepDefinition` | Ready 또는 Timing 단계 |
| 반응형 문법 | 근거리 계열 `CombatEventKind` | 짧은 신호에 즉시 반응 |
| 패턴형 문법 | 원거리 계열 `CombatEventKind` | 예비박을 기억한 뒤 응답 |
| 플레이어 | 런타임 `PlayerCombatActor` | 프로그램 내부에서 싸우는 모델링 AI |

`PatternDefinition`은 코드 자산 이름이다. 기획 대화에서는 원거리의 패턴형 문법과 혼동하지 않도록 `전투 문장` 또는 `문장 템플릿`이라고 부른다.

---

## 5. 노이즈 몬스터 기획 필수 정보

### 5-1. 정체와 표현

- 표시 이름
- 내부 식별자 또는 `monsterId`
- 연결된 모델링 데이터 또는 곡
- 노이즈가 표현하는 이상 파형의 성질
  - 예: 불규칙한 펄스
  - 예: 과도한 진폭
  - 예: 끊기는 신호
- 음악적 특징
  - 예: 싱코페이션
  - 예: 반복되는 8분음표
  - 예: 긴 쉼 뒤 강한 한 박
- 시각 키워드와 대표 색
- 기본 실루엣과 크기
- 프로토타입 표현 방식 또는 실제 프리팹
- 제거 또는 소멸 연출

### 5-2. 전투 타입

- `Melee`
- `Ranged`
- `Hybrid`

실제 코드에서는 `MonsterArchetype`으로 저장한다. 타입은 사용할 수 있는 문장 템플릿과 기본 거리 감각을 설명하지만 개별 문장의 `CombatEventKind`를 대신하지 않는다.

### 5-3. 시작 상태

- 기본 시작 거리 band
- 허용 가능한 시작 band 목록
- 시작 시 AI와 노이즈 몬스터의 기본 대치 관계
- 시작 방향과 시선
- 첫 문장 전에 필요한 등장 또는 안정화 연출

기본값은 정면 대치지만 모든 문장은 자신의 `EntryBandUnits`를 명시해야 한다.

### 5-4. 지원 전투 문장

문장마다 다음 정보를 제공한다.

- 표시 이름
- 내부 `patternId`
- 목적과 화면에서의 의미
- 반응형 또는 패턴형 문법
- `CombatEventKind`
  - `MeleeThreatCounterable`
  - `MeleeThreatDodgeOnly`
  - `RangedThreatCounterable`
  - `RangedThreatDodgeOnly`
- 진입 band
- Ready 큐 FMOD 이벤트 경로
- 문장 step 목록
- Timing 결과
- 문장 종료 후 상태

`PlayerDashAttack`과 `PlayerRangedAttack`은 플레이어블 AI 공격이므로 노이즈 몬스터의 위협 문장과 구분한다. 다만 노이즈 몬스터 정의가 해당 문장을 지원할 수는 있다.

---

## 6. 문장 step 설계

### 6-1. 현재 `PatternStepDefinition` 필드

- `BeatOffsetFromTiming`
- `Phase`
- `ReadyCueType`
- `DirectionHint`
- `MovementResolveMode`
- `MoveUnits`
- `TargetBandUnits`
- `MovementDirectionHint`
- `PlayerKnockbackUnits`
- `MonsterKnockbackUnits`

### 6-2. Ready step

- `Phase`: `Ready`
- `ReadyCueType`: `Pose` 또는 `Move`
- Timing 기준 몇 beat 전에 실행되는지 명시한다.
- `Pose`는 위치를 바꾸지 않고 공격 의도를 읽히게 한다.
- `Move`는 `MovementResolveMode`, 이동량과 방향을 모두 가져야 한다.
- 현재 이동 해석은 다음 두 방식을 지원한다.
  - `ForwardToBand`: 지정한 목표 band로 접근
  - `SideMoveOnBand`: 현재 band를 유지하며 좌우 이동
- 노이즈 몬스터의 Ready 이동 방향은 문장 step의 `MovementDirectionHint`가 가진다.

### 6-3. Timing step

- `Phase`: `Timing`
- `ReadyCueType`: `None`
- 최종 Timing step의 기준 offset은 `0`이다.
- AI 회피 방향은 차트의 `DirectionHint`가 지정한다.
- 반격형은 플레이어와 노이즈 몬스터의 넉백 거리를 각각 명시한다.
- Timing에서의 접근 목표와 반응 후 위치가 모두 정의되어야 한다.

### 6-4. 예시 문장

- `ready(pose) -> timing`
- `ready(move) -> ready(move) -> timing`
- `ready -> timing` 플레이어 근거리 공격
- 원거리 예비박 여러 개 -> 응답 Timing 여러 개

---

## 7. 이동과 Timing 결과 규칙

### 7-1. 노이즈 몬스터 이동

- 같은 band에서 측면 이동하는가?
- AI 쪽으로 접근하는가?
- 어느 band에서 공격을 시작하는가?
- 공격 시 AI 위치를 잠그는가?
- 반격 또는 문장 종료 후 어느 위치로 회복하는가?
- 이동 단위별 지속 시간을 `MonsterDefinition`에서 제공하는가?

### 7-2. 플레이어블 AI 이동

- 회피 방향과 이동량
- 피격 또는 Miss 연출의 이동량
- 반격 후 넉백량
- 원거리 응답을 위한 자동 band 이동 여부
- 문장 종료 후 다음 문장을 시작할 수 있는 위치

### 7-3. Timing 결과

반드시 다음을 명시한다.

- 공격 시작 순간의 목표 위치
- AI의 성공 이동
- AI의 실패 또는 피격 이동
- 노이즈 몬스터의 반응 이동
- AI와 노이즈 몬스터 각각의 넉백량
- 성공과 실패의 최종 논리 상태가 같은지
- 다음 문장 시작에 필요한 최종 band

연출 차이는 허용하지만 성공과 실패 때문에 차트의 후속 위치가 불확정해지지 않도록 설계한다.

---

## 8. 원거리 문장 추가 정보

원거리 회피형 또는 반격형 문장은 다음 정보를 추가로 제공한다.

- 예비박 cue 수
- 씬 anchor 수
- cue 간격 beat
- 예비박에서 응답까지의 offset beat
- 원거리 회피 이동량
- 응답 시 플레이어와 노이즈 몬스터의 band
- 플레이어를 응답 band로 자동 이동시킬지 여부
- Open 구간 사용 여부와 위치
- 플레이어 원거리 공격을 섞을 cue 위치
- 투사체 프리팹
- 투사체 앞 방향 기준 자식 이름
- 논리 이동 거리와 이동 시간
- 접촉 시각과 접촉 대상 anchor
- 원거리 문장 종료 후 다음 비원거리 문장으로의 회복 위치

현재 `PatternDefinition`은 다음 원거리 필드를 제공한다.

- `RangedCueCount`
- `RangedAnchorCount`
- `RangedCueIntervalBeats`
- `RangedResponseOffsetBeats`
- `RangedDodgeMoveUnits`
- `RangedResponseBandUnits`
- `AutoMovePlayerToResponseBand`
- `ProjectilePrefab`
- `ProjectileFrontTipName`
- `ProjectileTravelUnits`
- `ProjectileTravelDurationSec`

---

## 9. 이벤트 종료 후 상태

문장이 끝난 뒤 다음 문장이 즉시 이어질 수 있어야 한다.

- 최종 노이즈 몬스터 band
- 최종 AI band 또는 상대 위치
- 양쪽의 방향과 시선
- 남아 있는 투사체 또는 빔 처리
- Ready 마크 제거 시점
- 다음 문장의 `EntryBandUnits` 충족 여부
- 원거리 문장 뒤 회복 이동 필요 여부

후속 문장의 시작 band가 다르면 현재 문장의 post-event recovery에 포함할지, 별도의 Ready 이동으로 표현할지 명확히 정한다.

---

## 10. 시청각 설계 원칙

### 10-1. 가독성

- Ready와 Timing이 포즈, 이동, 색 또는 사운드로 구분되어야 한다.
- 회피형과 반격형은 입력 문법이 비슷하더라도 결과 연출로 즉시 구분되어야 한다.
- 공격 방향은 카메라가 회전해도 AI 기준으로 읽을 수 있어야 한다.
- 원거리 cue는 anchor 순서와 리듬 순서가 시각적으로 일치해야 한다.

### 10-2. 노이즈 정체성

- 공격 리듬은 곡의 대표적인 이상 파형을 반영한다.
- 색, 실루엣과 이펙트는 같은 노이즈 성질을 공유한다.
- 피격과 반격은 단순한 생물 피해보다 파형이 깨지거나 정렬되는 느낌을 우선한다.
- 제거 연출은 노이즈가 정돈되고 모델링 결과가 안정화되는 과정으로 표현한다.

---

## 11. 기획 전달 템플릿

```md
노이즈 몬스터 이름:
monsterId:
연결된 모델링 데이터/곡:
노이즈 성질:
음악적 특징:
전투 타입: Melee / Ranged / Hybrid
시각 키워드:
프로토타입 또는 프리팹:

기본 시작 band:
허용 시작 band:
기본 대치 관계:

문장 이름:
patternId:
문법: 반응형 / 패턴형
CombatEventKind:
목적:
EntryBandUnits:
ReadyCueEventPath:

step 0:
- BeatOffsetFromTiming:
- Phase:
- ReadyCueType:
- 의미:
- MovementResolveMode:
- MoveUnits:
- TargetBandUnits:
- MovementDirectionHint:

Timing 결과:
- 공격 목표:
- AI 이동:
- 노이즈 몬스터 이동:
- AI 넉백:
- 노이즈 몬스터 넉백:
- 성공/실패 최종 상태 동일 여부:

원거리 문장일 경우:
- cue/anchor 수:
- cue 간격:
- response offset:
- response band:
- 회피 이동량:
- Open 위치:
- 플레이어 공격 cue:
- 투사체와 이동 시간:

이벤트 종료 후:
- 노이즈 몬스터 band:
- AI band 또는 상대 위치:
- 회복 이동:
- 다음 문장 시작 가능 여부:

시청각 표현:
- Ready 신호:
- Timing 신호:
- 판정별 반응:
- 노이즈 제거 연출:
```

---

## 12. 구현 전 확인 질문

### 필수

- 노이즈 몬스터가 어떤 모델링 데이터와 이상 파형을 표현하는가?
- 시작 band는 몇인가?
- 지원하는 각 문장의 `CombatEventKind`는 무엇인가?
- 모든 Ready step이 Pose인지 Move인지 정해졌는가?
- Move의 방식, 이동량, 목표 band와 방향은 무엇인가?
- Timing에서 AI와 노이즈 몬스터가 어디로 이동하는가?
- 반격형이면 양쪽 넉백량은 얼마인가?
- 원거리형이면 cue, anchor, 응답, Open과 투사체 정보가 있는가?
- 이벤트 종료 후 다음 문장을 시작할 수 있는 최종 상태는 무엇인가?

### 있으면 좋은 정보

- 프로토타입 색과 임시 연출
- 판정별 애니메이션과 이펙트 차이
- FMOD Ready cue 경로
- 카메라 강조가 필요한 순간
- 제거 연출과 완성 모델 생성의 연결 방식

---

## 13. 현재 자산 역할

### `MonsterDefinition`

- 식별자와 표시 이름
- 전투 타입
- 프리팹
- Ready 이동 지원 여부
- 이동 단위별 Ready 및 Timing 지속 시간
- 지원 문장 템플릿 목록
- 런타임 signature

### `PatternDefinition`

- 문장 식별자와 표시 이름
- `CombatEventKind`
- 진입 band
- Ready cue
- 원거리 문장과 투사체 설정
- step 목록
- 차트 동기화 signature

### `PatternStepDefinition`

- Timing 기준 beat offset
- Ready/Timing phase
- 준비 신호
- 플레이어 방향 힌트
- Ready 이동 방식과 목표
- 양쪽 넉백 값

현재 이 데이터만으로 처리하기 어려운 서사적 정체성, 노이즈 성질과 시청각 규칙은 기획 문서에서 별도로 유지한다.

---

## 14. 핵심 체크리스트

노이즈 몬스터 하나를 구현하기 전에 최소한 다음 여섯 가지가 확정되어야 한다.

1. 모델링 데이터와 노이즈 정체성
2. 시작 band
3. 문장별 step과 이동 규칙
4. Timing 판정 결과
5. 원거리 문장의 cue·응답·투사체 규칙
6. 이벤트 종료 후 다음 문장으로 이어지는 최종 상태
