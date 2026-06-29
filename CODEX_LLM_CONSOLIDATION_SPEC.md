# WebOCR LLM/OCR → codex 단일화 제거·이관 스펙

작성: 2026-06-29 (Claude 분석). 실행: codex.

## 0. 목표
WebOCR의 **모든 LLM/OCR 작업을 codex CLI 하나로 통합**한다. 이미지 읽기(OCR)는 codex가 `codex exec -i <img> -- "지시문"`으로 직접 보고 처리한다(이미 `run_keyword_job`이 그렇게 동작).
→ Claude/OpenAI 직접호출 클라이언트, Tesseract, Google Vision 경로를 **전부 폐기**한다.

핵심 원칙: **그냥 삭제 금지.** 각 LLM 호출이 만들던 산출물을 codex가 커버하는지 확인하고 이관한 뒤 제거한다. 비-LLM 기능(이미지 다운로드·리스팅이미지 가공·엑셀 export·GS 처리)은 **유지**한다.

---

## 1. 제거 대상 (정확한 위치)

### 1-1. LLM 클라이언트 머신리 (전부 폐기)
- `backend/app/services/anthropic_wrapper.py` — **파일 삭제** (`AnthropicClientWrapper`)
- `backend/app/services/legacy_core.py`
  - `from openai ...` import (≈137)
  - `OpenAI(...)` 생성 (≈277, 309, 335)
  - `refresh_openai_client` (≈319) 및 `core.client` 전역
  - `AnthropicClientWrapper` 분기 (≈153,157,269,271,303,305,327,329)

### 1-2. LLM 텍스트 생성 함수 (codex가 대체 → 제거 또는 codex 호출로 대체)
`backend/app/services/legacy_core.py`:
- `generate_longtail10` (1530) — 롱테일 키워드 10개
- `generate_r_keywords_gpt5` (1580) — R열 키워드
- `generate_keyword_gpt` (2675) — 메인 키워드 (3 호출)
- `generate_keyword_stage2` (2941) — 2차 키워드
- `generate_search_terms20` (3111) — 검색어 20개
- `generate_search_keywords` (3375) — 검색 키워드

`backend/app/services/market_keywords.py`:
- `_generate_bucket_candidates_llm` (1058) — 마켓 키워드 버킷 후보 (core.client 1064,1135)

`backend/app/services/pipeline.py` `run_pipeline`:
- core.client 호출 (1984, 2296, 2308, 4285)
- **비전 호출**: 2090-2098 (base64 이미지 → `image_url` 블록) = Claude/OpenAI 비전 OCR → 제거

### 1-3. Tesseract / Google Vision OCR
- `backend/app/services/ocr.py` — 재노출 파일, 삭제
- `backend/app/services/legacy_core.py` — `setup_tesseract`(347), `_ocr_pil_image`(5349), `ocr_image_url`(5421), `ocr_image_file`(5457), `import pytesseract`(101)
- `backend/app/services/ocr_pipeline.py` — `_get_gv_client`(26), `_ocr_google_vision`(33); `_do_ocr_single`(302)에서 Tesseract/Vision 분기 제거
- 설정 플래그: `use_local_ocr`, `use_html_ocr`, `use_google_vision`, `tesseract_path`, `ocr_excel_path`(검토)

### 1-4. 의존성 / 빌드
- `packaging/requirements-runtime.txt`: `anthropic`(1), `google-cloud-vision`(4), `openai`(6) 제거. `pytesseract` 있으면 제거(확인).
- Tesseract 바이너리 번들 제거 → **EXE 용량 감소**
- Google Vision 키 파일 로딩(`GOOGLE_APPLICATION_CREDENTIALS`) 제거

---

## 2. 유지 대상 (건드리지 말 것)
- **codex 경로**: `run_keyword_job` + `append_codex_images_with_budget`(4457) + `run_codex_group`(5606) — 단일 생성기
- 이미지 다운로드: `ocr_pipeline._download_*`, `_download_sequential_images`, `_exclude_last_numbered_image`
- 리스팅 이미지 가공(`listing_images.py`), 엑셀 export(`io_excel.py`), GS 코드 처리, 마켓 업로드(cafe24/coupang/naver) — 단, 마켓키는 서버 위임 별도 작업

---

## 3. run_pipeline / run_seed_job 재설계
**현재**: 자동화 → `run_seed_job` → Bridge → `run_pipeline`이 (a)Tesseract/Vision OCR + (b)LLM 키워드 생성 + (c)이미지가공/export 까지 다 함. 그 뒤 `run_keyword_job`(codex)이 키워드를 **다시** 생성 → 중복.

**변경**: `run_pipeline`에서 **(a)+(b) 제거**, **(c)만 유지**(다운로드·리스팅이미지·export·GS). 생성(OCR+키워드)은 `run_keyword_job`(codex)이 단독 수행.

---

## 4. 실행 순서 (안전)
1. **호출 그래프 확정** — 위 generate_* / core.client 각각의 호출자(caller)를 grep해서, 제거 시 빈자리에 무엇이 들어가야 하는지 확인
2. **codex 커버리지 검증** — run_keyword_job(codex)의 출력 스키마가 위 함수들의 산출물(롱테일/R열/검색어20/마켓버킷)을 모두 포함하는지 대조. 빠진 필드 있으면 codex prompt.md에 추가
3. run_pipeline에서 OCR 단계 + LLM 키워드 단계 제거 → 다운로드/가공/export만 남김
4. legacy_core의 generate_* + 클라이언트 머신리 삭제, market_keywords LLM 분기 삭제
5. ocr.py 삭제, ocr_pipeline의 Tesseract/Vision 분기 삭제
6. requirements/번들/Vision키 로딩 정리
7. 자동화 1배치 e2e 실행 → 시드·키워드 정상 생성 확인

## 5. 실행 중 반드시 확인할 검증 포인트
- [ ] generate_* 함수가 **codex 외 다른 경로**(예: 단건 키워드 API 엔드포인트)에서도 호출되는가? → 그렇다면 그 엔드포인트도 codex로 이관
- [ ] seed가 keyword 단계 전에 키워드 필드를 **미리 채워야** 하는 소비자가 있는가? (없으면 4-3 안전)
- [ ] `_extract_gs_code_with_option` 등 **GS↔이미지 매핑이 OCR 텍스트에 의존**하는가? → 의존하면 GS 매핑을 소스CSV/PM DB 기반으로 대체 후 OCR 제거
- [ ] core.client를 쓰는 곳이 위 목록 외에 더 있는가? (`grep -rn "core.client\|client.chat.completions" backend/app`)
