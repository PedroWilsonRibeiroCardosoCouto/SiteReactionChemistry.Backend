using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using ReactionChemistry.Backend.Models;

namespace ReactionChemistry.Backend.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context) => _context = context;

    [HttpGet("me")]
    public async Task<ActionResult<DeveloperProfileResponse>> GetMyProfile()
    {
        var userId = GetUserId();
        var user = await _context.Users.SingleOrDefaultAsync(item => item.Id == userId);
        if (user is null) return NotFound();
        return Ok(await BuildProfile(user));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileRequest request)
    {
        var displayName = request.DisplayName?.Trim();
        if (displayName is null || displayName.Length is < 2 or > 100)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Nome de exibição inválido.",
                Detail = "O nome deve ter entre 2 e 100 caracteres."
            });
        }

        var user = await _context.Users.SingleOrDefaultAsync(item => item.Id == GetUserId());
        if (user is null) return NotFound();
        user.DisplayName = displayName;
        await _context.SaveChangesAsync();
        return Ok(await BuildProfile(user));
    }

    [HttpGet]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<IEnumerable<AdminUserResponse>>> GetUsers()
    {
        var users = await _context.Users
            .OrderBy(user => user.CreatedAt)
            .Select(user => new AdminUserResponse(
                user.Id,
                user.DisplayName,
                user.Email,
                user.Provider,
                user.Role,
                user.IsSuspended,
                user.CreatedAt,
                user.LastLoginAt,
                _context.Games.Count(game => game.OwnerId == user.Id),
                _context.Games.Where(game => game.OwnerId == user.Id).Sum(game => (long?)game.ViewCount) ?? 0,
                _context.Games.Where(game => game.OwnerId == user.Id).Sum(game => (long?)game.DownloadCount) ?? 0,
                _context.GameComments.Count(comment => comment.Game.OwnerId == user.Id)))
            .ToListAsync();
        return Ok(users);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] AdminUpdateUserRequest request)
    {
        if (request.Role is not UserRoles.Admin and not UserRoles.User)
        {
            return BadRequest(new ProblemDetails { Title = "Papel de usuário inválido." });
        }

        var user = await _context.Users.SingleOrDefaultAsync(item => item.Id == id);
        if (user is null) return NotFound();
        if (user.Role == UserRoles.Admin && request.Role != UserRoles.Admin
            && await _context.Users.CountAsync(item => item.Role == UserRoles.Admin) <= 1)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Não é possível remover o último administrador."
            });
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            var displayName = request.DisplayName.Trim();
            if (displayName.Length is < 2 or > 100)
            {
                return BadRequest(new ProblemDetails { Title = "Nome de exibição inválido." });
            }
            user.DisplayName = displayName;
        }

        user.Role = request.Role;
        user.IsSuspended = request.IsSuspended;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        if (id == GetUserId())
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Não é possível excluir a própria conta de administrador."
            });
        }

        var user = await _context.Users.SingleOrDefaultAsync(item => item.Id == id);
        if (user is null) return NotFound();
        if (user.Role == UserRoles.Admin
            && await _context.Users.CountAsync(item => item.Role == UserRoles.Admin) <= 1)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Não é possível excluir o último administrador."
            });
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<DeveloperProfileResponse> BuildProfile(AppUser user)
    {
        var ownedGames = _context.Games.Where(game =>
            user.Role == UserRoles.Admin || game.OwnerId == user.Id);
        var profile = new DeveloperProfileResponse(
            user.Id,
            user.DisplayName,
            user.Email,
            user.Role,
            user.IsSuspended,
            user.CreatedAt,
            await ownedGames.CountAsync(),
            await ownedGames.CountAsync(game => game.IsActive && game.Visibility == "Pública"),
            await ownedGames.SumAsync(game => (long?)game.ViewCount) ?? 0,
            await ownedGames.SumAsync(game => (long?)game.DownloadCount) ?? 0,
            await _context.GameComments.CountAsync(comment =>
                user.Role == UserRoles.Admin || comment.Game.OwnerId == user.Id));
        return profile;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record UpdateProfileRequest(string DisplayName);
public sealed record AdminUpdateUserRequest(string Role, bool IsSuspended, string? DisplayName);
public sealed record DeveloperProfileResponse(
    Guid Id,
    string DisplayName,
    string Email,
    string Role,
    bool IsSuspended,
    DateTime CreatedAt,
    int TotalGames,
    int PublishedGames,
    long Views,
    long Downloads,
    int CommentsReceived);
public sealed record AdminUserResponse(
    Guid Id,
    string DisplayName,
    string Email,
    string Provider,
    string Role,
    bool IsSuspended,
    DateTime CreatedAt,
    DateTime LastLoginAt,
    int TotalGames,
    long Views,
    long Downloads,
    int CommentsReceived);
