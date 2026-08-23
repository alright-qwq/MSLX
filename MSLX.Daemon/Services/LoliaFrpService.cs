using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace MSLX.Daemon.Services;

public class LoliaFrpService
{
    public const string ApiBaseUrl = "https://api.lolia.link/api/v1";
    public const string AuthorizeBaseUrl = "https://dash.lolia.link/oauth/authorize";
    public const string Scopes = "user:read tunnel:read tunnel:write node:read";
    public const string DefaultClientId = "vq8em13ezub515p8";

    public sealed record LoliaToken(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);

    private readonly HttpClient _httpClient;
    private static readonly string[] RequiredCreateFields =
    [
        "node_id", "type", "local_ip", "local_port", "remote_port", "custom_domain", "remark"
    ];

    public LoliaFrpService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsConfigured => true;

    public (string url, string state) CreateAuthorizeUrl(string userId, string redirectUri)
    {
        var config = IConfigBase.Config.ReadConfig();
        var clientId = string.IsNullOrWhiteSpace(config["loliaOAuthClientId"]?.ToString())
            ? DefaultClientId
            : config["loliaOAuthClientId"]!.ToString();

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Lolia OAuth 回调地址无效。");

        var state = Guid.NewGuid().ToString("N");
        IConfigBase.Config.WriteConfigKey($"lolia_oauth_state_{state}", new JObject
        {
            ["user_id"] = userId,
            ["redirect_uri"] = redirectUri,
            ["expires_at"] = DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"),
        });
        var url = BuildAuthorizeUrl(clientId!, redirectUri, state);
        return (url, state);
    }

    public async Task<bool> CompleteAuthorizeAsync(string userId, string code, string state)
    {
        var stateData = IConfigBase.Config.ReadConfigKey($"lolia_oauth_state_{state}");
        var savedUserId = stateData?["user_id"]?.ToString();
        var redirectUri = stateData?["redirect_uri"]?.ToString();
        var stateExpiresAt = DateTimeOffset.TryParse(stateData?["expires_at"]?.ToString(), out var parsedExpiresAt)
            ? parsedExpiresAt
            : DateTimeOffset.MinValue;

        if (string.IsNullOrWhiteSpace(savedUserId) || string.IsNullOrWhiteSpace(redirectUri) ||
            savedUserId != userId || stateExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("OAuth State 无效或已过期。");

        IConfigBase.Config.WriteConfigKey($"lolia_oauth_state_{state}", "");
        var config = IConfigBase.Config.ReadConfig();
        var clientId = string.IsNullOrWhiteSpace(config["loliaOAuthClientId"]?.ToString())
            ? DefaultClientId
            : config["loliaOAuthClientId"]!.ToString();
        var clientSecret = string.IsNullOrWhiteSpace(config["loliaOAuthClientSecret"]?.ToString())
            ? throw new InvalidOperationException("请先在系统设置中配置 Lolia OAuth Client Secret。")
            : config["loliaOAuthClientSecret"]!.ToString();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
        });

        using var response = await _httpClient.PostAsync($"{ApiBaseUrl}/oauth2/token", form);
        var content = await response.Content.ReadAsStringAsync();
        var payload = ParseJson(content);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadErrorMessage(payload, response.StatusCode, "Lolia OAuth 授权失败。"));
        }

        var accessToken = payload["access_token"]?.ToString();
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Lolia OAuth 响应中没有 access_token。");

        DateTimeOffset? expiresAt = payload["expires_in"]?.Value<long>() is { } expiresInSeconds
            ? DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
            : null;
        var token = new LoliaToken(
            accessToken,
            payload["refresh_token"]?.ToString(),
            expiresAt);
        SaveTokenToConfig(userId, token);
        return true;
    }

    public bool IsAuthorized(string userId) => LoadTokenFromConfig(userId) != null;

    private async Task<LoliaToken> EnsureFreshTokenAsync(string userId)
    {
        var token = LoadTokenFromConfig(userId);
        if (token == null)
            throw new UnauthorizedAccessException("尚未连接 Lolia FRP 或授权已过期。");

        if (token.ExpiresAt is not { } expiresAt || expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return token;

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            Logout(userId);
            throw new UnauthorizedAccessException("Lolia 授权已过期，请重新连接。");
        }

        var config = IConfigBase.Config.ReadConfig();
        var clientId = string.IsNullOrWhiteSpace(config["loliaOAuthClientId"]?.ToString())
            ? DefaultClientId
            : config["loliaOAuthClientId"]!.ToString();
        var clientSecret = string.IsNullOrWhiteSpace(config["loliaOAuthClientSecret"]?.ToString())
            ? throw new InvalidOperationException("请先在系统设置中配置 Lolia OAuth Client Secret。")
            : config["loliaOAuthClientSecret"]!.ToString();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = token.RefreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });
        using var response = await _httpClient.PostAsync($"{ApiBaseUrl}/oauth2/token", form);
        var payload = ParseJson(await response.Content.ReadAsStringAsync());

        if (!response.IsSuccessStatusCode || payload["access_token"]?.ToString() is not { Length: > 0 } accessToken)
        {
            Logout(userId);
            throw new UnauthorizedAccessException("Lolia 授权已失效，请重新连接。");
        }

        DateTimeOffset? nextExpiresAt = payload["expires_in"]?.Value<long>() is { } expiresInSeconds
            ? DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
            : null;
        var refreshed = new LoliaToken(
            accessToken,
            payload["refresh_token"]?.ToString() ?? token.RefreshToken,
            nextExpiresAt);
        SaveTokenToConfig(userId, refreshed);
        return refreshed;
    }

    public void Logout(string userId)
    {
        IConfigBase.Config.WriteConfigKey($"lolia_token_{userId}", "");
    }

    public async Task<(HttpStatusCode StatusCode, JToken Payload)> SendPassthroughAsync(
        string userId,
        HttpMethod method,
        string path,
        object? body = null,
        string? query = null)
    {
        LoliaToken token;
        try
        {
            token = await EnsureFreshTokenAsync(userId);
        }
        catch (UnauthorizedAccessException)
        {
            return (HttpStatusCode.Unauthorized, ParseJson("{}"));
        }

        using var request = new HttpRequestMessage(method, CombineUrl(path, query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        if (body != null)
        {
            request.Content = new StringContent(
                body is JToken json ? json.ToString(Formatting.None) : JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        using var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var payload = ParseJson(content);

        if ((int)response.StatusCode == 401)
            Logout(userId);

        return (response.StatusCode, payload);
    }

    public async Task<JToken> SendAsync(
        string userId,
        HttpMethod method,
        string path,
        object? body = null,
        string? query = null)
    {
        var token = await EnsureFreshTokenAsync(userId);

        using var request = new HttpRequestMessage(method, CombineUrl(path, query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        if (body != null)
        {
            request.Content = new StringContent(
                body is JToken json ? json.ToString(Formatting.None) : JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        using var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var payload = ParseJson(content);

        if ((int)response.StatusCode == 401)
        {
            Logout(userId);
            throw new UnauthorizedAccessException("Lolia 授权已失效，请重新连接。");
        }

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(ReadErrorMessage(payload, response.StatusCode, "Lolia FRP 请求失败。"));

        return payload;
    }

    public async Task<JToken> GetTunnelConfigAsync(string userId, string tunnelName)
    {
        var detail = await ResolveTunnelDetailAsync(userId, tunnelName);
        var tunnelToken = detail["data"]?["tunnel_token"]?.ToString();
        var tunnelId = detail["data"]?["id"]?.ToString();

        if (string.IsNullOrWhiteSpace(tunnelToken))
            throw new InvalidOperationException("隧道详情中缺少 tunnel_token，无法获取配置。");

        if (string.IsNullOrWhiteSpace(tunnelId))
            throw new InvalidOperationException("隧道详情中缺少隧道 ID，无法获取配置。");

        var (statusCode, response) = await SendPassthroughAsync(
            userId,
            HttpMethod.Get,
            "/tunnel/frpc/config",
            query: $"token={Uri.EscapeDataString(tunnelToken)}&id={Uri.EscapeDataString(tunnelId)}");

        if ((int)statusCode == 401)
            throw new UnauthorizedAccessException("Lolia 授权已失效，请重新连接。");

        if (!response.Value<int?>("code").Equals(200) || response["data"]?["config"]?.ToString() is not { Length: > 0 } encodedConfig)
            throw new InvalidOperationException(ReadErrorMessage(response, statusCode, "Lolia 未返回有效的 frpc 配置。"));

        var config = DecodeConfigContent(encodedConfig);

        if (string.IsNullOrWhiteSpace(config))
            throw new InvalidOperationException("Lolia 未返回有效的 frpc 配置。");

        return new JObject
        {
            ["config"] = config,
            ["format"] = DetectConfigFormat(config),
            ["tunnel_name"] = detail["data"]?["name"],
            ["tunnel_id"] = detail["data"]?["id"],
            ["node_name"] = detail["data"]?["node_name"],
            ["remark"] = detail["data"]?["remark"] ?? response["data"]?["tunnel_remark"],
        };
    }

    private async Task<JToken> ResolveTunnelDetailAsync(string userId, string tunnelName)
    {
        var (statusCode, byName) = await SendPassthroughAsync(
            userId,
            HttpMethod.Get,
            $"/user/tunnel/{Uri.EscapeDataString(tunnelName)}");

        if ((int)statusCode >= 200 && (int)statusCode < 300 && byName["data"] != null)
            return byName;

        if ((int)statusCode == 401)
            throw new UnauthorizedAccessException("Lolia 授权已失效，请重新连接。");

        var (_, listResponse) = await SendPassthroughAsync(
            userId,
            HttpMethod.Get,
            "/user/tunnel",
            query: "page=1&limit=1000");
        var tunnels = listResponse["data"]?["list"] as JArray ?? listResponse["data"] as JArray;

        var match = tunnels?.FirstOrDefault(tunnel =>
            string.Equals(tunnel?["name"]?.ToString(), tunnelName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tunnel?["id"]?.ToString(), tunnelName, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(tunnel?["remark"]?.ToString()) &&
             string.Equals(tunnel["remark"]!.ToString(), tunnelName, StringComparison.OrdinalIgnoreCase)));

        if (match == null)
            throw new InvalidOperationException($"隧道不存在或无权访问：{tunnelName}");

        if (!string.IsNullOrWhiteSpace(match["tunnel_token"]?.ToString()))
            return new JObject { ["data"] = match };

        var identifiers = new[] { match["id"]?.ToString(), match["name"]?.ToString() }
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Select(identifier => identifier!)
            .Distinct()
            .ToArray();

        foreach (var identifier in identifiers)
        {
            var (retryStatus, byIdentifier) = await SendPassthroughAsync(
                userId,
                HttpMethod.Get,
                $"/user/tunnel/{Uri.EscapeDataString(identifier)}");

            if ((int)retryStatus >= 200 && (int)retryStatus < 300 && byIdentifier["data"] != null)
                return byIdentifier;

            if ((int)retryStatus == 401)
                throw new UnauthorizedAccessException("Lolia 授权已失效，请重新连接。");
        }

        throw new InvalidOperationException($"隧道不存在或无权访问：{tunnelName}");
    }

    public async Task<JToken> GetPublicTunnelConfigAsync(string token, string id)
    {
        var query = $"token={Uri.EscapeDataString(token)}&id={Uri.EscapeDataString(id)}";
        using var response = await _httpClient.GetAsync(CombineUrl("/tunnel/frpc/config", query));
        var content = await response.Content.ReadAsStringAsync();
        var payload = ParseJson(content);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(ReadErrorMessage(payload, response.StatusCode, "Lolia FRP 配置获取失败。"));

        var encodedConfig = payload["data"]?["config"]?.ToString();
        var config = DecodeConfigContent(encodedConfig ?? "");
        if (string.IsNullOrWhiteSpace(config))
            throw new InvalidOperationException("Lolia 未返回有效的 frpc 配置。");

        return new JObject
        {
            ["config"] = config,
            ["format"] = DetectConfigFormat(config),
            ["tunnel_id"] = payload["data"]?["id"],
            ["node_name"] = payload["data"]?["node_name"],
        };
    }

    public void ValidateCreateTunnelBody(JObject body)
    {
        foreach (var field in RequiredCreateFields)
        {
            if (body[field] == null)
                throw new ArgumentException($"创建 Lolia 隧道缺少字段：{field}");
        }
    }

    public static string BuildAuthorizeUrl(string clientId, string redirectUri, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scopes,
            ["state"] = state,
        };
        return $"{AuthorizeBaseUrl}{QueryString.Create(query)}";
    }

    public static string DetectConfigFormat(string config)
    {
        var firstMeaningfulLine = config.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !line.StartsWith('#') && !line.StartsWith("//"));

        if (string.IsNullOrWhiteSpace(firstMeaningfulLine)) return "toml";

        if (firstMeaningfulLine.StartsWith('[')) return "ini";
        if (firstMeaningfulLine.Contains(':') && !firstMeaningfulLine.Contains(" = ")) return "yaml";
        return "toml";
    }

    private static void SaveTokenToConfig(string userId, LoliaToken token)
    {
        var data = new JObject
        {
            ["access_token"] = token.AccessToken,
            ["refresh_token"] = token.RefreshToken,
            ["expires_at"] = token.ExpiresAt?.ToString("O"),
        };
        IConfigBase.Config.WriteConfigKey($"lolia_token_{userId}", data);
    }

    public static string DecodeConfigContent(string content)
    {
        var trimmed = content.Trim().Replace("\n", "").Replace("\r", "");
        var isSingleLineBase64 = !string.IsNullOrWhiteSpace(trimmed) &&
                                 trimmed.Length % 4 == 0 &&
                                 System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[A-Za-z0-9+/]+={0,2}$");

        if (!isSingleLineBase64)
        {
            return content;
        }

        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
            return decoded.Any(decodedCharacter => char.IsControl(decodedCharacter) && decodedCharacter is not ('\n' or '\r' or '\t'))
                ? content
                : decoded;
        }
        catch (FormatException)
        {
            return content;
        }
    }

    private static LoliaToken? LoadTokenFromConfig(string userId)
    {
        var data = IConfigBase.Config.ReadConfigKey($"lolia_token_{userId}");
        var accessToken = data?["access_token"]?.ToString();
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        DateTimeOffset? expiresAt = DateTimeOffset.TryParse(data?["expires_at"]?.ToString(), out var parsed) ? parsed : null;
        return new LoliaToken(accessToken, data?["refresh_token"]?.ToString(), expiresAt);
    }

    private static string CombineUrl(string path, string? query) =>
        string.IsNullOrWhiteSpace(query) ? $"{ApiBaseUrl}{path}" : $"{ApiBaseUrl}{path}?{query}";

    private static JToken ParseJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new JObject();
        try
        {
            return JToken.Parse(content);
        }
        catch
        {
            return new JObject { ["message"] = content };
        }
    }

    private static string ReadErrorMessage(JToken payload, HttpStatusCode statusCode, string fallback)
    {
        return payload.Value<string>("msg") ??
               payload.Value<string>("message") ??
               payload.Value<string>("error_description") ??
               payload.Value<string>("error") ??
               $"{fallback} (HTTP {(int)statusCode})";
    }
}
