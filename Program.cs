using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configuração Robusta do Cloudflare R2
var r2Config = builder.Configuration.GetSection("CloudflareR2");
var serviceUrl = r2Config["ServiceURL"] ?? "https://SEU_ACCOUNT_ID.r2.cloudflarestorage.com";

var s3ClientConfig = new AmazonS3Config
{
    ServiceURL = serviceUrl,
    AuthenticationRegion = "auto" // Essencial para o Cloudflare R2
};

builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(
    r2Config["AccessKey"] ?? "CHAVE_FALSA",
    r2Config["SecretKey"] ?? "SENHA_FALSA",
    s3ClientConfig
));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("UploadPresign", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Limite de solicitações de upload excedido.",
                Detail = "Aguarde um minuto antes de solicitar novas URLs de upload."
            },
            cancellationToken);
    };
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors("PermitirFrontend");
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();