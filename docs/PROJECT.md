# PROJECT

## 제품 목표

DK Image AI Editor는 Windows에서 한 장의 이미지를 중심으로 OpenRouter 이미지 편집 모델과 대화하듯 수정 작업을 반복하는 데스크톱 편집기다.

핵심 사용 흐름:

1. 사용자가 새 대화를 만들고 이미지 한 장을 첨부한다.
2. 한국어 자연어로 수정 요청을 입력한다.
3. 설정된 OpenRouter 이미지 모델이 수정 결과를 반환한다.
4. 결과 이미지를 다음 수정의 입력으로 사용한다.
5. 각 요청과 결과는 같은 이미지 대화 안에 기록된다.
6. 프로그램을 종료했다 다시 실행해도 이미지별 대화가 복원된다.

## 메인 화면

- 좌측: 이미지별 대화 목록
- 중앙: 현재 이미지 캔버스
- 우측: 수정 요청 및 결과 히스토리
- 상단: 새 대화, 설정
- 하단: 전체 편집 / 영역 편집 / Crop 편집 / 선택 해제 / 원본 비교 / 저장

## 설정 창

설정은 메인 화면과 분리된 팝업 창에서 관리한다.

필수 설정:

- OpenRouter API Key
- 대표 이미지 편집 모델 Selector
- Custom Model ID 직접 입력
- OpenRouter Image Models 컬렉션 링크

모델 참고 링크:

https://openrouter.ai/collections/image-models

## 편집 모드

### 전체 편집

현재 이미지 전체를 `input_references`로 OpenRouter에 보내고 반환 이미지를 새 버전으로 저장한다.

### 영역 편집

사용자가 사각형을 지정한다. AI에는 선택 영역 주변 문맥을 포함한 영역을 전달하되 결과에서는 실제 선택 사각형 안쪽만 원본에 합성한다. 이 모드는 모델이 선택 영역 바깥을 변경하더라도 앱 수준에서 바깥 부분을 보존하는 것을 목표로 한다.

### Crop 편집

선택 사각형만 잘라 AI에 보내고 반환 결과를 해당 좌표에 다시 합성한다.

## 대화 단위

하나의 대화는 하나의 최초 원본 이미지를 가진다.

예:

```text
Original
  -> Prompt 01
  -> Result 01
  -> Prompt 02
  -> Result 02
```

V1은 선형 히스토리를 기본으로 한다.

## 로컬 저장

예정 위치:

```text
%LOCALAPPDATA%\DKImageAIEditor\
├─ settings.json
├─ editor.db
└─ Conversations\{conversation-id}\
   ├─ original.png
   ├─ 0001.png
   └─ 0002.png
```

- 설정 메타데이터: JSON
- 대화/편집 메타데이터: SQLite 예정
- 이미지 바이너리: 파일 시스템
- OpenRouter API Key: Windows 자격 증명 관리자

## 라이선스

MIT License
