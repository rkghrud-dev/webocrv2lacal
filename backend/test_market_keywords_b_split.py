from app.services import legacy_core as core
from app.services.market_keywords import (
    MARKET_KEYWORD_COLUMNS_10,
    _build_avoid_semantic_keys,
    _dedupe_market_items,
    _normalize_bucket_map,
    generate_market_keyword_packages,
)


def test_normalize_bucket_map_keeps_identity_but_drops_a_title_terms() -> None:
    bucketed = {
        "identity": ["가구 연결 볼트", "캠록"],
        "usage_context": ["옷장", "선반"],
        "function": ["결합", "잠금"],
        "problem_solution": ["흔들림방지"],
        "material_spec": ["니켈도금"],
        "audience_scene": ["조립가구"],
        "synonyms": ["가구부속"],
    }
    avoid = _build_avoid_semantic_keys("가구 연결 볼트 니켈도금 선반 조립가구 가구부속")

    normalized = _normalize_bucket_map(
        bucketed,
        anchors=set(),
        baseline=set(),
        market="B",
        avoid_keys=avoid,
    )

    assert "가구 연결 볼트" in normalized["identity"], normalized
    assert "캠록" in normalized["identity"], normalized
    assert "결합" in normalized["function"], normalized
    assert "잠금" in normalized["function"], normalized
    assert "선반" not in normalized["usage_context"], normalized
    assert "니켈도금" not in normalized["material_spec"], normalized
    assert "조립가구" not in normalized["audience_scene"], normalized
    assert "가구부속" not in normalized["synonyms"], normalized


def test_generate_market_keyword_packages_avoids_a_title_terms_for_b_market() -> None:
    pkg = generate_market_keyword_packages(
        product_name="가구 연결 볼트 캠록 결합 잠금 옷장 선반 가구부속",
        source_text="가구 연결 볼트 캠록 결합 잠금 옷장 선반 가구부속 니켈도금 커넥터",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="B",
        avoid_terms="옷장 선반 가구부속 니켈도금",
    )

    for blocked in ("옷장", "선반", "가구부속", "니켈도금"):
        assert blocked not in pkg.search_keywords, pkg.search_keywords
    assert any(
        token in pkg.search_keywords
        for token in ("가구연결볼트", "연결볼트", "캠록", "가구연결")
    ), pkg.search_keywords


def test_generate_market_keyword_packages_builds_topic_filter_from_product_name_when_empty() -> None:
    pkg = generate_market_keyword_packages(
        product_name="트럭 D링 적재함 고정고리",
        source_text="트럭 D링 적재함 고정고리 화물 결속 각도조절 외부조명 볼트고리 나사고리",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    joined = " ".join(pkg.candidate_pool) + " " + pkg.search_keywords
    assert any(token in joined for token in ("트럭", "D링", "적재함", "고정고리")), joined
    for blocked in ("각도조절", "외부조명", "볼트고리", "나사고리"):
        assert blocked not in joined, joined


def test_generate_market_keyword_packages_does_not_invent_typos_for_synonyms() -> None:
    pkg = generate_market_keyword_packages(
        product_name="후크택 고정클립",
        source_text="후크택 고정클립 연결 정리",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    joined = " ".join(pkg.candidate_pool) + " " + pkg.search_keywords
    assert "후크택" in joined, joined
    assert "후크텍" not in joined, joined


def test_generate_keyword_stage2_keeps_seed_when_reference_is_weak() -> None:
    seed = "트럭 D링 적재함 고정고리 결속"
    out, tokens = core.generate_keyword_stage2(
        seed_keywords=seed,
        naver_keyword_table="",
        ocr_text="트럭 D링 적재함 화물 결속 볼트체결",
        model_name="없음",
        max_len=120,
        max_words=12,
    )

    assert out, tokens
    assert core.keyword_local_score(out, base_name="트럭 D링 적재함", anchors=None, baseline=None) >= core.keyword_local_score(
        seed,
        base_name="트럭 D링 적재함",
        anchors=None,
        baseline=None,
    )
    for blocked in ("304", "다용도", "간편", "강력", "휴대용"):
        assert blocked not in out, out


def test_generate_market_keyword_packages_filters_ocr_numeric_noise_and_size_but_keeps_thread_specs() -> None:
    pkg = generate_market_keyword_packages(
        product_name="가구 연결 볼트 35mm M8 캠록",
        source_text="가구 연결 볼트 35mm M8 캠록 801 2024 19900원 2O2 바코드",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    joined = " ".join(pkg.candidate_pool + pkg.coupang_tags + [pkg.search_keywords])
    assert "35mm" not in joined, joined
    assert "M8" in joined, joined
    for blocked in ("801", "2024", "19900원", "2O2"):
        assert blocked not in joined, joined


def test_generate_market_keyword_packages_prefers_specific_compounds_over_subterms() -> None:
    pkg = generate_market_keyword_packages(
        product_name="가구 연결 볼트 캠록",
        source_text="가구 연결 볼트 캠록 결합 잠금 옷장 선반",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    tags = pkg.coupang_tags
    assert tags, pkg
    assert not (
        "볼트" in tags
        and any(tag != "볼트" and "볼트" in tag for tag in tags)
    ), tags


def test_generate_market_keyword_packages_builds_ten_market_keyword_sets_without_size_noise() -> None:
    pkg = generate_market_keyword_packages(
        product_name="신발 밑창테이프 EVA 보강 패드 미끄럼방지 셀프수선 1M 2mm",
        source_text="신발 밑창테이프 EVA 보강 패드 미끄럼방지 셀프수선 1M 2mm 접착 테이프",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    assert tuple(pkg.market_keywords.keys()) == MARKET_KEYWORD_COLUMNS_10
    assert all(pkg.market_keywords[col] for col in MARKET_KEYWORD_COLUMNS_10), pkg.market_keywords
    joined = " ".join(pkg.market_keywords.values())
    assert "EVA" in joined, joined
    assert "1M" not in joined.upper(), joined
    assert "2mm" not in joined.lower(), joined


def test_generate_market_keyword_packages_drops_color_option_keywords() -> None:
    pkg = generate_market_keyword_packages(
        product_name="손목 폰 가방 휴대폰 파우치 퍼플 그레이",
        source_text="손목 폰 가방 휴대폰 파우치 손목 스트랩 미니 가방 외출용 퍼플 그레이 블랙",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    joined = " ".join(pkg.coupang_tags + pkg.naver_tags + list(pkg.market_keywords.values()))
    for blocked in ("퍼플", "그레이", "블랙"):
        assert blocked not in joined, joined


def test_other_market_keyword_sets_are_space_separated_and_richer() -> None:
    pkg = generate_market_keyword_packages(
        product_name="브레이크 니플 브레이크 라인 연결 너트 유니온 세트",
        source_text="브레이크 니플 브레이크 라인 연결 너트 유니온 세트 배관 연결 황동 누유 방지 정밀 나사산 자동차 정비 오일 라인 연료 배관 부품",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    for col in ("홈런_ESM검색키워드", "홈런_11번가검색키워드", "홈런_롯데ON검색키워드"):
        value = pkg.market_keywords[col]
        assert value, pkg.market_keywords
        assert "," not in value, value
        assert len(value.split()) >= 5, value


def test_dedupe_market_items_splits_repeated_compound_heads_and_keeps_spacing() -> None:
    items = _dedupe_market_items(
        [
            "차량조명브라켓",
            "무타공브라켓",
            "조명마운트",
            "보조등브라켓",
            "스틸브라켓",
        ],
        max_items=10,
    )

    assert items == ["차량 조명 브라켓", "무타공", "마운트", "보조등", "스틸"], items


def test_generate_market_keyword_packages_promotes_ocr_identity_when_product_name_is_generic() -> None:
    pkg = generate_market_keyword_packages(
        product_name="상품 이미지 OCR",
        source_text="차량조명브라켓 무타공브라켓 조명마운트 보조등브라켓 스틸브라켓 볼트 고정 각도 조절",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    assert "차량 조명 브라켓" in pkg.search_keywords, pkg.search_keywords
    assert "무타공" in pkg.search_keywords, pkg.search_keywords
    assert "보조등" in pkg.search_keywords, pkg.search_keywords


def test_generate_market_keyword_packages_removes_weak_standalone_noise() -> None:
    pkg = generate_market_keyword_packages(
        product_name="신발 밑창 테이프 EVA 보강 패드 미끄럼방지",
        source_text="신발 밑창테이프 EVA 보강 패드 미끄럼방지 셀프수선 접착 테이프 구두 운동화 밑창 보호 마모 방지 DIY 수선",
        model_name="없음",
        anchors=set(),
        baseline=set(),
        market="A",
    )

    joined = " ".join(pkg.coupang_tags + pkg.naver_tags + list(pkg.market_keywords.values()))
    assert "마모 방지" in joined, joined
    assert "보호 마모" not in joined, joined
    smartstore = pkg.market_keywords.get("홈런_스마트스토어태그", "")
    for weak in ("보호", "방지", "수선", "조절"):
        assert weak not in {part.strip() for part in smartstore.split("|")}, smartstore
