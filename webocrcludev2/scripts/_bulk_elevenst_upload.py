# -*- coding: utf-8 -*-
"""시드 전체에서 A:11번가 미업로드 상품 일괄 등록 (1회성 배치)."""
import importlib.util, io, sys, json, time, uuid
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("las", HERE / "local_api_server.py")
las = importlib.util.module_from_spec(spec)
sys.modules["las"] = las
spec.loader.exec_module(las)

LOG = open(HERE / "_bulk_elevenst_upload.log", "w", encoding="utf-8")

def log(msg):
    line = f"[{las.now_text()}] {msg}"
    print(line)
    LOG.write(line + "\n")
    LOG.flush()

# 업로드 대상 수집
uploaded = set()
for h in las.collect_upload_history(limit_jobs=200):
    if h.get("channelKey") == "A:11번가" and h.get("status") == "uploaded":
        uploaded.add(h.get("gs", "").upper())

seeds = sorted((HERE.parent / "data" / "seeds").glob("*.webseed.json"), key=lambda p: p.stat().st_mtime, reverse=True)
products = {}
for sp in seeds:
    try:
        seed = json.load(open(sp, encoding="utf-8"))
    except Exception:
        continue
    for p in seed.get("products", []):
        gs = str(p.get("gs", "")).upper()
        mk = (p.get("marketKeywords") or {}).get("A:11번가") or {}
        if gs and mk.get("title") and gs not in products:
            products[gs] = p

pending = [(gs, p) for gs, p in sorted(products.items()) if gs not in uploaded]
log(f"대상 {len(pending)}건 시작")

settings = las.read_market_key_settings().get("items", {})
cafe24_path = las.resolve_cafe24_key_path_for_account(settings, "A")
elevenst_item = settings.get(las.market_key_id("A", "11번가"))
elevenst_key_path = None
if isinstance(elevenst_item, dict):
    try:
        elevenst_key_path = las.resolve_market_key_item_path(elevenst_item)
    except Exception:
        pass

results = []
ok_count = fail_count = 0
for index, (gs, p) in enumerate(pending, 1):
    mk = (p.get("marketKeywords") or {}).get("A:11번가") or {}
    imgs = p.get("images") or {}
    entry = {
        "account": "A", "market": "11번가", "channel": "A:11번가",
        "channelKey": "A:11번가", "queueKey": f"A:11번가:{gs}",
        "gs": gs, "baseGs": str(p.get("baseGs", "")),
        "sourceName": p.get("sourceName", ""),
        "title": mk.get("title", ""),
        "searchTerms": mk.get("searchTerms", ""),
        "tags": mk.get("tags") or [],
        "mainImageSrc": imgs.get("representative") or imgs.get("sourceThumb") or "",
        "additionalImageSrcs": imgs.get("additional") or [],
        "detailImageSrcs": imgs.get("detail") or [],
        "detailHtml": p.get("detailHtml", ""),
        "price": p.get("price"), "salePrice": p.get("salePrice"),
        "consumerPrice": p.get("consumerPrice"), "supplyPrice": p.get("supplyPrice"),
        "optionSummary": p.get("optionSummary", ""), "optionInput": p.get("optionInput", ""),
        "optionAdditionalAmounts": p.get("optionAdditionalAmounts") or [],
        "optionItems": p.get("optionItems") or [],
        "naverProvidedNotice": p.get("naverProvidedNotice") or {},
        "categories": p.get("categories") or {},
    }
    if elevenst_key_path is not None:
        entry["_elevenstApiKeyPath"] = str(elevenst_key_path)
    # 이미지 우선순위: 네이버 등록 이미지 → 쿠팡 등록 이미지 → Cafe24 기준상품
    shared = []
    source = ""
    try:
        shared = las.get_naver_uploaded_images(gs, "A")
        source = "네이버"
        if not shared:
            shared = las.get_coupang_uploaded_images(gs, "A")
            source = "쿠팡"
        if not shared and cafe24_path:
            shared = las.get_cafe24_listing_images(cafe24_path, gs, entry["baseGs"])
            source = "Cafe24"
    except Exception:
        shared = []
    if shared:
        entry["_cafe24ListingImages"] = shared
        result = las.upload_elevenst_product(entry)
        if result.get("status") == "uploaded":
            result["imageSource"] = source
    else:
        result = {**entry, "status": "failed", "updatedAt": las.now_text(),
                  "error": "네이버/쿠팡/Cafe24 어디에도 검증된 이미지 없음 — 등록 건너뜀", "productId": ""}
    status = result.get("status")
    if status == "uploaded":
        ok_count += 1
        log(f"[{index}/{len(pending)}] {gs} OK -> {result.get('productId')}")
    else:
        fail_count += 1
        log(f"[{index}/{len(pending)}] {gs} FAIL: {str(result.get('error', ''))[:110]}")
    results.append({k: v for k, v in result.items() if not str(k).startswith("_")})
    time.sleep(0.35)

# 이력에 반영되도록 job 파일 기록
job_id = uuid.uuid4().hex[:12]
job = {
    "jobId": job_id,
    "action": "marketUpload",
    "status": "completed",
    "createdAt": las.now_text(),
    "finishedAt": las.now_text(),
    "note": "bulk 11st seed upload",
    "results": results,
}
las.write_json(las.JOBS_ROOT / f"{job_id}.json", job)
log(f"완료: 성공 {ok_count} / 실패 {fail_count} / 총 {len(pending)} | job={job_id}")
LOG.close()
