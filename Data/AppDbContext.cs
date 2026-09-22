using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Models;

namespace ReactionChemistry.Backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Representa a tabela no banco de dados, utilizando o modelo ApprovedGame.
        public DbSet<ApprovedGame> Games { get; set; }
    }
}