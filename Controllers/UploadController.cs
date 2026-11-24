using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReactionChemistry.Backend.Contracts;
using System;
using System.IO;

namespace ReactionChemistry.Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class UploadController : ControllerBase
{
    private static readonly string[] SupportedPlatforms =
    [
        "PC Windows",
        "PC Linux",
        "Mobile Android",
        "VR"
    ];

    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<UploadController> _logger;

    public UploadController(
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<UploadController> logger)
    {
        _s3Client = s3Client;
        _bucketName = configuration["CloudflareR2:BucketName"] ?? "reaction-chemistry-releases";
        _logger = logger;
    }

    /// <summary>Cria uma URL PUT temporária para enviar um build ao Cloudflare R2.</summary>
    [HttpPost("presign")]
    [EnableRateLimiting("UploadPresign")]
    [ProducesResponseType<UploadPresignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public ActionResult<UploadPresignResponse> CreatePresignedUpload(
        [FromBody] CreateUploadPresignRequest request)
    {
        var platform = request.Platform.Trim();
        if (!Array.Exists(SupportedPlatforms, supported =>
                string.Equals(supported, platform, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Plataforma não suportada.",
                Detail = "Selecione uma plataforma disponível no formulário."
            });
        }

        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        var isAndroid = string.Equals(platform, "Mobile Android", StringComparison.OrdinalIgnoreCase);
        if (extension != ".zip" && !(isAndroid && extension == ".apk"))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tipo de arquivo não suportado.",
                Detail = isAndroid
                    ? "A plataforma Android aceita arquivos .apk ou .zip."
                    : "Esta plataforma aceita somente arquivos .zip."
            });
        }

        var contentType = extension == ".apk"
            ? "application/vnd.android.package-archive"
            : "application/zip";
        var s3ObjectKey = $"builds/{Guid.NewGuid():N}{extension}";

        try
        {
            var presignRequest = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = s3ObjectKey,
                Verb = HttpVerb.PUT,
                ContentType = contentType,
                Expires = DateTime.UtcNow.AddMinutes(15)
            };

            var uploadUrl = _s3Client.GetPreSignedURL(presignRequest);
            return Ok(new UploadPresignResponse(uploadUrl, s3ObjectKey, contentType));
        }
        catch (AmazonS3Exception exception)
        {
            _logger.LogError(exception, "Falha ao gerar URL de upload para a plataforma {Platform}.", platform);
            return Problem(
                title: "Não foi possível preparar o upload.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
