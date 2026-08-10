using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using bionicpro_auth.Models;
using System.IdentityModel.Tokens.Jwt;

[ApiController]
[Route("[controller]")]
public class AuthController : ControllerBase
{
    private readonly KeycloakTokenService _tokenService;
    private readonly SessionTokenStore _tokenStore;
    private readonly IConfiguration _config;
    private readonly ILogger<SessionRotationMiddleware> _logger;

    public AuthController(KeycloakTokenService tokenService, SessionTokenStore tokenStore, IConfiguration config, ILogger<SessionRotationMiddleware> logger)
    {
        _tokenService = tokenService;
        _tokenStore = tokenStore;
        _config = config;
        _logger = logger;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Генерируем уникальный state, который будет использоваться как ключ в Redis
        var state = Guid.NewGuid().ToString();
        // Сохраняем code_verifier в Redis с временем жизни 2 минуты
        _tokenStore.SaveCodeVerifierAsync(state, codeVerifier, TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();

        var authorizationEndpoint = $"{_config["Keycloak:ExternalAuthority"]}/protocol/openid-connect/auth";
        var query = QueryString.Create(new Dictionary<string, string>
        {
            {"client_id", _config["Keycloak:ClientId"]},
            {"redirect_uri", _config["Keycloak:RedirectUri"]},
            {"response_type", "code"},
            {"scope", "openid profile email"},
            {"code_challenge", codeChallenge},
            {"code_challenge_method", "S256"},
            {"state", state}
        });

        return Redirect($"{authorizationEndpoint}{query}");
    }

    [HttpGet("signin-oidc")]
    public async Task<IActionResult> Callback(string code, string state)
    {
        if (string.IsNullOrEmpty(state))
            return BadRequest("Missing state parameter");

        var codeVerifier = await _tokenStore.GetCodeVerifierAsync(state);
        if (string.IsNullOrEmpty(codeVerifier))
            return BadRequest("Missing or expired code_verifier");

        var (accessToken, refreshToken, expiresIn) =
            await _tokenService.ExchangeCodeAsync(code, codeVerifier);

        // Создаём сессию и сохраняем токены в Redis
        var sessionId = Guid.NewGuid().ToString();
        var tokens = new TokenData
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn)
        };
        await _tokenStore.SaveTokensAsync(sessionId, tokens);

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(accessToken))
            {
                var jwt = handler.ReadJwtToken(accessToken);
                var idp = jwt.Claims.FirstOrDefault(c => c.Type == "identity_provider")?.Value;
                if (idp == "yandex")
                {
                    var profile = new UserProfile
                    {
                        Email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value,
                        FirstName = jwt.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value,
                        LastName = jwt.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value,
                        Phone = jwt.Claims.FirstOrDefault(c => c.Type == "phone")?.Value,
                    };
                    await _tokenStore.SaveUserProfileAsync(sessionId, profile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract user profile from JWT");
        }

        // Устанавливаем сессионную куку
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(1)
        };
        Response.Cookies.Append("bionicpro-auth-session", sessionId, cookieOptions);

        // Редирект на фронтенд
        var frontendBase = _config["Frontend:BaseUrl"] ?? "http://localhost:3000";
        return Redirect(frontendBase);
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        var sessionId = Request.Cookies["bionicpro-auth-session"];
        if (!string.IsNullOrEmpty(sessionId))
        {
            await _tokenStore.DeleteSessionAsync(sessionId);
            Response.Cookies.Delete("bionicpro-auth-session");
        }
        return Redirect(_config["Frontend:BaseUrl"] ?? "http://localhost:3000");
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var sessionId = Request.Cookies["bionicpro-auth-session"];
        if (string.IsNullOrEmpty(sessionId))
            return Unauthorized();

        var tokens = await _tokenStore.GetTokensAsync(sessionId);
        if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
            return Unauthorized();

        return Ok(new { authenticated = true, sessionId = sessionId });
    }

    private string GenerateCodeVerifier()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private string GenerateCodeChallenge(string verifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}