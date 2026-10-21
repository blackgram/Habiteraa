using Habitera.DTOs;
using Habitera.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Habitera.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IAdminService _adminService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(IAdminService adminService, ILogger<AdminController> logger)
        {
            _adminService = adminService;
            _logger = logger;
        }

        /// <summary>
        /// Listings waiting for admin review.
        /// </summary>
        [HttpGet("Listings/Pending")]
        public async Task<IActionResult> GetPendingListings([FromQuery] PaginatedRequest request)
        {
            var result = await _adminService.GetPendingListingsAsync(request);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Approve a pending listing and publish it immediately.
        /// </summary>
        [HttpPost("Listings/{propertyId:guid}/Approve")]
        public async Task<IActionResult> ApproveListing(Guid propertyId)
        {
            var adminId = GetAdminId();
            if (adminId == null)
            {
                return Unauthorized(new { message = "Invalid admin user" });
            }

            var result = await _adminService.ApproveListingAsync(adminId.Value, propertyId);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Reject a pending listing (moves to Inactive).
        /// </summary>
        [HttpPost("Listings/{propertyId:guid}/Reject")]
        public async Task<IActionResult> RejectListing(
            Guid propertyId,
            [FromBody] RejectListingRequestDTO? request)
        {
            var adminId = GetAdminId();
            if (adminId == null)
            {
                return Unauthorized(new { message = "Invalid admin user" });
            }

            var result = await _adminService.RejectListingAsync(
                adminId.Value,
                propertyId,
                request?.Reason);

            return StatusCode(result.StatusCode, result);
        }

        private Guid? GetAdminId()
        {
            var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }
}
