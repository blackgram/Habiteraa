using Habitera.DTOs;
using Habitera.Models;
using Habitera.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;

namespace Habitera.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PropertyController : ControllerBase
    {
        private readonly IPropertyService _propertyService;
        private readonly IRecommendationService _recommendationService;
        private readonly ILocationSuggestionService _locationSuggestionService;
        private readonly ILogger<PropertyController> _logger;

        public PropertyController(
            IPropertyService propertyService,
            IRecommendationService recommendationService,
            ILocationSuggestionService locationSuggestionService,
            ILogger<PropertyController> logger)
        {
            _propertyService = propertyService;
            _recommendationService = recommendationService;
            _locationSuggestionService = locationSuggestionService;
            _logger = logger;
        }

        [HttpPost("Search")]
        public async Task<IActionResult> SearchProperties([FromBody] PropertySearchRequest request)
        {
            var userId = User.Identity?.IsAuthenticated == true
                ? Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value)
                : (Guid?)null;

            var result = await _propertyService.SearchPropertiesAsync(request);

            // Track search if user is logged in
            if (userId.HasValue && result.Properties.Any())
            {
                _ = Task.Run(async () =>
                {
                    foreach (var property in result.Properties.Take(10))
                    {
                        await _recommendationService.TrackUserInteractionAsync(
                            userId.Value, property.Id, "search");
                    }
                });
            }

            return Ok(result);
        }

        [HttpGet("Published")]
        public async Task<IActionResult> GetPublishedProperties([FromQuery] PaginatedRequest request)
        {
            var result = await _propertyService.GetPublishedPropertiesAsync(request);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Nearby")]
        public async Task<IActionResult> GetNearbyProperties(
            [FromQuery] decimal latitude,
            [FromQuery] decimal longitude,
            [FromQuery] double radiusKm = 5)
        {
            var result = await _propertyService.GetNearbyPropertiesAsync(latitude, longitude, radiusKm);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("{propertyId}/Similar")]
        public async Task<IActionResult> GetSimilarProperties(
            Guid propertyId,
            [FromQuery] int limit = 10)
        {
            var properties = await _propertyService.GetSimilarPropertiesAsync(propertyId, limit);
            return Ok(new { properties });
        }

        [HttpGet("Trending")]
        public async Task<IActionResult> GetTrendingProperties([FromQuery] int limit = 10)
        {
            var properties = await _propertyService.GetTrendingPropertiesAsync(limit);
            return Ok(new { properties });
        }

        [HttpGet("Recommendations")]
        [Authorize]
        public async Task<IActionResult> GetRecommendations([FromQuery] int limit = 10)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var properties = await _recommendationService.GetPersonalizedRecommendationsAsync(userId, limit);
            return Ok(new { properties });
        }

        //[HttpGet("Autocomplete")]
        //public async Task<IActionResult> Autocomplete([FromQuery] string query, [FromQuery] int limit = 10)
        //{
        //    var suggestions = await _elasticsearchService.AutocompleteAsync(query, limit);
        //    return Ok(new { suggestions });
        //}

        [HttpGet("Locations/Suggest")]
        public async Task<IActionResult> GetLocationSuggestions(
            [FromQuery] string query,
            [FromQuery] int limit = 10)
        {
            var result = await _locationSuggestionService.SuggestAsync(query, limit);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("ForLocation")]
        public async Task<IActionResult> GetListingsForLocation(
            [FromQuery] double latitude,
            [FromQuery] double longitude,
            [FromQuery] string? label = null,
            [FromQuery] string? city = null,
            [FromQuery] string? state = null,
            [FromQuery] string? country = null,
            [FromQuery] double localRadiusKm = 10,
            [FromQuery] int nearbyLimit = 20)
        {
            var result = await _propertyService.GetListingsForLocationAsync(
                latitude,
                longitude,
                label,
                city,
                state,
                country,
                localRadiusKm,
                nearbyLimit);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> CreateProperty([FromBody] CreatePropertyDTO dto)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.CreatePropertyAsync(agentId, dto);

            if (result.Success)
                return StatusCode(result.StatusCode, result);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("{propertyId}")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> UpdateProperty(Guid propertyId, [FromBody] UpdatePropertyDTO dto)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.UpdatePropertyAsync(agentId, propertyId, dto);

            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("{propertyId}")]
        public async Task<IActionResult> GetProperty(Guid propertyId)
        {
            var userId = User.Identity?.IsAuthenticated == true
                ? Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value)
                : (Guid?)null;

            var result = await _propertyService.GetPropertyByIdAsync(propertyId, userId);

            // Increment view count asynchronously
            if (result.Success)
            {
                _ = _propertyService.IncrementViewCountAsync(propertyId);
            }

            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Agent/Properties")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> GetAgentProperties(
            [FromQuery] PaginatedRequest request,
            [FromQuery] PropertyStatus? status = null)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.GetAgentPropertiesAsync(agentId, request, status);
            return StatusCode(result.StatusCode, result);
        }

        [HttpDelete("{propertyId}")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> DeleteProperty(Guid propertyId)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.DeletePropertyAsync(agentId, propertyId);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Submit a listing for admin review. Listings are not live until approved
        /// (or auto-approved after the configured timeout).
        /// </summary>
        [HttpPost("{propertyId}/SubmitForReview")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> SubmitForReview(Guid propertyId)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.SubmitForReviewAsync(agentId, propertyId);
            return StatusCode(result.StatusCode, result);
        }

        [Obsolete("Use SubmitForReview. Agent publish no longer goes live immediately.")]
        [HttpPost("{propertyId}/Publish")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> PublishProperty(Guid propertyId)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.SubmitForReviewAsync(agentId, propertyId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{propertyId}/Unpublish")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> UnpublishProperty(Guid propertyId)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.UnpublishPropertyAsync(agentId, propertyId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPatch("{propertyId}/Status")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> UpdatePropertyStatus(
            Guid propertyId,
            [FromBody] PropertyStatus status)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.UpdatePropertyStatusAsync(agentId, propertyId, status);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("{propertyId}/Images/{imageId}/Primary")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> SetPrimaryImage(Guid propertyId, Guid imageId)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.SetPrimaryImageAsync(agentId, propertyId, imageId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("{propertyId}/Amenities")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> UpsertPropertyAmenity(
            Guid propertyId,
            [FromBody] PropertyAmenityDTO dto)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.UpsertPropertyAmenityAsync(
                agentId, propertyId, dto.Category, dto.Amenities);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("{propertyId}/Analytics")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> GetPropertyAnalytics(Guid propertyId)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.GetPropertyAnalyticsAsync(agentId, propertyId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Agent/Dashboard")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> GetAgentDashboard()
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.GetAgentDashboardAsync(agentId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{propertyId}/Images")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> UploadPropertyImage(
            Guid propertyId,
            IFormFile image)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.AddPropertyImageAsync(agentId, propertyId, image);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{propertyId}/Images/Multiple")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> UploadMultiplePropertyImages(
            Guid propertyId,
            [FromForm] IFormFileCollection images)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.AddMultiplePropertyImagesAsync(agentId, propertyId, images);

            return StatusCode(result.StatusCode, result);
        }

        [HttpDelete("{propertyId}/Images/{imageId}")]
        [Authorize(Roles = "Agent")]
        public async Task<IActionResult> DeletePropertyImage(Guid propertyId, Guid imageId)
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.DeletePropertyImageAsync(agentId, propertyId, imageId);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("SeedTestData")]
        [Authorize(Roles = "Agent,Admin")]
        public async Task<IActionResult> SeedTestData()
        {
            var agentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _propertyService.SeedDemoListingsAsync(agentId);
            return StatusCode(result.StatusCode, result);
        }

        
        [HttpGet("TestCachePerformance")]
        public async Task<IActionResult> TestCachePerformance([FromQuery] string query)
        {
            var sw = Stopwatch.StartNew();

            // First call (no cache)
            var request = new PropertySearchRequest { Query = query, PageSize = 20 };
            var result1 = await _propertyService.SearchPropertiesAsync(request);
            var firstCallTime = sw.ElapsedMilliseconds;

            sw.Restart();

            // Second call (should be cached)
            var result2 = await _propertyService.SearchPropertiesAsync(request);
            var cachedCallTime = sw.ElapsedMilliseconds;

            return Ok(new
            {
                firstCall = $"{firstCallTime}ms",
                cachedCall = $"{cachedCallTime}ms",
                improvement = $"{((firstCallTime - cachedCallTime) / (double)firstCallTime * 100):F1}%",
                resultCount = result1.Properties.Count
            });
        }

    }
}