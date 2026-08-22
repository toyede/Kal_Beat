# Kal_Beat 현재 프로토타입 기능 정리

## 1. 문서 목적

이 문서는 현재 저장소에서 실제로 확인할 수 있는 기능을 요약한다. 최신 세계관에서 전투는 비트 모델링 과정이고, 몬스터는 이상 파형을 시각화한 노이즈 몬스터다. 다만 이 문서는 기획 목표가 아니라 구현 여부를 판단하는 기술 현황 문서로 사용한다.

구현 상태는 다음 세 범주로 구분한다.

- 구현 완료: 현재 에디터나 런타임에서 실제로 확인 가능
- 부분 구현: 기반 구조는 있으나 전체 게임 루프와 연결되지 않음
- 미구현: 기획에는 있으나 현재 저장소에 플레이 가능한 흐름이 없음

---

## 2. 현재 프로토타입의 핵심 흐름

현재 연결된 흐름은 다음과 같다.

1. `ChartData`에 곡 메타데이터와 전투 이벤트를 저장한다.
2. 채보 에디터에서 이벤트와 문장 템플릿을 편집한다.
3. 저장 시 패턴 동기화와 validation을 수행한다.
4. 현재 전투 씬의 플레이어·몬스터·원거리 anchor를 해석한다.
5. 위치, 이동, 응답 상태와 투사체 정보를 `ResolvedTimeline`에 미리 계산한다.
6. 런타임은 raw 이벤트를 직접 해석하지 않고 ResolvedTimeline을 재생한다.
7. FMOD timeline 시간에 맞춰 이동, 공격, 판정과 피드백을 실행한다.
8. `PrototypeCombat` 씬에서 Boar 노이즈 몬스터와 1대1 전투를 확인한다.

```text
ChartData
  -> 문장 템플릿 동기화
  -> validation
  -> 씬 anchor 해석
  -> ResolvedTimeline 생성
  -> FMOD timeline 기반 재생
  -> 입력 판정과 전투 피드백
```

---

## 3. 차트 데이터

### 3-1. 메타데이터

`ChartMeta`는 다음 정보를 가진다.

- 곡 ID와 표시 이름
- 에디터 미리 듣기용 AudioClip
- FMOD 이벤트 경로
- BPM
- offset
- 곡 길이
- grid division
- lane 수
- 동일 타임 슬롯 최대 이벤트 수
- lane binding

런타임 필수 메타는 BPM, offset, 곡 길이와 FMOD 이벤트 경로다. 차트의 정규 시간 단위는 beat다.

### 3-2. 전투 이벤트

현재 지원하는 `CombatEventKind`는 다음과 같다.

- `MeleeThreatCounterable`
- `MeleeThreatDodgeOnly`
- `PlayerDashAttack`
- `PlayerRangedAttack`
- `RangedThreatCounterable`
- `RangedThreatDodgeOnly`

각 이벤트는 Ready 또는 Timing phase, 준비 신호, 방향, band, 이동, 넉백과 연결된 문장 템플릿 정보를 가진다. 원거리 이벤트는 phrase, cue, anchor, Open과 투사체 bake 정보도 포함한다.

### 3-3. lane binding

- `ChartMeta`에서 lane별 의미를 고정한다.
- 현재 Monster binding은 `MonsterDefinition`을 참조한다.
- 에디터는 선택한 lane의 노이즈 몬스터가 지원하는 문장 템플릿을 노출한다.
- 데이터 구조는 여러 lane을 저장할 수 있지만 현재 ResolvedTimeline 빌더는 하나의 활성 Monster lane만 지원한다.

---

## 4. 채보 에디터

Unity 메뉴의 `Tools > KalBeat > Chart Editor`에서 사용한다.

### 4-1. 기본 편집

- ChartData 선택
- 곡 메타데이터 편집
- lane binding 편집
- 이벤트 추가, 삭제와 드래그 이동
- grid 표시와 snap
- 줌과 타임라인 이동
- 단일 선택, 다중 선택과 marquee 선택
- 이벤트 속성 일괄 편집
- 문장 템플릿 적용과 연결 정보 표시

### 4-2. 문장 템플릿 동기화

- 적용된 `PatternDefinition`의 asset GUID, pattern ID와 signature를 이벤트에 기록한다.
- 저장 전에 연결된 문장 그룹을 최신 자산 값으로 자동 동기화한다.
- step 수나 timing anchor처럼 구조가 달라지면 자동으로 덮어쓰지 않고 수동 재적용을 요구한다.
- 차트와 몬스터 정의의 signature가 바뀌면 오래된 ResolvedTimeline 재생을 막는다.

### 4-3. 원거리 문장 도구

- Ready call과 Timing response 쌍 생성
- cue 수, 간격과 응답 offset 기반 문장 배치
- phrase ID와 cue index 관리
- 첫 Ready에만 Open 지정
- 한 phrase 안의 응답 일부를 `PlayerRangedAttack`으로 전환
- 플레이어 공격을 다시 노이즈 몬스터 위협으로 복구
- 원거리 회피형과 반격형 문장 편집

### 4-4. 저장과 validation

저장 순서는 다음과 같다.

1. 연결된 문장 템플릿 자동 동기화
2. 차트 메타와 이벤트 검증
3. 원거리 이벤트가 있으면 전투 씬의 anchor 검증
4. ResolvedTimeline 생성
5. 에셋 저장과 Unity refresh

Error가 하나라도 있으면 저장을 차단하고 Warning만 있으면 허용한다. 주요 오류 조건은 다음과 같다.

- BPM 또는 곡 길이가 0 이하
- 유효하지 않은 grid division
- 필수 FMOD 메타 누락
- lane 또는 step 정보 오류
- 동일 타임 슬롯 이벤트 4개 초과
- 잘못 연결된 원거리 phrase
- 필요한 원거리 anchor 또는 투사체 정보 누락

### 4-5. 프리뷰와 외부 데이터

- FMOD 이벤트 프리뷰
- 실제 FMOD timeline 위치 표시
- metronome과 ready/input one-shot 재생
- 외부 JSON 분석 결과 가져오기
- 가져온 timing candidate를 `Kind=None` 참조 마커로 변환

---

## 5. 전투 자산

### 5-1. `MonsterDefinition`

노이즈 몬스터가 지원하는 전투 문장과 이동 시간 감각을 설명하는 자산이다.

- ID와 표시 이름
- `MonsterArchetype`
- 프리팹
- Ready 이동 지원 여부
- 이동 단위별 Ready 지속 시간
- 이동 단위별 Timing 접근 시간
- 지원 `PatternDefinition` 목록
- 런타임 signature

### 5-2. `PatternDefinition`

하나의 전투 문장 템플릿이다.

- ID와 표시 이름
- `CombatEventKind`
- 진입 band
- Ready cue FMOD 경로
- step 목록
- 원거리 cue와 response 설정
- 회피 이동량과 응답 band
- 투사체 프리팹, 방향 기준과 이동 시간
- 차트 동기화 signature

### 5-3. `PatternStepDefinition`

문장의 Ready 또는 Timing step을 beat 기준으로 설명한다.

- Timing 기준 beat offset
- phase와 준비 신호
- 플레이어 및 몬스터 방향 힌트
- `ForwardToBand`, `SideMoveOnBand` 이동 해석
- 이동량과 목표 band
- 플레이어와 몬스터 넉백량

### 5-4. `PlayerAttackDefinition`

현재 플레이어 원거리 공격용 투사체 프리팹과 이동 정보를 제공한다.

---

## 6. ResolvedTimeline

### 6-1. 사전 계산

`ChartResolvedTimelineBuilder`는 차트 이벤트를 순서대로 시뮬레이션하며 플레이어블 AI와 노이즈 몬스터의 duel-local 상태를 계산한다.

- Ready 이동 시작과 종료 위치
- Timing 접근 위치
- 회피와 반격 후 위치
- 진입 및 종료 band
- 양쪽 넉백
- 원거리 phrase의 응답 frame
- 플레이어 자동 응답 band 이동
- 원거리 문장 뒤 다음 비원거리 문장을 위한 회복 위치

### 6-2. 원거리 bake

원거리 반격형은 현재 씬 anchor를 사용해 투사체 정보를 미리 계산한다.

- 생성 anchor와 목표 anchor
- launch 및 contact 시각
- 생성 위치와 목표 위치
- 투사체 front tip offset
- 이동 속도 검증
- 판정 Good window를 고려한 접촉 시각

원거리 차트를 저장하려면 `CombatDuelRoot`, 플레이어·몬스터 actor, 공격 및 피격 anchor, 원거리 anchor root와 `CombatJudgementSystem`이 씬에 연결되어 있어야 한다.

### 6-3. 무결성

- 차트 source signature를 기록한다.
- 몬스터 정의와 지원 문장의 signature를 기록한다.
- 원본 데이터가 변경되면 stale timeline 재생을 중단한다.
- builder 오류가 있으면 timeline을 invalid 상태로 저장하고 이유를 기록한다.

---

## 7. 런타임 전투

### 7-1. 시간 권위

- `CombatTimelineController`가 FMOD event instance를 생성한다.
- 매 프레임 FMOD timeline 위치를 읽어 공용 rhythm clock에 전달한다.
- `Time.time`은 전투 판정과 차트 동기화의 기준으로 사용하지 않는다.
- Ready one-shot은 목표 시각보다 설정된 lead time만큼 먼저 실행할 수 있다.

### 7-2. 위치와 이동

- `CombatDuelRoot`가 duel-local과 world 좌표를 변환한다.
- 플레이어와 노이즈 몬스터의 기본 거리를 단위 크기로 관리한다.
- actor는 공용 rhythm clock을 사용하는 coroutine으로 이동한다.
- 공격 시작 시 잠근 목표와 미리 계산한 post-result 위치를 사용한다.

### 7-3. 근거리 전투

현재 다음 동작이 런타임에 연결되어 있다.

- 근거리 회피형
- 근거리 반격형
- 플레이어 근거리 대시 공격
- Pose 및 Move Ready
- Timing 접근
- 회피, 피격과 반격 넉백
- 판정별 색상과 애니메이션 피드백

### 7-4. 원거리 전투

현재 다음 동작이 런타임에 연결되어 있다.

- 원거리 회피형 빔
- 원거리 반격형 투사체
- 예비박 magic circle과 anchor 표시
- baked projectile 이동
- 플레이어 원거리 공격
- Perfect 또는 Good에서 플레이어 투사체 발사
- Miss에서 플레이어 투사체 미발사
- phrase 종료 후 노이즈 몬스터 회복 이동

### 7-5. actor와 시각화

- `PlayerCombatActor`: 이동, 회피, 공격, 피격, 색상과 애니메이션
- `MonsterCombatActor`: 공통 위치와 anchor 인터페이스
- `BoarMonsterActor`: 현재 Boar 프로토타입의 근거리·원거리 동작
- `CombatReadyMarkView`: Ready 마크
- `RangedMagicCircleView`: 원거리 cue 원형 표시
- `RangedAttackVisualView`: anchor, 빔과 반격 투사체

---

## 8. 판정과 입력

`CombatJudgementSystem`은 Unity Input System 이벤트 시간을 FMOD timeline 시각으로 변환한다.

- Perfect
- Good
- Miss
- Ignore

현재 동작:

- Timing step만 판정 대상으로 등록
- 입력 시점에서 가장 가까운 판정 target 선택
- 입력이 없으면 auto miss
- 허용 범위 밖의 입력은 Ignore
- 판정 결과 이벤트 발행
- 판정 결과에 맞는 actor 피드백 호출

현재 `PrototypeCombat` 씬의 판정 범위는 Perfect 35ms, Good 80ms, Miss 130ms이며 전투 입력은 `Combat/Judgement Button`의 Space 키에 연결되어 있다.

점수 합산, HP 감소, 결과 등급과 난이도별 판정 범위 변경은 아직 전체 게임 루프에 연결되지 않았다.

---

## 9. 오디오와 카메라

### 9-1. FMOD

- 곡 재생과 런타임 시간 동기화
- ready 및 input cue one-shot
- sample data 사전 로드와 description 캐시
- 에디터 프리뷰와 metronome

프로젝트 작업 순서는 `FMOD Studio Save -> Build Banks -> Unity Refresh`다.

### 9-2. 카메라

`QuarterViewFollowCamera`가 다음 기능을 제공한다.

- 플레이어 추적
- 부드러운 위치와 회전 보간
- 이동 중 카메라 회전 잠금
- 이동 종료 후 지연 회전

현재는 1대1 전투 가독성을 위한 기본 카메라이며 작업장 회전이나 연출용 샷 시스템은 아직 없다.

---

## 10. 현재 테스트 콘텐츠

### 10-1. 씬과 프리팹

- `Assets/Scenes/PrototypeCombat.unity`
- `Assets/Prefabs/Player/Player.prefab`
- `Assets/Prefabs/Monsters/Boar.prefab`
- `Assets/Prefabs/RangedCounterProjectile_Prototype.prefab`

`PrototypeCombat`은 현재 Build Settings에서 활성화된 전투 테스트 씬이다.

### 10-2. 전투 데이터

- `Assets/Data/Monsters/Boar/Boar_Def.asset`
- `Assets/Data/Monsters/Boar/Patterns/`의 근거리·원거리 문장 7종
- `Assets/Data/Player/Default_Player_RangedAttack.asset`
- `Assets/Data/Charts/Test_Song.asset`
- `Assets/Data/Charts/Test_Song2.asset`

`Test_Song`은 근거리·원거리·플레이어 공격을 함께 포함한 메인 테스트 차트이며 유효한 ResolvedTimeline을 가진다. `Test_Song2`는 원본 데이터 변경 이후 ResolvedTimeline을 다시 생성해야 하는 보조 테스트 차트다.

### 10-3. 현재 노이즈 몬스터

Boar는 현재 유일하게 완전하게 연결된 노이즈 몬스터 프로토타입이다.

- Hybrid archetype
- Pose 및 Move Ready
- 근거리 회피형과 반격형
- 원거리 빔 회피형
- 원거리 투사체 반격형
- 거리 단위별 이동 시간
- 8개 원거리 anchor

---

## 11. 세계관 기준 기능 상태

### 11-1. 구현 완료

- 비트 모델링의 핵심 전투로 사용할 수 있는 1대1 리듬 액션
- 노이즈 몬스터 전투 문장 자산
- 모델링 음악을 재생하는 FMOD 기반 timeline
- 차트 편집, 검증과 런타임 데이터 생성
- 근거리 및 원거리 call-and-response 전투
- 플레이어블 AI 캐릭터의 기본 전투 모델과 애니메이션

### 11-2. 부분 구현

- 플레이어블 AI: 전투 actor와 애니메이션만 있고 캐릭터 대화와 허브 행동은 없음
- 노이즈 몬스터: 전투 기능은 있으나 이상 파형이라는 최종 시각 정체성은 미완성
- 모델링 데이터: 곡과 ChartData는 있으나 `???` 칩셋, 정체 공개와 완성 모델 연결은 없음
- 결과: 판정은 있으나 점수, HP, 등급과 보상은 없음

### 11-3. 미구현

- 프로그램 시작과 관리자 권한 인증
- 허브 공간과 작업대
- 데이터 칩셋 선택 및 곡 미리 듣기
- 작업장 게이트와 씬 전환
- 곡 클리어 후 노이즈 소멸과 완성 모델 생성
- 데이터 조각, 조건부 곡 해금과 진행도 저장
- 허브 꾸미기와 완성 모델 상호작용
- AI 대화, 관계 변화와 스토리 연출
- 내보내기 기능 부재를 다루는 이야기
- HP, 점수, 결과 등급, 난이도와 보상 UI
- 실패 후 기절 및 작업대 복귀
- 일시 정지와 플레이 재개
- 다중 활성 노이즈 몬스터

---

## 12. 현재 기술 한계

- ResolvedTimeline은 하나의 활성 Monster lane만 지원한다.
- 전투 결과가 점수·HP·진행도로 이어지지 않는다.
- 판정별 연출은 존재하지만 논리적인 후속 상태는 의도적으로 동일하다.
- scene anchor bake 때문에 원거리 채보 저장은 올바른 전투 씬 구성을 요구한다.
- 일부 에디터 구조 변경은 문장 자동 동기화가 아니라 수동 재적용이 필요하다.
- 원거리와 카메라 이펙트는 프로토타입 품질이다.
- `EditorAudioPreview` 반사 기반 유틸리티와 일부 테스트 씬·프리팹은 이전 실험 흔적으로 남아 있다.

---

## 13. 다음 확장 우선순위

1. 프로그램 시작, 튜토리얼과 싱크 조절
2. 허브, 작업대와 데이터 칩셋 선택
3. 작업장 게이트와 전투 진입·복귀
4. HP, 점수, 결과 등급과 실패 흐름
5. 완성 모델 생성과 데이터 해금
6. AI 캐릭터 대화와 허브 상호작용
7. 노이즈 몬스터의 최종 시각·사운드 정체성
8. 작업장 회전, 카메라 무빙과 UI/UX 고도화
9. 음악, 채보와 노이즈 몬스터 콘텐츠 반복 제작

---

## 14. 현재 상태 한 줄 요약

현재 Kal_Beat 프로토타입은 **전투 문장 기반 차트 편집 -> 저장 시 씬 기준 ResolvedTimeline 생성 -> FMOD timeline 기반 근거리·원거리 1대1 전투 -> 입력 판정과 시청각 피드백**까지 연결되어 있으며, 이를 비트 모델링 세계관의 작업대·허브·진행 시스템으로 확장하는 단계다.
