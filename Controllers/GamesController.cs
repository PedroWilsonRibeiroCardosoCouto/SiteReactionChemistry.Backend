using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReactionChemistry.Backend.Data;
using ReactionChemistry.Backend.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq; // Necessário para a função Any()

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

        // GET: api/Games (Listar todos)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ApprovedGame>>> GetGames()
        {
            return await _context.Games.ToListAsync();
        }

        // POST: api/Games (Criar novo)
        [HttpPost]
        public async Task<ActionResult<ApprovedGame>> PostGame(ApprovedGame game)
        {
            _context.Games.Add(game);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetGames), new { id = game.Id }, game);
        }

        // --- NOVOS MÉTODOS PARA COMPLETAR O CRUD ---

        // PUT: api/Games/{id} (Atualizar jogo existente)
        [HttpPut("{id}")]
        public async Task<IActionResult> PutGame(Guid id, ApprovedGame game)
        {
            if (id != game.Id)
            {
                return BadRequest("O ID fornecido na URL não corresponde ao ID do jogo.");
            }

            _context.Entry(game).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!GameExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/Games/{id} (Remover jogo)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteGame(Guid id)
        {
            var game = await _context.Games.FindAsync(id);
            if (game == null)
            {
                return NotFound();
            }

            _context.Games.Remove(game);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // Método auxiliar para verificar se o jogo existe
        private bool GameExists(Guid id)
        {
            return _context.Games.Any(e => e.Id == id);
        }
    }
}