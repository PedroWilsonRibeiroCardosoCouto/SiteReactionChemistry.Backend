using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using ReactionChemistry.Backend.Models;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace ReactionChemistry.Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DownloadController : ControllerBase
    {
        private readonly IAmazonS3 _s3Client;
        private readonly AppDbContext _context;
        private readonly string _bucketName;
        private readonly ILogger<DownloadController> _logger;

        public DownloadController(
            IAmazonS3 s3Client,
            AppDbContext context,
            IConfiguration configuration,
            ILogger<DownloadController> logger)
        {
            _s3Client = s3Client;
            _context = context;
            _bucketName = configuration["CloudflareR2:BucketName"] ?? "reaction-chemistry-releases";
            _logger = logger;
        }

        // Rota: GET api/Download/{gameId}
        [HttpGet("{gameId}")]
        public async Task<IActionResult> GetSecureDownloadLink(
            Guid gameId,
            [FromQuery] string? key,
            CancellationToken cancellationToken)
        {
            // 1. Verifica no PostgreSQL se o jogo existe e está aprovado (ativo)
            var game = await _context.Games.FirstOrDefaultAsync(
                g => g.Id == gameId && g.IsActive,
                cancellationToken);
            var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
                ? parsedUserId
                : (Guid?)null;
            var isAdmin = User.IsInRole(UserRoles.Admin);
            var isPublic = game?.Visibility == "Pública";
            if (game == null || (!isPublic && !isAdmin && game.OwnerId != userId))
            {
                return NotFound("Jogo não encontrado ou não autorizado para download.");
            }

            var s3ObjectKey = game.S3ObjectKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                List<GameBuild>? builds;
                try
                {
                    builds = JsonSerializer.Deserialize<List<GameBuild>>(
                        game.BuildsJson ?? "[]",
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                catch (JsonException exception)
                {
                    _logger.LogError(exception, "Metadados de builds inválidos para o jogo {GameId}.", gameId);
                    return Problem(
                        title: "Os metadados dos builds deste jogo estão inválidos.",
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                var matchingBuild = builds?.FirstOrDefault(build =>
                    string.Equals(build.S3ObjectKey, key, StringComparison.Ordinal));
                if (matchingBuild is null)
                {
                    return NotFound("Build não encontrado para este jogo.");
                }

                s3ObjectKey = matchingBuild.S3ObjectKey;
            }

            try
            {
                // 2. Gera a URL temporária baseada na chave do ficheiro (S3ObjectKey)
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = _bucketName,
                    Key = s3ObjectKey,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddMinutes(5) // Magia: O link morre em 5 minutos
                };

                string urlTemporaria = _s3Client.GetPreSignedURL(request);
                await _context.Games
                    .Where(item => item.Id == gameId)
                    .ExecuteUpdateAsync(
                        update => update.SetProperty(item => item.DownloadCount, item => item.DownloadCount + 1),
                        cancellationToken);

                return Ok(new
                {
                    Message = "Acesso autorizado.",
                    GameTitle = game.Title,
                    Version = game.Version,
                    DownloadUrl = urlTemporaria
                });
            }
            catch (AmazonS3Exception exception)
            {
                _logger.LogError(exception, "Falha ao gerar URL de download para o jogo {GameId}.", gameId);
                return Problem(
                    title: "Erro interno ao contactar o armazenamento de arquivos.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }

        private sealed record GameBuild(string S3ObjectKey, string Platform);
    }
}
