# StockMon

NAS 기반 AI 주식 모니터링 시스템의 초기 구현입니다. **현재는 합성 데이터 조회 전용**이며 실제 증권사·AI·Telegram 연결, 지속 수집, 주문 기능은 없습니다. 주문 인터페이스와 주문 자격증명을 포함하지 않습니다.

## 시작

- Windows/Linux, .NET SDK **10.0.403** (`global.json` 고정)
- DB 통합 시험 및 컨테이너 시험: Docker와 PowerShell 7
- PostgreSQL 16 격리 테스트 컨테이너는 시험 스크립트가 생성·삭제합니다.

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --filter 'Category!=Database'
dotnet run -c Release --project src/StockMon.Worker -- fake-once
# 전체 검증 (Docker 필요)
pwsh ./scripts/verify.ps1 -WithDatabase
```

Fake 응답에는 `quality: Synthetic`이 표시됩니다. 실제 계좌·시세 데이터가 아닙니다. `Monitor__Scenario`로 Normal, MultiPage, Unauthorized, RateLimited, Timeout, PartialPage를 선택할 수 있습니다. 다른 계좌 ID 요청은 거절하며 부분 페이지 실패는 완전한 배치를 반환하지 않습니다. 시세·일봉 조회는 명시적으로 unsupported입니다.

`Monitor__Mode`를 Fake 이외로 설정하면 종료 코드 2로 거절합니다. 실행 명령은 `fake-once`와 별도 관리 명령 `migrate`뿐입니다. 상시 worker/healthcheck는 아직 없으며 기존 SRS Compose 예시를 운영에 실행하면 안 됩니다.

```powershell
docker compose build
docker compose run --rm monitor
```

위 Compose는 네트워크가 차단된 합성 데이터 1회 실행입니다. NAS 운영 배포물은 MVP-10 이후 별도 인수합니다.

## 구현 범위

| 항목 | 내용 |
|---|---|
| MVP-01 | .NET solution, CLI, 설정, 고정 SDK/NuGet lock, Windows/Linux CI |
| MVP-02 | 논리 계좌·명의·종목·품질 모델, decimal/UTC, 기본 거부 권한 |
| MVP-03 | 버전·해시 기반 SQL migration, 제한 DB 역할, 명의 격리 RLS, grant/consent, 최소 감사 이력 |
| MVP-04 | IReadOnlyBroker, Fake adapter, 정상·페이지·401·429·timeout·부분 실패 fixture |

구현과 검증 상태는 [개발 상태](docs/implementation-status.md)에 구분합니다. 실제 주문과 운영 배포는 수행하지 않습니다. Telegram **알림 발송 계획은 유지**하고 외부 채널 수신은 후순위입니다.

## DB 권한과 migration

첫 migration은 전용 PostgreSQL 인스턴스에서 bootstrap 관리자로 수행합니다. 일반 실행에는 관리자 DSN을 전달하지 않습니다. `STOCKMON_MIGRATION_DSN_FILE`이 가리키는 저장소 밖 파일에서 DSN을 읽습니다. 값이나 provider 예외를 로그에 출력하지 않습니다.

```powershell
# 파일은 저장소 밖에 ACL로 보호하여 준비합니다. DSN 값을 명령행에 쓰지 않습니다.
$env:STOCKMON_MIGRATION_DSN_FILE = 'C:\secure\stockmon\migration-dsn.txt'
dotnet run --project src/StockMon.Worker -- migrate
Remove-Item Env:STOCKMON_MIGRATION_DSN_FILE
```

현재는 명시적 SQL+Npgsql migration을 사용합니다. SRS의 EF Core 권장안 대신 권한·RLS 정의를 직접 검토 가능한 SQL로 두었습니다. 전체 migration은 트랜잭션과 advisory lock으로 보호하고 적용한 파일 해시 변경 시 중단합니다. 초기 데이터나 실제 계좌는 자동 등록하지 않습니다. 전용 역할 이름이 이미 다른 DB에서 사용 중이면 자동 변경하지 않고 실패합니다.

bootstrap 관리자는 명의와 논리 계좌를 등록하고 **principal마다 별도 DB login**을 발급하여 `stockmon.principals.db_login`에 연결합니다. 읽기 주체에는 `stockmon_reader`, 명의별 수집 주체에는 `stockmon_collector`만 부여합니다. LOGIN은 NOSUPERUSER/NOBYPASSRLS/NOCREATEROLE/NOCREATEDB이어야 하며 다른 principal 역할의 멤버가 되면 안 됩니다. 앱 로그인은 테이블 소유자가 될 수 없습니다.

RLS는 `session_user`를 사용합니다. 클라이언트가 설정한 owner/principal 값은 신뢰하지 않습니다. DSN과 풀도 principal별로 분리해야 합니다. 하나의 대시보드 공용 DB login으로 여러 사용자를 대리하는 구조는 아직 지원하지 않습니다. 향후 API 인증 주체와 DB 접근 구조 연결은 MVP-09 범위입니다.

- owner는 자기 상세/집계만 기본 허용합니다.
- 타명의 조회는 해당 계좌 grant와 해당 명의의 유효한 consent가 모두 필요합니다.
- account_viewer의 상세 권한은 집계 권한을 포함하지 않습니다. risk_viewer는 원시 상세를 조회할 수 없습니다.
- operator는 투자 상세를 조회할 수 없습니다. collector는 동의가 있어도 다른 명의를 수집할 수 없습니다.
- 앱에는 grant/consent/principal 변경과 audit 조회·수정 권한을 주지 않습니다.
- DB에는 원문 비밀·실계좌번호를 저장하는 열이 없습니다. 감사 로그에는 actor·대상 테이블·작업 종류만 남깁니다. 상세 변경 대사/보존 정책은 후속 범위입니다.

현재 account_observations는 권한 시험용 최소 저장 모델입니다. 잔고/현금의 전체 페이지 원자 저장 및 최신 스냅샷은 MVP-05에서 구현합니다. 집계 SQL/API, 권한 회수 시 알림·캐시 취소는 아직 없습니다.

## 개발·보안·배포

- [SRS v1.2 원본](docs/baseline/SRS_기술설계서.md)은 변경하지 않은 기준 문서입니다.
- [GitHub 전환 제안](docs/GitHub_전환_및_MVP_착수안.md) 및 [현재 적용 차이](docs/srs-delta.md)를 함께 읽습니다.
- `main`에서 작업 브랜치를 만들고 검증된 변경을 검토합니다. CI에는 운영 비밀이나 NAS 배포 권한이 없습니다.
- 실제 키·토큰·계좌번호·원본 계좌 응답·DSN·덤프는 커밋하지 않습니다. 합성 fixture만 사용합니다.
- 커밋 전 `scripts/scan-secrets.ps1`을 실행합니다. 패턴 검사에는 한계가 있으므로 diff를 함께 확인해야 합니다. 비밀이 유출되면 즉시 폐기·재발급하며 파일 삭제만으로 해결된 것으로 보지 않습니다.
- CI는 고정 Action SHA, SDK, NuGet lock 및 컨테이너 digest를 사용합니다. 버전 업데이트는 lock과 테스트를 함께 갱신합니다.
- NAS에는 배포하지 않았습니다. CI 통과는 DSM·장비 성능·백업 복구·실계좌 권한의 인수 증거가 아닙니다.

공식 참조: [Microsoft .NET 배포 메타데이터](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json), [Npgsql](https://www.npgsql.org/doc/basic-usage.html), [PostgreSQL RLS](https://www.postgresql.org/docs/16/ddl-rowsecurity.html).
