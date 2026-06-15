using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace KeywordOcr.App.Services;

/// <summary>
/// 쿠팡 WING API 클라이언트 (순수 C#)
/// - HMAC-SHA256 서명 생성
/// - 카테고리 추천 / 메타 조회 / 상품 등록
/// </summary>
public sealed class CoupangApiClient : IDisposable
{
    private const string BaseUrl = "https://api-gateway.coupang.com";

    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _vendorId;
    private readonly string _vendorUserId;
    private readonly HttpClient _http;
    private readonly long? _referenceSellerProductId;
    private readonly long? _outboundShippingPlaceCode;
    private readonly long? _returnCenterCode;

    public string VendorId => _vendorId;
    public string VendorUserId => _vendorUserId;
    public long? ReferenceSellerProductId => _referenceSellerProductId;
    public long? OutboundShippingPlaceCode => _outboundShippingPlaceCode;
    public long? ReturnCenterCode => _returnCenterCode;

    public CoupangApiClient(
        string accessKey,
        string secretKey,
        string vendorId,
        string vendorUserId,
        long? referenceSellerProductId = null,
        long? outboundShippingPlaceCode = null,
        long? returnCenterCode = null)
    {
        _accessKey = accessKey;
        _secretKey = secretKey;
        _vendorId = vendorId;
        _vendorUserId = vendorUserId;
        _referenceSellerProductId = referenceSellerProductId;
        _outboundShippingPlaceCode = outboundShippingPlaceCode;
        _returnCenterCode = returnCenterCode;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>키 파일(~/Desktop/key/coupang_wing_api.txt)에서 로드</summary>
    public static CoupangApiClient FromKeyFile()
    {
        var keyFile = DesktopKeyStore.GetPath("coupang_wing_api.txt");

        var kv = new Dictionary<string, string>();
        if (File.Exists(keyFile))
        {
            foreach (var line in File.ReadAllLines(keyFile, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) continue;
                kv[trimmed[..eqIdx].Trim()] = trimmed[(eqIdx + 1)..].Trim();
            }
        }

        var accessKey = kv.GetValueOrDefault("access_key", "");
        var secretKey = kv.GetValueOrDefault("secret_key", "");
        var vendorId = kv.GetValueOrDefault("vendor_id", "");
        var vendorUserId = kv.GetValueOrDefault("vendor_user_id",
            kv.GetValueOrDefault("vendorUserId",
            kv.GetValueOrDefault("user_id",
            kv.GetValueOrDefault("userId", "rkghrud"))));
        var referenceSellerProductId = ParseOptionalLong(
            kv.GetValueOrDefault("reference_seller_product_id",
            kv.GetValueOrDefault("REFERENCE_SELLER_PRODUCT_ID", "")));
        var outboundShippingPlaceCode = ParseOptionalLong(
            kv.GetValueOrDefault("outbound_shipping_place_code",
            kv.GetValueOrDefault("outboundShippingPlaceCode", "")));
        var returnCenterCode = ParseOptionalLong(
            kv.GetValueOrDefault("return_center_code",
            kv.GetValueOrDefault("returnCenterCode", "")));

        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            throw new InvalidOperationException(
                $"쿠팡 API 키를 찾을 수 없습니다: {keyFile}");

        return new CoupangApiClient(
            accessKey,
            secretKey,
            vendorId,
            vendorUserId,
            referenceSellerProductId,
            outboundShippingPlaceCode,
            returnCenterCode);
    }

    private static long? ParseOptionalLong(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return long.TryParse(raw.Trim(), out var value) ? value : null;
    }

    // ── 서명 생성 ──────────────────────────────────

    private string BuildAuthorization(string method, string path, string? query = null)
    {
        var dt = DateTime.UtcNow.ToString("yyMMdd'T'HHmmss'Z'");
        var message = dt + method + path + (query ?? "");
        var sig = HmacSha256(_secretKey, message);
        return $"CEA algorithm=HmacSHA256, access-key={_accessKey}, signed-date={dt}, signature={sig}";
    }

    private static string HmacSha256(string key, string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── HTTP 호출 ──────────────────────────────────

    private async Task<JsonDocument> CallAsync(
        string method, string path, string? query = null,
        byte[]? body = null, CancellationToken ct = default)
    {
        const int maxRetries = 2;

        for (int attempt = 0; ; attempt++)
        {
            var url = BaseUrl + path;
            if (!string.IsNullOrEmpty(query)) url += "?" + query;

            using var req = new HttpRequestMessage(new HttpMethod(method), url);
            req.Headers.TryAddWithoutValidation("Authorization", BuildAuthorization(method, path, query));
            req.Headers.TryAddWithoutValidation("X-EXTENDED-TIMEOUT", "90000");
            req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, identity");

            if (body is not null)
            {
                req.Content = new ByteArrayContent(body);
                req.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "UTF-8" };
            }

            using var resp = await _http.SendAsync(req, ct);

            if (resp.StatusCode == (System.Net.HttpStatusCode)429 && attempt < maxRetries)
            {
                await Task.Delay(1500, ct);
                continue;
            }

            var raw = await resp.Content.ReadAsByteArrayAsync(ct);

            if (raw.Length >= 2 && raw[0] == 0x1f && raw[1] == 0x8b)
            {
                using var ms = new MemoryStream(raw);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var reader = new MemoryStream();
                await gz.CopyToAsync(reader, ct);
                raw = reader.ToArray();
            }

            if (raw.Length == 0)
            {
                var errJson = $$$"""{"code":"ERROR","message":"빈 응답 (HTTP {{{(int)resp.StatusCode}}})" }""";
                return JsonDocument.Parse(errJson);
            }

            try
            {
                return JsonDocument.Parse(raw);
            }
            catch
            {
                var text = Encoding.UTF8.GetString(raw);
                if (text.Length > 300) text = text[..300];
                text = text.Replace("\"", "'").Replace("\\", "");
                var errJson = $$$"""{"code":"ERROR","message":"JSON 파싱 실패: {{{text}}}" }""";
                return JsonDocument.Parse(errJson);
            }
        }
    }

    // ── 공개 API ───────────────────────────────────

    /// <summary>카테고리 자동 추천</summary>
    public async Task<JsonDocument> PredictCategoryAsync(string productName, CancellationToken ct = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { productName });
        return await CallAsync("POST",
            "/v2/providers/openapi/apis/api/v1/categorization/predict",
            body: body, ct: ct);
    }

    /// <summary>카테고리 메타 정보 (고시정보, 필수속성 등)</summary>
    public async Task<JsonDocument> GetCategoryMetaAsync(long categoryCode, CancellationToken ct = default)
    {
        var path = $"/v2/providers/seller_api/apis/api/v1/marketplace/meta/category-related-metas/display-category-codes/{categoryCode}";
        return await CallAsync("GET", path, ct: ct);
    }

    /// <summary>기준 상품 조회</summary>
    public async Task<JsonDocument> GetProductAsync(long sellerProductId, CancellationToken ct = default)
    {
        var path = $"/v2/providers/seller_api/apis/api/v1/marketplace/seller-products/{sellerProductId}";
        return await CallAsync("GET", path, ct: ct);
    }

    /// <summary>상품 등록</summary>
    public async Task<JsonDocument> CreateProductAsync(JsonElement productJson, CancellationToken ct = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(productJson);
        return await CallAsync("POST",
            "/v2/providers/seller_api/apis/api/v1/marketplace/seller-products",
            body: body, ct: ct);
    }

    /// <summary>이 계정(vendor)의 사용 가능한 출고지 코드. 없으면 null.</summary>
    public async Task<long?> GetUsableOutboundCodeAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await CallAsync("GET",
                "/v2/providers/marketplace_openapi/apis/api/v1/vendor/shipping-place/outbound",
                query: "pageNum=1&pageSize=50", ct: ct);
            if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in content.EnumerateArray())
                {
                    var usable = it.TryGetProperty("usable", out var u) && u.ValueKind == JsonValueKind.True;
                    if (usable && it.TryGetProperty("outboundShippingPlaceCode", out var code))
                        return code.TryGetInt64(out var v) ? v : null;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>이 계정(vendor)의 사용 가능한 반품지 상세(코드+주소+연락처+배송사). 없으면 null.</summary>
    public async Task<JsonObject?> GetUsableReturnCenterAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await CallAsync("GET",
                $"/v2/providers/openapi/apis/api/v4/vendors/{_vendorId}/returnShippingCenters",
                query: "pageSize=50", ct: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data))
            {
                JsonElement list = data;
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("content", out var c)) list = c;
                if (list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in list.EnumerateArray())
                    {
                        var usable = it.TryGetProperty("usable", out var u2) ? u2.ValueKind == JsonValueKind.True
                                   : (!it.TryGetProperty("usableYn", out var u) || u.ValueKind != JsonValueKind.False);
                        if (!usable || !it.TryGetProperty("returnCenterCode", out var code)) continue;
                        var result = new JsonObject();
                        var codeStr = code.ValueKind == JsonValueKind.String ? code.GetString() : code.GetRawText();
                        result["returnCenterCode"] = long.TryParse(codeStr, out var rcv) ? rcv : (JsonNode?)null;
                        if (it.TryGetProperty("deliverCode", out var dc)) result["deliverCode"] = dc.GetString();
                        if (it.TryGetProperty("shippingPlaceName", out var nm)) result["shippingPlaceName"] = nm.GetString();
                        if (it.TryGetProperty("placeAddresses", out var addrs) && addrs.ValueKind == JsonValueKind.Array)
                        {
                            var a = addrs.EnumerateArray().FirstOrDefault();
                            if (a.ValueKind == JsonValueKind.Object)
                            {
                                if (a.TryGetProperty("companyContactNumber", out var ph)) result["companyContactNumber"] = ph.GetString();
                                if (a.TryGetProperty("returnZipCode", out var zip)) result["returnZipCode"] = zip.GetString();
                                if (a.TryGetProperty("returnAddress", out var ad)) result["returnAddress"] = ad.GetString();
                                if (a.TryGetProperty("returnAddressDetail", out var add)) result["returnAddressDetail"] = add.GetString();
                            }
                        }
                        return result;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
