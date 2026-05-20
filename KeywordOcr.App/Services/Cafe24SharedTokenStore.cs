using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace KeywordOcr.App.Services;

internal static class Cafe24SharedTokenStore
{
    public static string GetDefaultPath()
    {
        return DesktopKeyStore.GetPath("cafe24_token.json");
    }

    public static string GetDefaultPathB()
    {
        return DesktopKeyStore.GetPath("cafe24_token_jb.json");
    }

    public static Dictionary<string, string> LoadAsKeyValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return values;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = document.RootElement;
        Add(root, values, "MallId", "MALL_ID");
        Add(root, values, "mall_id", "MALL_ID");
        Add(root, values, "MALL_ID", "MALL_ID");
        Add(root, values, "AccessToken", "ACCESS_TOKEN");
        Add(root, values, "access_token", "ACCESS_TOKEN");
        Add(root, values, "ACCESS_TOKEN", "ACCESS_TOKEN");
        Add(root, values, "RefreshToken", "REFRESH_TOKEN");
        Add(root, values, "refresh_token", "REFRESH_TOKEN");
        Add(root, values, "REFRESH_TOKEN", "REFRESH_TOKEN");
        Add(root, values, "ClientId", "CLIENT_ID");
        Add(root, values, "client_id", "CLIENT_ID");
        Add(root, values, "CLIENT_ID", "CLIENT_ID");
        Add(root, values, "ClientSecret", "CLIENT_SECRET");
        Add(root, values, "client_secret", "CLIENT_SECRET");
        Add(root, values, "CLIENT_SECRET", "CLIENT_SECRET");
        Add(root, values, "RedirectUri", "REDIRECT_URI");
        Add(root, values, "redirect_uri", "REDIRECT_URI");
        Add(root, values, "REDIRECT_URI", "REDIRECT_URI");
        Add(root, values, "ApiVersion", "API_VERSION");
        Add(root, values, "api_version", "API_VERSION");
        Add(root, values, "API_VERSION", "API_VERSION");
        Add(root, values, "ShopNo", "SHOP_NO");
        Add(root, values, "shop_no", "SHOP_NO");
        Add(root, values, "SHOP_NO", "SHOP_NO");
        Add(root, values, "Scope", "SCOPE");
        Add(root, values, "scope", "SCOPE");
        Add(root, values, "SCOPE", "SCOPE");
        return values;
    }

    public static void Save(string path, Cafe24TokenConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new Dictionary<string, string?>
        {
            ["MallId"] = config.MallId,
            ["ClientId"] = config.ClientId,
            ["ClientSecret"] = config.ClientSecret,
            ["AccessToken"] = config.AccessToken,
            ["RefreshToken"] = config.RefreshToken,
            ["RedirectUri"] = config.RedirectUri,
            ["ApiVersion"] = config.ApiVersion,
            ["ShopNo"] = config.ShopNo,
            ["Scope"] = config.Scope,
            ["UpdatedAt"] = DateTime.Now.ToString("o")
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    private static void Add(JsonElement root, IDictionary<string, string> values, string jsonKey, string targetKey)
    {
        if (!root.TryGetProperty(jsonKey, out var element))
        {
            return;
        }

        var value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(value))
        {
            values[targetKey] = value;
        }
    }
}
