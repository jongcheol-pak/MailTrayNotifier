# MailTrayNotifier

POP3 메일 서버를 주기적으로 확인하고 새 메일이 발견되면 Windows 알림을 표시하는 트레이 상주 앱입니다.

## 프로젝트 개요

- WPF 기반 데스크톱 앱 (.NET 9)
- 시스템 트레이 상주 및 설정 화면 제공
- MVVM 패턴(CommunityToolkit.Mvvm) 적용

## 현재 기능 목록

- POP3 서버 연결 및 UID 기반 새 메일 감지
- 새 메일 발생 시 Windows 토스트 알림 표시
- 트레이 아이콘 우클릭 메뉴(설정/종료)
- 트레이 아이콘 좌클릭 시 설정 창 표시
- 설정 저장(POP3/SMTP 서버, ID, 비밀번호, 새로고침 시간)
- 설정 초기화 (설정값 및 메일 상태 전체 삭제)

## 주요 클래스 구조

| 클래스 | 역할 |
|--------|------|
| `MailClientService` | POP3 서버 연결 및 메일 헤더 조회 |
| `MailPollingService` | 주기적 메일 확인 (PeriodicTimer 기반) |
| `MailStateStore` | UID 상태 파일 관리 (HashSet 기반 중복 체크) |
| `NotificationService` | Windows 토스트 알림 표시 및 클릭 이벤트 처리 |
| `SettingsService` | JSON 설정 파일 저장/로드 |
| `SettingsViewModel` | 설정 화면 MVVM ViewModel |

## 동작 방식(흐름)

1. 앱 시작 시 트레이 아이콘 표시 (창은 숨김)
2. 저장된 설정을 로드하여 폴링 서비스 시작
3. POP3 서버에서 UID 목록 조회
4. 신규 UID 발견 시 Windows 토스트 알림 표시
5. 설정 화면에서 저장 시 폴링 주기 갱신

## 설정 정보

- POP3 서버: 메일 수신 서버 주소
- SMTP 서버: 메일 발신 서버 주소(현재 수신에는 사용하지 않음)
- 아이디: 로그인 계정
- 비밀번호: JSON 파일에 평문 저장
- 새로고침 시간: 분 단위

## 외부 인터페이스

- 입력: POP3/SMTP 서버 주소, 계정, 비밀번호, 새로고침 시간
- 출력: Windows 토스트 알림(새 메일 건수)

## 파일 저장 경로

- 설정 파일: `%LocalAppData%\MailTrayNotifier\settings.json`
- UID 상태 파일: `%LocalAppData%\MailTrayNotifier\mail_state.json`

## 의존성

- CommunityToolkit.Mvvm 8.4.0
- Hardcodet.NotifyIcon.Wpf 2.0.1
- Microsoft.Toolkit.Uwp.Notifications 7.1.3
- MailKit 4.14.1

## 성능 최적화

- **헤더만 조회**: 메일 본문을 다운로드하지 않고 헤더(제목, 발신자, 날짜)만 조회
- **역순 조회**: 최신 메일부터 조회하여 최대 100개까지만 확인
- **조기 종료**: 연속 10개 이상 30일 이전 메일 발견 시 조회 중단
- **HashSet 기반**: O(1) 시간복잡도로 UID 중복 체크
- **메모리 캐싱**: UID 상태를 메모리에 캐싱하여 파일 I/O 최소화

## 알려진 제한사항

- 비밀번호를 JSON 파일에 평문 저장 (보안 주의)
- IMAP 미지원 (POP3만 지원)
- 단일 계정만 지원

## 변경 이력 요약

- 2026-01-24: 초기 버전 구현(WinUI 3, 트레이, 폴링, 설정 화면)
- 2026-01-24: WinUI 3 → WPF 변환
- 2026-01-25: 버그 수정 및 성능 개선 (설정 저장 누락 수정, 메모리 누수 방지, 비동기 I/O 적용)
- 2026-01-25: 백그라운드 성능 최적화 (메모리 캐싱, 파일 I/O 최소화, HashSet 기반 중복 체크)
