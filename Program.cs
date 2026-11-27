using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ReactionChemistry.Backend.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var jwtSigningKey = builder.Configuration["Authentication:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
{
    throw new InvalidOperationException("Configure Authentication:JwtSigningKey com pelo menos 32 bytes.");
}

var jwtIssuer = builder.Configuration["Authentication:Issuer"] ?? "ReactionChemistry";
var jwtAudience = builder.Configuration["Authentication:Audience"] ?? "ReactionChemistryFrontend";
var publicBaseUrl = builder.Configuration["Authentication:PublicBaseUrl"];
Uri? publicUri = null;
if (!string.IsNullOrWhiteSpace(publicBaseUrl))
{
    if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out publicUri)
        || (publicUri.Scheme != Uri.UriSchemeHttps
            && !(publicUri.Scheme == Uri.UriSchemeHttp && publicUri.IsLoopback))
        || publicUri.AbsolutePath != "/"
        || !string.IsNullOrEmpty(publicUri.Query)
        || !string.IsNullOrEmpty(publicUri.Fragment))
    {
        throw new InvalidOperationException(
            "Authentication:PublicBaseUrl deve ser uma origem HTTPS válida (HTTP apenas em localhost).");
    }
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role
        };
    })
    .AddCookie("External", options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle("Google", options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = "External";
        options.CallbackPath = "/signin-google";
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.Events.OnCreatingTicket = context =>
        {
            context.Identity?.AddClaim(new Claim("identity_provider", "google"));

            var verified =
                (context.User.TryGetProperty("email_verified", out var emailVerified)
                    && (emailVerified.ValueKind == JsonValueKind.True
                        || (emailVerified.ValueKind == JsonValueKind.String
                            && bool.TryParse(emailVerified.GetString(), out var ev) && ev)))
                || (context.User.TryGetProperty("verified_email", out var verifiedEmail)
                    && (verifiedEmail.ValueKind == JsonValueKind.True
                        || (verifiedEmail.ValueKind == JsonValueKind.String
                            && bool.TryParse(verifiedEmail.GetString(), out var ve) && ve)));

            context.Identity?.AddClaim(new Claim("email_verified", verified ? "true" : "false"));
            return Task.CompletedTask;
        };
    });
}

var githubClientId = builder.Configuration["Authentication:GitHub:ClientId"];
var githubClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"];
if (!string.IsNullOrWhiteSpace(githubClientId) && !string.IsNullOrWhiteSpace(githubClientSecret))
{
    builder.Services.AddAuthentication().AddOAuth("GitHub", options =>
    {
        options.ClientId = githubClientId;
        options.ClientSecret = githubClientSecret;
        options.SignInScheme = "External";
        options.CallbackPath = "/signin-github";
        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint = "https://api.github.com/user";
        options.Scope.Add("read:user");
        options.Scope.Add("user:email");
        options.Events.OnCreatingTicket = async context =>
     {
         context.Identity?.AddClaim(new Claim("identity_provider", "github"));

         using (var userRequest = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint))
         {
             userRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
             userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                 "Bearer", context.AccessToken);
             userRequest.Headers.UserAgent.ParseAdd("ReactionChemistry");

             using var userResponse = await context.Backchannel.SendAsync(userRequest, context.HttpContext.RequestAborted);
             userResponse.EnsureSuccessStatusCode();

             using var userDoc = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
             var userRoot = userDoc.RootElement;

             if (userRoot.TryGetProperty("id", out var id))
             {
                 context.Identity?.AddClaim(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
             }

             var displayName = userRoot.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())
                 ? name.GetString()
                 : (userRoot.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.String ? login.GetString() : string.Empty);

             if (!string.IsNullOrWhiteSpace(displayName))
             {
                 context.Identity?.AddClaim(new Claim(ClaimTypes.Name, displayName));
             }

             if (userRoot.TryGetProperty("email", out var publicEmail)
                 && publicEmail.ValueKind == JsonValueKind.String
                 && !string.IsNullOrWhiteSpace(publicEmail.GetString()))
             {
                 context.Identity?.AddClaim(new Claim(ClaimTypes.Email, publicEmail.GetString()!));
             }
         }

         using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails"))
         {
             request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
             request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                 "Bearer", context.AccessToken);
             request.Headers.UserAgent.ParseAdd("ReactionChemistry");
             using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
             response.EnsureSuccessStatusCode();
             using var emails = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
             var verifiedEmail = emails.RootElement.EnumerateArray()
                 .FirstOrDefault(email =>
                     email.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True
                     && email.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True);
             if (verifiedEmail.ValueKind == JsonValueKind.Object
                 && verifiedEmail.TryGetProperty("email", out var emailValue))
             {
                 var existingEmails = context.Identity?.FindAll(ClaimTypes.Email).ToArray() ?? [];
                 foreach (var existingEmail in existingEmails)
                 {
                     context.Identity?.RemoveClaim(existingEmail);
                 }
                 context.Identity?.AddClaim(new Claim(ClaimTypes.Email, emailValue.GetString() ?? string.Empty));
                 context.Identity?.AddClaim(new Claim("email_verified", "true"));
             }
         }

         if (context.Identity?.FindFirst("email_verified") is null)
         {
             context.Identity?.AddClaim(new Claim("email_verified", "false"));
         }
     };
    });
}

builder.Services.AddAuthorization();

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
                PermitLimit = 30,
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
    var allowedOrigins = builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    if (publicUri is not null)
    {
        context.Request.Scheme = publicUri.Scheme;
        context.Request.Host = publicUri.IsDefaultPort
            ? new HostString(publicUri.Host)
            : new HostString(publicUri.Host, publicUri.Port);
    }
    await next();
});
app.UseCors("PermitirFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var dbContext = context.RequestServices.GetRequiredService<AppDbContext>();
        var account = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.Role, user.IsSuspended })
            .SingleOrDefaultAsync();
        var tokenRole = context.User.FindFirstValue(ClaimTypes.Role);
        if (account is null || account.IsSuspended || account.Role != tokenRole)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();