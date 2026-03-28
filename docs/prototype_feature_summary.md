# Kal_Beat 프로토타입 기능 정리

## 1. 작업 목표 요약
이번 작업의 목표는 리듬 전투 프로토타입을 실제로 굴릴 수 있는 최소 구조를 만드는 것이었다.  
특히 아래 세 가지를 중심으로 진행했다.

- 차트가 전투 의미를 직접 표현하도록 데이터 구조를 정리
- 몬스터/문장 템플릿 기반으로 채보 에디터에서 패턴을 선택할 수 있게 구성
- FMOD 시간을 기준으로 런타임 전투가 실제로 재생되도록 연결

---

## 2. 차트 데이터 구조 개편

### 2-1. 전투 의미 중심 이벤트 구조
기존 이벤트 구조를 전투 문법 중심으로 정리했다.  
이제 차트 이벤트는 단순 타입 구분이 아니라, 실제 전투에서 어떤 의미를 가지는지를 직접 담는다.

- `CombatEventKind`
  - `MeleeThreatCounterable`
  - `MeleeThreatDodgeOnly`
  - `PlayerDashAttack`
  - `PlayerRangedAttack`
  - `RangedThreatCounterable`
  - `RangedThreatDodgeOnly`
- `CombatEventPhase`
  - `Ready`
  - `Timing`
- `ReadyCueType`
  - `Pose`
  - `Move`
- `PlayerRelativeDirection`
  - 플레이어 기준 `Left / Right`

이 구조 덕분에 차트가 단순 노트 배치가 아니라,  
“이 박은 근거리 회피형 준비 동작인가”, “이 박은 실제 판정이 열리는 타이밍인가”를 직접 표현할 수 있게 되었다.

### 2-2. 라인 바인딩 구조
각 lane은 곡 전체에서 의미가 고정되는 구조로 두었다.  
현재 lane binding은 `ChartMeta` 안에 들어가며, 라인마다 어떤 몬스터/주체가 이 lane을 해석하는지 결정한다.

현재 사용 흐름:
- `None`
- `Monster`

이 구조를 통해 lane마다 “이 라인은 Boar와 관련된 전투 문장을 담는 라인” 같은 식의 해석이 가능해졌다.

---

## 3. 몬스터 정의 / 문장 템플릿 자산 구조

### 3-1. MonsterDefinition
`MonsterDefinition`은 몬스터의 코드 실행체 자체가 아니라,  
에디터와 런타임이 함께 참조하는 설명 자산으로 사용한다.

현재 포함 내용:
- `MonsterId`
- `DisplayName`
- `MonsterArchetype`
- `Prefab`
- `SupportsReadyMove`
- `ReadyMoveDurationByUnits`
- `TimingApproachDurationByUnits`
- `SupportedPatterns`

즉 이 자산은
- 어떤 몬스터인지
- 어떤 문장 템플릿을 지원하는지
- 몇 칸 이동에 몇 초를 쓸지
를 묶어서 들고 있다.

### 3-2. PatternDefinition
`PatternDefinition`은 문장 템플릿 하나를 의미한다.  
하나의 asset이 하나의 전투 문장이다.

예:
- `Pose -> Timing`
- `Move -> Move -> Timing`

현재 포함 내용:
- `PatternId`
- `DisplayName`
- `CombatKind`
- `EntryBandUnits`
- `Steps`

### 3-3. PatternStepDefinition
각 step은 beat 기준으로 어떤 준비/타이밍 동작인지 표현한다.

현재 포함 내용:
- `BeatOffsetFromTiming`
- `Phase`
- `ReadyCueType`
- `DirectionHint`
- `MovementResolveMode`
- `MoveUnits`
- `TargetBandUnits`
- `MovementDirectionHint`

여기서 중요한 원칙은:
- 플레이어 회피 방향은 차트/타이밍 이벤트가 결정
- 몬스터 준비 이동 방향은 step 데이터가 결정

즉 “무엇을 할지”는 문장 템플릿이 알고 있고,  
“얼마나 빠르게 보일지”는 몬스터 정의가 알고 있는 구조다.

---

## 4. 채보 에디터 개선

### 4-1. 라인 바인딩과 몬스터 선택
차트 에디터는 프로젝트 안의 `MonsterDefinition` 자산을 읽어서,  
각 lane에 어떤 몬스터를 배정할지 선택할 수 있게 만들었다.

이를 통해:
- `Boar` lane 지정
- 해당 lane에서 `Boar`가 지원하는 문장 템플릿만 표시
가 가능해졌다.

### 4-2. 패턴 적용 기능
선택한 `Timing` 이벤트를 기준점으로 하여,  
해당 몬스터가 지원하는 `PatternDefinition`을 적용하면 `Ready` step들이 자동 생성되도록 했다.

규칙:
- 선택한 `Timing` 이벤트는 anchor
- 앞의 `Ready` step은 자동 생성
- `None` marker와 겹치면 대체
- 실제 이벤트와 겹치면 생성 차단

### 4-3. Kind/Direction 필터링
에디터는 monster definition과 pattern definition을 기준으로  
지원하지 않는 `Kind`, `ReadyCueType`, 패턴을 숨기거나 제한하도록 정리했다.

현재 상태:
- `Boar`가 `Ready Move`를 지원하지 않으면 관련 패턴 숨김
- `Boar` lane에서는 `Boar`가 지원하는 `CombatKind`만 표시
- `Direction`은 현재 합의된 규칙에 따라 필요한 경우만 노출

---

## 5. 1대1 전투 런타임 프로토타입

### 5-1. CombatDuelRoot
`CombatDuelRoot`는 1대1 전투의 기준점 역할을 한다.

역할:
- 플레이어와 몬스터 기본 배치
- duel-local 좌표계 제공
- 현재 전투 축 계산
- duel-local <-> world 변환
- band 기반 목표 위치 계산 보조

이 구조 덕분에 월드 좌표에 직접 묶이지 않고,  
“플레이어와 몬스터 사이의 전투 관계”를 중심으로 로직을 짤 수 있게 되었다.

### 5-2. PlayerCombatActor / MonsterCombatActor / BoarMonsterActor
런타임에서는 최소한의 액터 뼈대를 만들었다.

- `PlayerCombatActor`
  - 피격/회피 이동
  - 색 변화
- `MonsterCombatActor`
  - 공통 이동/리셋/색 변화
  - 이벤트 진입점
- `BoarMonsterActor`
  - `Pose`
  - `Timing` 돌진
  - 이후 `Ready Move` 재생까지 확장

처음에는 `Pose -> Timing`만 처리했지만,  
이후 `Move -> Move -> Timing`도 재생할 수 있도록 구조를 넓혔다.

### 5-3. FMOD 시간 기준 재생
런타임 시간 기준은 `Time.time`이 아니라 FMOD timeline이다.  
이제 전투 재생은 FMOD 시간이 기준이고, 차트의 beat는 그 timeline에 맞춰 해석된다.

---

## 6. 밴드 방식 Move 설계와 해석

### 6-1. 밴드 방식의 의미
Move는 단순히 “왼쪽으로 몇 칸”이 아니라,  
현재 플레이어-몬스터 관계를 band 기준으로 해석하는 구조로 설계했다.

현재 1차 구현 범위:
- `ForwardToBand`
- `SideMoveOnBand`

이 방식의 장점:
- 준비 이동과 타이밍 공격을 같은 전투 축 안에서 해석 가능
- 이후 반격형, 후퇴형, 넉백형으로 자연스럽게 확장 가능

### 6-2. 몬스터별 duration 테이블
이동 시간은 step에 직접 적지 않고, monster definition이 가진다.

예:
- `Ready Move 2칸 = 0.09초`
- `Ready Move 4칸 = 0.18초`
- `Timing 4칸 접근 = 0.18초`

이렇게 두면 같은 문장이라도 몬스터별로 감각을 다르게 가져갈 수 있다.

---

## 7. ResolvedTimeline 생성 파이프라인

### 7-1. 왜 필요한가
`Ready Move`와 `Timing`은 모두 “그 beat에 도착 완료”되어야 한다.  
즉 런타임은 단순히 beat가 되었을 때 반응하는 게 아니라,

- 언제 출발해야 하는지
- 목표 위치가 어디인지
- 시작/종료 시각이 언제인지

를 미리 알아야 한다.

이를 위해 `ChartData` 안에 숨겨진 generated 데이터인 `ResolvedTimeline`을 넣었다.

### 7-2. 구조
`ChartData`
- `Meta`
- `Events`
- `ResolvedTimeline`

`ResolvedTimeline`
- `SourceSignature`
- `IsValid`
- `InvalidReason`
- `Steps`

`ResolvedCombatStep`
- `MonsterStartSec`
- `HitSec`
- `MonsterDurationSec`
- `PlayerStartSec`
- `PlayerDurationSec`
- `StartPlayerLocalPos`
- `EndPlayerLocalPos`
- `StartMonsterLocalPos`
- `EndMonsterLocalPos`
- `LockedPlayerLocalPos`
- `EntryBandUnits`
- `ExitBandUnits`
- 그 외 실행에 필요한 이벤트 정보

즉 원본 차트는 편집용으로 두고,  
런타임은 미리 계산된 재생용 step만 읽게 만들었다.

### 7-3. 저장 시 자동 생성
`Chart Editor`에서 저장할 때:
1. validation 통과 확인
2. `ChartResolvedTimelineBuilder`가 source chart를 읽음
3. monster definition / pattern step / band resolver를 적용
4. `ResolvedTimeline` 생성
5. `ChartData` 안에 저장

이렇게 하도록 연결했다.

### 7-4. stale / invalid 차단
`ResolvedTimeline`은 원본 차트와 monster definition이 바뀌면 낡을 수 있다.  
이를 막기 위해:

- source signature 비교
- monster definition signature 비교
- invalid reason 저장
- stale/invalid면 재생 차단

구조를 넣었다.

즉 에디터든 플레이든, 잘못된 generated timeline으로 어설프게 재생하지 않고  
먼저 다시 저장해서 timeline을 재생성하도록 강제한다.

---

## 8. 현재 재생 경로
지금 런타임은 더 이상 raw `Events`를 직접 해석하지 않고,  
`ResolvedTimeline`을 읽어서 재생하도록 바뀌었다.

재생 흐름:
1. FMOD timeline time 읽기
2. `ResolvedCombatStep.MonsterStartSec` / `PlayerStartSec` 비교
3. 시각이 도달한 step 실행
4. monster/player를 duel-local 목표 위치로 이동

즉 플레이 중에는 band 계산을 반복하지 않고,  
이미 계산된 결과를 재생하는 구조다.

---

## 9. 추가된 테스트 자산
이번 작업 과정에서 아래 테스트 자산도 함께 정리되었다.

- `Assets/Data/Monsters/Boar_Def.asset`
- `Assets/Data/Monsters/Boar_Pattern.asset`
- `Assets/Data/Monsters/Boar_Pattern2.asset`
- `Assets/Data/Charts/Test_Song.asset`
- `Assets/Scenes/PrototypeCombat.unity`

이 자산들은 현재 구조를 실제로 검증하기 위한 프로토타입 테스트용이다.

---

## 10. 아직 남은 과제
현재 구조는 프로토타입으로는 충분히 강해졌지만, 아직 남은 부분도 있다.

### 미구현 / 다음 단계
- 판정 시스템 (`Perfect / Good / Miss`)
- 반격형 근거리 문장
- 원거리 문장
- 다중 몬스터 lane
- editor UX polish
  - field 조건부 노출
  - 더 명확한 오류 표시
- generated timeline 디버그 시각화
- 런타임 연출 강화
  - 회피/피격 차이
  - 넉백
  - 카메라 / 이펙트 / 키음

---

## 11. 현재 상태 한 줄 요약
현재 Kal_Beat 프로토타입은  
**몬스터/문장 템플릿 기반 차트 편집 -> 저장 시 generated timeline 자동 생성 -> FMOD 시간을 기준으로 duel-local step을 재생하는 1대1 리듬 전투 구조**  
까지 연결된 상태다.
