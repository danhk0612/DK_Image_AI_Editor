# DK Image AI Editor

Windows용 OpenRouter 기반 대화형 이미지 편집기입니다.

한 이미지에서 대화를 시작하고 한국어 자연어로 수정 요청을 반복하며, 이후에는 사각형 영역 지정 및 Crop 기반 부분 편집까지 지원하는 것을 목표로 합니다.

## 기술 구성

- Windows 10/11 x64
- .NET 8
- WPF
- SQLite (`Microsoft.Data.Sqlite`)
- OpenRouter Image API
- MIT License

## OpenRouter 이미지 모델

프로그램의 모델 프리셋은 OpenRouter의 현재 이미지 모델 컬렉션을 참고합니다.

- Image Models Collection: https://openrouter.ai/collections/image-models
- 기본 모델: `google/gemini-3.1-flash-image`
- 설정 창에서 대표 모델을 선택하거나 Custom Model ID를 직접 입력할 수 있습니다.

현재 프리셋에는 다음 모델이 포함됩니다.

- `google/gemini-3.1-flash-image`
- `x-ai/grok-imagine-image-2.0`
- `x-ai/grok-imagine-image-quality`
- `bytedance-seed/seedream-4.5`
- `google/gemini-3.1-flash-lite-image`
- `openai/gpt-image-2`
- `google/gemini-3-pro-image`
- `black-forest-labs/flux.2-pro`

모델 목록, 가격, 편집 지원 범위는 OpenRouter에서 변경될 수 있으므로 프로그램 설정 창에도 위 컬렉션 링크를 제공합니다.

## 목표 기능

- 이미지 한 장으로 새 대화 시작
- 좌측 이미지별 대화 목록
- 대화/이미지 결과 기록 로컬 저장 및 삭제
- 한국어 자연어 이미지 수정
- 이전 편집 결과를 다음 수정 입력으로 사용
- 전체 이미지 편집
- 사각형 영역 편집
- 선택 영역 Crop 편집
- OpenRouter API Key 설정
- 대표 이미지 모델 Selector
- Custom OpenRouter Model ID
- 결과 이미지 저장

## 현재 구현 상태

- [x] .NET 8 WPF 프로젝트 초기화
- [x] MIT License
- [x] 3열 메인 UI 기본 Shell
- [x] 이미지 파일 선택 및 Drag & Drop
- [x] 이미지별 새 대화 생성
- [x] 원본 이미지를 앱 데이터 폴더로 복사
- [x] SQLite 기반 대화/편집 기록 저장 구조
- [x] 프로그램 재실행 시 대화 목록 복원
- [x] 좌측 대화 썸네일/제목/마지막 작업 시간
- [x] 대화 전환 및 삭제
- [x] 설정 팝업
- [x] OpenRouter API Key 입력 UI
- [x] Windows 자격 증명 관리자 기반 API Key 저장 구조
- [x] 이미지 모델 프리셋 / Custom Model ID 설정
- [x] OpenRouter Image Models 컬렉션 링크
- [x] OpenRouter `/api/v1/images` 편집 클라이언트
- [x] 전체 이미지 편집 요청 UI 연결
- [x] 편집 결과 버전 파일 저장
- [x] 프롬프트/모델/결과 이미지 대화 히스토리 저장 및 복원
- [x] 직전 편집 결과를 다음 요청 입력으로 사용
- [ ] Windows Release 빌드/실 API 검증
- [ ] 사각형 선택 도구
- [ ] 영역 편집 및 합성
- [ ] Crop 편집 및 합성
- [ ] 이미지 저장/내보내기

자세한 작업 순서는 `docs/TASKS.md`를 참고하세요.

## 빌드

Visual Studio 2022 또는 .NET 8 SDK가 설치된 Windows 환경에서:

```powershell
dotnet restore .\DK_Image_AI_Editor.sln
dotnet build .\DK_Image_AI_Editor.sln -c Release
```

## 데이터 저장

```text
%LOCALAPPDATA%\DKImageAIEditor\
├─ settings.json
├─ editor.db
└─ Conversations\
   └─ {conversation-id}\
      ├─ original.png
      ├─ 0001.png
      ├─ 0002.png
      └─ ...
```

원본 확장자와 모델 출력 형식에 따라 실제 확장자는 달라질 수 있습니다.

OpenRouter API Key는 `settings.json`에 평문으로 저장하지 않고 Windows 자격 증명 관리자를 사용합니다.

## License

MIT
