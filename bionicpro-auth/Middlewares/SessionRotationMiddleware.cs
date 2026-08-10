public class SessionRotationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionRotationMiddleware> _logger;

    public SessionRotationMiddleware(RequestDelegate next, ILogger<SessionRotationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, SessionTokenStore store)
    {
        var oldSessionId = context.Request.Cookies["bionicpro-auth-session"];
        bool sessionValid = false;

        if (!string.IsNullOrEmpty(oldSessionId))
        {
            // Проверяем, есть ли токены для этой сессии
            var tokens = await store.GetTokensAsync(oldSessionId);
            if (tokens != null)
            {
                sessionValid = true;
                // Сохраняем старый ID и факт валидности в Items для использования после _next
                context.Items["OldSessionId"] = oldSessionId;
                context.Items["SessionValid"] = true;
            }
        }

        // Выполняем основной запрос (контроллер / прокси)
        await _next(context);

        // После выполнения запроса, если сессия была валидной и ответ успешный (200 OK),
        // выполняем ротацию
        if (sessionValid && !context.Response.HasStarted && context.Response.StatusCode == 200)
        {
            var oldId = context.Items["OldSessionId"] as string;
            if (!string.IsNullOrEmpty(oldId))
            {
                var newSessionId = Guid.NewGuid().ToString();
                var copied = await store.CopyTokensAsync(oldId, newSessionId);
                if (copied)
                {
                    context.Response.Cookies.Append("bionicpro-auth-session", newSessionId, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddHours(1)
                    });
                    _logger.LogInformation($"Session rotated: {oldId} → {newSessionId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to rotate session {oldId}, keeping old cookie");
                }
            }
        }
    }
}