using System.ComponentModel.DataAnnotations;

namespace ReactionChemistry.Backend.Contracts;

/// <summary>Dados necessários para criar uma URL temporária de upload de imagem (capa ou galeria).</summary>
public sealed record CreateMediaUploadPresignRequest
{
    /// <summary>Nome original da imagem enviada pelo navegador.</summary>
    [Required, MaxLength(255)]
    public required string FileName { get; init; }

    /// <summary>Tipo de arte: "cover" (capa) ou "gallery" (galeria/screenshot).</summary>
    [Required, MaxLength(16)]
    public required string MediaKind { get; init; }
}
