"""Small helpers for the WEBOCR marketplace category matching SQLite DB."""

from __future__ import annotations

import sqlite3
from datetime import datetime
from pathlib import Path
from typing import Any


DEFAULT_DB_PATH = (
    Path(__file__).resolve().parents[1] / "data" / "category" / "category_matching.db"
)


def now_text() -> str:
    return datetime.now().isoformat(timespec="seconds")


def connect(db_path: str | Path = DEFAULT_DB_PATH) -> sqlite3.Connection:
    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row
    return conn


def rows_to_dicts(rows: list[sqlite3.Row]) -> list[dict[str, Any]]:
    return [dict(row) for row in rows]


def search_categories(
    market: str,
    query: str,
    *,
    limit: int = 30,
    category_type: str | None = None,
    include_excluded: bool = False,
    db_path: str | Path = DEFAULT_DB_PATH,
) -> list[dict[str, Any]]:
    """Search one marketplace category catalog for review UI.

    Results prefer active leaf categories, then non-excluded paths, then shorter
    category paths. This keeps manual search usable without hiding fallback rows.
    """
    terms = [term.strip().lower() for term in query.replace(">", " ").split() if term.strip()]
    if not terms:
        return []

    where = ["market = ?", "active = 1"]
    params: list[Any] = [market]
    if category_type:
        where.append("category_type = ?")
        params.append(category_type)
    if not include_excluded:
        where.append("excluded = 0")
    for term in terms:
        where.append("search_text LIKE ?")
        params.append(f"%{term}%")

    params.append(limit)
    sql = f"""
        SELECT market, category_type, category_id, category_name, category_path,
               leaf_name, depth, is_leaf, excluded, source_file
        FROM category_catalog
        WHERE {' AND '.join(where)}
        ORDER BY is_leaf DESC, excluded ASC, depth ASC, LENGTH(category_path) ASC
        LIMIT ?
    """
    with connect(db_path) as conn:
        return rows_to_dicts(conn.execute(sql, params).fetchall())


def get_candidates(
    source_category_id: str,
    target_market: str,
    *,
    limit: int = 3,
    db_path: str | Path = DEFAULT_DB_PATH,
) -> list[dict[str, Any]]:
    """Return top candidate categories for one Naver category and target market."""
    with connect(db_path) as conn:
        return rows_to_dicts(
            conn.execute(
                """
                WITH ranked AS (
                    SELECT
                        source_market, source_category_id, source_category_path, source_leaf,
                        target_market, target_category_id, target_category_path, target_leaf,
                        candidate_rank, confidence_score, match_method, relation_type,
                        review_status, notes, source_file,
                        ROW_NUMBER() OVER (
                            PARTITION BY target_category_path
                            ORDER BY
                              confidence_score DESC,
                              candidate_rank ASC,
                              CASE review_status
                                WHEN 'auto_ready' THEN 0
                                WHEN 'review_priority' THEN 1
                                ELSE 2
                              END
                        ) AS duplicate_rank
                    FROM category_mapping_candidates
                    WHERE source_market = 'naver'
                      AND source_category_id = ?
                      AND target_market = ?
                      AND target_category_path NOT LIKE '%어린이%'
                      AND target_category_path NOT LIKE '%유아%'
                      AND target_category_path NOT LIKE '%영유아%'
                      AND target_category_path NOT LIKE '%아동%'
                      AND target_category_path NOT LIKE '%키즈%'
                      AND target_category_path NOT LIKE '%베이비%'
                      AND target_category_path NOT LIKE '%주니어%'
                      AND target_category_path NOT LIKE '%완구%'
                      AND target_category_path NOT LIKE '%브랜드%'
                      AND target_category_path NOT LIKE '%해외직구%'
                      AND target_category_path NOT LIKE '%직구%'
                      AND target_category_path NOT LIKE '%도서%'
                      AND target_category_path NOT LIKE '%서적%'
                      AND target_category_path NOT LIKE '%음반%'
                      AND target_category_path NOT LIKE '%DVD%'
                      AND target_category_path NOT LIKE '%블루레이%'
                )
                SELECT source_market, source_category_id, source_category_path, source_leaf,
                       target_market, target_category_id, target_category_path, target_leaf,
                       candidate_rank, confidence_score, match_method, relation_type,
                       review_status, notes, source_file
                FROM ranked
                WHERE duplicate_rank = 1
                ORDER BY
                  CASE review_status
                    WHEN 'auto_ready' THEN 0
                    WHEN 'review_priority' THEN 1
                    ELSE 2
                  END,
                  confidence_score DESC,
                  candidate_rank ASC
                LIMIT ?
                """,
                (source_category_id, target_market, limit),
            ).fetchall()
        )


def get_approved_mapping(
    source_category_id: str,
    target_market: str,
    *,
    keyword_signature: str = "",
    product_group: str = "",
    db_path: str | Path = DEFAULT_DB_PATH,
) -> dict[str, Any] | None:
    """Return the saved manual approval that should win before auto candidates."""
    with connect(db_path) as conn:
        row = conn.execute(
            """
            SELECT *
            FROM category_mapping_rules
            WHERE source_market = 'naver'
              AND source_category_id = ?
              AND target_market = ?
              AND approved = 1
              AND active = 1
              AND (keyword_signature = ? OR keyword_signature = '')
              AND (product_group = ? OR product_group = '')
            ORDER BY
              CASE WHEN keyword_signature = ? THEN 0 ELSE 1 END,
              CASE WHEN product_group = ? THEN 0 ELSE 1 END,
              usage_count DESC,
              updated_at DESC
            LIMIT 1
            """,
            (
                source_category_id,
                target_market,
                keyword_signature,
                product_group,
                keyword_signature,
                product_group,
            ),
        ).fetchone()
    return dict(row) if row else None


# 롯데ON 표준(BC) ↔ 전시(FC/EC) 검증 페어 시드.
# 기존 LotteOnUploadService.cs / infer_direct_upload_categories 하드코딩에서 이전.
LOTTEON_SEED_PAIRS: tuple[tuple[str, str, str], ...] = (
    ("BC10080200", "FC19040401", "38"),
    ("BC04071100", "FC17101800", "38"),
    ("BC37101000", "FC02061010", "38"),
    ("BC41081502", "FC08120400", "38"),
    ("BC55040500", "FC03040405", "38"),
    ("BC66120200", "FC08071202", "38"),
    ("BC10040800", "FC19041003", "04"),
    ("BC43071000", "FC18101001", "38"),
    ("BC63120300", "FC11160703", "38"),
    ("BC20040800", "EC10400324", "38"),
)


def ensure_lotteon_pairs(db_path: str | Path = DEFAULT_DB_PATH) -> None:
    """Create the LotteON standard↔display pair table and seed verified pairs."""
    current_time = now_text()
    with connect(db_path) as conn:
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS lotteon_category_pairs (
                standard_code TEXT PRIMARY KEY,
                display_code TEXT NOT NULL,
                item_code TEXT NOT NULL DEFAULT '38',
                source TEXT NOT NULL DEFAULT 'seed',
                success_count INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )
            """
        )
        for standard, display, item in LOTTEON_SEED_PAIRS:
            conn.execute(
                """
                INSERT OR IGNORE INTO lotteon_category_pairs
                    (standard_code, display_code, item_code, source, success_count, created_at, updated_at)
                VALUES (?, ?, ?, 'seed', 0, ?, ?)
                """,
                (standard, display, item, current_time, current_time),
            )
        conn.commit()


def get_lotteon_pair(standard_code: str, *, db_path: str | Path = DEFAULT_DB_PATH) -> dict[str, Any] | None:
    """Return the display/item codes paired with a LotteON standard category."""
    if not standard_code:
        return None
    ensure_lotteon_pairs(db_path)
    with connect(db_path) as conn:
        row = conn.execute(
            "SELECT * FROM lotteon_category_pairs WHERE standard_code = ?",
            (standard_code,),
        ).fetchone()
    return dict(row) if row else None


def record_lotteon_pair(
    standard_code: str,
    display_code: str,
    item_code: str = "38",
    *,
    source: str = "upload_success",
    db_path: str | Path = DEFAULT_DB_PATH,
) -> dict[str, Any] | None:
    """Save (or reinforce) a LotteON pair after a successful upload."""
    if not standard_code or not display_code:
        return None
    ensure_lotteon_pairs(db_path)
    current_time = now_text()
    with connect(db_path) as conn:
        conn.execute(
            """
            INSERT INTO lotteon_category_pairs
                (standard_code, display_code, item_code, source, success_count, created_at, updated_at)
            VALUES (?, ?, ?, ?, 1, ?, ?)
            ON CONFLICT(standard_code) DO UPDATE SET
                display_code = excluded.display_code,
                item_code = excluded.item_code,
                source = excluded.source,
                success_count = lotteon_category_pairs.success_count + 1,
                updated_at = excluded.updated_at
            """,
            (standard_code, display_code, item_code or "38", source, current_time, current_time),
        )
        conn.commit()
    return get_lotteon_pair(standard_code, db_path=db_path)


def approve_mapping(
    *,
    source_category_id: str,
    target_market: str,
    target_category_id: str,
    source_category_path: str = "",
    source_leaf: str = "",
    target_category_path: str = "",
    target_leaf: str = "",
    keyword_signature: str = "",
    product_group: str = "",
    approved_by: str = "user",
    notes: str = "",
    db_path: str | Path = DEFAULT_DB_PATH,
) -> dict[str, Any]:
    """Save a user's category approval and return the stored rule."""
    current_time = now_text()
    with connect(db_path) as conn:
        if not source_category_path or not source_leaf:
            source = conn.execute(
                """
                SELECT category_path, leaf_name
                FROM category_catalog
                WHERE market = 'naver' AND category_id = ?
                LIMIT 1
                """,
                (source_category_id,),
            ).fetchone()
            if source:
                source_category_path = source_category_path or source["category_path"]
                source_leaf = source_leaf or source["leaf_name"]

        if not target_category_path or not target_leaf:
            target = conn.execute(
                """
                SELECT category_path, leaf_name
                FROM category_catalog
                WHERE market = ? AND category_id = ?
                ORDER BY is_leaf DESC, active DESC
                LIMIT 1
                """,
                (target_market, target_category_id),
            ).fetchone()
            if target:
                target_category_path = target_category_path or target["category_path"]
                target_leaf = target_leaf or target["leaf_name"]

        conn.execute(
            """
            INSERT INTO category_mapping_rules (
                source_market, source_category_id, source_category_path, source_leaf,
                target_market, target_category_id, target_category_path, target_leaf,
                rule_scope, keyword_signature, product_group, confidence_score,
                source, approved, approved_by, approved_at, active, notes,
                created_at, updated_at
            ) VALUES (
                'naver', ?, ?, ?, ?, ?, ?, ?,
                'naver_category', ?, ?, 1.0,
                'manual', 1, ?, ?, 1, ?, ?, ?
            )
            ON CONFLICT(source_market, source_category_id, target_market, keyword_signature, product_group, active)
            DO UPDATE SET
                target_category_id = excluded.target_category_id,
                target_category_path = excluded.target_category_path,
                target_leaf = excluded.target_leaf,
                source = 'manual',
                approved = 1,
                approved_by = excluded.approved_by,
                approved_at = excluded.approved_at,
                notes = excluded.notes,
                updated_at = excluded.updated_at
            """,
            (
                source_category_id,
                source_category_path,
                source_leaf,
                target_market,
                target_category_id,
                target_category_path,
                target_leaf,
                keyword_signature,
                product_group,
                approved_by,
                current_time,
                notes,
                current_time,
                current_time,
            ),
        )
        conn.commit()
        stored = get_approved_mapping(
            source_category_id,
            target_market,
            keyword_signature=keyword_signature,
            product_group=product_group,
            db_path=db_path,
        )
    if stored is None:
        raise RuntimeError("approval was not stored")
    return stored
