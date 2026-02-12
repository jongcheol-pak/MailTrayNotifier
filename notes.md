# 작업 노트

## 상세 로그
- [2026-02 작업 로그](docs/notes/2026-02.md)

## 미해결 이슈
- 없음

## 주의 사항
- 예외 래핑 시 반드시 원본 예외를 InnerException으로 전달할 것
- 락을 이미 보유한 상태에서 같은 락을 획득하는 메서드를 호출하지 말 것 (데드락)
- async 메서드에서 동기 File I/O를 사용하지 말 것
- fire-and-forget 호출에는 반드시 예외 핸들러를 추가할 것
- 파일은 반드시 UTF-8로 저장할 것
- 비동기 태스크가 보유 중인 리소스(SemaphoreSlim 등)를 동기 메서드에서 즉시 dispose하지 말 것
- fire-and-forget 태스크의 오류 핸들러에서 공유 딕셔너리 항목 제거 시 자신의 항목인지 확인할 것

---

## 최근 변경 요약

### 좌측 메뉴 About 버튼 하단 배치
- MainWindow.xaml 좌측 내비게이션의 StackPanel을 DockPanel로 변경
- MenuAbout 버튼을 DockPanel.Dock="Bottom"으로 하단 고정
- 검증: 빌드 성공

### Reset 시 테마/언어 미초기화 버그 수정
- SettingsViewModel.Reset()에서 테마/언어가 시스템 기본으로 초기화되지 않는 버그 수정
- `_selectedThemeCode`, `_selectedLanguageCode`를 빈 문자열로 초기화 + ApplyTheme/ApplyLanguage 호출
- 초기화 완료 후 LanguageChanged 이벤트로 UI 갱신
- 검증: 빌드 성공

### MainWindow 하드코딩 배경색 제거
- MainWindow.xaml의 하드코딩 배경색 4곳(`#151c26` 3곳, `#21262d` 1곳)을 테마 리소스로 교체
- Grid/TitleBar/Nav: Transparent (Mica 배경 투과)
- 콘텐츠 영역: `CardBackgroundFillColorDefaultBrush` (테마 연동)
- 검증: 빌드 성공

### 테마 변경 기능 추가
- GeneralSettingsPage 언어 설정 아래에 테마 변경 기능 추가 (시스템 기본 / 다크 / 라이트)
- WPF-UI ApplicationThemeManager를 사용한 테마 전환
- 선택한 테마는 settings.json에 저장, 앱 재시작 시에도 유지
- 5개 언어 리소스 파일에 테마 문자열 추가
- 변경 파일: ThemeOption.cs (신규), MailSettingsCollection.cs, SettingsService.cs, SettingsViewModel.cs, GeneralSettingsPage.xaml, App.xaml.cs, 리소스 5개
- 검증: 빌드 성공

### 배포 전 코드 검증 및 이슈 수정
- `MailTrayNotifier.csproj`: `warning.ico`에 `CopyToOutputDirectory=PreserveNewest` 추가 (배포 시 아이콘 누락 수정)
- `MainWindow.xaml.cs`: `ForceClose()`에서 `ViewModel.Dispose()` 호출 추가 (이벤트 구독 해제 누락 수정)
- `README.md`: 최대 계정 수 "5개" -> "10개"로 수정 (MailConstants.MaxAccounts=10과 일치)
- 검증: 빌드 성공 (경고 0건, 오류 0건)

### 일본어/중국어(간체·번체) 리소스 추가
- `Strings.ja.resx` (일본어), `Strings.zh-CN.resx` (중국어 간체), `Strings.zh-TW.resx` (중국어 번체) 리소스 파일 생성
- `SettingsViewModel.cs`: `AvailableLanguages`에 일본어/중국어(간체/번체) 3개 언어 추가
- `MailTrayNotifier.csproj`: 3개 resx 파일 등록 (`DependentUpon`)
- 해당 언어가 없는 경우 .NET 리소스 폴백에 의해 영어(Strings.resx)로 표시
- 검증: 빌드 성공

### 신규 계정 IsEnabled 기본값 및 미저장 계정 정리
- 신규 계정 추가 시 `IsEnabled` 기본값을 `false`로 변경 (저장 전 싱크/오류 아이콘 미표시)
- 창 닫기(숨기기) 시 미저장 신규 계정 자동 제거 (`RemoveUnsavedAccounts`)
- 변경 파일: MailAccountViewModel.cs (`_isEnabled` 기본값), SettingsViewModel.cs (`RemoveUnsavedAccounts` 추가), MainWindow.xaml.cs (`OnClosing`에서 호출)
- 검증: 빌드 성공

### 계정 목록 Expander 기본 상태 변경
- 계정 목록의 모든 Expander가 접힌 상태로 초기화되도록 수정 (기존: 첫 번째 계정만 펼침)
- 변경 파일: SettingsViewModel.cs, MailAccountViewModel.cs
- 검증: 빌드 성공

### GeneralSettingsPage 언어 변경 기능 추가
- 설정 화면에서 언어 선택 (시스템 기본 / English / 한국어)
- 언어 변경 시 즉시 UI 갱신 (페이지 캐시 클리어 + 재로드, MainWindow 정적 텍스트 갱신, 트레이 메뉴 갱신)
- 앱 시작 시 저장된 언어 설정 자동 적용 (UI 생성 전)
- 변경 파일: MailSettingsCollection (Language 속성), SettingsService (LoadLanguageSync), LanguageOption (신규), SettingsViewModel (언어 변경 로직), GeneralSettingsPage.xaml (ComboBox), MainWindow.xaml/cs (x:Name + 핸들러), App.xaml.cs (시작 언어 적용 + 트레이 갱신)
- 리소스 키 3개 추가: LanguageTitle, LanguageDescription, LanguageSystemDefault
- 검증: 빌드 성공

### 하드코딩된 문자열 리소스 파일로 이동
- 12개 하드코딩된 사용자 노출 문자열을 `Strings.resx` / `Strings.ko.resx`로 이동
- XAML: MainWindow.xaml (About), MailSettingsPage.xaml (Port, SSL), GeneralSettingsPage.xaml (On/Off)
- C#: MailClientService.cs (6개 오류 메시지), MailPollingService.cs (1개 알림 메시지)
- SettingsViewModel.cs: 하드코딩된 한국어 문자열 비교를 `Strings.AccountNameTrimmed` 직접 비교로 수정
- 검증: 빌드 성공

### AboutPage Open Source Licenses 버튼 링크 변환
- `Models/OpenSourceLibrary.cs`: 오픈 소스 라이브러리 정보 record 추가 (Name, License, Url)
- `ViewModels/SettingsViewModel.cs`: `LicenseInfo` 문자열 → `OpenSourceLibraries` 컬렉션 + `OpenLicenseUrlCommand` 로 교체
- `Views/AboutPage.xaml`: TextBox → ItemsControl + Button 목록으로 변경, 버튼 클릭 시 해당 라이브러리 홈페이지 열기
- 검증: 빌드 성공

### 2026-02-08 (MailSettingsPage.xaml.cs 깨진 한글 주석 수정)
- Views/MailSettingsPage.xaml.cs: 깨진 한글 주석 2건 UTF-8로 재작성
- 검증: 빌드 성공 (경고 0건, 오류 0건), dotnet format 완료

### 2026-02-08 (폴링 재시작 시 오류 알림 버그 수정)
- [심각] StopAllAccountPolling에서 SemaphoreSlim 조기 dispose로 인한 ObjectDisposedException → 오류 알림 발생
- [심각] 이전 태스크의 오류 핸들러가 새 태스크의 CTS를 제거/dispose하여 새 폴링도 중단
- 수정: SemaphoreSlim 해제를 Dispose()로 이동, RunAccountPollingAsync에서 stale 태스크 검증 추가
- 검증: 빌드 성공 (경고 0건, 오류 0건), dotnet format 완료

### 2026-02-08 (미사용 코드 삭제)
- NotificationService.cs: CS0168 경고 수정 (`catch (Exception ex)` -> `catch (Exception)`)
- Models/MailStateFile.cs 삭제 (미사용)
- Helpers/PasswordBoxHelper.cs 삭제 (미사용)
- MailConstants.cs: 미사용 상수 `DefaultPop3NonSslPort`, `DefaultSmtpNonSslPort` 삭제
- 검증: 빌드 성공 (경고 0건, 오류 0건), dotnet format 완료

### 2026-02-08 (코드 분석 이슈 5건 수정)
- [중간] App.xaml.cs: NotificationService.SaveUidsRequested 이벤트 구독 해제 추가
- [중간] SettingsViewModel.cs: SaveIsRefreshEnabledAsync 중복 Start/Stop 호출 제거
- [낮음] MailStateStore.cs: Clear/ClearAccount에서 SemaphoreSlim dispose 추가
- [낮음] MailInfo.cs: 깨진 한글 주석 UTF-8로 재작성
- [낮음] MailPollingService.cs: ApplySettings 불필요한 else 분기 정리 (유효하지 않은 설정 시 중지)
- 검증: 빌드 성공 (오류 0건), dotnet format 완료

### 2026-02-07 (MailPollingService 2건 버그 수정)
- [심각] RunAccountPollingAsync: 초기 즉시 확인에 개별 try-catch 래핑, 일시적 오류 시 폴링 루프 진입 보장
- [중간] StartAllAccountPolling: TryAdd를 Task 시작 전으로 이동하여 경쟁 상태 해소
- 검증: 빌드 성공 (오류 0건), dotnet format 완료

### 2026-02-07 (코드 검토 이슈 전체 수정)
- [심각] MailClientService: InnerException 보존하여 일시적 오류 분류 정상화
- [심각] MailPollingService: 폴링 루프 내부 try-catch로 일시적 오류 시 영구 중지 방지
- [심각] MailStateStore: SaveAsync 데드락 수정 (LoadFromCacheOrFileAsync 추출)
- [중간] SettingsService: 동기 File I/O -> 실제 비동기 I/O 적용
- [중간] App.xaml.cs, SettingsViewModel.cs: fire-and-forget 예외 처리 보강
- [중간] App.xaml.cs: 빈 예외 핸들러에 Debug.WriteLine 진단 추가
- [낮음] AccountBackup.cs, MailConstants.cs: 깨진 한글 주석 UTF-8로 재작성
- 검증: 빌드 성공 (오류 0건), dotnet format 완료

---

## 2026-02-07 (계정 편집 모드 기능 추가)

### 수행한 작업 요약
- 계정별 수정/취소/저장 버튼 추가
- 기본적으로는 모든 필드 읽기 전용 (편집 불가)
- 수정 버튼 클릭 시 편집 모드 진입
- 취소 버튼 클릭 시 이전 값으로 복원
- 저장 버튼 클릭 시 편집 모드 종료

### 변경된 파일 목록
- `ViewModels/MailAccountViewModel.cs` (IsEditMode 속성, BeginEdit/CancelEdit/EndEdit 메서드, 백업 필드 추가)
- `ViewModels/SettingsViewModel.cs` (InitializeAsync, SaveAsync에서 편집 모드 종료 로직 추가)
- `Views/MailSettingsPage.xaml` (모든 필드에 IsEditMode 바인딩, 수정/취소/저장 버튼 추가)
- `Views/MailSettingsPage.xaml.cs` (버튼 클릭 이벤트 핸들러 추가)

### 변경 내용

#### 1. ViewModel 변경사항
- **IsEditMode 속성**: 편집 모드 여부
- **백업 필드**: 취소 시 복원을 위한 모든 필드의 백업 (_backupPop3Server 등)
- **BeginEdit()**: 편집 모드 진입 + 현재 값 백업
- **CancelEdit()**: 백업된 값으로 복원 + 모든 속성 변경 알림 + 편집 모드 종료
- **EndEdit()**: 편집 모드 종료 (이미 변경된 값 유지)
- 새 계정 생성 시 자동으로 편집 모드 시작

#### 2. SettingsViewModel 변경사항
- **InitializeAsync()**: 기존 계정 로드 시 편집 모드 종료 상태로 설정
- **SaveAsync()**: 저장 전 모든 계정의 편집 모드 종료

#### 3. UI 변경사항
- 모든 TextBox, PasswordBox, CheckBox에 `IsEnabled="{Binding IsEditMode}"` 바인딩
- **버튼 그룹**:
  - 편집 모드가 아닐 때: "수정", "계정 삭제" 버튼 표시
  - 편집 모드일 때: "취소", "저장" 버튼 표시
  - DataTrigger로 IsEditMode에 따라 버튼 표시/숨김 제어

#### 4. 코드 비하인드 변경사항
- **EditAccount_Click**: 계정.BeginEdit() 호출
- **CancelEdit_Click**: 계정.CancelEdit() 호출
- **SaveEdit_Click**: 계정.EndEdit() 호출

### UI 동작
- **기본 상태**: 모든 필드가 읽기 전용, "수정"과 "계정 삭제" 버튼 표시
- **수정 버튼 클릭**: 편집 모드 진입, 모든 필드 편집 가능, "취소"와 "저장" 버튼 표시
- **취소 버튼 클릭**: 편집 전 값으로 복원, 편집 모드 종료
- **저장 버튼 클릭**: 현재 값으로 저장, 편집 모드 종료
- **새 계정 추가**: 자동으로 편집 모드 시작

### 검증 결과
- 빌드: 성공 (경고 12건, 오류 0건)
- LSP 진단: 오류 없음
- dotnet format: 완료

---

## 2026-02-07 (계정별 활성화/비활성화 및 계정 이름 편집 기능 추가)

### 수행한 작업 요약
- 각 계정별로 ToggleSwitch로 활성화/비활성화 설정 가능
- Expander 헤더에서 계정 이름 직접 편집 가능
- 계정 이름 필드를 내부 설정에도 추가

### 변경된 파일 목록
- `Models/MailSettings.cs` (IsEnabled, AccountName 속성 추가)
- `ViewModels/MailAccountViewModel.cs` (IsEnabled, AccountName 속성 추가, DisplayName 동적 업데이트)
- `Services/MailPollingService.cs` (IsEnabled 확인 로직 추가)
- `Views/MailSettingsPage.xaml` (Expander Header 커스터마이징, 계정 이름 필드 추가)

### 변경 내용

#### 1. 모델 변경사항
- `MailSettings.IsEnabled`: 계정 활성화 여부 (기본값: true)
- `MailSettings.AccountName`: 사용자 지정 계정 이름

#### 2. ViewModel 변경사항
- `MailAccountViewModel.IsEnabled`: ToggleSwitch 바인딩
- `MailAccountViewModel.AccountName`: 계정 이름 (변경 시 DisplayName 자동 업데이트)
- `DisplayName` 로직:
  1. AccountName이 있으면 사용
  2. 없으면 UserId @ Pop3Server 형식
  3. 둘 다 없으면 "새 계정"

#### 3. Service 변경사항
- `MailPollingService.StartAllAccountPolling()`:
  - `!account.IsEnabled` 확인 후 비활성화된 계정은 폴링 제외

#### 4. UI 변경사항
- **Expander Header 커스터마이징**:
  - 좌측: 계정 이름 TextBox (IsExpanded=true일 때만 편집 가능)
  - 우측: ToggleSwitch (계정 활성화/비활성화)
- **계정 설정 내부**:
  - "계정 이름(선택)" 필드 추가

### UI 동작
- Expander가 접힌 상태: 계정 이름 표시 (읽기 전용처럼 보임)
- Expander가 펼쳐진 상태: 계정 이름 TextBox 편집 가능
- ToggleSwitch off: 해당 계정 폴링 중지 (다른 계정은 계속 동작)

### 검증 결과
- 빌드: 성공 (경고 12건, 오류 0건)
- LSP 진단: 오류 없음
- dotnet format: 완료

---

## 2026-02-07 (다중 메일 계정 지원 구현)

### 수행한 작업 요약
- 단일 메일 계정만 지원하던 것을 최대 5개의 다중 계정으로 확장
- 레거시 단일 계정 설정 자동 마이그레이션 기능 추가
- 각 계정별 독립적인 병렬 폴링 구현

### 변경된 파일 목록

#### 신규 생성
- `Models/MailSettingsCollection.cs` (다중 계정 컬렉션 모델)
- `ViewModels/MailAccountViewModel.cs` (개별 계정 ViewModel)

#### 수정
- `ViewModels/SettingsViewModel.cs` (ObservableCollection, Add/RemoveAccountCommand)
- `Services/SettingsService.cs` (LoadCollectionAsync/SaveCollectionAsync, 레거시 마이그레이션)
- `Services/MailPollingService.cs` (다중 계정 병렬 폴링, ConcurrentDictionary)
- `Views/MailSettingsPage.xaml` (ItemsControl + Expander 구조)
- `App.xaml.cs` (LoadCollectionAsync 호출로 변경)
- `README.md` (다중 계정 지원 설명 추가)

### 변경 내용

#### 1. 모델 구조 변경
- `MailSettingsCollection`: 다중 계정 컨테이너 (IsRefreshEnabled + Accounts List)
- `MailSettings`: 개별 계정 모델 (기존 유지, 하위 호환성)
- JSON 구조: 단일 객체 → Accounts 배열

#### 2. Service 변경사항
- **SettingsService**:
  - `LoadCollectionAsync()`: 신규 다중 계정 로드 + 레거시 자동 마이그레이션
  - `SaveCollectionAsync()`: 다중 계정 저장 (비밀번호 DPAPI 암호화 유지)
  - 레거시 단일 계정 형식 감지 시 자동 변환

- **MailPollingService**:
  - 각 계정별로 독립적인 `PeriodicTimer` 사용
  - `ConcurrentDictionary<string, CancellationTokenSource>`로 계정별 폴링 관리
  - 계정 추가/제거 시 해당 계정만 동적으로 시작/중지
  - 영구적 오류 발생 시 해당 계정만 중지 (다른 계정은 계속 동작)

#### 3. ViewModel 변경사항
- **SettingsViewModel**:
  - 단일 속성들(Pop3Server, UserId 등) 제거
  - `ObservableCollection<MailAccountViewModel> Accounts` 추가
  - `AddAccountCommand`: 계정 추가 (최대 5개 제한)
  - `RemoveAccountCommand`: 계정 삭제 (확인 메시지 박스)
  - 유효성 검사: 최소 1개 계정 필수

- **MailAccountViewModel**:
  - 개별 계정 설정 래핑
  - `DisplayName`: "UserId @ Pop3Server" 형식 표시
  - `IsExpanded`: UI Expander 확장 상태 (첫 번째 계정만 true)

#### 4. UI 변경사항
- `ItemsControl` + `Expander`로 계정 카드 표시
- "계정 추가" 버튼으로 새 계정 생성
- 각 계정 카드 내에 "계정 삭제" 버튼
- 첫 번째 계정만 기본적으로 펼쳐진 상태

### 레거시 마이그레이션 동작
```
기존 settings.json:
{
  "IsRefreshEnabled": true,
  "Pop3Server": "pop.example.com",
  "UserId": "user@example.com"
}

→ 자동 변환됨:

{
  "IsRefreshEnabled": true,
  "Accounts": [
    {
      "Pop3Server": "pop.example.com",
      "UserId": "user@example.com"
    }
  ]
}
```

### 검증 결과
- 빌드: 성공 (경고 12건, 오류 0건)
  - 경고는 기존 코드의 사용하지 않는 변수 (CS0168)
- LSP 진단: 오류 없음
- dotnet format: 완료

### 제한사항
- 최대 5개 계정까지 추가 가능 (UI 복잡도 및 리소스 관리)
- 각 계정별로 독립적인 폴링 주기 설정 가능

### 동일 실수를 반복하지 않도록 참고
- WPF-UI에는 `Expander` 컨트롤이 없음 (기본 WPF `Expander` 사용)
- 다중 계정 구현 시 각 계정별로 독립적인 `CancellationTokenSource` 관리 필요
- 레거시 데이터 마이그레이션은 앱 시작 시 1회 자동 수행되도록 구현

---

## 2026-01-26 (초기화 버튼 기능 추가)

### 수행한 작업 요약
- 설정 화면의 "초기화" 버튼 기능 구현

### 변경된 파일 목록
- `ViewModels/SettingsViewModel.cs` (ResetCommand 추가, MailStateStore 의존성 추가)
- `Views/MailSettingsPage.xaml` (버튼에 Command 바인딩)
- `Services/SettingsService.cs` (Clear 메서드 추가)
- `MainWindow.xaml.cs` (MailStateStore 주입)
- `App.xaml.cs` (MailStateStore 프로퍼티 노출)

### 변경 내용
- 초기화 버튼 클릭 시:
  1. 확인 메시지 박스 표시
  2. 폴링 서비스 중지
  3. settings.json 파일 삭제
  4. mail_state.json 파일 삭제
  5. 화면 설정값 기본값으로 초기화
  6. "초기화 완료" 메시지 박스 표시

### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

---

## 2026-01-26 (UID 저장 순서 수정)

### 수행한 작업 요약
- mail_state.json 파일에 UID가 오래된 것부터 최근 순서로 저장되도록 수정

### 변경된 파일 목록
- `Services/NotificationService.cs`

### 변경 내용
- `ShowNewMail`에서 UID 문자열 생성 시 날짜 오름차순 정렬 적용
- `newMails.OrderBy(m => m.Date).Select(m => m.Uid)`

### 문제 원인
- MailClientService가 최신 메일부터 역순으로 조회 → 결과: [최신, ..., 오래된]
- 알림 클릭 시 그 순서대로 UID가 저장됨

### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

---

## 2026-01-26 (레거시 인코딩 지원 추가)

### 수행한 작업 요약
- EUC-KR, ISO-2022-KR 등 한국어 레거시 인코딩 지원 추가

### 변경된 파일 목록
- `App.xaml.cs`

### 변경 내용
- `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` 호출 추가
- 앱 시작 시 레거시 인코딩 프로바이더 등록

### 왜 필요했는지
- .NET Core/.NET 5+ 이후 기본적으로 UTF-8과 일부 인코딩만 지원
- EUC-KR, ISO-2022-KR 같은 레거시 인코딩은 명시적으로 등록해야 함
- 오래된 메일 클라이언트나 국내 시스템에서 보낸 메일의 한글이 깨지는 문제 발생

### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

---

## 2026-01-26

### 수행한 작업 요약
- 메일 알림 한글 깨짐 문제 수정

### 변경된 파일 목록
- `Services/MailClientService.cs`

### 변경 내용
- `HeaderList[HeaderId.Subject]` 인덱서 대신 `Header` 객체의 `.Value` 프로퍼티 사용
- MimeKit의 `Header.Value`는 MIME 디코딩된 값을 자동 반환

### 이전 잘못된 수정 내용
- `MimeKit.Utils.Rfc2047.DecodeText(ParserOptions.Default, Encoding.UTF8.GetBytes(rawString))` 사용
- 이미 문자열인 헤더 값을 UTF-8 바이트로 변환하면 이중 인코딩 문제 발생

### 왜 문제였는지
- `HeaderList[HeaderId.Subject]`는 MIME 인코딩된 원본 문자열 반환 (예: `=?UTF-8?B?...?=`)
- 이를 `UTF8.GetBytes()`로 변환하면 원본 ASCII 바이트가 아닌 잘못된 바이트 배열 생성
- 올바른 방법: `Header` 객체의 `.Value` 프로퍼티가 자동으로 MIME 디코딩 수행

### 이번에 어떻게 바로잡았는지
```csharp
// 이전 (잘못된 방식)
var subjectHeader = headers[HeaderId.Subject];

// 수정 (올바른 방식)
var subjectHeaderObj = headers.FirstOrDefault(h => h.Id == HeaderId.Subject);
var subjectHeader = subjectHeaderObj?.Value ?? string.Empty;
```

### 검증 결과
- LSP 진단: 오류 없음

### 동일 실수를 반복하지 않도록 참고
- MimeKit에서 헤더 값을 가져올 때는 `Header.Value` 사용 (자동 MIME 디코딩)
- `HeaderList[HeaderId.X]` 인덱서는 raw 값 반환 (디코딩 안 됨)

---

## 2026-01-24

### 수행한 작업 요약
- 트레이 상주 및 설정 화면 구성
- POP3 UID 기반 새 메일 감지 로직 구현
- Windows 알림 서비스 및 설정 저장 구현
- UID 상태 파일이 비어있는 경우 처리 보강
- MVVMTK0045 경고 제거(수동 속성 구현)
- MailKit 버전 상향 및 빌드 경고 정리

### 변경된 파일 목록
- `App.xaml.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Views/MainPage.xaml`
- `Views/MainPage.xaml.cs`
- `ViewModels/SettingsViewModel.cs`
- `Models/MailSettings.cs`
- `Models/MailStateFile.cs`
- `Helpers/PasswordBoxHelper.cs`
- `Services/SettingsService.cs`
- `Services/MailClientService.cs`
- `Services/MailStateStore.cs`
- `Services/MailPollingService.cs`
- `Services/NotificationService.cs`
- `MailTrayNotifier.csproj`
- `README.md`

### 검증 결과
- LSP 진단: csharp-ls 미설치로 진단 실패
- dotnet format: 경고 1건(MVVMTK0045) 발생
- 빌드: 성공 (경고 0건)
- 테스트/린트: 미실행
- 테스트/린트: 미실행
- 성능/메모리: 폴링 루프 및 UID 저장 방식 정적 검토(대규모 UID 누적 가능성은 있음)

### 이슈 및 조치 사항
- 비밀번호를 LocalSettings에 평문 저장(요청사항에 따라 적용)
- WindowsPackageType=None 환경에서 AppNotification 동작은 OS 설정에 따라 제한될 수 있음
- README.md 신규 작성
- XAML 생성 코드의 CS0612 경고는 NoWarn에 추가하여 억제

### 2026-01-24 (추가)

#### 수행한 작업 요약
- MainPage.xaml의 정의되지 않은 스타일 참조(`TitleTextBlockStyle`, `AccentButtonStyle`) 수정
- App.xaml에 정의된 스타일(`MyLabel`, `PrimaryAction`)로 대체

#### 변경된 파일 목록
- `Views/MainPage.xaml`

#### 검증 결과
- 정적 검토: App.xaml의 리소스 키와 일치하도록 수정함

### 2026-01-24 (csharp-ls 진단 활성화)

#### 수행한 작업 요약
- csharp-ls가 솔루션을 인식하도록 `MailTrayNotifier.sln` 생성 및 프로젝트 연결
- VS Code용 csharp-ls 설정 파일 추가(솔루션 경로/실행 경로 지정)

#### 변경된 파일 목록
- `MailTrayNotifier.sln`
- `.vscode/settings.json`

#### 검증 결과
- LSP 진단: csharp-ls 실행 경로를 설정했으나 현재 환경의 LSP 서버 탐지에서는 `csharp-ls` 미설치로 표시됨
- 빌드/테스트/린트: 미실행
- 성능/메모리/최적화: 설정 파일 추가로 런타임 영향 없음(정적 검토)

#### 이슈 및 조치 사항
- LSP 도구가 PATH에서 csharp-ls를 찾지 못함. VS Code에서는 설정의 `csharp-ls.path`로 우회 가능

### 2026-01-24 (WinUI 3 → WPF 변환)

#### 수행한 작업 요약
- WinUI 3 프로젝트를 WPF로 전체 변환
- 트레이 아이콘: WinUIEx → Hardcodet.NotifyIcon.Wpf
- 토스트 알림: AppNotificationManager → ToastNotificationManager
- 설정 저장: ApplicationData.LocalSettings → JSON 파일 기반
- UID 상태 저장: Windows.Storage API → System.IO 기반
- XAML 바인딩: x:Bind → Binding
- NumberBox → TextBox + 문자열 변환

#### 변경된 파일 목록
- `MailTrayNotifier.csproj` (WPF 프로젝트로 변경)
- `Imports.cs` (WPF 네임스페이스로 변경)
- `App.xaml` (WPF Application으로 변환)
- `App.xaml.cs` (트레이 아이콘 로직 변경)
- `MainWindow.xaml` (WPF Window + 설정 화면 통합)
- `MainWindow.xaml.cs` (WPF Window로 변환)
- `ViewModels/SettingsViewModel.cs` (RefreshMinutesText 속성 추가)
- `Helpers/PasswordBoxHelper.cs` (WPF용으로 변환)
- `Services/SettingsService.cs` (JSON 파일 기반으로 변환)
- `Services/MailStateStore.cs` (System.IO 기반으로 변환)
- `Services/NotificationService.cs` (ToastNotificationManager로 변환)

#### 삭제된 파일 목록
- `Views/MainPage.xaml` (MainWindow에 통합)
- `Views/MainPage.xaml.cs`
- `app.manifest`
- `Assets/` 폴더 전체 (WinUI 관련 리소스)

#### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)
- dotnet format: 완료
- 테스트/린트: 미실행

#### 이슈 및 조치 사항
- 설정 파일 저장 경로: `%LocalAppData%\MailTrayNotifier\settings.json`
- UID 상태 파일 경로: `%LocalAppData%\MailTrayNotifier\mail_state.json`
- 비밀번호는 JSON 파일에 평문 저장 (기존과 동일)
- System.Windows.Forms와 System.Windows.Controls 네임스페이스 충돌 해결 (using 정리)


### 2026-01-24 (NavigationView 메뉴 선택 버그 수정)

#### 수행한 작업 요약
- WPF UI NavigationView의 메뉴 선택이 동작하지 않는 문제 수정
- `ContentOverlay` → Grid 분리 레이아웃으로 변경 (NavigationView가 직접 Content를 가질 수 없음)
- `SelectionChanged` → `ItemInvoked` 이벤트로 변경

#### 변경된 파일 목록
- `MainWindow.xaml` (NavigationView와 Frame 레이아웃 분리)
- `MainWindow.xaml.cs` (ItemInvoked 이벤트 핸들러로 변경)

#### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

### 2026-01-24 (NavigationView 메뉴 선택 버그 재수정)

#### 이전 잘못된 수정 내용
- Grid 분리 레이아웃 + ItemInvoked 이벤트 조합은 WPF-UI의 NavigationView 내장 기능을 활용하지 못함
- Tag 기반 수동 내비게이션은 WPF-UI 4.x에서 올바르게 동작하지 않음

#### 왜 문제였는지
- WPF-UI NavigationView는 자체적으로 페이지 내비게이션을 관리함
- `TargetPageType` 속성을 사용해야 클릭 시 자동으로 페이지 전환됨
- 외부 Frame을 사용하면 NavigationView의 내장 기능이 무시됨

#### 이번에 어떻게 바로잡았는지
- `TargetPageType="{x:Type views:PageName}"` 속성 사용
- NavigationView의 `Navigated` 이벤트에서 DataContext 설정
- `Navigate(typeof(Page))` 메서드로 초기 페이지 설정

#### 변경된 파일 목록
- `MainWindow.xaml` (TargetPageType 속성 추가, 외부 Frame 제거)
- `MainWindow.xaml.cs` (Navigated 이벤트 핸들러로 변경)

### 2026-01-24 (기능 검토 및 수정)

#### 수행한 작업 요약
1. 저장 버튼 활성화/비활성화 로직 추가
   - 초기 값 저장 및 변경 감지 (`HasChanges`, `SaveInitialValues`)
   - `CanSave` 속성으로 저장 버튼 상태 제어
2. 필수 입력 값 유효성 검사 추가
   - `ValidateRequiredFields` 메서드로 빈 필드 검사
   - 저장 시 메시지 박스로 누락된 항목 표시
3. 오류 발생 시 메뉴 비활성화
   - `ErrorOccurred` 이벤트 추가
   - 오류 발생 시 '메일 알림 시작' 메뉴 비활성화

#### 변경된 파일 목록
- `ViewModels/SettingsViewModel.cs` (CanSave, HasChanges, ValidateRequiredFields 추가)
- `Views/MailSettingsPage.xaml` (저장 버튼 IsEnabled 바인딩)
- `Services/MailPollingService.cs` (ErrorOccurred 이벤트 추가)
- `App.xaml.cs` (OnPollingErrorOccurred 이벤트 핸들러)

#### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

#### 기능 구현 상태 확인
1. ✅ IsRefreshEnabled 상태 값 설정 파일 저장 - 구현됨
2. ✅ IsRefreshEnabled true/false에 따른 트레이 메뉴 표시 - 구현됨
3. ✅ 메일 알림 시작/중지 메뉴 동작 - 구현됨
4. ✅ 오류 발생 시 알림 및 메뉴 비활성화 - 구현됨
5. ✅ 저장 버튼 활성화/비활성화 - 구현됨
6. ✅ 필수 값 유효성 검사 메시지 박스 - 구현됨
7. ✅ 알림 클릭 시에만 UID 저장 - 구현됨
8. ✅ 앱 시작/저장 시 즉시 메일 확인 후 주기적 새로고침 - 구현됨
9. ✅ 메모리 누수 방지 (이벤트 구독 해제, Dispose 패턴) - 구현됨
10. ✅ 동시 실행 방지 (SemaphoreSlim) - 구현됨

#### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

#### 동일 실수를 반복하지 않도록 참고
- WPF-UI NavigationView 사용 시 반드시 `TargetPageType` 속성 사용
- 수동 Frame 관리 대신 NavigationView의 내장 내비게이션 기능 활용

### 2026-01-24 (IsRefreshEnabled 즉시 저장 분리)

#### 수행한 작업 요약
- IsRefreshEnabled 토글 스위치를 저장 버튼과 분리하여 즉시 저장되도록 수정
- 토글 변경 시 바로 설정 파일 저장 및 트레이 메뉴 상태 적용

#### 변경된 파일 목록
- `ViewModels/SettingsViewModel.cs`
  - `SaveIsRefreshEnabledAsync()` 메서드 추가
    - `IsRefreshEnabled` 속성에서 즉시 저장 호출
    - `HasChanges()`에서 IsRefreshEnabled 비교 제외
    - `SaveInitialValues()`에서 IsRefreshEnabled 제외

  #### 검증 결과
  - 빌드: 성공 (경고 0건, 오류 0건)

  ### 2026-01-24 (저장 버튼 유효성 검사 수정)

  #### 수행한 작업 요약
  - 저장 버튼을 항상 활성화하도록 변경 (값이 변경되지 않아도 누를 수 있음)
  - 저장 시 필수 입력 값 유효성 검사 메시지 박스 표시
  - 불필요한 CanSave, HasChanges, SaveInitialValues 관련 코드 제거

  #### 변경된 파일 목록
  - `ViewModels/SettingsViewModel.cs`
    - `CanSave` 속성 및 관련 필드 제거
    - `HasChanges()`, `SaveInitialValues()` 메서드 제거
    - `UpdateCanSave()` 호출 제거
    - `ValidateRequiredFields()`에서 IsRefreshEnabled 검사 제거
  - `Views/MailSettingsPage.xaml`
    - 저장 버튼에서 `IsEnabled="{Binding CanSave}"` 제거

  #### 검증 결과
  - 빌드: 성공 (경고 0건, 오류 0건)

## 2026-01-25

### 수행한 작업 요약
프로젝트 전체 검토 및 버그/성능 이슈 수정

1. **심각한 버그 수정**
   - `SettingsService.SaveAsync`에서 `IsRefreshEnabled` 속성 누락 수정
   - `App.CleanupResources`에서 무효한 이벤트 핸들러 해제 코드(`_window.Closing -= null`) 수정

2. **메모리 누수 방지**
   - `MainWindow`에 페이지 캐싱 추가 (매번 새 Page 인스턴스 생성 방지)
   - `ContentFrame` 저널 기록 제거로 메모리 누적 방지
   - `ForceClose()` 메서드 추가로 앱 종료 시 이벤트 핸들러 정상 해제

3. **비동기 I/O 적용**
   - `MailStateStore`의 파일 읽기/쓰기를 `File.ReadAllTextAsync`/`File.WriteAllTextAsync`로 변경
   - `SemaphoreSlim`으로 파일 동시 접근 방지

4. **UID 순서 보장**
   - `MailStateFile.Accounts` 타입을 `Dictionary<string, HashSet<string>>`에서 `Dictionary<string, List<string>>`로 변경
   - UID 개수 제한 시 실제로 오래된 것(앞쪽)부터 제거되도록 수정

### 변경된 파일 목록
- `Services/SettingsService.cs` (IsRefreshEnabled 저장 추가)
- `App.xaml.cs` (ForceClose 호출로 변경)
- `MainWindow.xaml.cs` (페이지 캐싱, ForceClose 메서드, 저널 제거)
- `Services/MailStateStore.cs` (비동기 I/O, SemaphoreSlim, List 기반 순서 보장)
- `Models/MailStateFile.cs` (HashSet → List 변경)

### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)
- dotnet format: 완료
- 성능/메모리: 정적 검토 완료

### 이슈 및 조치 사항
- 기존 `mail_state.json` 파일은 `HashSet` 대신 `List`로 역직렬화됨 (JSON 배열이므로 호환성 유지)

### 동일 실수를 반복하지 않도록 참고
- 설정 복사 시 모든 속성을 빠짐없이 복사할 것
- 이벤트 핸들러 해제 시 실제 등록된 메서드를 지정할 것
- 동기 I/O를 비동기로 래핑하지 말고 실제 비동기 메서드 사용
- HashSet은 순서가 보장되지 않으므로 순서가 필요한 경우 List 사용

### 2026-01-25 (백그라운드 성능 최적화)

#### 수행한 작업 요약
1. **MailStateStore 메모리 캐싱 추가**
   - 앱 실행 중 파일을 1회만 읽고 메모리에 캐싱
   - 변경된 경우에만 파일에 쓰기 (`_isDirty` 플래그)
   - 파일 I/O 횟수 대폭 감소

2. **List.Contains O(n) → HashSet.Contains O(1) 최적화**
   - 중복 체크 시 임시 HashSet 사용
   - 500개 UID 기준 최대 500배 성능 향상

3. **List.RemoveRange 사용**
   - `Skip().ToList()` 대신 `RemoveRange(0, count)` 사용
   - 불필요한 List 재생성 방지

4. **LINQ ToList() 지연 평가 최적화**
   - 새 메일이 없는 경우 List 생성 자체를 방지
   - `newMails ??= new List<MailInfo>()` 패턴 사용

#### 변경된 파일 목록
- `Services/MailStateStore.cs` (메모리 캐싱, HashSet 최적화, RemoveRange)
- `Services/MailPollingService.cs` (LINQ 지연 평가 최적화)

#### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)
- dotnet format: 완료

#### 성능 개선 효과
| 항목 | 이전 | 이후 |
|------|------|------|
| 파일 읽기 | 매 폴링마다 | 앱 시작 시 1회 |
| 파일 쓰기 | 매 저장마다 | 변경 시에만 |
| 중복 체크 | O(n) | O(1) |
| 새 메일 없을 때 List 생성 | 항상 | 안 함 |

### 2026-01-25 (메일 조회 로직 버그 수정)

#### 수행한 작업 요약
POP3 서버가 메일을 날짜순으로 정렬하지 않는 경우 새 메일을 놓치는 버그 수정

1. **30일 이전 메일 처리 개선**
   - `break` → `continue`로 변경 (오래된 메일 건너뛰고 계속 조회)
   - 연속 10개 이상 오래된 메일 발견 시에만 종료 (`ConsecutiveOldMailThreshold`)

2. **개별 헤더 조회 실패 처리**
   - try-catch로 특정 메일 실패해도 다음 메일 계속 조회
   - 실패 시 로그 기록

#### 변경된 파일 목록
- `Services/MailClientService.cs`

#### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

#### 이전 문제점
```
인덱스 99: 2026-01-25 → 가져옴
인덱스 98: 2025-12-01 → break (30일 이전)
인덱스 97: 2026-01-24 → 놓침!
```

#### 수정 후 동작
```
인덱스 99: 2026-01-25 → 가져옴
인덱스 98: 2025-12-01 → continue (건너뜀, 연속 1개)
인덱스 97: 2026-01-24 → 가져옴 (연속 리셋)
```

### 2026-01-25 (네트워크 상태 확인 추가)

#### 수행한 작업 요약
네트워크 상태를 확인하여 사용 가능한 경우에만 메일을 조회하도록 개선

1. **네트워크 가용성 확인**
   - `NetworkInterface.GetIsNetworkAvailable()` 사용
   - 네트워크 불가 시 로그만 남기고 다음 폴링까지 대기

2. **일시적 네트워크 오류 처리**
   - `SocketException`, `IOException`, `TimeoutException` 등
   - MailKit 연결/타임아웃 오류
   - 일시적 오류는 폴링을 중단하지 않고 다음 주기까지 대기

3. **오류 분류**
   - 일시적 오류: 네트워크 연결 실패, 타임아웃 → 다음 폴링까지 대기
   - 영구적 오류: 인증 실패, 설정 오류 → 폴링 중단 및 알림

#### 변경된 파일 목록
- `Services/MailPollingService.cs`
  - `IsNetworkAvailable()` 메서드 추가
  - `IsTransientNetworkError()` 메서드 추가
  - `CheckOnceAsync`에서 네트워크 확인 후 메일 조회
  - `CheckOnceWithLockAsync`에서 일시적 오류와 영구적 오류 분리 처리

#### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

#### 동작 흐름
```
폴링 시작
  ↓
네트워크 확인 ─ 불가 → 로그 기록, 다음 폴링 대기
  ↓ 가능
메일 조회 시도
  ↓
성공 → 새 메일 확인 및 알림
  ↓
일시적 오류 → 로그 기록, 다음 폴링 대기 (중단 안 함)
  ↓
영구적 오류 → 알림 표시, 폴링 중단
```

### 2026-01-25 (네트워크 오류 로그 강화)

#### 수행한 작업 요약
네트워크 오류 발생 시 JsonLogWriter를 사용하여 상세 로그 기록

1. **MailPollingService**
   - 네트워크 불가 시 로그 레벨 `Debug` → `Warning`으로 변경

2. **MailClientService**
   - POP3 서버 연결 실패 시 Warning 로그 (서버:포트 정보 포함)
   - POP3 인증 실패 시 Error 로그 (사용자 ID 정보 포함)

#### 변경된 파일 목록
- `Services/MailPollingService.cs` (로그 레벨 변경)
- `Services/MailClientService.cs` (연결/인증 오류 로그 추가)

#### 검증 결과
- 빌드: 성공 (경고 0건, 오류 0건)

#### 로그 기록 위치
| 상황 | 레벨 | 메시지 |
|------|------|--------|
| 네트워크 불가 | Warning | "네트워크를 사용할 수 없음, 다음 폴링까지 대기" |
| 일시적 네트워크 오류 | Warning | "일시적 네트워크 오류, 다음 폴링까지 대기" |
| POP3 연결 실패 | Warning | "POP3 서버 연결 실패: {서버}:{포트}" |
| POP3 인증 실패 | Error | "POP3 인증 실패: {사용자ID}" |
| 영구적 오류 | Error | "메일 확인 중 오류 발생" |

