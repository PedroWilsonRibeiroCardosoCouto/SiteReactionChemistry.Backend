using System.Security.Claims;
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
    private readonly AppDbContext _context;

    public GamesController(AppDbContext context) => _context = context;

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApprovedGame>>> GetGames()
    {
        var games = _context.Games.AsNoTracking();
        if (IsAdmin())
        {
            return await games.OrderBy(game => game.Title).ToListAsync();
        }

        var userId = GetUserId();
        games = userId.HasValue
            ? games.Where(game => game.OwnerId == userId || (game.IsActive && game.Visibility == "Pública"))
            : games.Where(game => game.IsActive && game.Visibility == "Pública");
        return await games.OrderBy(game => game.Title).ToListAsync();
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
        return game is not null && CanView(game) ? game : NotFound();
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

        var game = await _context.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
        if (game is null || !game.IsActive || game.Visibility != "Pública") return NotFound();

        var comment = new GameComment
        {
            GameId = id,
            UserId = GetUserId()!.Value,
            Content = content,
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
            new GameCommentResponse(comment.Id, userName, comment.Content, comment.CreatedAt, true));
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

    private static bool IsPublic(ApprovedGame game) =>
        game.IsActive && game.Visibility == "Pública";

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
        target.TrailerUrl = source.TrailerUrl;
        target.Platform = source.Platform.Trim();
        target.Description = source.Description.Trim();
        target.DevelopmentPhase = source.DevelopmentPhase.Trim();
        target.DeveloperName = source.DeveloperName.Trim();
        target.SystemRequirements = source.SystemRequirements;
        target.TeamMembersJson = source.TeamMembersJson;
        target.BuildsJson = source.BuildsJson;
    }
}

public sealed record CreateGameCommentRequest(string? Content);
public sealed record GameCommentResponse(Guid Id, string Author, string Content, DateTime CreatedAt, bool CanDelete);
public sealed record ManagedGameResponse(ApprovedGame Game, int CommentCount);