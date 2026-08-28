using bionicpro_auth.Models;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// 1. Шифрование
var encryptionKey = builder.Configuration["EncryptionKey"]
    ?? throw new InvalidOperationException("EncryptionKey is not configured");
builder.Services.AddSingleton<IEncryptionService>(new AesEncryptionService(encryptionKey));

// 2. Redis
var redis = ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis"));
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// 3. Хранилище токенов
builder.Services.AddSingleton<SessionTokenStore>();

// 4. HttpClient и сервис Keycloak
builder.Services.AddHttpClient();
builder.Services.AddSingleton<KeycloakTokenService>();

// 5. Контроллеры
builder.Services.AddControllers();

// 6. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowCredentials()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// 7. Reverse Proxy с обновлением токенов
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transforms =>
    {
        transforms.AddRequestTransform(async transformContext =>
        {
            var sessionId = transformContext.HttpContext.Request.Cookies["bionicpro-auth-session"];
            var logger = transformContext.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation($"Proxy transform: sessionId = {sessionId}");
            if (!string.IsNullOrEmpty(sessionId))
            {
                var tokenStore = transformContext.HttpContext.RequestServices.GetRequiredService<SessionTokenStore>();
                var tokens = await tokenStore.GetTokensAsync(sessionId);
                if (tokens != null)
                {
                    logger.LogInformation("Tokens found, adding Authorization header.");
                    if (DateTime.UtcNow >= tokens.AccessTokenExpiry)
                    {
                        var keycloakService = transformContext.HttpContext.RequestServices.GetRequiredService<KeycloakTokenService>();
                        var (newAccess, newRefresh, expiresIn) = await keycloakService.RefreshTokenAsync(tokens.RefreshToken);
                        tokens = new TokenData
                        {
                            AccessToken = newAccess,
                            RefreshToken = newRefresh,
                            AccessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn)
                        };
                        await tokenStore.SaveTokensAsync(sessionId, tokens);
                    }
                    transformContext.ProxyRequest.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                }
                else
                {
                    logger.LogWarning($"No tokens found for session {sessionId}");
                }
            }
            else
            {
                logger.LogWarning("No session cookie in proxy request");
            }
        });
    });

var app = builder.Build();

app.UseRouting();
app.UseCors("AllowFrontend");
app.UseMiddleware<SessionRotationMiddleware>();

app.MapControllers();
app.MapReverseProxy();

app.Run();