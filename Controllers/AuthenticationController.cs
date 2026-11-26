using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ReactionChemistry.Backend.Data;
using ReactionChemistry.Backend.Models;

namespace ReactionChemistry.Backend.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthenticationController> _logger;
    private readonly IAuthenticationSchemeProvider _schemes;

    public AuthenticationController(
        AppDbContext context,
        IConfiguration configuration,
        ILogger<AuthenticationController> logger,
        IAuthenticationSchemeProvider schemes)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _schemes = schemes;
    }

    [AllowAnonymous]
    [HttpGet("login/{provider}")]
    public async Task<IActionResult> Login(string? provider)
    {
        var normalizedProvider = provider?.Trim().ToLowerInvariant();
        var scheme = GetScheme(normalizedProvider);
        if (scheme is null || await _schemes.GetSchemeAsync(scheme) is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Provedor de login indisponível.",
                Detail = "Configure as credenciais OAuth do provedor no ambiente da API."
            });
        }

        return Challenge(
            new AuthenticationProperties { RedirectUri = $"/api/auth/callback?provider={normalizedProvider}" },
            scheme);
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> ExternalCallback([FromQuery] string? provider)
    {
        var external = await HttpContext.AuthenticateAsync("External");
        await HttpContext.SignOutAsync("External");
        if (!external.Succeeded || external.Principal is null)
        {
            return RedirectToFrontend("authError", "external_login_failed");
        }

        var principal = external.Principal;
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var emailVerified = principal.FindFirstValue("email_verified") == "true";
        var authenticatedProvider = principal.FindFirstValue("identity_provider");
        var normalizedProvider = provider?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedProvider)
            || GetScheme(normalizedProvider) is null
            || !string.Equals(authenticatedProvider, normalizedProvider, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(subject)
            || !emailVerified)
        {
            return RedirectToFrontend("authError", "verified_email_required");
        }

        var user = await _context.Users.SingleOrDefaultAsync(
            item => item.Provider == normalizedProvider && item.ProviderSubject == subject);
        var now = DateTime.UtcNow;
        if (user is null)
        {
            user = new AppUser
            {
                Provider = normalizedProvider,
                ProviderSubject = subject,
                Email = email,
                DisplayName = principal.FindFirstValue(ClaimTypes.Name) ?? email
            };
            _context.Users.Add(user);
        }
        else
        {
            user.Email = email;
        }

        var adminEmail = _configuration["Authentication:BootstrapAdminEmail"];
        if (!string.IsNullOrWhiteSpace(adminEmail)
            && string.Equals(email, adminEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            user.Role = UserRoles.Admin;
        }

        if (user.IsSuspended)
        {
            return RedirectToFrontend("authError", "account_suspended");
        }

        user.LastLoginAt = now;
        await _context.ExternalAuthCodes
            .Where(code => code.ExpiresAt < now || code.IsUsed)
            .ExecuteDeleteAsync();

        var rawCode = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawCode)));
        _context.ExternalAuthCodes.Add(new ExternalAuthCode
        {
            CodeHash = codeHash,
            User = user,
            ExpiresAt = now.AddMinutes(3)
        });
        await _context.SaveChangesAsync();

        return RedirectToFrontend("authCode", rawCode);
    }

    [AllowAnonymous]
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 100)
        {
            return BadRequest(new ProblemDetails { Title = "Código de autenticação inválido." });
        }

        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Code)));
        var code = await _context.ExternalAuthCodes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CodeHash == codeHash);
        if (code is null || code.IsUsed || code.ExpiresAt <= DateTime.UtcNow)
        {
            return Unauthorized(new ProblemDetails { Title = "Código expirado ou já utilizado." });
        }

        var markedUsed = await _context.ExternalAuthCodes
            .Where(item => item.Id == code.Id && !item.IsUsed && item.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.IsUsed, true));
        if (markedUsed != 1)
        {
            return Unauthorized(new ProblemDetails { Title = "Código expirado ou já utilizado." });
        }

        var user = await _context.Users.SingleOrDefaultAsync(item => item.Id == code.UserId);
        if (user is null || user.IsSuspended)
        {
            return Unauthorized(new ProblemDetails { Title = "A conta não está disponível." });
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _configuration["Authentication:JwtSigningKey"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        var token = new JwtSecurityToken(
            issuer: _configuration["Authentication:Issuer"] ?? "ReactionChemistry",
            audience: _configuration["Authentication:Audience"] ?? "ReactionChemistryFrontend",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return Ok(new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), new UserProfile(
            user.Id, user.DisplayName, user.Email, user.Role, user.IsSuspended)));
    }

    private string? GetScheme(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        "google" => "Google",
        "github" => "GitHub",
        _ => null
    };

    private IActionResult RedirectToFrontend(string parameter, string value)
    {
        var baseUrl = _configuration["Frontend:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var frontendUri)
            || (frontendUri.Scheme != Uri.UriSchemeHttps
                && !(frontendUri.Scheme == Uri.UriSchemeHttp && frontendUri.IsLoopback)))
        {
            _logger.LogError("Frontend:BaseUrl precisa ser uma URL HTTPS válida (HTTP é aceito apenas em localhost).");
            return Problem(
                title: "A URL de retorno do frontend não está configurada.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var separator = string.IsNullOrEmpty(frontendUri.Query) ? "?" : "&";
        var redirectUrl = $"{frontendUri}{separator}{Uri.EscapeDataString(parameter)}={Uri.EscapeDataString(value)}";
        return Redirect(redirectUrl);
    }
}

public sealed record ExchangeCodeRequest(string Code);
public sealed record UserProfile(Guid Id, string DisplayName, string Email, string Role, bool IsSuspended);
public sealed record AuthResponse(string AccessToken, UserProfile User);
