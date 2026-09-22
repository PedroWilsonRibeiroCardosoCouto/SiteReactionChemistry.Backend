using System;

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
    }
}