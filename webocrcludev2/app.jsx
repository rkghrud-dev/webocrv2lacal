/* eslint-disable no-undef */
// WEBOCRV2 — app demo
const { useState, useEffect, useRef, useCallback } = React;

const MARKET_NAMES = ['네이버','쿠팡','롯데ON','11번가','ESM'];
const DEFAULT_MARKET_SELECTION = Object.fromEntries(
  ['A', 'B'].flatMap((account) => MARKET_NAMES.map((market) => [`${account}:${market}`, true])),
);
const DEFAULT_KEYWORD_OPTIONS = {
  accountScope: '전체',
  runUnit: '상품단위',
  concurrency: 50,
  imageSize: 1000,
  jpegMin: 88,
  jpegMax: 92,
  logoPath: '',
  logoPathB: '',
  logoRatio: 14,
  logoOpacity: 65,
  logoPosition: 'tr',
  autoContrast: true,
  sharpen: true,
  fixRotation: true,
  mirror: false,
  detailTagA: "<img src='https://gi.esmplus.com/rkghrud/상세1.jpg' />",
  detailTagB: '',
};
const SEED_STORE_PATH = 'data/seeds';
const PIPELINE_STEPS = [
  { key: 'drop', title: '원본 입력', stage: 'drop' },
  { key: 'source', title: '원본 확인', stage: 'basic', fileMode: 'source' },
  { key: 'seed-setup', title: '시드 검수/마켓 선택', stage: 'basic', fileMode: 'seed' },
  { key: 'keyword', title: '키워드 생성', stage: 'keyword' },
  { key: 'upload', title: '업로드', stage: 'upload' },
  { key: 'result', title: '결과 확인', stage: 'result' },
];

function isSeedFile(file) {
  const name = (file?.name || '').toLowerCase();
  return name.endsWith('.webseed.json')
    || name.endsWith('.seed.json')
    || name.includes('webseed')
    || name.includes('시드')
    || name.includes('seed');
}

function getScopedMarketKeys(selection, accountScope = '전체') {
  return Object.entries(selection || {})
    .filter(([, enabled]) => enabled !== false)
    .map(([key]) => key)
    .filter((key) => !key.endsWith(':Cafe24'))
    .filter((key) => accountScope === '전체' || key.startsWith(`${accountScope}:`));
}

function storePipelinePayload(key, payload) {
  if (!payload) return;
  try {
    localStorage.setItem(key, JSON.stringify(payload, null, 2));
    window.WEBOCR_LAST_PIPELINE_PAYLOAD = payload;
  } catch {}
}

function uploadQueueKey(channelKey, gs) {
  return `${channelKey || ''}:${gs || ''}`;
}

function historyKey(channelKey, gs) {
  return `${channelKey || ''}:${gs || ''}`;
}

function rowsWithUploadHistory(rows, history) {
  const marketIndex = { Cafe24: 0, 네이버: 1, 쿠팡: 2, 롯데ON: 3, '11번가': 4, ESM: 5 };
  return rows.map((row) => {
    const next = {
      ...row,
      A: Array.isArray(row.A) ? [...row.A] : ['muted','muted','muted','muted','muted','muted'],
      B: Array.isArray(row.B) ? [...row.B] : ['muted','muted','muted','muted','muted','muted'],
    };
    if (row.cafe24Url) {
      next.A[0] = 'uploaded';
      next.B[0] = 'uploaded';
    }
    Object.values(history || {}).forEach((item) => {
      if (item?.gs !== row.gs || !item?.channelKey) return;
      const [account, market] = item.channelKey.split(':');
      const index = marketIndex[market];
      if (!['A', 'B'].includes(account) || index == null) return;
      const status = item.status === 'uploaded'
        ? 'uploaded'
        : item.status === 'failed'
          ? 'failed'
          : item.status === 'exported'
            ? 'excel'
            : item.status === 'requested'
              ? 'targeted'
              : 'targeted';
      next[account][index] = status;
    });
    return next;
  });
}

function countKeywordTerms(pool = {}) {
  return Object.values(pool || {}).reduce((total, value) => total + (Array.isArray(value) ? value.length : 0), 0);
}

function seedPayloadToFile(seedPayload, meta = {}) {
  const products = Array.isArray(seedPayload?.products) ? seedPayload.products : [];
  const preview = products.map((product, index) => {
    const images = product.images || {};
    const ocr = product.ocrAnalysis || {};
    const cafe24 = product.cafe24 || product.cafe24Upload || {};
    return {
      id: product.gs || `seed-${index + 1}`,
      gs: product.gs || '',
      baseGs: product.baseGs || '',
      name: product.reviewFields?.productName || product.sourceName || product.gs || '',
      price: Number(product.price || 0),
      supplyPrice: Number(product.supplyPrice || 0),
      salePrice: Number(product.salePrice || product.price || 0),
      consumerPrice: Number(product.consumerPrice || 0),
      opt: product.optionSummary || '단일',
      optionInput: product.optionInput || '',
      optionAdditionalAmounts: product.optionAdditionalAmounts || [],
      categories: product.categories || {},
      thumb: images.representative || images.sourceThumb || '',
      images,
      optionItems: product.optionItems || [],
      detailHtml: product.detailHtml || '',
      optionType: product.optionType || '',
      optionCount: product.optionCount || 0,
      ocrStatus: ocr.status || 'pending',
      imageSize: images.processedSize || '',
      keywordCount: countKeywordTerms(product.keywordCandidatePool),
      naverProvidedNotice: product.naverProvidedNotice || null,
      cafe24Url: cafe24.url || cafe24.productUrl || '',
      originalName: product.sourceName || product.gs || '',
      history: product.uploadHistory || [],
      seedProduct: product,
    };
  });
  return {
    name: meta.name || seedPayload?.name || 'seed.webseed.json',
    kind: meta.kind || '시드 파일',
    size: Number(meta.size || 0),
    rows: Number(seedPayload?.sourceFilter?.filteredRows || products.length),
    gsCodes: products.length,
    preview,
    seedPayload,
    sourcePath: meta.path || '',
    pipelineResult: seedPayload?.pipelineResult || {},
  };
}

async function fetchSeedPayload(path) {
  const response = await fetch(`/api/seed?path=${encodeURIComponent(path)}`);
  const payload = await response.json();
  if (!response.ok || !payload?.ok) throw new Error(payload?.error || `seed ${response.status}`);
  return payload.seed;
}

function formatRelativeAge(value) {
  if (!value) return '';
  const normalized = String(value).replace(' ', 'T');
  const time = new Date(normalized).getTime();
  if (!Number.isFinite(time)) return value;
  const seconds = Math.max(0, Math.round((Date.now() - time) / 1000));
  if (seconds < 60) return `${seconds}초 전`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}분 전`;
  return `${Math.round(minutes / 60)}시간 전`;
}

function rowsForFile(file) {
  const preview = file?.preview || [];
  if (!preview.length) return [];
  return preview.map((row, index) => ({
    id: row.id || `source-${index + 1}`,
    gs: row.gs,
    name: row.name,
    price: row.price || 0,
    supplyPrice: row.supplyPrice || row.seedProduct?.supplyPrice || 0,
    salePrice: row.salePrice || row.seedProduct?.salePrice || row.price || 0,
    consumerPrice: row.consumerPrice || row.seedProduct?.consumerPrice || 0,
    opt: row.opt || '단일',
    optionInput: row.optionInput || row.seedProduct?.optionInput || '',
    optionAdditionalAmounts: row.optionAdditionalAmounts || row.seedProduct?.optionAdditionalAmounts || [],
    categories: row.categories || row.seedProduct?.categories || {},
    thumb: row.thumb || '',
    images: row.images || row.seedProduct?.images || {},
    optionType: row.optionType || '',
    optionCount: row.optionCount || 0,
    optionItems: row.optionItems || [],
    detailHtml: row.detailHtml || row.seedProduct?.detailHtml || '',
    seedProduct: row.seedProduct || null,
    keywordCandidatePool: row.seedProduct?.keywordCandidatePool || row.keywordCandidatePool || {},
    generatedKeywordSeed: row.seedProduct?.generatedKeywordSeed || row.generatedKeywordSeed || {},
    marketKeywords: row.seedProduct?.marketKeywords || row.marketKeywords || {},
    naverProvidedNotice: row.seedProduct?.naverProvidedNotice || row.naverProvidedNotice || null,
    keywordGeneration: row.seedProduct?.keywordGeneration || row.keywordGeneration || {},
    cafe24Url: row.cafe24Url || '',
    ocrStatus: row.ocrStatus || '',
    imageSize: row.imageSize || '',
    keywordCount: row.keywordCount || 0,
    originalName: row.originalName || row.name,
    history: row.history || [],
    A: ['muted','muted','muted','muted','muted','muted'],
    B: ['muted','muted','muted','muted','muted','muted'],
  }));
}

function ProductManagerBrowser({ onImport, addLog }) {
  const h = React.createElement;
  const [pmAvailable, setPmAvailable] = useState(null);
  const [dates, setDates] = useState([]);
  const [selectedDate, setSelectedDate] = useState('');
  const [suppliers, setSuppliers] = useState([]);
  const [selectedSuppliers, setSelectedSuppliers] = useState(new Set());
  const [countPerSupplier, setCountPerSupplier] = useState(5);
  const [sortOrder, setSortOrder] = useState('latest');
  const [filterMode, setFilterMode] = useState('available');
  const [searchText, setSearchText] = useState('');
  const [products, setProducts] = useState([]);
  const [totalProducts, setTotalProducts] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [page, setPage] = useState(1);
  const [perPage, setPerPage] = useState(50);
  const [selectedSkus, setSelectedSkus] = useState(new Set());
  const [loading, setLoading] = useState(false);
  const [listLoaded, setListLoaded] = useState(false);
  const [csvDragOver, setCsvDragOver] = useState(false);
  const [supplierDropOpen, setSupplierDropOpen] = useState(false);
  const [importing, setImporting] = useState(false);
  const [csvUploadSummary, setCsvUploadSummary] = useState(null);
  const dropdownRef = React.useRef(null);

  useEffect(() => {
    if (!supplierDropOpen) return;
    const close = (e) => { if (dropdownRef.current && !dropdownRef.current.contains(e.target)) setSupplierDropOpen(false); };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [supplierDropOpen]);

  useEffect(() => {
    fetch('/api/pm/status').then(r => r.json()).then(d => {
      setPmAvailable(d.available);
      if (d.available) fetch('/api/pm/dates').then(r => r.json()).then(d2 => setDates(d2.dates || []));
    }).catch(() => setPmAvailable(false));
  }, []);

  useEffect(() => {
    if (!pmAvailable) return;
    const url = selectedDate ? `/api/pm/suppliers?upload_date=${selectedDate}` : '/api/pm/suppliers';
    fetch(url).then(r => r.json()).then(d => {
      setSuppliers(d.suppliers || []);
      setSelectedSuppliers(new Set((d.suppliers || []).filter(s => s.available_skus > 0).map(s => s.supplier_code)));
    });
  }, [selectedDate, pmAvailable]);

  const toggleSupplier = (code) => {
    setSelectedSuppliers(prev => { const n = new Set(prev); n.has(code) ? n.delete(code) : n.add(code); return n; });
  };

  const fetchProducts = async (pg = 1) => {
    if (!selectedSuppliers.size) return;
    setLoading(true);
    try {
      const res = await fetch('/api/pm/products', {
        method: 'POST', headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({
          suppliers: [...selectedSuppliers], upload_date: selectedDate,
          sort_order: sortOrder, filter_mode: filterMode,
          count: countPerSupplier,
          search: searchText, page: pg, per_page: perPage,
        }),
      });
      const data = await res.json();
      if (data.ok) {
        setProducts(data.products || []);
        setTotalProducts(data.total || 0);
        setTotalPages(data.total_pages || 0);
        setPage(data.page || 1);
        setListLoaded(true);
        if (pg === 1) setSelectedSkus(new Set());
      }
    } finally { setLoading(false); }
  };

  const doImport = async () => {
    if (!selectedSkus.size) return;
    setImporting(true);
    try {
      const res = await fetch('/api/pm/import', {
        method: 'POST', headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({
          suppliers: [...selectedSuppliers], sort_order: sortOrder,
          count: countPerSupplier, upload_date: selectedDate,
          filter_mode: filterMode,
          selectedSkus: [...selectedSkus],
        }),
      });
      const data = await res.json();
      if (!data.ok) throw new Error(data.error || 'import failed');
      addLog(`PM에서 ${data.skuCount}개 SKU (${data.productCount}개 상품) 가져옴 → ${data.fileName}`);
      onImport(data);
    } catch (e) {
      addLog(`PM 가져오기 실패: ${e.message}`);
    } finally { setImporting(false); }
  };

  const handleCsvDrop = async (e) => {
    e.preventDefault(); setCsvDragOver(false);
    const file = e.dataTransfer?.files?.[0];
    if (!file) return;
    const ext = file.name.split('.').pop().toLowerCase();
    if (!['csv','xlsx','xls'].includes(ext)) { addLog('CSV/Excel 파일만 업로드 가능합니다.'); return; }
    setLoading(true);
    try {
      const form = new FormData(); form.append('file', file);
      const res = await fetch('/api/pm/upload-csv', { method: 'POST', body: form });
      const data = await res.json();
      if (!data.ok) throw new Error(data.error || 'upload failed');
      setCsvUploadSummary(data);
      addLog(`PM CSV 업로드: ${data.fileName} (총 ${data.total || 0} · 신규 ${data.new_count || 0} · 중복제외 ${data.duplicate_count || 0} · 파일중복 ${data.file_duplicate_count || 0} · 건너뜀 ${data.skipped_count || 0})`);
      const url = selectedDate ? `/api/pm/suppliers?upload_date=${selectedDate}` : '/api/pm/suppliers';
      const sRes = await fetch(url); const sData = await sRes.json();
      setSuppliers(sData.suppliers || []);
      if (listLoaded) fetchProducts(page);
    } catch (e2) {
      addLog(`PM CSV 업로드 실패: ${e2.message}`);
    } finally { setLoading(false); }
  };

  const toggleSku = (sku) => setSelectedSkus(prev => { const n = new Set(prev); n.has(sku) ? n.delete(sku) : n.add(sku); return n; });
  const allVisibleSelected = products.length > 0 && products.every(p => selectedSkus.has(p.sku_group));
  const toggleAll = () => {
    if (allVisibleSelected) {
      setSelectedSkus(prev => { const n = new Set(prev); products.forEach(p => n.delete(p.sku_group)); return n; });
    } else {
      setSelectedSkus(prev => { const n = new Set(prev); products.forEach(p => n.add(p.sku_group)); return n; });
    }
  };

  const formatPrice = (v) => v != null ? Number(v).toLocaleString('ko-KR') + '원' : '-';
  const statusDot = (listed, naverStatus, isDup) => {
    if (listed) return h('span', {className: 'pm-dot pm-dot-done', title: '등록완료'});
    if (isDup) return h('span', {className: 'pm-dot pm-dot-dup', title: '중복'});
    if (naverStatus === '신규') return h('span', {className: 'pm-dot pm-dot-new', title: '미등록'});
    return h('span', {className: 'pm-dot pm-dot-muted', title: naverStatus || '-'});
  };

  if (pmAvailable === null) return h('div', {className: 'surface pad-md'}, '연결 확인 중...');
  if (!pmAvailable) return h('div', {className: 'surface pad-md color-muted'}, 'ProductManager DB를 찾을 수 없습니다.');

  const totalAvailable = suppliers.reduce((s, i) => s + (selectedSuppliers.has(i.supplier_code) ? i.available_skus : 0), 0);
  const selectedCount = selectedSuppliers.size;

  return h('div', {
    className: `pm-v2 ${csvDragOver ? 'csv-drag-over' : ''}`,
    onDragOver: (e) => { e.preventDefault(); setCsvDragOver(true); },
    onDragLeave: (e) => { if (e.currentTarget.contains(e.relatedTarget)) return; setCsvDragOver(false); },
    onDrop: handleCsvDrop,
  },
    csvDragOver && h('div', {className: 'pm-csv-overlay'},
      h('div', {className: 'pm-csv-overlay-inner'},
        h(IconUpload, {size: 32}),
        h('p', null, 'CSV / Excel 파일을 놓아 ProductManager에 상품 추가'),
      ),
    ),

    // ── toolbar ──
    h('div', {className: 'pm-toolbar'},
      h('div', {className: 'pm-toolbar-left'},
        // date
        h('select', {className: 'pm-select', value: selectedDate, onChange: e => setSelectedDate(e.target.value)},
          h('option', {value: ''}, '전체 날짜'),
          ...dates.map(d => h('option', {key: d.upload_date, value: d.upload_date}, `${d.upload_date} (${d.total_count}건)`)),
        ),
        // supplier dropdown
        h('div', {ref: dropdownRef, className: 'pm-dropdown-wrap', style: {position: 'relative'}},
          h('button', {type: 'button', className: 'pm-select pm-dropdown-btn', onClick: () => setSupplierDropOpen(v => !v)},
            `사업자 ${selectedCount}개 선택 ▾`
          ),
          supplierDropOpen && h('div', {className: 'pm-dropdown-panel'},
            h('div', {className: 'pm-dropdown-actions'},
              h('button', {type: 'button', onClick: () => setSelectedSuppliers(new Set(suppliers.map(s => s.supplier_code)))}, '전체'),
              h('button', {type: 'button', onClick: () => setSelectedSuppliers(new Set())}, '해제'),
              h('button', {type: 'button', onClick: () => setSelectedSuppliers(new Set(suppliers.filter(s => s.available_skus > 0).map(s => s.supplier_code)))}, '미등록만'),
            ),
            h('div', {className: 'pm-dropdown-list'},
              ...suppliers.map(s => h('label', {key: s.supplier_code, className: 'pm-dropdown-item'},
                h('input', {type: 'checkbox', checked: selectedSuppliers.has(s.supplier_code), onChange: () => toggleSupplier(s.supplier_code)}),
                h('span', {className: 'pm-dropdown-item-name'}, s.supplier_code),
                h('span', {className: 'pm-dropdown-item-count color-muted'}, `${s.available_skus}/${s.total_skus}`),
              )),
            ),
          ),
        ),
        // count per supplier
        h('label', {className: 'pm-toolbar-label'}, '수량',
          h('input', {type: 'number', className: 'pm-input-sm', min: 1, max: 9999, value: countPerSupplier,
            onChange: e => setCountPerSupplier(+e.target.value || 5)}),
        ),
        // sort
        h('select', {className: 'pm-select', value: sortOrder, onChange: e => setSortOrder(e.target.value)},
          h('option', {value: 'latest'}, '최신순'),
          h('option', {value: 'oldest'}, '오래된순'),
          h('option', {value: 'random'}, '랜덤'),
        ),
        // filter pills
        h('div', {className: 'pm-filter-pills'},
          ...['available', 'listed', 'all'].map(m => h('button', {
            key: m, type: 'button',
            className: `pm-pill ${filterMode === m ? 'active' : ''}`,
            onClick: () => setFilterMode(m),
          }, m === 'available' ? '미등록' : m === 'listed' ? '등록완료' : '전체')),
        ),
        // load button
        h('button', {className: 'btn-aurora pm-load-btn', onClick: () => fetchProducts(1), disabled: loading || !selectedSuppliers.size},
          loading ? '불러오는 중...' : '상품 불러오기'),
      ),
      h('div', {className: 'pm-toolbar-right'},
        // search
        h('input', {type: 'text', className: 'pm-search', placeholder: '검색 (SKU, 상품명)', value: searchText,
          onChange: e => setSearchText(e.target.value),
          onKeyDown: e => { if (e.key === 'Enter') fetchProducts(1); },
        }),
        // per page
        h('select', {className: 'pm-select', value: perPage, onChange: e => { setPerPage(+e.target.value); }},
          h('option', {value: 10}, '10개씩'),
          h('option', {value: 50}, '50개씩'),
          h('option', {value: 100}, '100개씩'),
        ),
      ),
    ),

    // ── action bar ──
    csvUploadSummary && h('div', {className: 'pm-upload-summary', style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(5, minmax(110px, 1fr))',
      gap: '10px',
      margin: '12px 0',
      padding: '12px',
      border: '1px solid rgba(80, 95, 130, .18)',
      borderRadius: '12px',
      background: 'rgba(255,255,255,.72)'
    }},
      h('div', {style: {gridColumn: '1 / -1', fontWeight: 800}}, `최근 CSV: ${csvUploadSummary.fileName}`),
      h('div', null, h('b', null, csvUploadSummary.total || 0), h('span', {className: 'color-muted'}, ' 전체')),
      h('div', null, h('b', null, csvUploadSummary.new_count || 0), h('span', {className: 'color-muted'}, ' 신규추가')),
      h('div', null, h('b', null, csvUploadSummary.duplicate_count || 0), h('span', {className: 'color-muted'}, ' 기존중복 제외')),
      h('div', null, h('b', null, csvUploadSummary.file_duplicate_count || 0), h('span', {className: 'color-muted'}, ' 파일내 중복')),
      h('div', null, h('b', null, csvUploadSummary.skipped_count || 0), h('span', {className: 'color-muted'}, ' 건너뜀')),
      ((csvUploadSummary.duplicate_samples || []).length > 0) && h('div', {style: {gridColumn: '1 / -1', fontSize: '.82rem'}},
        h('span', {className: 'color-muted'}, '중복 제외 예시: '),
        (csvUploadSummary.duplicate_samples || []).slice(0, 8).map(item => item.product_code).join(', '),
        (csvUploadSummary.duplicate_count || 0) > 8 ? ` 외 ${(csvUploadSummary.duplicate_count || 0) - 8}개` : ''
      )
    ),

    listLoaded && h('div', {className: 'pm-action-bar'},
      h('div', {className: 'pm-action-left'},
        h('span', {className: 'color-muted'}, `총 ${totalProducts.toLocaleString()}개 SKU 그룹`),
        selectedSkus.size > 0 && h('span', {className: 'pm-badge'}, `${selectedSkus.size}개 선택`),
      ),
      h('div', {className: 'pm-action-right'},
        h('button', {type: 'button', className: 'btn-ghost pm-sm-btn', onClick: toggleAll},
          allVisibleSelected ? '현재 페이지 해제' : '현재 페이지 선택'),
        h('button', {type: 'button', className: 'btn-ghost pm-sm-btn', onClick: () => setSelectedSkus(new Set())}, '전체 해제'),
        h('button', {className: 'btn-aurora pm-import-btn', onClick: doImport, disabled: importing || !selectedSkus.size},
          importing ? '가져오는 중...' : `선택 상품 가져오기 (${selectedSkus.size})`),
      ),
    ),

    // ── product table ──
    listLoaded && h('div', {className: 'pm-table-wrap'},
      h('table', {className: 'pm-table'},
        h('thead', null,
          h('tr', null,
            h('th', {className: 'pm-th-chk'}, h('input', {type: 'checkbox', checked: allVisibleSelected && products.length > 0, onChange: toggleAll})),
            h('th', {className: 'pm-th-img'}, ''),
            h('th', {className: 'pm-th-sku'}, 'SKU'),
            h('th', {className: 'pm-th-name'}, '상품명'),
            h('th', {className: 'pm-th-supplier'}, '사업자'),
            h('th', {className: 'pm-th-price'}, '가격'),
            h('th', {className: 'pm-th-opt'}, '옵션'),
            h('th', {className: 'pm-th-status'}, '상태'),
          ),
        ),
        h('tbody', null,
          products.length === 0 && h('tr', null,
            h('td', {colSpan: 8, className: 'pm-empty-row'}, loading ? '불러오는 중...' : '상품 불러오기를 클릭하세요'),
          ),
          ...products.map(p => h('tr', {
            key: p.sku_group,
            className: `pm-row ${p.is_listed ? 'pm-row-listed' : ''} ${selectedSkus.has(p.sku_group) ? 'pm-row-selected' : ''} ${p.is_naver_duplicate ? 'pm-row-dup' : ''}`,
            onClick: () => toggleSku(p.sku_group),
          },
            h('td', {className: 'pm-td-chk'}, h('input', {type: 'checkbox', checked: selectedSkus.has(p.sku_group), onChange: () => toggleSku(p.sku_group), onClick: e => e.stopPropagation()})),
            h('td', {className: 'pm-td-img'}, p.image_url ? h('img', {src: p.image_url, className: 'pm-thumb'}) : h('span', {className: 'pm-thumb-empty'})),
            h('td', {className: 'pm-td-sku'}, h('code', null, p.sku_group)),
            h('td', {className: 'pm-td-name'},
              h('div', {className: 'pm-name-main'}, p.product_name),
              p.option_count > 1 && h('div', {className: 'pm-name-opts color-muted'},
                p.options.slice(0, 4).map(o => o.name).join(' · ') + (p.option_count > 4 ? ` 외 ${p.option_count - 4}개` : ''),
              ),
            ),
            h('td', {className: 'pm-td-supplier'}, p.supplier_code),
            h('td', {className: 'pm-td-price'}, formatPrice(p.price)),
            h('td', {className: 'pm-td-opt'}, h('span', {className: 'pm-opt-badge'}, `${p.option_count}`)),
            h('td', {className: 'pm-td-status'}, statusDot(p.is_listed, p.naver_status, p.is_naver_duplicate)),
          )),
        ),
      ),
    ),

    // ── pagination ──
    listLoaded && totalPages > 1 && h('div', {className: 'pm-pagination'},
      h('button', {type: 'button', className: 'pm-page-btn', disabled: page <= 1, onClick: () => fetchProducts(page - 1)}, '← 이전'),
      h('span', {className: 'pm-page-info'}, `${page} / ${totalPages}`),
      h('button', {type: 'button', className: 'pm-page-btn', disabled: page >= totalPages, onClick: () => fetchProducts(page + 1)}, '다음 →'),
    ),

    // ── empty state ──
    !listLoaded && h('div', {className: 'pm-empty-state'},
      h('div', {className: 'pm-empty-icon'}, h(IconGrid, {size: 36})),
      h('p', null, '위에서 사업자를 선택하고 "상품 불러오기"를 클릭하세요'),
      h('p', {className: 'color-muted', style: {fontSize: '0.8rem'}}, 'CSV/Excel 파일을 이 영역에 드래그하면 PM에 상품을 추가할 수 있습니다'),
    ),
  );
}


function AutomationPrepWorkbench({ addLog, onSeedsChange }) {
  const h = React.createElement;
  const [pmAvailable, setPmAvailable] = useState(null);
  const [dates, setDates] = useState([]);
  const [selectedDate, setSelectedDate] = useState('');
  const [suppliers, setSuppliers] = useState([]);
  const [selectedSuppliers, setSelectedSuppliers] = useState(new Set());
  const [batchSize, setBatchSize] = useState(20);
  const [runCount, setRunCount] = useState(3);
  const [sortOrder, setSortOrder] = useState('latest');
  const [filterMode, setFilterMode] = useState('available');
  const [accountScope, setAccountScope] = useState('전체');
  const [job, setJob] = useState(null);
  const [running, setRunning] = useState(false);

  useEffect(() => {
    fetch('/api/pm/status').then(r => r.json()).then(d => {
      setPmAvailable(d.available);
      if (d.available) fetch('/api/pm/dates').then(r => r.json()).then(d2 => setDates(d2.dates || []));
    }).catch(() => setPmAvailable(false));
  }, []);

  useEffect(() => {
    if (!pmAvailable) return;
    const url = selectedDate ? `/api/pm/suppliers?upload_date=${selectedDate}` : '/api/pm/suppliers';
    fetch(url).then(r => r.json()).then(d => {
      const next = d.suppliers || [];
      setSuppliers(next);
      setSelectedSuppliers(new Set(next.filter(s => s.available_skus > 0).map(s => s.supplier_code)));
    });
  }, [selectedDate, pmAvailable]);

  const pollJob = (jobId) => {
    window.setTimeout(async () => {
      try {
        const response = await fetch(`/api/jobs/${jobId}`);
        const payload = await response.json();
        if (!response.ok || !payload?.ok) throw new Error(payload?.error || `job ${response.status}`);
        setJob(payload);
        if (payload.status === 'completed') {
          setRunning(false);
          addLog(`자동화 완료: 시드 ${payload.result?.totalBatches || 0}개 생성`);
          fetch('/api/seeds').then(r => r.json()).then(d => { if (d?.ok) onSeedsChange?.(d.seeds || []); }).catch(() => {});
          return;
        }
        if (payload.status === 'failed' || payload.status === 'cancelled') {
          setRunning(false);
          addLog(`자동화 중단: ${payload.error || payload.currentStage || payload.status}`);
          return;
        }
        pollJob(jobId);
      } catch (error) {
        setRunning(false);
        addLog(`자동화 상태 조회 실패: ${error.message}`);
      }
    }, 2000);
  };

  const startAutomation = async () => {
    if (!selectedSuppliers.size) {
      addLog('자동화 시작 실패: 사업자를 1개 이상 선택하세요.');
      return;
    }
    const channels = getScopedMarketKeys(DEFAULT_MARKET_SELECTION, accountScope);
    const payload = {
      suppliers: [...selectedSuppliers],
      upload_date: selectedDate,
      sort_order: sortOrder,
      filter_mode: filterMode,
      batchSize: Number(batchSize || 20),
      runCount: Number(runCount || 1),
      accountScope,
      channels,
      listingImageSettings: window.WEBOCR_PIPELINE?.buildListingImageSettings?.(DEFAULT_KEYWORD_OPTIONS) || {},
    };
    setRunning(true);
    setJob({ status: 'queued', progressPercent: 1, currentStage: '자동화 작업 요청 중' });
    addLog(`자동화 시작: 사업자 ${selectedSuppliers.size}개 · 사업자당 ${payload.batchSize}개 · ${payload.runCount}회 · 최대 ${selectedSuppliers.size * payload.batchSize * payload.runCount}개`);
    try {
      const response = await fetch('/api/automation-prepare', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      const nextJob = await response.json();
      if (!response.ok || !nextJob?.ok) throw new Error(nextJob?.error || `automation ${response.status}`);
      setJob(nextJob);
      pollJob(nextJob.jobId);
    } catch (error) {
      setRunning(false);
      setJob({ status: 'failed', error: error.message });
      addLog(`자동화 요청 실패: ${error.message}`);
    }
  };

  const stopAutomation = async () => {
    if (!job?.jobId) return;
    try {
      await fetch('/api/job-stop', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobId: job.jobId }),
      });
      setRunning(false);
      addLog(`자동화 중지 요청: ${job.jobId}`);
    } catch (error) {
      addLog(`자동화 중지 실패: ${error.message}`);
    }
  };

  const toggleSupplier = (code) => setSelectedSuppliers(prev => {
    const next = new Set(prev);
    next.has(code) ? next.delete(code) : next.add(code);
    return next;
  });

  if (pmAvailable === null) return h('div', {className: 'surface pad-md'}, '연결 확인 중...');
  if (!pmAvailable) return h('div', {className: 'surface pad-md color-muted'}, 'ProductManager DB를 찾을 수 없습니다.');

  const selectedSupplierCount = selectedSuppliers.size;
  const perRunTotal = selectedSupplierCount * Number(batchSize || 0);
  const expectedTotal = perRunTotal * Number(runCount || 0);
  const progress = Number(job?.progressPercent || 0);
  const staleSeconds = job?.updatedAt ? Math.max(0, Math.round((Date.now() - new Date(String(job.updatedAt).replace(' ', 'T')).getTime()) / 1000)) : 0;
  const staleWarning = running && staleSeconds > 90;
  return h('div', {className: 'automation-prep surface'},
    h('div', {className: 'automation-grid'},
      h('label', null, '날짜',
        h('select', {className: 'pm-select', value: selectedDate, disabled: running, onChange: e => setSelectedDate(e.target.value)},
          h('option', {value: ''}, '전체 날짜'),
          ...dates.map(d => h('option', {key: d.upload_date, value: d.upload_date}, `${d.upload_date} (${d.total_count}건)`)),
        ),
      ),
      h('label', null, '정렬',
        h('select', {className: 'pm-select', value: sortOrder, disabled: running, onChange: e => setSortOrder(e.target.value)},
          h('option', {value: 'latest'}, '최신순'),
          h('option', {value: 'oldest'}, '오래된순'),
          h('option', {value: 'random'}, '랜덤'),
        ),
      ),
      h('label', null, '사업자당 1회 처리 개수',
        h('input', {type: 'number', className: 'pm-input-sm automation-input', min: 1, max: 100, value: batchSize, disabled: running,
          onChange: e => setBatchSize(Math.max(1, Math.min(100, Number(e.target.value || 20))))}),
      ),
      h('label', null, '반복 실행 횟수',
        h('input', {type: 'number', className: 'pm-input-sm automation-input', min: 1, max: 50, value: runCount, disabled: running,
          onChange: e => setRunCount(Math.max(1, Math.min(50, Number(e.target.value || 1))))}),
      ),
      h('label', null, '대상',
        h('select', {className: 'pm-select', value: filterMode, disabled: running, onChange: e => setFilterMode(e.target.value)},
          h('option', {value: 'available'}, '미등록만'),
          h('option', {value: 'all'}, '전체'),
        ),
      ),
      h('label', null, '마켓 범위',
        h('select', {className: 'pm-select', value: accountScope, disabled: running, onChange: e => setAccountScope(e.target.value)},
          h('option', {value: '전체'}, 'A/B 전체'),
          h('option', {value: 'A'}, 'A만'),
          h('option', {value: 'B'}, 'B만'),
        ),
      ),
    ),
    h('div', {className: 'automation-suppliers'},
      h('div', {className: 'automation-row-head'},
        h('strong', null, `사업자 ${selectedSuppliers.size}개 선택`),
        h('div', null,
          h('button', {type: 'button', className: 'btn-ghost pm-sm-btn', disabled: running, onClick: () => setSelectedSuppliers(new Set(suppliers.map(s => s.supplier_code)))}, '전체'),
          h('button', {type: 'button', className: 'btn-ghost pm-sm-btn', disabled: running, onClick: () => setSelectedSuppliers(new Set(suppliers.filter(s => s.available_skus > 0).map(s => s.supplier_code)))}, '미등록'),
          h('button', {type: 'button', className: 'btn-ghost pm-sm-btn', disabled: running, onClick: () => setSelectedSuppliers(new Set())}, '해제'),
        ),
      ),
      h('div', {className: 'automation-supplier-list'},
        ...suppliers.map(s => h('label', {key: s.supplier_code, className: 'automation-supplier-item'},
          h('input', {type: 'checkbox', checked: selectedSuppliers.has(s.supplier_code), disabled: running, onChange: () => toggleSupplier(s.supplier_code)}),
          h('span', null, s.supplier_code),
          h('small', null, `${s.available_skus}/${s.total_skus}`),
        )),
      ),
    ),
    h('div', {className: 'automation-summary'},
      h('div', null, h('small', null, '선택 사업자'), h('strong', null, `${selectedSupplierCount.toLocaleString()}개`)),
      h('div', null, h('small', null, '1회 처리량'), h('strong', null, `${perRunTotal.toLocaleString()}개`)),
      h('div', null, h('small', null, '전체 예상 처리'), h('strong', null, `${expectedTotal.toLocaleString()}개`)),
      h('div', null, h('small', null, '예상 시드'), h('strong', null, `${Number(runCount || 0).toLocaleString()}개`)),
      h('div', null, h('small', null, '상태'), h('strong', null, job?.currentStage || '대기')),
      h('div', {className: 'automation-actions'},
        running
          ? h('button', {className: 'btn-ghost', type: 'button', onClick: stopAutomation}, '중지')
          : h('button', {className: 'btn-aurora', type: 'button', onClick: startAutomation}, '자동화 시작'),
      ),
    ),
    job && h('div', {className: 'automation-progress'},
      h('div', {className: 'automation-progress-track'}, h('span', {style: {width: `${progress}%`}})),
      h('div', {className: 'automation-job-meta'},
        h('span', null, job.jobId ? `Job ${job.jobId}` : '대기'),
        job.currentBatch && h('span', null, `${job.currentBatch}/${job.runCount || runCount}회차`),
        job.currentGs && h('span', null, `현재 ${job.currentGs}`),
        job.updatedAt && h('span', {className: staleWarning ? 'is-stale' : ''}, `갱신 ${formatRelativeAge(job.updatedAt)}`),
        h('span', null, `${progress}%`),
      ),
      staleWarning && h('div', {className: 'automation-stale-warning'}, '최근 90초 이상 상태 갱신이 없습니다. 하위 프로세스나 API 응답 지연을 확인하세요.'),
      Array.isArray(job.tail) && job.tail.length > 0 && h('div', {className: 'automation-tail'},
        ...job.tail.slice(-5).map((line, index) => h('code', {key: index}, line)),
      ),
      Array.isArray(job.batches) && job.batches.length > 0 && h('div', {className: 'automation-batches'},
        ...job.batches.map(item => h('div', {key: item.index, className: 'automation-batch-item'},
          h('span', null, `${item.index}회차`),
          h('strong', null, item.seedFileName || '생성 중'),
          h('small', null, `GS ${item.gsCount || 0}개`),
        )),
      ),
    ),
  );
}


function App() {
  const [stage, setStage]     = useState('drop');    // 'drop' | 'basic' | 'keyword' | 'upload' | 'matrix'
  const [dropMode, setDropModeRaw] = useState(null);    // null | 'excel' | 'seed' | 'pm' | 'automation'
  const setDropMode = (mode) => {
    setDropModeRaw(prev => {
      if (mode && !prev) history.pushState({dropMode: mode}, '');
      else if (!mode && prev) { /* going back, popstate handles it */ }
      else if (mode && prev && mode !== prev) history.replaceState({dropMode: mode}, '');
      return mode;
    });
  };
  useEffect(() => {
    const onPop = () => { setDropModeRaw(null); };
    window.addEventListener('popstate', onPop);
    return () => window.removeEventListener('popstate', onPop);
  }, []);
  const [file,  setFile]      = useState(null);
  const [fileMode, setFileMode] = useState('source'); // 'source' | legacy seed review modes | 'seed'
  const [source, setSource]   = useState(null);
  const [selected, setSelected] = useState(null);
  const [selectedGs, setSelectedGs] = useState(new Set());
  const [lastCheckedGs, setLastCheckedGs] = useState(null);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [keywordOptionsOpen, setKeywordOptionsOpen] = useState(false);
  const [keywordOptions, setKeywordOptions] = useState(() => {
    try {
      return {
        ...DEFAULT_KEYWORD_OPTIONS,
        ...JSON.parse(localStorage.getItem('webocr.keywordOptions') || '{}'),
        concurrency: 50,
        imageSize: 1000,
      };
    } catch {
      return DEFAULT_KEYWORD_OPTIONS;
    }
  });
  const [activeChannel, setActiveChannel] = useState('A:네이버');
  const [activeImageProduct, setActiveImageProduct] = useState('');
  const [categoryEditRequest, setCategoryEditRequest] = useState(null);
  const [marketSelection, setMarketSelection] = useState(DEFAULT_MARKET_SELECTION);
  const [seedLibrary, setSeedLibrary] = useState([]);
  const [seedExportBusy, setSeedExportBusy] = useState(false);
  const [sourceSeedJob, setSourceSeedJob] = useState(null);
  const [keywordJob, setKeywordJob] = useState(null);
  const [uploadQueue, setUploadQueue] = useState(() => {
    try {
      return JSON.parse(localStorage.getItem('webocr.uploadQueue') || '{}');
    } catch {
      return {};
    }
  });
  const [uploadHistory, setUploadHistory] = useState(() => {
    try {
      return JSON.parse(localStorage.getItem('webocr.uploadHistory') || '{}');
    } catch {
      return {};
    }
  });
  const [sessionArtifacts, setSessionArtifacts] = useState({ paths: [], jobIds: [] });
  const [appLogs, setAppLogs] = useState([{time:'시작', message:'WebOcrClude 로컬 서버 대기'}]);
  const [stepNavOpen, setStepNavOpen] = useState(false);
  const [peekOpen, setPeekOpen] = useState(false);
  const peekTimerRef = useRef(null);
  const stepSnapshotsRef = useRef(Array(PIPELINE_STEPS.length).fill(''));
  const [snapshotCapturing, setSnapshotCapturing] = useState(false);
  const snapCapLock = useRef(false);
  const stageRef = useRef(stage);
  const fileModeRef = useRef(fileMode);
  stageRef.current = stage;
  fileModeRef.current = fileMode;
  const baseProductRows = React.useMemo(() => rowsForFile(file), [file]);
  const productRows = React.useMemo(() => rowsWithUploadHistory(baseProductRows, uploadHistory), [baseProductRows, uploadHistory]);
  const addLog = (message) => {
    const time = new Date().toLocaleTimeString('ko-KR', { hour12:false });
    setAppLogs((prev) => [...prev.slice(-80), {time, message}]);
  };
  const rememberSessionArtifact = ({ path, jobId } = {}) => {
    setSessionArtifacts((prev) => {
      const paths = path ? Array.from(new Set([...(prev.paths || []), path])) : (prev.paths || []);
      const jobIds = jobId ? Array.from(new Set([...(prev.jobIds || []), jobId])) : (prev.jobIds || []);
      return { paths, jobIds };
    });
  };
  useEffect(() => {
    let alive = true;
    fetch('/api/seeds')
      .then((response) => response.ok ? response.json() : Promise.reject(new Error(`seeds ${response.status}`)))
      .then((payload) => {
        if (alive && payload?.ok) setSeedLibrary(payload.seeds || []);
      })
      .catch((error) => {
        const time = new Date().toLocaleTimeString('ko-KR', { hour12:false });
        setAppLogs((prev) => [...prev.slice(-80), {time, message:`시드 목록 로드 실패: ${error.message}`}]);
      });
    return () => { alive = false; };
  }, []);
  useEffect(() => {
    try {
      localStorage.setItem('webocr.uploadQueue', JSON.stringify(uploadQueue, null, 2));
    } catch {}
  }, [uploadQueue]);
  useEffect(() => {
    try {
      localStorage.setItem('webocr.uploadHistory', JSON.stringify(uploadHistory, null, 2));
    } catch {}
  }, [uploadHistory]);
  useEffect(() => {
    let alive = true;
    fetch('/api/upload-history')
      .then((response) => response.ok ? response.json() : Promise.reject(new Error(`upload-history ${response.status}`)))
      .then((payload) => {
        if (!alive || !payload?.ok || !Array.isArray(payload.items)) return;
        setUploadHistory((prev) => {
          const next = { ...prev };
          payload.items.forEach((item) => {
            const key = historyKey(item.channelKey || item.channel, item.gs);
            if (!key || key === ':') return;
            next[key] = { ...next[key], ...item, historyKey: key };
          });
          return next;
        });
      })
      .catch((error) => addLog(`업로드 이력 동기화 실패: ${error.message}`));
    return () => { alive = false; };
  }, [file?.sourcePath, file?.name]);

  const counts = React.useMemo(() => {
    const out = {
      A: { total: 0, targeted: 0, uploaded: 0 },
      B: { total: 0, targeted: 0, uploaded: 0 },
      all: { total: 0, targeted: 0, uploaded: 0 },
    };
    productRows.forEach(r => {
      ['A', 'B'].forEach((account) => {
        r[account].forEach((status) => {
          out[account].total += 1;
          out.all.total += 1;
          if (status === 'targeted') {
            out[account].targeted += 1;
            out.all.targeted += 1;
          }
          if (status === 'uploaded') {
            out[account].uploaded += 1;
            out.all.uploaded += 1;
          }
        });
      });
    });
    return out;
  }, [productRows]);

  const onDrop = async (inputFile) => {
    if (!inputFile) return;
    const seed = isSeedFile(inputFile);
    addLog(`${inputFile.name} 불러오기 시작`);
    const nextFile = {
      name: inputFile.name,
      size: inputFile.size || 0,
      kind: seed ? '시드 파일' : '소스 파일',
      rows: 0,
      gsCodes: 0,
      preview: [],
    };

    setFile(nextFile);
    setFileMode(seed ? 'seed' : 'source');
    setSource({ name:nextFile.name, count:0, importedAt:'방금' });
    setSelected(null);
    setSelectedGs(new Set());
    setLastCheckedGs(null);
    setStage('basic');
    setSourceSeedJob(null);

    if (seed) {
      try {
        const seedPayload = JSON.parse(await inputFile.text());
        const seedFile = seedPayloadToFile(seedPayload, {
          name: inputFile.name,
          size: inputFile.size,
          kind: '시드 파일',
        });
        setFile(seedFile);
        setFileMode('seed');
        setSource({ name:seedFile.name, count:seedFile.rows, importedAt:seedPayload.createdAt || '방금' });
        setSelectedGs(new Set(seedFile.preview.map((row) => row.gs)));
        addLog(`시드 로드 완료: 상품 ${seedFile.gsCodes}개`);
        setTimeout(() => captureAllSnapshots(), 800);
      } catch (error) {
        addLog(`시드 파일 읽기 실패: ${error.message}`);
        setSourceSeedJob({ status:'failed', action:'seedLoad', error:error.message });
      }
      return;
    }

    if (inputFile && !seed) {
      try {
        const form = new FormData();
        form.append('file', inputFile);
        const response = await fetch('/api/import-source', { method: 'POST', body: form });
        const uploaded = await response.json();
        if (!response.ok || !uploaded?.ok) throw new Error(uploaded?.error || `upload ${response.status}`);
        rememberSessionArtifact({ path: uploaded.path });
        const parsed = uploaded.parsed || {};
        const realPreview = parsed.preview || [];
        const realFile = {
          ...nextFile,
          sourcePath: uploaded.path,
          uploadId: uploaded.uploadId,
          size: uploaded.size || nextFile.size,
          rows: parsed.rows ?? nextFile.rows,
          gsCodes: parsed.gsCodes ?? nextFile.gsCodes,
          preview: realPreview.length ? realPreview : nextFile.preview,
          columns: parsed.columns || [],
        };
        setFile((current) => current ? {
          ...current,
          ...realFile,
        } : realFile);
        setSource({
          name:nextFile.name,
          count:realFile.rows,
          importedAt:'2026-05-12 11:40',
          sourcePath: uploaded.path,
        });
        setSelectedGs(new Set((realFile.preview || []).map((row) => row.gs)));
        addLog(`파싱 완료: ${realFile.rows}행 · GS ${realFile.gsCodes}개 · 미리보기 ${realFile.preview.length}개`);
        setTimeout(() => captureAllSnapshots(), 800);
      } catch (error) {
        addLog(`원본 파일 서버 저장/파싱 실패: ${error.message}`);
        setSourceSeedJob({
          status: 'failed',
          action: 'sourceUpload',
          error: `원본 파일 서버 저장 실패: ${error.message}`,
        });
      }
    }
  };
  const onPmImport = (data) => {
    const parsed = data.parsed || {};
    const realPreview = parsed.preview || [];
    const realFile = {
      name: data.fileName,
      size: data.size || 0,
      kind: '소스 파일',
      sourcePath: data.path,
      uploadId: data.uploadId,
      rows: parsed.rows ?? 0,
      gsCodes: parsed.gsCodes ?? 0,
      preview: realPreview,
      columns: parsed.columns || [],
    };
    rememberSessionArtifact({ path: data.path });
    setFile(realFile);
    setFileMode('source');
    setSource({ name: data.fileName, count: realFile.rows, importedAt: '방금', sourcePath: data.path });
    setSelected(null);
    setSelectedGs(new Set(realPreview.map(r => r.gs)));
    setLastCheckedGs(null);
    setStage('basic');
    setSourceSeedJob(null);
    addLog(`PM 소스 로드: ${realFile.rows}행 · GS ${realFile.gsCodes}개 · SKU ${data.skuCount}개`);
    setTimeout(() => captureAllSnapshots(), 800);
  };
  const onImportClick = () => { setStage('drop'); setDropModeRaw(null); };
  const resetWorkspace = async () => {
    const hasWork = file || sourceSeedJob || keywordJob || Object.keys(uploadQueue || {}).length || Object.keys(uploadHistory || {}).length;
    if (hasWork && !window.confirm('현재까지 진행과정을 초기화합니다.\n진행 중인 작업은 중지하고, 이번 세션에서 만든 임시 원본/job/시드/export 파일을 삭제합니다.')) return;
    const cleanupPayload = {
      jobIds: Array.from(new Set([
        ...(sessionArtifacts.jobIds || []),
        sourceSeedJob?.jobId,
        keywordJob?.jobId,
      ].filter(Boolean))),
      paths: Array.from(new Set([
        ...(sessionArtifacts.paths || []),
      ].filter(Boolean))),
    };
    let cleanupMessage = '작업 상태 초기화';
    if (cleanupPayload.jobIds.length || cleanupPayload.paths.length) {
      try {
        const response = await fetch('/api/workspace-reset', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(cleanupPayload),
        });
        const payload = await response.json();
        if (!response.ok || !payload?.ok) throw new Error(payload?.error || `reset ${response.status}`);
        setSeedLibrary(payload.seeds || []);
        cleanupMessage = `작업 상태 초기화 · 삭제 ${payload.cleanup?.deleted || 0}개 · 중지 ${payload.cleanup?.stoppedJobs?.length || 0}개`;
      } catch (error) {
        cleanupMessage = `작업 상태 초기화 · 서버 정리 실패: ${error.message}`;
      }
    }
    setStage('drop');
    setDropModeRaw(null);
    setFile(null);
    setFileMode('source');
    setSource(null);
    setSelected(null);
    setSettingsOpen(false);
    setKeywordOptionsOpen(false);
    setSourceSeedJob(null);
    setKeywordJob(null);
    setUploadQueue({});
    setUploadHistory({});
    setSessionArtifacts({ paths: [], jobIds: [] });
    setMarketSelection(DEFAULT_MARKET_SELECTION);
    setSelectedGs(new Set());
    setLastCheckedGs(null);
    try {
      localStorage.removeItem('webocr.uploadQueue');
      localStorage.removeItem('webocr.uploadHistory');
      localStorage.removeItem('webocr.pipeline.sourceToSeed');
      localStorage.removeItem('webocr.pipeline.keywordSeed');
      localStorage.removeItem('webocr.pipeline.marketUpload');
    } catch {}
    setAppLogs([{time:'초기화', message: cleanupMessage}]);
  };
  const switchView = (view) => {
    if (!file) return;
    setSelected(null);
    setStage(view);
  };
  const pollSourceSeedJob = (jobId) => {
    window.setTimeout(async () => {
      try {
        const response = await fetch(`/api/jobs/${jobId}`);
        const job = await response.json();
        if (!response.ok || !job?.ok) throw new Error(job?.error || `job ${response.status}`);
        setSourceSeedJob(job);
        if (job.status === 'completed' && job.result) {
          addLog(`1차 시드 생성 완료: ${job.result.seedFileName}`);
          rememberSessionArtifact({ path: job.result.seedPath });
          const seedPayload = await fetchSeedPayload(job.result.seedPath);
          const seedFile = seedPayloadToFile(seedPayload, {
            name: job.result.seedFileName,
            kind: '시드 파일',
            size: job.result.seedSize || 0,
            path: job.result.seedPath,
          });
          const seedRecord = {
            id: `seed-${job.jobId}`,
            name: seedFile.name,
            createdAt: job.finishedAt || '2026-05-12 11:40',
            rows: seedFile.rows,
            gsCodes: seedFile.gsCodes,
            size: seedFile.size,
            thumbnail: seedFile.preview?.[0]?.thumb,
            path: job.result.seedPath,
          };
          setFile(seedFile);
          setFileMode('seed');
          setSource({ name:seedFile.name, count:seedFile.rows, importedAt:seedRecord.createdAt, sourcePath: job.result.seedPath });
          setSeedLibrary((prev) => [seedRecord, ...prev.filter((item) => item.name !== seedRecord.name)]);
          setSelectedGs(new Set(seedFile.preview.map((row) => row.gs)));
          return;
        }
        if (job.status !== 'failed') pollSourceSeedJob(jobId);
        if (job.status === 'failed') addLog(`1차 시드 생성 실패: ${job.error || 'unknown error'}`);
      } catch (error) {
        addLog(`작업 상태 조회 실패: ${error.message}`);
        setSourceSeedJob({ jobId, status: 'failed', error: error.message });
      }
    }, 1500);
  };
  const createSeedFile = async () => {
    if (!file) return;
    if (selectedGs.size === 0) {
      addLog('1차 시드 생성 중단: 선택된 상품이 없습니다.');
      setSourceSeedJob({
        status: 'failed',
        action: 'sourceToSeed',
        createdAt: '방금',
        error: '선택된 상품이 없습니다.',
        tail: ['상품을 1개 이상 선택한 뒤 다시 실행하세요.'],
      });
      return;
    }
    const payload = window.WEBOCR_PIPELINE?.buildSourceToSeedPayload?.({
      file,
      selectedGs,
      options: keywordOptions,
    });
    const actualPayload = {
      ...payload,
      sourcePath: file.sourcePath || source?.sourcePath || '',
      sourceFilePath: file.sourcePath || source?.sourcePath || '',
    };
    storePipelinePayload('webocr.pipeline.sourceToSeed', actualPayload);
    if (!actualPayload.sourcePath) {
      setSourceSeedJob({ status: 'failed', error: '원본 파일이 아직 서버에 저장되지 않았습니다. 파일을 다시 드래그해서 넣어주세요.' });
      return;
    }
    setSourceSeedJob({
          status: 'queued',
          action: 'sourceToSeed',
          createdAt: '방금',
          progressPercent: 2,
          currentStage: '선택 원본 준비',
          tail: [`선택 상품 ${selectedGs.size}개 기준으로 필터 원본 생성 중`],
        });
    addLog(`1차 시드 생성 요청 전송: 선택 ${selectedGs.size}개`);
    try {
      const response = await fetch('/api/source-to-seed', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(actualPayload),
      });
      const job = await response.json();
      if (!response.ok || !job?.ok) throw new Error(job?.error || `job ${response.status}`);
      setSourceSeedJob(job);
      rememberSessionArtifact({ jobId: job.jobId });
      addLog(`서버 작업 생성: ${job.jobId}`);
      pollSourceSeedJob(job.jobId);
    } catch (error) {
      addLog(`1차 시드 생성 요청 실패: ${error.message}`);
      setSourceSeedJob({ status: 'failed', action: 'sourceToSeed', error: error.message });
    }
  };
  const loadSecondSeed = () => {
    if (!file) return;
    setFile({
      ...file,
      name: file.name.replace(/\.webseed\.json$/i, '.review.webseed.json'),
      kind: '2차 수정 시드',
    });
    setFileMode('seed');
  };
  const startMarketWork = () => {
    setFileMode('seed');
  };
  const loadSeedFromLibrary = async (seed) => {
    try {
      addLog(`시드 파일 로드: ${seed.name}`);
      const seedPayload = await fetchSeedPayload(seed.path);
      const seedFile = seedPayloadToFile(seedPayload, {
        name: seed.name,
        kind: '시드 파일',
        size: seed.size,
        path: seed.path,
      });
      setFile(seedFile);
      setFileMode('seed');
      setSource({ name:seedFile.name, count:seedFile.rows, importedAt:seed.createdAt, sourcePath: seed.path });
      setSelected(null);
      setSelectedGs(new Set(seedFile.preview.map((row) => row.gs)));
      setLastCheckedGs(null);
      setStage('basic');
      setTimeout(() => captureAllSnapshots(), 800);
    } catch (error) {
      addLog(`시드 파일 로드 실패: ${error.message}`);
      setSourceSeedJob({ status:'failed', action:'seedLoad', error:error.message });
    }
  };
  const deleteSeedFromLibrary = async (seed) => {
    if (!seed?.path) return;
    if (!window.confirm(`${seed.name}\n\n이 시드 파일을 삭제할까요?`)) return;
    try {
      const response = await fetch('/api/seed-action', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action:'delete', path: seed.path }),
      });
      const payload = await response.json();
      if (!response.ok || !payload?.ok) throw new Error(payload?.error || `delete ${response.status}`);
      setSeedLibrary(payload.seeds || []);
      if (file?.sourcePath === seed.path) {
        setFile(null);
        setSource(null);
        setSelectedGs(new Set());
        setStage('drop');
        setDropModeRaw(null);
      }
      addLog(`시드 삭제: ${seed.name}`);
    } catch (error) {
      addLog(`시드 삭제 실패: ${error.message}`);
      setSourceSeedJob({ status:'failed', action:'seedDelete', error:error.message });
    }
  };
  const renameSeedFromLibrary = async (seed) => {
    if (!seed?.path) return;
    const nextName = window.prompt('새 시드 파일명', seed.name);
    if (!nextName || nextName === seed.name) return;
    try {
      const response = await fetch('/api/seed-action', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action:'rename', path: seed.path, newName: nextName }),
      });
      const payload = await response.json();
      if (!response.ok || !payload?.ok) throw new Error(payload?.error || `rename ${response.status}`);
      setSeedLibrary(payload.seeds || []);
      if (file?.sourcePath === seed.path && payload.seed) {
        setFile((current) => current ? { ...current, name: payload.seed.name, sourcePath: payload.seed.path } : current);
        setSource((current) => current ? { ...current, name: payload.seed.name, sourcePath: payload.seed.path } : current);
      }
      addLog(`시드 이름 수정: ${seed.name} → ${payload.seed?.name || nextName}`);
    } catch (error) {
      addLog(`시드 이름 수정 실패: ${error.message}`);
      setSourceSeedJob({ status:'failed', action:'seedRename', error:error.message });
    }
  };
  const exportCombinedSeeds = async () => {
    if (seedExportBusy) return;
    setSeedExportBusy(true);
    try {
      const response = await fetch('/api/seeds-export', { method: 'POST' });
      const payload = await response.json();
      if (!response.ok || !payload?.ok) throw new Error(payload?.error || `seed export ${response.status}`);
      const exportInfo = payload.export || {};
      rememberSessionArtifact({ path: exportInfo.path });
      addLog(`시드 통합 엑셀 생성: ${exportInfo.fileName} · ${exportInfo.count || 0}개 상품`);
      try {
        await fetch('/api/open-export-path', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ path: exportInfo.downloadsPath || exportInfo.path, folder: true }),
        });
      } catch {}
      window.alert(`시드 통합 엑셀 생성 완료\n${exportInfo.downloadsPath || exportInfo.path}`);
    } catch (error) {
      addLog(`시드 통합 엑셀 생성 실패: ${error.message}`);
      window.alert(`시드 통합 엑셀 생성 실패\n${error.message}`);
    } finally {
      setSeedExportBusy(false);
    }
  };
  const toggleMarket = (key) => {
    setMarketSelection((prev) => ({ ...prev, [key]: prev[key] === false }));
  };
  const queueUploadItem = (items) => {
    const list = Array.isArray(items) ? items : [items];
    const validItems = list.filter((item) => item?.gs && item?.channelKey);
    if (!validItems.length) return;
    const queuedAt = new Date().toISOString();
    setUploadQueue((prev) => {
      const next = { ...prev };
      validItems.forEach((item) => {
        const key = uploadQueueKey(item.channelKey, item.gs);
        next[key] = {
          ...next[key],
          ...item,
          queueKey: key,
          status: 'queued',
          queuedAt,
        };
      });
      return next;
    });
    addLog(`업로드 대기열 반영: ${validItems.length}개`);
    setUploadHistory((prev) => {
      const next = { ...prev };
      validItems.forEach((item) => {
        const key = historyKey(item.channelKey, item.gs);
        next[key] = {
          ...next[key],
          ...item,
          historyKey: key,
          status: 'queued',
          updatedAt: queuedAt,
        };
      });
      return next;
    });
    setStage('upload');
  };
  const updateUploadHistory = (items, status) => {
    const list = Array.isArray(items) ? items : [items];
    const validItems = list.filter((item) => item?.gs && item?.channelKey);
    if (!validItems.length) return;
    const updatedAt = new Date().toISOString();
    setUploadHistory((prev) => {
      const next = { ...prev };
      validItems.forEach((item) => {
        const key = historyKey(item.channelKey, item.gs);
        next[key] = {
          ...next[key],
          ...item,
          historyKey: key,
          status,
          updatedAt,
        };
      });
      return next;
    });
  };
  const openCategoryFixFromResult = ({ row, channel } = {}) => {
    if (!row?.gs || !channel?.key) return;
    setSelectedGs((prev) => {
      const next = new Set(prev);
      next.add(row.gs);
      return next;
    });
    setActiveChannel(channel.key);
    setActiveImageProduct(row.id || row.gs);
    setCategoryEditRequest({
      id: `${Date.now()}:${channel.key}:${row.gs}`,
      channelKey: channel.key,
      productId: row.id || '',
      gs: row.gs,
    });
    setStage('keyword');
    addLog(`롯데ON 카테고리 수정 이동: ${channel.key} ${row.gs}`);
  };
  const openKeywordOptions = () => {
    setKeywordOptionsOpen(true);
  };
  const generateKeywords = () => {
    startKeywordRun(keywordOptions);
  };
  const applyCompletedKeywordJob = async (job) => {
    addLog(`키워드 생성 완료: 상품 ${job.result.products}개 · 채널 결과 ${job.result.generatedChannels}개`);
    const seedPayload = await fetchSeedPayload(job.result.seedPath);
    const seedFile = seedPayloadToFile(seedPayload, {
      name: file?.name || 'keyword.webseed.json',
      kind: '마켓 키워드 시드',
      size: 0,
      path: job.result.seedPath,
    });
    setFile((current) => ({
      ...seedFile,
      name: current?.name || seedFile.name,
      sourcePath: job.result.seedPath,
    }));
    setSource((current) => current ? { ...current, sourcePath: job.result.seedPath } : current);
    setKeywordJob(job);
  };
  const pollKeywordJob = (jobId) => {
    window.setTimeout(async () => {
      try {
        const response = await fetch(`/api/jobs/${jobId}`);
        const job = await response.json();
        if (!response.ok || !job?.ok) throw new Error(job?.error || `job ${response.status}`);
        setKeywordJob(job);
        if (job.status === 'completed' && job.result) {
          await applyCompletedKeywordJob(job);
          return;
        }
        if (job.status !== 'failed') pollKeywordJob(jobId);
        if (job.status === 'failed') addLog(`키워드 생성 실패: ${job.error || 'unknown error'}`);
      } catch (error) {
        addLog(`키워드 작업 상태 조회 실패: ${error.message}`);
        setKeywordJob({ jobId, status:'failed', error:error.message });
      }
    }, 1800);
  };
  useEffect(() => {
    if (!keywordJob?.jobId || !['queued', 'running'].includes(keywordJob.status)) return undefined;
    let cancelled = false;
    const timer = window.setInterval(async () => {
      try {
        const response = await fetch(`/api/jobs/${keywordJob.jobId}`);
        const job = await response.json();
        if (cancelled || !response.ok || !job?.ok) return;
        setKeywordJob(job);
        if (job.status === 'completed' && job.result) {
          await applyCompletedKeywordJob(job);
        }
        if (job.status === 'failed') addLog(`키워드 생성 실패: ${job.error || 'unknown error'}`);
      } catch (error) {
        if (!cancelled) addLog(`키워드 작업 재확인 실패: ${error.message}`);
      }
    }, 2500);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [keywordJob?.jobId, keywordJob?.status, file?.name]);
  const startKeywordRun = async (options) => {
    const nextOptions = {
      ...options,
      concurrency: 50,
      imageSize: 1000,
    };
    const payload = window.WEBOCR_PIPELINE?.buildKeywordRunPayload?.({
      file,
      selectedGs,
      options: nextOptions,
      marketSelection,
    });
    const actualPayload = {
      ...payload,
      seedPath: file?.sourcePath || source?.sourcePath || '',
      sourcePath: file?.sourcePath || source?.sourcePath || '',
      options: nextOptions,
    };
    storePipelinePayload('webocr.pipeline.keywordSeed', actualPayload);
    setKeywordOptions(nextOptions);
    try {
      localStorage.setItem('webocr.keywordOptions', JSON.stringify(nextOptions));
    } catch {}
    const firstChannel = getScopedMarketKeys(marketSelection, nextOptions.accountScope)[0] || 'A:네이버';
    const firstRow = productRows.find((row) => selectedGs.has(row.gs));
    setKeywordOptionsOpen(false);
    setSelected(null);
    setActiveChannel(firstChannel);
    setActiveImageProduct(firstRow?.id || '');
    setStage('keyword');
    if (!actualPayload.seedPath) {
      addLog('키워드 생성 중단: 시드 파일 경로가 없습니다.');
      setKeywordJob({ status:'failed', action:'keywordGenerate', error:'시드 파일 경로가 없습니다. 시드를 다시 불러와 주세요.' });
      return;
    }
    if (selectedGs.size === 0) {
      addLog('키워드 생성 중단: 선택된 상품이 없습니다.');
      setKeywordJob({ status:'failed', action:'keywordGenerate', error:'선택된 상품이 없습니다.' });
      return;
    }
    setKeywordJob({
      status:'queued',
      action:'keywordGenerate',
      createdAt:'방금',
      progressPercent:1,
      currentStage:'Codex 키워드 생성 대기',
      selectedGs:Array.from(selectedGs),
      channels:actualPayload.channels || [],
    });
    addLog(`키워드 생성 요청 전송: 상품 ${selectedGs.size}개 · 채널 ${(actualPayload.channels || []).length}개`);
    try {
      const response = await fetch('/api/keyword-generate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(actualPayload),
      });
      const job = await response.json();
      if (!response.ok || !job?.ok) throw new Error(job?.error || `job ${response.status}`);
      setKeywordJob(job);
      rememberSessionArtifact({ jobId: job.jobId });
      addLog(`키워드 서버 작업 생성: ${job.jobId}`);
      pollKeywordJob(job.jobId);
    } catch (error) {
      addLog(`키워드 생성 요청 실패: ${error.message}`);
      setKeywordJob({ status:'failed', action:'keywordGenerate', error:error.message });
    }
  };
  const selectAllProducts = () => {
    setSelectedGs(new Set(productRows.map((row) => row.gs)));
  };
  const clearAllProducts = () => {
    setSelectedGs(new Set());
    setLastCheckedGs(null);
  };
  const toggleProductSelection = (gs, index, event, rows = productRows) => {
    const rangeStart = rows.findIndex((row) => row.gs === lastCheckedGs);
    setSelectedGs((prev) => {
      const next = new Set(prev);
      if (event?.shiftKey && rangeStart >= 0) {
        const start = Math.min(rangeStart, index);
        const end = Math.max(rangeStart, index);
        rows.slice(start, end + 1).forEach((row) => next.add(row.gs));
      } else if (next.has(gs)) {
        next.delete(gs);
      } else {
        next.add(gs);
      }
      return next;
    });
    setLastCheckedGs(gs);
  };
  const getFlowKey = () => {
    if (stage === 'drop') return 'drop';
    if (stage === 'keyword') return 'keyword';
    if (stage === 'upload') return 'upload';
    if (stage === 'result') return 'result';
    if (fileMode === 'source') return 'source';
    return 'seed-setup';
  };
  const currentFlowKey = getFlowKey();
  const currentFlowIndex = Math.max(0, PIPELINE_STEPS.findIndex((step) => step.key === currentFlowKey));

  useEffect(() => {
    if (snapCapLock.current) return;
    const tid = setTimeout(() => {
      const el = document.querySelector('.main');
      if (el) stepSnapshotsRef.current[currentFlowIndex] = el.innerHTML;
    }, 600);
    return () => clearTimeout(tid);
  }, [currentFlowIndex, stage, fileMode, file, selected]);

  const captureAllSnapshots = useCallback(async () => {
    if (snapCapLock.current) return;
    snapCapLock.current = true;
    const origStage = stageRef.current;
    const origFileMode = fileModeRef.current;
    setSnapshotCapturing(true);
    const steps = [
      ['drop', null],
      ['basic', 'source'],
      ['basic', 'seed'],
      ['keyword', null],
      ['upload', null],
      ['result', null],
    ];
    const waitFrame = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
    for (let i = 0; i < steps.length; i++) {
      const [s, fm] = steps[i];
      ReactDOM.flushSync(() => {
        setStage(s);
        if (fm) setFileMode(fm);
      });
      await waitFrame();
      await waitFrame();
      const el = document.querySelector('.main');
      if (el) stepSnapshotsRef.current[i] = el.innerHTML;
    }
    ReactDOM.flushSync(() => {
      setStage(origStage);
      setFileMode(origFileMode);
    });
    await waitFrame();
    setSnapshotCapturing(false);
    snapCapLock.current = false;
  }, []);

  const sourceSeedRunning = ['queued', 'running'].includes(sourceSeedJob?.status);
  const keywordRunning = ['queued', 'running'].includes(keywordJob?.status);
  const selectedProductRows = productRows.filter((row) => selectedGs.has(row.gs));
  const keywordHasResults = selectedProductRows.length > 0 && selectedProductRows.some((row) => {
    const marketKeywords = row.marketKeywords || row.seedProduct?.marketKeywords || {};
    return Object.keys(marketKeywords).length > 0;
  });
  const canUseSeedKeywords = keywordHasResults;
  const sourceSeedProgress = Math.max(0, Math.min(100, Number(
    sourceSeedJob?.progressPercent
      ?? (sourceSeedJob?.status === 'completed' ? 100 : sourceSeedJob?.status === 'failed' ? 100 : 0)
  )));
  const sourceSeedStage = sourceSeedJob?.currentStage
    || (sourceSeedRunning ? '1차 시드 생성 실행중' : sourceSeedJob?.status === 'completed' ? '1차 시드 생성 완료' : '');
  const keywordProgress = Math.max(0, Math.min(100, Number(
    keywordJob?.progressPercent
      ?? (keywordJob?.status === 'completed' ? 100 : keywordJob?.status === 'failed' ? 100 : 0)
  )));
  const keywordStage = keywordJob?.currentStage
    || (keywordRunning ? '키워드 생성 실행중' : keywordJob?.status === 'completed' ? '키워드 생성 완료' : '');
  const moveToFlowStep = (step) => {
    setSelected(null);
    if (!step) return;
    setStage(step.stage);
    if (step.fileMode) setFileMode(step.fileMode);
  };
  const goBack = () => {
    moveToFlowStep(PIPELINE_STEPS[currentFlowIndex - 1]);
  };
  const stopKeywordJobIfRunning = async () => {
    if (!keywordRunning || !keywordJob?.jobId) return;
    try {
      await fetch('/api/job-stop', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobId: keywordJob.jobId }),
      });
      addLog(`키워드 생성 중지: ${keywordJob.jobId}`);
    } catch (error) {
      addLog(`키워드 생성 중지 실패: ${error.message}`);
    }
  };
  const moveToKeywordReviewWithSeedKeywords = async () => {
    await stopKeywordJobIfRunning();
    setKeywordOptionsOpen(false);
    setKeywordJob({
      status: 'completed',
      action: 'keywordGenerate',
      progressPercent: 100,
      currentStage: '시드 저장 키워드 사용',
      skippedGeneration: true,
    });
    addLog('시드에 저장된 상품명/검색어 사용: 키워드 검수/수정 화면으로 이동');
    setStage('keyword');
  };
  const goForward = async () => {
    setSelected(null);
    if (currentFlowKey === 'drop') {
      moveToFlowStep(PIPELINE_STEPS[currentFlowIndex + 1]);
      return;
    }
    if (currentFlowKey === 'source') {
      createSeedFile();
      return;
    }
    if (currentFlowKey === 'seed-setup') {
      if (canUseSeedKeywords) {
        await moveToKeywordReviewWithSeedKeywords();
        return;
      }
      generateKeywords();
      return;
    }
    if (currentFlowKey === 'keyword') {
      if (!canUseSeedKeywords) {
        generateKeywords();
        return;
      }
      moveToFlowStep(PIPELINE_STEPS[currentFlowIndex + 1]);
      return;
    }
    if (currentFlowKey === 'upload') {
      setStage('result');
      return;
    }
    if (currentFlowKey === 'result') {
      moveToFlowStep(PIPELINE_STEPS[0]);
      return;
    }
  };
  const navState = (() => {
    const currentStep = PIPELINE_STEPS[currentFlowIndex] || PIPELINE_STEPS[0];
    const previousStep = PIPELINE_STEPS[currentFlowIndex - 1];
    const forwardLabel = !file
      ? '파일 대기'
      : currentFlowKey === 'source'
      ? '1차 시드 생성'
      : currentFlowKey === 'seed-setup'
        ? canUseSeedKeywords ? '키워드 검수' : '키워드 생성'
            : currentFlowKey === 'keyword'
              ? canUseSeedKeywords ? '업로드 화면' : '키워드 생성 시작'
              : currentFlowKey === 'upload'
                ? '결과 확인'
              : currentFlowKey === 'result'
                ? '처음으로'
              : PIPELINE_STEPS[currentFlowIndex + 1]?.title || '다음';
    return {
      title: currentStep.title,
      backLabel: previousStep?.title || '처음 단계',
      forwardLabel: sourceSeedRunning ? `생성 중 ${sourceSeedProgress}%` : keywordRunning && !canUseSeedKeywords ? `생성 중 ${keywordProgress}%` : forwardLabel,
      canBack: currentFlowIndex > 0,
      canForward: !!file && !sourceSeedRunning && (!keywordRunning || canUseSeedKeywords),
    };
  })();

  const product = selected && (() => {
    const r = productRows.find(x => x.id === selected);
    return r ? { ...r, originalName:r.originalName || r.name, history:r.history || [] } : null;
  })();
  const basicCrumb = fileMode === 'source'
    ? 'STEP 02 · 원본 기본화면 · source_products'
    : 'STEP 03 · 시드 검수/마켓 선택 · seed_setup';

  return (
    <div className="app">
      <TopBar
        source={source?.name}
        onImport={onImportClick}
        onReset={resetWorkspace}
        onSettings={() => setSettingsOpen(true)}/>
      <div className="body">
        <Sidebar
          source={source}
          seedFiles={seedLibrary}
          seedStorePath={SEED_STORE_PATH}
          onLoadSeed={loadSeedFromLibrary}
          onRenameSeed={renameSeedFromLibrary}
          onDeleteSeed={deleteSeedFromLibrary}
          onExportSeeds={exportCombinedSeeds}
          seedExportBusy={seedExportBusy}/>

        <main className="main">
          {stage === 'drop' && !dropMode && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 01 · 원본 입력</span>
                  <h2>소스 불러오기 방법 선택</h2>
                </div>
              </div>
              <div className="entry-cards">
                <button type="button" className="entry-card" onClick={() => setDropMode('excel')}>
                  <div className="entry-card-icon"><IconUpload size={28}/></div>
                  <h3>엑셀 데이터 불러오기</h3>
                  <p>CSV, XLSX, XLS 파일을 드래그하거나 선택</p>
                  <div className="entry-card-formats"><code>.csv</code><code>.xlsx</code><code>.xls</code></div>
                </button>
                <button type="button" className="entry-card" onClick={() => setDropMode('seed')}>
                  <div className="entry-card-icon"><IconFile size={28}/></div>
                  <h3>기존 시드 불러오기</h3>
                  <p>이전에 생성한 시드 파일에서 이어서 작업</p>
                  <div className="entry-card-formats"><code>.webseed.json</code><span className="color-muted" style={{fontSize:'0.8rem'}}>{seedLibrary.length}개 저장됨</span></div>
                </button>
                <button type="button" className="entry-card" onClick={() => setDropMode('pm')}>
                  <div className="entry-card-icon"><IconGrid size={28}/></div>
                  <h3>ProductManager 상품 선택</h3>
                  <p>사업자별·날짜별 미등록 상품을 골라서 가져오기</p>
                  <div className="entry-card-formats"><span className="color-muted" style={{fontSize:'0.8rem'}}>DB 직접 연동</span></div>
                </button>
                <button type="button" className="entry-card" onClick={() => setDropMode('automation')}>
                  <div className="entry-card-icon"><IconSync size={28}/></div>
                  <h3>자동화 작업</h3>
                  <p>지정 개수 단위로 업로드 직전 시드까지 자동 생성</p>
                  <div className="entry-card-formats"><span className="color-muted" style={{fontSize:'0.8rem'}}>배치 저장</span></div>
                </button>
              </div>
            </>
          )}
          {stage === 'drop' && dropMode === 'excel' && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 01 · 원본 입력</span>
                  <h2>엑셀 데이터 불러오기</h2>
                </div>
                <div className="right">
                  <GhostBtn onClick={() => history.back()}>← 돌아가기</GhostBtn>
                </div>
              </div>
              <DropZone onDrop={onDrop}/>
            </>
          )}
          {stage === 'drop' && dropMode === 'seed' && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 01 · 원본 입력</span>
                  <h2>기존 시드 불러오기</h2>
                </div>
                <div className="right">
                  <GhostBtn onClick={() => history.back()}>← 돌아가기</GhostBtn>
                </div>
              </div>
              <div className="seed-picker surface">
                {seedLibrary.length > 0 ? (
                  <div className="seed-picker-list">
                    {seedLibrary.map(seed => (
                      <button key={seed.id} type="button" className="seed-picker-item" onClick={() => loadSeedFromLibrary(seed)}>
                        <ProductThumb src={seed.thumbnail} compact/>
                        <div className="seed-picker-info">
                          <strong>{seed.name}</strong>
                          <small>{seed.createdAt} · {seed.rows}행 · GS {seed.gsCodes}개 · {seed.progress?.badge || '전처리 완료'}</small>
                        </div>
                      </button>
                    ))}
                  </div>
                ) : (
                  <div className="pad-lg color-muted" style={{textAlign:'center'}}>저장된 시드 파일이 없습니다.<br/>먼저 엑셀에서 소스를 불러와 시드를 생성하세요.</div>
                )}
              </div>
            </>
          )}
          {stage === 'drop' && dropMode === 'pm' && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 01 · 원본 입력</span>
                  <h2>ProductManager 상품 선택</h2>
                </div>
                <div className="right">
                  <GhostBtn onClick={() => history.back()}>← 돌아가기</GhostBtn>
                </div>
              </div>
              <ProductManagerBrowser onImport={onPmImport} addLog={addLog}/>
            </>
          )}
          {stage === 'drop' && dropMode === 'automation' && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 01 · 원본 입력</span>
                  <h2>자동화 작업</h2>
                </div>
                <div className="right">
                  <GhostBtn onClick={() => history.back()}>← 돌아가기</GhostBtn>
                </div>
              </div>
              <AutomationPrepWorkbench addLog={addLog} onSeedsChange={setSeedLibrary}/>
            </>
          )}

          {stage === 'basic' && file && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">{basicCrumb}</span>
                  <h2>{file.name}</h2>
                </div>
                <ViewSwitch view="basic" onChange={switchView}/>
              </div>
              <ImportPreview
                file={file}
                mode={fileMode}
                selectedGs={selectedGs}
                sourceJob={sourceSeedJob}
                onToggleProduct={toggleProductSelection}
                onSelectAll={selectAllProducts}
                onClearAll={clearAllProducts}
                onUpdateImages={(gs, newImages) => {
                  const seedPath = file?.sourcePath || '';
                  setFile(prev => {
                    if (!prev) return prev;
                    const preview = (prev.preview || []).map(r => {
                      if (r.gs !== gs) return r;
                      const updated = { ...r, images: newImages, thumb: newImages.representative || r.thumb };
                      if (updated.seedProduct) {
                        updated.seedProduct = { ...updated.seedProduct, images: newImages };
                      }
                      return updated;
                    });
                    return { ...prev, preview };
                  });
                  if (seedPath) {
                    fetch('/api/seed-update-images', {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json' },
                      body: JSON.stringify({ path: seedPath, gs, images: newImages }),
                    })
                      .then(async (response) => {
                        const payload = await response.json().catch(() => ({}));
                        if (!response.ok || !payload?.ok) throw new Error(payload?.error || `seed-update ${response.status}`);
                        addLog(`대표 이미지 저장: ${gs}`);
                      })
                      .catch((error) => addLog(`대표 이미지 저장 실패: ${gs} · ${error.message}`));
                  }
                }}/>
            </>
          )}

          {stage === 'matrix' && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 03 · 마켓 분기 · listing_status</span>
                  <h2>상품 매트릭스</h2>
                </div>
                <AccountSummary counts={counts}/>
                <div className="summary">
                  <div><small>총</small><strong>{counts.all.total}</strong></div>
                  <div><small>등록</small><strong style={{color:'var(--color-night-violet)'}}>{counts.all.targeted}</strong></div>
                  <div><small>업로드 완료</small><strong style={{color:'var(--color-quizlet-violet)'}}>{counts.all.uploaded}</strong></div>
                  <ViewSwitch view="matrix" onChange={switchView}/>
                </div>
              </div>
              <ProductMatrix
                rows={productRows}
                selectedId={selected}
                selectedGs={selectedGs}
                onToggleProduct={toggleProductSelection}
                onSelectAll={selectAllProducts}
                onClearAll={clearAllProducts}
                onSelect={(id) => setSelected(id === selected ? null : id)}/>
            </>
          )}

          {stage === 'keyword' && file && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 05 · 마켓별 키워드 생성 · keyword_seed</span>
                  <h2>마켓별 키워드 생성</h2>
                </div>
                <AccountSummary counts={counts}/>
                <div className="summary">
                  <div><small>대상</small><strong>{selectedGs.size}</strong></div>
                  <div><small>병렬</small><strong style={{color:'var(--color-quizlet-violet)'}}>{keywordOptions.concurrency}</strong></div>
                  <AuroraBtn icon={<IconSync size={16}/>} onClick={generateKeywords} disabled={keywordRunning}>
                    {keywordRunning ? `생성 중 ${keywordProgress}%` : '키워드 생성 시작'}
                  </AuroraBtn>
                  <GhostBtn onClick={openKeywordOptions}>옵션</GhostBtn>
                </div>
              </div>
              <KeywordWorkbench
                rows={productRows}
                selectedGs={selectedGs}
                marketSelection={marketSelection}
                options={keywordOptions}
                activeChannel={activeChannel}
                onChannelChange={setActiveChannel}
                activeProductId={activeImageProduct}
                onActiveProductChange={setActiveImageProduct}
                keywordJob={keywordJob}
                onStartKeyword={generateKeywords}
                uploadQueue={uploadQueue}
                onQueueUploadItem={queueUploadItem}
                categoryEditRequest={categoryEditRequest}/>
            </>
          )}

          {stage === 'upload' && file && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 04 · 마켓 업로드 · market_upload</span>
                  <h2>마켓별 업로드</h2>
                </div>
                <div className="summary">
                  <div><small>대기열</small><strong>{Object.keys(uploadQueue || {}).length}</strong></div>
                  <div><small>API/Excel</small><strong style={{color:'var(--color-quizlet-violet)'}}>선택</strong></div>
                </div>
              </div>
              <MarketUploadWorkbench
                rows={productRows}
                selectedGs={selectedGs}
                marketSelection={marketSelection}
                options={keywordOptions}
                activeChannel={activeChannel}
                onChannelChange={setActiveChannel}
                seedName={file?.name || source?.name || ''}
                seedPath={file?.sourcePath || source?.sourcePath || ''}
                uploadQueue={uploadQueue}
                history={uploadHistory}
                onRuntimeArtifact={rememberSessionArtifact}
                onUploadHistoryChange={updateUploadHistory}/>
            </>
          )}

          {stage === 'result' && file && (
            <>
              <div className="section-head">
                <div className="left">
                  <span className="crumbs">STEP 07 · 업로드 결과 · listing_status</span>
                  <h2>상품별 업로드 결과</h2>
                </div>
                <AccountSummary counts={counts}/>
                <div className="summary">
                  <div><small>총</small><strong>{counts.all.total}</strong></div>
                  <div><small>대기/등록</small><strong style={{color:'var(--color-night-violet)'}}>{counts.all.targeted}</strong></div>
                  <div><small>업로드 완료</small><strong style={{color:'var(--color-quizlet-violet)'}}>{counts.all.uploaded}</strong></div>
                </div>
              </div>
              <UploadResultTable
                rows={productRows}
                selectedGs={selectedGs}
                history={uploadHistory}
                onRuntimeArtifact={rememberSessionArtifact}
                onUploadHistoryChange={updateUploadHistory}
                onEditCategory={openCategoryFixFromResult}/>
            </>
          )}

          {stage !== 'drop' && stage !== 'keyword' && stage !== 'upload' && stage !== 'result' && file && (
            <>
              <WorkflowActionPanel
                mode={fileMode}
                file={file}
                markets={marketSelection}
                onToggleMarket={toggleMarket}/>
            </>
          )}
          <section className="app-log-panel surface">
            <div className="app-log-head">
              <span>실행 로그</span>
              <strong>{appLogs.length}</strong>
            </div>
            <div className="app-log-lines">
              {appLogs.slice(-6).map((log, index) => (
                <code key={index}>[{log.time}] {log.message}</code>
              ))}
            </div>
          </section>
          <nav className="page-step-nav surface" aria-label="페이지 이동">
            <div className="page-flow-rail-wrap" style={{position:'relative'}}>
              <ol className="page-flow-rail">
                {PIPELINE_STEPS.map((step, index) => {
                  const done = index < currentFlowIndex;
                  const active = index === currentFlowIndex;
                  const canJump = done || active || (index <= currentFlowIndex + 1 && !!file);
                  return (
                    <li
                      key={step.key}
                      className={`${done ? 'is-done' : ''} ${active ? 'is-active' : ''} ${canJump && !active ? 'is-clickable' : ''}`}
                      onClick={() => { if (canJump && !active) moveToFlowStep(step); }}>
                      <span className="pf-num">{index + 1}</span>
                      <span className="pf-title">{step.title}</span>
                    </li>
                  );
                })}
              </ol>
            </div>
            <GhostBtn onClick={goBack} disabled={!navState.canBack}>
              뒤로: {navState.backLabel}
            </GhostBtn>
            <div className="page-step-current">
              <span>현재 단계</span>
              <strong>{navState.title}</strong>
              {sourceSeedRunning && (
                <span className="nav-run-progress" aria-label="1차 시드 생성 진행률">
                  <i style={{width: `${sourceSeedProgress}%`}}/>
                  <b>{sourceSeedStage}</b>
                </span>
              )}
              {keywordRunning && (
                <span className="nav-run-progress" aria-label="키워드 생성 진행률">
                  <i style={{width: `${keywordProgress}%`}}/>
                  <b>{keywordStage}</b>
                </span>
              )}
              <div className="peek-tray">
                <div className="peek-handle"
                  onMouseEnter={() => { clearTimeout(peekTimerRef.current); setPeekOpen(true); }}>
                  <span/>
                </div>
              </div>
            </div>
            <AuroraBtn icon={<IconChevR size={16}/>} onClick={goForward} disabled={!navState.canForward}>
              앞으로: {navState.forwardLabel}
            </AuroraBtn>
          </nav>
        </main>
      </div>

      {snapshotCapturing && (
        <div className="snapshot-overlay">
          <div className="snapshot-overlay__inner">미리보기 생성 중…</div>
        </div>
      )}

      {peekOpen && (
        <DesignGallery
          initialSlide={currentFlowIndex}
          snapshots={(() => {
            const el = document.querySelector('.main');
            if (el) stepSnapshotsRef.current[currentFlowIndex] = el.innerHTML;
            return [...stepSnapshotsRef.current];
          })()}
          onClose={() => setPeekOpen(false)}
          onNavigate={(i) => {
            const step = PIPELINE_STEPS[i];
            if (step) moveToFlowStep(step);
            setPeekOpen(false);
          }}/>
      )}

      {stage === 'matrix' && selected && product && (
        <DetailModal
          product={product}
          onClose={() => setSelected(null)}
          onStatusChange={() => {}}/>
      )}

      {settingsOpen && (
        <SettingsModal onClose={() => setSettingsOpen(false)}/>
      )}

      {keywordOptionsOpen && (
        <KeywordOptionsModal
          initialOptions={keywordOptions}
          marketSelection={marketSelection}
          onClose={() => setKeywordOptionsOpen(false)}
          onStart={startKeywordRun}/>
      )}
    </div>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App/>);
