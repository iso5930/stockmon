# 구현 및 검증 상태

2026-11-06 KST. 초기 MVP-01~04 구현. 운영 연결·NAS 배포 없음.

| 범위 | 구현 | 검증 |
|---|---|---|
| MVP-01 | solution/CLI/설정/SDK·패키지 고정/CI/문서 | Windows/Linux Release 빌드·CLI 검증 및 Linux 컨테이너 빌드·실행 통과. |
| MVP-02 | 계좌·명의·종목·품질/decimal/UTC/권한 | 로컬 단위 시험 통과. |
| MVP-03 | migration/제한 역할/RLS/grant/consent/audit | Linux CI의 격리 PostgreSQL 16에서 migration·RLS·권한 철회·풀 재사용·감사 통합 시험 통과. 로컬 DB 시험은 미실행. |
| MVP-04 | Fake 조회 및 오류 fixture | 로컬 정상·다중 페이지·401·429·timeout·부분 실패·범위·취소 시험 통과. |

로컬: .NET SDK 10.0.403, Windows. 단위 시험 12개 통과. PostgreSQL 통합 시험은 별도 Category=Database이며 환경이 없으면 skipped로 표시한다. 검증 스크립트는 해당 시험을 생략한 경우 NOT RUN을 출력한다.

CI 증거: [verify 실행 42152858639](https://github.com/iso5930/stockmon/actions/runs/42152858639), 코드 commit `248ee68e25a5ae1e13b048a510cbb997fc2591e2`. Windows/Linux 각각 단위 시험 12개 통과. Linux PostgreSQL 통합 시험 1개 통과(건너뜀 0), Compose 설정 검사·Docker 빌드·네트워크 차단 Fake 실행 통과. 초기 CI에서 드러난 검증 스크립트 종료 코드 처리와 SQL 문자열 오류는 수정했다.

인수 잔여: 저장소 브랜치 보호 적용 가능 여부 및 후속 MVP. 전체 T01/T04의 API·알림·캐시 범위는 아직 구현 전이다. NAS 실기·실계좌·외부 API·주문은 이번 인수 범위 밖이다.

원격 게시 상태: 사용자의 두 브랜치 게시 승인을 받아 main(기준 문서)과 feature/mvp-01-04(구현)를 게시했다. 위 코드 commit의 GitHub CI 검증을 완료했다. 이 결과 기록은 문서만 변경하며 코드 재검증은 생략한다. 작업 브랜치를 main에 병합하거나 운영에 배포하지 않았다.
