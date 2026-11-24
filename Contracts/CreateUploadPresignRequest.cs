using System.ComponentModel.DataAnnotations;

namespace ReactionChemistry.Backend.Contracts;

/// <summary>Dados necessários para criar uma URL temporária de upload de build.</summary>
public sealed record CreateUploadPresignRequest
{
    /// <summary>Nome original do arquivo enviado pelo navegador.</summary>
    [Required, MaxLength(255)]
    public required string FileName { get; init; }

    /// <summary>Plataforma a que o build se destina.</summary>
    [Required, MaxLength(32)]
    public required string Platform { get; init; }
}
