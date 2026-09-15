# ARCHITECTURE

## 기술 기준

- .NET 8
- WPF
- Windows 10/11 x64
- OpenRouter Unified Image API

## 주요 컴포넌트

```text
MainWindow
├─ Conversation List
├─ Image Canvas
├─ Chat History
└─ Prompt Input

SettingsWindow
├─ OpenRouter API Key
├─ Preset Model Selector
├─ Custom Model ID
└─ Image Models Collection Link

Services
├─ AppSettingsService
├─ CredentialStore
├─ OpenRouterImageService
├─ ConversationStore        (예정)
└─ ImageCompositionService  (예정)
```

## OpenRouter 연결

이미지 편집 요청은 다음 API를 사용한다.

```text
POST https://openrouter.ai/api/v1/images
Authorization: Bearer {OPENROUTER_API_KEY}
```

기본 요청 개념:

```json
{
  "model": "google/gemini-3.1-flash-image",
  "prompt": "사용자 수정 요청",
  "input_references": [
    {
      "type": "image_url",
      "image_url": {
        "url": "data:image/png;base64,..."
      }
    }
  ]
}
```

응답의 `data[0].b64_json`을 새 이미지 버전으로 사용한다.

## 모델 관리

대표 프리셋은 `Models/OpenRouterModelPreset.cs`에서 관리한다.

현재 기준 출처:

https://openrouter.ai/collections/image-models

프리셋은 편의를 위한 목록일 뿐 전체 OpenRouter 모델 목록을 하드코딩하지 않는다. 사용자는 Custom Model ID로 새 모델을 앱 업데이트 없이 사용할 수 있어야 한다.

향후 필요 시 `GET /api/v1/images/models`를 사용해 런타임 모델/지원 파라미터 확인 기능을 추가할 수 있다.

## 보안

OpenRouter API Key는 일반 설정 JSON에 저장하지 않는다.

Windows Credential Manager의 Generic Credential을 사용하고 Target Name은 다음으로 고정한다.

```text
DKImageAIEditor/OpenRouterApiKey
```

## 편집 영역 좌표

선택 사각형 좌표는 화면 좌표가 아니라 원본 이미지 픽셀 좌표로 저장한다.

```text
SelectionX
SelectionY
SelectionWidth
SelectionHeight
```

따라서 캔버스 줌/팬 상태와 무관하게 동일 영역을 재현할 수 있어야 한다.

## 영역 편집 합성

영역 편집은 특정 모델의 전용 마스크 API에 종속시키지 않는다.

1. 선택 영역 주변을 일정 비율 확장한다.
2. 확장 Crop을 OpenRouter에 전달한다.
3. AI 결과에서 실제 선택 영역에 대응하는 부분만 추출한다.
4. 원본 이미지의 동일 좌표에 합성한다.
5. 필요 시 경계 feathering을 적용한다.

이 방식으로 모델별 inpainting API 차이를 피한다.
