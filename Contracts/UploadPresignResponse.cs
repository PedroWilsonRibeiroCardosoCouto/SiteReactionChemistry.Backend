namespace ReactionChemistry.Backend.Contracts;

/// <summary>URL temporária e metadados para enviar um build diretamente ao armazenamento.</summary>
public sealed record UploadPresignResponse(
    string UploadUrl,
    string S3ObjectKey,
    string ContentType);
