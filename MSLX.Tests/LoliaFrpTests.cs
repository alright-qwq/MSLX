using MSLX.Daemon.Services;
using Xunit;

namespace MSLX.Tests;

public class LoliaFrpTests
{
    [Theory]
    [InlineData("serverAddr = \"tcp.example.com\"\nserverPort = 7000", "toml")]
    [InlineData("serverAddr: tcp.example.com\nserverPort: 7000", "yaml")]
    [InlineData("[common]\nserver_addr = tcp.example.com", "ini")]
    public void DetectConfigFormat_ReturnsSupportedFrpcFormat(string config, string expected)
    {
        Assert.Equal(expected, LoliaFrpService.DetectConfigFormat(config));
    }

    [Fact]
    public void BuildAuthorizeUrl_UsesOfficialEndpointsAndScopes()
    {
        var url = LoliaFrpService.BuildAuthorizeUrl(
            "client-id",
            "https://panel.example/oauth/callback/lolia",
            "state-123");

        Assert.StartsWith("https://dash.lolia.link/oauth/authorize?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=client-id", url);
        Assert.Contains(Uri.EscapeDataString("user:read tunnel:read tunnel:write node:read"), url);
        Assert.Contains("state=state-123", url);
        Assert.Contains(Uri.EscapeDataString("https://panel.example/oauth/callback/lolia"), url);
    }

    [Fact]
    public void DecodeConfigContent_DecodesSingleLineBase64Config()
    {
        const string expected = "serverAddr = \"tcp.example.com\"\nserverPort = 7000";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(expected));

        Assert.Equal(expected, LoliaFrpService.DecodeConfigContent(encoded));
    }

    [Theory]
    [InlineData("serverAddr = \"tcp.example.com\"")]
    [InlineData("[common]\nserver_addr = tcp.example.com")]
    public void DecodeConfigContent_KeepsPlainTextFrpcConfig(string config)
    {
        Assert.Equal(config, LoliaFrpService.DecodeConfigContent(config));
    }
}
