from app.services.free_keyword_analysis.text_rules import (
    clean_ocr_text,
    generate_keyword_variants,
    infer_product_fields,
    is_noise_only_text,
)


def test_shrink_plastic_keyword_source_is_rich_and_ordered():
    raw = (
        "슈링클 44 종이 소재 플라스틱 열수축 필름 수량 1장 "
        "옵션 반투명 투명 사이즈 44 20x29cm "
        "열을 가하면 수축 단단해짐 키링 네임택 굿즈 오븐 펀칭 채색 색연필 마카"
    )

    fields = infer_product_fields("GS0101306A", raw, detail_count=10, listing_count=5)
    variants = generate_keyword_variants(fields)

    assert fields.product_identity == "슈링클 A4 종이"
    assert "열수축필름" in fields.keyword_source
    assert "키링 만들기" in fields.keyword_source
    assert len(variants) == 5
    assert variants[0].startswith("슈링클 A4 종이 슈링클지 열수축필름")
    assert "배송" not in variants[0]


def test_clean_ocr_corrects_a4_common_error():
    assert "슈링클 A4 종이" in clean_ocr_text("슈링클 44 종이")


def test_common_shipping_image_is_excluded_as_noise():
    text = "홈런마켓 급배송 평일 2시 이전 주문 확인시 당일 발송 택배사 사정 교환 반품 문의"
    assert is_noise_only_text(text)
