<p align="center">
  <img src="Assets/icon.png" alt="MailTrayNotifier 앱 아이콘" width="160" />
</p>

<h1 align="center">MailTrayNotifier</h1>

<p align="center">
POP3 메일 서버를 주기적으로 확인하고 새 메일이 도착하면 Windows 토스트 알림으로 알려주는 트레이 상주 앱입니다.<br/>
WinUI 3 (Windows App SDK) · .NET 10 · MVVM(CommunityToolkit.Mvvm) 기반으로 만들어졌습니다.
</p>

## 주요 기능

- POP3 서버 연결 및 UID 기반 새 메일 감지
- 다중 메일 계정 지원 (최대 10개, 계정별 독립 폴링 주기)
- 새 메일 도착 시 Windows 토스트 알림 표시 (클릭 시 처리)
- 시스템 트레이 상주 (좌클릭: 설정 창, 우클릭: 메뉴)
- 트레이 메뉴에서 메일 알림 시작/중지 토글
- 다국어 UI: English / 한국어 / 日本語 / 简体中文 / 繁體中文 (시스템 기본 자동 선택 가능)
- 테마 변경: 시스템 기본 / 다크 / 라이트 (즉시 적용)
- 계정 초기화 / 알림 메일 초기화 (확인 팝업 후 삭제)
- GitHub Releases 기반 자동 업데이트 확인 및 알림
- 비밀번호는 Windows DPAPI로 암호화 저장

## 시스템 요구 사항

- Windows 10 (1809 이상) 또는 Windows 11
- .NET 10 Desktop Runtime (자체 포함 빌드를 사용하는 경우 별도 설치 불필요)

## 설치

### 릴리스에서 설치

1. [Releases](https://github.com/jongcheol-pak/MailTrayNotifier/releases) 페이지에서 최신 버전을 내려받습니다.
2. 압축을 해제한 뒤 `MailTrayNotifier.exe`를 실행합니다.

### 소스에서 빌드

```powershell
git clone https://github.com/jongcheol-pak/MailTrayNotifier.git
cd MailTrayNotifier
dotnet build -c Release -p:Platform=x64
```

빌드 결과물은 `bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/` 아래에 생성됩니다.

## 사용 방법

1. **앱 실행** — 첫 실행 시 시스템 트레이에 메일 아이콘이 나타납니다 (창은 열리지 않음).
2. **설정 창 열기** — 트레이 아이콘을 좌클릭하거나, 우클릭 메뉴에서 `설정`을 선택합니다.

   <img src="docs/screenshots/tray-menu.png" alt="트레이 우클릭 메뉴" width="220" />

3. **계정 추가** — 좌측 메뉴 `메일` → `계정 추가` 버튼을 누르고 정보를 입력합니다.
   - **계정 이름**: 목록에서 표시할 별칭 (선택)
   - **POP3 서버 / 포트**: 메일 제공자가 안내한 수신 서버 주소와 포트 (예: `pop.gmail.com` / `995`)
   - **SMTP 서버 / 포트**: 발신 서버 정보 (현재 수신에는 사용하지 않으며 향후 확장을 위한 항목)
   - **SSL/TLS 사용**: 일반적으로 활성화 권장 (SSL: 995, 비SSL: 110)
   - **아이디 / 비밀번호**: 메일 계정 자격 증명 (Gmail 등은 앱 비밀번호 사용)
   - **동기화 시간(분)**: 1~60분 사이의 폴링 주기 (기본 5분)

   <img src="docs/screenshots/account-settings.png" alt="메일 계정 설정 화면" width="500" />

4. **저장 및 활성화** — 우측 상단 `저장` 버튼을 누른 뒤, 계정 헤더의 토글 스위치로 활성화합니다.
5. **알림 수신** — 새 메일이 도착하면 Windows 토스트 알림이 표시되고, `메일로 이동` 버튼이나 알림 본문을 클릭하면 해당 메일이 처리된 것으로 기록됩니다.

   <img src="docs/screenshots/toast-notification.png" alt="새 메일 토스트 알림" width="400" />

6. **알림 중지** — 트레이 우클릭 → `메일 알림 중지`로 전체 폴링을 일시 중지할 수 있습니다.

### 일반 설정

좌측 메뉴 `설정`에서 자동 실행, 알림 사용 여부, 언어, 테마를 변경하고 계정 및 알림 메일을 초기화할 수 있습니다. 언어를 제외한 항목은 변경 즉시 적용되며, 언어는 앱을 다시 시작한 후 적용됩니다.

<img src="docs/screenshots/general-settings.png" alt="일반 설정 화면" width="500" />

- **자동 실행**: Windows 로그인 시 자동으로 시작 (최초 설치 시 기본 켜짐)
- **알림**: 메일 알림 토스트 사용 여부
- **언어**: 시스템 기본 / English / 한국어 / 日本語 / 简体中文 / 繁體中文 (변경 후 앱을 다시 시작하면 적용)
- **테마**: 시스템 기본 / 다크 / 라이트
- **계정 초기화**: 등록된 모든 계정과 알림 메일 정보를 삭제합니다 (테마/언어 설정은 유지).
- **알림 메일 초기화**: 계정은 유지하고 모든 알림 메일 정보를 삭제합니다. 이후 서버에 남은 메일이 다시 알림될 수 있습니다.

## 설정 파일 위치

| 항목 | 경로 |
|---|---|
| 설정 파일 | `%LocalAppData%\MailTrayNotifier\settings.json` |
| 메일 UID 상태 | `%LocalAppData%\MailTrayNotifier\mail_state.json` |

## 주요 의존성

- [Windows App SDK (WinUI 3)](https://github.com/microsoft/WindowsAppSDK) — UI 프레임워크 및 앱 런타임
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM 프레임워크
- [CommunityToolkit.WinUI Controls](https://github.com/CommunityToolkit/Windows) — SettingsCard 등 설정 UI 컨트롤
- [WinUIEx](https://github.com/dotMorten/WinUIEx) — 창/백드롭 확장
- [MailKit](https://github.com/jstedfast/MailKit) — POP3 클라이언트

## 알려진 제한 사항

- POP3만 지원합니다 (IMAP 미지원).
- 비밀번호는 Windows DPAPI로 암호화되어 동일 사용자 프로필에서만 복호화됩니다.
- 동시 등록 가능한 계정은 최대 10개입니다.

## 라이선스

[MIT License](LICENSE) 하에 배포됩니다.

Copyright © 2026 JongCheol Pak ([@jongcheol-pak](https://github.com/jongcheol-pak))
