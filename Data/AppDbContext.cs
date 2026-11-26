using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Models;

namespace ReactionChemistry.Backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Representa a tabela no banco de dados, utilizando o modelo ApprovedGame.
        public DbSet<ApprovedGame> Games { get; set; }
        public DbSet<AppUser> Users { get; set; }
        public DbSet<GameComment> GameComments { get; set; }
        public DbSet<ExternalAuthCode> ExternalAuthCodes { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AppUser>()
                .HasIndex(user => new { user.Provider, user.ProviderSubject })
                .IsUnique();
            modelBuilder.Entity<AppUser>()
                .HasIndex(user => user.Email);

            modelBuilder.Entity<ApprovedGame>()
                .HasOne(game => game.Owner)
                .WithMany()
                .HasForeignKey(game => game.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<GameComment>()
                .HasOne(comment => comment.Game)
                .WithMany()
                .HasForeignKey(comment => comment.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<GameComment>()
                .HasOne(comment => comment.User)
                .WithMany()
                .HasForeignKey(comment => comment.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExternalAuthCode>()
                .HasIndex(code => code.CodeHash)
                .IsUnique();
            modelBuilder.Entity<ExternalAuthCode>()
                .HasOne(code => code.User)
                .WithMany()
                .HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}