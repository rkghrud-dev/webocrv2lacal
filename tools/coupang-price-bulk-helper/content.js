(function () {
  "use strict";

  const DATA = Array.isArray(window.COUPANG_PRICE_HELPER_DATA)
    ? window.COUPANG_PRICE_HELPER_DATA
    : [];

  const state = {
    matches: [],
    autoSave: false,
    busy: false,
  };

  const style = document.createElement("style");
  style.textContent = `
    #cph-panel {
      position: fixed;
      right: 18px;
      bottom: 18px;
      z-index: 2147483647;
      width: 360px;
      max-height: 70vh;
      overflow: auto;
      background: #fff;
      border: 1px solid #2563eb;
      border-radius: 8px;
      box-shadow: 0 12px 32px rgba(15, 23, 42, .22);
      color: #111827;
      font-family: Arial, "Malgun Gothic", sans-serif;
      font-size: 13px;
    }
    #cph-panel * { box-sizing: border-box; }
    #cph-panel header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 10px 12px;
      color: #fff;
      background: #2563eb;
      font-weight: 700;
    }
    #cph-panel main { padding: 10px 12px 12px; }
    #cph-panel button {
      height: 32px;
      border: 1px solid #2563eb;
      border-radius: 4px;
      background: #fff;
      color: #1d4ed8;
      font-weight: 700;
      cursor: pointer;
      padding: 0 9px;
      margin: 4px 4px 4px 0;
    }
    #cph-panel button.primary { background: #2563eb; color: #fff; }
    #cph-panel button.danger { border-color: #dc2626; color: #dc2626; }
    #cph-panel button:disabled { opacity: .5; cursor: not-allowed; }
    #cph-panel label { display: flex; gap: 7px; align-items: center; margin: 8px 0; }
    #cph-log {
      margin-top: 8px;
      padding: 8px;
      max-height: 260px;
      overflow: auto;
      white-space: pre-wrap;
      background: #f8fafc;
      border: 1px solid #e5e7eb;
      border-radius: 4px;
      line-height: 1.45;
    }
    .cph-highlight {
      outline: 3px solid #2563eb !important;
      outline-offset: -3px !important;
      background: rgba(37, 99, 235, .06) !important;
    }
  `;
  document.documentElement.appendChild(style);

  function makePanel() {
    if (document.getElementById("cph-panel")) return;
    const panel = document.createElement("div");
    panel.id = "cph-panel";
    panel.innerHTML = `
      <header>
        <span>쿠팡 가격도우미</span>
        <button id="cph-close" type="button" style="height:24px;border-color:#fff;color:#fff;background:transparent;">닫기</button>
      </header>
      <main>
        <div>대상 데이터: <b>${DATA.length}</b>개 상품</div>
        <label><input id="cph-autosave" type="checkbox"> 저장까지 자동 실행</label>
        <div>
          <button id="cph-scan" type="button">현재 화면 매칭</button>
          <button id="cph-run-one" class="primary" type="button">선택+입력</button>
          <button id="cph-clear" class="danger" type="button">표시 제거</button>
        </div>
        <div id="cph-log">대기 중</div>
      </main>
    `;
    document.body.appendChild(panel);
    document.getElementById("cph-close").addEventListener("click", () => panel.remove());
    document.getElementById("cph-scan").addEventListener("click", scanVisible);
    document.getElementById("cph-run-one").addEventListener("click", runFirstVisibleMatch);
    document.getElementById("cph-clear").addEventListener("click", clearHighlights);
    document.getElementById("cph-autosave").addEventListener("change", (event) => {
      state.autoSave = event.target.checked;
      log(state.autoSave ? "저장까지 자동 실행 ON" : "저장 전 멈춤");
    });
  }

  function log(message) {
    const box = document.getElementById("cph-log");
    if (!box) return;
    const stamp = new Date().toLocaleTimeString("ko-KR", { hour12: false });
    box.textContent = `[${stamp}] ${message}\n` + box.textContent;
  }

  function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  function visible(el) {
    if (!el) return false;
    const rect = el.getBoundingClientRect();
    const style = window.getComputedStyle(el);
    return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
  }

  function normalize(text) {
    return (text || "").replace(/\s+/g, " ").trim();
  }

  function clearHighlights() {
    document.querySelectorAll(".cph-highlight").forEach(el => el.classList.remove("cph-highlight"));
    state.matches = [];
    log("표시 제거 완료");
  }

  function candidateContainers() {
    const containers = new Set();
    document.querySelectorAll("tr, li, [role='row'], div").forEach(el => {
      if (!visible(el)) return;
      const txt = normalize(el.innerText);
      if (!txt || txt.length < 30 || txt.length > 3000) return;
      if (!/GS\d{7}[A-Z0-9]*/i.test(txt)) return;
      if (!el.querySelector("input[type='checkbox']")) return;
      containers.add(el);
    });
    return [...containers].filter(el => {
      return ![...containers].some(other => other !== el && other.contains(el) && normalize(other.innerText).length < normalize(el.innerText).length + 500);
    });
  }

  function findTargetForRow(text) {
    const compact = text.replace(/\s+/g, "");
    const bySellerProduct = DATA.find(item =>
      (item.sellerProductIds || []).some(id => id && compact.includes(String(id)))
    );
    if (bySellerProduct) return bySellerProduct;
    return DATA.find(item => item.gs && compact.toUpperCase().includes(item.gs.toUpperCase()));
  }

  function scanVisible() {
    clearHighlights();
    const matches = [];
    for (const row of candidateContainers()) {
      const target = findTargetForRow(row.innerText || "");
      if (!target) continue;
      row.classList.add("cph-highlight");
      matches.push({ row, target });
    }
    state.matches = matches;
    if (matches.length === 0) {
      log("현재 화면에서 매칭 상품 없음");
      return;
    }
    log(`현재 화면 매칭 ${matches.length}건\n` + matches.map(m => `${m.target.gs}: 1000원 -> ${m.target.targetPrice}원 (${m.target.decreaseAmount}원 인하)`).join("\n"));
  }

  function clickElementByText(texts, root = document) {
    const wanted = Array.isArray(texts) ? texts : [texts];
    const nodes = [...root.querySelectorAll("button, a, li, div, span, [role='button'], [role='menuitem']")]
      .filter(visible)
      .filter(el => {
        const txt = normalize(el.innerText || el.textContent);
        return wanted.some(w => txt === w || txt.includes(w));
      });
    const best = nodes.sort((a, b) => normalize(a.innerText).length - normalize(b.innerText).length)[0];
    if (!best) return false;
    best.click();
    return true;
  }

  function setNativeValue(input, value) {
    const proto = input instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(proto, "value")?.set;
    setter ? setter.call(input, String(value)) : input.value = String(value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  }

  function selectDropdownValue(select, keyword) {
    const option = [...select.options].find(opt => normalize(opt.textContent).includes(keyword) || normalize(opt.value).includes(keyword));
    if (!option) return false;
    select.value = option.value;
    select.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
  }

  async function openPriceModal() {
    if (!clickElementByText("선택한 상품 일괄적용")) {
      log("일괄적용 버튼을 찾지 못함");
      return false;
    }
    await sleep(350);
    if (!clickElementByText("판매가 변경")) {
      log("판매가 변경 메뉴를 찾지 못함");
      return false;
    }
    await sleep(600);
    return true;
  }

  function currentModal() {
    const dialogs = [...document.querySelectorAll("[role='dialog'], .modal, div")]
      .filter(visible)
      .filter(el => normalize(el.innerText).includes("판매가") && normalize(el.innerText).includes("저장"));
    return dialogs.sort((a, b) => b.getBoundingClientRect().width * b.getBoundingClientRect().height - a.getBoundingClientRect().width * a.getBoundingClientRect().height)[0] || document;
  }

  async function fillPriceModal(target) {
    const modal = currentModal();
    const inputs = [...modal.querySelectorAll("input")]
      .filter(visible)
      .filter(input => !["checkbox", "radio", "hidden"].includes((input.type || "").toLowerCase()));
    const amountInput = inputs[0];
    if (!amountInput) {
      log("판매가 입력칸을 찾지 못함");
      return false;
    }
    setNativeValue(amountInput, target.decreaseAmount);

    const selects = [...modal.querySelectorAll("select")].filter(visible);
    for (const sel of selects) {
      selectDropdownValue(sel, "원");
      selectDropdownValue(sel, "인하");
    }

    if (!normalize(modal.innerText).includes("인하") || normalize(modal.innerText).includes("인상")) {
      const candidates = [...modal.querySelectorAll("button, [role='button'], div, span")].filter(visible);
      const decreaseControl = candidates
        .filter(el => normalize(el.innerText || el.textContent).includes("인상"))
        .sort((a, b) => normalize(a.innerText).length - normalize(b.innerText).length)[0];
      if (decreaseControl) {
        decreaseControl.click();
        await sleep(200);
        clickElementByText("인하");
      }
    }

    log(`${target.gs}: ${target.decreaseAmount}원 인하 입력 완료 (${target.currentPrice} -> ${target.targetPrice})`);
    if (state.autoSave) {
      await sleep(250);
      if (clickElementByText("저장", modal)) {
        log(`${target.gs}: 저장 클릭`);
      } else {
        log("저장 버튼을 찾지 못함");
        return false;
      }
    } else {
      log("저장 전 멈춤. 쿠팡 모달에서 직접 확인 후 저장하세요.");
    }
    return true;
  }

  async function runFirstVisibleMatch() {
    if (state.busy) return;
    state.busy = true;
    try {
      if (state.matches.length === 0) scanVisible();
      const match = state.matches.find(m => document.body.contains(m.row));
      if (!match) {
        log("처리할 매칭 행이 없습니다.");
        return;
      }
      match.row.scrollIntoView({ block: "center" });
      await sleep(250);
      const checkbox = match.row.querySelector("input[type='checkbox']");
      if (!checkbox) {
        log("체크박스를 찾지 못함");
        return;
      }
      if (!checkbox.checked) checkbox.click();
      await sleep(300);
      if (!(await openPriceModal())) return;
      await fillPriceModal(match.target);
    } finally {
      state.busy = false;
    }
  }

  makePanel();
})();
