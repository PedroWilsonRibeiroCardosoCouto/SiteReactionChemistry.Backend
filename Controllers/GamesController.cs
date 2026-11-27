using System.Security.Claims;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using ReactionChemistry.Backend.Models;

namespace ReactionChemistry.Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class GamesController : ControllerBase
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _context;
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string? _publicMediaBaseUrl;
    private readonly ILogger<GamesController> _logger;

    public GamesController(
        AppDbContext context,
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<GamesController> logger)
    {
        _context = context;
        _s3Client = s3Client;
        _bucketName = configuration["CloudflareR2:BucketName"] ?? "reaction-chemistry-releases";
        _publicMediaBaseUrl = configuration["CloudflareR2:PublicBaseUrl"]?.TrimEnd('/');
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApprovedGame>>> GetGames()
    {
        var games = _context.Games.AsNoTracking();
        if (!IsAdmin())
        {
            var userId = GetUserId();
            games = userId.HasValue
                ? games.Where(game => game.OwnerId == userId || (game.IsActive && game.Visibility == "Pública"))
                : games.Where(game => game.IsActive && game.Visibility == "Pública");
        }

        var list = await games.OrderBy(game => game.Title).ToListAsync();
        await EnrichGamesAsync(list);
        return list;
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<ManagedGameResponse>>> GetMyGames()
    {
        var games = _context.Games.AsNoTracking();
        if (!IsAdmin())
        {
            var userId = GetUserId();
            games = games.Where(game => game.OwnerId == userId);
        }
        var ownedGames = await games.OrderByDescending(game => game.CreatedAt).ToListAsync();
        await EnrichGamesAsync(ownedGames);

        var gameIds = ownedGames.Select(game => game.Id).ToArray();
        var commentCounts = await _context.GameComments
            .Where(comment => gameIds.Contains(comment.GameId))
            .GroupBy(comment => comment.GameId)
            .Select(group => new { GameId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.GameId, item => item.Count);
        return Ok(ownedGames.Select(game => new ManagedGameResponse(
            game, commentCounts.GetValueOrDefault(game.Id))));
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApprovedGame>> GetGame(Guid id)
    {
        var game = await _context.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
        if (game is null || !CanView(game)) return NotFound();

        await EnrichGamesAsync([game]);
        return game;
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ApprovedGame>> PostGame([FromBody] ApprovedGame input)
    {
        if (!IsValidGame(input) || !IsValidVisibilityForAdmin(input))
            return BadRequest(new ProblemDetails { Title = "Dados obrigatórios do jogo inválidos." });

        var game = new ApprovedGame();
        ApplyEditableFields(game, input);
        game.OwnerId = GetUserId();
        game.CreatedAt = DateTime.UtcNow;
        if (IsAdmin())
        {
            game.Visibility = input.Visibility;
            game.IsActive = input.IsActive;
        }
        else
        {
            game.Visibility = "Privada";
            game.IsActive = false;
        }

        _context.Games.Add(game);
        await _context.SaveChangesAsync();
        await EnrichGamesAsync([game]);
        return CreatedAtAction(nameof(GetGame), new { id = game.Id }, game);
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> PutGame(Guid id, [FromBody] ApprovedGame input)
    {
        if (id != input.Id)
        {
            return BadRequest(new ProblemDetails { Title = "O ID da URL não corresponde ao jogo." });
        }
        if (!IsValidGame(input) || !IsValidVisibilityForAdmin(input))
            return BadRequest(new ProblemDetails { Title = "Dados obrigatórios do jogo inválidos." });

        var game = await _context.Games.SingleOrDefaultAsync(item => item.Id == id);
        if (game is null || !CanManage(game)) return NotFound();

        ApplyEditableFields(game, input);
        if (IsAdmin())
        {
            game.Visibility = input.Visibility;
            game.IsActive = input.IsActive;
        }
        else
        {
            game.Visibility = "Privada";
            game.IsActive = false;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteGame(Guid id)
    {
        var game = await _context.Games.SingleOrDefaultAsync(item => item.Id == id);
        if (game is null || !CanManage(game)) return NotFound();
        _context.Games.Remove(game);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("{id:guid}/views")]
    public async Task<IActionResult> RecordView(Guid id)
    {
        var updated = await _context.Games
            .Where(game => game.Id == id && game.IsActive && game.Visibility == "Pública")
            .ExecuteUpdateAsync(update => update.SetProperty(game => game.ViewCount, game => game.ViewCount + 1));
        return updated == 0 ? NotFound() : NoContent();
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<IEnumerable<GameCommentResponse>>> GetComments(Guid id)
    {
        var game = await _context.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
        if (game is null || !CanView(game)) return NotFound();

        var userId = GetUserId();
        var isAdmin = IsAdmin();
        var comments = await _context.GameComments
            .Where(comment => comment.GameId == id)
            .OrderByDescending(comment => comment.CreatedAt)
            .Select(comment => new GameCommentResponse(
                comment.Id,
                comment.User.DisplayName,
                comment.Content,
                comment.Rating,
                comment.CreatedAt,
                isAdmin || userId == comment.UserId))
            .ToListAsync();
        return Ok(comments);
    }

    [Authorize]
    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<GameCommentResponse>> PostComment(Guid id, [FromBody] CreateGameCommentRequest request)
    {
        var content = request.Content?.Trim();
        if (content is null || content.Length is < 1 or > 2000)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Comentário inválido.",
                Detail = "O comentário deve ter entre 1 e 2000 caracteres."
            });
        }

        if (request.Rating is null or < 1 or > 5)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Avaliação inválida.",
                Detail = "Selecione uma avaliação de 1 a 5 estrelas antes de enviar seu feedback."
            });
        }

        var game = await _context.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
        if (game is null || !game.IsActive || game.Visibility != "Pública") return NotFound();

        var comment = new GameComment
        {
            GameId = id,
            UserId = GetUserId()!.Value,
            Content = content,
            Rating = request.Rating.Value,
            CreatedAt = DateTime.UtcNow
        };
        _context.GameComments.Add(comment);
        await _context.SaveChangesAsync();
        var userName = await _context.Users
            .Where(user => user.Id == comment.UserId)
            .Select(user => user.DisplayName)
            .SingleAsync();
        return CreatedAtAction(
            nameof(GetComments),
            new { id },
            new GameCommentResponse(comment.Id, userName, comment.Content, comment.Rating, comment.CreatedAt, true));
    }

    [Authorize]
    [HttpDelete("{gameId:guid}/comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteComment(Guid gameId, Guid commentId)
    {
        var comment = await _context.GameComments
            .SingleOrDefaultAsync(item => item.Id == commentId && item.GameId == gameId);
        if (comment is null) return NotFound();
        if (!IsAdmin() && comment.UserId != GetUserId()) return Forbid();
        _context.GameComments.Remove(comment);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task EnrichGamesAsync(IReadOnlyList<ApprovedGame> games)
    {
        if (games.Count == 0) return;

        var gameIds = games.Select(game => game.Id).ToArray();
        var ratingsByGame = await _context.GameComments
            .AsNoTracking()
            .Where(comment => gameIds.Contains(comment.GameId) && comment.Rating != null)
            .GroupBy(comment => comment.GameId)
            .Select(group => new
            {
                GameId = group.Key,
                Average = group.Average(comment => (double)comment.Rating!.Value),
                Count = group.Count()
            })
            .ToDictionaryAsync(item => item.GameId);

        foreach (var game in games)
        {
            if (ratingsByGame.TryGetValue(game.Id, out var ratingStats))
            {
                game.AverageRating = Math.Round(ratingStats.Average, 1);
                game.RatingCount = ratingStats.Count;
            }
            else
            {
                game.AverageRating = 0;
                game.RatingCount = 0;
            }

            game.CoverImageUrl = ResolveMediaUrl(game.CoverImageKey);
            game.GalleryImages = ResolveGalleryImages(game.GalleryImagesJson);
        }
    }

    private List<GameMediaItem> ResolveGalleryImages(string? galleryImagesJson)
    {
        if (string.IsNullOrWhiteSpace(galleryImagesJson)) return [];

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredGalleryImage>>(galleryImagesJson, WebJsonOptions);
            if (stored is null || stored.Count == 0) return [];

            var resolved = new List<GameMediaItem>(stored.Count);
            foreach (var item in stored)
            {
                var url = ResolveMediaUrl(item.S3ObjectKey);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    resolved.Add(new GameMediaItem(
                        item.S3ObjectKey,
                        url,
                        string.IsNullOrWhiteSpace(item.FileName) ? "arte" : item.FileName));
                }
            }
            return resolved;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private string? ResolveMediaUrl(string? keyOrUrl)
    {
        if (string.IsNullOrWhiteSpace(keyOrUrl)) return null;
        var trimmed = keyOrUrl.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri.AbsoluteUri;
        }

        if (!IsSafeR2MediaKey(trimmed)) return null;

        if (!string.IsNullOrWhiteSpace(_publicMediaBaseUrl))
        {
            return $"{_publicMediaBaseUrl}/{trimmed}";
        }

        try
        {
            var presignRequest = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = trimmed,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(60)
            };
            return _s3Client.GetPreSignedURL(presignRequest);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Não foi possível gerar URL assinada para a mídia {MediaKey}.", trimmed);
            return null;
        }
    }

    private bool CanView(ApprovedGame game) =>
        IsAdmin()
        || (game.IsActive && game.Visibility == "Pública")
        || (GetUserId() is Guid userId && game.OwnerId == userId);

    private bool CanManage(ApprovedGame game) =>
        IsAdmin() || (GetUserId() is Guid userId && game.OwnerId == userId);

    private bool IsAdmin() => User.IsInRole(UserRoles.Admin);

    private Guid? GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static bool IsValidGame(ApprovedGame game) =>
        !string.IsNullOrWhiteSpace(game.Title)
        && !string.IsNullOrWhiteSpace(game.Version)
        && !string.IsNullOrWhiteSpace(game.S3ObjectKey)
        && !string.IsNullOrWhiteSpace(game.Platform)
        && !string.IsNullOrWhiteSpace(game.Description)
        && !string.IsNullOrWhiteSpace(game.DevelopmentPhase)
        && !string.IsNullOrWhiteSpace(game.DeveloperName);

    private bool IsValidVisibilityForAdmin(ApprovedGame game) =>
        !IsAdmin() || game.Visibility is "Privada" or "Pública" or "Não listado";

    private static void ApplyEditableFields(ApprovedGame target, ApprovedGame source)
    {
        target.Title = source.Title.Trim();
        target.Version = source.Version.Trim();
        target.S3ObjectKey = source.S3ObjectKey.Trim();
        target.TrailerUrl = NormalizeOptionalText(source.TrailerUrl, 500);
        target.Platform = source.Platform.Trim();
        target.Description = source.Description.Trim();
        target.DevelopmentPhase = source.DevelopmentPhase.Trim();
        target.DeveloperName = source.DeveloperName.Trim();
        target.SystemRequirements = NormalizeOptionalText(source.SystemRequirements, 1000);
        target.TeamMembersJson = source.TeamMembersJson;
        target.BuildsJson = source.BuildsJson;

        target.Niche = NormalizeOptionalText(source.Niche, 80);
        target.Subject = NormalizeOptionalText(source.Subject, 80);
        target.GenresJson = SanitizeGenresJson(source.GenresJson);
        target.CoverImageKey = SanitizeMediaReference(source.CoverImageKey);
        target.GalleryImagesJson = SanitizeGalleryImagesJson(source.GalleryImagesJson);
        target.ContactPhone = NormalizeOptionalText(source.ContactPhone, 40);
        target.SocialLink = SanitizeSocialLink(source.SocialLink);
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? SanitizeGenresJson(string? genresJson)
    {
        if (string.IsNullOrWhiteSpace(genresJson)) return null;

        try
        {
            var raw = JsonSerializer.Deserialize<List<string>>(genresJson, WebJsonOptions);
            if (raw is null || raw.Count == 0) return null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<string>();
            foreach (var item in raw)
            {
                var trimmed = item?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.Length > 40) trimmed = trimmed[..40];
                if (seen.Add(trimmed))
                {
                    cleaned.Add(trimmed);
                    if (cleaned.Count >= 15) break;
                }
            }

            return cleaned.Count == 0 ? null : JsonSerializer.Serialize(cleaned, WebJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsSafeR2MediaKey(string value) =>
        (value.StartsWith("media/covers/", StringComparison.Ordinal)
         || value.StartsWith("media/gallery/", StringComparison.Ordinal))
        && !value.Contains("..", StringComparison.Ordinal)
        && value.Length <= 200;

    private static string? SanitizeMediaReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (IsSafeR2MediaKey(trimmed)) return trimmed;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && trimmed.Length <= 1000)
        {
            return uri.AbsoluteUri;
        }

        return null;
    }

    private static string? SanitizeGalleryImagesJson(string? galleryJson)
    {
        if (string.IsNullOrWhiteSpace(galleryJson)) return null;

        try
        {
            var raw = JsonSerializer.Deserialize<List<StoredGalleryImage>>(galleryJson, WebJsonOptions);
            if (raw is null || raw.Count == 0) return null;

            var cleaned = new List<StoredGalleryImage>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in raw)
            {
                var safeKey = SanitizeMediaReference(item.S3ObjectKey);
                if (safeKey is null || !seenKeys.Add(safeKey)) continue;

                var fileName = NormalizeOptionalText(item.FileName, 120) ?? "arte";
                cleaned.Add(new StoredGalleryImage(safeKey, fileName));
                if (cleaned.Count >= 8) break;
            }

            return cleaned.Count == 0 ? null : JsonSerializer.Serialize(cleaned, WebJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? SanitizeSocialLink(string? value)
    {
        var trimmed = NormalizeOptionalText(value, 300);
        if (trimmed is null) return null;

        var lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("javascript:", StringComparison.Ordinal)
            || lower.StartsWith("data:", StringComparison.Ordinal)
            || lower.StartsWith("vbscript:", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }

    private sealed record StoredGalleryImage(string S3ObjectKey, string FileName);
}

public sealed record CreateGameCommentRequest(string? Content, int? Rating);
public sealed record GameCommentResponse(Guid Id, string Author, string Content, int? Rating, DateTime CreatedAt, bool CanDelete);
public sealed record ManagedGameResponse(ApprovedGame Game, int CommentCount);
