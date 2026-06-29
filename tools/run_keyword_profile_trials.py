from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SEED = ROOT / "webocrcludev2" / "data" / "seeds" / "pm_source_20260615_024727_20260615_031532.webseed.json"
TRIAL_ROOT = ROOT / "webocrcludev2" / "data" / "keyword_profile_trials" / datetime.now().strftime("%Y%m%d_%H%M%S")
CHANNELS = ["A:네이버", "A:쿠팡", "A:롯데ON", "A:11번가", "A:ESM"]


TRIALS = [
    ("01_balanced", "균형 표준형", "정확도와 마켓별 차이를 균형 있게 둔다. 네이버는 정제, 쿠팡은 정보량, 롯데ON은 표준, 11번가는 동의어, ESM은 구매의도 중심."),
    ("02_search_intent", "검색의도 분리형", "각 마켓 첫 단어를 서로 다르게 만든다. 네이버=표준명, 쿠팡=문제해결, 롯데ON=생활공간, 11번가=동의어, ESM=작업상황."),
    ("03_market_native", "마켓 네이티브형", "마켓별 실제 업로드 감각을 강하게 둔다. 쿠팡은 가장 길고 실사용 정보량을 늘리며, 네이버/ESM은 제한 길이를 엄격히 지킨다."),
    ("04_synonym_rich", "동의어 확장형", "동의어·현장명·다른명칭을 적극 발굴한다. 단 상품명에는 과반복 없이 1~2개만 넣고 검색어에 넓게 분산한다."),
    ("05_conservative", "보수 검수형", "OCR/원본/네이버 API 근거가 약한 추론은 배제한다. 안정성과 금지어 제거를 최우선으로 둔다."),
    ("06_longtail", "롱테일 확장형", "상품명은 정확하게 유지하되 검색어에는 사용처·문제상황·구매상황 롱테일을 많이 넣는다."),
    ("07_category_anchor", "카테고리 앵커형", "네이버 앵커 카테고리와 상위 표현을 가장 강하게 반영한다. 상품군을 흔들지 않고 카테고리 적합도를 최우선으로 둔다."),
    ("08_mobile_front", "모바일 앞단 최적화형", "앞 25자 안에 핵심 품목과 구매 의도를 넣는다. 마켓별 앞 3단어 중복을 강하게 금지한다."),
    ("09_operator", "실무 작업자형", "실제 사용하는 사람과 작업 장면을 상품명 후반과 검색어에 반영한다. 공구/소품/생활용품에 강한 현장형."),
    ("10_low_dup", "저중복 엄격형", "같은 상품 내 5개 마켓 상품명을 나란히 비교해 토큰 중복과 시작어 중복을 가장 엄격히 줄인다."),
]


BASE_PROMPT = """너는 한국 오픈마켓 상품명/검색어 생성 담당자다.
현재 작업 폴더의 `keyword_input.json`을 읽고, 반드시 `keyword_result.json` 하나만 UTF-8 JSON으로 작성해라.
코드 파일이나 다른 파일은 수정하지 마라.

목표:
- 타인이 단일 계정으로 쓰는 WebOCR용 5개 마켓 키워드 셋을 만든다.
- 실제 생성 채널은 `A:네이버`, `A:쿠팡`, `A:롯데ON`, `A:11번가`, `A:ESM` 5개뿐이다.
- A/B 계정, B채널, 10채널 규칙은 이번 테스트에서 사용하지 않는다.
- 입력 상품마다 요청된 5개 채널의 `title`, `searchTerms`, `tags`, `notes`를 모두 새로 작성한다.
- 5개 마켓 결과는 같은 후보 풀을 공유하되 시작 단어와 강조축이 달라야 한다.

출력 JSON 스키마:
{
  "schema": "webocr.keyword.v1",
  "provider": "codex-cli",
  "products": [
    {
      "gs": "GS코드",
      "baseProductName": "기본상품명",
      "naverApiQuery": "네이버 API 기준 검색어",
      "channels": {
        "A:네이버": {"title": "상품명", "searchTerms": "쉼표구분검색어", "tags": ["태그"], "candidateCount": 12, "notes": "근거"}
      }
    }
  ]
}

공통 규칙:
- 상품 정체성은 OCR/원본/네이버 API 상위 결과가 함께 가리키는 실제 물건으로 확정한다.
- 색상, 수량, 소재, 사이즈, 호환 모델은 원본/OCR/API 근거가 확실할 때만 쓴다.
- 판매문구, 무료배송, 할인, 추천, 인기, 최저가, 문의, 배송, 타사 브랜드, 과장효능은 금지한다.
- 상품명은 자연어 띄어쓰기, 검색어와 태그는 공백 없이 붙여쓰기+쉼표 구분을 기본으로 한다.
- 같은 상품의 5개 title을 나란히 놓았을 때 첫 2~3단어가 전부 같으면 실패다.
- 상품명은 후보어 나열이 아니라 `정체성 + 기능 + 사용처 + 문제해결 + 현장명/동의어` 중 서로 다른 축을 조합한다.
- 중복 토큰을 줄인다. `고정/고정용`, `수리/수리용`, `교체/교체용` 같은 변형 중복도 하나만 쓴다.

마켓별 페르소나:
- A:네이버 = [정확 검수형] 표준 상품명과 대표 검색어를 앞에 둔다. 40~50자, tags 8~10개.
- A:쿠팡 = [정보량 확장형] 가장 길고 풍부하게 쓴다. 정체성, 기능, 규격, 사용처, 문제해결을 넓게 담는다. 55~75자, 검색어 18~25개.
- A:롯데ON = [표준 MD형] 네이버보다 조금 풍부하고 쿠팡보다 절제한다. 생활공간/사용맥락을 1개 이상 넣는다. 40~55자, 검색어 12~18개.
- A:11번가 = [동의어 균형형] 표준명과 현장명/동의어를 고르게 배치한다. 45~60자, 검색어 14~20개.
- A:ESM = [구매의도 전환형] 교체, 수리, 고정, 보호, 정리, 보관 같은 구매의도 단어를 1~2개 넣는다. 40~50자, 검색어 14~20개.

최종 검수:
1. 5개 채널이 모두 있는가?
2. 5개 title의 첫 3단어가 과도하게 같지 않은가?
3. 쿠팡이 가장 풍부한가?
4. 네이버/ESM이 너무 길지 않은가?
5. 11번가에 동의어/현장명이 충분한가?
6. 검색어는 각 마켓별로 복붙이 아니라 30% 이상 다르게 구성됐는가?
7. 금지어와 근거 없는 속성이 없는가?

이번 후보 셋의 추가 전략:
__TRIAL_STRATEGY__

최종 응답은 설명하지 말고 `keyword_result.json` 파일 작성만 끝내라.
"""


def slim_product(product: dict) -> dict:
    analysis = product.get("naverShoppingAnalysis") or {}
    return {
        "gs": product.get("gs"),
        "baseGs": product.get("baseGs"),
        "sourceName": product.get("sourceName"),
        "baseProductName": product.get("baseProductName"),
        "naverApiQuery": product.get("naverApiQuery"),
        "optionSummary": product.get("optionSummary"),
        "ocrPrimaryKeyword": product.get("ocrPrimaryKeyword"),
        "keywordCandidatePool": product.get("keywordCandidatePool"),
        "generatedKeywordSeed": product.get("generatedKeywordSeed"),
        "categories": product.get("categories"),
        "ocrRawText": ((product.get("ocrAnalysis") or {}).get("rawText") or "")[:1800],
        "naverShoppingAnalysis": {
            "status": analysis.get("status"),
            "query": analysis.get("query"),
            "anchor": analysis.get("anchor"),
            "topTitles": (analysis.get("topTitles") or [])[:12],
            "topTerms": (analysis.get("topTerms") or [])[:30],
            "topPhrases": (analysis.get("topPhrases") or [])[:20],
        },
    }


def title_tokens(title: str) -> list[str]:
    return [tok for tok in re.split(r"\s+", title.strip()) if tok]


def score_result(result: dict) -> dict:
    rows = []
    for product in result.get("products", []):
        channels = product.get("channels") or {}
        titles = {ch: (channels.get(ch) or {}).get("title", "") for ch in CHANNELS}
        starts = [" ".join(title_tokens(v)[:3]) for v in titles.values()]
        unique_starts = len(set(starts))
        lengths = {ch: len(v) for ch, v in titles.items()}
        coupang_longest = lengths.get("A:쿠팡", 0) == max(lengths.values() or [0])
        rows.append({
            "gs": product.get("gs"),
            "uniqueStarts": unique_starts,
            "coupangLongest": coupang_longest,
            "lengths": lengths,
            "titles": titles,
        })
    return {
        "avgUniqueStarts": round(sum(r["uniqueStarts"] for r in rows) / max(1, len(rows)), 2),
        "coupangLongestCount": sum(1 for r in rows if r["coupangLongest"]),
        "rows": rows,
    }


def write_report(summary: list[dict]) -> None:
    lines = [
        "# WebOCR 5마켓 키워드 셋 반복 테스트",
        "",
        f"- 생성시각: {datetime.now():%Y-%m-%d %H:%M:%S}",
        f"- 기준 seed: `{SEED.name}`",
        f"- 채널: {', '.join(CHANNELS)}",
        "",
        "## 후보 요약",
        "",
        "|후보|전략|평균 시작어 다양성|쿠팡 최장 상품 수|상태|",
        "|---|---|---:|---:|---|",
    ]
    for item in summary:
        score = item.get("score") or {}
        lines.append(
            f"|{item['id']}|{item['name']}|{score.get('avgUniqueStarts', 0)}|"
            f"{score.get('coupangLongestCount', 0)}|{item['status']}|"
        )

    for item in summary:
        lines.extend(["", f"## {item['id']} · {item['name']}", "", item["strategy"], ""])
        if item["status"] != "ok":
            lines.append(f"- 실패: {item.get('error', '')}")
            continue
        for row in item["score"]["rows"]:
            lines.append(f"### {row['gs']}")
            for ch in CHANNELS:
                lines.append(f"- {ch}: {row['titles'].get(ch, '')}")
            lines.append("")
    (TRIAL_ROOT / "REPORT.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    TRIAL_ROOT.mkdir(parents=True, exist_ok=True)
    log_path = TRIAL_ROOT / "run.log"
    seed = json.loads(SEED.read_text(encoding="utf-8"))
    products = [slim_product(p) for p in seed.get("products", [])[:3]]
    summary: list[dict] = []
    codex = shutil.which("codex.cmd") or shutil.which("codex") or "codex"

    with log_path.open("w", encoding="utf-8", errors="replace") as log:
        log.write(f"TRIAL_ROOT={TRIAL_ROOT}\n")
        log.write(f"codex={codex}\n")
        log.flush()
        for index, (trial_id, name, strategy) in enumerate(TRIALS, start=1):
            tdir = TRIAL_ROOT / trial_id
            tdir.mkdir(parents=True, exist_ok=True)
            prompt = BASE_PROMPT.replace("__TRIAL_STRATEGY__", strategy)
            (tdir / "prompt.md").write_text(prompt, encoding="utf-8")
            (tdir / "keyword_input.json").write_text(json.dumps({
                "schema": "webocr.keyword.input.v1",
                "createdAt": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                "channels": CHANNELS,
                "products": products,
                "trial": {"id": trial_id, "name": name, "strategy": strategy},
            }, ensure_ascii=False, indent=2), encoding="utf-8")
            instruction = "prompt.md를 먼저 읽고 keyword_input.json의 3개 상품에 대해 keyword_result.json만 작성해."
            cmd = [
                codex, "exec",
                "--skip-git-repo-check",
                "--dangerously-bypass-approvals-and-sandbox",
                "-C", str(tdir),
                instruction,
            ]
            log.write(f"\n[{datetime.now():%H:%M:%S}] START {index}/10 {trial_id} {name}\n")
            log.flush()
            try:
                proc = subprocess.run(
                    cmd,
                    cwd=str(tdir),
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    timeout=420,
                )
                (tdir / "codex_output.log").write_text(proc.stdout or "", encoding="utf-8", errors="replace")
                if proc.returncode != 0:
                    raise RuntimeError(f"codex exit {proc.returncode}")
                result_path = tdir / "keyword_result.json"
                if not result_path.is_file():
                    raise FileNotFoundError("keyword_result.json not created")
                result = json.loads(result_path.read_text(encoding="utf-8"))
                score = score_result(result)
                summary.append({"id": trial_id, "name": name, "strategy": strategy, "status": "ok", "score": score})
                log.write(f"[{datetime.now():%H:%M:%S}] OK {trial_id} avgUniqueStarts={score['avgUniqueStarts']} coupangLongest={score['coupangLongestCount']}\n")
            except Exception as exc:
                summary.append({"id": trial_id, "name": name, "strategy": strategy, "status": "failed", "error": str(exc), "score": {}})
                log.write(f"[{datetime.now():%H:%M:%S}] FAILED {trial_id}: {exc}\n")
            log.flush()
            write_report(summary)

    (TRIAL_ROOT / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    write_report(summary)
    print(TRIAL_ROOT)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
