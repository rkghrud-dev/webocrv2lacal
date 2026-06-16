#!/usr/bin/env python3
"""A:네이버(스마트스토어) 전체 상품을 백업한다 (읽기전용).

상품 삭제 전 반드시 실행 — 상품명/코드/가격/카테고리 등을 파일로 보존해 재업로드/감사 근거로 남긴다.
네이버 커머스 API 상품 권한만으로 동작 (주문 권한 불필요).

키: Desktop/key/naver_client_key.txt (NAVER_COMMERCE_CLIENT_ID / _SECRET)
산출물(바탕화면):
  - naver_A_products_backup_<날짜>.json  (원본 contents 전체)
  - naver_A_products_backup_<날짜>.csv   (요약: productNo/GS/상품명/상태/가격/등록일)
"""
from __future__ import annotations
import base64, csv, json, sys, time
import datetime as dt
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
import requests, bcrypt

KEY = Path.home() / "Desktop" / "key" / "naver_client_key.txt"
BASE = "https://api.commerce.naver.com/external"
DESKTOP = Path.home() / "Desktop"


def read_kv(path: Path) -> dict[str, str]:
    kv = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        line = line.strip()
        if "=" in line and not line.startswith("#"):
            k, v = line.split("=", 1)
            kv[k.strip()] = v.strip()
    return kv


def get_token(cid: str, cs: str) -> str:
    ts = int(time.time() * 1000) - 3000
    sign = base64.b64encode(bcrypt.hashpw(f"{cid}_{ts}".encode(), cs.encode())).decode()
    r = requests.post(f"{BASE}/v1/oauth2/token", data={
        "client_id": cid, "timestamp": str(ts), "client_secret_sign": sign,
        "grant_type": "client_credentials", "type": "SELF",
    }, timeout=30)
    r.raise_for_status()
    return r.json()["access_token"]


def main() -> None:
    kv = read_kv(KEY)
    tok = get_token(kv["NAVER_COMMERCE_CLIENT_ID"], kv["NAVER_COMMERCE_CLIENT_SECRET"])
    H = {"Authorization": f"Bearer {tok}", "Content-Type": "application/json"}

    all_contents: list[dict] = []
    page, size = 1, 100
    while True:
        r = requests.post(f"{BASE}/v1/products/search", headers=H,
                          json={"page": page, "size": size}, timeout=60)
        r.raise_for_status()
        body = r.json()
        contents = body.get("contents") or []
        all_contents.extend(contents)
        total = body.get("totalElements")
        print(f"page {page}: +{len(contents)} (누적 {len(all_contents)} / 전체 {total})")
        if not contents or len(all_contents) >= (total or 0):
            break
        page += 1
        time.sleep(0.3)

    today = dt.date.today().strftime("%Y%m%d")
    json_path = DESKTOP / f"naver_A_products_backup_{today}.json"
    csv_path = DESKTOP / f"naver_A_products_backup_{today}.csv"
    json_path.write_text(json.dumps(all_contents, ensure_ascii=False, indent=2), encoding="utf-8")

    rows = []
    for c in all_contents:
        origin_no = c.get("originProductNo")
        for ch in (c.get("channelProducts") or [{}]):
            rows.append({
                "originProductNo": origin_no,
                "channelProductNo": ch.get("channelProductNo"),
                "sellerManagementCode": ch.get("sellerManagementCode", ""),
                "name": ch.get("name", ""),
                "statusType": ch.get("statusType", ""),
                "salePrice": ch.get("salePrice", ""),
                "discountedPrice": ch.get("discountedPrice", ""),
                "categoryId": ch.get("categoryId", ""),
                "regDate": ch.get("regDate", ""),
            })
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as fp:
        w = csv.DictWriter(fp, fieldnames=list(rows[0].keys()) if rows else
                           ["originProductNo", "channelProductNo", "sellerManagementCode", "name", "statusType", "salePrice", "discountedPrice", "categoryId", "regDate"])
        w.writeheader()
        w.writerows(rows)

    # 상태 분포
    from collections import Counter
    st = Counter(r["statusType"] for r in rows)
    gs = sum(1 for r in rows if str(r["sellerManagementCode"]).upper().startswith("GS"))
    print(f"\n백업 완료: {len(rows)} 채널상품 / {len(all_contents)} 원상품")
    print(f"  GS코드 보유: {gs}")
    print(f"  상태분포: {dict(st)}")
    print(f"  JSON: {json_path}")
    print(f"  CSV : {csv_path}")


if __name__ == "__main__":
    main()
