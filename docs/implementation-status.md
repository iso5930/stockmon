# 구현 및 검증 상태

2026-11-06 KST. 초기 MVP-01~04 구현. 운영 연결·NAS 배포 없음.

| 범위 | 구현 | 검증 |
|---|---|---|
| MVP-01 | solution/CLI/설정/SDK·패키지 고정/CI/문서 | Windows Release 빌드 경고·오류 0. Linux/컨테이너 CI는 실행 결과 확인 전. |
| MVP-02 | 계좌·명의·종목·품질/decimal/UTC/권한 | 로컬 단위 시험 통과. |
| MVP-03 | migration/제한 역할/RLS/grant/consent/audit | PostgreSQL 통합 시험 작성. 로컬 Docker 미설치로 미실행. Linux CI 결과 대기. |
| MVP-04 | Fake 조회 및 오류 fixture | 로컬 정상·다중 페이지·401·429·timeout·부분 실패·범위·취소 시험 통과. |

로컬: .NET SDK 10.0.403, Windows. 단위 시험 12개 통과. PostgreSQL 통합 시험은 별도 Category=Database이며 환경이 없으면 skipped로 표시한다. 검증 스크립트는 해당 시험을 생략한 경우 NOT RUN을 출력한다.

인수 잔여: Linux 빌드/테스트, 실제 PostgreSQL 권한 시험, Linux Docker build/run, 저장소 브랜치 보호 적용 가능 여부. NAS 실기·실계좌·외부 API·주문은 이번 인수 범위 밖이다.

원격 게시 상태: 자동 승인 검토에서 공개 main 및 작업 브랜치 push의 명시적 승인이 필요하다고 판단하여 차단했다. 아직 GitHub에 게시하지 않았으며 CI도 실행되지 않았다. 사용자의 게시 승인 요청이 대기 중이다. 로컬 변경과 테스트 결과는 보존되어 있다.
