using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using Amazon.S3;

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();