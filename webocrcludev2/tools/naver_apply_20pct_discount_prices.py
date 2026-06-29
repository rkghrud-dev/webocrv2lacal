#!/usr/bin/env python3
"""네이버 A:홈런마켓 기존 상품 가격을 표시가+즉시할인 구조로 전환한다.

기본은 읽기/미리보기 전용이다. --apply를 붙여야 실제 원상품 수정 API를 호출한다.

계산:
  - 현재 실판매가 = discountedPrice가 있으면 discountedPrice, 없으면 salePrice
  - 100원 미만 상품은 제외
  - 표시가 = 현재 실판매가 * 1.25 후 올림
    * 현재가 2,000원 이하: 100원 단위 올림
    * 현재가 2,001원 이상: 500원 단위 올림
  - 할인액 = 표시가 - 현재 실판매가
  - 예상 최종가 = 현재 실판매가 유지
"""
from __future__ import annotations

import argparse
import base64
import csv
import json
import math
import re
import sys
import time
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any

import bcrypt
import requests


API_BASE = "https://api.commerce.naver.com/external"
ROOT = Path(__file__).resolve().parents[1]
REPORT_ROOT = ROOT / "data" / "exports" / "reports"
KEY_ROOT = Path.home() / "Desktop" / "key"


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


def ceil_to_unit(value: float | int, unit: int) -> int:
    unit = max(int(unit), 1)
    return int(math.ceil(float(value) / float(unit)) * unit)


def short(value: str, limit: int = 800) -> str:
    value = text(value)
    return value if len(value) <= limit else value[:limit] + "..."


def read_kv(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8-sig", errors="ignore").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        key, value = stripped.split("=", 1)
        values[key.strip()] = value.strip().strip("\"'")
    return values


class NaverClient:
    def __init__(self, key_file: Path):
        keys = read_kv(key_file)
        self.client_id = keys.get("NAVER_COMMERCE_CLIENT_ID", "")
        self.client_secret = keys.get("NAVER_COMMERCE_CLIENT_SECRET", "")
        if not self.client_id or not self.client_secret:
            raise RuntimeError(f"missing naver commerce key: {key_file}")
        self.session = requests.Session()
        self.token = ""
        self.token_until = datetime.min

    def access_token(self) -> str:
        if self.token and datetime.now() < self.token_until:
            return self.token
        timestamp = int(time.time() * 1000) - 3000
        password = f"{self.client_id}_{timestamp}".encode("utf-8")
        hashed = bcrypt.hashpw(password, self.client_secret.encode("utf-8"))
        sign = base64.b64encode(hashed).decode("utf-8")
        response = self.session.post(
            f"{API_BASE}/v1/oauth2/token",
            data={
                "client_id": self.client_id,
                "timestamp": str(timestamp),
                "client_secret_sign": sign,
                "grant_type": "client_credentials",
                "type": "SELF",
            },
            timeout=30,
        )
        response.raise_for_status()
        payload = response.json()
        self.token = payload["access_token"]
        self.token_until = datetime.now() + timedelta(seconds=int(payload.get("expires_in", 600)) - 60)
        return self.token

    def request(self, method: str, path: str, **kwargs: Any) -> requests.Response:
        headers = kwargs.pop("headers", {})
        headers["Authorization"] = f"Bearer {self.access_token()}"
        headers.setdefault("Content-Type", "application/json")
        response = self.session.request(method, f"{API_BASE}{path}", headers=headers, timeout=60, **kwargs)
        for delay in (2, 5, 10):
            if response.status_code != 429:
                break
            time.sleep(delay)
            response = self.session.request(method, f"{API_BASE}{path}", headers=headers, timeout=60, **kwargs)
        return response


def search_products(client: NaverClient, page_size: int = 100) -> list[dict[str, Any]]:
    all_contents: list[dict[str, Any]] = []
    page = 1
    while True:
        response = client.request(
            "POST",
            "/v1/products/search",
            data=json.dumps({"page": page, "size": page_size}, ensure_ascii=False).encode("utf-8"),
        )
        response.raise_for_status()
        payload = response.json()
        contents = payload.get("contents") or []
        if not isinstance(contents, list):
            break
        all_contents.extend(contents)
        total = parse_int(payload.get("totalElements"))
        print(f"search page {page}: +{len(contents)} (total {len(all_contents)} / {total or '?'})")
        if not contents or (total and len(all_contents) >= total):
            break
        page += 1
        time.sleep(0.2)
    return all_contents


def first_channel_product(content: dict[str, Any]) -> dict[str, Any]:
    channels = content.get("channelProducts")
    if isinstance(channels, list) and channels:
        first = channels[0]
        return first if isinstance(first, dict) else {}
    return {}


def effective_price(content: dict[str, Any], channel: dict[str, Any]) -> int:
    for key in ("discountedPrice", "salePrice"):
        price = parse_int(channel.get(key))
        if price > 0:
            return price
    for key in ("discountedPrice", "salePrice"):
        price = parse_int(content.get(key))
        if price > 0:
            return price
    return 0


def display_round_unit_for_price(current_price: int, default_unit: int) -> int:
    return 100 if 0 < current_price <= 2000 else default_unit


def calculated_prices(current_price: int, display_round_unit: int, preserve_final_price: bool = True) -> tuple[int, int, int]:
    unit = display_round_unit_for_price(current_price, display_round_unit)
    display_price = ceil_to_unit(current_price * 1.25, unit)
    if preserve_final_price:
        discount_amount = display_price - current_price
    else:
        discount_amount = ceil_to_unit(display_price * 0.20, 10)
    final_price = display_price - discount_amount
    return display_price, discount_amount, final_price


def load_target_origin_numbers(path: Path | None) -> set[str]:
    if path is None:
        return set()
    rows: list[dict[str, Any]]
    if path.suffix.lower() == ".csv":
        with path.open(encoding="utf-8-sig", newline="") as fp:
            rows = list(csv.DictReader(fp))
    else:
        rows = json.loads(path.read_text(encoding="utf-8-sig"))
    targets = set()
    for row in rows:
        if text(row.get("status")) == "OK":
            origin_no = text(row.get("originProductNo"))
            if origin_no:
                targets.add(origin_no)
    return targets


def make_plan(
    contents: list[dict[str, Any]],
    min_price: int,
    gs_prefix: str,
    display_round_unit: int,
    target_origin_numbers: set[str],
    preserve_final_price: bool,
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for content in contents:
        channel = first_channel_product(content)
        origin_no = text(content.get("originProductNo"))
        channel_no = text(channel.get("channelProductNo"))
        gs = text(channel.get("sellerManagementCode")).upper()
        name = text(channel.get("name") or content.get("name"))
        status_type = text(channel.get("statusType") or content.get("statusType"))
        current = effective_price(content, channel)
        if target_origin_numbers and origin_no not in target_origin_numbers:
            action = "SKIP_NOT_TARGET"
        elif gs_prefix and not gs.startswith(gs_prefix.upper()):
            action = "SKIP_NOT_GS"
        elif not origin_no:
            action = "SKIP_NO_ORIGIN"
        elif current < min_price:
            action = "SKIP_LOW_PRICE"
        else:
            action = "READY"
        display, discount, final = calculated_prices(current, display_round_unit, preserve_final_price) if action == "READY" else (0, 0, 0)
        rows.append({
            "action": action,
            "status": "",
            "message": "",
            "originProductNo": origin_no,
            "channelProductNo": channel_no,
            "sellerManagementCode": gs,
            "statusType": status_type,
            "name": name,
            "currentSalePrice": parse_int(channel.get("salePrice") or content.get("salePrice")),
            "currentDiscountedPrice": parse_int(channel.get("discountedPrice") or content.get("discountedPrice")),
            "currentEffectivePrice": current,
            "newDisplayPrice": display,
            "newDiscountAmount": discount,
            "expectedFinalPrice": final,
            "finalMinusCurrent": final - current if action == "READY" else "",
        })
    return rows


def backup_contents(contents: list[dict[str, Any]], stamp: str) -> Path:
    REPORT_ROOT.mkdir(parents=True, exist_ok=True)
    path = REPORT_ROOT / f"naver_homerun_price_backup_{stamp}.json"
    path.write_text(json.dumps(contents, ensure_ascii=False, indent=2), encoding="utf-8")
    return path


def write_csv(rows: list[dict[str, Any]], path: Path) -> None:
    if not rows:
        path.write_text("", encoding="utf-8-sig")
        return
    columns: list[str] = []
    for row in rows:
        for key in row.keys():
            if key not in columns:
                columns.append(key)
    with path.open("w", encoding="utf-8-sig", newline="") as fp:
        writer = csv.DictWriter(fp, fieldnames=columns)
        writer.writeheader()
        writer.writerows(rows)


def apply_one(client: NaverClient, row: dict[str, Any]) -> dict[str, Any]:
    origin_no = text(row.get("originProductNo"))
    result = dict(row)
    get_response = client.request("GET", f"/v2/products/origin-products/{origin_no}")
    result["getStatus"] = get_response.status_code
    if get_response.status_code != 200:
        result["action"] = "UPDATE"
        result["status"] = "GET_FAILED"
        result["message"] = short(get_response.text)
        return result

    payload = get_response.json()
    origin = payload.get("originProduct") if isinstance(payload.get("originProduct"), dict) else None
    if origin is None:
        result["action"] = "UPDATE"
        result["status"] = "NO_ORIGIN_PRODUCT"
        result["message"] = short(json.dumps(payload, ensure_ascii=False))
        return result

    origin["salePrice"] = int(result["newDisplayPrice"])
    origin["customerBenefit"] = {
        "immediateDiscountPolicy": {
            "discountMethod": {
                "value": int(result["newDiscountAmount"]),
                "unitType": "WON",
            },
            "mobileDiscountMethod": {
                "value": int(result["newDiscountAmount"]),
                "unitType": "WON",
            },
        },
    }

    put_response = client.request(
        "PUT",
        f"/v2/products/origin-products/{origin_no}",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
    )
    result["putStatus"] = put_response.status_code
    ok = put_response.status_code in {200, 201, 202}
    result["action"] = "UPDATE"
    result["status"] = "OK" if ok else "PUT_FAILED"
    result["message"] = short(put_response.text)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description="네이버 홈런마켓 기존 상품을 표시가+20% 즉시할인 구조로 전환")
    parser.add_argument("--key-file", type=Path, default=KEY_ROOT / "naver_client_key.txt")
    parser.add_argument("--apply", action="store_true", help="실제 네이버 원상품 수정 API 호출")
    parser.add_argument("--limit", type=int, default=0, help="앞에서 N개 READY 행만 처리")
    parser.add_argument("--min-price", type=int, default=100, help="이 금액 미만은 제외")
    parser.add_argument("--gs-prefix", default="GS", help="판매자관리코드 prefix 필터. 빈 문자열이면 전체")
    parser.add_argument("--display-round-unit", type=int, default=500, help="표시가 올림 단위")
    parser.add_argument("--target-report", type=Path, help="이전 apply 리포트의 status=OK originProductNo만 처리")
    parser.add_argument("--fixed-20-discount", action="store_true", help="최종가 유지 대신 표시가의 정확한 20%를 할인")
    args = parser.parse_args()

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    client = NaverClient(args.key_file)
    contents = search_products(client)
    backup_path = backup_contents(contents, stamp)
    target_origin_numbers = load_target_origin_numbers(args.target_report)
    rows = make_plan(
        contents,
        args.min_price,
        args.gs_prefix,
        args.display_round_unit,
        target_origin_numbers,
        preserve_final_price=not args.fixed_20_discount,
    )

    ready_indexes = [idx for idx, row in enumerate(rows) if row["action"] == "READY"]
    if args.limit:
        ready_indexes = ready_indexes[:args.limit]

    if args.apply:
        ready_set = set(ready_indexes)
        for index, row in enumerate(rows):
            if index not in ready_set:
                continue
            print(f"apply {len([i for i in ready_indexes if i <= index])}/{len(ready_indexes)} {row['sellerManagementCode']} {row['currentEffectivePrice']} -> {row['newDisplayPrice']} - {row['newDiscountAmount']}")
            rows[index] = apply_one(client, row)
            time.sleep(0.25)

    suffix = "apply" if args.apply else "preview"
    json_path = REPORT_ROOT / f"naver_homerun_price_discount_{suffix}_{stamp}.json"
    csv_path = REPORT_ROOT / f"naver_homerun_price_discount_{suffix}_{stamp}.csv"
    json_path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    write_csv(rows, csv_path)

    summary = {
        "apply": args.apply,
        "backup": str(backup_path),
        "json": str(json_path),
        "csv": str(csv_path),
        "total": len(rows),
        "ready": sum(1 for row in rows if row["action"] == "READY"),
        "skippedLowPrice": sum(1 for row in rows if row["action"] == "SKIP_LOW_PRICE"),
        "ok": sum(1 for row in rows if row.get("status") == "OK"),
        "failed": sum(1 for row in rows if row.get("status") and row.get("status") != "OK"),
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0 if summary["failed"] == 0 else 2


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
