import csv
import gzip
import hmac
import json
import time
from collections import defaultdict
from datetime import datetime, timedelta
from hashlib import sha256
from pathlib import Path
from typing import Any

import requests


ROOT = Path(__file__).resolve().parents[1]
REPORT_ROOT = ROOT / "data" / "exports" / "reports"
KEY_ROOT = Path.home() / "Desktop" / "key"
SOURCE_CSV = REPORT_ROOT / "coupang_1000_to_naver_price_20260526_203656.csv"


def read_kv(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        key, value = stripped.split("=", 1)
        values[key.strip()] = value.strip()
    return values


def short(value: str, limit: int = 500) -> str:
    return value if len(value) <= limit else value[:limit] + "..."


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

    def request(self, method: str, path: str, *, query: str = "", body: Any = None) -> tuple[int, str, Any]:
        url = self.BASE + path + (("?" + query) if query else "")
        headers = {
            "Authorization": self.auth(method, path, query),
            "X-EXTENDED-TIMEOUT": "90000",
            "Accept-Encoding": "gzip, identity",
            "Content-Type": "application/json;charset=UTF-8",
        }
        response = self.session.request(method, url, headers=headers, json=body, timeout=45, verify=False)
        raw = response.content
        if len(raw) >= 2 and raw[0] == 0x1F and raw[1] == 0x8B:
            raw = gzip.decompress(raw)
        text = raw.decode("utf-8", errors="replace")
        try:
            parsed = json.loads(text) if text else {}
        except Exception:
            parsed = {}
        return response.status_code, text, parsed

    def contract_list(self) -> tuple[int, str, Any]:
        path = f"/v2/providers/fms/apis/api/v2/vendors/{self.vendor_id}/contract/list"
        return self.request("GET", path)

    def create_coupon(self, body: dict[str, Any]) -> tuple[int, str, Any]:
        path = f"/v2/providers/fms/apis/api/v2/vendors/{self.vendor_id}/coupon"
        return self.request("POST", path, body=body)

    def add_items(self, coupon_id: str, vendor_item_ids: list[str]) -> tuple[int, str, Any]:
        path = f"/v2/providers/fms/apis/api/v1/vendors/{self.vendor_id}/coupons/{coupon_id}/items"
        return self.request("POST", path, body={"vendorItems": [int(item) for item in vendor_item_ids]})

    def request_status(self, requested_id: str) -> tuple[int, str, Any]:
        path = f"/v2/providers/fms/apis/api/v1/vendors/{self.vendor_id}/requested/{requested_id}"
        return self.request("GET", path)


def extract_content(parsed: Any) -> dict[str, Any]:
    if not isinstance(parsed, dict):
        return {}
    data = parsed.get("data")
    if isinstance(data, dict):
        content = data.get("content")
        if isinstance(content, dict):
            return content
    return {}


def latest_request_status(client: CoupangClient, requested_id: str, waits: int = 6) -> dict[str, Any]:
    last: dict[str, Any] = {}
    for _ in range(waits):
        status, text, parsed = client.request_status(requested_id)
        content = extract_content(parsed)
        last = {
            "requestStatusCode": status,
            "requestStatusBody": short(text),
            "requestStatus": content.get("status", ""),
            "couponId": content.get("couponId", ""),
            "succeeded": content.get("succeeded", ""),
            "failed": content.get("failed", ""),
            "total": content.get("total", ""),
            "failedVendorItems": json.dumps(content.get("failedVendorItems", []), ensure_ascii=False),
        }
        if content.get("status") in {"DONE", "FAIL"}:
            break
        time.sleep(2)
    return last


def active_contract_id(client: CoupangClient) -> tuple[str, dict[str, Any]]:
    status, text, parsed = client.contract_list()
    if not (200 <= status < 300):
        raise RuntimeError(f"contract list failed: {status} {short(text)}")
    content = parsed.get("data", {}).get("content", []) if isinstance(parsed, dict) else []
    now = datetime.now()
    candidates = []
    for item in content:
        if not isinstance(item, dict):
            continue
        start = datetime.strptime(item["start"], "%Y-%m-%d %H:%M:%S")
        end = datetime.strptime(item["end"], "%Y-%m-%d %H:%M:%S")
        if start <= now <= end:
            candidates.append(item)
    if not candidates:
        raise RuntimeError("active contract not found")
    selected = sorted(candidates, key=lambda x: (x.get("type") != "NON_CONTRACT_BASED", x.get("contractId", 0)))[0]
    return str(selected["contractId"]), selected


def load_targets() -> dict[tuple[str, int, int], list[dict[str, str]]]:
    with SOURCE_CSV.open(encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
    groups: dict[tuple[str, int, int], list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        current = int(row.get("currentCoupangPrice") or 0)
        target = int(row.get("targetNaverPrice") or 0)
        if row.get("status") != "FAIL_400" or current != 1000 or not (500 <= target < 1000):
            continue
        discount_rate = int(round((current - target) / current * 100))
        if discount_rate <= 0 or discount_rate > 50:
            continue
        groups[(row["account"], target, discount_rate)].append(row)
    return groups


def write_report(rows: list[dict[str, Any]], stamp: str) -> Path:
    path = REPORT_ROOT / f"coupang_instant_discount_500plus_{stamp}.csv"
    columns = [
        "account", "targetPrice", "discountRate", "contractId", "couponId", "createRequestedId",
        "addItemsRequestedId", "vendorItemCount", "vendorItemIds", "gsCodes", "createStatus",
        "createMessage", "createRequestStatus", "addStatus", "addMessage", "addRequestStatus",
        "succeeded", "failed", "failedVendorItems",
    ]
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=columns)
        writer.writeheader()
        for row in rows:
            writer.writerow({key: row.get(key, "") for key in columns})
    return path


def main() -> int:
    clients = {
        "A": CoupangClient(KEY_ROOT / "coupang_wing_api.txt"),
        "B": CoupangClient(KEY_ROOT / "coupang_api_junbi.txt"),
    }
    contracts: dict[str, tuple[str, dict[str, Any]]] = {
        account: active_contract_id(client)
        for account, client in clients.items()
    }
    groups = load_targets()
    start = (datetime.now() + timedelta(days=1)).strftime("%Y-%m-%d 00:00:00")
    end = "2026-12-31 23:59:59"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    report: list[dict[str, Any]] = []

    for (account, target_price, discount_rate), items in sorted(groups.items()):
        client = clients[account]
        contract_id, _contract = contracts[account]
        vendor_item_ids = [row["vendorItemId"] for row in items]
        gs_codes = sorted({row["gs"] for row in items})
        coupon_name = f"NV{target_price}_{discount_rate}P_{account}_{stamp[-6:]}"
        body = {
            "contractId": contract_id,
            "name": coupon_name[:45],
            "maxDiscountPrice": "1000",
            "discount": str(discount_rate),
            "startAt": start,
            "endAt": end,
            "type": "RATE",
            "wowExclusive": "false",
        }
        create_status, create_text, create_parsed = client.create_coupon(body)
        create_content = extract_content(create_parsed)
        create_requested_id = str(create_content.get("requestedId") or "")
        create_check = latest_request_status(client, create_requested_id) if create_requested_id else {}
        coupon_id = str(create_check.get("couponId") or create_content.get("couponId") or "")

        row: dict[str, Any] = {
            "account": account,
            "targetPrice": target_price,
            "discountRate": discount_rate,
            "contractId": contract_id,
            "couponId": coupon_id,
            "createRequestedId": create_requested_id,
            "vendorItemCount": len(vendor_item_ids),
            "vendorItemIds": "|".join(vendor_item_ids),
            "gsCodes": "|".join(gs_codes),
            "createStatus": create_status,
            "createMessage": short(create_text),
            "createRequestStatus": create_check.get("requestStatus", ""),
        }

        if not coupon_id or create_check.get("requestStatus") == "FAIL":
            report.append(row)
            continue

        add_status, add_text, add_parsed = client.add_items(coupon_id, vendor_item_ids)
        add_content = extract_content(add_parsed)
        add_requested_id = str(add_content.get("requestedId") or "")
        add_check = latest_request_status(client, add_requested_id) if add_requested_id else {}
        row.update({
            "addItemsRequestedId": add_requested_id,
            "addStatus": add_status,
            "addMessage": short(add_text),
            "addRequestStatus": add_check.get("requestStatus", ""),
            "succeeded": add_check.get("succeeded", ""),
            "failed": add_check.get("failed", ""),
            "failedVendorItems": add_check.get("failedVendorItems", ""),
        })
        report.append(row)

    report_path = write_report(report, stamp)
    print(f"report_csv={report_path}")
    print(f"coupon_groups={len(report)}")
    for row in report:
        print(
            row["account"],
            f"target={row['targetPrice']}",
            f"rate={row['discountRate']}%",
            f"items={row['vendorItemCount']}",
            f"coupon={row.get('couponId', '')}",
            f"create={row.get('createRequestStatus', '')}",
            f"add={row.get('addRequestStatus', '')}",
            f"ok={row.get('succeeded', '')}",
            f"fail={row.get('failed', '')}",
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
