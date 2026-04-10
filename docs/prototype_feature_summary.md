# Kal_Beat 현재 프로토타입 기능 정리

## 1. 문서 목적
이 문서는 현재 저장소 기준으로 실제 연결되어 있는 프로토타입 기능을 정리한 문서이다.  
초기 구조 설계 시점을 설명하는 문서가 아니라, 지금 당장 에디터와 런타임에서 확인 가능한 기능, 현재 한계, 다음 확장 지점을 빠르게 파악하기 위한 요약 문서로 사용한다.

---

## 2. 현재 프로토타입의 핵심 흐름
현재 Kal_Beat 프로토타입은 아래 흐름까지 실제로 연결되어 있다.

1. ChartData에 곡 메타와 전투 이벤트를 저장한다.
2. 채보 에디터에서 이벤트를 배치하고 검증한 뒤 저장한다.
3. 저장 시 ResolvedTimeline을 자동 생성한다.
4. 런타임은 raw 이벤트를 직접 해석하지 않고 ResolvedTimeline을 읽는다.
5. FMOD timeline 시간을 기준으로 준비 동작, 타이밍 동작, 플레이어 이동, 판정 입력을 재생한다.
6. PrototypeCombat 씬에서 1대1 전투 프로토타입을 확인한다.

즉 현재 구조는  
차트 편집 -> 저장 시 런타임용 타임라인 생성 -> FMOD 기준 전투 재생  
까지 이어지는 상태이다.

---

## 3. 차트 데이터와 에디터

### 3-1. 차트 데이터 구조
차트 이벤트는 단순 노트가 아니라 전투 의미를 직접 담는 구조로 정리되어 있다.

- CombatEventKind
  - MeleeThreatDodgeOnly
  - MeleeThreatCounterable
  - PlayerDashAttack
  - PlayerRangedAttack
  - RangedThreatCounterable
  - RangedThreatDodgeOnly
- CombatEventPhase
  - Ready
  - Timing
- ReadyCueType
  - Pose
  - Move
- PlayerRelativeDirection
  - Left / Right

현재 ResolvedTimeline 빌더 기준으로 실제 런타임 재생이 연결된 전투 종류는 아래 세 가지이다.

- MeleeThreatDodgeOnly
- MeleeThreatCounterable
- PlayerDashAttack

### 3-2. 라인 바인딩 구조
라인은 곡 전체에서 의미가 유지되는 구조로 사용한다.  
현재는 ChartMeta 안에서 lane binding을 관리하며, 각 라인에 어떤 몬스터가 배치되는지 지정할 수 있다.

현재 프로토타입 기준으로는 한 라인에 한 몬스터 의미를 부여하고, 해당 라인에서는 그 몬스터가 지원하는 패턴만 편집하는 흐름을 사용한다.

### 3-3. 채보 에디터 기능
현재 채보 에디터에서 사용할 수 있는 핵심 기능은 아래와 같다.

- ChartData 선택 및 생성
- BPM, offset, song length, grid, lane 수, FMOD 이벤트 경로 편집
- 그리드 표시, 스냅 On/Off, 줌 인/아웃, 타임라인 이동
- 이벤트 추가, 삭제, 드래그 이동
- 선택한 Timing 이벤트 기준 Ready step 자동 생성
- 몬스터별 지원 패턴만 노출
- 저장 전 validation 수행
- FMOD 기반 프리뷰 재생
- 메트로놈 및 이벤트 효과음 재생
- 외부 JSON 차트 결과 불러오기

즉 현재 채보 에디터는 단순한 노트 배치 도구가 아니라, 전투 문장 기반 차트를 만들고 FMOD 기준으로 확인하는 편집 도구까지 연결된 상태이다.

---

## 4. 전투 자산 구조

### 4-1. MonsterDefinition
MonsterDefinition은 몬스터 실행 코드가 아니라, 에디터와 런타임이 함께 참조하는 설명 자산이다.

현재 포함 정보:

- MonsterId
- DisplayName
- MonsterArchetype
- Prefab
- SupportsReadyMove
- ReadyMoveDurationByUnits
- TimingApproachDurationByUnits
- SupportedPatterns

이 자산은 몬스터가 어떤 문장 템플릿을 지원하는지와, 이동 단위별 시간 감각을 함께 정의한다.

### 4-2. PatternDefinition / PatternStepDefinition
PatternDefinition은 전투 문장 템플릿 하나를 의미한다.  
PatternStepDefinition은 그 문장의 각 step을 beat 기준으로 설명한다.

현재 구조는 아래 역할 분리를 가진다.

- 문장 템플릿은 무엇을 할지 정의한다.
- 몬스터 정의는 얼마나 빠르게 보일지 정의한다.
- 차트는 어느 타이밍에 어떤 문장을 배치할지 결정한다.

---

## 5. 런타임 전투 프로토타입

### 5-1. 1대1 duel-local 구조
CombatDuelRoot를 기준으로 플레이어와 몬스터의 관계를 world 좌표가 아니라 duel-local 좌표계에서 해석한다.

현재 이 구조가 담당하는 역할은 아래와 같다.

- 플레이어와 몬스터 기본 배치
- duel-local <-> world 변환
- 전투 축 계산
- band 기반 목표 위치 계산 보조

이 구조 덕분에 런타임 로직은 맵 좌표 전체보다 플레이어와 몬스터의 전투 관계에 집중할 수 있다.

### 5-2. 밴드 기반 이동 해석
현재 이동은 단순한 좌우 칸 개념이 아니라 band 기준으로 해석한다.

현재 1차 구현 범위:

- ForwardToBand
- SideMoveOnBand

이 방식은 준비 이동과 타이밍 공격, 회피 후 위치, 반격 후 위치 계산을 같은 기준 안에서 다룰 수 있게 해준다.

### 5-3. ResolvedTimeline 기반 재생
현재 런타임은 raw 이벤트를 직접 해석하지 않는다.  
차트 저장 시 생성한 ResolvedTimeline을 읽고, 각 step의 시작 시각과 목표 위치를 기준으로 재생한다.

현재 ResolvedCombatStep에는 아래 정보가 포함된다.

- MonsterStartSec
- HitSec
- MonsterDurationSec
- PlayerStartSec
- PlayerDurationSec
- StartPlayerLocalPos / EndPlayerLocalPos
- StartMonsterLocalPos / EndMonsterLocalPos
- PlayerApproachLocalPos
- MonsterApproachLocalPos
- LockedPlayerLocalPos
- EntryBandUnits / ExitBandUnits
- PlayerKnockbackUnits / MonsterKnockbackUnits

또한 source signature와 monster definition signature를 비교하여, 차트나 몬스터 정의가 바뀌면 stale timeline 재생을 막는다.

### 5-4. FMOD 기준 전투 재생
전투 재생 기준 시간은 Time.time이 아니라 FMOD timeline이다.  
CombatTimelineController가 FMOD timeline 위치를 읽고, 그 시간에 맞춰 몬스터 이동, 플레이어 이동, 준비 큐, 판정 입력을 처리한다.

---

## 6. 현재 구현된 전투 동작

### 6-1. 근거리 회피형
현재 프로토타입에서는 근거리 회피형 전투가 재생 가능하다.

- Ready 단계에서 Pose 또는 Move를 재생
- Timing 단계에서 몬스터가 돌진
- 플레이어는 ResolvedTimeline에 계산된 목표 위치로 이동

### 6-2. 근거리 반격형
근거리 반격형도 현재 데이터와 런타임 구조에 연결되어 있다.

- counterable melee kind를 차트에 배치 가능
- timing 접근 위치와 이후 반응 위치를 ResolvedTimeline에서 계산
- 판정 입력 후 플레이어와 몬스터 반응 훅을 호출

현재는 입력 타이밍에 따른 판정과 반응 호출이 연결되어 있으며, 판정 등급마다 이동 결과나 상태 결과를 별도로 분기하지 않고 동일한 반응 흐름 안에서 처리한다.

### 6-3. 플레이어 근거리 대쉬 어택
PlayerDashAttack도 현재 지원 범위에 포함된다.

- 차트에서 PlayerDashAttack 이벤트를 배치 가능
- ResolvedTimeline에서 플레이어 접근 위치와 후속 반응 위치 계산
- 런타임에서 플레이어가 timing 전에 접근 이동 시작
- 몬스터 측에는 준비 마크 표시 가능

---

## 7. 판정, 카메라, 시각 피드백

### 7-1. 판정 시스템
현재 CombatJudgementSystem이 아래 판정을 지원한다.

- Perfect
- Good
- Miss
- Ignore

현재 동작 범위:

- Timing step만 판정 대상으로 수집
- 현재 FMOD 시간 기준으로 가장 가까운 target 판정
- 입력 없을 경우 auto miss 처리
- 판정 결과 이벤트 발행

즉 판정 시스템은 이미 들어가 있으며, 현재는 입력 타이밍을 판정하고 그 결과를 동일한 전투 반응 흐름에 연결하는 구조로 사용하고 있다.

### 7-2. 카메라
QuarterViewFollowCamera를 통해 전투 장면을 따라가는 쿼터뷰 카메라가 구현되어 있다.

현재 포함 기능:

- 타겟 추적
- 이동 중 시야 유지
- 이동 종료 후 지연 회전
- 부드러운 position / rotation 보간

### 7-3. 준비 마크와 반응 피드백
현재는 공격 준비 시점을 시각적으로 보여주기 위한 구조도 추가되어 있다.

- MonsterCombatActor에 AttackMarkAnchor 사용
- CombatReadyMarkView를 통한 ready mark 표시
- 플레이어 반응 시 색상 변화
- 몬스터 준비 및 돌진 시 색상 변화
- 입력 cue / ready cue FMOD one-shot 재생

---

## 8. 현재 확인 가능한 테스트 자산
현재 프로토타입 검증용으로 아래 자산과 씬이 사용되고 있다.

- Assets/Data/Monsters/Boar_Def.asset
- Assets/Data/Monsters/Boar_Pattern.asset
- Assets/Data/Monsters/Boar_Pattern2.asset
- Assets/Data/Charts/Test_Song.asset
- Assets/Data/Charts/PlayerAttack1.asset
- Assets/Scenes/PrototypeCombat.unity

이 자산들은 전투 문장, 차트 구조, 판정, 카메라, 플레이어 대쉬 어택을 실제로 확인하기 위한 테스트용이다.

---

## 9. 아직 남아 있는 한계
현재 구조는 프로토타입으로는 충분히 강해졌지만, 아직 아래 항목은 미완성 또는 확장 전 단계이다.

- 원거리 위협 및 플레이어 원거리 공격 런타임 연결
- 다중 몬스터 lane과 다수 전투 대상 처리
- 판정 등급별 전투 결과 차별화
- 회피, 피격, 반격 결과의 더 풍부한 연출
- generated timeline 디버그 시각화
- editor UX polish
- HP, 결과 등급, 곡 선택, 스테이지 흐름 등 게임 루프 연결

즉 지금은 전투 프로토타입의 핵심 골격은 연결되었지만, 시스템 완성도와 콘텐츠 확장은 아직 계속 필요하다.

---

## 10. 현재 상태 한 줄 요약
현재 Kal_Beat 프로토타입은  
**전투 문장 기반 차트 편집 -> 저장 시 ResolvedTimeline 자동 생성 -> FMOD timeline 기준 1대1 리듬 전투 재생 -> 판정, 카메라, 플레이어 대쉬 어택까지 포함한 전투 프로토타입 검증**  
까지 연결된 상태이다.
