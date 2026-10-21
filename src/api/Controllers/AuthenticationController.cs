using Habitera.DTOs;
using Habitera.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace Habitera.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IAuthenticationService _authenticationService;
        private readonly ILogger<AuthenticationController> _logger;

        public AuthenticationController(
            IConfiguration configuration,
            IAuthenticationService authenticationService, 
            ILogger<AuthenticationController> logger)
        {
            _configuration = configuration;
            _authenticationService = authenticationService;
            _logger = logger;
        }

        [HttpPost("MockLogin")]
        public async Task<IActionResult> MockLogin([FromQuery] MockLoginRequestDTO dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userTypeKey = dto.UserType.ToString();

            var mockUserSection = _configuration.GetSection($"MockUsers:{userTypeKey}");

            if (!mockUserSection.Exists())
            {
                return BadRequest($"Mock user configuration not found for user type: {dto.UserType}");
            }

            var email = mockUserSection["Email"]
                ?? throw new InvalidOperationException($"Mock {dto.UserType} Email is not configured");

            var password = mockUserSection["Password"]
                ?? throw new InvalidOperationException($"Mock {dto.UserType} Password is not configured");

            var request = new LoginRequestDTO
            {
                Email = email,
                Password = password,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString(),
            };

            var result = await _authenticationService.LoginAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }

            return Unauthorized(result);
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            request.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _authenticationService.LoginAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }

            // Preserve 403 for unverified accounts so the app can route to OTP.
            return StatusCode(result.StatusCode, result);

        }

        [HttpPost("RefreshToken")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _authenticationService.RefreshTokenAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }

            return Unauthorized(result);
        }

        [HttpPost("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            request.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _authenticationService.RegisterAsync(request);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("ResendVerification")]
        public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendCodeRequestDTO request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            request.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _authenticationService.ResendVerificationCodeAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("VerifyEmail")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequestDTO request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            request.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _authenticationService.VerifyEmailAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("ChangePassword")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDTO request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
            {
                return Unauthorized(new { message = "Invalid user" });
            }

            request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            request.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _authenticationService.ChangePasswordAsync(userGuid, request);

            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            request.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _authenticationService.ForgotPasswordAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ResetPasswordDTO request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            request.DeviceInfo = HttpContext.Request.Headers["User-Agent"].ToString();

            var result = await _authenticationService.ResetPasswordAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpDelete("DeleteAccount")]
        [Authorize]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequestDTO request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
            {
                return Unauthorized(new { message = "Invalid user" });
            }

            request.DeviceInfo = Request.Headers["User-Agent"].ToString();
            request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = Request.Headers["User-Agent"].ToString();

            var result = await _authenticationService.DeleteAccountAsync(userGuid, request);

            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

    }
}