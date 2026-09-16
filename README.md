# DK Image AI Editor

Windows용 OpenRouter 기반 대화형 이미지 편집기입니다.

현재 정식 버전: **v1.0.1**

한 이미지에서 대화를 시작하고 한국어 자연어로 반복 수정할 수 있으며, 전체 편집·사각형 영역 편집·선택 영역 Crop 편집을 지원합니다.

## 기술 구성

- Windows 10/11 x64
- .NET 8
- WPF
- SQLite (`Microsoft.Data.Sqlite`)
- OpenRouter Image API
- MIT License

## OpenRouter 이미지 모델

설정 창에서 대표 모델을 선택하거나 Custom Model ID를 직접 입력할 수 있습니다.

- Image Models Collection: https://openrouter.ai/collections/image-models
- 기본 모델: `google/gemini-3.1-flash-image`

현재 프리셋:

- `google/gemini-3.1-flash-image`
- `x-ai/grok-imagine-image-2.0`
- `x-ai/grok-imagine-image-quality`
- `bytedance-seed/seedream-4.5`
- `google/gemini-3.1-flash-lite-image`
- `openai/gpt-image-2`
- `google/gemini-3-pro-image`
- `black-forest-labs/flux.2-pro`

모델 목록, 가격, 편집 지원 범위는 OpenRouter에서 변경될 수 있습니다.

## 주요 기능

- 이미지 한 장으로 새 대화 시작
- 이미지별 대화/편집 기록 로컬 저장
- 최근 대화를 위쪽에 정렬하고 실행 시 최신 대화 자동 선택
- 대화 선택 시 최신 작업 위치로 자동 스크롤
- 좌측 대화 목록에 최신 결과 썸네일과 최신 프롬프트 표시
- 한국어 자연어 이미지 수정
- 과거 결과 이미지 선택 후 해당 이미지를 기준으로 추가 편집
- 기존 작업 `다시 시도` 및 재시도 모델 선택
- 선택 모델의 예상 비용 및 생성 완료 후 실제 비용 표시
- 전체 이미지 편집
- 사각형 영역 편집
  - 선택 영역 주변 문맥 포함
  - 결과는 원본 전체 크기 유지
  - 선택 경계 feather blending
- 잘라서 편집
  - 선택 영역만 AI에 전달
  - 결과를 독립 이미지로 반환
  - 원본 위치에 재합성하지 않음
- 마우스 휠 줌 100~800%
- 가운데 버튼 드래그 팬
- 선택 영역 이동 및 모서리 크기 조절
- 원본 비교
- PNG / JPG(품질 95) / 현재 형식 저장
- 대화별 이미지 저장 위치 선택
  - 앱 내부
  - 원본 이미지 폴더
  - 사용자 지정 폴더
- OpenRouter 오류 유형별 한국어 안내
- OpenRouter API Key를 Windows Credential Manager에 저장
- 설정에서 API Key 표시/숨김 토글
- 시작/런타임 치명 오류 로그 저장

## 기본 사용법

1. `새 대화`에서 이미지를 선택하거나 창으로 이미지를 드래그합니다.
2. 설정에서 OpenRouter API Key와 모델을 지정합니다.
3. 편집 방식을 선택합니다.
   - `전체 편집`: 현재 이미지 전체 수정
   - `영역 편집`: 사각형 영역만 수정하고 원본 전체에 자연스럽게 합성
   - `잘라서 편집`: 선택한 부분만 독립 이미지로 편집
4. 우측 입력창에 수정 요청을 입력하고 `이미지 수정`을 누릅니다.
5. 우측 히스토리에서 과거 결과를 선택하면 해당 이미지를 기준으로 다음 편집을 진행할 수 있습니다.
6. 기존 결과의 `다시 시도`에서 모델을 선택해 당시 입력 이미지·프롬프트·편집 방식을 기준으로 새 결과를 추가할 수 있습니다.

## 캔버스 조작

- 마우스 휠: 확대/축소
- 가운데 버튼 드래그: 팬
- 영역/Crop 모드에서 드래그: 새 선택 영역 생성
- 선택 영역 내부 드래그: 선택 영역 이동
- 네 모서리 핸들 드래그: 선택 영역 크기 조절
- `Esc`: 선택 해제

## 빌드

Visual Studio 2022 또는 .NET 8 SDK가 설치된 Windows 환경에서:

```powershell
dotnet restore .\DK_Image_AI_Editor.sln
dotnet build .\DK_Image_AI_Editor.sln -c Release
```

실행:

```powershell
dotnet run --project .\src\DKImageAIEditor\DKImageAIEditor.csproj -c Release
```

## Windows x64 배포

루트의 `publish.ps1`을 실행합니다.

```powershell
.\publish.ps1
```

출력 위치:

```text
src\DKImageAIEditor\bin\Release\net8.0-windows\win-x64\publish\
```

배포 프로필은 `win-x64`, self-contained, multi-file 구성을 사용합니다. 대상 PC에 별도 .NET 8 Runtime 설치는 필요하지 않으며, ZIP을 통째로 압축 해제한 뒤 `DKImageAIEditor.exe`를 실행해야 합니다.

## 데이터 저장

기본 앱 내부 저장 시:

```text
%LOCALAPPDATA%\DKImageAIEditor\
├─ settings.json
├─ editor.db
├─ Logs\
└─ Conversations\
   └─ {conversation-id}\
      ├─ original.*
      ├─ 0001.*
      ├─ 0002.*
      └─ ...
```

설정에서 원본 폴더 또는 사용자 지정 폴더를 선택하면 새 대화부터 해당 위치에 대화별 폴더가 생성됩니다.

원본 확장자와 모델 출력 형식에 따라 실제 확장자는 달라질 수 있습니다.

OpenRouter API Key는 `settings.json`에 평문으로 저장하지 않고 Windows Credential Manager를 사용합니다.

시작 또는 실행 중 치명 오류가 발생하면 `%LOCALAPPDATA%\DKImageAIEditor\Logs`에 로그를 남깁니다.

## 작업 상태

자세한 현재 작업 상태와 남은 검증 항목은 `docs/TASKS.md`를 참고하세요.

## License

MIT
