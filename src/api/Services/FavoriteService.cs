using AutoMapper;
using Habitera.DTOs;
using Habitera.Repositories;

namespace Habitera.Services
{
    public interface IFavoriteService
    {
        Task<OperationResult<FavoriteIdsDTO>> GetFavoriteIdsAsync(Guid userId);
        Task<PaginatedOperationResult<PropertyDTO>> GetFavoritesAsync(
            Guid userId,
            PaginatedRequest request);
        Task<OperationResult<bool>> ToggleAsync(Guid userId, Guid propertyId);
        Task<OperationResult<FavoriteIdsDTO>> SyncAsync(Guid userId, IEnumerable<Guid> propertyIds);
    }

    public class FavoriteService : IFavoriteService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<FavoriteService> _logger;

        public FavoriteService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            ILogger<FavoriteService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<OperationResult<FavoriteIdsDTO>> GetFavoriteIdsAsync(Guid userId)
        {
            try
            {
                var ids = await _unitOfWork.Favorites.GetUserFavoritePropertyIdsAsync(userId);
                return OperationResult<FavoriteIdsDTO>.Successful(new FavoriteIdsDTO
                {
                    PropertyIds = ids
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading favorite ids for {UserId}", userId);
                return OperationResult<FavoriteIdsDTO>.Failure(
                    "Unable to load bookmarks",
                    500);
            }
        }

        public async Task<PaginatedOperationResult<PropertyDTO>> GetFavoritesAsync(
            Guid userId,
            PaginatedRequest request)
        {
            try
            {
                var page = Math.Max(1, request.PageNumber);
                var size = Math.Clamp(request.PageSize, 1, 50);
                var result = await _unitOfWork.Favorites.GetUserFavoritesAsync(userId, page, size);
                var dtos = result.Items.Select(p =>
                {
                    var dto = _mapper.Map<PropertyDTO>(p);
                    dto.IsFavorited = true;
                    return dto;
                }).ToList();

                return PaginatedOperationResult<PropertyDTO>.Successful(
                    dtos,
                    result.TotalCount,
                    page,
                    size);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading favorites for {UserId}", userId);
                return PaginatedOperationResult<PropertyDTO>.Failure(
                    "Unable to load bookmarks",
                    500);
            }
        }

        public async Task<OperationResult<bool>> ToggleAsync(Guid userId, Guid propertyId)
        {
            try
            {
                var property = await _unitOfWork.Properties.GetByIdAsync(propertyId);
                if (property == null)
                {
                    return OperationResult<bool>.Failure("Property not found", 404);
                }

                var isBookmarked = await _unitOfWork.Favorites.ToggleFavoriteAsync(userId, propertyId);
                return OperationResult<bool>.Successful(isBookmarked);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling favorite {PropertyId}", propertyId);
                return OperationResult<bool>.Failure("Unable to update bookmark", 500);
            }
        }

        public async Task<OperationResult<FavoriteIdsDTO>> SyncAsync(
            Guid userId,
            IEnumerable<Guid> propertyIds)
        {
            try
            {
                var desired = propertyIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToHashSet();

                var current = (await _unitOfWork.Favorites.GetUserFavoritePropertyIdsAsync(userId))
                    .ToHashSet();

                foreach (var id in desired.Except(current))
                {
                    var property = await _unitOfWork.Properties.GetByIdAsync(id);
                    if (property == null) continue;
                    await _unitOfWork.Favorites.SetFavoriteAsync(userId, id, true);
                }

                foreach (var id in current.Except(desired))
                {
                    await _unitOfWork.Favorites.SetFavoriteAsync(userId, id, false);
                }

                await _unitOfWork.SaveChangesAsync();

                var synced = await _unitOfWork.Favorites.GetUserFavoritePropertyIdsAsync(userId);
                return OperationResult<FavoriteIdsDTO>.Successful(new FavoriteIdsDTO
                {
                    PropertyIds = synced
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing favorites for {UserId}", userId);
                return OperationResult<FavoriteIdsDTO>.Failure("Unable to sync bookmarks", 500);
            }
        }
    }
}
