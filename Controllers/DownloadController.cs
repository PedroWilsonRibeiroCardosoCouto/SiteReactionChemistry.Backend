using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using System;
using System.Threading.Tasks;

namespace ReactionChemistry.Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DownloadController : ControllerBase
    {
        private readonly IAmazonS3 _s3Client;
        private readonly AppDbContext _context;
        private readonly string _bucketName = "reaction-chemistry-releases";

        public DownloadController(IAmazonS3 s3Client, AppDbContext context)
        {
            _s3Client = s3Client;
            _context = context;
        }

        // Rota: GET api/Download/{gameId}
        [HttpGet("{gameId}")]
        public async Task<IActionResult> GetSecureDownloadLink(Guid gameId)
        {
            // 1. Verifica no PostgreSQL se o jogo existe e está aprovado (ativo)
            var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == gameId && g.IsActive);
            
            if (game == null)
            {
                return NotFound("Jogo não encontrado ou não autorizado para download.");
            }

            try
            {
                // 2. Gera a URL temporária baseada na chave do ficheiro (S3ObjectKey)
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = _bucketName,
                    Key = game.S3ObjectKey,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddMinutes(5) // Magia: O link morre em 5 minutos
                };

                string urlTemporaria = _s3Client.GetPreSignedURL(request);

                return Ok(new
                {
                    Message = "Acesso autorizado.",
                    GameTitle = game.Title,
                    Version = game.Version,
                    DownloadUrl = urlTemporaria
                });
            }
            catch (Exception ex)
            {
                // Registamos o erro exato na consola do servidor para podermos depurar
                Console.WriteLine($"[ERRO CLOUDFLARE R2] A geração do link falhou: {ex.Message}");
                
                // Mas devolvemos uma mensagem genérica e segura para o utilizador
                return StatusCode(500, "Erro interno ao contactar o cofre de ficheiros.");
            }
        }
    }
}
