# Plan: 인터넷 연결 복구 및 잠금 화면 해제 시 메일 폴링 자동 재시작

## 목표
인터넷 연결이 끊기거나 화면이 잠긴 동안 폴링이 사실상 멈추는 문제를, 네트워크가 다시
연결되거나 잠금이 해제될 때 폴링을 자동으로 재시작하도록 보완한다. 절전 복귀(Resume)는
이미 처리되어 있으므로 동일한 재시작 경로를 재사용한다.

## 범위
- **In scope**
  - `App.xaml.cs`에 네트워크 가용성 변경(`NetworkChange.NetworkAvailabilityChanged`) 구독 →
    네트워크가 사용 가능해질 때 폴링 재시작 트리거
  - `App.xaml.cs`에 세션 전환(`SystemEvents.SessionSwitch`) 구독 →
    잠금 해제(`SessionUnlock`) 시 폴링 재시작 트리거
  - 기존 Resume 디바운스/지연 재시작 로직을 공통 메서드로 추출해 세 트리거가 공유
  - 추가한 이벤트 핸들러의 구독 해제(`CleanupResources`)
- **Out of scope**
  - `MailPollingService` 내부 로직 변경 (기존 `RestartAfterResume()` 그대로 재사용)
  - 폴링 주기/네트워크 판정 알고리즘 변경
  - 원격 데스크톱 연결(`RemoteConnect`) 등 잠금 해제 외 세션 이벤트 처리

## 현황 분석 (Investigation Log)
- `App.xaml.cs:94` `SystemEvents.PowerModeChanged += OnPowerModeChanged` 구독.
- `App.xaml.cs:136-182` `OnPowerModeChanged`: `PowerModes.Resume`만 처리. 10초 디바운스
  (`_resumeLock`/`_lastResumeTime`) + `_resumeCts`로 이전 작업 취소 → 5초 대기 후
  `_mailPollingService.RestartAfterResume()` 호출.
- `MailPollingService.RestartAfterResume()`(`MailPollingService.cs:270`)는 새로고침 활성 +
  유효 계정이 있을 때 `RestartAllAccountPollingLocked()` 호출 → 각 계정 폴링을 재시작하고
  즉시 1회 메일 확인을 수행한다.
- 폴링 루프(`RunAccountPollingAsync`)는 `PeriodicTimer`로 계속 돌지만, 네트워크 불가 시
  `CheckAccountAsync`가 `IsNetworkAvailable()` false로 건너뛰고(`MailPollingService.cs:407`)
  다음 주기까지 대기한다. 또한 모든 계정이 영구 오류로 중지되면 `StopDueToError()`로 전체
  폴링이 멈추며(`MailPollingService.cs:363-366`) 네트워크 복구만으로는 자동 재개되지 않는다.
- `RestartAfterResume` 호출처: `App.xaml.cs:167` 한 곳뿐 (전수 grep 확인).
- `_resumeCts`/`_resumeLock`/`_lastResumeTime` 사용처: 모두 `App.xaml.cs` 내부 (전수 grep 확인).
- 빌드 명령: `dotnet build` (CLAUDE.md 기준).

## 위험
- `NetworkAvailabilityChanged`는 NIC별/짧은 시간 내 다중 발생 가능 → 디바운스 필수.
  ThreadPool 스레드에서 호출되므로 기존 `lock(_resumeLock)` 디바운스로 스레드 안전 처리.
- `IsAvailable == true`가 실제 인터넷 도달성을 보장하지 않음 → 재시작 트리거로만 사용하고,
  실제 도달성은 폴링 루프의 메일 확인이 판단(기존 동작 유지).
- 기존 `OnPowerModeChanged`의 디바운스/지연 로직을 공통화하면 Resume 동작이 회귀할 수 있음
  → 동작(10초 디바운스 + 지연 후 `RestartAfterResume`)을 그대로 보존하도록 추출.

## 설계
1. 디바운스 + 지연 재시작 로직을 `SchedulePollingRestart(int delaySeconds)` private 메서드로
   추출한다. 기존 `_resumeLock`/`_lastResumeTime`(10초 디바운스)/`_resumeCts`를 그대로 사용하고,
   지연 시간만 인자로 받는다. 내부 동작은 현재 `OnPowerModeChanged`와 동일
   (이전 작업 취소 → `Task.Delay(delaySeconds)` → `RestartAfterResume()`).
   단, 현재 `lock` 밖에 있는 토큰 읽기(`var ct = _resumeCts!.Token;`, 현 `App.xaml.cs:159`)는
   트리거 3종 동시 호출 시 경합이 커지므로 **`lock(_resumeLock)` 내부에서 지역변수로 캡처**하도록
   옮긴다.
2. `OnPowerModeChanged`는 `Resume`일 때 `SchedulePollingRestart(60)` 호출로 단순화.
3. `OnSessionSwitch(object, SessionSwitchEventArgs)` 추가: `e.Reason == SessionSwitchReason.SessionUnlock`
   일 때 `SchedulePollingRestart(60)` 호출.
4. `OnNetworkAvailabilityChanged(object?, NetworkAvailabilityEventArgs)` 추가:
   `e.IsAvailable`일 때 `SchedulePollingRestart(60)` 호출 (네트워크 안정화 대기 겸 디바운스 공유).
5. `OnStartup`에서 `SystemEvents.SessionSwitch += OnSessionSwitch`,
   `NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged` 구독.
6. `CleanupResources`에서 두 이벤트 구독 해제 추가.
7. `App.xaml.cs` 상단에 `using System.Net.NetworkInformation;` 추가
   (`NetworkChange`/`NetworkAvailabilityEventArgs`가 이 네임스페이스 소속이며 현재 import에 없음).

세 트리거가 동일한 10초 디바운스 윈도우와 단일 `_resumeCts`를 공유하므로, 짧은 시간에
여러 이벤트(예: 절전 복귀 직후 네트워크 복구)가 겹쳐도 폴링 재시작은 1회로 합쳐진다.

## 작업 분해

### T1. 디바운스/지연 재시작 로직 공통화 — Type C
- Files: `App.xaml.cs`
- 내용: `SchedulePollingRestart(int delaySeconds)` 추출, `OnPowerModeChanged`를 이 메서드 호출로 변경.
- Decision points:
  - 지연 시간: 모든 복구 트리거(Resume/Unlock/NetworkRestored)를 **60초(1분)로 통일**한다.
    (기존 Resume의 5초 → 60초로 변경. 복구 직후 불안정 구간을 피하기 위함)
  - 디바운스 윈도우(10초)는 변경하지 않음.
- Edge cases:
  - `_disposed`/종료 중: 기존과 동일하게 `OperationCanceledException`/`ObjectDisposedException` 무시.
  - `_mailPollingService?.RestartAfterResume()` null 조건부 유지.
- Halt Forecast:
  - `_resumeCts!`의 null-forgiving 패턴 유지 — lock 내부에서 생성 직후 캡처하므로 null 아님.
  - 추출 시 기존 try/catch(`OperationCanceledException`/`ObjectDisposedException`/일반) 블록을
    그대로 옮겨 종료 중 예외로 인한 중단이 없도록 한다.
- Acceptance: `OnPowerModeChanged`가 `SchedulePollingRestart(60)`만 호출하고, 토큰이 lock 내부에서
  캡처되며, 빌드 성공 + Resume 재시작이 10초 디바운스 + 60초 지연으로 동작한다.

### T2. 잠금 해제(SessionSwitch) 트리거 추가 — Type C
- Files: `App.xaml.cs`
- 내용: `OnSessionSwitch` 핸들러 추가, `OnStartup` 구독, `CleanupResources` 해제.
- Decision points:
  - 처리 대상: `SessionSwitchReason.SessionUnlock`만. (그 외 reason 무시)
- Edge cases:
  - 잠금/해제 반복: 디바운스로 중복 재시작 방지.
- Halt Forecast:
  - 델리게이트 시그니처는 `SessionSwitchEventHandler(object, SessionSwitchEventArgs)` —
    핸들러를 `void OnSessionSwitch(object sender, SessionSwitchEventArgs e)`로 선언해 불일치 방지.
  - `SessionSwitchReason`/`SessionSwitchEventArgs`는 `Microsoft.Win32`(이미 import됨) 소속 — 추가 using 불필요.
- Acceptance: 잠금 해제 시 `SchedulePollingRestart(60)`가 호출되고, 구독/해제가 짝을 이루며 빌드 성공.

### T3. 네트워크 가용성 복구(NetworkAvailabilityChanged) 트리거 추가 — Type C
- Files: `App.xaml.cs`
- 내용: `OnNetworkAvailabilityChanged` 핸들러 추가, `OnStartup` 구독, `CleanupResources` 해제,
  필요한 using 추가.
- Decision points:
  - 트리거 조건: `e.IsAvailable == true`만. (해제 이벤트는 무시 — 폴링 루프가 알아서 skip)
- Edge cases:
  - 다중 NIC/연속 이벤트: 디바운스로 1회 재시작.
  - ThreadPool 스레드 호출: `lock(_resumeLock)` 디바운스 + lock 내 토큰 캡처로 안전.
- Halt Forecast:
  - 델리게이트 시그니처는 `NetworkAvailabilityChangedEventHandler(object?, NetworkAvailabilityEventArgs)` —
    핸들러를 `void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)`로 선언.
  - `using System.Net.NetworkInformation;`를 T3에서 반드시 추가(설계 7번).
- Acceptance: 네트워크 사용 가능(`e.IsAvailable`) 전환 시 `SchedulePollingRestart(60)`가 호출되고 빌드 성공.

## 검증 방법
- `dotnet build` 경고/에러 0.
- 코드 리뷰로 구독(`OnStartup`)과 해제(`CleanupResources`)가 모든 이벤트에 대해 짝을 이루는지 확인.
- Resume/Unlock/NetworkRestored 세 경로가 동일한 `SchedulePollingRestart`를 호출하는지 확인.
- (수동) 잠금(Win+L) → 해제 시 폴링 재시작 로그(`절전 모드 복귀 감지 - 폴링 재시작` 경유) 관찰.
- (수동) 네트워크 어댑터 비활성화 → 재활성화 시 폴링 재시작 관찰. 자동 테스트 인프라는 없음.

## 승인 필요 사항
- 신규 OS 이벤트 구독 2종 추가(`SessionSwitch`, `NetworkAvailabilityChanged`) — 동작 추가에 해당.

## Tasks
- [x] T1. 디바운스/지연 재시작 로직 공통화
- [x] T2. 잠금 해제(SessionSwitch) 트리거 추가
- [x] T3. 네트워크 가용성 복구 트리거 추가

## Progress Log
- T1~T3 완료 (2026-05-30, App.xaml.cs 단일 파일): SchedulePollingRestart 공통 추출 +
  SessionSwitch/NetworkAvailabilityChanged 트리거 추가, 지연 60초 통일, 토큰 lock 내 캡처.
  빌드 OK(경고 0/오류 0), spec-compliance-reviewer OK(이슈 0).
