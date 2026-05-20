using System;
using System.Threading;
using System.Threading.Tasks;

namespace KeywordOcr.App.Services;

internal static class Cafe24TokenRefreshSupport
{
    public static async Task<bool> TryRefreshAndSaveAsync(
        Cafe24ConfigStore configStore,
        Cafe24ApiClient apiClient,
        Cafe24TokenState tokenState,
        CancellationToken cancellationToken,
        Action<string>? report = null,
        string label = "Cafe24")
    {
        var config = tokenState.Config;
        if (string.IsNullOrWhiteSpace(config.RefreshToken)
            || string.IsNullOrWhiteSpace(config.ClientId)
            || string.IsNullOrWhiteSpace(config.ClientSecret)
            || string.IsNullOrWhiteSpace(config.MallId))
        {
            return false;
        }

        try
        {
            await apiClient.RefreshAccessTokenAsync(config, cancellationToken);
            configStore.SaveTokenConfig(tokenState.ConfigPath, config);
            report?.Invoke($"{label} 토큰 JSON 자동 갱신 완료");
            return true;
        }
        catch (Cafe24ReauthenticationRequiredException ex)
        {
            report?.Invoke($"{label} 토큰 자동 갱신 실패: 다시 인증 필요 ({ShortMessage(ex)})");
            return false;
        }
        catch (Exception ex)
        {
            report?.Invoke($"{label} 토큰 자동 갱신 생략: {ShortMessage(ex)}");
            return false;
        }
    }

    private static string ShortMessage(Exception ex)
    {
        var message = Cafe24UploadSupport.UnwrapMessage(ex);
        return message.Length <= 160 ? message : message[..160];
    }
}
