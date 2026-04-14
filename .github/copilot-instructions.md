# Copilot Instructions

## 프로젝트 개요

- **프로젝트**: MailTrayNotifier — POP3 메일을 주기적으로 확인하고 Windows 토스트 알림을 표시하는 WPF 트레이 상주 앱
- **프레임워크**: .NET 10 (`net10.0-windows10.0.26100.0`), C# latest, WPF
- **UI 라이브러리**: WPF-UI (`Wpf.Ui` — `FluentWindow`, `NavigationView`) + Hardcodet.NotifyIcon.Wpf (트레이 아이콘)
- **MVVM**: CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`, `[ObservableProperty]`)
- **메일**: MailKit (POP3 전용, IMAP 미지원)
- **알림**: Microsoft.Toolkit.Uwp.Notifications (토스트)
- **빌드/테스트**: `dotnet build` / `dotnet test`

## 아키텍처

### 진입점 및 생명주기
- `App.xaml.cs`가 앱 전체를 관리: 트레이 아이콘, 서비스 생성, 이벤트 구독, 리소스 정리
- DI 컨테이너 없이 `App` 생성자에서 서비스를 직접 생성하고 `App.Instance`로 접근
- `MainWindow`는 `FluentWindow`를 상속, 닫기 시 숨김 처리 (트레이 상주)

### 서비스 계층 (`Services/`)
| 서비스 | 역할 |
|---|---|
| `MailPollingService` | 다중 계정 병렬 폴링 (계정별 `PeriodicTimer` + `CancellationTokenSource`). 이벤트로 상태 전파 |
| `MailClientService` | MailKit POP3 연결, 헤더만 조회 (본문 다운로드 안 함) |
| `MailStateStore` | 계정별 UID를 `HashSet<string>`으로 메모리 캐싱 + 개별 JSON 파일 저장 |
| `SettingsService` | JSON 설정 파일 관리, DPAPI 비밀번호 암호화, 레거시→다중 계정 자동 마이그레이션 |
| `NotificationService` | 토스트 알림 표시 및 클릭 이벤트 처리 |
| `UpdateCheckService` | GitHub Releases API 기반 업데이트 확인 |

### 이벤트 기반 통신 패턴
`MailPollingService`는 이벤트(`RunningStateChanged`, `ErrorOccurred`, `AccountErrorOccurred` 등)로 UI에 상태를 전파하고, `App.xaml.cs`가 `Dispatcher.Invoke`로 트레이 UI를 갱신한다. 새 이벤트 핸들러 추가 시 반드시 `CleanupResources()`에서 해제할 것.

### ViewModel 계층 (`ViewModels/`)
- `SettingsViewModel` — 다중 계정 관리, 언어/테마 변경, 내보내기/가져오기
- `MailAccountViewModel` — 개별 계정 편집 (Memento 패턴으로 취소 지원)

### 모델 (`Models/`)
- `MailSettingsCollection` — 전역 설정 + `List<MailSettings>` 계정 목록
- `MailSettings` — 개별 계정 설정 (POP3, 비밀번호, 주기 등)
- `MailInfo` — 메일 헤더 정보 (UID, 제목, 발신자, 날짜)

## 핵심 규칙

### 트레이 UI 업데이트
- 트레이 메뉴/아이콘 상태 변경은 반드시 `UpdateTrayUI()` 메서드를 통해 수행 (직접 속성 설정 금지)
- 트레이 아이콘 3종: `start.ico`(실행 중), `stop.ico`(중지), `warning.ico`(오류)

### 다국어
- `Resources/Strings.resx` (영문 기본) + `.ko.resx`, `.ja.resx`, `.zh-CN.resx`, `.zh-TW.resx`
- 사용자 표시 문자열은 반드시 리소스 파일 사용. 코드에 하드코딩 금지
- `Strings.Designer.cs`는 자동 생성 — 직접 수정 금지

### 비밀번호 보안
- `SettingsService`에서 DPAPI (`ProtectedData`) 암호화/복호화
- 평문 비밀번호가 JSON에 저장되지 않도록 주의

### 비동기 패턴
- 라이브러리/서비스 코드: `ConfigureAwait(false)` 사용
- UI 이벤트 핸들러: `Dispatcher.Invoke` 경유
- fire-and-forget 호출에는 반드시 `.ContinueWith` 예외 핸들러 추가

### 상수값
- 매직 넘버 대신 `Constants/MailConstants.cs`에 정의된 상수 사용

## 파일 저장 경로

- 설정: `{AppContext.BaseDirectory}\settings.json`
- UID 상태: `{AppContext.BaseDirectory}\mail\{accountKey}.json`
