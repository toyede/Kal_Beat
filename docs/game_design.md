# Kal_Beat Game Design (Prototype)

## 1. 프로젝트 개요
- 장르: 3D 스타일라이즈드 리듬 카운터 액션
- 컨셉: 퇴마/정화 분위기에서, 플레이어가 정화 범위를 유지하며 제자리 카운터 입력으로 귀신 공격을 받아친다.
- 개발 방향: 프로토타입 우선, 이후 곡/몬스터/패턴 확장

## 2. 프로토타입 목표 (고정)
- 승리 조건: 곡 종료 시점까지 생존
- 실패 조건: 미스 10회 누적 (HP 10)
- 범위: 곡 1개, 몬스터 1개, 단일 난이도
- 평가지표: 점수/콤보보다 정확도 등급(S~D) 중심

## 3. 코어 플레이 루프
- 몬스터는 비트 패턴에 맞춰 행동한다.
- 예시: 7박자 공격 몬스터의 경우 6박 이동/준비 후 7박에 플레이어를 공격.
- 플레이어는 AttackTiming 타이밍에 카운터 입력.
- 카운터 성공 시 몬스터는 원위치 복귀.

## 4. 채보 이벤트 체계
- AttackTiming: 판정 대상 이벤트 (HP/등급에 직접 반영)
- MonsterAction: 몬스터 상태 전환 트리거 (준비/공격/회복 등)
- CameraCue: 카메라 샷 전환 트리거
- CameraMoving: 카메라 모션 트리거 (진동/이동/회전 등)
- 동시 이벤트 제약: 같은 타임슬롯에 최대 4개
- 우선순위: AttackTiming -> MonsterAction -> CameraMoving -> CameraCue

## 5. 채보 데이터 정책 (MVP)
- 저장 포맷: ScriptableObject (향후 JSON 변환 고려)
- 시간 기준: beat 기반 저장
- 메타 권장 필드:
  - songId
  - songName
  - bpm
  - offsetSec
  - songLengthSec
  - gridDivision
  - fmodEventPath
  - previewAudioClip (에디터 보조용, 선택)
- 이벤트 공통 필드 권장:
  - beat
  - lane
  - eventType
  - monsterActionKind / cameraCueKind / cameraMovingKind (타입별 enum)
  - nudgeMs

## 6. 오디오/FMOD 운영 원칙
- FMOD 2.02.x 가정
- BGM/SFX Bus 분리
- 기본 작업 흐름: Save -> Build Banks -> Unity Refresh
- 에디터 채보 UX는 AudioClip 보조 사용 가능 (파형/스크럽)
- 런타임 재생 정본은 FMOD (BGM/키음/SFX/타임 기준 통일)
- 동기화 안정화를 위해 런타임에서는 FMOD 재생시간을 기준으로 이벤트 발행

## 7. 채보 에디터 MVP 범위
- 상단: 곡/메타(BPM, Offset, Grid)
- 중앙: 타임라인 + 그리드(기본 4/8, 추후 확장)
- 좌측: 이벤트 팔레트(4종)
- 우측: 선택 이벤트 속성
- MVP 기능:
  - 그리드 표시
  - 스냅 On/Off
  - 이벤트 추가/삭제
  - 저장/로드
  - 재생/일시정지
  - 플레이헤드 이동(스크럽)
  - 스페이스바로 임시 마커(None) 입력 후 타입 지정
  - 메트로놈 On/Off
  - 오프셋/BPM 수동 입력
  - 이벤트 단위 nudge(ms) 미세 보정
  - 스크럽 중에는 미리보기만 수행(게임플레이 판정/HP 로직 미실행)

## 8. 검증 규칙 (Validator)
- BPM > 0
- 곡 길이 > 0
- gridDivision 유효값 (MVP: 4 또는 8, 구조는 확장 가능)
- 동일 타임슬롯 이벤트 <= 4
- 필수 메타 누락 금지
- 저장 정책: Error가 1개라도 있으면 저장 차단, Warning만 있으면 저장 허용

## 9. 구현 단계 및 DoD
- 1단계 에디터 MVP
  - 내용: 곡 메타 + 그리드 + 스냅 + 이벤트 추가/삭제 + 저장/로드
  - DoD: 1분 곡 기준 채보 저장/재로드 성공
- 2단계 런타임 리더
  - 내용: 비트 계산 + 이벤트 발행 + 디버그 로그
  - DoD: 에디터 이벤트와 런타임 타임라인 일치
- 3단계 최소 게임플레이
  - 내용: 판정 + HP + 게임오버 + 등급
  - DoD: 곡 종료/실패까지 1회 플레이 완주 가능
- 4단계 연출 연동
  - 내용: MonsterAction + CameraCue + CameraMoving 최소 세트
  - DoD: 가독성 유지 상태에서 연출 반영 확인

## 10. 카메라 연출 가드레일
- 기본 앵커샷: 플레이어 백 쿼터뷰
- 연출샷은 짧게 삽입 후 원복
- 가독성 우선
- 연속 강한 흔들림/과도한 시점 전환 제한

## 11. 현재 미확정 항목
- 판정창(Perfect/Good/Miss) 수치
- S~D 등급 계산식
- 멀티 BPM(템포 마커) 도입 시점
- 레인 확장(4 -> 6) 적용 시점

## 12. 확장 포인트 (Backlog)
- 멀티 BPM(Tempo Map) 지원
- 레인 수 가변(4 -> 6 이상) + 타임슬롯 이벤트 수 정책 분리
- 반복 패턴 자동 생성(선택 구간 반복 배치)
- 카메라 연출 안전장치(강한 흔들림/시점 전환 쿨다운)
