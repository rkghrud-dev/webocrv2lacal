# -*- coding: utf-8 -*-
"""프론트엔드 사전 번들 빌드.

components.jsx + pipeline_contracts.js + app.jsx 를 esbuild로 변환·압축해
app.bundle.js 하나로 만든다 (실행 순서 유지, window 전역 패턴 그대로).

사용법:  python build_frontend.py
jsx를 수정하면 반드시 다시 실행해야 브라우저에 반영된다.
"""
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCES = ["components.jsx", "pipeline_contracts.js", "app.jsx"]
OUT = HERE / "app.bundle.js"


def main() -> int:
    parts = []
    for name in SOURCES:
        src = HERE / name
        result = subprocess.run(
            ["npx", "--yes", "esbuild", str(src), "--loader:.jsx=jsx", "--target=es2018", "--minify"],
            capture_output=True, text=True, encoding="utf-8", shell=True,
        )
        if result.returncode != 0:
            print(f"[FAIL] {name}\n{result.stderr[:2000]}")
            return 1
        # 파일별 IIFE 격리 — 최상위 const(React 훅 구조분해 등) 충돌 방지. 공유는 window 전역으로만.
        parts.append(f"/* === {name} === */\n(function(){{\n{result.stdout}\n}})();")
        print(f"[OK] {name} -> {len(result.stdout):,} bytes")
    stamp = time.strftime("%Y%m%d-%H%M")
    OUT.write_text(f"/* built {stamp} */\n" + "\n;\n".join(parts), encoding="utf-8")
    print(f"[DONE] {OUT.name} ({OUT.stat().st_size:,} bytes)")

    # index.html의 번들 버전 쿼리 자동 갱신
    index_path = HERE / "index.html"
    if index_path.is_file():
        import re
        html = index_path.read_text(encoding="utf-8")
        new_html = re.sub(r"app\.bundle\.js\?v=[\w-]+", f"app.bundle.js?v={stamp}", html)
        if new_html != html:
            index_path.write_text(new_html, encoding="utf-8")
            print(f"[DONE] index.html 버전 갱신 -> {stamp}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
