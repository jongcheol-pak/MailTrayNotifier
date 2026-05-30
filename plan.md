# Plan: 설정 화면 초기화 기능 추가 (계정 초기화 / 알림 메일 초기화)

## 목표
일반 설정(GeneralSettingsPage) 하단에 두 초기화 기능을 추가한다.
1. **계정 초기화**: 등록된 모든 계정 + 알림 메일 정보 삭제 (테마/언어는 유지).
2. **알림 메일 초기화**: 계정은 유지하고 모든 계정의 알림 메일 정보만 삭제.
3. 두 버튼 모두 클릭 시 경고(확인) 팝업 표시.

## 범위 / 승인 (사용자 결정 2026-05-30)
- 계정 초기화 = 계정+알림메일만 삭제, 테마/언어 유지.
- 기존 미노출 `Reset()`/`ResetCommand` 로직을 "계정 초기화"로 재사용/조정(테마·언어 리셋 부분 제거).
- 알림 메일 초기화 = 폴링 중지 → 상태 삭제 → 재시작. 이후 서버 잔존 메일 재알림 가능성은 경고 문구에 명시.
- UI = GeneralSettingsPage 하단 SettingsCard 2개(각 실행 버튼). 계정 초기화는 위험 강조.

## 현황 / 영향 분석 (Investigation Log)
- 기존 `SettingsViewModel.Reset()` (786행, `[RelayCommand]` → `ResetCommand`): 경고 → 폴링 Stop → `SettingsService.Clear()` → `MailStateStore.Clear()` → 계정/테마/언어 UI 초기화. **현재 어떤 XAML에도 바인딩 안 됨**(grep 전수 확인). 직접 호출처 없음 → 메서드명/동작 변경 안전.
- 재사용 자산: `SettingsService.Clear()`(settings.json 삭제), `MailStateStore.Clear()`(mail_state_*.json 전체 삭제, 캐시/락 정리), `MailPollingService.Stop()/Start()`(Start 내부에 유효계정·IsRefreshEnabled 조건 검사 존재, 161행), `Dialogs/MessageBox.Show`(Win32 래퍼, YesNo/Warning).
- known(읽음) UID는 사용자가 **알림 클릭 시에만** 저장(`NotificationService`) → 알림메일 초기화 시 재알림 가능.
- 리소스 이중 구조:
  - x:Uid → `Strings/{ko,en-US,ja,zh-CN,zh-TW}/Resources.resw` (`GsXXX.Header`/`.Description`, 버튼 `*.Content`).
  - 코드 메시지 → 중립 `Resources/Strings.resx`(=영어) + `Strings.ko/ja/zh-CN/zh-TW.resx` (별도 en-US resx 없음, 중립이 영어) + `Resources/Strings.Designer.cs`. **Designer.cs는 수동 관리**(`public static string X => GetString("X")` 패턴, csproj에 ResXFileCodeGenerator 미지정) → 신규 키 접근자를 직접 추가해야 함. 기존 키 `ResetConfirmMessage`/`ResetConfirmTitle`/`ResetCompleted`/`AlertTitle` 존재.

### 동시성 분석 (알림 메일 초기화)
`MailStateStore.Clear()`는 계정별 `SemaphoreSlim` 락을 `Dispose()`한다. 동일 락을 동시에 만질 수 있는 경로:
1. **알림 클릭 저장** `App.OnSaveUidsRequested`(`App.xaml.cs:569`): `SaveUidsRequested` 이벤트 구동, 폴링 Stop과 무관. → `LoadAsync`/`SaveAsync`가 dispose된 세마포어 접근 가능. **단 메서드 전체가 `try/catch(Exception)`로 감싸여 크래시 없음**(Debug 로그만).
2. **잔여 폴링 워커**: `Stop()`은 `cts.Cancel()`만 하고 fire-and-forget 워커를 await하지 않음 → `CheckAccountAsync`→`LoadAsync`가 진행 중일 수 있음. 워커 루프도 예외 catch.
- **판단**: 모든 동시 접근 경로가 예외를 catch → **앱 크래시 없음**. 최악의 경우 극히 드문 타이밍(알림 클릭과 초기화 버튼 동시)에 알림 1건 저장 유실 또는 무해한 예외 로그. 이 `Stop→Clear` 패턴은 **기존 `Reset()`(L799-806)에 이미 존재**하므로 신규 회귀가 아님. → **추가 방어 없이 현행 리스크 수용**(아래 Out of Scope).

## 작업 단계

### T1 (Type C) — x:Uid 리소스 추가 (.resw 5개 언어)
- 파일: `Strings/ko/Resources.resw`, `Strings/en-US/Resources.resw`, `Strings/ja/Resources.resw`, `Strings/zh-CN/Resources.resw`, `Strings/zh-TW/Resources.resw`
- 추가 키:
  - `GsResetAccounts.Header`, `GsResetAccounts.Description`
  - `GsResetAccountsButton.Content`
  - `GsClearMailStates.Header`, `GsClearMailStates.Description`
  - `GsClearMailStatesButton.Content`
- Acceptance: 5개 파일에 동일 키 세트가 각 언어 번역으로 추가됨.

### T2 (Type C) — 코드 메시지 리소스 추가/조정 (.resx 5개 + Designer.cs)
- 파일: `Resources/Strings.resx`, `Strings.ko.resx`, `Strings.ja.resx`, `Strings.zh-CN.resx`, `Strings.zh-TW.resx`, `Resources/Strings.Designer.cs`
- 조정: `ResetConfirmMessage` 문구를 "계정 초기화(계정+알림메일, 테마/언어 유지)" 의미로 수정. `ResetConfirmTitle`/`ResetCompleted` 재사용.
- 신규 키: `ClearMailStatesConfirmMessage`, `ClearMailStatesConfirmTitle`, `ClearMailStatesCompleted` (+ Designer.cs 접근자 3개).
- 중립 `Strings.resx`=영어 값, 나머지 4개 .resx=해당 언어 값. 즉 신규 키 3개를 5개 .resx 모두에 추가, Designer.cs에 접근자 3개 1세트 추가.
- Acceptance(자동): `dotnet build` 0경고, `Strings.ClearMailStates*` 컴파일 성공, 5개 .resx 각각에서 신규 키 3개 grep 확인.

### T3 (Type D) — SettingsViewModel 명령 2개
- 파일: `ViewModels/SettingsViewModel.cs`
- `Reset()` → `ResetAccounts()`로 변경(명령명 `ResetAccountsCommand`). 테마/언어 리셋 코드(814~820행) 제거, 나머지(경고/폴링 Stop/Clear 2종/계정목록 Clear/완료 메시지) 유지. 확인 메시지는 `ResetConfirmMessage`.
- 신규 `ClearAllMailStates()` `[RelayCommand]`(→ `ClearAllMailStatesCommand`): 경고(`ClearMailStatesConfirmMessage`) → `MailPollingService.Stop()` → `MailStateStore.Clear()` → `MailPollingService.Start()` → 완료(`ClearMailStatesCompleted`).
- Acceptance: 두 명령이 빌드되고 XAML에서 바인딩 가능.

### T4 (Type C) — GeneralSettingsPage UI
- 파일: `Views/GeneralSettingsPage.xaml`
- 테마 카드 아래 SettingsCard 2개 추가:
  - 계정 초기화: `x:Uid="GsResetAccounts"`, HeaderIcon(위험), Button `x:Uid="GsResetAccountsButton"` `Command="{Binding ResetAccountsCommand}"`, 위험 강조 스타일.
  - 알림 메일 초기화: `x:Uid="GsClearMailStates"`, HeaderIcon, Button `x:Uid="GsClearMailStatesButton"` `Command="{Binding ClearAllMailStatesCommand}"`.
- Acceptance: 두 카드가 표시되고 버튼 클릭 시 각 명령 실행.

### T5 (Type A) — 문서
- `README.md`: "일반 설정"의 기존 "초기화: 모든 설정과 메일 상태를 삭제합니다." 항목을 두 항목(계정 초기화 / 알림 메일 초기화)으로 정정. "주요 기능"에도 반영.
- `notes.md`: 변경 항목 추가.

## Edge Cases / Halt Forecast
- 계정/상태 파일 없음(이미 빈 상태): `Clear()`는 파일 없으면 무시(기존 try/catch) → 정상.
- 알림메일 초기화 시 폴링이 유효계정 없거나 IsRefreshEnabled=false: `Start()`가 내부 조건으로 시작 안 함 → 정상.
- 파일 삭제 실패(잠금): 기존 Clear가 예외 무시 → Halt 없음.
- 경고에서 '아니오' 선택: 즉시 return, 아무 변경 없음.
- **동시성 경합(알림 클릭 저장 vs 초기화, 잔여 폴링 워커 vs Clear)**: 위 "동시성 분석" 결정대로 모든 경로가 예외 catch → 크래시 없음, 추가 방어 없음(현행 수용). 구현 시 Stop→Clear→Start 순서를 기존 `Reset()`과 동일하게 유지하면 됨 → 구현 중 멈출 분기 없음.
- ViewModel 명령명 변경(`ResetCommand`→`ResetAccountsCommand`): 기존 바인딩 없음(전수 확인) → 깨질 호출자 없음.

## Out of Scope
- `MailStateStore`의 락 관리/동시성 구조 재설계(현행 리스크 수용).
- 서버측 메일 삭제(로컬 상태/설정만 삭제).
- 기존 전체 초기화(테마/언어 포함)를 별도로 유지(요청에 따라 계정 초기화로 흡수).

## 검증 방법
- 자동: `dotnet build` 경고/에러 0. 신규 리소스 키 grep 확인. Command 바인딩 컴파일.
- 반자동: 수정 코드 1회 재확인(누락 점검).
- 수동(사용자): 두 버튼 → 경고 팝업 → 계정/알림메일 삭제, 테마/언어 유지 확인.

## 승인 필요 사항
- 결정 4건 모두 사용자 승인 완료. 동시성은 "현행 수용"으로 판단 완료. 추가 승인 불필요.

## Progress Log
- T1 완료: 5개 언어 `.resw`에 카드/버튼 x:Uid 키 6종 추가. grep 20건 확인.
- T2 완료: 5개 `.resx` 기존 Reset 문구 조정 + 신규 키 3종, Designer.cs 접근자 3종 추가.
- T3 완료: `SettingsViewModel` `Reset()`→`ResetAccounts()`(테마/언어 리셋 제거) + `ClearAllMailStates()` 신규. 빌드 OK.
- T4 완료: `GeneralSettingsPage.xaml` 초기화 카드 2개(위험 강조 버튼) 추가. 빌드 OK(경고0/오류0).
- T5 완료: README 초기화 항목 정정(주요 기능 + 일반 설정), notes.md 기록.

## Next Steps
- 권장 다음 액션: 실제 앱 실행으로 두 버튼 동작(경고 팝업/삭제/테마·언어 유지) 육안 확인. 이상 없으면 사용자 검토 후 커밋.
- Suggested skills: 공식 /code-review, 공식 /verify
