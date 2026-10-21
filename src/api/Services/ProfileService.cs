using AutoMapper;
using Habitera.DTOs;
using Habitera.Models;
using Habitera.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Habitera.Services
{
    public interface IProfileService
    {
        Task<OperationResult<ProfileCompletionStatusDTO>> GetProfileCompletionStatusAsync(Guid userId);
        Task<OperationResult<AuthResponseDTO>> CompleteRegularUserProfileAsync(Guid userId, CompleteRegularUserProfileDTO dto);
        Task<OperationResult<AuthResponseDTO>> CompleteAgentProfileAsync(Guid userId, CompleteAgentProfileDTO dto);
        Task<OperationResult<ApplicationUserDTO>> UpdateRegularUserProfileAsync(Guid userId, CompleteRegularUserProfileDTO dto);
        Task<OperationResult<ApplicationUserDTO>> UpdateAgentProfileAsync(Guid userId, CompleteAgentProfileDTO dto);
        Task<OperationResult<ApplicationUserDTO>> GetUserProfileAsync(Guid userId);
        Task<OperationResult<ApplicationUserDTO>> UpdateProfilePhotoAsync(Guid userId, string profilePhoto);
        Task<OperationResult<ApplicationUserDTO>> UploadProfilePhotoAsync(Guid userId, IFormFile file, string? publicBaseUrl);
        Task<OperationResult<AgentPublicProfileDTO>> GetPublicAgentProfileAsync(Guid agentId);
    }

    public class ProfileService : IProfileService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ITokenService _tokenService;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ProfileService> _logger;

        private static readonly HashSet<string> AllowedPresetAvatars = new(StringComparer.OrdinalIgnoreCase)
        {
            "preset:avatar-1",
            "preset:avatar-2",
            "preset:avatar-3",
            "preset:avatar-4",
            "preset:avatar-5",
            "preset:avatar-6",
        };

        public ProfileService(
            UserManager<ApplicationUser> userManager,
            IUnitOfWork unitOfWork,
            IMapper mapper,
            ITokenService tokenService,
            ICloudinaryService cloudinaryService,
            IWebHostEnvironment environment,
            ILogger<ProfileService> logger)
        {
            _userManager = userManager;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _tokenService = tokenService;
            _cloudinaryService = cloudinaryService;
            _environment = environment;
            _logger = logger;
        }

        public async Task<OperationResult<ProfileCompletionStatusDTO>> GetProfileCompletionStatusAsync(Guid userId)
        {
            try
            {
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return OperationResult<ProfileCompletionStatusDTO>.Failure("User not found", 404);

                var (missing, percentage) = ProfileCompletionHelper.Evaluate(user);
                var status = new ProfileCompletionStatusDTO
                {
                    IsProfileCompleted = user.ProfileCompleted,
                    UserType = user.UserType,
                    MissingFields = missing,
                    CompletionPercentage = percentage
                };

                return OperationResult<ProfileCompletionStatusDTO>.Successful(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting profile completion status for user {UserId}", userId);
                return OperationResult<ProfileCompletionStatusDTO>.Failure(
                    "An error occurred while checking profile status", 500);
            }
        }

        public async Task<OperationResult<AuthResponseDTO>> CompleteRegularUserProfileAsync(
            Guid userId,
            CompleteRegularUserProfileDTO dto)
        {
            try
            {
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return OperationResult<AuthResponseDTO>.Failure("User not found", 404);

                if (user.UserType != UserType.User && user.UserType != UserType.Admin)
                    return OperationResult<AuthResponseDTO>.Failure("Invalid user type", 400);

                if (user.ProfileCompleted)
                    return OperationResult<AuthResponseDTO>.Failure("Profile already completed", 400);

                // Update profile
                user.Profile.FirstName = dto.FirstName.Trim();
                user.Profile.LastName = dto.LastName.Trim();
                user.Profile.City = dto.City.Trim();
                user.Profile.State = dto.State.Trim();
                user.Profile.Country = dto.Country.Trim();
                user.Profile.Location = new Point(dto.Longitude, dto.Latitude) { SRID = 4326 };
                user.Profile.PreferredLanguage = dto.PreferredLanguage;

                if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                {
                    user.PhoneNumber = dto.PhoneNumber.Trim();
                }

                user.ProfileCompleted = true;
                user.ProfileCompletedAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;

                await _userManager.UpdateAsync(user);
                await _unitOfWork.SaveChangesAsync();

                // Generate new tokens with updated profile
                var roles = await _userManager.GetRolesAsync(user);
                var accessToken = _tokenService.GenerateAccessToken(user, roles);
                var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
                    user.Id, dto.DeviceInfo, dto.IpAddress);

                var userDto = _mapper.Map<RegularUserDTO>(user);

                var authResponse = new AuthResponseDTO
                {
                    Token = accessToken,
                    RefreshToken = refreshToken,
                    User = userDto
                };

                _logger.LogInformation("Profile completed for user {UserId}", userId);

                return OperationResult<AuthResponseDTO>.Successful(
                    authResponse,
                    "Profile completed successfully",
                    200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing profile for user {UserId}", userId);
                return OperationResult<AuthResponseDTO>.Failure(
                    "An error occurred while completing profile", 500);
            }
        }

        public async Task<OperationResult<AuthResponseDTO>> CompleteAgentProfileAsync(
            Guid userId,
            CompleteAgentProfileDTO dto)
        {
            try
            {
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return OperationResult<AuthResponseDTO>.Failure("User not found", 404);

                if (user.UserType != UserType.Agent)
                    return OperationResult<AuthResponseDTO>.Failure("Invalid user type", 400);

                if (user.ProfileCompleted)
                    return OperationResult<AuthResponseDTO>.Failure("Profile already completed", 400);

                // Update basic profile
                user.Profile.FirstName = dto.FirstName.Trim();
                user.Profile.LastName = dto.LastName.Trim();
                user.Profile.City = dto.City.Trim();
                user.Profile.State = dto.State.Trim();
                user.Profile.Country = dto.Country.Trim();
                user.Profile.Location = new Point(0, 0) { SRID = 4326 }; // Agents don't need precise location

                if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                {
                    user.PhoneNumber = dto.PhoneNumber.Trim();
                }

                // Update agent-specific profile
                if (user.AgentProfile != null)
                {
                    user.AgentProfile.LicenseNumber = dto.LicenseNumber?.Trim();
                    user.AgentProfile.AgencyName = dto.AgencyName?.Trim();
                }

                user.ProfileCompleted = true;
                user.ProfileCompletedAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;

                await _userManager.UpdateAsync(user);
                await _unitOfWork.SaveChangesAsync();

                // Generate new tokens
                var roles = await _userManager.GetRolesAsync(user);
                var accessToken = _tokenService.GenerateAccessToken(user, roles);
                var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
                    user.Id, dto.DeviceInfo, dto.IpAddress);

                var userDto = _mapper.Map<AgentUserDTO>(user);

                var authResponse = new AuthResponseDTO
                {
                    Token = accessToken,
                    RefreshToken = refreshToken,
                    User = userDto
                };

                _logger.LogInformation("Agent profile completed for user {UserId}", userId);

                return OperationResult<AuthResponseDTO>.Successful(
                    authResponse,
                    "Profile completed successfully",
                    200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing agent profile for user {UserId}", userId);
                return OperationResult<AuthResponseDTO>.Failure(
                    "An error occurred while completing profile", 500);
            }
        }

        public async Task<OperationResult<ApplicationUserDTO>> UpdateRegularUserProfileAsync(
            Guid userId,
            CompleteRegularUserProfileDTO dto)
        {
            try
            {
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return OperationResult<ApplicationUserDTO>.Failure("User not found", 404);

                // Update profile
                user.Profile.FirstName = dto.FirstName.Trim();
                user.Profile.LastName = dto.LastName.Trim();
                user.Profile.City = dto.City.Trim();
                user.Profile.State = dto.State.Trim();
                user.Profile.Country = dto.Country.Trim();
                user.Profile.Location = new Point(dto.Longitude, dto.Latitude) { SRID = 4326 };
                user.Profile.PreferredLanguage = dto.PreferredLanguage;

                if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                {
                    user.PhoneNumber = dto.PhoneNumber.Trim();
                }

                user.UpdatedAt = DateTime.UtcNow;
                ProfileCompletionHelper.TryMarkCompleted(user);

                await _userManager.UpdateAsync(user);
                await _unitOfWork.SaveChangesAsync();

                var userDto = _mapper.Map<RegularUserDTO>(user);

                return OperationResult<ApplicationUserDTO>.Successful(
                    userDto,
                    "Profile updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile for user {UserId}", userId);
                return OperationResult<ApplicationUserDTO>.Failure(
                    "An error occurred while updating profile", 500);
            }
        }

        public async Task<OperationResult<ApplicationUserDTO>> UpdateAgentProfileAsync(
            Guid userId,
            CompleteAgentProfileDTO dto)
        {
            try
            {
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return OperationResult<ApplicationUserDTO>.Failure("User not found", 404);

                // Update profiles
                user.Profile.FirstName = dto.FirstName.Trim();
                user.Profile.LastName = dto.LastName.Trim();
                user.Profile.City = dto.City.Trim();
                user.Profile.State = dto.State.Trim();
                user.Profile.Country = dto.Country.Trim();

                if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                {
                    user.PhoneNumber = dto.PhoneNumber.Trim();
                }

                if (user.AgentProfile != null)
                {
                    user.AgentProfile.LicenseNumber = dto.LicenseNumber?.Trim();
                    user.AgentProfile.AgencyName = dto.AgencyName?.Trim();
                }

                user.UpdatedAt = DateTime.UtcNow;
                ProfileCompletionHelper.TryMarkCompleted(user);

                await _userManager.UpdateAsync(user);
                await _unitOfWork.SaveChangesAsync();

                var userDto = _mapper.Map<AgentUserDTO>(user);

                return OperationResult<ApplicationUserDTO>.Successful(
                    userDto,
                    "Profile updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating agent profile for user {UserId}", userId);
                return OperationResult<ApplicationUserDTO>.Failure(
                    "An error occurred while updating profile", 500);
            }
        }

        public async Task<OperationResult<ApplicationUserDTO>> GetUserProfileAsync(Guid userId)
        {
            try
            {
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return OperationResult<ApplicationUserDTO>.Failure("User not found", 404);

                ApplicationUserDTO userDto = user.UserType == UserType.Agent
                    ? _mapper.Map<AgentUserDTO>(user)
                    : _mapper.Map<RegularUserDTO>(user);

                return OperationResult<ApplicationUserDTO>.Successful(userDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting profile for user {UserId}", userId);
                return OperationResult<ApplicationUserDTO>.Failure(
                    "An error occurred while retrieving profile", 500);
            }
        }

        public async Task<OperationResult<ApplicationUserDTO>> UpdateProfilePhotoAsync(
            Guid userId,
            string profilePhoto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(profilePhoto))
                    return OperationResult<ApplicationUserDTO>.Failure("Profile photo is required", 400);

                var value = profilePhoto.Trim();
                var isPreset = AllowedPresetAvatars.Contains(value);
                var isHttpUrl = value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                var isLocalUpload = value.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase);

                if (!isPreset && !isHttpUrl && !isLocalUpload)
                {
                    return OperationResult<ApplicationUserDTO>.Failure(
                        "Invalid profile photo value", 400);
                }

                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return OperationResult<ApplicationUserDTO>.Failure("User not found", 404);

                user.ProfilePhoto = value;
                user.UpdatedAt = DateTime.UtcNow;

                await _userManager.UpdateAsync(user);
                await _unitOfWork.SaveChangesAsync();

                ApplicationUserDTO userDto = user.UserType == UserType.Agent
                    ? _mapper.Map<AgentUserDTO>(user)
                    : _mapper.Map<RegularUserDTO>(user);

                return OperationResult<ApplicationUserDTO>.Successful(
                    userDto,
                    "Profile photo updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile photo for user {UserId}", userId);
                return OperationResult<ApplicationUserDTO>.Failure(
                    "An error occurred while updating profile photo", 500);
            }
        }

        public async Task<OperationResult<ApplicationUserDTO>> UploadProfilePhotoAsync(
            Guid userId,
            IFormFile file,
            string? publicBaseUrl)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return OperationResult<ApplicationUserDTO>.Failure("Image file is required", 400);

                string photoValue;

                var cloudinaryResult = await _cloudinaryService.UploadAvatarAsync(file, "profiles");
                if (cloudinaryResult.Success && cloudinaryResult.Data != null)
                {
                    photoValue = cloudinaryResult.Data.Url;
                }
                else
                {
                    // Local/dev fallback when Cloudinary is unavailable
                    var uploadsRoot = Path.Combine(_environment.ContentRootPath, "wwwroot", "uploads", "profiles");
                    Directory.CreateDirectory(uploadsRoot);

                    var extension = Path.GetExtension(file.FileName);
                    if (string.IsNullOrWhiteSpace(extension))
                        extension = ".jpg";

                    var fileName = $"{userId:N}{extension.ToLowerInvariant()}";
                    var filePath = Path.Combine(uploadsRoot, fileName);

                    await using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var relativePath = $"/uploads/profiles/{fileName}";
                    photoValue = string.IsNullOrWhiteSpace(publicBaseUrl)
                        ? relativePath
                        : $"{publicBaseUrl.TrimEnd('/')}{relativePath}";

                    _logger.LogWarning(
                        "Cloudinary avatar upload failed ({Message}); saved local profile photo for {UserId}",
                        cloudinaryResult.Message,
                        userId);
                }

                return await UpdateProfilePhotoAsync(userId, photoValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading profile photo for user {UserId}", userId);
                return OperationResult<ApplicationUserDTO>.Failure(
                    "An error occurred while uploading profile photo", 500);
            }
        }

        public async Task<OperationResult<AgentPublicProfileDTO>> GetPublicAgentProfileAsync(Guid agentId)
        {
            try
            {
                var agent = await _userManager.Users
                    .AsNoTracking()
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)
                    .FirstOrDefaultAsync(u => u.Id == agentId && u.DeletedAt == null);

                if (agent == null || agent.UserType != UserType.Agent)
                {
                    return OperationResult<AgentPublicProfileDTO>.Failure("Agent not found", 404);
                }

                var properties = await _unitOfWork.Properties.GetPropertiesByAgentAsync(agentId);
                var published = properties
                    .Where(p => p.IsPublished && p.Status == PropertyStatus.Active)
                    .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
                    .ToList();

                var profile = agent.Profile;
                var agentProfile = agent.AgentProfile;
                var fullName = profile == null
                    ? "Agent"
                    : $"{profile.FirstName} {profile.LastName}".Trim();

                var dto = new AgentPublicProfileDTO
                {
                    Id = agent.Id,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? "Agent" : fullName,
                    ProfilePhoto = agent.ProfilePhoto,
                    AgencyName = agentProfile?.AgencyName,
                    LicenseNumber = agentProfile?.LicenseNumber,
                    AverageRating = agentProfile?.AverageRating ?? 0,
                    TotalReviews = agentProfile?.TotalReviews ?? 0,
                    City = profile?.City,
                    State = profile?.State,
                    Country = profile?.Country,
                    MemberSince = agent.CreatedAt,
                    ListingCount = published.Count,
                    Listings = _mapper.Map<List<PropertyDTO>>(published)
                };

                return OperationResult<AgentPublicProfileDTO>.Successful(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting public profile for agent {AgentId}", agentId);
                return OperationResult<AgentPublicProfileDTO>.Failure(
                    "An error occurred while retrieving the agent profile", 500);
            }
        }
    }
}