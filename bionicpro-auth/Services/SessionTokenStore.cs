using bionicpro_auth.Models;
using StackExchange.Redis;
using System.Text.Json;

public class SessionTokenStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly IEncryptionService _encryptionService;

    private readonly ILogger<SessionTokenStore> _logger;

    public SessionTokenStore(IConnectionMultiplexer redis, IEncryptionService encryptionService, ILogger<SessionTokenStore> logger)
    {
        _redis = redis;
        _db = _redis.GetDatabase();
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task SaveTokensAsync(string sessionId, TokenData tokens)
    {
        var accessKey = $"session:{sessionId}:access";
        var refreshKey = $"session:{sessionId}:refresh";

        // Access token с TTL = время до истечения
        var accessData = JsonSerializer.Serialize(new AccessData
        {
            Token = tokens.AccessToken,
            ExpiresAt = tokens.AccessTokenExpiry
        });
        var accessTtl = tokens.AccessTokenExpiry - DateTime.UtcNow;
        if (accessTtl <= TimeSpan.Zero) accessTtl = TimeSpan.FromMinutes(2);
        await _db.StringSetAsync(accessKey, accessData, accessTtl);

        // Refresh token шифруем и сохраняем с TTL
        var encryptedRefresh = _encryptionService.Encrypt(tokens.RefreshToken);
        var refreshTtl = TimeSpan.FromDays(7);
        await _db.StringSetAsync(refreshKey, encryptedRefresh, refreshTtl);

        _logger.LogInformation($"Saved tokens for session {sessionId}, access TTL: {accessTtl}, refresh TTL: {refreshTtl}");
    }

    public async Task<TokenData> GetTokensAsync(string sessionId)
    {
        var accessKey = $"session:{sessionId}:access";
        var refreshKey = $"session:{sessionId}:refresh";

        var accessTask = _db.StringGetAsync(accessKey);
        var refreshTask = _db.StringGetAsync(refreshKey);
        await Task.WhenAll(accessTask, refreshTask);

        var accessJson = accessTask.Result.ToString();
        var encryptedRefresh = refreshTask.Result.ToString();

        if (string.IsNullOrEmpty(accessJson) || string.IsNullOrEmpty(encryptedRefresh))
        {
            _logger.LogWarning($"Missing data for session {sessionId}: access empty={string.IsNullOrEmpty(accessJson)}, refresh empty={string.IsNullOrEmpty(encryptedRefresh)}");
            return null;
        }

        try
        {
            var accessData = JsonSerializer.Deserialize<AccessData>(accessJson);
            var refreshToken = _encryptionService.Decrypt(encryptedRefresh);
            return new TokenData
            {
                AccessToken = accessData.Token,
                RefreshToken = refreshToken,
                AccessTokenExpiry = accessData.ExpiresAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to deserialize or decrypt tokens for session {sessionId}");
            return null;
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        var accessKey = $"session:{sessionId}:access";
        var refreshKey = $"session:{sessionId}:refresh";
        await _db.KeyDeleteAsync(new RedisKey[] { accessKey, refreshKey });
    }

    public async Task<bool> CopyTokensAsync(string oldSessionId, string newSessionId)
    {
        var oldAccessKey = $"session:{oldSessionId}:access";
        var oldRefreshKey = $"session:{oldSessionId}:refresh";
        var newAccessKey = $"session:{newSessionId}:access";
        var newRefreshKey = $"session:{newSessionId}:refresh";

        var accessValue = await _db.StringGetAsync(oldAccessKey);
        var refreshValue = await _db.StringGetAsync(oldRefreshKey);
        var accessTtl = await _db.KeyTimeToLiveAsync(oldAccessKey);
        var refreshTtl = await _db.KeyTimeToLiveAsync(oldRefreshKey);

        if (accessValue.IsNullOrEmpty && refreshValue.IsNullOrEmpty)
        {
            _logger.LogWarning($"Nothing to copy for old session {oldSessionId}");
            return false;
        }

        bool success = true;

        if (!accessValue.IsNullOrEmpty)
        {
            var exp = accessTtl.HasValue ? new Expiration(accessTtl.Value) : default;
            if (await _db.StringSetAsync(newAccessKey, accessValue, exp))
            {
                _logger.LogInformation($"Copied access key to {newAccessKey}");
            }
            else
            {
                _logger.LogError($"Failed to copy access key to {newAccessKey}");
                success = false;
            }
        }

        if (!refreshValue.IsNullOrEmpty)
        {
            var exp = refreshTtl.HasValue ? new Expiration(refreshTtl.Value) : default;
            if (await _db.StringSetAsync(newRefreshKey, refreshValue, exp))
            {
                _logger.LogInformation($"Copied refresh key to {newRefreshKey}");
            }
            else
            {
                _logger.LogError($"Failed to copy refresh key to {newRefreshKey}");
                success = false;
            }
        }

        // Если всё успешно – удаляем старые ключи
        if (success)
        {
            await _db.KeyDeleteAsync(oldAccessKey);
            await _db.KeyDeleteAsync(oldRefreshKey);
            _logger.LogInformation($"Deleted old keys for session {oldSessionId}");
        }
        else
        {
            _logger.LogWarning($"Copy failed, old keys for {oldSessionId} were NOT deleted");
        }

        return success;
    }

    public async Task SaveCodeVerifierAsync(string key, string codeVerifier, TimeSpan ttl)
    {
        var redisKey = $"code_verifier:{key}";
        await _db.StringSetAsync(redisKey, codeVerifier, ttl);
    }

    public async Task<string> GetCodeVerifierAsync(string key)
    {
        var redisKey = $"code_verifier:{key}";
        var value = await _db.StringGetAsync(redisKey);
        return value.ToString();
    }

    public async Task SaveUserProfileAsync(string sessionId, UserProfile profile)
    {
        var key = $"user_profile:{sessionId}";
        var json = JsonSerializer.Serialize(profile);
        await _db.StringSetAsync(key, json, TimeSpan.FromHours(1));
    }

    private class AccessData
    {
        public string Token { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}