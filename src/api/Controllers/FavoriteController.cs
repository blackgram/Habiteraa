using Habitera.DTOs;
using Habitera.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Habitera.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class FavoriteController : ControllerBase
    {
        private readonly IFavoriteService _favoriteService;

        public FavoriteController(IFavoriteService favoriteService)
        {
            _favoriteService = favoriteService;
        }

        private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        /// <summary>Lightweight bookmark IDs only.</summary>
        [HttpGet("Ids")]
        public async Task<IActionResult> GetIds()
        {
            var result = await _favoriteService.GetFavoriteIdsAsync(UserId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet]
        public async Task<IActionResult> GetFavorites(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await _favoriteService.GetFavoritesAsync(
                UserId,
                new PaginatedRequest { PageNumber = pageNumber, PageSize = pageSize });
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{propertyId:guid}/Toggle")]
        public async Task<IActionResult> Toggle(Guid propertyId)
        {
            var result = await _favoriteService.ToggleAsync(UserId, propertyId);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Offline-first sync: client sends desired property IDs; server reconciles.
        /// </summary>
        [HttpPut("Sync")]
        public async Task<IActionResult> Sync([FromBody] FavoriteSyncRequest request)
        {
            var result = await _favoriteService.SyncAsync(UserId, request.PropertyIds);
            return StatusCode(result.StatusCode, result);
        }
    }
}
