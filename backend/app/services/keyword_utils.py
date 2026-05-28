"""keyword_builder / market_keywords 공통 유틸리티.

두 모듈에서 동일하게 사용되는 정규식·필터 함수를 한 곳에서 관리한다.
"""
from __future__ import annotations

import re

SPEC_NUMERIC_RE = re.compile(
    r"("
    r"m\d+|"
    r"\d+(/\d+)?(mm|cm|m|ml|l|v|w|a|kg|g|호|인치|평|구|단|매|개|입|p|pcs|ea)|"
    r"\d+(인용|인분|자루|박스)|"
    r"\d+[xX]\d+"
    r")",
    re.IGNORECASE,
)

ALLOWED_LATIN_TOKEN_RE = re.compile(
    r"^(?:abs|eva|pvc|pa\d+|pe|pp|pet|diy|m\d+)$", re.IGNORECASE
)


def has_disallowed_latin(text: str) -> bool:
    """텍스트에 허용되지 않는 라틴 문자가 포함되어 있는지 검사.

    규격 숫자(35mm, 12V 등)와 허용 약어(PVC, ABS, DIY 등)는 통과시킨다.
    """
    compact = re.sub(r"[^0-9A-Za-z가-힣]", "", str(text or ""))
    if not re.search(r"[A-Za-z]", compact):
        return False
    without_specs = SPEC_NUMERIC_RE.sub("", compact)
    for token in re.findall(r"[A-Za-z]+\d*", without_specs):
        if not ALLOWED_LATIN_TOKEN_RE.fullmatch(token):
            return True
    return False
