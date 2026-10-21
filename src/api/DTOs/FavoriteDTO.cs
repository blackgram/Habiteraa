using System.ComponentModel.DataAnnotations;

namespace Habitera.DTOs
{
    public class FavoriteSyncRequest
    {
        /// <summary>
        /// Desired bookmarked property IDs (client is source of truth for offline-first sync).
        /// </summary>
        [Required]
        public List<Guid> PropertyIds { get; set; } = new();
    }

    public class FavoriteIdsDTO
    {
        public List<Guid> PropertyIds { get; set; } = new();
    }
}
