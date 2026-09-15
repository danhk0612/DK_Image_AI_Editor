# TASKS

상태 표기: `[ ]` 대기 / `[-]` 진행 / `[x]` 완료

## T01 프로젝트 초기화

- [x] .NET 8 WPF 솔루션/프로젝트
- [x] `.gitignore`
- [x] MIT License
- [x] README 및 기본 문서
- [ ] Windows Release 빌드 검증

## T02 메인 UI Shell

- [x] 좌측 이미지 대화 목록 영역
- [x] 중앙 이미지 캔버스 영역
- [x] 우측 대화/프롬프트 영역
- [x] 상단 새 대화/설정 버튼
- [x] 하단 편집 모드 버튼

## T03 새 이미지 대화

- [x] 파일 선택 진입
- [x] Drag & Drop 진입
- [x] 중앙 이미지 표시 기본 구조
- [x] 대화 엔티티 생성
- [x] 최초 원본을 앱 데이터 폴더로 복사
- [x] 여러 대화를 좌측 목록에 누적 표시

## T04 로컬 대화 저장

- [x] SQLite 스키마
- [x] Conversation 저장/불러오기
- [x] Edit 저장/불러오기
- [x] 이미지 파일 버전 관리
- [x] 프로그램 재실행 시 복원

## T05 설정 창

- [x] 별도 설정 팝업
- [x] API Key 입력
- [x] Windows Credential Manager 저장 구조
- [x] 대표 모델 Selector
- [x] Custom Model ID
- [x] OpenRouter Image Models 컬렉션 링크
- [x] Grok Imagine Image 2.0 / Image Quality 프리셋
- [ ] 실제 Windows 환경 저장/재실행 검증

## T06 OpenRouter 전체 이미지 편집

- [x] `/api/v1/images` 클라이언트 기본 구현
- [x] `input_references` base64 data URL 요청 구조
- [x] `data[0].b64_json` 응답 해석 구조
- [x] 메인 UI의 이미지 수정 버튼과 연결
- [x] 결과 이미지를 새 버전으로 저장
- [x] 요청/결과를 우측 대화 히스토리에 표시
- [x] 다음 요청에서 직전 결과를 입력으로 사용
- [ ] 실제 OpenRouter API 요청 검증

## T07 대화 목록/삭제

- [x] 썸네일
- [x] 대화 제목
- [x] 마지막 작업 시각
- [x] 선택/전환
- [x] 삭제 확인
- [x] DB + 이미지 파일 일괄 삭제

## T08 이미지 캔버스

- [ ] 줌
- [ ] 팬
- [ ] 원본 픽셀 좌표 변환
- [ ] 사각형 선택
- [ ] 선택 영역 이동/크기 변경
- [ ] Esc 선택 해제

## T09 영역 편집

- [ ] 선택 영역 주변 문맥 확장 Crop
- [ ] OpenRouter 편집 요청
- [ ] 실제 선택 영역만 추출
- [ ] 원본 합성
- [ ] 경계 feathering 검토/적용

## T10 Crop 편집

- [ ] 선택 영역만 Crop
- [ ] OpenRouter 편집 요청
- [ ] 결과 크기 정규화
- [ ] 원래 좌표에 합성

## T11 히스토리/버전 탐색

- [x] 요청별 결과 이미지 기록
- [ ] 이전 결과 선택
- [ ] 최신 결과 복귀
- [ ] 원본 비교

## T12 내보내기

- [ ] PNG 저장
- [ ] JPG 저장
- [ ] 현재 결과 저장

## T13 마무리

- [ ] Windows x64 Release 빌드
- [ ] 기본 모델 실 API 테스트
- [ ] Custom 모델 테스트
- [ ] README 사용법 갱신
