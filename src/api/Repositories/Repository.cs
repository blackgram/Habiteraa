using Habitera.Data;
using Habitera.Models;
using Habitera.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Habitera.Repositories
{
    public interface IGenericRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(Guid id);
        Task<IEnumerable<T>> GetAllAsync();
        Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);
        Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
        Task AddAsync(T entity);
        Task AddRangeAsync(IEnumerable<T> entities);
        void Update(T entity);
        void Remove(T entity);
        void RemoveRange(IEnumerable<T> entities);
        Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null);
        Task<bool> AnyAsync(Expression<Func<T, bool>> predicate);

        Task<PagedResult<T>> GetPagedAsync<TKey>(
            int pageNumber,
            int pageSize,
            Expression<Func<T, bool>>? predicate = null,
            Expression<Func<T, TKey>>? orderBy = null,
            bool descending = false);
    }

    public interface IEmailVerificationTokenRepository : IGenericRepository<EmailVerificationToken>
    {
        Task<List<EmailVerificationToken>> GetActiveTokensByEmailAsync(string email);
        Task<EmailVerificationToken?> GetValidTokenAsync(string email, string tokenHash);
        Task InvalidateTokensAsync(IEnumerable<EmailVerificationToken> tokens);
        Task<EmailVerificationToken?> GetLatestTokenForUserAsync(Guid userId);
    }

    public interface IPasswordResetTokenRepository : IGenericRepository<PasswordResetToken>
    {
        Task<List<PasswordResetToken>> GetActivePasswordResetTokensByEmailAsync(Guid userId);
        Task<PasswordResetToken?> GetValidPasswordResetTokenAsync(Guid userId, string tokenHash);
        Task InvalidatePasswordResetTokensAsync(IEnumerable<PasswordResetToken> tokens);
        Task<PasswordResetToken?> GetLatestPasswordResetTokenForUserAsync(Guid userId);
    }

    public interface IPropertyRepository : IGenericRepository<Property>
    {
        Task<IEnumerable<Property>> GetPropertiesByAgentAsync(Guid agentId);
        Task<PagedResult<Property>> GetPropertiesByAgentPagedAsync(
            Guid agentId,
            int pageNumber,
            int pageSize,
            PropertyStatus? status = null);

        Task<Property?> GetPropertyWithImagesAsync(Guid propertyId);
        Task<Property?> GetPropertyWithAllDetailsAsync(Guid propertyId);

        Task<bool> UpdatePropertyStatusAsync(Guid propertyId, PropertyStatus status);
        Task<bool> PublishPropertyAsync(Guid propertyId);
        Task<bool> UnpublishPropertyAsync(Guid propertyId);
        Task<IEnumerable<Property>> GetAllPublishedAsync(PropertyStatus? status = null);
        Task<int> IncrementViewCountAsync(Guid propertyId);
        Task<Dictionary<PropertyStatus, int>> GetPropertyCountByStatusAsync(Guid agentId);

        Task<PagedResult<Property>> GetPendingReviewPropertiesPagedAsync(int pageNumber, int pageSize);
        Task<IEnumerable<Property>> GetListingsDueForAutoApprovalAsync(TimeSpan autoApproveAfter);

        Task<IEnumerable<Property>> GetPropertiesNearLocationAsync(
            decimal latitude,
            decimal longitude,
            double radiusInKm,
            int limit = 50);

        Task<IEnumerable<Property>> GetPropertiesByIdsAsync(IEnumerable<Guid> propertyIds);
        Task<bool> PropertyExistsAsync(Guid propertyId);
        Task<bool> IsAgentOwnerAsync(Guid propertyId, Guid agentId);
        Task<Dictionary<Guid, int>> GetContactCountsAsync(IEnumerable<Guid> propertyIds);
    }

    public interface IPropertyImageRepository : IGenericRepository<PropertyImage>
    {
        Task<IEnumerable<PropertyImage>> GetImagesByPropertyIdAsync(Guid propertyId);
        Task<PropertyImage?> GetPrimaryImageAsync(Guid propertyId);
        Task<bool> SetPrimaryImageAsync(Guid propertyId, Guid imageId);
        Task<int> DeleteImagesByPropertyIdAsync(Guid propertyId);
        Task<bool> ReorderImagesAsync(Guid propertyId, Dictionary<Guid, int> imageOrders);
    }

    public interface IPropertyAmenityRepository : IGenericRepository<PropertyAmenity>
    {
        Task<IEnumerable<PropertyAmenity>> GetAmenitiesByPropertyIdAsync(Guid propertyId);
        Task<PropertyAmenity?> GetAmenityByCategoryAsync(Guid propertyId, AmenityCategory category);
        Task<bool> UpsertAmenityAsync(Guid propertyId, AmenityCategory category, Dictionary<string, object> amenities);
        Task<int> DeleteAmenitiesByPropertyIdAsync(Guid propertyId);
    }

    public interface IFavoriteRepository : IGenericRepository<Favorite>
    {
        Task<PagedResult<Property>> GetUserFavoritesAsync(Guid userId, int pageNumber, int pageSize);
        Task<List<Guid>> GetUserFavoritePropertyIdsAsync(Guid userId);
        Task<bool> IsFavoriteAsync(Guid userId, Guid propertyId);
        Task<Favorite?> GetFavoriteAsync(Guid userId, Guid propertyId);
        Task<bool> ToggleFavoriteAsync(Guid userId, Guid propertyId);
        Task SetFavoriteAsync(Guid userId, Guid propertyId, bool bookmarked);
        Task<int> GetFavoriteCountByPropertyAsync(Guid propertyId);
        Task<Dictionary<Guid, int>> GetFavoriteCountsAsync(IEnumerable<Guid> propertyIds);
        Task<Dictionary<Guid, bool>> GetFavoriteStatusesAsync(Guid userId, IEnumerable<Guid> propertyIds);
    }

    public interface IViewingBookingRepository : IGenericRepository<ViewingBooking>
    {
        Task<PagedResult<ViewingBooking>> GetUserBookingsAsync(
            Guid userId,
            int pageNumber,
            int pageSize,
            BookingStatus? status = null);

        Task<PagedResult<ViewingBooking>> GetAgentBookingsAsync(
            Guid agentId,
            int pageNumber,
            int pageSize,
            BookingStatus? status = null);

        Task<IEnumerable<ViewingBooking>> GetPropertyBookingsAsync(Guid propertyId);

        Task<bool> UpdateBookingStatusAsync(Guid bookingId, BookingStatus status);
        Task<bool> CancelBookingAsync(Guid bookingId);

        Task<bool> IsSlotAvailableAsync(Guid propertyId, DateTime scheduledDate);
        Task<IEnumerable<DateTime>> GetBookedSlotsAsync(Guid propertyId, DateTime startDate, DateTime endDate);
    }

    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected readonly ApplicationDbContext _context;
        protected readonly DbSet<T> _dbSet;

        public GenericRepository(ApplicationDbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public virtual async Task<T?> GetByIdAsync(Guid id)
        {
            return await _dbSet.FindAsync(id);
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        public virtual async Task AddAsync(T entity)
        {
            await _dbSet.AddAsync(entity);
        }

        public virtual async Task AddRangeAsync(IEnumerable<T> entities)
        {
            await _dbSet.AddRangeAsync(entities);
        }

        public virtual void Update(T entity)
        {
            _dbSet.Update(entity);
        }

        public virtual void Remove(T entity)
        {
            _dbSet.Remove(entity);
        }

        public virtual void RemoveRange(IEnumerable<T> entities)
        {
            _dbSet.RemoveRange(entities);
        }

        public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.Where(predicate).ToListAsync();
        }

        public virtual async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.FirstOrDefaultAsync(predicate);
        }

        public virtual async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.AnyAsync(predicate);
        }

        public virtual async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
        {
            if (predicate == null)
            {
                return await _dbSet.CountAsync();
            }
            return await _dbSet.CountAsync(predicate);
        }

        public virtual async Task<PagedResult<T>> GetPagedAsync<TKey>(
            int pageNumber,
            int pageSize,
            Expression<Func<T, bool>>? predicate = null,
            Expression<Func<T, TKey>>? orderBy = null,
            bool descending = false)
        {
            var query = _dbSet.AsQueryable();

            if (predicate != null)
            {
                query = query.Where(predicate);
            }

            var totalCount = await query.CountAsync();

            if (orderBy != null)
            {
                query = descending
                    ? query.OrderByDescending(orderBy)
                    : query.OrderBy(orderBy);
            }

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<T>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
    }

    public class EmailVerificationTokenRepository : GenericRepository<EmailVerificationToken>, IEmailVerificationTokenRepository
    {
        public EmailVerificationTokenRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<List<EmailVerificationToken>> GetActiveTokensByEmailAsync(string email)
        {
            return await _context.EmailVerificationTokens
                .Where(t =>
                    t.Email == email &&
                    !t.Used &&
                    t.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();
        }

        public async Task<EmailVerificationToken?> GetValidTokenAsync(string email, string tokenHash)
        {
            return await _context.EmailVerificationTokens.FirstOrDefaultAsync(t =>
                t.Email == email &&
                t.TokenHash == tokenHash &&
                !t.Used &&
                t.ExpiresAt > DateTime.UtcNow
            );
        }

        public async Task InvalidateTokensAsync(IEnumerable<EmailVerificationToken> tokens)
        {
            foreach (var token in tokens)
            {
                token.Used = true;
                token.UsedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<EmailVerificationToken?> GetLatestTokenForUserAsync(Guid userId)
        {
            return await _context.EmailVerificationTokens
                .Where(t =>
                    t.UserId == userId &&
                    !t.Used &&
                    t.ExpiresAt > DateTime.UtcNow
                )
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
        }
    }

    public class PasswordResetTokenRepository : GenericRepository<PasswordResetToken>, IPasswordResetTokenRepository
    {
        public PasswordResetTokenRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<List<PasswordResetToken>> GetActivePasswordResetTokensByEmailAsync(Guid userId)
        {
            return await _context.PasswordResetTokens
                .Where(t =>
                    t.UserId == userId &&
                    !t.Used &&
                    t.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();
        }

        public async Task<PasswordResetToken?> GetValidPasswordResetTokenAsync(Guid userId, string tokenHash)
        {
            return await _context.PasswordResetTokens.FirstOrDefaultAsync(t =>
                t.UserId == userId &&
                t.TokenHash == tokenHash &&
                !t.Used &&
                t.ExpiresAt > DateTime.UtcNow
            );
        }

        public async Task InvalidatePasswordResetTokensAsync(IEnumerable<PasswordResetToken> tokens)
        {
            foreach (var token in tokens)
            {
                token.Used = true;
                token.UsedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<PasswordResetToken?> GetLatestPasswordResetTokenForUserAsync(Guid userId)
        {
            return await _context.PasswordResetTokens
                .Where(t =>
                    t.UserId == userId &&
                    !t.Used &&
                    t.ExpiresAt > DateTime.UtcNow
                )
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
        }
    }

    public class PropertyRepository : GenericRepository<Property>, IPropertyRepository
    {
        public PropertyRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Property>> GetPropertiesByAgentAsync(Guid agentId)
        {
            return await _dbSet
                .Where(p => p.AgentId == agentId)
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<PagedResult<Property>> GetPropertiesByAgentPagedAsync(
            Guid agentId,
            int pageNumber,
            int pageSize,
            PropertyStatus? status = null)
        {
            var query = _dbSet
                .Where(p => p.AgentId == agentId)
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder).Take(1))
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(p => p.Status == status.Value);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Property>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<Property?> GetPropertyWithImagesAsync(Guid propertyId)
        {
            return await _dbSet
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder))
                .FirstOrDefaultAsync(p => p.Id == propertyId);
        }

        public async Task<Property?> GetPropertyWithAllDetailsAsync(Guid propertyId)
        {
            return await _dbSet
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder))
                .Include(p => p.Amenities)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == propertyId);
        }

        public async Task<bool> UpdatePropertyStatusAsync(Guid propertyId, PropertyStatus status)
        {
            var property = await _dbSet.FindAsync(propertyId);
            if (property == null) return false;

            property.Status = status;
            property.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> PublishPropertyAsync(Guid propertyId)
        {
            var property = await _dbSet.FindAsync(propertyId);
            if (property == null) return false;

            property.Status = PropertyStatus.Active;
            property.IsPublished = true;
            property.PublishedAt = DateTime.UtcNow;
            property.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<PagedResult<Property>> GetPendingReviewPropertiesPagedAsync(
            int pageNumber,
            int pageSize)
        {
            var query = _dbSet
                .Where(p =>
                    p.Status == PropertyStatus.Pending &&
                    p.ReviewSubmittedAt != null &&
                    !p.IsPublished)
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder).Take(1))
                .OrderBy(p => p.ReviewSubmittedAt);

            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Property>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<IEnumerable<Property>> GetListingsDueForAutoApprovalAsync(
            TimeSpan autoApproveAfter)
        {
            var cutoff = DateTime.UtcNow - autoApproveAfter;

            return await _dbSet
                .Where(p =>
                    p.Status == PropertyStatus.Pending &&
                    p.ReviewSubmittedAt != null &&
                    p.ReviewSubmittedAt <= cutoff &&
                    !p.IsPublished)
                .Include(p => p.Images)
                .OrderBy(p => p.ReviewSubmittedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task<bool> UnpublishPropertyAsync(Guid propertyId)
        {
            var property = await _dbSet.FindAsync(propertyId);
            if (property == null) return false;

            property.IsPublished = false;
            property.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<Property>> GetAllPublishedAsync(PropertyStatus? status = null)
        {
            var query = _dbSet
                .Where(p => p.IsPublished)
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder).Take(1))
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(p => p.Status == status.Value);
            }

            return await query
                .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
                .ToListAsync();
        }

        public async Task<int> IncrementViewCountAsync(Guid propertyId)
        {
            var property = await _dbSet.FindAsync(propertyId);
            if (property == null) return 0;

            property.ViewCount++;
            await _context.SaveChangesAsync();

            return property.ViewCount;
        }

        public async Task<Dictionary<PropertyStatus, int>> GetPropertyCountByStatusAsync(Guid agentId)
        {
            return await _dbSet
                .Where(p => p.AgentId == agentId)
                .GroupBy(p => p.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count);
        }

        public async Task<IEnumerable<Property>> GetPropertiesNearLocationAsync(
            decimal latitude,
            decimal longitude,
            double radiusInKm,
            int limit = 50)
        {
            //const double earthRadiusKm = 6371.0;

            var properties = await _dbSet
                .Where(p => p.IsPublished && p.Status == PropertyStatus.Active)
                .ToListAsync();

            var nearby = properties
                .Select(p => new
                {
                    Property = p,
                    Distance = CalculateDistance(latitude, longitude, p.Latitude, p.Longitude)
                })
                .Where(x => x.Distance <= radiusInKm)
                .OrderBy(x => x.Distance)
                .Take(limit)
                .Select(x => x.Property)
                .ToList();

            return nearby;
        }

        public async Task<IEnumerable<Property>> GetPropertiesByIdsAsync(IEnumerable<Guid> propertyIds)
        {
            return await _dbSet
                .Where(p => propertyIds.Contains(p.Id))
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder).Take(1))
                .ToListAsync();
        }

        public async Task<bool> PropertyExistsAsync(Guid propertyId)
        {
            return await _dbSet.AnyAsync(p => p.Id == propertyId);
        }

        public async Task<bool> IsAgentOwnerAsync(Guid propertyId, Guid agentId)
        {
            return await _dbSet.AnyAsync(p => p.Id == propertyId && p.AgentId == agentId);
        }

        public async Task<Dictionary<Guid, int>> GetContactCountsAsync(IEnumerable<Guid> propertyIds)
        {
            var ids = propertyIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<Guid, int>();
            }

            return await _context.Conversations
                .AsNoTracking()
                .Where(c => c.PropertyId != null && ids.Contains(c.PropertyId.Value))
                .GroupBy(c => c.PropertyId!.Value)
                .Select(g => new { PropertyId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PropertyId, x => x.Count);
        }

        private static double CalculateDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
        {
            const double earthRadiusKm = 6371.0;

            var dLat = DegreesToRadians((double)(lat2 - lat1));
            var dLon = DegreesToRadians((double)(lon2 - lon1));

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(DegreesToRadians((double)lat1)) * Math.Cos(DegreesToRadians((double)lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return earthRadiusKm * c;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }

    public class PropertyImageRepository : GenericRepository<PropertyImage>, IPropertyImageRepository
    {
        public PropertyImageRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<PropertyImage>> GetImagesByPropertyIdAsync(Guid propertyId)
        {
            return await _dbSet
                .Where(i => i.PropertyId == propertyId)
                .OrderBy(i => i.DisplayOrder)
                .ToListAsync();
        }

        public async Task<PropertyImage?> GetPrimaryImageAsync(Guid propertyId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(i => i.PropertyId == propertyId && i.IsPrimary);
        }

        public async Task<bool> SetPrimaryImageAsync(Guid propertyId, Guid imageId)
        {
            var images = await _dbSet
                .Where(i => i.PropertyId == propertyId)
                .ToListAsync();

            foreach (var img in images)
            {
                img.IsPrimary = img.Id == imageId;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> DeleteImagesByPropertyIdAsync(Guid propertyId)
        {
            var images = await _dbSet
                .Where(i => i.PropertyId == propertyId)
                .ToListAsync();

            _dbSet.RemoveRange(images);
            await _context.SaveChangesAsync();

            return images.Count;
        }

        public async Task<bool> ReorderImagesAsync(Guid propertyId, Dictionary<Guid, int> imageOrders)
        {
            var images = await _dbSet
                .Where(i => i.PropertyId == propertyId && imageOrders.Keys.Contains(i.Id))
                .ToListAsync();

            foreach (var image in images)
            {
                if (imageOrders.TryGetValue(image.Id, out var order))
                {
                    image.DisplayOrder = order;
                }
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }

    public class PropertyAmenityRepository : GenericRepository<PropertyAmenity>, IPropertyAmenityRepository
    {
        public PropertyAmenityRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<PropertyAmenity>> GetAmenitiesByPropertyIdAsync(Guid propertyId)
        {
            return await _dbSet
                .Where(a => a.PropertyId == propertyId)
                .ToListAsync();
        }

        public async Task<PropertyAmenity?> GetAmenityByCategoryAsync(Guid propertyId, AmenityCategory category)
        {
            return await _dbSet
                .FirstOrDefaultAsync(a => a.PropertyId == propertyId && a.Category == category);
        }

        public async Task<bool> UpsertAmenityAsync(
            Guid propertyId,
            AmenityCategory category,
            Dictionary<string, object> amenities)
        {
            var existing = await GetAmenityByCategoryAsync(propertyId, category);

            if (existing != null)
            {
                existing.Amenities = amenities;
            }
            else
            {
                await _dbSet.AddAsync(new PropertyAmenity
                {
                    Id = Guid.NewGuid(),
                    PropertyId = propertyId,
                    Category = category,
                    Amenities = amenities
                });
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> DeleteAmenitiesByPropertyIdAsync(Guid propertyId)
        {
            var amenities = await _dbSet
                .Where(a => a.PropertyId == propertyId)
                .ToListAsync();

            _dbSet.RemoveRange(amenities);
            await _context.SaveChangesAsync();

            return amenities.Count;
        }
    }

    public class FavoriteRepository : GenericRepository<Favorite>, IFavoriteRepository
    {
        public FavoriteRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<PagedResult<Property>> GetUserFavoritesAsync(Guid userId, int pageNumber, int pageSize)
        {
            var query = _context.Properties
                .Where(p => p.Favorites.Any(f => f.UserId == userId))
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder).Take(1));

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(p => p.Favorites.First(f => f.UserId == userId).CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Property>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<List<Guid>> GetUserFavoritePropertyIdsAsync(Guid userId)
        {
            return await _dbSet
                .Where(f => f.UserId == userId)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => f.PropertyId)
                .ToListAsync();
        }

        public async Task<bool> IsFavoriteAsync(Guid userId, Guid propertyId)
        {
            return await _dbSet.AnyAsync(f => f.UserId == userId && f.PropertyId == propertyId);
        }

        public async Task<Favorite?> GetFavoriteAsync(Guid userId, Guid propertyId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(f => f.UserId == userId && f.PropertyId == propertyId);
        }

        public async Task SetFavoriteAsync(Guid userId, Guid propertyId, bool bookmarked)
        {
            var favorite = await GetFavoriteAsync(userId, propertyId);

            if (bookmarked)
            {
                if (favorite != null) return;

                await _dbSet.AddAsync(new Favorite
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    PropertyId = propertyId,
                    CreatedAt = DateTime.UtcNow
                });

                var property = await _context.Properties.FindAsync(propertyId);
                if (property != null)
                {
                    property.FavoriteCount++;
                }
            }
            else if (favorite != null)
            {
                _dbSet.Remove(favorite);

                var property = await _context.Properties.FindAsync(propertyId);
                if (property != null)
                {
                    property.FavoriteCount = Math.Max(0, property.FavoriteCount - 1);
                }
            }
        }

        public async Task<bool> ToggleFavoriteAsync(Guid userId, Guid propertyId)
        {
            var isFavorite = await IsFavoriteAsync(userId, propertyId);
            await SetFavoriteAsync(userId, propertyId, !isFavorite);
            await _context.SaveChangesAsync();
            return !isFavorite;
        }

        public async Task<int> GetFavoriteCountByPropertyAsync(Guid propertyId)
        {
            return await _dbSet.CountAsync(f => f.PropertyId == propertyId);
        }

        public async Task<Dictionary<Guid, int>> GetFavoriteCountsAsync(IEnumerable<Guid> propertyIds)
        {
            var ids = propertyIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<Guid, int>();
            }

            return await _dbSet
                .AsNoTracking()
                .Where(f => ids.Contains(f.PropertyId))
                .GroupBy(f => f.PropertyId)
                .Select(g => new { PropertyId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PropertyId, x => x.Count);
        }

        public async Task<Dictionary<Guid, bool>> GetFavoriteStatusesAsync(
            Guid userId,
            IEnumerable<Guid> propertyIds)
        {
            var favorites = await _dbSet
                .Where(f => f.UserId == userId && propertyIds.Contains(f.PropertyId))
                .Select(f => f.PropertyId)
                .ToListAsync();

            return propertyIds.ToDictionary(
                id => id,
                id => favorites.Contains(id)
            );
        }
    }

    public class ViewingBookingRepository : GenericRepository<ViewingBooking>, IViewingBookingRepository
    {
        public ViewingBookingRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<PagedResult<ViewingBooking>> GetUserBookingsAsync(
            Guid userId,
            int pageNumber,
            int pageSize,
            BookingStatus? status = null)
        {
            var query = _dbSet
                .Where(b => b.UserId == userId)
                .Include(b => b.Property)
                    .ThenInclude(p => p.Images.OrderBy(i => i.DisplayOrder).Take(1))
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(b => b.Status == status.Value);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(b => b.ScheduledDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<ViewingBooking>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<PagedResult<ViewingBooking>> GetAgentBookingsAsync(
            Guid agentId,
            int pageNumber,
            int pageSize,
            BookingStatus? status = null)
        {
            var query = _dbSet
                .Where(b => b.AgentId == agentId)
                .Include(b => b.Property)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(b => b.Status == status.Value);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(b => b.ScheduledDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<ViewingBooking>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<IEnumerable<ViewingBooking>> GetPropertyBookingsAsync(Guid propertyId)
        {
            return await _dbSet
                .Where(b => b.PropertyId == propertyId)
                .OrderBy(b => b.ScheduledDate)
                .ToListAsync();
        }

        public async Task<bool> UpdateBookingStatusAsync(Guid bookingId, BookingStatus status)
        {
            var booking = await _dbSet.FindAsync(bookingId);
            if (booking == null) return false;

            booking.Status = status;
            booking.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CancelBookingAsync(Guid bookingId)
        {
            return await UpdateBookingStatusAsync(bookingId, BookingStatus.Cancelled);
        }

        public async Task<bool> IsSlotAvailableAsync(Guid propertyId, DateTime scheduledDate)
        {
            var oneHourBefore = scheduledDate.AddHours(-1);
            var oneHourAfter = scheduledDate.AddHours(1);

            return !await _dbSet.AnyAsync(b =>
                b.PropertyId == propertyId &&
                b.Status == BookingStatus.Confirmed &&
                b.ScheduledDate >= oneHourBefore &&
                b.ScheduledDate <= oneHourAfter);
        }

        public async Task<IEnumerable<DateTime>> GetBookedSlotsAsync(
            Guid propertyId,
            DateTime startDate,
            DateTime endDate)
        {
            return await _dbSet
                .Where(b =>
                    b.PropertyId == propertyId &&
                    b.Status == BookingStatus.Confirmed &&
                    b.ScheduledDate >= startDate &&
                    b.ScheduledDate <= endDate)
                .Select(b => b.ScheduledDate)
                .ToListAsync();
        }
    }
}