#!/usr/bin/env python3
"""백업한 A:네이버 전체 상품 + 스마트스토어센터 판매엑셀 → 보존/삭제 분류표 생성 (읽기전용).

판매이력(=보존) 기준: 판매엑셀에 등장하는 상품(상품번호 또는 판매자상품코드/GS 또는 상품명 일치).
그 외 = 삭제 후보. 결과 CSV를 사람이 검수한 뒤 별도 삭제 스크립트로 실행한다.

사용:
  python naver_classify_delete.py --sales "C:\\Users\\rkghr\\Desktop\\판매엑셀.xlsx"
  (백업 JSON은 바탕화면 naver_A_products_backup_*.json 중 최신본 자동 사용)

판매엑셀은 아래 중 하나라도 열로 포함되면 매칭됨(우선순위 순):
  - 상품번호 / channelProductNo  (가장 정확)
  - 판매자상품코드 / 판매자 상품코드 / GS코드  (= sellerManagementCode)
  - 상품명  (최후수단, 정규화 후 일치)
"""
from __future__ import annotations
import argparse, csv, json, re, sys
import datetime as dt
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
DESKTOP = Path.home() / "Desktop"


def latest_backup() -> Path:
    cands = sorted(DESKTOP.glob("naver_A_products_backup_*.json"), reverse=True)
    if not cands:
        sys.exit("백업 JSON(naver_A_products_backup_*.json)을 바탕화면에서 찾지 못했습니다. 먼저 naver_backup_products.py 실행.")
    return cands[0]


def norm_name(s: str) -> str:
    s = re.sub(r"GS\d{5,9}[A-Z]?", "", str(s or ""), flags=re.IGNORECASE)
    return re.sub(r"[\s\W]+", "", s).lower()


def load_sales(path: Path) -> tuple[set, set, set]:
    """판매엑셀에서 (상품번호집합, GS집합, 정규화상품명집합) 추출."""
    rows: list[dict] = []
    if path.suffix.lower() in (".xlsx", ".xls"):
        try:
            import openpyxl
        except ImportError:
            sys.exit("openpyxl 필요: pip install openpyxl  (또는 csv로 저장해 주세요)")
        wb = openpyxl.load_workbook(path, read_only=True, data_only=True)
        ws = wb.active
        it = ws.iter_rows(values_only=True)
        header = [str(c or "").strip() for c in next(it)]
        for r in it:
            rows.append({header[i]: r[i] for i in range(min(len(header), len(r)))})
    else:
        with open(path, encoding="utf-8-sig", newline="") as fp:
            rows = list(csv.DictReader(fp))

    if not rows:
        sys.exit("판매엑셀에서 데이터를 읽지 못했습니다.")
    cols = list(rows[0].keys())
    print("판매엑셀 컬럼:", cols)

    def find(*names):
        for n in names:
            for c in cols:
                if n.replace(" ", "") in str(c).replace(" ", ""):
                    return c
        return None

    col_no = find("상품번호", "channelProductNo", "원상품번호", "originProductNo")
    col_gs = find("판매자상품코드", "판매자상품코드", "판매자코드", "GS코드", "sellerManagementCode")
    col_nm = find("상품명", "name")
    print(f"매칭컬럼 → 상품번호:{col_no} / 판매자코드:{col_gs} / 상품명:{col_nm}")

    sold_no, sold_gs, sold_nm = set(), set(), set()
    for r in rows:
        if col_no and r.get(col_no) not in (None, ""):
            sold_no.add(str(r[col_no]).strip().split(".")[0])
        if col_gs and r.get(col_gs):
            sold_gs.add(str(r[col_gs]).strip().upper())
        if col_nm and r.get(col_nm):
            sold_nm.add(norm_name(r[col_nm]))
    print(f"판매엑셀 보존키: 상품번호 {len(sold_no)} / GS {len(sold_gs)} / 상품명 {len(sold_nm)}")
    return sold_no, sold_gs, sold_nm


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--sales", required=True, help="스마트스토어센터 판매/통계 엑셀(.xlsx/.csv) 경로")
    ap.add_argument("--backup", help="백업 JSON 경로(기본: 바탕화면 최신본)")
    args = ap.parse_args()

    backup = Path(args.backup) if args.backup else latest_backup()
    sold_no, sold_gs, sold_nm = load_sales(Path(args.sales))
    contents = json.loads(backup.read_text(encoding="utf-8"))

    out_rows = []
    keep = delete = 0
    for c in contents:
        origin_no = str(c.get("originProductNo") or "")
        for ch in (c.get("channelProducts") or [{}]):
            ch_no = str(ch.get("channelProductNo") or "")
            gs = str(ch.get("sellerManagementCode") or "").upper()
            name = ch.get("name") or ""
            status = ch.get("statusType") or ""
            matched_by = ""
            if ch_no and ch_no in sold_no: matched_by = "상품번호"
            elif origin_no and origin_no in sold_no: matched_by = "원상품번호"
            elif gs and gs in sold_gs: matched_by = "판매자코드"
            elif name and norm_name(name) in sold_nm: matched_by = "상품명"
            decision = "KEEP(판매이력)" if matched_by else "DELETE(무판매)"
            if matched_by: keep += 1
            else: delete += 1
            out_rows.append({
                "decision": decision, "matched_by": matched_by,
                "originProductNo": origin_no, "channelProductNo": ch_no,
                "sellerManagementCode": gs, "statusType": status, "name": name,
            })

    today = dt.date.today().strftime("%Y%m%d")
    out_path = DESKTOP / f"naver_A_delete_plan_{today}.csv"
    with open(out_path, "w", encoding="utf-8-sig", newline="") as fp:
        w = csv.DictWriter(fp, fieldnames=list(out_rows[0].keys()))
        w.writeheader(); w.writerows(out_rows)

    from collections import Counter
    st_del = Counter(r["statusType"] for r in out_rows if r["decision"].startswith("DELETE"))
    print(f"\n분류 완료: 보존(KEEP) {keep} / 삭제후보(DELETE) {delete}")
    print(f"  삭제후보 상태분포: {dict(st_del)}")
    print(f"  검수표: {out_path}")
    print("  → CSV의 DELETE 행을 검수한 뒤, 삭제 실행 스크립트로 진행하세요.")


if __name__ == "__main__":
    main()
