// WEBOCRV2 -> KeywordOCR V4/V5 pipeline matching contracts.
// This file keeps UI action names aligned with the existing C#/Python pipeline.

(function () {
  const ACTIONS = {
    sourceToSeed: {
      ui: '1차 시드 파일 생성',
      legacy: [
        'KeywordOcr.App/Services/PythonPipelineBridgeService.cs::RunPipelineAsync',
        'KeywordOcr.App/Bridge/run_pipeline_bridge.py',
        'backend/app/services/pipeline.py::run_pipeline',
        'backend/app/services/legacy_core.py::_apply_logo',
      ],
      output: ['.webseed.json(webocr.seed.v2)', 'products[].ocrAnalysis', 'products[].photoAnalysis', 'products[].keywordCandidatePool', 'data/thumbs/{GS코드}.jpg'],
    },
    keywordSeed: {
      ui: '마켓별 키워드 생성',
      legacy: [
        'KeywordOcr.App/MainWindow.xaml.cs::RefreshV4ImageCliCodexCommands',
        'KeywordOcr.App/MainWindow.xaml.cs::RunCodexCommandsParallelAsync',
        'backend/app/services/keyword_builder.py',
        'backend/app/services/market_keywords.py',
      ],
      output: ['listing_variants', '상품명', '검색어설정', 'image_selections.json'],
    },
    cafe24Create: {
      ui: 'Cafe24 자동 업로드 및 URL 수집',
      legacy: [
        'KeywordOcr.App/Services/Cafe24CreateProductService.cs::CreateAsync',
        'KeywordOcr.App/Services/Cafe24CreateProductService.cs::CreateBMarketAsync',
      ],
      output: ['Cafe24 상품번호', 'Cafe24 URL', 'upload_attempts'],
    },
    apiMarketUpload: {
      ui: '네이버/쿠팡/롯데ON API 업로드',
      legacy: [
        'KeywordOcr.App/Services/NaverUploadService.cs::UploadAsync',
        'KeywordOcr.App/Services/CoupangUploadService.cs::UploadAsync',
        'KeywordOcr.App/Services/LotteOnUploadService.cs::UploadAsync',
      ],
      output: ['마켓 상품번호', '성공/실패 응답', 'upload_attempts'],
    },
    excelMarketExport: {
      ui: '11번가/ESM 엑셀 데이터 다운로드',
      legacy: [
        'KeywordOcr.App/Services/MarketExcelExportService.cs::Export',
      ],
      output: ['11번가 Excel/ZIP', 'ESM Excel', '검수리포트'],
    },
  };

  function buildListingImageSettings(options) {
    const o = options || {};
    return {
      MakeListing: true,
      ListingSize: 1000,
      ListingPad: 20,
      ListingMax: 20,
      LogoPath: o.logoPath || '',
      LogoRatio: Number(o.logoRatio || 14),
      LogoOpacity: Number(o.logoOpacity || 65),
      LogoPosition: o.logoPosition || 'tr',
      UseAutoContrast: o.autoContrast !== false,
      UseSharpen: o.sharpen !== false,
      UseSmallRotate: o.fixRotation !== false,
      RotateZoom: 1.04,
      UltraAngleDeg: 0.35,
      UltraTranslatePx: 0.6,
      UltraScalePct: 0.25,
      TrimTolerance: 8,
      JpegQualityMin: Number(o.jpegMin || 88),
      JpegQualityMax: Number(o.jpegMax || 92),
      FlipLeftRight: !!o.mirror,
      LogoPathB: o.logoPathB || '',
      ImgTag: o.detailTagA || '',
      ImgTagB: o.detailTagB || '',
      ANameMin: 80,
      ANameMax: 100,
      BNameMin: 63,
      BNameMax: 98,
      ATagCount: 20,
      BTagCount: 14,
      KeywordVersion: '3.0',
    };
  }

  function buildSourceToSeedPayload({ file, selectedGs, options }) {
    return {
      action: 'sourceToSeed',
      actionContract: ACTIONS.sourceToSeed,
      sourceFile: file?.name || '',
      selectedGs: Array.from(selectedGs || []),
      phase: 'seed_prepare',
      listingImageSettings: buildListingImageSettings(options),
    };
  }

  function buildKeywordRunPayload({ file, selectedGs, options, marketSelection }) {
    return {
      action: 'keywordSeed',
      actionContract: ACTIONS.keywordSeed,
      seedFile: file?.name || '',
      selectedGs: Array.from(selectedGs || []),
      accountScope: options?.accountScope || '전체',
      concurrency: 50,
      channels: Object.entries(marketSelection || {})
        .filter(([, enabled]) => enabled !== false)
        .filter(([key]) => !key.endsWith(':Cafe24'))
        .map(([key]) => key),
      listingImageSettings: buildListingImageSettings(options),
    };
  }

  function buildMarketUploadPayload({ channel, rows }) {
    const market = channel?.market || '';
    const excelMarkets = new Set(['11번가', 'ESM']);
    return {
      action: excelMarkets.has(market) ? 'excelMarketExport' : 'apiMarketUpload',
      actionContract: excelMarkets.has(market) ? ACTIONS.excelMarketExport : ACTIONS.apiMarketUpload,
      channel: channel?.key || '',
      rows: (rows || []).map((row) => row.gs),
    };
  }

  window.WEBOCR_PIPELINE = {
    ACTIONS,
    buildListingImageSettings,
    buildSourceToSeedPayload,
    buildKeywordRunPayload,
    buildMarketUploadPayload,
  };
})();
