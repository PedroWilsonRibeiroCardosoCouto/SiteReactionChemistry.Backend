using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using ReactionChemistry.Backend.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ReactionChemistry.Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GamesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public GamesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Games
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ApprovedGame>>> GetGames()
        {
            return await _context.Games.ToListAsync();
        }

        // POST: api/Games
        [HttpPost]
        public async Task<ActionResult<ApprovedGame>> PostGame(ApprovedGame game)
        {
            // O EF Core vai gerar automaticamente o Id (Guid) e a data (CreatedAt)
            _context.Games.Add(game);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetGames), new { id = game.Id }, game);
        }
    }
}