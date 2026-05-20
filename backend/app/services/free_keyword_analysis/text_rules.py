from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Iterable


NOISE_WORDS = {
    "홈런마켓",
    "급배송",
    "무료배송",
    "당일발송",
    "택배",
    "배송",
    "교환",
    "반품",
    "문의",
    "상담",
    "전화",
    "문자",
    "주의사항",
    "참고사항",
    "판매자",
    "상점",
    "평일",
    "발송",
    "택배사",
    "시리즈",
    "용품",
}

NOISE_PHRASES = (
    "평일 2시",
    "당일 발송",
    "택배사 사정",
    "무료 배송",
    "무료배송",
    "교환 반품",
    "교환반품",
    "대량구매 문의",
    "문자 상담",
    "홈런마켓",
)

PRODUCT_SIGNAL_WORDS = (
    "소재",
    "옵션",
    "사이즈",
    "수량",
    "사용법",
    "제작",
    "가이드",
    "특징",
    "장점",
    "구성",
    "색상",
    "재질",
    "규격",
    "슈링클",
    "열수축",
)

OCR_FIXES = (
    (re.compile(r"슈링클\s*(?:44|\^4|4A|A\s*4)\s*종\s*이", re.IGNORECASE), "슈링클 A4 종이"),
    (re.compile(r"\b44\s*종\s*이\b", re.IGNORECASE), "A4 종이"),
    (re.compile(r"\bA\s*4\b", re.IGNORECASE), "A4"),
    (re.compile(r"옴션|옵션선택|중선택"), "옵션"),
    (re.compile(r"반투명\s*8\s*투명|반투명\s*6\s*투명"), "반투명 투명"),
    (re.compile(r"열수축\s*특수\s*필름"), "열수축 특수 필름"),
    (re.compile(r"핫둘|힛둘|핫툴"), "힛툴"),
)


@dataclass
class ProductFields:
    gs_code: str = ""
    product_identity: str = ""
    representative_terms: list[str] = field(default_factory=list)
    aliases: list[str] = field(default_factory=list)
    specs: list[str] = field(default_factory=list)
    options: list[str] = field(default_factory=list)
    material: list[str] = field(default_factory=list)
    features: list[str] = field(default_factory=list)
    use_cases: list[str] = field(default_factory=list)
    target_users: list[str] = field(default_factory=list)
    tools: list[str] = field(default_factory=list)
    exclusions: list[str] = field(default_factory=list)
    ocr_clean_text: str = ""
    image_summary: str = ""
    keyword_source: str = ""
    confidence: int = 0


def clean_ocr_text(text: str) -> str:
    text = str(text or "")
    for pattern, repl in OCR_FIXES:
        text = pattern.sub(repl, text)
    text = re.sub(r"[^0-9A-Za-z가-힣×xX/.,()\-\s]", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text


def is_noise_only_text(text: str) -> bool:
    text = clean_ocr_text(text)
    if not text:
        return True
    phrase_hits = sum(1 for phrase in NOISE_PHRASES if phrase in text)
    word_hits = sum(1 for word in NOISE_WORDS if word in text)
    has_product_signal = any(word in text for word in PRODUCT_SIGNAL_WORDS)
    has_phone = bool(re.search(r"01[016789]\s*\d{3,4}\s*\d{4}", text))
    if has_product_signal:
        return False
    if phrase_hits >= 1 and (word_hits >= 2 or has_phone):
        return True
    if phrase_hits >= 2:
        return True
    return False


def unique(items: Iterable[str]) -> list[str]:
    out: list[str] = []
    seen: set[str] = set()
    for item in items:
        token = re.sub(r"\s+", " ", str(item or "")).strip()
        if not token:
            continue
        key = token.lower().replace(" ", "")
        if key in seen:
            continue
        seen.add(key)
        out.append(token)
    return out


def split_terms(value: str) -> list[str]:
    if not value:
        return []
    parts = re.split(r"[,;|/]+|\s{2,}", str(value))
    return unique(parts)


def _has_any(text: str, words: Iterable[str]) -> bool:
    return any(w in text for w in words)


def infer_product_fields(
    gs_code: str,
    ocr_text: str,
    detail_count: int,
    listing_count: int,
) -> ProductFields:
    text = clean_ocr_text(ocr_text)
    fields = ProductFields(gs_code=gs_code, ocr_clean_text=text)

    if _has_any(text, ["슈링클", "열수축", "수축필름"]):
        fields.product_identity = "슈링클 A4 종이" if "A4" in text else "슈링클 종이"
        fields.representative_terms = ["슈링클 A4 종이", "슈링클지", "열수축필름", "수축필름"]
        fields.aliases = ["슈링클지", "열수축필름", "수축필름", "공예필름"]
        fields.material = ["플라스틱 열수축 필름", "열수축 특수 필름"]

        if "A4" in text:
            fields.specs.append("A4")
        if re.search(r"20\s*[xX×]\s*29\s*cm", text, flags=re.IGNORECASE):
            fields.specs.append("20x29cm")
        if "1장" in text or "낱장" in text:
            fields.specs.append("1장 낱장")

        if "반투명" in text:
            fields.options.append("반투명")
        if "투명" in text:
            fields.options.append("투명")

        if _has_any(text, ["수축", "작아", "작게"]):
            fields.features.extend(["열을 가하면 수축", "작아짐"])
        if _has_any(text, ["단단", "두껍"]):
            fields.features.extend(["단단해짐", "두꺼워짐"])
        if _has_any(text, ["펀치", "펀칭", "구멍"]):
            fields.features.append("펀칭 가능")
        if _has_any(text, ["채색", "밑그림", "그림"]):
            fields.features.append("그림 채색 가능")
        if _has_any(text, ["오리", "가위"]):
            fields.features.append("오리기 가능")

        if _has_any(text, ["키링"]):
            fields.use_cases.append("키링 만들기")
        if _has_any(text, ["네임택"]):
            fields.use_cases.append("네임택 제작")
        if _has_any(text, ["굿즈"]):
            fields.use_cases.append("굿즈 제작")
        fields.use_cases.extend(["DIY 공예", "핸드메이드 소품", "어린이 미술", "공예 수업"])
        fields.target_users.extend(["어린이", "학생", "공예 취미", "핸드메이드 제작자"])

        if "오븐" in text:
            fields.tools.append("오븐")
        if _has_any(text, ["힛툴", "열풍"]):
            fields.tools.append("열풍기")
        if "가위" in text:
            fields.tools.append("가위")
        if _has_any(text, ["펀치", "펀칭"]):
            fields.tools.append("펀치")
        if "유성매직" in text:
            fields.tools.append("유성매직")
        if "유성펜" in text:
            fields.tools.append("유성펜")
        if "색연필" in text:
            fields.tools.append("색연필")
        if "마카" in text:
            fields.tools.append("마카")
    else:
        tokens = _fallback_identity_terms(text)
        fields.product_identity = " ".join(tokens[:3])
        fields.representative_terms = tokens[:4]
        fields.aliases = tokens[4:8]

    fields.exclusions = [word for word in NOISE_WORDS if word in text]
    if detail_count:
        fields.image_summary = f"상세이미지 {detail_count}장 OCR 기반"
    if listing_count:
        suffix = f"대표/추가 후보 {listing_count}장 존재"
        fields.image_summary = f"{fields.image_summary}; {suffix}" if fields.image_summary else suffix

    fields.representative_terms = unique(fields.representative_terms)
    fields.aliases = unique(fields.aliases)
    fields.specs = unique(fields.specs)
    fields.options = unique(fields.options)
    fields.material = unique(fields.material)
    fields.features = unique(fields.features)
    fields.use_cases = unique(fields.use_cases)
    fields.target_users = unique(fields.target_users)
    fields.tools = unique(fields.tools)
    fields.exclusions = unique(fields.exclusions)
    fields.keyword_source = make_keyword_source(fields)
    fields.confidence = _confidence(fields, detail_count, listing_count)
    return fields


def _fallback_identity_terms(text: str) -> list[str]:
    words = re.findall(r"[0-9A-Za-z가-힣]+", text)
    out = []
    for word in words:
        if len(word) < 2 or word in NOISE_WORDS:
            continue
        if any(noise in word for noise in NOISE_WORDS):
            continue
        if re.search(r"[A-Za-z]", word):
            continue
        if re.fullmatch(r"\d+", word):
            continue
        out.append(word)
    return unique(out)


def _confidence(fields: ProductFields, detail_count: int, listing_count: int) -> int:
    score = 20
    if fields.product_identity:
        score += 25
    if fields.aliases:
        score += 10
    if fields.features:
        score += 15
    if fields.use_cases:
        score += 15
    if detail_count >= 2:
        score += 10
    if listing_count:
        score += 5
    return min(100, score)


def make_keyword_source(fields: ProductFields) -> str:
    items = (
        [fields.product_identity]
        + fields.representative_terms
        + fields.aliases
        + fields.specs
        + fields.options
        + fields.material
        + fields.features
        + fields.use_cases
        + fields.target_users
        + fields.tools
    )
    return " ".join(unique(items))


def generate_keyword_variants(fields: ProductFields, count: int = 5) -> list[str]:
    if "슈링클" in fields.product_identity or any("슈링클" in x for x in fields.representative_terms):
        variants = [
            [
                fields.product_identity or "슈링클 A4 종이",
                "슈링클지",
                "열수축필름",
                "수축필름",
                "공예필름",
                *fields.options,
                "공예재료",
                "키링",
                "네임택",
                "굿즈 만들기",
            ],
            [
                "슈링클지",
                "A4",
                "열수축",
                "공예필름",
                *fields.options,
                "낱장 재료",
                "오븐가열",
                "펀칭",
                "채색",
                "DIY",
                "키링 제작",
            ],
            [
                "열수축필름",
                "슈링클 종이",
                "A4",
                "수축 플라스틱 필름",
                "그림그리기",
                "오리기",
                "굿즈",
                "네임택 만들기",
            ],
            [
                "슈링클 공예 종이",
                *fields.options,
                "A4 필름",
                "핸드메이드 소품",
                "캐릭터 키링",
                "어린이 미술",
                "공예 수업 재료",
            ],
            [
                "수축필름",
                "슈링클지",
                "A4",
                "DIY 공예재료",
                "오븐",
                "열풍기",
                "가열",
                "펀칭 가능",
                "학교 미술 수업",
                "키링 만들기",
            ],
        ]
        return [_join_keyword_line(v) for v in variants[:count]]

    core = fields.representative_terms or split_terms(fields.product_identity)
    middle = fields.aliases + fields.specs + fields.options + fields.material + fields.features
    back = fields.use_cases + fields.target_users + fields.tools
    variants = [
        core + fields.aliases + middle[:4] + back[:3],
        core[:2] + middle + back[:3],
        core[:2] + fields.specs + fields.features + fields.use_cases,
        core + fields.options + fields.use_cases + fields.target_users,
        fields.aliases + core + fields.tools + fields.features + fields.use_cases,
    ]
    return [_join_keyword_line(v) for v in variants[:count] if _join_keyword_line(v)]


def _join_keyword_line(items: Iterable[str]) -> str:
    cleaned = []
    for item in unique(items):
        if not item:
            continue
        if item in NOISE_WORDS:
            continue
        latin_remainder = re.sub(r"A\s*4", "", item, flags=re.IGNORECASE)
        if re.search(r"[A-Za-z]", latin_remainder):
            continue
        cleaned.append(item)
    return " ".join(cleaned).strip()
