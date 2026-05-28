from __future__ import annotations

import argparse
import base64
import json
import re
import time
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any

import bcrypt
import requests


API_BASE = "https://api.commerce.naver.com/external"


def text(value: Any) -> str:
    return "" if value is None else str(value).strip()


def read_key_file(path: Path) -> dict[str, str]:
    data: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        data[key.strip()] = value.strip().strip("\"'")
    return data


class NaverClient:
    def __init__(self, key_file: Path):
        keys = read_key_file(key_file)
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
        resp = self.session.post(
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
        resp.raise_for_status()
        payload = resp.json()
        self.token = payload["access_token"]
        self.token_until = datetime.now() + timedelta(seconds=int(payload.get("expires_in", 600)) - 60)
        return self.token

    def request(self, method: str, path: str, **kwargs: Any) -> requests.Response:
        headers = kwargs.pop("headers", {})
        headers["Authorization"] = f"Bearer {self.access_token()}"
        headers.setdefault("Content-Type", "application/json")
        resp = self.session.request(method, f"{API_BASE}{path}", headers=headers, timeout=45, **kwargs)
        for delay in (2, 5, 10):
            if resp.status_code != 429:
                break
            time.sleep(delay)
            resp = self.session.request(method, f"{API_BASE}{path}", headers=headers, timeout=45, **kwargs)
        return resp


def latest_naver_products(log_root: Path) -> dict[str, dict[str, Any]]:
    out: dict[str, dict[str, Any]] = {}
    for response_path in sorted((log_root / "naver_upload").glob("*/responses/*.json")):
        gs = response_path.stem.replace("_retry", "").upper()
        if not re.match(r"^GS\d+[A-Z]?$", gs):
            continue
        try:
            payload = json.loads(response_path.read_text(encoding="utf-8-sig"))
        except Exception:
            continue
        origin_no = text(payload.get("originProductNo"))
        channel_no = text(payload.get("smartstoreChannelProductNo"))
        if origin_no:
            out[gs] = {
                "gs": gs,
                "originProductNo": origin_no,
                "smartstoreChannelProductNo": channel_no,
                "responsePath": str(response_path),
            }
    return out


def load_targets(report_path: Path, log_root: Path) -> list[dict[str, Any]]:
    report = json.loads(report_path.read_text(encoding="utf-8"))
    products = latest_naver_products(log_root)
    targets: list[dict[str, Any]] = []
    for row in report:
        gs = text(row.get("gs")).upper()
        current = row.get("currentCategories") if isinstance(row.get("currentCategories"), dict) else {}
        naver = row.get("naver") if isinstance(row.get("naver"), dict) else {}
        before = text(current.get("naver"))
        after = text(naver.get("id"))
        if not after or before == after:
            continue
        product = products.get(gs)
        if not product:
            targets.append({
                "gs": gs,
                "beforeCategory": before,
                "afterCategory": after,
                "afterPath": text(naver.get("path")),
                "ok": False,
                "status": "NO_ORIGIN_PRODUCT_NO",
            })
            continue
        targets.append({
            **product,
            "beforeCategory": before,
            "afterCategory": after,
            "afterPath": text(naver.get("path")),
            "query": text(row.get("naverApiQuery")),
            "name": text(row.get("sourceName")),
        })
    return targets


def update_one(client: NaverClient, target: dict[str, Any]) -> dict[str, Any]:
    if target.get("status") == "NO_ORIGIN_PRODUCT_NO":
        return target
    origin_no = text(target.get("originProductNo"))
    result = dict(target)
    get_resp = client.request("GET", f"/v2/products/origin-products/{origin_no}")
    result["getStatus"] = get_resp.status_code
    if get_resp.status_code != 200:
        result["ok"] = False
        result["status"] = "GET_FAILED"
        result["message"] = get_resp.text[:1000]
        return result
    payload = get_resp.json()
    origin = payload.get("originProduct") if isinstance(payload.get("originProduct"), dict) else None
    if not origin:
        result["ok"] = False
        result["status"] = "NO_ORIGIN_PRODUCT"
        result["message"] = json.dumps(payload, ensure_ascii=False)[:1000]
        return result
    result["liveBeforeCategory"] = text(origin.get("leafCategoryId"))
    if result["liveBeforeCategory"] == text(target.get("afterCategory")):
        result["ok"] = True
        result["status"] = "ALREADY_OK"
        result["liveAfterCategory"] = result["liveBeforeCategory"]
        return result
    origin["leafCategoryId"] = text(target.get("afterCategory"))
    put_resp = client.request("PUT", f"/v2/products/origin-products/{origin_no}", data=json.dumps(payload, ensure_ascii=False).encode("utf-8"))
    result["putStatus"] = put_resp.status_code
    result["ok"] = put_resp.status_code in {200, 201, 202}
    result["status"] = "OK" if result["ok"] else "PUT_FAILED"
    result["message"] = put_resp.text[:1000]
    if result["ok"]:
        verify = client.request("GET", f"/v2/products/origin-products/{origin_no}")
        result["verifyStatus"] = verify.status_code
        if verify.status_code == 200:
            verify_payload = verify.json()
            verify_origin = verify_payload.get("originProduct") if isinstance(verify_payload.get("originProduct"), dict) else {}
            result["liveAfterCategory"] = text(verify_origin.get("leafCategoryId"))
            result["ok"] = result["liveAfterCategory"] == text(target.get("afterCategory"))
            if not result["ok"]:
                result["status"] = "VERIFY_MISMATCH"
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--key-file", default=r"C:\Users\rkghr\Desktop\key\naver_client_key.txt", type=Path)
    parser.add_argument("--log-root", default=r"C:\Users\rkghr\Desktop\WEBOCRV2_LOCAL\webocrcludev2\data\exports\logs", type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--limit", type=int, default=0)
    args = parser.parse_args()

    targets = load_targets(args.report, args.log_root)
    if args.limit:
        targets = targets[:args.limit]
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    out = args.out or Path(r"C:\Users\rkghr\Desktop\key\reports") / f"naver_live_category_update_{stamp}.json"
    client = NaverClient(args.key_file)
    results: list[dict[str, Any]] = []
    total = len(targets)
    for idx, target in enumerate(targets, 1):
        print(f"{idx}/{total} {target.get('gs')} {target.get('beforeCategory')} -> {target.get('afterCategory')}")
        try:
            results.append(update_one(client, target))
        except Exception as exc:
            failed = dict(target)
            failed["ok"] = False
            failed["status"] = "EXCEPTION"
            failed["message"] = str(exc)[:1000]
            results.append(failed)
        out.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
        time.sleep(0.2)
    summary = {
        "total": len(results),
        "ok": sum(1 for row in results if row.get("ok")),
        "failed": sum(1 for row in results if not row.get("ok")),
        "out": str(out),
    }
    print(json.dumps(summary, ensure_ascii=False))
    return 0 if summary["failed"] == 0 else 2


if __name__ == "__main__":
    raise SystemExit(main())
