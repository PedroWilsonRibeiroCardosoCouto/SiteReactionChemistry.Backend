using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ReactionChemistry.Backend.Models
{
    public class ApprovedGame
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        
        // O nome exato do ficheiro .zip encriptado dentro do seu Cloudflare R2
        public string S3ObjectKey { get; set; } = string.Empty; 
        
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // --- NOVOS CAMPOS DO DOCUMENTO DE REQUISITOS ---

        // Opcional (?) - Vídeo Trailler
        public string? TrailerUrl { get; set; } 

        // Obrigatório - Ex: "mobile", "pc windows", "pc linux", "VR"
        public string Platform { get; set; } = string.Empty; 

        // Obrigatório - Informações (História narrativa ou objetiva)
        public string Description { get; set; } = string.Empty; 

        // Obrigatório - Ex: "Beta 1.0", "Finalizado", "em testes de release"
        public string DevelopmentPhase { get; set; } = string.Empty; 

        // Obrigatório - Ex: "Privada", "Pública", "Não listado"
        public string Visibility { get; set; } = "Privada"; 

        // Obrigatório - Nome do desenvolvedor / equipe
        public string DeveloperName { get; set; } = string.Empty; 

        // Opcional (?) - Especificações dos requisitos de sistema
        public string? SystemRequirements { get; set; } 

        public string? TeamMembersJson { get; set; }

        public string? BuildsJson { get; set; }

        // --- CLASSIFICAÇÃO (NICHO, MATÉRIA/ÁREA E GÊNEROS) ---
        public string? Niche { get; set; }
        public string? Subject { get; set; }
        public string? GenresJson { get; set; }

        // --- ARTES E IDENTIDADE VISUAL (CAPA E GALERIA) ---
        public string? CoverImageKey { get; set; }
        public string? GalleryImagesJson { get; set; }

        // --- NETWORK / CONTATO (OPCIONAIS) ---
        public string? ContactPhone { get; set; }
        public string? SocialLink { get; set; }

        public Guid? OwnerId { get; set; }
        public AppUser? Owner { get; set; }
        public int ViewCount { get; set; }
        public int DownloadCount { get; set; }

        // --- CAMPOS CALCULADOS NA RESPOSTA DA API ---
        [NotMapped, ValidateNever]
        public string? CoverImageUrl { get; set; }

        [NotMapped, ValidateNever]
        public List<GameMediaItem>? GalleryImages { get; set; } = [];

        [NotMapped, ValidateNever]
        public double AverageRating { get; set; }

        [NotMapped, ValidateNever]
        public int RatingCount { get; set; }
    }

    public sealed record GameMediaItem(string Key, string Url, string FileName);
}