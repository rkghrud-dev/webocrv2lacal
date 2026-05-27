import argparse
import csv
import gzip
import hmac
import json
import re
import time
from datetime import datetime
from hashlib import sha256
from pathlib import Path
from typing import Any

import requests


ROOT = Path(__file__).resolve().parents[1]
JOBS_ROOT = ROOT / "data" / "jobs"
REPORT_ROOT = ROOT / "data" / "exports" / "reports"
KEY_ROOT = Path.home() / "Desktop" / "key"


def read_kv(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        key, value = stripped.split("=", 1)
        values[key.strip()] = value.strip()
    return values


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig", errors="ignore"))


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


def short(value: str, limit: int = 500) -> str:
    return value if len(value) <= limit else value[:limit] + "..."


def iter_job_results() -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for path in JOBS_ROOT.glob("*.json"):
        try:
            job = load_json(path)
        except Exception:
            continue
        if not isinstance(job, dict):
            continue
        created = text(job.get("createdAt"))
        for item in job.get("result", {}).get("results", []) or []:
            if not isinstance(item, dict):
                continue
            rows.append({**item, "_jobPath": str(path), "_jobCreatedAt": created})
    return rows


def is_uploaded(item: dict[str, Any]) -> bool:
    status = text(item.get("status")).lower()
    raw = text(item.get("rawStatus")).upper()
    return status in {"uploaded", "success"} or raw in {"OK", "SUCCESS"}


def product_id(item: dict[str, Any]) -> str:
    for key in ("productId", "sellerProductId", "spdNo"):
        value = text(item.get(key))
        if value:
            return value
    return ""


def collect_latest_naver_prices(rows: list[dict[str, Any]]) -> dict[tuple[str, str], dict[str, Any]]:
    latest: dict[tuple[str, str], dict[str, Any]] = {}
    for item in rows:
        if text(item.get("market")) != "네이버" or not is_uploaded(item):
            continue
        account = text(item.get("account")) or "A"
        gs = text(item.get("gs")).upper()
        if not gs:
            continue
        option_items = item.get("optionItems") if isinstance(item.get("optionItems"), list) else []
        option_prices = [parse_int(opt.get("salePrice") or opt.get("price")) for opt in option_items if isinstance(opt, dict)]
        option_prices = [price for price in option_prices if price > 0]
        base_price = parse_int(item.get("salePrice") or item.get("price"))
        if not option_prices and base_price > 0:
            option_prices = [base_price]
        if not option_prices:
            continue
        key = (account, gs)
        created = text(item.get("_jobCreatedAt"))
        if key not in latest or created >= text(latest[key].get("_jobCreatedAt")):
            latest[key] = {
                "account": account,
                "gs": gs,
                "naverPrice": option_prices[0],
                "naverOptionPrices": option_prices,
                "naverProductId": product_id(item),
                "naverJobCreatedAt": created,
                "_jobCreatedAt": created,
            }
    return latest


def collect_coupang_candidates(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    candidates: list[dict[str, Any]] = []
    seen: set[tuple[str, str, str]] = set()
    for item in rows:
        if text(item.get("market")) != "쿠팡" or not is_uploaded(item):
            continue
        account = text(item.get("account")) or "A"
        gs = text(item.get("gs")).upper()
        seller_product_id = product_id(item)
        if not gs or not seller_product_id:
            continue
        local_price = parse_int(item.get("salePrice") or item.get("price"))
        if local_price >= 1000:
            continue
        key = (account, gs, seller_product_id)
        if key in seen:
            continue
        seen.add(key)
        candidates.append({
            "account": account,
            "gs": gs,
            "sellerProductId": seller_product_id,
            "localRecordedPrice": local_price,
            "workbookPath": text(item.get("workbookPath")),
            "jobCreatedAt": text(item.get("_jobCreatedAt")),
        })
    return candidates


class CoupangClient:
    BASE = "https://api-gateway.coupang.com"

    def __init__(self, key_file: Path):
        kv = read_kv(key_file)
        self.access_key = kv["access_key"]
        self.secret_key = kv["secret_key"]
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

    def update_price(self, vendor_item_id: str, price: int) -> tuple[int, str, Any]:
        path = f"/v2/providers/seller_api/apis/api/v1/marketplace/vendor-items/{vendor_item_id}/prices/{price}"
        return self.request("PUT", path, "forceSalePriceUpdate=true")


def find_vendor_items(value: Any) -> list[dict[str, Any]]:
    found: list[dict[str, Any]] = []
    if isinstance(value, dict):
        if value.get("vendorItemId"):
            found.append({
                "vendorItemId": text(value.get("vendorItemId")),
                "itemName": text(value.get("itemName")),
                "salePrice": parse_int(value.get("salePrice") or value.get("coupangSalePrice")),
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


def client_for_account(account: str, cache: dict[str, CoupangClient]) -> CoupangClient:
    if account not in cache:
        key_file = KEY_ROOT / ("coupang_api_junbi.txt" if account.upper() == "B" else "coupang_wing_api.txt")
        cache[account] = CoupangClient(key_file)
    return cache[account]


def write_report(rows: list[dict[str, Any]], stamp: str) -> tuple[Path, Path]:
    REPORT_ROOT.mkdir(parents=True, exist_ok=True)
    json_path = REPORT_ROOT / f"coupang_1000_to_naver_price_{stamp}.json"
    csv_path = REPORT_ROOT / f"coupang_1000_to_naver_price_{stamp}.csv"
    json_path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    columns = [
        "account", "gs", "sellerProductId", "vendorItemId", "itemName",
        "currentCoupangPrice", "targetNaverPrice", "action", "status", "message",
        "workbookPath",
    ]
    with csv_path.open("w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=columns)
        writer.writeheader()
        for row in rows:
            writer.writerow({col: row.get(col, "") for col in columns})
    return json_path, csv_path


def main() -> int:
    parser = argparse.ArgumentParser(description="쿠팡 1,000원 보정 상품을 네이버 판매가 기준으로 가격 동기화")
    parser.add_argument("--apply", action="store_true", help="실제 쿠팡 가격 변경 API 호출")
    parser.add_argument("--gs", help="특정 GS코드만 처리")
    parser.add_argument("--account", choices=["A", "B"], help="특정 계정만 처리")
    args = parser.parse_args()

    rows = iter_job_results()
    naver_prices = collect_latest_naver_prices(rows)
    candidates = collect_coupang_candidates(rows)
    if args.gs:
        candidates = [item for item in candidates if item["gs"] == args.gs.upper()]
    if args.account:
        candidates = [item for item in candidates if item["account"].upper() == args.account]

    clients: dict[str, CoupangClient] = {}
    report: list[dict[str, Any]] = []
    for candidate in candidates:
        naver = naver_prices.get((candidate["account"], candidate["gs"]))
        if naver is None:
            report.append({**candidate, "action": "SKIP", "status": "NO_NAVER_PRICE", "message": "같은 계정/GS의 네이버 업로드 가격을 찾지 못함"})
            continue

        client = client_for_account(candidate["account"], clients)
        status, body, product = client.get_product(candidate["sellerProductId"])
        time.sleep(0.15)
        if not (200 <= status < 300):
            report.append({**candidate, "action": "SKIP", "status": f"COUPANG_GET_{status}", "message": short(body)})
            continue

        vendor_items = find_vendor_items(product)
        if not vendor_items:
            report.append({**candidate, "action": "SKIP", "status": "NO_VENDOR_ITEM", "message": "vendorItemId를 찾지 못함"})
            continue

        target_prices = naver["naverOptionPrices"]
        for index, vendor_item in enumerate(vendor_items):
            current = vendor_item["salePrice"]
            if current != 1000:
                continue
            target = target_prices[index] if index < len(target_prices) else target_prices[0]
            row = {
                **candidate,
                "vendorItemId": vendor_item["vendorItemId"],
                "itemName": vendor_item["itemName"],
                "currentCoupangPrice": current,
                "targetNaverPrice": target,
                "naverProductId": naver["naverProductId"],
                "naverJobCreatedAt": naver["naverJobCreatedAt"],
            }
            if not args.apply:
                report.append({**row, "action": "DRY_RUN", "status": "READY", "message": "실제 변경 없음"})
                continue
            update_status, update_body, _ = client.update_price(vendor_item["vendorItemId"], target)
            time.sleep(0.2)
            ok = 200 <= update_status < 300
            report.append({
                **row,
                "action": "UPDATE",
                "status": "OK" if ok else f"FAIL_{update_status}",
                "message": short(update_body),
            })

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    json_path, csv_path = write_report(report, stamp)
    ready = sum(1 for row in report if row.get("status") in {"READY", "OK"})
    print(f"report_json={json_path}")
    print(f"report_csv={csv_path}")
    print(f"rows={len(report)} ready_or_ok={ready} apply={args.apply}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
