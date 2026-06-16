#!/usr/bin/env python3
"""롯데ON 표준(BC)→전시(FC) 카테고리 매핑을 카테고리 이름 대응으로 구축한다.

배경: 롯데ON 상품등록은 표준-전시 "맵핑된" 페어만 허용하는데, 그 매핑을 주는 공개 API/CSV가 없다.
관찰: 업로드 성공으로 검증된 페어 50쌍 중 40쌍이 표준 카테고리명 == 전시 카테고리명 (완전일치),
      나머지도 부분일치("렌치"↔"렌치/복스/몽키"). 롯데ON의 표준/전시 분류 체계가 서로 미러링됨.
방법: 표준 leaf 이름 → 동일 이름의 전시 leaf 로 매핑. 동명이 여러 개면 category_path 겹침이 큰 것을 택함.
      업로드 성공으로 검증된 기존 페어(source=upload_success)는 절대 덮어쓰지 않는다.

산출물: Desktop/key/lotteon_category_map.json (C# LotteOnUploadService.LoadVerifiedCategoryMap 가 읽음)
사용:   python build_lotteon_category_map.py            # 적용(파일 갱신)
        python build_lotteon_category_map.py --dry-run  # 통계만
"""
from __future__ import annotations
import csv, json, os, sys
from pathlib import Path

KEY_DIR = Path(os.path.expanduser("~")) / "Desktop" / "key"
CAT_DIR = KEY_DIR / "카테고리"
MAP_PATH = KEY_DIR / "lotteon_category_map.json"


def load_categories(filename: str) -> dict[str, dict]:
    out: dict[str, dict] = {}
    with open(CAT_DIR / filename, encoding="utf-8-sig", newline="") as fp:
        for row in csv.DictReader(fp):
            cid = (row.get("category_id") or "").strip()
            if not cid:
                continue
            out[cid] = {
                "name": (row.get("category_name") or "").strip(),
                "path": (row.get("category_path") or "").strip(),
                "leaf": (row.get("leaf_yn") or "").strip().upper(),
                "use": (row.get("use_yn") or "").strip().upper(),
            }
    return out


def path_overlap(a: str, b: str) -> int:
    """두 category_path의 공통 세그먼트(이름) 개수."""
    sa = {s.strip() for s in a.split(">") if s.strip()}
    sb = {s.strip() for s in b.split(">") if s.strip()}
    return len(sa & sb)


def build(dry_run: bool = False) -> None:
    std = load_categories("lotteon_standard_categories.csv")
    disp = load_categories("lotteon_display_categories.csv")

    # 전시: 이름 -> [(code, info)]. 셀러 등록은 FC 트리만 허용(EC는 거부됨)하므로 FC leaf 만 후보로.
    disp_by_name: dict[str, list[tuple[str, dict]]] = {}
    for code, info in disp.items():
        if info["name"] and code.startswith("FC") and info["leaf"] == "Y":
            disp_by_name.setdefault(info["name"], []).append((code, info))

    def pick_display(std_info: dict) -> tuple[str, str] | None:
        cands = disp_by_name.get(std_info["name"])
        if not cands:
            return None
        # 동명이 여러 개면 표준 경로와 겹치는 세그먼트가 많은 것(=같은 대분류 맥락) → 사용중 우선
        ranked = sorted(
            cands,
            key=lambda c: (path_overlap(std_info["path"], c[1]["path"]), c[1]["use"] == "Y"),
            reverse=True,
        )
        code = ranked[0][0]
        # 동명 FC가 유일하면 high(거의 확정), 여러 개면 path로 고른 것이라 mid
        kind = "unique" if len(cands) == 1 else "ambiguous_pathbest"
        return code, kind

    existing: dict = {}
    if MAP_PATH.exists():
        try:
            existing = json.loads(MAP_PATH.read_text(encoding="utf-8"))
        except Exception:
            existing = {}

    result = dict(existing)
    added = 0
    kept_success = 0
    skipped_no_match = 0
    for bc, info in std.items():
        if not bc.startswith("BC") or info["leaf"] != "Y":
            continue
        prev = existing.get(bc)
        # 검증된 성공 페어는 보존
        if isinstance(prev, dict) and prev.get("source") == "upload_success":
            kept_success += 1
            continue
        picked = pick_display(info)
        if not picked:
            skipped_no_match += 1
            continue
        fc, kind = picked
        if not (fc.startswith("FC") or fc.startswith("EC")):
            continue
        result[bc] = {"display": fc, "item": "38", "source": f"name_match:{kind}"}
        added += 1

    print(f"표준 leaf: {sum(1 for v in std.values() if v['leaf']=='Y')}")
    print(f"기존 맵: {len(existing)} (성공검증 보존 {kept_success})")
    print(f"이름매칭 추가/갱신: {added}, 매칭실패(전시 동명 없음): {skipped_no_match}")
    print(f"최종 맵 크기: {len(result)}")

    if dry_run:
        print("[dry-run] 파일 미저장")
        return
    MAP_PATH.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"저장 완료: {MAP_PATH}")


if __name__ == "__main__":
    build(dry_run="--dry-run" in sys.argv)
