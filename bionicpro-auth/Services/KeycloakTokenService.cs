using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

public class KeycloakTokenService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public KeycloakTokenService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<(string accessToken, string refreshToken, int expiresIn)>
        ExchangeCodeAsync(string code, string codeVerifier)
    {
        var tokenEndpoint = $"{_config["Keycloak:InternalAuthority"]}/protocol/openid-connect/token";
        var parameters = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            {"grant_type", "authorization_code"},
            {"client_id", _config["Keycloak:ClientId"]},
            {"client_secret", _config["Keycloak:ClientSecret"]},
            {"code", code},
            {"redirect_uri", _config["Keycloak:RedirectUri"]},
            {"code_verifier", codeVerifier}
        });

        var response = await _httpClient.PostAsync(tokenEndpoint, parameters);
        response.EnsureSuccessStatusCode();

        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        return (
            json["access_token"]?.Value<string>(),
            json["refresh_token"]?.Value<string>(),
            json["expires_in"]?.Value<int>() ?? 120
        );
    }

    public async Task<(string accessToken, string refreshToken, int expiresIn)>
        RefreshTokenAsync(string refreshToken)
    {
        var tokenEndpoint = $"{_config["Keycloak:InternalAuthority"]}/protocol/openid-connect/token";
        var parameters = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            {"grant_type", "refresh_token"},
            {"client_id", _config["Keycloak:ClientId"]},
            {"client_secret", _config["Keycloak:ClientSecret"]},
            {"refresh_token", refreshToken}
        });

        var response = await _httpClient.PostAsync(tokenEndpoint, parameters);
        response.EnsureSuccessStatusCode();

        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        return (
            json["access_token"]?.Value<string>(),
            json["refresh_token"]?.Value<string>(),
            json["expires_in"]?.Value<int>() ?? 120
        );
    }
}