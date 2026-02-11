# Copilot Instructions

## 필수 지침 참조

이 프로젝트의 모든 작업은 프로젝트 루트의 `AGENTS.md` 파일에 정의된 지침을 따라야 합니다.

**작업 시작 전 반드시 `AGENTS.md` 파일을 읽고 다음을 확인하세요:**

1. 프로젝트 유형 판별 및 해당 Option 선택
2. 작업 원칙 (요청 범위만 수정, 한글 응답)
3. 작업 시작 전 `notes.md`, `README.md` 확인
4. 작업 종료 후 `notes.md`, `README.md` 갱신
5. LSP 진단, 빌드 검증 수행
6. Plan 필요 여부 판단 및 사용자 승인

## 프로젝트 개요

- **프로젝트**: MailTrayNotifier (WPF 메일 알림 앱)
- **프레임워크**: .NET 10, WPF
- **UI 라이브러리**: WPF-UI (Wpf.Ui)
- **MVVM**: CommunityToolkit.Mvvm
- **다국어**: `Resources/Strings.resx` (영문 기본), `Resources/Strings.ko.resx` (한국어)
- **빌드**: `dotnet build`
- **테스트**: `dotnet test`
