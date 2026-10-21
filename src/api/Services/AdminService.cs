using Habitera.DTOs;

namespace Habitera.Services
{
    public interface IAdminService
    {
        Task<PaginatedOperationResult<PropertyDTO>> GetPendingListingsAsync(PaginatedRequest request);
        Task<OperationResult<string>> ApproveListingAsync(Guid adminId, Guid propertyId);
        Task<OperationResult<string>> RejectListingAsync(Guid adminId, Guid propertyId, string? reason);
    }

    public class AdminService : IAdminService
    {
        private readonly IPropertyService _propertyService;
        private readonly ILogger<AdminService> _logger;

        public AdminService(IPropertyService propertyService, ILogger<AdminService> logger)
        {
            _propertyService = propertyService;
            _logger = logger;
        }

        public Task<PaginatedOperationResult<PropertyDTO>> GetPendingListingsAsync(PaginatedRequest request)
            => _propertyService.GetPendingReviewListingsAsync(request);

        public async Task<OperationResult<string>> ApproveListingAsync(Guid adminId, Guid propertyId)
        {
            _logger.LogInformation("Admin {AdminId} approving property {PropertyId}", adminId, propertyId);
            return await _propertyService.ApproveListingAsync(propertyId, adminId, autoApproved: false);
        }

        public async Task<OperationResult<string>> RejectListingAsync(
            Guid adminId,
            Guid propertyId,
            string? reason)
        {
            _logger.LogInformation("Admin {AdminId} rejecting property {PropertyId}", adminId, propertyId);
            return await _propertyService.RejectListingAsync(propertyId, adminId, reason);
        }
    }
}
