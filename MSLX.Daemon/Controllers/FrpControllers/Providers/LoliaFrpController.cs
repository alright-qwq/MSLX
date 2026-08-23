using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.FrpControllers.Providers;

[ApiController]
[Route("api/frp/loliafrp")]
[Authorize(Roles = "admin")]
public class LoliaFrpController : ControllerBase
{
    private readonly LoliaFrpService _loliaService;

    public LoliaFrpController(LoliaFrpService loliaService)
    {
        _loliaService = loliaService;
    }

    private string UserId => User.FindFirst("UserId")?.Value ?? throw new UnauthorizedAccessException();

    [HttpGet("oauth/url")]
    public IActionResult GetAuthorizeUrl([FromQuery] string redirectUri)
    {
        try
        {
            var (url, state) = _loliaService.CreateAuthorizeUrl(UserId, redirectUri);
            return Ok(ApiResponseService.Success(new { url, state }));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return BadRequest(ApiResponseService.Error(e.Message, 400));
        }
    }

    [HttpPost("oauth/callback")]
    public async Task<IActionResult> CompleteAuthorize([FromBody] LoliaOAuthCallbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
            return BadRequest(ApiResponseService.Error("缺少 OAuth Code 或 State。", 400));

        try
        {
            await _loliaService.CompleteAuthorizeAsync(UserId, request.Code, request.State);
            var profile = await _loliaService.SendAsync(UserId, HttpMethod.Get, "/user/info");
            return Ok(profile["data"]);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or HttpRequestException)
        {
            return BadRequest(ApiResponseService.Error(e.Message, 400));
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        if (!_loliaService.IsAuthorized(UserId))
            return Ok(ApiResponseService.Success<object?>(null));

        try
        {
            var profile = await _loliaService.SendAsync(UserId, HttpMethod.Get, "/user/info");
            return Ok(profile["data"]);
        }
        catch (UnauthorizedAccessException)
        {
            return Ok(ApiResponseService.Success<object?>(null));
        }
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        _loliaService.Logout(UserId);
        return Ok(ApiResponseService.Success(true));
    }

    [HttpPost("nodes")]
    public async Task<IActionResult> GetNodes([FromBody] JObject request)
    {
        request.TryAdd("page", request["page"] ?? 1);
        request.TryAdd("limit", request["limit"] ?? 1000);
        var (statusCode, payload) = await _loliaService.SendPassthroughAsync(UserId, HttpMethod.Post, "/user/nodes", request);
        return StatusCode((int)statusCode, payload);
    }

    [HttpGet("tunnels")]
    public async Task<IActionResult> GetTunnels([FromQuery] int page = 1, [FromQuery] int limit = 100)
    {
        var query = $"page={Math.Max(page, 1)}&limit={Math.Clamp(limit, 1, 1000)}";
        var (statusCode, payload) = await _loliaService.SendPassthroughAsync(UserId, HttpMethod.Get, "/user/tunnel", query: query);
        return StatusCode((int)statusCode, payload);
    }

    [HttpPost("tunnels")]
    public async Task<IActionResult> CreateTunnel([FromBody] JObject request)
    {
        _loliaService.ValidateCreateTunnelBody(request);
        var (statusCode, payload) = await _loliaService.SendPassthroughAsync(UserId, HttpMethod.Post, "/user/tunnel", request);
        return StatusCode((int)statusCode, payload);
    }

    [HttpDelete("tunnels/{name}")]
    public async Task<IActionResult> DeleteTunnel(string name)
    {
        var (statusCode, payload) = await _loliaService.SendPassthroughAsync(UserId, HttpMethod.Delete, $"/user/tunnel/{Uri.EscapeDataString(name)}");
        return StatusCode((int)statusCode, payload);
    }

    [HttpGet("tunnel-config")]
    public async Task<IActionResult> GetTunnelConfig([FromQuery] string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(ApiResponseService.Error("隧道名称不能为空。", 400));
        try
        {
            var response = await _loliaService.GetTunnelConfigAsync(UserId, name);
            return Ok(ApiResponseService.Success(response));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException or InvalidOperationException or HttpRequestException)
        {
            return BadRequest(ApiResponseService.Error(e.Message, 400));
        }
    }

    [HttpGet("import-config")]
    public async Task<IActionResult> ImportConfig([FromQuery] string token, [FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(id))
            return BadRequest(ApiResponseService.Error("导入配置需要 token 和隧道 ID。", 400));

        try
        {
            var response = await _loliaService.GetPublicTunnelConfigAsync(token, id);
            return Ok(ApiResponseService.Success(response));
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
        {
            return BadRequest(ApiResponseService.Error(e.Message, 400));
        }
    }
}

public class LoliaOAuthCallbackRequest
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}
