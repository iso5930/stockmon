# SRS v1.2 대비 현재 구현 차이

2026-11-06 KST. 원본은 `baseline/`에 보존한다. 원본 추적표의 planned를 구현 완료로 일괄 변경하지 않는다.

| 요구사항/구간 | 현재 적용 및 남은 범위 |
|---|---|
| FR-035, NFR-009, 15장, POC-11 | GitHub를 메인 저장소로 사용한다. SVN revision을 Git commit SHA로 바꾸며 Windows/Linux CI를 추가한다. 자동 커밋 도구, 브랜치 보호 및 필수 검사 설정 인수는 아직 미완료다. |
| NFR-001, T01/T19 | Git/이미지 제외 규칙, 비밀 패턴 검사, 합성 데이터만 사용한다. 패턴 검사만으로 비밀 부재를 보증하지 않는다. 전체 Git 이력 검사·서버 push protection 적용은 후속이다. |
| FR-036, MVP-11/12 | 검증 스크립트는 배포·자동 병합을 호출하지 않는다. NAS 릴리스·백업·롤백은 후속이다. |
| 19장 DB 권장안 | 초기 migration은 EF Core 대신 명시적 SQL+Npgsql을 사용한다. migration 버전/해시·트랜잭션·잠금을 제공하고 DB 수준 권한을 직접 시험한다. |
| NFR-002, 7장, T04 | principal별 DB login 및 풀로 명의를 격리한다. DB session_user를 인증 경계로 사용하며 caller가 지정하는 owner 변수를 신뢰하지 않는다. 공용 대시보드 로그인 방식은 미지원이다. |
| MVP-01, 6장 | Worker CLI는 fake-once 및 분리된 migrate만 제공한다. BackgroundService 수집·healthcheck·운영 Compose는 미구현이다. 현재 Compose는 네트워크 없는 합성 smoke test다. |
| MVP-03, 8장 | accounts/owners/principals/grants/consents/instruments, 최소 audit 및 scoped observation을 제공한다. positions/cash 원자 저장은 MVP-05다. |
| MVP-04, 7.1장 | 원본 IReadOnlyBroker 조회 계약을 유지한다. positions/cash는 합성 구현, quotes/daily bars는 unsupported. 실제 계좌의 capability 검증 성공을 반환하지 않는다. |
| FR-029/030 | Telegram 발송 요구사항과 P5 계획 유지. 외부 텔레그램 채널 수신은 후순위이며 이번 구현에 포함하지 않는다. |

검증 기준: 전체 제품의 FR/T 항목이 아니라 이번에 구현한 좁은 범위의 증거만 기록한다. DB 시험 미실행은 통과와 구분한다. 운영 명의·동의·실계좌 PoC를 합성 시험으로 대체하지 않는다.
