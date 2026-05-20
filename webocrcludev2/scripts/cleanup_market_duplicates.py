import base64
import csv
import gzip
import hmac
import json
import re
import time
from collections import defaultdict
from datetime import datetime, timedelta
from hashlib import sha256
from pathlib import Path
from typing import Any

import bcrypt
import requests


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "data"
JOBS_ROOT = DATA_ROOT / "jobs"
LOG_ROOT = DATA_ROOT / "exports" / "logs"
KEY_ROOT = Path.home() / "Desktop" / "key"
NOW = datetime.now()
CUTOFF = NOW - timedelta(days=30)


def read_kv(path: Path) -> dict[str, str]:
    out: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        key, value = stripped.split("=", 1)
        out[key.strip()] = value.strip()
    return out


def parse_dt_from_path(path: Path) -> datetime:
    match = re.search(r"(20\d{6})_(\d{6})", str(path))
    if match:
        return datetime.strptime(match.group(1) + match.group(2), "%Y%m%d%H%M%S")
    return datetime.fromtimestamp(path.stat().st_mtime)


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig", errors="ignore"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def short(text: str, limit: int = 500) -> str:
    return text if len(text) <= limit else text[:limit] + "..."


class CoupangClient:
    BASE = "https://api-gateway.coupang.com"

    def __init__(self, key_file: Path):
        kv = read_kv(key_file)
        self.access_key = kv["access_key"]
        self.secret_key = kv["secret_key"]
        self.vendor_id = kv.get("vendor_id", "")
        self.session = requests.Session()

    def auth(self, method: str, path: str, query: str = "") -> str:
        dt = datetime.utcnow().strftime("%y%m%dT%H%M%SZ")
        message = dt + method + path + query
        sig = hmac.new(self.secret_key.encode(), message.encode(), sha256).hexdigest()
        return f"CEA algorithm=HmacSHA256, access-key={self.access_key}, signed-date={dt}, signature={sig}"

    def request(self, method: str, path: str, *, query: str = "", json_body: Any = None) -> tuple[int, str, Any]:
        url = self.BASE + path + (("?" + query) if query else "")
        headers = {
            "Authorization": self.auth(method, path, query),
            "X-EXTENDED-TIMEOUT": "90000",
            "Accept-Encoding": "gzip, identity",
            "Content-Type": "application/json;charset=UTF-8",
        }
        resp = self.session.request(method, url, headers=headers, json=json_body, timeout=45, verify=False)
        raw = resp.content
        if len(raw) >= 2 and raw[0] == 0x1F and raw[1] == 0x8B:
            raw = gzip.decompress(raw)
        text = raw.decode("utf-8", errors="replace")
        try:
            parsed = json.loads(text) if text else {}
        except Exception:
            parsed = {}
        return resp.status_code, text, parsed

    def get_product(self, seller_product_id: str) -> tuple[bool, int, str, Any]:
        path = f"/v2/providers/seller_api/apis/api/v1/marketplace/seller-products/{seller_product_id}"
        status, text, parsed = self.request("GET", path)
        return 200 <= status < 300, status, text, parsed

    def stop_vendor_item(self, vendor_item_id: str) -> tuple[bool, int, str]:
        path = f"/v2/providers/seller_api/apis/api/v1/marketplace/vendor-items/{vendor_item_id}/sales/stop"
        status, text, _ = self.request("PUT", path)
        return 200 <= status < 300, status, text

    def delete_product(self, seller_product_id: str) -> tuple[bool, int, str]:
        path = f"/v2/providers/seller_api/apis/api/v1/marketplace/seller-products/{seller_product_id}"
        status, text, _ = self.request("DELETE", path)
        return 200 <= status < 300, status, text


class NaverClient:
    BASE = "https://api.commerce.naver.com/external"

    def __init__(self, key_file: Path):
        kv = read_kv(key_file)
        self.client_id = kv["NAVER_COMMERCE_CLIENT_ID"]
        self.client_secret = kv["NAVER_COMMERCE_CLIENT_SECRET"]
        self.session = requests.Session()
        self.token = ""
        self.expiry = 0.0

    def access_token(self) -> str:
        if self.token and time.time() < self.expiry:
            return self.token
        timestamp = int(time.time() * 1000) - 3000
        password = f"{self.client_id}_{timestamp}".encode()
        hashed = bcrypt.hashpw(password, self.client_secret.encode())
        sign = base64.b64encode(hashed).decode()
        resp = self.session.post(
            f"{self.BASE}/v1/oauth2/token",
            data={
                "client_id": self.client_id,
                "timestamp": str(timestamp),
                "client_secret_sign": sign,
                "grant_type": "client_credentials",
                "type": "SELF",
            },
            timeout=30,
        )
        resp.raise_for_status()
        data = resp.json()
        self.token = data["access_token"]
        self.expiry = time.time() + int(data.get("expires_in", 600)) - 60
        return self.token

    def request(self, method: str, path: str) -> tuple[int, str, Any]:
        resp = self.session.request(
            method,
            self.BASE + path,
            headers={"Authorization": f"Bearer {self.access_token()}"},
            timeout=45,
        )
        text = resp.text
        try:
            parsed = resp.json() if text else {}
        except Exception:
            parsed = {}
        return resp.status_code, text, parsed

    def get_origin_product(self, origin_product_no: str) -> tuple[bool, int, str, Any]:
        status, text, parsed = self.request("GET", f"/v2/products/origin-products/{origin_product_no}")
        return 200 <= status < 300, status, text, parsed

    def delete_origin_product(self, origin_product_no: str) -> tuple[bool, int, str]:
        status, text, _ = self.request("DELETE", f"/v2/products/origin-products/{origin_product_no}")
        return 200 <= status < 300, status, text


def collect_coupang_successes() -> list[dict[str, Any]]:
    items: list[dict[str, Any]] = []

    def walk(value: Any, file_path: Path, dt: datetime, context_gs: str = "") -> None:
        if isinstance(value, dict):
            gs = str(value.get("gs") or value.get("GsCode") or context_gs or "").upper()
            market = str(value.get("market") or value.get("Market") or value.get("channel") or "")
            status = str(value.get("status") or value.get("Status") or "")
            pid = str(value.get("sellerProductId") or value.get("productId") or value.get("ProductId") or "")
            queue = str(value.get("queueKey") or "")
            if ("쿠팡" in market or "쿠팡" in queue) and gs.startswith("GS") and pid:
                if status.lower() in {"uploaded", "ok", "success"}:
                    items.append({
                        "market": "coupang",
                        "dt": dt,
                        "file": str(file_path),
                        "account": queue.split(":", 1)[0] if ":" in queue else "",
                        "gs": gs,
                        "productId": pid,
                        "name": str(value.get("sourceName") or value.get("title") or value.get("name") or ""),
                    })
            for child in value.values():
                walk(child, file_path, dt, gs or context_gs)
        elif isinstance(value, list):
            for child in value:
                walk(child, file_path, dt, context_gs)

    for path in JOBS_ROOT.glob("*.json"):
        dt = parse_dt_from_path(path)
        if dt < CUTOFF:
            continue
        try:
            walk(load_json(path), path, dt)
        except Exception:
            continue

    unique: dict[tuple[str, str, str], dict[str, Any]] = {}
    for item in items:
        unique[(item["account"], item["gs"], item["productId"])] = item
    return list(unique.values())


def collect_naver_successes() -> list[dict[str, Any]]:
    items: list[dict[str, Any]] = []
    for summary in (LOG_ROOT / "naver_upload").glob("*/summary.json"):
        dt = parse_dt_from_path(summary)
        if dt < CUTOFF:
            continue
        try:
            rows = load_json(summary)
        except Exception:
            continue
        if not isinstance(rows, list):
            continue
        request_dir = summary.parent / "requests"
        files = sorted(request_dir.glob("*.json"), key=lambda p: ("_retry" in p.stem, p.name))
        if not files:
            continue
        gs = files[0].stem.replace("_retry", "").upper()
        if not gs.startswith("GS"):
            continue
        for row in rows:
            status = str(row.get("Status") or row.get("status") or "")
            pid = str(row.get("ProductId") or row.get("productId") or "")
            if status.upper() in {"OK", "SUCCESS"} and pid:
                items.append({
                    "market": "naver",
                    "dt": dt,
                    "file": str(summary),
                    "account": "A",
                    "gs": gs,
                    "productId": pid,
                    "name": str(row.get("Name") or row.get("name") or ""),
                })
    unique: dict[tuple[str, str], dict[str, Any]] = {}
    for item in items:
        unique[(item["gs"], item["productId"])] = item
    return list(unique.values())


def recursive_vendor_item_ids(value: Any) -> list[str]:
    ids: list[str] = []
    if isinstance(value, dict):
        if "vendorItemId" in value and value["vendorItemId"]:
            ids.append(str(value["vendorItemId"]))
        for child in value.values():
            ids.extend(recursive_vendor_item_ids(child))
    elif isinstance(value, list):
        for child in value:
            ids.extend(recursive_vendor_item_ids(child))
    seen: set[str] = set()
    out: list[str] = []
    for item in ids:
        if item not in seen:
            seen.add(item)
            out.append(item)
    return out


def group_duplicates(items: list[dict[str, Any]], *, per_account: bool = False) -> dict[tuple[str, str], list[dict[str, Any]]]:
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for item in items:
        account_key = item.get("account", "") if per_account else ""
        grouped[(account_key, item["gs"])].append(item)
    return {
        key: sorted(values, key=lambda x: (x["dt"], x["productId"]), reverse=True)
        for key, values in grouped.items()
        if len({v["productId"] for v in values}) > 1
    }


def coupang_status_name(parsed: Any) -> str:
    if isinstance(parsed, dict):
        data = parsed.get("data")
        if isinstance(data, dict):
            return str(data.get("statusName") or data.get("sellerProductStatus") or "")
        return str(parsed.get("statusName") or parsed.get("sellerProductStatus") or "")
    return ""


def is_coupang_deleted_status(parsed: Any) -> bool:
    status_name = coupang_status_name(parsed)
    return "삭제" in status_name


def cleanup_coupang() -> list[dict[str, Any]]:
    clients = {
        "A": CoupangClient(KEY_ROOT / "coupang_wing_api.txt"),
        "B": CoupangClient(KEY_ROOT / "coupang_api_junbi.txt"),
    }
    report: list[dict[str, Any]] = []
    for (_account_key, gs), candidates in group_duplicates(collect_coupang_successes(), per_account=True).items():
        existing: list[dict[str, Any]] = []
        for item in candidates:
            account = item.get("account") or "A"
            client = clients.get(account)
            if client is None:
                item["exists"] = False
                item["checkStatus"] = "NO_CLIENT"
                report.append({**item, "action": "CHECK_FAILED", "reason": "no client for account"})
                continue
            ok, status, text, parsed = client.get_product(item["productId"])
            time.sleep(0.12)
            if ok and not is_coupang_deleted_status(parsed):
                item["parsed"] = parsed
                existing.append(item)
                report.append({**strip_runtime(item), "action": "EXISTS", "checkStatus": status})
            elif ok:
                report.append({
                    **strip_runtime(item),
                    "action": "ALREADY_DELETED",
                    "checkStatus": status,
                    "statusName": coupang_status_name(parsed),
                })
            else:
                report.append({**strip_runtime(item), "action": "MISSING_OR_INACCESSIBLE", "checkStatus": status, "body": short(text)})
        if not existing:
            continue
        keep = existing[0]
        report.append({**strip_runtime(keep), "action": "KEEP_NEWEST"})
        for item in existing[1:]:
            client = clients[item.get("account") or "A"]
            vendor_ids = recursive_vendor_item_ids(item.get("parsed"))
            stop_results = []
            for vendor_id in vendor_ids:
                ok, status, text = client.stop_vendor_item(vendor_id)
                stop_results.append({"vendorItemId": vendor_id, "ok": ok, "status": status, "body": short(text)})
                time.sleep(0.15)
            ok, status, text = client.delete_product(item["productId"])
            time.sleep(0.2)
            report.append({
                **strip_runtime(item),
                "action": "STOP_THEN_DELETE",
                "keptProductId": keep["productId"],
                "vendorItemCount": len(vendor_ids),
                "stopOk": sum(1 for r in stop_results if r["ok"]),
                "stopFailed": sum(1 for r in stop_results if not r["ok"]),
                "deleteOk": ok,
                "deleteStatus": status,
                "deleteBody": short(text),
                "stopResults": stop_results,
            })
    return report


def cleanup_naver() -> list[dict[str, Any]]:
    client = NaverClient(KEY_ROOT / "naver_client_key.txt")
    report: list[dict[str, Any]] = []
    for (_account_key, gs), candidates in group_duplicates(collect_naver_successes(), per_account=True).items():
        existing: list[dict[str, Any]] = []
        for item in candidates:
            ok, status, text, parsed = client.get_origin_product(item["productId"])
            time.sleep(0.12)
            if ok:
                item["parsed"] = parsed
                existing.append(item)
                report.append({**strip_runtime(item), "action": "EXISTS", "checkStatus": status})
            else:
                report.append({**strip_runtime(item), "action": "MISSING_OR_INACCESSIBLE", "checkStatus": status, "body": short(text)})
        if not existing:
            continue
        keep = existing[0]
        report.append({**strip_runtime(keep), "action": "KEEP_NEWEST"})
        for item in existing[1:]:
            ok, status, text = client.delete_origin_product(item["productId"])
            time.sleep(0.2)
            report.append({
                **strip_runtime(item),
                "action": "DELETE_ORIGIN_PRODUCT",
                "keptProductId": keep["productId"],
                "deleteOk": ok,
                "deleteStatus": status,
                "deleteBody": short(text),
            })
    return report


def strip_runtime(item: dict[str, Any]) -> dict[str, Any]:
    return {
        "account": item.get("account", ""),
        "gs": item.get("gs", ""),
        "productId": item.get("productId", ""),
        "uploadedAt": item.get("dt").strftime("%Y-%m-%d %H:%M:%S") if isinstance(item.get("dt"), datetime) else "",
        "name": item.get("name", ""),
        "sourceFile": item.get("file", ""),
    }


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    flat_rows = []
    for row in rows:
        flat = {k: (json.dumps(v, ensure_ascii=False) if isinstance(v, (list, dict)) else v) for k, v in row.items()}
        flat_rows.append(flat)
    fields: list[str] = []
    for row in flat_rows:
        for key in row:
            if key not in fields:
                fields.append(key)
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(flat_rows)


def main() -> None:
    stamp = NOW.strftime("%Y%m%d_%H%M%S")
    print("Collecting and cleaning Coupang duplicates...")
    coupang_report = cleanup_coupang()
    coupang_json = JOBS_ROOT / f"coupang_duplicate_cleanup_{stamp}.json"
    coupang_csv = JOBS_ROOT / f"coupang_duplicate_cleanup_{stamp}.csv"
    write_json(coupang_json, coupang_report)
    write_csv(coupang_csv, coupang_report)
    print(f"Coupang report: {coupang_json}")

    print("Collecting and cleaning Naver duplicates...")
    naver_report = cleanup_naver()
    naver_json = JOBS_ROOT / f"naver_duplicate_cleanup_{stamp}.json"
    naver_csv = JOBS_ROOT / f"naver_duplicate_cleanup_{stamp}.csv"
    write_json(naver_json, naver_report)
    write_csv(naver_csv, naver_report)
    print(f"Naver report: {naver_json}")

    def count(rows: list[dict[str, Any]], action: str, ok_field: str | None = None) -> int:
        selected = [row for row in rows if row.get("action") == action]
        if ok_field:
            selected = [row for row in selected if row.get(ok_field) is True]
        return len(selected)

    print("SUMMARY")
    print("coupang keep", count(coupang_report, "KEEP_NEWEST"))
    print("coupang delete ok", count(coupang_report, "STOP_THEN_DELETE", "deleteOk"))
    print("coupang delete attempted", count(coupang_report, "STOP_THEN_DELETE"))
    print("naver keep", count(naver_report, "KEEP_NEWEST"))
    print("naver delete ok", count(naver_report, "DELETE_ORIGIN_PRODUCT", "deleteOk"))
    print("naver delete attempted", count(naver_report, "DELETE_ORIGIN_PRODUCT"))


if __name__ == "__main__":
    requests.packages.urllib3.disable_warnings()
    main()
