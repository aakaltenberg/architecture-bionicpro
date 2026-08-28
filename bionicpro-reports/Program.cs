using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ClickHouse.Client.ADO;
using System.Security.Claims;
using Amazon.S3;
using Amazon.S3.Model;

var builder = WebApplication.CreateBuilder(args);

// Устанавливаем схемы по умолчанию
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = "http://keycloak:8080/realms/reports-realm";
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuers = new[]
    {
        "http://localhost:8080/realms/reports-realm",
        "http://keycloak:8080/realms/reports-realm"
    },
        ValidateAudience = false,
        ValidateLifetime = true
    };
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError($"JWT authentication failed: {context.Exception}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("JWT token validated successfully");
            foreach (var claim in context.Principal.Claims)
            {
                logger.LogInformation($"Claim: {claim.Type} = {claim.Value}");
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddSingleton(new ClickHouseConnection(builder.Configuration.GetConnectionString("ClickHouse")));

var s3Config = new AmazonS3Config
{
    ServiceURL = "http://minio:9000",
    ForcePathStyle = true,
};
builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client("minioadmin", "minioadmin", s3Config));


var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/reports", async (HttpContext context, ClickHouseConnection ch, IAmazonS3 s3) =>
{
    var userIdClaim = context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
    if (userIdClaim == null)
        return Results.Unauthorized();

    var userId = userIdClaim.Value;

    var period = context.Request.Query["period"].ToString();
    if (string.IsNullOrEmpty(period))
        period = "latest";

    var s3Key = $"{userId}/{period}.json";
    var bucketName = "reports";
    var cdnUrl = $"http://localhost:8082/{bucketName}/{s3Key}";

    // 1. Проверяем наличие отчёта в S3
    try
    {
        var getMetadata = await s3.GetObjectMetadataAsync(bucketName, s3Key);
        // Файл существует – возвращаем ссылку на CDN

        return Results.Ok(new { url = cdnUrl });
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        // Отчёт не найден, требуется генерация
    }

    // 2. Генерация отчёта из ClickHouse
    var reportData = await GenerateReportFromClickHouse(userId, period, ch);
    if (reportData.Count == 0)
        return Results.NotFound("No data for the specified period.");

    var json = System.Text.Json.JsonSerializer.Serialize(reportData);

    // 3. Сохранение в S3
    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    var putRequest = new PutObjectRequest
    {
        BucketName = bucketName,
        Key = s3Key,
        InputStream = stream,
        ContentType = "application/json"
    };
    await s3.PutObjectAsync(putRequest);

    // 4. Возвращаем ссылку
    return Results.Ok(new { url = cdnUrl });
}).RequireAuthorization();

async Task<List<object>> GenerateReportFromClickHouse(string userId, string period, ClickHouseConnection ch)
{
    var results = new List<object>();
    using var command = ch.CreateCommand();
    command.CommandText = $@"
        SELECT date, user_id, user_name, total_movements, errors
        FROM report_view_v2
        WHERE user_id = '{userId}'
        AND date <= today()
        ORDER BY date DESC
    ";
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        results.Add(new
        {
            Date = reader.GetDateTime(0),
            UserId = reader.GetString(1),
            UserName = reader.GetString(2),
            TotalMovements = reader.GetFieldValue<uint>(3),
            Errors = reader.GetFieldValue<uint>(4)
        });
    }
    return results;
}

app.Run();