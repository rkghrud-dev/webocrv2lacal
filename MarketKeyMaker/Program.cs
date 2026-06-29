using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;

ApplicationConfiguration.Initialize();
Application.Run(new KeyMakerForm());

internal sealed class KeyMakerForm : Form
{
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly TextBox _targetRoot = new();
    private readonly CheckBox _alsoDesktop = new();
    private readonly CheckBox _overwrite = new();
    private readonly TextBox _log = new();

    public KeyMakerForm()
    {
        Text = "WebOCR API 키 생성기";
        Width = 920;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Malgun Gothic", 9F);

        var localKeyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebOCR",
            "key");
        _targetRoot.Text = localKeyRoot;
        _alsoDesktop.Text = "Desktop\\key 에도 같이 생성";
        _alsoDesktop.Checked = true;
        _overwrite.Text = "기존 파일 덮어쓰기";
        _overwrite.Checked = true;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        Controls.Add(main);

        var targetPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        targetPanel.Controls.Add(new Label { Text = "저장 위치", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 7, 8, 0) }, 0, 0);
        _targetRoot.Dock = DockStyle.Fill;
        targetPanel.Controls.Add(_targetRoot, 1, 0);
        var browse = new Button { Text = "찾기", AutoSize = true };
        browse.Click += (_, _) => BrowseTarget();
        targetPanel.Controls.Add(browse, 2, 0);
        main.Controls.Add(targetPanel);

        var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        options.Controls.Add(_alsoDesktop);
        options.Controls.Add(_overwrite);
        main.Controls.Add(options);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildAccountTab("홈런 / A 계정", "A"));
        tabs.TabPages.Add(BuildAccountTab("준비 / B 계정", "B"));
        tabs.TabPages.Add(BuildCommonTab());
        main.Controls.Add(tabs);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "키 파일 생성", Width = 140, Height = 34 };
        save.Click += (_, _) => SaveAll();
        var open = new Button { Text = "폴더 열기", Width = 110, Height = 34 };
        open.Click += (_, _) => OpenTarget();
        buttons.Controls.Add(save);
        buttons.Controls.Add(open);
        main.Controls.Add(buttons);

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        main.Controls.Add(_log);
    }

    private TabPage BuildAccountTab(string title, string account)
    {
        var page = new TabPage(title);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(layout);
        page.Controls.Add(scroll);

        AddHeader(layout, "네이버 커머스 API");
        AddField(layout, $"{account}.naver.client_id", "Client ID");
        AddField(layout, $"{account}.naver.client_secret", "Client Secret", secret: true);
        AddField(layout, $"{account}.naver.account_id", "Account ID");
        AddField(layout, $"{account}.naver.reference_channel_product_no", "기준 상품번호");

        AddHeader(layout, "쿠팡 Wing API");
        AddField(layout, $"{account}.coupang.access_key", "Access Key", secret: true);
        AddField(layout, $"{account}.coupang.secret_key", "Secret Key", secret: true);
        AddField(layout, $"{account}.coupang.vendor_id", "Vendor ID");
        AddField(layout, $"{account}.coupang.vendor_user_id", "Vendor User ID");
        AddField(layout, $"{account}.coupang.return_center_code", "반품지 코드");
        AddField(layout, $"{account}.coupang.outbound_shipping_place_code", "출고지 코드");

        AddHeader(layout, "롯데ON API");
        AddField(layout, $"{account}.lotte.seller_id", "Seller ID");
        AddField(layout, $"{account}.lotte.vendor_no", "Vendor No");
        AddField(layout, $"{account}.lotte.api_key", "API Key", secret: true);
        AddField(layout, $"{account}.lotte.trGrpCd", "거래처 그룹코드");
        AddField(layout, $"{account}.lotte.trNo", "거래처 번호");
        AddField(layout, $"{account}.lotte.trNm", "거래처명");
        AddField(layout, $"{account}.lotte.owhpNo", "출고지 번호");
        AddField(layout, $"{account}.lotte.rtrpNo", "반품지 번호");
        AddField(layout, $"{account}.lotte.dvCstPolNo", "배송비 정책번호");
        AddField(layout, $"{account}.lotte.hdcCd", "택배사 코드");

        AddHeader(layout, "Cafe24");
        AddField(layout, $"{account}.cafe24.mall_id", "Mall ID");
        AddField(layout, $"{account}.cafe24.client_id", "Client ID");
        AddField(layout, $"{account}.cafe24.client_secret", "Client Secret", secret: true);
        AddField(layout, $"{account}.cafe24.access_token", "Access Token", secret: true);
        AddField(layout, $"{account}.cafe24.refresh_token", "Refresh Token", secret: true);
        AddField(layout, $"{account}.cafe24.redirect_uri", "Redirect URI");
        AddField(layout, $"{account}.cafe24.api_version", "API Version", "2025-12-01");
        AddField(layout, $"{account}.cafe24.shop_no", "Shop No", "1");
        AddField(layout, $"{account}.cafe24.scope", "Scope");

        return page;
    }

    private TabPage BuildCommonTab()
    {
        var page = new TabPage("공통 / 11번가");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(layout);
        page.Controls.Add(scroll);

        AddHeader(layout, "11번가 Open API");
        AddField(layout, "common.11st.api_key", "Open API Key", secret: true);
        AddField(layout, "common.11st.seller_id", "Seller ID");
        AddField(layout, "common.11st.nickname", "별칭");

        AddHeader(layout, "AI API 키");
        AddField(layout, "common.openai.api_key", "OpenAI API Key", secret: true);
        AddField(layout, "common.anthropic.api_key", "Anthropic API Key", secret: true);

        return page;
    }

    private void AddHeader(TableLayoutPanel layout, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 16, 0, 4),
        };
        layout.Controls.Add(label, 0, layout.RowCount);
        layout.SetColumnSpan(label, 2);
        layout.RowCount++;
    }

    private void AddField(TableLayoutPanel layout, string key, string label, string defaultValue = "", bool secret = false)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) }, 0, row);
        var box = new TextBox { Dock = DockStyle.Top, Text = defaultValue, UseSystemPasswordChar = secret };
        layout.Controls.Add(box, 1, row);
        _fields[key] = box;
    }

    private string V(string key) => _fields.TryGetValue(key, out var box) ? box.Text.Trim() : "";

    private void BrowseTarget()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _targetRoot.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _targetRoot.Text = dialog.SelectedPath;
    }

    private void OpenTarget()
    {
        Directory.CreateDirectory(_targetRoot.Text);
        Process.Start(new ProcessStartInfo("explorer.exe", _targetRoot.Text) { UseShellExecute = true });
    }

    private void SaveAll()
    {
        _log.Clear();
        var targets = new List<string> { _targetRoot.Text.Trim() };
        if (_alsoDesktop.Checked)
        {
            targets.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "key"));
        }

        foreach (var target in targets.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(target);
            SaveRootFiles(target);
            SaveAccountFolders(target);
            Log($"완료: {target}");
        }

        MessageBox.Show(this, "키 파일 생성이 완료되었습니다.", "WebOCR API 키 생성기", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveRootFiles(string root)
    {
        SaveNaver(root, "A", "naver_client_key.txt");
        SaveNaver(root, "B", "naver_key_junbi.txt");
        SaveCoupang(root, "A", "coupang_wing_api.txt");
        SaveCoupang(root, "B", "coupang_api_junbi.txt");
        SaveLotte(root, "A", "lotteon_api.txt", minimal: true);
        SaveLotte(root, "B", "lotteon_junbi_upload_defaults.txt", minimal: false);
        SaveCafe24(root, "A", "cafe24_token_rkghrud1.json");
        SaveCafe24(root, "B", "cafe24_token_jb.json");

        var elevenKey = V("common.11st.api_key");
        if (!string.IsNullOrWhiteSpace(elevenKey))
        {
            WriteText(root, "elevenst_api_key.txt", $"API_KEY={elevenKey}\r\n");
            WriteText(root, Path.Combine("마켓별_키정리", "07_11번가", "계정_API정보", "11st_upload_id.txt"),
                $"api_key={elevenKey}\r\nseller_id={V("common.11st.seller_id")}\r\nnickname={V("common.11st.nickname")}\r\n");
        }

        if (!string.IsNullOrWhiteSpace(V("common.openai.api_key")))
            WriteText(root, "api_key.txt", V("common.openai.api_key") + "\r\n");
        if (!string.IsNullOrWhiteSpace(V("common.anthropic.api_key")))
            WriteText(root, "anthropic_api_key.txt", V("common.anthropic.api_key") + "\r\n");
    }

    private void SaveAccountFolders(string root)
    {
        SaveCafe24(Path.Combine(root, "홈런", "Cafe24"), "A", "cafe24_token_rkghrud1.json");
        SaveNaver(Path.Combine(root, "홈런", "네이버"), "A", "naver_client_key.txt");
        SaveCoupang(Path.Combine(root, "홈런", "쿠팡"), "A", "coupang_wing_api.txt");
        SaveLotte(Path.Combine(root, "홈런", "롯데ON"), "A", "lotteon_api.txt", minimal: true);

        SaveCafe24(Path.Combine(root, "준비", "Cafe24"), "B", "cafe24_token_jb.json");
        SaveNaver(Path.Combine(root, "준비", "네이버"), "B", "naver_key_junbi.txt");
        SaveCoupang(Path.Combine(root, "준비", "쿠팡"), "B", "coupang_api_junbi.txt");
        SaveLotte(Path.Combine(root, "준비", "롯데ON"), "B", "lotteon_api_junbi.txt", minimal: false);
    }

    private void SaveNaver(string root, string account, string fileName)
    {
        if (AllEmpty($"{account}.naver.client_id", $"{account}.naver.client_secret", $"{account}.naver.account_id", $"{account}.naver.reference_channel_product_no"))
            return;
        WriteText(root, fileName,
            $"NAVER_COMMERCE_CLIENT_ID={V($"{account}.naver.client_id")}\r\n" +
            $"NAVER_COMMERCE_CLIENT_SECRET={V($"{account}.naver.client_secret")}\r\n" +
            $"NAVER_COMMERCE_ACCOUNT_ID={V($"{account}.naver.account_id")}\r\n" +
            $"NAVER_REFERENCE_CHANNEL_PRODUCT_NO={V($"{account}.naver.reference_channel_product_no")}\r\n");
    }

    private void SaveCoupang(string root, string account, string fileName)
    {
        if (AllEmpty($"{account}.coupang.access_key", $"{account}.coupang.secret_key", $"{account}.coupang.vendor_id"))
            return;
        WriteText(root, fileName,
            $"vendor_name=\r\nurl=\r\nip=\r\n" +
            $"vendor_id={V($"{account}.coupang.vendor_id")}\r\n" +
            $"vendor_user_id={V($"{account}.coupang.vendor_user_id")}\r\n" +
            $"return_center_code={V($"{account}.coupang.return_center_code")}\r\n" +
            $"outbound_shipping_place_code={V($"{account}.coupang.outbound_shipping_place_code")}\r\n" +
            $"access_key={V($"{account}.coupang.access_key")}\r\n" +
            $"secret_key={V($"{account}.coupang.secret_key")}\r\n" +
            $"expires_at=\r\n");
    }

    private void SaveLotte(string root, string account, string fileName, bool minimal)
    {
        if (AllEmpty($"{account}.lotte.seller_id", $"{account}.lotte.vendor_no", $"{account}.lotte.api_key"))
            return;
        var lines = new List<string>
        {
            $"seller_id={V($"{account}.lotte.seller_id")}",
            $"vendor_no={V($"{account}.lotte.vendor_no")}",
            $"api_key={V($"{account}.lotte.api_key")}",
        };
        if (!minimal)
        {
            lines.AddRange(new[]
            {
                $"trGrpCd={V($"{account}.lotte.trGrpCd")}",
                $"trNo={V($"{account}.lotte.trNo")}",
                $"trNm={V($"{account}.lotte.trNm")}",
                "trNmEn=",
                "representative_name=",
                "business_registration_no=",
                "business_type=",
                $"owhpNo={V($"{account}.lotte.owhpNo")}",
                $"rtrpNo={V($"{account}.lotte.rtrpNo")}",
                $"dvCstPolNo={V($"{account}.lotte.dvCstPolNo")}",
                "adtnDvCstPolNo=",
                $"hdcCd={V($"{account}.lotte.hdcCd")}",
                "rtngHdcCd=",
                "as_contact=",
                "business_phone=",
                "customer_service_phone=",
                "joined_date=",
            });
        }
        WriteText(root, fileName, string.Join("\r\n", lines) + "\r\n");
    }

    private void SaveCafe24(string root, string account, string fileName)
    {
        if (AllEmpty($"{account}.cafe24.mall_id", $"{account}.cafe24.client_id", $"{account}.cafe24.client_secret", $"{account}.cafe24.access_token", $"{account}.cafe24.refresh_token"))
            return;
        var payload = new Dictionary<string, string>
        {
            ["MallId"] = V($"{account}.cafe24.mall_id"),
            ["ClientId"] = V($"{account}.cafe24.client_id"),
            ["ClientSecret"] = V($"{account}.cafe24.client_secret"),
            ["AccessToken"] = V($"{account}.cafe24.access_token"),
            ["RefreshToken"] = V($"{account}.cafe24.refresh_token"),
            ["RedirectUri"] = V($"{account}.cafe24.redirect_uri"),
            ["ApiVersion"] = V($"{account}.cafe24.api_version"),
            ["ShopNo"] = V($"{account}.cafe24.shop_no"),
            ["Scope"] = V($"{account}.cafe24.scope"),
            ["UpdatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["RefreshTokenUpdatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        WriteText(root, fileName, json + "\r\n");
    }

    private bool AllEmpty(params string[] keys) => keys.All(k => string.IsNullOrWhiteSpace(V(k)));

    private void WriteText(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        if (!_overwrite.Checked && File.Exists(path))
        {
            Log($"건너뜀: {relativePath}");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Log($"생성: {relativePath}");
    }

    private void Log(string message)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
    }
}
