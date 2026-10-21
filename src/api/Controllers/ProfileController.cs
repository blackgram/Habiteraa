using Habitera.DTOs;
using Habitera.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Habitera.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly IProfileService _profileService;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(
            IProfileService profileService,
            ILogger<ProfileController> logger)
        {
            _profileService = profileService;
            _logger = logger;
        }

        [HttpGet("Status")]
        public async Task<IActionResult> GetProfileCompletionStatus()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _profileService.GetProfileCompletionStatusAsync(userId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("Complete/User")]
        public async Task<IActionResult> CompleteUserProfile([FromBody] CompleteRegularUserProfileDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            dto.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            dto.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            dto.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _profileService.CompleteRegularUserProfileAsync(userId, dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("Complete/Agent")]
        public async Task<IActionResult> CompleteAgentProfile([FromBody] CompleteAgentProfileDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            dto.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            dto.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            dto.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _profileService.CompleteAgentProfileAsync(userId, dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("Update/User")]
        public async Task<IActionResult> UpdateUserProfile([FromBody] CompleteRegularUserProfileDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _profileService.UpdateRegularUserProfileAsync(userId, dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("Update/Agent")]
        public async Task<IActionResult> UpdateAgentProfile([FromBody] CompleteAgentProfileDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _profileService.UpdateAgentProfileAsync(userId, dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Me")]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _profileService.GetUserProfileAsync(userId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("Photo")]
        public async Task<IActionResult> UpdateProfilePhoto([FromBody] UpdateProfilePhotoDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await _profileService.UpdateProfilePhotoAsync(userId, dto.ProfilePhoto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("Photo")]
        [RequestSizeLimit(5_000_000)]
        public async Task<IActionResult> UploadProfilePhoto(IFormFile file)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var publicBaseUrl = $"{Request.Scheme}://{Request.Host}";
            var result = await _profileService.UploadProfilePhotoAsync(userId, file, publicBaseUrl);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Agents/{agentId:guid}")]
        public async Task<IActionResult> GetPublicAgentProfile(Guid agentId)
        {
            var result = await _profileService.GetPublicAgentProfileAsync(agentId);
            return StatusCode(result.StatusCode, result);
        }
    }
}