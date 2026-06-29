#!/usr/bin/env python3
"""쿠팡 업로드 상품을 API로 조회해 가격 수식 조정 HTML을 만든다.

실제 가격/쿠폰 변경은 하지 않는다.
"""
from __future__ import annotations

import argparse
import csv
import gzip
import hmac
import html
import json
import math
import re
import time
from datetime import datetime
from hashlib import sha256
from pathlib import Path
from typing import Any
from urllib.parse import urlencode

import requests
import urllib3


ROOT = Path(__file__).resolve().parents[1]
JOBS_ROOT = ROOT / "data" / "jobs"
REPORT_ROOT = ROOT / "data" / "exports" / "reports"
KEY_ROOT = Path.home() / "Desktop" / "key"

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)


def text(value: Any) -> str:
    return "" if value is None else str(value).strip()


def parse_int(value: Any) -> int:
    cleaned = re.sub(r"[^\d.-]", "", text(value))
    if not cleaned:
        return 0
    try:
        return int(float(cleaned))
    except ValueError:
        return 0


def read_kv(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        key, value = stripped.split("=", 1)
        values[key.strip()] = value.strip().strip("\"'")
    return values


def short(value: str, limit: int = 500) -> str:
    value = text(value)
    return value if len(value) <= limit else value[:limit] + "..."


def is_uploaded(item: dict[str, Any]) -> bool:
    status = text(item.get("status")).lower()
    raw = text(item.get("rawStatus")).upper()
    return status in {"uploaded", "success"} or raw in {"OK", "SUCCESS"}


def product_id(item: dict[str, Any]) -> str:
    for key in ("productId", "sellerProductId", "spdNo", "ProductId", "SellerProductId", "SpdNo"):
        value = text(item.get(key))
        if value:
            return value
    return ""


def iter_uploaded_coupang_products() -> list[dict[str, str]]:
    latest: dict[tuple[str, str, str], dict[str, str]] = {}
    for path in JOBS_ROOT.glob("*.json"):
        try:
            job = json.loads(path.read_text(encoding="utf-8-sig", errors="ignore"))
        except Exception:
            continue
        if not isinstance(job, dict):
            continue
        created = text(job.get("createdAt"))
        for item in job.get("result", {}).get("results", []) or []:
            if not isinstance(item, dict):
                continue
            if text(item.get("market")) != "쿠팡" or not is_uploaded(item):
                continue
            account = text(item.get("account")) or "A"
            gs = text(item.get("gs")).upper()
            seller_product_id = product_id(item)
            if not gs or not seller_product_id:
                continue
            key = (account, gs, seller_product_id)
            if key not in latest or created >= latest[key].get("jobCreatedAt", ""):
                latest[key] = {
                    "account": account,
                    "gs": gs,
                    "sellerProductId": seller_product_id,
                    "jobCreatedAt": created,
                    "workbookPath": text(item.get("workbookPath")),
                }
    return sorted(latest.values(), key=lambda row: (row["account"], row["gs"], row["sellerProductId"]))


class CoupangClient:
    BASE = "https://api-gateway.coupang.com"

    def __init__(self, key_file: Path):
        kv = read_kv(key_file)
        self.access_key = kv["access_key"]
        self.secret_key = kv["secret_key"]
        self.vendor_id = kv["vendor_id"]
        self.session = requests.Session()

    def auth(self, method: str, path: str, query: str = "") -> str:
        signed_date = datetime.utcnow().strftime("%y%m%dT%H%M%SZ")
        message = signed_date + method + path + query
        signature = hmac.new(self.secret_key.encode(), message.encode(), sha256).hexdigest()
        return f"CEA algorithm=HmacSHA256, access-key={self.access_key}, signed-date={signed_date}, signature={signature}"

    def request(self, method: str, path: str, query: str = "") -> tuple[int, str, Any]:
        url = self.BASE + path + (("?" + query) if query else "")
        headers = {
            "Authorization": self.auth(method, path, query),
            "X-EXTENDED-TIMEOUT": "90000",
            "Accept-Encoding": "gzip, identity",
            "Content-Type": "application/json;charset=UTF-8",
        }
        response = self.session.request(method, url, headers=headers, timeout=45, verify=False)
        raw = response.content
        if len(raw) >= 2 and raw[0] == 0x1F and raw[1] == 0x8B:
            raw = gzip.decompress(raw)
        body = raw.decode("utf-8", errors="replace")
        try:
            parsed = json.loads(body) if body else {}
        except Exception:
            parsed = {}
        return response.status_code, body, parsed

    def get_product(self, seller_product_id: str) -> tuple[int, str, Any]:
        path = f"/v2/providers/seller_api/apis/api/v1/marketplace/seller-products/{seller_product_id}"
        return self.request("GET", path)

    def list_products(self, *, next_token: str = "1", max_per_page: int = 100,
                      status: str = "APPROVED") -> tuple[int, str, Any]:
        path = "/v2/providers/seller_api/apis/api/v1/marketplace/seller-products"
        params = {
            "vendorId": self.vendor_id,
            "nextToken": next_token,
            "maxPerPage": str(min(max(int(max_per_page), 1), 100)),
        }
        if status:
            params["status"] = status
        query = urlencode(params)
        return self.request("GET", path, query)


def client_for_account(account: str, cache: dict[str, CoupangClient]) -> CoupangClient:
    account = account.upper()
    if account not in cache:
        key_file = KEY_ROOT / ("coupang_api_junbi.txt" if account == "B" else "coupang_wing_api.txt")
        cache[account] = CoupangClient(key_file)
    return cache[account]


def gs_from_product(value: dict[str, Any]) -> str:
    for key in ("sellerProductName", "sellerProductName", "externalVendorSku", "sellerProductId"):
        found = re.search(r"GS\d{7}[A-Z]?", text(value.get(key)).upper())
        if found:
            return found.group(0)
    return ""


def iter_coupang_products_from_api(
    account: str,
    client: CoupangClient,
    *,
    limit: int = 0,
    status: str = "APPROVED",
    max_pages: int = 0,
) -> list[dict[str, str]]:
    products: list[dict[str, str]] = []
    seen: set[str] = set()
    next_token = "1"
    page = 0
    while next_token:
        page += 1
        if max_pages and page > max_pages:
            break
        status_code, body, parsed = client.list_products(next_token=next_token, status=status)
        print(f"list {account} page {page} token={next_token} -> {status_code}")
        if not (200 <= status_code < 300):
            products.append({
                "account": account,
                "gs": "",
                "sellerProductId": "",
                "jobCreatedAt": "",
                "workbookPath": "",
                "listStatus": f"LIST_{status_code}",
                "listMessage": short(body),
            })
            break
        data = parsed.get("data") if isinstance(parsed, dict) else []
        if not isinstance(data, list) or not data:
            break
        for item in data:
            if not isinstance(item, dict):
                continue
            seller_product_id = text(item.get("sellerProductId"))
            if not seller_product_id or seller_product_id in seen:
                continue
            seen.add(seller_product_id)
            products.append({
                "account": account,
                "gs": gs_from_product(item),
                "sellerProductId": seller_product_id,
                "sellerProductName": text(item.get("sellerProductName")),
                "coupangProductId": text(item.get("productId")),
                "statusName": text(item.get("statusName")),
                "createdAt": text(item.get("createdAt")),
                "jobCreatedAt": "",
                "workbookPath": "",
            })
            if limit and len(products) >= limit:
                return products
        next_token = text(parsed.get("nextToken")) if isinstance(parsed, dict) else ""
        if not next_token:
            break
        time.sleep(0.15)
    return products


def find_vendor_items(value: Any) -> list[dict[str, Any]]:
    found: list[dict[str, Any]] = []
    if isinstance(value, dict):
        if value.get("vendorItemId"):
            found.append({
                "vendorItemId": text(value.get("vendorItemId")),
                "itemName": text(value.get("itemName") or value.get("sellerProductItemName")),
                "salePrice": parse_int(value.get("salePrice") or value.get("coupangSalePrice")),
                "originalPrice": parse_int(value.get("originalPrice")),
            })
        for child in value.values():
            found.extend(find_vendor_items(child))
    elif isinstance(value, list):
        for child in value:
            found.extend(find_vendor_items(child))
    unique: dict[str, dict[str, Any]] = {}
    for item in found:
        unique.setdefault(item["vendorItemId"], item)
    return list(unique.values())


def write_csv(rows: list[dict[str, Any]], path: Path) -> None:
    columns = [
        "account", "gs", "sellerProductId", "vendorItemId", "itemName",
        "currentPrice", "originalPrice", "status", "message", "workbookPath",
    ]
    with path.open("w", encoding="utf-8-sig", newline="") as fp:
        writer = csv.DictWriter(fp, fieldnames=columns)
        writer.writeheader()
        for row in rows:
            writer.writerow({key: row.get(key, "") for key in columns})


def html_report(rows: list[dict[str, Any]], path: Path) -> None:
    data = json.dumps([row for row in rows if row.get("status") == "OK"], ensure_ascii=False)
    page = f"""<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><title>쿠팡 가격 수식 조정표</title>
<style>
*{{box-sizing:border-box}}body{{font-family:Segoe UI,Malgun Gothic,Arial,sans-serif;margin:24px;background:#f7f9fc;color:#172033}}h1{{font-size:22px;margin:0 0 6px}}p{{margin:0 0 16px;color:#5b6678}}.controls{{display:grid;grid-template-columns:repeat(7,minmax(120px,1fr));gap:10px;margin:16px 0;padding:14px;background:#fff;border:1px solid #dbe3ee;border-radius:8px}}.field{{display:flex;flex-direction:column;gap:5px}}.field label{{font-size:12px;color:#526071;font-weight:700}}.field input,.field select{{height:34px;border:1px solid #cbd5e1;border-radius:6px;padding:0 9px;background:#fff}}.summary{{display:flex;gap:10px;flex-wrap:wrap;margin:14px 0}}.pill{{background:#fff;border:1px solid #dbe3ee;border-radius:8px;padding:9px 11px;font-weight:700}}.table-wrap{{border:1px solid #dbe3ee;background:#fff;overflow:auto;max-height:72vh}}table{{border-collapse:collapse;width:100%;font-size:13px}}th,td{{border-bottom:1px solid #e7eef6;padding:8px 9px;text-align:left;vertical-align:top}}th{{position:sticky;top:0;background:#eaf1f8;z-index:1;white-space:nowrap}}tr:nth-child(even){{background:#fbfdff}}td.num{{text-align:right;white-space:nowrap}}td.name{{min-width:260px;max-width:520px}}.skip{{color:#9a3412;background:#fff7ed}}.diff-zero{{color:#16703a}}.diff-plus{{color:#b42318}}@media(max-width:1100px){{.controls{{grid-template-columns:repeat(2,minmax(120px,1fr))}}}}
</style></head><body>
<h1>쿠팡 가격 수식 조정표</h1>
<p>쿠팡 API로 현재 상품/옵션 가격을 가져온 표입니다. 값 변경 시 예상 판매가와 할인 후 금액을 즉시 다시 계산합니다.</p>
<div class="controls">
  <div class="field"><label for="minPrice">제외 기준 이하</label><input id="minPrice" type="number" value="1000" step="100"></div>
  <div class="field"><label for="markupPct">초기가격 + %</label><input id="markupPct" type="number" value="25" step="0.1"></div>
  <div class="field"><label for="discountPct">할인 - %</label><input id="discountPct" type="number" value="20" step="0.1"></div>
  <div class="field"><label for="threshold">단위 전환 기준</label><input id="threshold" type="number" value="2000" step="100"></div>
  <div class="field"><label for="lowUnit">기준 이하 단위</label><input id="lowUnit" type="number" value="100" step="10"></div>
  <div class="field"><label for="highUnit">기준 초과 단위</label><input id="highUnit" type="number" value="100" step="100"></div>
  <div class="field"><label for="roundMode">변경가 처리</label><select id="roundMode"><option value="ceil">올림</option><option value="round">반올림</option><option value="floor">내림</option></select></div>
</div>
<div class="summary" id="summary"></div>
<div class="table-wrap"><table><thead><tr><th>계정</th><th>GS</th><th>옵션ID</th><th>옵션명</th><th>기본가</th><th>변경가</th><th>할인액</th><th>예상 최종가</th><th>기존가 차이</th><th>할인율</th><th>적용 여부</th></tr></thead><tbody id="tbody"></tbody></table></div>
<script>
const rows = {data};
const el = id => document.getElementById(id);
const money = n => Number(n||0).toLocaleString('ko-KR') + '원';
function roundTo(value, unit, mode) {{ unit=Math.max(1,Number(unit||1)); const x=value/unit; if(mode==='floor') return Math.floor(x)*unit; if(mode==='round') return Math.round(x)*unit; return Math.ceil(x)*unit; }}
function render() {{
  const minPrice=Number(el('minPrice').value||0), markup=Number(el('markupPct').value||0)/100, discountPct=Number(el('discountPct').value||0)/100;
  const threshold=Number(el('threshold').value||0), lowUnit=Number(el('lowUnit').value||1), highUnit=Number(el('highUnit').value||1), mode=el('roundMode').value;
  let ok=0, skip=0, diffSum=0, maxAbsDiff=0;
  document.getElementById('tbody').innerHTML = rows.map(r => {{
    const base=Number(r.currentPrice||0);
    if(base <= minPrice) {{ skip++; return `<tr class="skip"><td>${{r.account}}</td><td>${{r.gs}}</td><td>${{r.vendorItemId}}</td><td class="name">${{r.itemName||''}}</td><td class="num">${{money(base)}}</td><td class="num">-</td><td class="num">-</td><td class="num">-</td><td class="num">-</td><td class="num">-</td><td>제외</td></tr>`; }}
    const unit = base <= threshold ? lowUnit : highUnit;
    const display = roundTo(base * (1 + markup), unit, mode);
    const discount = roundTo(display * discountPct, 10, 'round');
    const finalPrice = Math.max(0, display - discount);
    const diff = finalPrice - base;
    const rate = display ? discount / display * 100 : 0;
    ok++; diffSum += diff; maxAbsDiff = Math.max(maxAbsDiff, Math.abs(diff));
    const diffClass = diff === 0 ? 'diff-zero' : 'diff-plus';
    return `<tr><td>${{r.account}}</td><td>${{r.gs}}</td><td>${{r.vendorItemId}}</td><td class="name">${{r.itemName||''}}</td><td class="num">${{money(base)}}</td><td class="num">${{money(display)}}</td><td class="num">${{money(discount)}}</td><td class="num">${{money(finalPrice)}}</td><td class="num ${{diffClass}}">${{diff>0?'+':''}}${{money(diff)}}</td><td class="num">${{rate.toFixed(1)}}%</td><td>적용</td></tr>`;
  }}).join('');
  const avgDiff = ok ? Math.round(diffSum / ok) : 0;
  el('summary').innerHTML = `<div class="pill">가져온 옵션 ${{rows.length.toLocaleString()}}개</div><div class="pill">적용 대상 ${{ok.toLocaleString()}}개</div><div class="pill">제외 ${{skip.toLocaleString()}}개</div><div class="pill">평균 차이 ${{avgDiff>0?'+':''}}${{money(avgDiff)}}</div><div class="pill">최대 차이 ${{money(maxAbsDiff)}}</div>`;
}}
['minPrice','markupPct','discountPct','threshold','lowUnit','highUnit','roundMode'].forEach(id => el(id).addEventListener('input', render));
render();
</script></body></html>"""
    path.write_text(page, encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="쿠팡 현재 상품 가격 수식 조정표 생성")
    parser.add_argument("--account", choices=["A", "B"], help="특정 계정만 조회")
    parser.add_argument("--limit", type=int, default=0, help="앞에서 N개 상품만 조회")
    parser.add_argument("--source", choices=["api", "jobs"], default="api",
                        help="api=쿠팡 상품목록 API 직접 조회, jobs=기존 업로드 로그에서 조회")
    parser.add_argument("--status", default="APPROVED", help="쿠팡 상품목록 status 필터. 빈 문자열이면 전체")
    parser.add_argument("--max-pages", type=int, default=0, help="API 목록 조회 페이지 제한(0=끝까지)")
    args = parser.parse_args()

    account_list = [args.account] if args.account else ["A", "B"]
    clients: dict[str, CoupangClient] = {}
    if args.source == "api":
        products = []
        per_account_limit = args.limit if len(account_list) == 1 else 0
        for account in account_list:
            client = client_for_account(account, clients)
            products.extend(iter_coupang_products_from_api(
                account, client,
                limit=per_account_limit,
                status=args.status,
                max_pages=args.max_pages,
            ))
        if args.limit and len(account_list) > 1:
            products = products[:args.limit]
    else:
        products = iter_uploaded_coupang_products()
        if args.account:
            products = [row for row in products if row["account"].upper() == args.account]
        if args.limit:
            products = products[:args.limit]

    if args.limit:
        products = products[:args.limit]

    rows: list[dict[str, Any]] = []
    for idx, product in enumerate(products, start=1):
        if product.get("listStatus"):
            rows.append({**product, "status": product["listStatus"], "message": product.get("listMessage", "")})
            continue
        client = client_for_account(product["account"], clients)
        status, body, parsed = client.get_product(product["sellerProductId"])
        print(f"fetch {idx}/{len(products)} {product['account']} {product['gs']} {product['sellerProductId']} -> {status}")
        if not (200 <= status < 300):
            rows.append({**product, "status": f"GET_{status}", "message": short(body)})
            continue
        vendor_items = find_vendor_items(parsed)
        if not vendor_items:
            rows.append({**product, "status": "NO_VENDOR_ITEM", "message": "vendorItemId 없음"})
            continue
        for item in vendor_items:
            rows.append({
                **product,
                "vendorItemId": item["vendorItemId"],
                "itemName": item["itemName"],
                "currentPrice": item["salePrice"],
                "originalPrice": item["originalPrice"],
                "status": "OK",
                "message": "",
            })
        time.sleep(0.15)

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    REPORT_ROOT.mkdir(parents=True, exist_ok=True)
    json_path = REPORT_ROOT / f"coupang_price_formula_products_{stamp}.json"
    csv_path = REPORT_ROOT / f"coupang_price_formula_products_{stamp}.csv"
    html_path = REPORT_ROOT / f"coupang_price_formula_products_{stamp}.html"
    json_path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    write_csv(rows, csv_path)
    html_report(rows, html_path)
    print(json.dumps({
        "products": len(products),
        "rows": len(rows),
        "okRows": sum(1 for row in rows if row.get("status") == "OK"),
        "json": str(json_path),
        "csv": str(csv_path),
        "html": str(html_path),
    }, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
