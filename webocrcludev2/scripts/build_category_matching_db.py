"""Build the local marketplace category DB used by WEBOCR review flow.

The source files under Desktop/key/카테고리 are treated as import data. Runtime
review decisions should be stored in category_mapping_rules inside this DB.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import sqlite3
from datetime import datetime
from pathlib import Path
from typing import Any, Iterable


DEFAULT_CATEGORY_DIR = Path(r"C:\Users\rkghr\Desktop\key\카테고리")
DEFAULT_OUTPUT_DB = (
    Path(__file__).resolve().parents[1] / "data" / "category" / "category_matching.db"
)

PATH_SPLIT_RE = re.compile(r"\s*>\s*|/")
SPACE_RE = re.compile(r"\s+")
EXCLUDED_PATH_RE = re.compile(
    r"(어린이|유아|영유아|아동|키즈|베이비|주니어|완구|브랜드|해외직구|직구|도서|서적|책|음반|DVD|블루레이)",
    re.IGNORECASE,
)


def now_text() -> str:
    return datetime.now().isoformat(timespec="seconds")


def text(value: Any) -> str:
    if value is None:
        return ""
    value = str(value).strip()
    if value.lower() in {"nan", "none", "null"}:
        return ""
    return value


def normalize_space(value: Any) -> str:
    return SPACE_RE.sub(" ", text(value)).strip()


def normalize_path(value: Any) -> str:
    parts = [normalize_space(part) for part in PATH_SPLIT_RE.split(text(value)) if normalize_space(part)]
    return " > ".join(parts)


def leaf_name(path: str, fallback: Any = "") -> str:
    clean_path = normalize_path(path)
    if clean_path:
        return clean_path.split(" > ")[-1].strip()
    return normalize_space(fallback)


def to_int(value: Any, default: int = 0) -> int:
    try:
        if text(value) == "":
            return default
        return int(float(str(value)))
    except Exception:
        return default


def to_float(value: Any, default: float = 0.0) -> float:
    try:
        if text(value) == "":
            return default
        return float(str(value))
    except Exception:
        return default


def is_yes(value: Any) -> int:
    return 1 if text(value).upper() in {"Y", "YES", "TRUE", "1"} else 0


def stable_id(prefix: str, value: str) -> str:
    digest = hashlib.sha1(value.encode("utf-8", errors="ignore")).hexdigest()[:12]
    return f"{prefix}_{digest}"


def read_csv_rows(path: Path) -> list[dict[str, Any]]:
    for encoding in ("utf-8-sig", "cp949", "euc-kr", "utf-8"):
        try:
            with path.open("r", encoding=encoding, newline="") as handle:
                return list(csv.DictReader(handle))
        except UnicodeDecodeError:
            continue
    raise UnicodeDecodeError("csv", b"", 0, 1, f"Could not decode {path}")


def schema(conn: sqlite3.Connection) -> None:
    conn.executescript(
        """
        PRAGMA journal_mode=WAL;

        CREATE TABLE IF NOT EXISTS category_catalog (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            market TEXT NOT NULL,
            category_type TEXT NOT NULL DEFAULT '',
            category_id TEXT NOT NULL,
            category_name TEXT NOT NULL DEFAULT '',
            category_path TEXT NOT NULL,
            leaf_name TEXT NOT NULL DEFAULT '',
            parent_id TEXT NOT NULL DEFAULT '',
            depth INTEGER NOT NULL DEFAULT 0,
            is_leaf INTEGER NOT NULL DEFAULT 0,
            active INTEGER NOT NULL DEFAULT 1,
            excluded INTEGER NOT NULL DEFAULT 0,
            source_file TEXT NOT NULL DEFAULT '',
            source_updated_at TEXT NOT NULL DEFAULT '',
            search_text TEXT NOT NULL DEFAULT '',
            raw_json TEXT NOT NULL DEFAULT '',
            imported_at TEXT NOT NULL DEFAULT '',
            UNIQUE(market, category_type, category_id)
        );

        CREATE TABLE IF NOT EXISTS category_mapping_candidates (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            source_market TEXT NOT NULL DEFAULT 'naver',
            source_category_id TEXT NOT NULL,
            source_category_path TEXT NOT NULL DEFAULT '',
            source_leaf TEXT NOT NULL DEFAULT '',
            target_market TEXT NOT NULL,
            target_category_id TEXT NOT NULL,
            target_category_path TEXT NOT NULL DEFAULT '',
            target_leaf TEXT NOT NULL DEFAULT '',
            candidate_rank INTEGER NOT NULL DEFAULT 1,
            confidence_score REAL NOT NULL DEFAULT 0,
            match_method TEXT NOT NULL DEFAULT '',
            relation_type TEXT NOT NULL DEFAULT '',
            review_status TEXT NOT NULL DEFAULT '',
            notes TEXT NOT NULL DEFAULT '',
            source_file TEXT NOT NULL DEFAULT '',
            imported_at TEXT NOT NULL DEFAULT '',
            UNIQUE(source_market, source_category_id, target_market, target_category_id, candidate_rank, source_file)
        );

        CREATE TABLE IF NOT EXISTS category_mapping_rules (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            source_market TEXT NOT NULL DEFAULT 'naver',
            source_category_id TEXT NOT NULL,
            source_category_path TEXT NOT NULL DEFAULT '',
            source_leaf TEXT NOT NULL DEFAULT '',
            target_market TEXT NOT NULL,
            target_category_id TEXT NOT NULL,
            target_category_path TEXT NOT NULL DEFAULT '',
            target_leaf TEXT NOT NULL DEFAULT '',
            rule_scope TEXT NOT NULL DEFAULT 'naver_category',
            keyword_signature TEXT NOT NULL DEFAULT '',
            product_group TEXT NOT NULL DEFAULT '',
            confidence_score REAL NOT NULL DEFAULT 1.0,
            source TEXT NOT NULL DEFAULT 'manual',
            approved INTEGER NOT NULL DEFAULT 1,
            approved_by TEXT NOT NULL DEFAULT 'user',
            approved_at TEXT NOT NULL DEFAULT '',
            usage_count INTEGER NOT NULL DEFAULT 0,
            last_used_at TEXT NOT NULL DEFAULT '',
            active INTEGER NOT NULL DEFAULT 1,
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL DEFAULT '',
            updated_at TEXT NOT NULL DEFAULT '',
            UNIQUE(
                source_market,
                source_category_id,
                target_market,
                keyword_signature,
                product_group,
                active
            )
        );

        CREATE TABLE IF NOT EXISTS category_import_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            source_file TEXT NOT NULL,
            imported_rows INTEGER NOT NULL DEFAULT 0,
            skipped_rows INTEGER NOT NULL DEFAULT 0,
            imported_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_category_catalog_market_path
            ON category_catalog(market, category_type, category_path);
        CREATE INDEX IF NOT EXISTS idx_category_catalog_search
            ON category_catalog(market, active, is_leaf, excluded, search_text);
        CREATE INDEX IF NOT EXISTS idx_mapping_candidates_source
            ON category_mapping_candidates(source_category_id, target_market, confidence_score DESC);
        CREATE INDEX IF NOT EXISTS idx_mapping_rules_lookup
            ON category_mapping_rules(source_category_id, target_market, active, approved);
        """
    )


def reset_data(conn: sqlite3.Connection) -> None:
    conn.executescript(
        """
        DELETE FROM category_catalog;
        DELETE FROM category_mapping_candidates;
        DELETE FROM category_import_log;
        """
    )


def insert_catalog(conn: sqlite3.Connection, row: dict[str, Any]) -> None:
    path = normalize_path(row.get("category_path") or row.get("full_path"))
    category_name = normalize_space(row.get("category_name")) or leaf_name(path)
    category_id = text(row.get("category_id") or row.get("category_code"))
    if not category_id and path:
        category_id = stable_id(row["market"], path)
    if not category_id or not path:
        raise ValueError("category_id/category_path missing")

    clean_leaf = leaf_name(path, category_name)
    raw_json = json.dumps(row.get("raw", row), ensure_ascii=False, default=str)
    market = text(row["market"])
    category_type = text(row.get("category_type"))
    active = to_int(row.get("active"), 1)
    excluded = 1 if EXCLUDED_PATH_RE.search(path) else 0
    search_text = " ".join(
        part
        for part in [
            category_id,
            category_name,
            clean_leaf,
            path,
            normalize_path(path).replace(" > ", " "),
        ]
        if part
    ).lower()

    conn.execute(
        """
        INSERT OR REPLACE INTO category_catalog (
            market, category_type, category_id, category_name, category_path,
            leaf_name, parent_id, depth, is_leaf, active, excluded, source_file,
            source_updated_at, search_text, raw_json, imported_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            market,
            category_type,
            category_id,
            category_name,
            path,
            clean_leaf,
            text(row.get("parent_id") or row.get("parent_code")),
            to_int(row.get("depth")),
            to_int(row.get("is_leaf")),
            active,
            excluded,
            text(row.get("source_file")),
            text(row.get("source_updated_at")),
            search_text,
            raw_json,
            now_text(),
        ),
    )


def import_catalog_file(conn: sqlite3.Connection, path: Path, market: str, category_type: str = "") -> tuple[int, int]:
    rows = read_csv_rows(path)
    imported = 0
    skipped = 0

    for raw in rows:
        try:
            row = dict(raw)
            row["raw"] = raw
            row["market"] = market
            row["category_type"] = category_type or text(raw.get("category_type"))
            row["source_file"] = path.name
            row["source_updated_at"] = datetime.fromtimestamp(path.stat().st_mtime).isoformat(timespec="seconds")

            if path.name == "naver_categories.csv":
                row["category_id"] = raw.get("category_code")
                row["category_path"] = raw.get("full_path")
                row["is_leaf"] = is_yes(raw.get("is_leaf"))
            elif path.name == "11st_categories.csv":
                row["is_leaf"] = is_yes(raw.get("leaf_yn"))
                row["active"] = 1
            elif path.name.startswith("lotteon"):
                row["is_leaf"] = is_yes(raw.get("leaf_yn"))
                row["active"] = 0 if text(raw.get("use_yn")).upper() == "N" else 1
            elif path.name == "coupang_categories.csv":
                row["is_leaf"] = 1
                row["active"] = 1
            elif path.name == "auction_categories.csv":
                row["is_leaf"] = 1 if text(raw.get("is_leaf")).lower() == "true" else 0
                row["active"] = 1 if text(raw.get("is_display")).lower() != "false" else 0

            insert_catalog(conn, row)
            imported += 1
        except Exception:
            skipped += 1

    conn.execute(
        "INSERT INTO category_import_log(source_file, imported_rows, skipped_rows, imported_at) VALUES (?, ?, ?, ?)",
        (path.name, imported, skipped, now_text()),
    )
    return imported, skipped


def import_esm_path_file(conn: sqlite3.Connection, path: Path) -> tuple[int, int]:
    rows = read_csv_rows(path)
    imported = 0
    skipped = 0
    seen: set[str] = set()
    for raw in rows:
        esm_path = normalize_path(raw.get("ESM 카테고리명"))
        if not esm_path or esm_path in seen:
            continue
        seen.add(esm_path)
        try:
            insert_catalog(
                conn,
                {
                    "market": "esm",
                    "category_type": "esm_path",
                    "category_id": stable_id("esm", esm_path),
                    "category_name": leaf_name(esm_path),
                    "category_path": esm_path,
                    "depth": len(esm_path.split(" > ")),
                    "is_leaf": 1,
                    "active": 1,
                    "source_file": path.name,
                    "source_updated_at": datetime.fromtimestamp(path.stat().st_mtime).isoformat(timespec="seconds"),
                    "raw": raw,
                }
            )
            imported += 1
        except Exception:
            skipped += 1
    conn.execute(
        "INSERT INTO category_import_log(source_file, imported_rows, skipped_rows, imported_at) VALUES (?, ?, ?, ?)",
        (path.name, imported, skipped, now_text()),
    )
    return imported, skipped


def insert_candidate(conn: sqlite3.Connection, row: dict[str, Any], source_file: str) -> None:
    source_id = text(row.get("source_category_id"))
    target_market = text(row.get("target_platform") or row.get("target_market"))
    target_id = text(row.get("target_category_id"))
    target_path = normalize_path(row.get("target_path") or row.get("target_category_path"))
    if not source_id or not target_market or not target_id or not target_path:
        raise ValueError("candidate key missing")

    source_path = normalize_path(row.get("source_path") or row.get("source_category_path"))
    target_leaf = leaf_name(target_path, row.get("target_leaf"))
    source_leaf = leaf_name(source_path, row.get("source_leaf"))
    rank = to_int(row.get("candidate_rank"), 1)
    confidence = to_float(row.get("confidence_score"))
    review_status = text(row.get("review_status"))
    if not review_status:
        if confidence >= 0.80:
            review_status = "auto_ready"
        elif confidence >= 0.55:
            review_status = "review_priority"
        else:
            review_status = "manual_search"

    conn.execute(
        """
        INSERT OR REPLACE INTO category_mapping_candidates (
            source_market, source_category_id, source_category_path, source_leaf,
            target_market, target_category_id, target_category_path, target_leaf,
            candidate_rank, confidence_score, match_method, relation_type,
            review_status, notes, source_file, imported_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            "naver",
            source_id,
            source_path,
            source_leaf,
            target_market,
            target_id,
            target_path,
            target_leaf,
            rank,
            confidence,
            text(row.get("match_method")),
            text(row.get("relation_type")),
            review_status,
            text(row.get("notes")),
            source_file,
            now_text(),
        ),
    )

    # Make ESM paths from the revised mapping workbook searchable even when
    # there is no standalone category catalog file with those codes.
    if target_market == "esm":
        insert_catalog(
            conn,
            {
                "market": "esm",
                "category_type": "mapping_result",
                "category_id": target_id,
                "category_name": target_leaf,
                "category_path": target_path,
                "depth": len(target_path.split(" > ")),
                "is_leaf": 1,
                "active": 1,
                "source_file": source_file,
                "raw": row,
            },
        )


def import_candidate_csv(conn: sqlite3.Connection, path: Path) -> tuple[int, int]:
    rows = read_csv_rows(path)
    imported = 0
    skipped = 0
    for raw in rows:
        try:
            insert_candidate(conn, raw, path.name)
            imported += 1
        except Exception:
            skipped += 1
    conn.execute(
        "INSERT INTO category_import_log(source_file, imported_rows, skipped_rows, imported_at) VALUES (?, ?, ?, ?)",
        (path.name, imported, skipped, now_text()),
    )
    return imported, skipped


def import_candidate_xlsx(conn: sqlite3.Connection, path: Path) -> tuple[int, int]:
    try:
        import pandas as pd
    except Exception as exc:
        print(f"SKIP {path.name}: pandas unavailable ({exc})")
        return 0, 0

    imported = 0
    skipped = 0
    try:
        sheets = pd.read_excel(path, sheet_name=None)
    except Exception as exc:
        print(f"SKIP {path.name}: read failed ({exc})")
        return 0, 0

    for sheet_name, df in sheets.items():
        if "source_category_id" not in df.columns or "target_category_id" not in df.columns:
            continue
        for raw in df.fillna("").to_dict(orient="records"):
            try:
                raw["candidate_rank"] = raw.get("candidate_rank") or 1
                insert_candidate(conn, raw, f"{path.name}:{sheet_name}")
                imported += 1
            except Exception:
                skipped += 1

    conn.execute(
        "INSERT INTO category_import_log(source_file, imported_rows, skipped_rows, imported_at) VALUES (?, ?, ?, ?)",
        (path.name, imported, skipped, now_text()),
    )
    return imported, skipped


def create_seed_approval_rule(
    conn: sqlite3.Connection,
    source_category_id: str,
    target_market: str,
    target_category_id: str,
    note: str,
) -> None:
    row = conn.execute(
        """
        SELECT source_category_path, source_leaf, target_category_path, target_leaf, confidence_score
        FROM category_mapping_candidates
        WHERE source_category_id = ? AND target_market = ? AND target_category_id = ?
        ORDER BY confidence_score DESC
        LIMIT 1
        """,
        (source_category_id, target_market, target_category_id),
    ).fetchone()
    if not row:
        return
    conn.execute(
        """
        INSERT OR IGNORE INTO category_mapping_rules (
            source_market, source_category_id, source_category_path, source_leaf,
            target_market, target_category_id, target_category_path, target_leaf,
            rule_scope, confidence_score, source, approved, approved_by, approved_at,
            active, notes, created_at, updated_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            "naver",
            source_category_id,
            row["source_category_path"],
            row["source_leaf"],
            target_market,
            target_category_id,
            row["target_category_path"],
            row["target_leaf"],
            "naver_category",
            row["confidence_score"],
            "seed_import",
            0,
            "system",
            "",
            1,
            note,
            now_text(),
            now_text(),
        ),
    )


def print_summary(conn: sqlite3.Connection, db_path: Path) -> None:
    print(f"\nDB: {db_path}")
    print("\ncategory_catalog")
    for row in conn.execute(
        """
        SELECT market, category_type, COUNT(*) AS cnt,
               SUM(CASE WHEN is_leaf = 1 THEN 1 ELSE 0 END) AS leaf_cnt,
               SUM(CASE WHEN excluded = 1 THEN 1 ELSE 0 END) AS excluded_cnt
        FROM category_catalog
        GROUP BY market, category_type
        ORDER BY market, category_type
        """
    ):
        print(
            f"  {row['market']:<8} {row['category_type'] or '-':<16} "
            f"rows={row['cnt']:<6} leaf={row['leaf_cnt']:<6} excluded={row['excluded_cnt']}"
        )

    print("\ncategory_mapping_candidates")
    for row in conn.execute(
        """
        SELECT target_market, COUNT(*) AS cnt, COUNT(DISTINCT source_category_id) AS src_cnt
        FROM category_mapping_candidates
        GROUP BY target_market
        ORDER BY target_market
        """
    ):
        print(f"  {row['target_market']:<8} rows={row['cnt']:<7} naver_sources={row['src_cnt']}")

    rules = conn.execute("SELECT COUNT(*) AS cnt FROM category_mapping_rules").fetchone()["cnt"]
    print(f"\ncategory_mapping_rules rows={rules}")


def build_db(category_dir: Path, output_db: Path) -> None:
    output_db.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(str(output_db))
    conn.row_factory = sqlite3.Row
    try:
        schema(conn)
        reset_data(conn)

        catalog_specs = [
            ("naver_categories.csv", "naver", "standard"),
            ("coupang_categories.csv", "coupang", "standard"),
            ("11st_categories.csv", "11st", "standard"),
            ("auction_categories.csv", "esm", "auction"),
            ("lotteon_standard_categories.csv", "lotteon", "standard"),
            ("lotteon_display_categories.csv", "lotteon", "display"),
        ]

        for file_name, market, category_type in catalog_specs:
            path = category_dir / file_name
            if not path.exists():
                print(f"MISS {file_name}")
                continue
            imported, skipped = import_catalog_file(conn, path, market, category_type)
            print(f"CAT  {file_name}: imported={imported} skipped={skipped}")

        esm_path_file = category_dir / "esm_auction_gmarket_category_matching.csv"
        if esm_path_file.exists():
            imported, skipped = import_esm_path_file(conn, esm_path_file)
            print(f"CAT  {esm_path_file.name}: imported={imported} skipped={skipped}")

        candidate_csv = category_dir / "naver_category_crosswalk_candidates.csv"
        if candidate_csv.exists():
            imported, skipped = import_candidate_csv(conn, candidate_csv)
            print(f"CAND {candidate_csv.name}: imported={imported} skipped={skipped}")

        best_csv = category_dir / "naver_category_crosswalk_best.csv"
        if best_csv.exists():
            imported, skipped = import_candidate_csv(conn, best_csv)
            print(f"CAND {best_csv.name}: imported={imported} skipped={skipped}")

        result_xlsx = category_dir / "category_mapping_result.xlsx"
        if result_xlsx.exists():
            imported, skipped = import_candidate_xlsx(conn, result_xlsx)
            print(f"CAND {result_xlsx.name}: imported={imported} skipped={skipped}")

        conn.commit()
        print_summary(conn, output_db)
    finally:
        conn.close()


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Build WEBOCR marketplace category SQLite DB.")
    parser.add_argument("--category-dir", type=Path, default=DEFAULT_CATEGORY_DIR)
    parser.add_argument("--output-db", type=Path, default=DEFAULT_OUTPUT_DB)
    args = parser.parse_args(list(argv) if argv is not None else None)

    build_db(args.category_dir, args.output_db)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
