using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Neo.Core.Minecraft.Auth;

/// <summary>设备码认证请求返回体。</summary>
public sealed record DeviceCodeResponse(
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("message")] string Message);

/// <summary>认证完成后的完整账户凭据。</summary>
public sealed record MinecraftAccount(
    string Uuid,
    string Username,
    string AccessToken,
    string? SkinUrl = null,
    string AccountType = "msa");

/// <summary>
/// 微软官方设备代码流 (OAuth Device Code Flow) 认证器。
/// 不需要内嵌本地 WebView 浏览器，在任何无头或多平台环境（如 Linux / macOS）
/// 均可无缝通过给出的 8 位 UserCode 由用户在外部浏览器中确认登录。
/// </summary>
public sealed class MicrosoftAuthClient(HttpClient httpClient)
{
    private readonly HttpClient _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    // 社区通用的公共客户端 Client ID (Minecraft 官方客户端或跨平台应用通用注册)
    public const string DefaultClientId = "00000000402b5328";

    /// <summary>第一步：发起设备授权请求，获得 8 位配对码与验证网址。</summary>
    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(
        string clientId = DefaultClientId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "service::user.auth.xboxlive.com::MBI_SSL"
        };

        using var response = await _http.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
            new FormUrlEncodedContent(parameters),
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
               ?? throw new JsonException("无效的设备代码响应");
    }

    /// <summary>第二步：轮询等待用户在浏览器中授权完成，换取最终 Minecraft 凭据。</summary>
    public async Task<MinecraftAccount> PollForAccountAsync(
        string deviceCode,
        int intervalSeconds = 5,
        string clientId = DefaultClientId,
        IProgress<string>? statusProgress = null,
        CancellationToken cancellationToken = default)
    {
        var interval = Math.Max(2, intervalSeconds);
        var tokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

        string msAccessToken;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode
            };

            using var res = await _http.PostAsync(tokenUrl, new FormUrlEncodedContent(parameters), cancellationToken)
                .ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                msAccessToken = doc.RootElement.GetProperty("access_token").GetString()!;
                break;
            }

            var errBody = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (errBody.Contains("authorization_pending"))
            {
                statusProgress?.Report("等待用户在浏览器中确认授权...");
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (errBody.Contains("slow_down"))
            {
                interval += 3;
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new InvalidOperationException($"微软授权失败: {errBody}");
        }

        // 2. 交换 Xbox Live 令牌
        statusProgress?.Report("正在进行 Xbox Live 认证...");
        var (xblToken, userHash) = await AuthenticateXboxLiveAsync(msAccessToken, cancellationToken).ConfigureAwait(false);

        // 3. 交换 XSTS 令牌
        statusProgress?.Report("正在换取 XSTS 安全令牌...");
        var xstsToken = await AuthenticateXstsAsync(xblToken, cancellationToken).ConfigureAwait(false);

        // 4. 换取 Minecraft AccessToken
        statusProgress?.Report("正在完成 Minecraft Services 登录...");
        var mcToken = await LoginWithMinecraftServicesAsync(userHash, xstsToken, cancellationToken).ConfigureAwait(false);

        // 5. 获取最终 Profile (UUID, 昵称, 皮肤)
        statusProgress?.Report("正在获取玩家档案与正版 UUID...");
        return await FetchMinecraftProfileAsync(mcToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string Token, string UserHash)> AuthenticateXboxLiveAsync(string msAccessToken, CancellationToken ct)
    {
        var payload = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={msAccessToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var res = await _http.PostAsJsonAsync("https://user.auth.xboxlive.com/user/authenticate", payload, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct).ConfigureAwait(false);
        var root = doc!.RootElement;
        var token = root.GetProperty("Token").GetString()!;
        var userHash = root.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;
        return (token, userHash);
    }

    private async Task<string> AuthenticateXstsAsync(string xblToken, CancellationToken ct)
    {
        var payload = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var res = await _http.PostAsJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", payload, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct).ConfigureAwait(false);
        return doc!.RootElement.GetProperty("Token").GetString()!;
    }

    private async Task<string> LoginWithMinecraftServicesAsync(string userHash, string xstsToken, CancellationToken ct)
    {
        var payload = new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        };

        using var res = await _http.PostAsJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", payload, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct).ConfigureAwait(false);
        return doc!.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<MinecraftAccount> FetchMinecraftProfileAsync(string mcToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct).ConfigureAwait(false);
        var root = doc!.RootElement;

        var id = root.GetProperty("id").GetString()!;
        var name = root.GetProperty("name").GetString()!;

        string? skinUrl = null;
        if (root.TryGetProperty("skins", out var skins) && skins.GetArrayLength() > 0)
        {
            skinUrl = skins[0].GetProperty("url").GetString();
        }

        return new MinecraftAccount(id, name, mcToken, skinUrl, "msa");
    }
}
