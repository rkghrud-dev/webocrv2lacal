from __future__ import annotations

from dataclasses import dataclass, field
import json
import re
from typing import Iterable

from . import legacy_core as core


@dataclass
class MarketKeywordPackages:
    search_keywords: str
    coupang_tags: list[str]
    naver_tags: list[str]
    candidate_pool: list[str]
    market_keywords: dict[str, str] = field(default_factory=dict)


MARKET_KEYWORD_COLUMNS_10 = (
    "홈런_Cafe24검색어설정",
    "홈런_Cafe24검색키워드",
    "홈런_스마트스토어태그",
    "홈런_스마트스토어검색키워드",
    "홈런_쿠팡검색태그",
    "홈런_쿠팡검색키워드",
    "홈런_ESM검색키워드",
    "홈런_11번가검색키워드",
    "홈런_롯데ON검색키워드",
    "홈런_공통마켓검색키워드",
)


_BUCKET_ORDER = (
    "identity",
    "usage_context",
    "function",
    "problem_solution",
    "material_spec",
    "audience_scene",
    "synonyms",
)

_SPACE_KEEP_RE = re.compile(r"[^0-9A-Za-z가-힣\s]")
_TOKEN_RE = re.compile(r"[0-9A-Za-z가-힣]+")
_BAD_END_RE = re.compile(r"(하다|하는|되어|됨|하기|하고|하는데|이다|입니다)$")
_BAD_JOSA_RE = re.compile(r"(에|에서|으로|로|을|를|이|가|은|는|의|와|과)$")

_EXTRA_BAN = {
    "마켓",
    "스토어",
    "쇼핑몰",
    "샵",
    "몰",
    "상품",
    "제품",
    "정품",
    "할인",
    "배송",
    "쿠폰",
    "당일",
    "무료",
    "특가",
    "행사",
    "사은품",
    "추천",
    "인기",
    "선물",
    "귀여운",
    "예쁜",
    "고급진",
    "럭셔리",
    "힐링",
    "인싸",
    "필수품",
    "데일리",
    "프리미엄",
    "고품질",
    "최고급",
    "베스트",
    "핫딜",
    "신상",
    "모음",
    "추천템",
    "가성비",
    "초특가",
}

_NAVER_TAG_SELLER_BAN = {
    "홈런마켓",
    "홈런",
    "준비몰",
    "스마트스토어",
    "네이버",
    "쿠팡",
    "카페24",
}

_NAVER_TAG_CATEGORY_BAN = {
    "생활용품",
    "생활잡화",
    "주방용품",
    "욕실용품",
    "청소용품",
    "정리용품",
    "수납용품",
    "자동차용품",
    "차량용품",
    "캠핑용품",
    "원예용품",
    "반려동물용품",
    "디지털",
    "가전",
    "공구",
    "철물",
    "산업용품",
    "문구",
    "완구",
    "가구",
    "인테리어",
    "패션잡화",
    "잡화",
    "스포츠",
    "레저",
    "식품",
    "화장품",
    "뷰티",
    "출산",
    "유아동",
    "침구",
    "커튼",
    "주방",
    "욕실",
    "자동차",
    "카테고리",
}

_USAGE_HINTS = {
    "차량",
    "본넷",
    "보닛",
    "트렁크",
    "게이트",
    "적재함",
    "정원",
    "전기박스",
    "콘센트",
    "가구",
    "도어",
    "실내",
    "실외",
    "캠핑",
    "현장",
    "원예",
    "호스",
    "급수라인",
    "욕실",
    "화장실",
    "주방",
    "싱크대",
    "세면대",
    "배수구",
    "하수구",
    "창문",
    "벽면",
    "천장",
    "선반",
    "옷장",
    "서랍장",
    "붙박이장",
    "책상",
    "캐비닛",
    "수납장",
}

_FUNCTION_HINTS = {
    "설치",
    "장착",
    "체결",
    "연결",
    "고정",
    "거치",
    "잠금",
    "밀폐",
    "방수",
    "방진",
    "누수방지",
    "회전",
    "각도조절",
    "분리",
    "개폐",
    "작업등",
    "실링",
    "절단",
    "컷팅",
    "결속",
    "수납",
    "정리",
    "지지",
    "부착",
    "끼움",
    "교체",
    "수리",
    "보수",
    "배수",
    "분사",
    "고정력",
}

_PROBLEM_HINTS = {
    "방지",
    "차단",
    "보호",
    "보강",
    "완화",
    "해결",
    "흔들림",
    "누수",
    "유입",
    "처짐",
    "밀폐",
}

_MATERIAL_HINTS = {
    "스틸",
    "철제",
    "스테인리스",
    "스텐",
    "알루미늄",
    "고무",
    "플라스틱",
    "실버",
    "블랙",
    "화이트",
    "304",
    "ABS",
    "니켈",
    "아연합금",
}

_AUDIENCE_HINTS = {
    "사용자",
    "기사",
    "운전자",
    "시공",
    "수리",
    "작업",
    "원예",
    "DIY",
    "튜닝",
    "캠핑",
}

_IDENTITY_HINTS = {
    "브라켓",
    "브래킷",
    "마운트",
    "거치대",
    "홀더",
    "가스켓",
    "가스킷",
    "개스킷",
    "패드",
    "힌지",
    "경첩",
    "커넥터",
    "조인트",
    "캐치",
    "래치",
    "고리",
    "링",
    "도어락",
    "앵커포인트",
    "조명",
    "클램프",
    "브러시",
    "필터",
    "밸브",
    "후크",
    "볼트",
    "너트",
    "나사",
    "핀",
    "호스",
    "파이프",
    "케이블",
    "밴드",
    "테이프",
    "커버",
    "마개",
    "캡",
    "노즐",
    "레일",
    "롤러",
}

_SPEC_NUMERIC_RE = re.compile(
    r"("
    r"m\d+|"
    r"\d+(/\d+)?(mm|cm|m|ml|l|v|w|a|kg|g|호|인치|평|구|단|매|개|입|p|pcs|ea)|"
    r"\d+(인용|인분|자루|박스)|"
    r"\d+[xX]\d+"
    r")",
    re.IGNORECASE,
)
_PRICE_NUMERIC_RE = re.compile(r"\d{2,}(원|₩|만원|천원)")
_BROKEN_NUMERIC_RE = re.compile(r"^(?:[0-9OI]{3,}|[A-Z]?[0-9OI]{2,}[A-Z]?)$", re.IGNORECASE)
_ALLOWED_LATIN_TOKEN_RE = re.compile(r"^(?:abs|eva|pvc|pa\d+|pe|pp|pet|diy|m\d+)$", re.IGNORECASE)
_SIZE_OPTION_TOKEN_RE = re.compile(
    r"("
    r"(?:xs|s|m|l|xl|xxl|xxxl|free|프리|소형|중형|대형|특대형|프리사이즈)|"
    r"\d+(?:\.\d+)?(?:mm|cm|m|ml|l|kg|g)|"
    r"\d+(?:\.\d+)?(?:호|개|매|장|입|세트|p|pcs|ea)|"
    r"\d+(?:\.\d+)?(?:mm|cm|m)?[xX*]\d+(?:\.\d+)?(?:mm|cm|m)?(?:[xX*]\d+(?:\.\d+)?(?:mm|cm|m)?)?"
    r")",
    re.IGNORECASE,
)


def _has_disallowed_latin(text: str) -> bool:
    compact = _compact_phrase(text)
    if not re.search(r"[A-Za-z]", compact):
        return False
    without_specs = _SPEC_NUMERIC_RE.sub("", compact)
    for token in re.findall(r"[A-Za-z]+\d*", without_specs):
        if not _ALLOWED_LATIN_TOKEN_RE.fullmatch(token):
            return True
    return False


def generate_market_keyword_packages(
    product_name: str,
    source_text: str,
    model_name: str = "gpt-4.1-mini",
    anchors=None,
    baseline=None,
    naver_keyword_table: str = "",
    market: str = "A",
    avoid_terms: Iterable[str] | str | None = None,
) -> MarketKeywordPackages:
    anchor_set = set(anchors or [])
    baseline_set = set(baseline or [])
    if not anchor_set:
        anchor_set = set(core.build_anchors_from_name(product_name))
    if not baseline_set:
        baseline_set = set(core.build_baseline_tokens_from_name(product_name))
    _expand_topic_refs_from_product_name(anchor_set, baseline_set, product_name)
    avoid_keys = _build_avoid_semantic_keys(avoid_terms)
    product_name_keys = _build_product_name_semantic_keys(product_name)
    llm_bucketed = _generate_bucket_candidates_llm(
        product_name=product_name,
        source_text=source_text,
        model_name=model_name,
        naver_keyword_table=naver_keyword_table,
    )
    fallback_bucketed = _generate_bucket_candidates_fallback(
        product_name=product_name,
        source_text=source_text,
        naver_keyword_table=naver_keyword_table,
    )

    bucketed = _empty_bucket_map()
    for bucket in _BUCKET_ORDER:
        bucketed[bucket].extend(llm_bucketed.get(bucket, []))
        bucketed[bucket].extend(fallback_bucketed.get(bucket, []))

    bucketed["synonyms"].extend(_extract_naver_candidates(naver_keyword_table))
    bucketed = _normalize_bucket_map(
        bucketed,
        anchors=anchor_set,
        baseline=baseline_set,
        market=market,
        avoid_keys=avoid_keys,
    )

    candidate_pool = _flatten_bucket_map(bucketed)
    coupang_tags = _build_coupang_tags(
        bucketed=bucketed,
        candidate_pool=candidate_pool,
        product_name=product_name,
        source_text=source_text,
        anchors=anchor_set,
        baseline=baseline_set,
        market=market,
        avoid_keys=avoid_keys,
        product_name_keys=product_name_keys,
    )
    naver_tags = _build_naver_tags(
        bucketed=bucketed,
        candidate_pool=candidate_pool,
        product_name=product_name,
        source_text=source_text,
        anchors=anchor_set,
        baseline=baseline_set,
        market=market,
        avoid_keys=avoid_keys,
        product_name_keys=product_name_keys,
    )

    search_source = coupang_tags or [_normalize_phrase(x) for x in candidate_pool]
    search_keywords = " ".join(search_source[:18]).strip()
    market_keywords = _build_market_keyword_variants(
        bucketed=bucketed,
        candidate_pool=candidate_pool,
        coupang_tags=coupang_tags,
        naver_tags=naver_tags,
    )
    return MarketKeywordPackages(
        search_keywords=search_keywords,
        coupang_tags=coupang_tags,
        naver_tags=naver_tags,
        candidate_pool=candidate_pool,
        market_keywords=market_keywords,
    )


def _empty_bucket_map() -> dict[str, list[str]]:
    return {bucket: [] for bucket in _BUCKET_ORDER}


def _coerce_list(value) -> list[str]:
    if value is None:
        return []
    if isinstance(value, list):
        return [str(x).strip() for x in value if str(x).strip()]
    if isinstance(value, dict):
        out: list[str] = []
        for child in value.values():
            out.extend(_coerce_list(child))
        return out
    text = str(value).strip()
    if not text:
        return []
    return [x.strip() for x in re.split(r"[,\n|;/]+", text) if x.strip()]


def _normalize_phrase(text: str, compact: bool = False) -> str:
    source = re.sub(r"d\s*링", "디링", str(text or ""), flags=re.IGNORECASE)
    cleaned = _SPACE_KEEP_RE.sub(" ", source)
    cleaned = re.sub(r"\s+", " ", cleaned).strip()
    return cleaned.replace(" ", "") if compact else cleaned


def _compact_phrase(text: str) -> str:
    return _normalize_phrase(text, compact=True)


def _is_pure_size_option_token(text: str) -> bool:
    compact = _compact_phrase(text).lower()
    if not compact:
        return False
    if re.fullmatch(r"m\d+", compact, re.IGNORECASE):
        return False
    if re.fullmatch(r"\d+형", compact):
        return False
    if _ALLOWED_LATIN_TOKEN_RE.fullmatch(compact):
        return False
    return bool(_SIZE_OPTION_TOKEN_RE.fullmatch(compact))


def _strip_size_option_tokens(text: str) -> str:
    phrase = _normalize_phrase(text)
    if not phrase:
        return ""
    kept = [tok for tok in phrase.split() if not _is_pure_size_option_token(tok)]
    return _normalize_phrase(" ".join(kept))


def _is_size_option_phrase(text: str) -> bool:
    phrase = _normalize_phrase(text)
    if not phrase:
        return False
    return bool(phrase) and not _strip_size_option_tokens(phrase)


def _expand_topic_refs_from_product_name(anchor_set: set[str], baseline_set: set[str], product_name: str) -> None:
    for phrase in _collect_adjacent_phrases(product_name, max_tokens=24, max_size=2):
        cleaned = _strip_size_option_tokens(phrase)
        compact = _compact_phrase(cleaned)
        if not compact:
            continue
        if any(hint in compact for hint in _IDENTITY_HINTS):
            anchor_set.add(cleaned)
            baseline_set.add(cleaned)


def _semantic_key(text: str) -> str:
    key = core._clean_one_kw(_compact_phrase(text)).lower()
    replacements = (
        ("차량용", "차량"),
        ("브래킷", "브라켓"),
        ("디링", "d링"),
        ("가스킷", "가스켓"),
        ("개스킷", "가스켓"),
        ("스텐", "스테인리스"),
        ("고정대", "거치대"),
    )
    for old, new in replacements:
        key = key.replace(old, new)
    key = re.sub(r"(용|형|식)$", "", key)
    return key


def _build_avoid_semantic_keys(values: Iterable[str] | str | None) -> set[str]:
    if values is None:
        return set()

    if isinstance(values, str):
        raws = [values]
    else:
        raws = [str(x) for x in values if str(x).strip()]

    keys: set[str] = set()
    for raw in raws:
        phrase = _normalize_phrase(raw)
        if not phrase:
            continue

        pieces = [phrase]
        pieces.extend(_TOKEN_RE.findall(phrase))
        pieces.extend(_collect_adjacent_phrases(phrase, max_tokens=24, max_size=2))

        for piece in pieces:
            key = _semantic_key(piece)
            if key:
                keys.add(key)
    return keys


def _build_product_name_semantic_keys(product_name: str) -> set[str]:
    keys: set[str] = set()
    phrase = _normalize_phrase(product_name)
    pieces = [phrase]
    pieces.extend(_TOKEN_RE.findall(phrase))
    pieces.extend(_collect_adjacent_phrases(phrase, max_tokens=24, max_size=3))

    for piece in pieces:
        key = _semantic_key(piece)
        if key:
            keys.add(key)
    return keys


def _matches_avoid_semantics(key: str, avoid_keys: set[str] | None) -> bool:
    if not key or not avoid_keys:
        return False

    for avoid in avoid_keys:
        if not avoid:
            continue
        if key == avoid or avoid in key or key in avoid:
            return True
    return False


def _matches_semantic_key_set(key: str, keys: set[str] | None) -> bool:
    if not key or not keys:
        return False

    for existing in keys:
        if not existing:
            continue
        if key == existing or existing in key or key in existing:
            return True
    return False


def _is_bad_naver_tag(text: str) -> bool:
    if _is_bad_phrase(text):
        return True

    compact = _compact_phrase(text)
    if any(bad in compact for bad in _NAVER_TAG_SELLER_BAN):
        return True

    key = _semantic_key(compact)
    category_keys = {_semantic_key(value) for value in _NAVER_TAG_CATEGORY_BAN}
    return key in category_keys


def _has_bad_numeric_shape(text: str) -> bool:
    compact = _compact_phrase(text)
    if not compact:
        return False
    if not re.search(r"\d", compact):
        return False
    if re.fullmatch(r"\d+", compact):
        return True
    if _PRICE_NUMERIC_RE.search(compact):
        return True
    if _SPEC_NUMERIC_RE.search(compact):
        return False
    if _BROKEN_NUMERIC_RE.fullmatch(compact):
        return True
    if re.fullmatch(r"[A-Za-z0-9]+", compact):
        return True
    return False


def _drop_contained_weaker_key(key: str, seen: set[str], out: list[str]) -> bool:
    if len(key) < 3:
        for existing in seen:
            if len(existing) >= 3 and key in existing:
                return False
        return True

    for existing in list(seen):
        if len(existing) < 3 and not re.fullmatch(r"[가-힣]{2,}", existing):
            continue
        if key == existing:
            return False
        if key in existing:
            return False
        if existing in key:
            seen.remove(existing)
            out[:] = [item for item in out if _semantic_key(item) != existing]
    return True


def _is_bad_phrase(text: str) -> bool:
    compact = _compact_phrase(text)
    if not compact or len(compact) < 2 or len(compact) > 20:
        return True
    if _has_disallowed_latin(compact):
        return True
    if re.fullmatch(r"\d+", compact):
        return True
    if _has_bad_numeric_shape(compact):
        return True
    if _BAD_END_RE.search(compact) or _BAD_JOSA_RE.search(compact):
        return True
    if any(bad in compact for bad in core.BAN | _EXTRA_BAN):
        return True
    return False


def _passes_topic(text: str, anchors: set[str], baseline: set[str]) -> bool:
    compact = _compact_phrase(text)
    if not compact:
        return False
    key = _semantic_key(compact)
    for ref in set(anchors or set()) | set(baseline or set()):
        ref_key = _semantic_key(str(ref))
        if not key or not ref_key:
            continue
        if key == ref_key:
            return True
        if len(key) >= 3 and len(ref_key) >= 3 and (key in ref_key or ref_key in key):
            return True
    if anchors and baseline:
        return core.is_on_topic(compact, anchors, baseline)
    if baseline:
        return core.is_consistent_with_baseline(compact, baseline)
    return True


def _generate_bucket_candidates_llm(
    product_name: str,
    source_text: str,
    model_name: str,
    naver_keyword_table: str,
) -> dict[str, list[str]]:
    if core.client is None or model_name == "없음":
        return _empty_bucket_map()

    source = _normalize_phrase(source_text)[:1800]
    naver = _normalize_phrase(naver_keyword_table)[:900]
    system_msg = (
        "당신은 GPT-4.x 수준 모델에서도 오해 없이 동작해야 하는 국내 이커머스 키워드 구조 분류기다. "
        "네이버 NLU 관점(브랜드/카테고리/속성/전환/일반 키워드)으로 먼저 판단한 뒤, "
        "쿠팡/네이버/롯데ON/ESM/11번가/Cafe24에 나눠 넣을 후보를 JSON 버킷으로 분류하라. "
        "추상화하거나 새 카테고리를 창작하지 말고, 입력에서 근거가 있고 실제 구매자가 검색할 가능성이 높은 표현만 선택하라. "
        "JSON만 반환하라. "
        "각 후보는 2~20자, 명사구 중심, 조사/문장형/광고문구/배송문구 금지, "
        "경쟁사 상표명/무관 인기어/숫자-only 토큰/영어 단어/로마자 단어/중문 표현 금지. "
        "단, 숫자 규격에 붙은 단위 표기는 상품 스펙일 때만 허용한다."
    )
    user_msg = f"""아래 JSON 스키마로만 반환하라:
{{
  "identity": string[],
  "usage_context": string[],
  "function": string[],
  "problem_solution": string[],
  "material_spec": string[],
  "audience_scene": string[],
  "synonyms": string[]
}}

작업 순서:
1. 상품명에서 핵심상품군 1~2개를 먼저 확정한다.
2. OCR/Vision에서 같은 상품군에 속하는 표현만 남긴다.
3. 네이버 NLU식으로 브랜드/카테고리/속성/전환/일반 키워드를 내부 분류한다.
4. 전환 키워드는 상품명 후보 우선순위를 높이고, 카테고리/속성으로 대체 가능한 키워드는 태그/속성 후보로 돌린다.
5. 아래 순서로 출력 버킷에 분류한다: 핵심상품군 -> 사용처 -> 기능 -> 문제해결 -> 재질/규격 -> 사용자문맥 -> 동의어
6. 색상/사이즈/규격 옵션은 material_spec에만 넣고 최대 2개까지만 허용한다.
7. 동의어는 최대 2개까지만 허용한다.
8. 감성어/홍보어/판매어는 모두 버린다.
9. 영어 단어, 로마자 단어, 중문 표현은 버리고 한국어 검색어만 남긴다.
10. 근거 있고 검색 가능성이 높은 후보가 충분하면 기존보다 약 5개 더 넓게 후보를 수집한다.

버킷별 규칙:
- identity: 제품 정체성, 제품유형, 상위/하위 카테고리, 2~6개
- usage_context: 사용 공간, 설치 위치, 사용 상황, 1~4개
- function: 기능, 동작, 장착/연결 방식, 1~4개
- problem_solution: 방지/차단/보호/정리 목적, 0~3개
- material_spec: 재질, 색상, 규격, 호환 힌트, 0~3개
- audience_scene: 사용자 유형, 현장 표현, 구매 문맥, 0~2개
- synonyms: 실무 유사어/띄어쓰기 변형만, 0~2개

절대 규칙:
- 귀여운, 예쁜, 고급진, 럭셔리, 힐링, 인싸, 필수품, 데일리, 추천, 인기 같은 감성/홍보어 금지
- 강아지 반려견 댕댕이 애견처럼 같은 뜻을 3개 이상 늘어놓지 말 것
- 자동차 차량 오토바이 바이크 퀵보드 자전거처럼 무관 확장을 하지 말 것
- 네이버 검색 데이터는 같은 카테고리 여부 확인과 우선순위 참고용으로만 사용하고, 새로운 카테고리는 도입하지 말 것
- intentional typo, 맞춤법 변형, 혼동 유도형 표기변형 생성 금지
- product_name과 source_text가 충돌하면 product_name + OCR 교집합을 우선한다
- 근거가 약한 항목은 빈 배열로 둔다

참고 구조:
- 네이버 상품명은 짧고 정확하게 핵심 전환 키워드와 상품군 중심으로 만든다.
- 쿠팡 상품명은 브랜드/핵심특징/실제상품명/메인키워드/세부키워드 구조를 참고하고, 검색태그는 정상 띄어쓰기를 유지한다.
- 롯데ON 상품명은 네이버보다 조금 넓고 Cafe24 공통보다 간결한 중간형으로 본다.
- ESM/11번가 상품명은 엑셀 검증을 위해 한글 45자 안팎으로 짧게 줄일 수 있어야 한다.
- Cafe24 공통 상품명은 ESM/11번가 fallback이고, 쿠팡 전용값이 비었을 때만 쿠팡 fallback이 되므로 대표검색어, 용도, 규격, 소재를 과하지 않게 포함한다.
- 여기서는 위 구조를 만들기 위한 후보만 분류한다

상품명: {product_name}

OCR_Vision요약: {source}

네이버검색데이터: {naver}"""

    try:
        resp = core.client.chat.completions.create(
            model=model_name,
            messages=[
                {"role": "system", "content": system_msg},
                {"role": "user", "content": user_msg},
            ],
            temperature=0.1,
            top_p=0.8,
            max_tokens=900,
            response_format={"type": "json_object"},
        )
        raw = (resp.choices[0].message.content or "").strip()
        data = json.loads(raw) if raw else {}
    except Exception:
        return _empty_bucket_map()

    out = _empty_bucket_map()
    for bucket in _BUCKET_ORDER:
        out[bucket] = _coerce_list(data.get(bucket))
    return out


def _generate_bucket_candidates_fallback(
    product_name: str,
    source_text: str,
    naver_keyword_table: str,
) -> dict[str, list[str]]:
    out = _empty_bucket_map()
    phrases: list[str] = []
    phrases.extend(_collect_adjacent_phrases(product_name, max_tokens=16, max_size=2))
    phrases.extend(_collect_adjacent_phrases(source_text, max_tokens=40, max_size=1))
    phrases.extend(_extract_naver_candidates(naver_keyword_table))

    for phrase in phrases:
        bucket = _guess_bucket(phrase)
        out[bucket].append(phrase)
    return out


def _collect_adjacent_phrases(text: str, max_tokens: int = 20, max_size: int = 3) -> list[str]:
    tokens = [
        tok
        for tok in _TOKEN_RE.findall(_normalize_phrase(text))
        if 2 <= len(tok) <= 12 and tok not in core.STOPWORDS and tok not in _EXTRA_BAN
    ]
    tokens = tokens[:max_tokens]
    out: list[str] = []
    seen: set[str] = set()

    def push(value: str) -> None:
        phrase = _normalize_phrase(value)
        key = phrase.lower()
        if not phrase or key in seen:
            return
        seen.add(key)
        out.append(phrase)

    for tok in tokens:
        push(tok)
    for size in range(2, max_size + 1):
        for i in range(len(tokens) - size + 1):
            push(" ".join(tokens[i : i + size]))
    return out


def _guess_bucket(text: str) -> str:
    compact = _compact_phrase(text)
    if any(hint in compact for hint in _IDENTITY_HINTS):
        return "identity"
    if any(hint in compact for hint in _USAGE_HINTS):
        return "usage_context"
    if any(hint in compact for hint in _PROBLEM_HINTS):
        return "problem_solution"
    if any(hint in compact for hint in _MATERIAL_HINTS):
        return "material_spec"
    if any(hint in compact for hint in _AUDIENCE_HINTS):
        return "audience_scene"
    if any(hint in compact for hint in _FUNCTION_HINTS):
        return "function"
    return "synonyms"


def _normalize_bucket_map(
    bucketed: dict[str, Iterable[str]],
    anchors: set[str],
    baseline: set[str],
    market: str = "A",
    avoid_keys: set[str] | None = None,
) -> dict[str, list[str]]:
    out = _empty_bucket_map()
    seen: set[str] = set()

    for bucket in _BUCKET_ORDER:
        for raw in bucketed.get(bucket, []):
            phrase = _strip_size_option_tokens(raw)
            if _is_size_option_phrase(raw):
                continue
            if _is_bad_phrase(phrase):
                continue
            if not _passes_topic(phrase, anchors=anchors, baseline=baseline):
                continue
            key = _semantic_key(phrase)
            if not key or key in seen:
                continue
            if market == "B" and bucket != "identity" and _matches_avoid_semantics(key, avoid_keys):
                continue
            seen.add(key)
            out[bucket].append(phrase)
            if len(out[bucket]) >= 10:
                break
    return out


def _flatten_bucket_map(bucketed: dict[str, list[str]]) -> list[str]:
    out: list[str] = []
    for bucket in _BUCKET_ORDER:
        out.extend(bucketed.get(bucket, []))
    return out


def _dedupe_market_items(items: Iterable[str], max_items: int) -> list[str]:
    out: list[str] = []
    seen: set[str] = set()
    for raw in items:
        phrase = _strip_size_option_tokens(raw)
        if not phrase or _is_bad_phrase(phrase):
            continue
        key = _semantic_key(phrase)
        if not key or key in seen:
            continue
        if not _drop_contained_weaker_key(key, seen, out):
            continue
        seen.add(key)
        out.append(phrase)
        if len(out) >= max_items:
            break
    return out


def _pick_market_items(
    bucketed: dict[str, list[str]],
    bucket_order: Iterable[str],
    max_items: int,
    fallback: Iterable[str] = (),
) -> list[str]:
    raw_items: list[str] = []
    for bucket in bucket_order:
        raw_items.extend(bucketed.get(bucket, []))
    raw_items.extend(fallback)
    return _dedupe_market_items(raw_items, max_items=max_items)


def _format_keyword_line(items: Iterable[str], *, separator: str, compact: bool = False) -> str:
    cleaned: list[str] = []
    seen: set[str] = set()
    for raw in items:
        phrase = _strip_size_option_tokens(raw)
        if not phrase:
            continue
        value = _compact_phrase(phrase) if compact else _normalize_phrase(phrase)
        key = value.lower()
        if key in seen:
            continue
        seen.add(key)
        cleaned.append(value)
    return separator.join(cleaned).strip()


def _build_market_keyword_variants(
    bucketed: dict[str, list[str]],
    candidate_pool: list[str],
    coupang_tags: list[str],
    naver_tags: list[str],
) -> dict[str, str]:
    cafe24_items = _pick_market_items(
        bucketed,
        ("identity", "function", "usage_context", "problem_solution", "material_spec", "audience_scene", "synonyms"),
        max_items=20,
        fallback=candidate_pool,
    )
    smartstore_items = _dedupe_market_items(naver_tags or cafe24_items, max_items=10)
    coupang_items = _dedupe_market_items(coupang_tags or cafe24_items, max_items=20)
    esm_items = _pick_market_items(
        bucketed,
        ("identity", "function", "synonyms", "usage_context"),
        max_items=10,
        fallback=cafe24_items,
    )
    eleven_items = _pick_market_items(
        bucketed,
        ("identity", "usage_context", "function", "synonyms", "problem_solution"),
        max_items=12,
        fallback=cafe24_items,
    )
    lotte_items = _pick_market_items(
        bucketed,
        ("identity", "function", "usage_context", "material_spec", "synonyms"),
        max_items=14,
        fallback=cafe24_items,
    )
    common_items = _dedupe_market_items(candidate_pool or cafe24_items, max_items=18)

    values = {
        "홈런_Cafe24검색어설정": _format_keyword_line(cafe24_items[:20], separator=",", compact=True),
        "홈런_Cafe24검색키워드": _format_keyword_line(cafe24_items[:18], separator=" ", compact=False),
        "홈런_스마트스토어태그": _format_keyword_line(smartstore_items[:10], separator="|", compact=False),
        "홈런_스마트스토어검색키워드": _format_keyword_line(smartstore_items[:10], separator=" ", compact=False),
        "홈런_쿠팡검색태그": _format_keyword_line(coupang_items[:20], separator=",", compact=False),
        "홈런_쿠팡검색키워드": _format_keyword_line(coupang_items[:18], separator=" ", compact=False),
        "홈런_ESM검색키워드": _format_keyword_line(esm_items[:10], separator=",", compact=True),
        "홈런_11번가검색키워드": _format_keyword_line(eleven_items[:12], separator=",", compact=True),
        "홈런_롯데ON검색키워드": _format_keyword_line(lotte_items[:14], separator=" ", compact=False),
        "홈런_공통마켓검색키워드": _format_keyword_line(common_items[:18], separator=",", compact=True),
    }
    return {column: values.get(column, "") for column in MARKET_KEYWORD_COLUMNS_10}


def _build_coupang_tags(
    bucketed: dict[str, list[str]],
    candidate_pool: list[str],
    product_name: str,
    source_text: str,
    anchors: set[str],
    baseline: set[str],
    market: str = "A",
    avoid_keys: set[str] | None = None,
    product_name_keys: set[str] | None = None,
) -> list[str]:
    if market == "B":
        # B마켓: 총 14개, 버킷순서 변경 (identity→function→usage→material→problem→audience→synonyms)
        plan = (
            ("identity", 4),
            ("function", 3),
            ("usage_context", 2),
            ("material_spec", 2),
            ("problem_solution", 1),
            ("audience_scene", 1),
            ("synonyms", 1),
        )
        max_tags = 14
    else:
        plan = (
            ("identity", 6),
            ("usage_context", 4),
            ("function", 4),
            ("problem_solution", 3),
            ("material_spec", 2),
            ("audience_scene", 1),
            ("synonyms", 2),
        )
        max_tags = 20
    out: list[str] = []
    seen: set[str] = set()
    deferred: list[str] = []
    deferred_seen: set[str] = set()

    def defer(value: str, key: str) -> None:
        if key and key not in deferred_seen:
            deferred_seen.add(key)
            deferred.append(value)

    def push(value: str, *, allow_product_name_overlap: bool = False) -> bool:
        phrase = _strip_size_option_tokens(value)
        if _is_bad_phrase(phrase):
            return False
        if not _passes_topic(phrase, anchors=anchors, baseline=baseline):
            return False
        key = _semantic_key(phrase)
        if not key or key in seen:
            return False
        if not allow_product_name_overlap and _matches_semantic_key_set(key, product_name_keys):
            defer(value, key)
            return False
        if not _drop_contained_weaker_key(key, seen, out):
            return False
        seen.add(key)
        out.append(phrase)
        return True

    for bucket, quota in plan:
        added = 0
        for value in bucketed.get(bucket, []):
            if market == "B" and bucket != "identity" and _matches_avoid_semantics(_semantic_key(value), avoid_keys):
                continue
            if push(value):
                added += 1
            if added >= quota or len(out) >= max_tags:
                break
        if len(out) >= max_tags:
            return out[:max_tags]

    for value in candidate_pool:
        if market == "B" and _matches_avoid_semantics(_semantic_key(value), avoid_keys):
            continue
        push(value)
        if len(out) >= max_tags:
            return out[:max_tags]

    for value in deferred:
        push(value, allow_product_name_overlap=True)
        if len(out) >= max_tags:
            return out[:max_tags]

    for value in _collect_adjacent_phrases(product_name, max_tokens=16, max_size=2):
        if market == "B" and _matches_avoid_semantics(_semantic_key(value), avoid_keys):
            continue
        push(value, allow_product_name_overlap=True)
        if len(out) >= max_tags:
            break
    return out[:max_tags]


def _build_naver_tags(
    bucketed: dict[str, list[str]],
    candidate_pool: list[str],
    product_name: str,
    source_text: str,
    anchors: set[str],
    baseline: set[str],
    market: str = "A",
    avoid_keys: set[str] | None = None,
    product_name_keys: set[str] | None = None,
) -> list[str]:
    if market == "B":
        # B마켓: 총 7개
        plan = (
            ("identity", 2),
            ("function", 2),
            ("usage_context", 1),
            ("material_spec", 1),
            ("synonyms", 1),
        )
        max_tags = 7
    else:
        plan = (
            ("identity", 4),
            ("usage_context", 2),
            ("function", 2),
            ("problem_solution", 1),
            ("material_spec", 1),
            ("audience_scene", 1),
            ("synonyms", 1),
        )
        max_tags = 10
    out: list[str] = []
    seen: set[str] = set()
    deferred: list[str] = []
    deferred_seen: set[str] = set()
    char_budget = 100

    def defer(value: str, key: str) -> None:
        if key and key not in deferred_seen:
            deferred_seen.add(key)
            deferred.append(value)

    def push(value: str, *, allow_product_name_overlap: bool = False) -> bool:
        phrase = _strip_size_option_tokens(value)
        if _is_bad_naver_tag(phrase):
            return False
        if not _passes_topic(phrase, anchors=anchors, baseline=baseline):
            return False
        key = _semantic_key(phrase)
        if not key or key in seen:
            return False
        if not allow_product_name_overlap and _matches_semantic_key_set(key, product_name_keys):
            defer(value, key)
            return False
        prev_seen = set(seen)
        prev_out = list(out)
        if not _drop_contained_weaker_key(key, seen, out):
            return False
        projected = len("|".join(out + [phrase]))
        if projected > char_budget:
            seen.clear()
            seen.update(prev_seen)
            out[:] = prev_out
            return False
        seen.add(key)
        out.append(phrase)
        return True

    for bucket, quota in plan:
        added = 0
        for value in bucketed.get(bucket, []):
            if market == "B" and bucket != "identity" and _matches_avoid_semantics(_semantic_key(value), avoid_keys):
                continue
            if push(value):
                added += 1
            if added >= quota or len(out) >= max_tags:
                break
        if len(out) >= max_tags:
            return out[:max_tags]

    for value in candidate_pool:
        if market == "B" and _matches_avoid_semantics(_semantic_key(value), avoid_keys):
            continue
        push(value)
        if len(out) >= max_tags:
            return out[:max_tags]

    for value in deferred:
        push(value, allow_product_name_overlap=True)
        if len(out) >= max_tags:
            return out[:max_tags]

    for value in _collect_adjacent_phrases(product_name, max_tokens=16, max_size=2):
        if market == "B" and _matches_avoid_semantics(_semantic_key(value), avoid_keys):
            continue
        push(value, allow_product_name_overlap=True)
        if len(out) >= max_tags:
            break
    return out[:max_tags]


def _extract_naver_candidates(naver_keyword_table: str) -> list[str]:
    rows: list[tuple[str, int]] = []
    text = str(naver_keyword_table or "").strip()
    if not text:
        return []

    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("키워드|"):
            continue
        parts = [p.strip() for p in line.split("|")]
        if len(parts) >= 4:
            keyword = parts[0]
            try:
                total = int(parts[3])
            except Exception:
                total = 0
            if keyword:
                rows.append((keyword, total))

    if not rows:
        for label in ("PC5", "MO5"):
            match = re.search(rf"{label}=([^|]+)", text)
            if not match:
                continue
            for keyword in match.group(1).split(","):
                keyword = keyword.strip()
                if keyword:
                    rows.append((keyword, 0))

    rows.sort(key=lambda item: item[1], reverse=True)
    return [kw for kw, _ in rows]
