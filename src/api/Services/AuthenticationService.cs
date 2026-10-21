using Habitera.Data;
using Habitera.DTOs;
using Habitera.Models;
using Habitera.Repositories;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Habitera.Services
{
    public interface IAuthenticationService
    {
        Task<OperationResult<AuthResponseDTO>> RegisterAsync(RegisterRequestDTO request);
        Task<OperationResult<AuthResponseDTO>> LoginAsync(LoginRequestDTO request);
        Task<OperationResult<AuthResponseDTO>> RefreshTokenAsync(RefreshTokenRequest request);
        Task<OperationResult<AuthResponseDTO>> VerifyEmailAsync(VerifyEmailRequestDTO request);
        Task<OperationResult<AuthResponseDTO>> ResendVerificationCodeAsync(ResendCodeRequestDTO request);
        Task<OperationResult<string>> ChangePasswordAsync(Guid userId, ChangePasswordRequestDTO request);
        Task<OperationResult<string>> ForgotPasswordAsync(ForgotPasswordDTO request);
        Task<OperationResult<string>> ResetPasswordAsync(ResetPasswordDTO request);
        Task<OperationResult<string>> DeleteAccountAsync(Guid userId, DeleteAccountRequestDTO request);
    }

    public class AuthenticationService : IAuthenticationService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ITokenService _tokenService;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthenticationService> _logger;
        private readonly ApplicationDbContext _context;
        private readonly VerificationOptions _options;

        public AuthenticationService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IUnitOfWork unitOfWork,
            IMapper mapper,
            ITokenService tokenService,
            IEmailService emailService,
            ILogger<AuthenticationService> logger,
            ApplicationDbContext context,
            IOptions<VerificationOptions> options)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _tokenService = tokenService;
            _emailService = emailService;
            _logger = logger;
            _context = context;
            _options = options.Value;
        }

        public async Task<OperationResult<AuthResponseDTO>> RegisterAsync(RegisterRequestDTO request)
        {
            var correlationId = Guid.NewGuid().ToString();
            var overallTimer = Stopwatch.StartNew();

            try
            {
                var validationResult = ValidateRegistrationInput(request);
                if (!validationResult.IsValid)
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                       validationResult.ErrorMessage,
                       400
                   );
                }

                var normalizedEmail = NormalizeEmail(request.Email);

                var strategy = _context.Database.CreateExecutionStrategy();

                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        var existingUser = await _userManager.FindByEmailAsync(request.Email);
                        if (existingUser != null)
                        {
                            if (!existingUser.EmailConfirmed &&
                                (existingUser.Status == UserStatus.New || existingUser.Status == UserStatus.Pending))
                            {
                                await transaction.RollbackAsync();
                                return OperationResult<AuthResponseDTO>.Failure(
                                    "An account with this email exists but is not verified. Please check your email for the verification code or request a new one.",
                                    400
                                );
                            }

                            _logger.LogInformation(
                                "Registration attempted for existing verified user. CorrelationId: {CorrelationId}, ExistingUserType: {ExistingUserType}, RequestedUserType: {RequestedUserType}",
                                correlationId,
                                existingUser.UserType,
                                request.UserType);

                            await transaction.RollbackAsync();

                            var existingTypeLabel = existingUser.UserType == UserType.Agent
                                ? "an agent"
                                : "a user";

                            var message = existingUser.UserType != request.UserType
                                ? $"This email is already registered as {existingTypeLabel} account. Sign in with that account type, or use a different email."
                                : "An account with this email already exists. Please sign in instead.";

                            return OperationResult<AuthResponseDTO>.Failure(message, 409);
                        }

                        var userProfile = new UserProfile
                        {
                            UserId = Guid.NewGuid(),
                            FirstName = request.FirstName?.Trim() ?? "",
                            LastName = request.LastName?.Trim() ?? "",
                            City = "",
                            State = "",
                            Country = "",
                            Location = new Point(0, 0) { SRID = 4326 },
                            PreferredLanguage = "en",
                            EmailNotifications = true,
                            PushNotifications = true,
                            User = null!
                        };

                        var user = new ApplicationUser
                        {
                            Id = userProfile.UserId,
                            UserName = request.Email,
                            Email = request.Email,
                            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                                ? null
                                : request.PhoneNumber.Trim(),
                            EmailConfirmed = false,
                            UserType = request.UserType,
                            Status = UserStatus.New,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            Profile = userProfile
                        };

                        userProfile.User = user;

                        var result = await _userManager.CreateAsync(user, request.Password);
                        if (!result.Succeeded)
                        {
                            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                            return OperationResult<AuthResponseDTO>.Failure(
                                $"Failed to create user: {errors}",
                                400,
                                result.Errors
                            );
                        }

                        string roleName = request.UserType switch
                        {
                            UserType.Admin => "Admin",
                            UserType.Agent => "Agent",
                            _ => "User"
                        };

                        await _userManager.AddToRoleAsync(user, roleName);

                        if (request.UserType == UserType.Agent)
                        {
                            var agentProfile = new AgentProfile
                            {
                                UserId = user.Id,
                                User = user,
                                LicenseNumber = null,
                                AgencyName = null,
                                AverageRating = 0,
                                TotalReviews = 0
                            };

                            await _unitOfWork.AgentProfiles.AddAsync(agentProfile);
                            await _unitOfWork.SaveChangesAsync();
                        }

                        var verificationCode = await GenerateSecureVerificationCode(
                              user.Id,
                              user.Email,
                              correlationId);

                        if (verificationCode == null || string.IsNullOrWhiteSpace(verificationCode.PlainCode))
                        {
                            // TODO: RESEND EMAIL IF USER NOT VERIFIED or return the error below if verified
                            await transaction.RollbackAsync();
                            return OperationResult<AuthResponseDTO>.Failure(
                                "Failed to generate email verification code",
                                400
                            );
                        }


                        await transaction.CommitAsync();

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _emailService.SendEmailVerificationAsync(user.Email, verificationCode.PlainCode);

                                _logger.LogInformation(
                                    "Verification code generated and sent. CorrelationId: {CorrelationId}",
                                    correlationId);
                            }
                            catch (Exception emailEx)
                            {
                                _logger.LogError(emailEx,
                                    "Background verification email failed. CorrelationId: {CorrelationId}, UserId: {UserId}",
                                    correlationId, user.Id);
                            }
                        });

                        await LogAuditAsync(
                          user.Id,
                          "Registration",
                          null,
                          null,
                          true,
                          $"User registered as {request.UserType} - pending verification",
                          correlationId);

                        return OperationResult<AuthResponseDTO>.Successful(
                            null,
                            "Registration successful. Please check your email for verification code.",
                            201
                        );

                    }
                    catch (Exception transactionEx)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(transactionEx,
                            "Transaction failed during registration. CorrelationId: {CorrelationId}, Email: {Email}",
                            correlationId, normalizedEmail);
                        throw;
                    }

                });

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return OperationResult<AuthResponseDTO>.Failure(
                    "An error occurred during registration",
                    500,
                    ex.Message
                );
            }
        }

        public async Task<OperationResult<AuthResponseDTO>> LoginAsync(LoginRequestDTO request)
        {
            var correlationId = Guid.NewGuid().ToString();
            try
            {
                var validationResult = ValidateLoginInput(request);
                if (!validationResult.IsValid)
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                       validationResult.ErrorMessage,
                       400
                   );
                }

                var normalizedEmail = NormalizeEmail(request.Email);

                // Match the exact email Identity stored (including +aliases).
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)
                    .FirstOrDefaultAsync(u =>
                        u.NormalizedEmail == normalizedEmail.ToUpperInvariant() ||
                        u.Email!.ToLower() == normalizedEmail);

                if (user == null)
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid email or password",
                        401
                    );
                }

                // Validate credentials before revealing verification state or sending OTP.
                var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
                if (!result.Succeeded)
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid email or password",
                        401
                    );
                }

                if (!user.EmailConfirmed)
                {
                    await TryQueueVerificationEmailOnLoginAsync(user, correlationId);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Account not verified. We've sent a verification code to your email.",
                        403
                    );
                }

                if (user.Status != UserStatus.Active)
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                        "Account is not active",
                        403
                    );
                }

                user.LastLoginAt = DateTime.UtcNow;
                user.Status = UserStatus.Active;
                await _userManager.UpdateAsync(user);

                var roles = await _userManager.GetRolesAsync(user);
                var accessToken = _tokenService.GenerateAccessToken(user, roles);
                var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
                    user.Id, request.DeviceInfo, request.IpAddress);

                // Map to appropriate DTO based on UserType
                ApplicationUserDTO userDto = user.UserType == UserType.Agent
                    ? _mapper.Map<AgentUserDTO>(user)
                    : _mapper.Map<RegularUserDTO>(user);

                var authResponse = new AuthResponseDTO
                {
                    Token = accessToken,
                    RefreshToken = refreshToken,
                    User = userDto
                };

                return OperationResult<AuthResponseDTO>.Successful(
                    authResponse,
                    "Login Successful.",
                    200
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return OperationResult<AuthResponseDTO>.Failure(
                    "An error occurred during login",
                    500,
                    ex.Message
                );
            }
        }

        public async Task<OperationResult<AuthResponseDTO>> RefreshTokenAsync(RefreshTokenRequest request)
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                if (string.IsNullOrWhiteSpace(request.AccessToken))
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                        "Access token is required",
                        400
                    );
                }

                if (string.IsNullOrWhiteSpace(request.RefreshToken))
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                        "Refresh token is required",
                        400
                    );
                }

                var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
                if (principal == null)
                {
                    _logger.LogWarning(
                        "Invalid token provided for refresh. CorrelationId: {CorrelationId}",
                        correlationId);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid token",
                        401
                    );
                }

                var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    _logger.LogWarning(
                        "Invalid user ID claim in token. CorrelationId: {CorrelationId}",
                        correlationId);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid token",
                        401
                    );
                }

                // Include AgentProfile for agents
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)  // Added
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogWarning(
                        "User not found for token refresh. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, userId);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid token",
                        401
                    );
                }

                if (!user.EmailConfirmed)
                {
                    _logger.LogWarning(
                        "Token refresh attempted for unverified user. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, userId);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Account not verified",
                        403
                    );
                }

                if (user.Status != UserStatus.Active)
                {
                    _logger.LogWarning(
                        "Token refresh attempted for inactive user. CorrelationId: {CorrelationId}, UserId: {UserId}, Status: {Status}",
                        correlationId, userId, user.Status);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Account is not active",
                        403
                    );
                }

                var refreshTokenHash = _tokenService.HashToken(request.RefreshToken);

                var storedRefreshToken = await _unitOfWork.RefreshTokens
                    .FirstOrDefaultAsync(rt =>
                        rt.UserId == userId &&
                        rt.TokenHash == refreshTokenHash &&
                        !rt.Revoked &&
                        rt.ExpiresAt > DateTime.UtcNow
                    );

                if (storedRefreshToken == null)
                {
                    _logger.LogWarning(
                        "Invalid or expired refresh token. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, userId);

                    await LogAuditAsync(
                        userId,
                        "TokenRefreshFailed",
                        null,
                        null,
                        false,
                        "Invalid or expired refresh token",
                        correlationId);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid or expired refresh token",
                        401
                    );
                }

                var strategy = _context.Database.CreateExecutionStrategy();

                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        storedRefreshToken.Revoked = true;
                        storedRefreshToken.RevokedAt = DateTime.UtcNow;
                        _unitOfWork.RefreshTokens.Update(storedRefreshToken);

                        user.LastLoginAt = DateTime.UtcNow;
                        await _userManager.UpdateAsync(user);

                        var roles = await _userManager.GetRolesAsync(user);
                        var newAccessToken = _tokenService.GenerateAccessToken(user, roles);
                        var newRefreshToken = await _tokenService.GenerateRefreshTokenAsync(
                            user.Id,
                            storedRefreshToken.DeviceInfo,
                            storedRefreshToken.IpAddress
                        );

                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        await LogAuditAsync(
                            userId,
                            "TokenRefresh",
                            storedRefreshToken.IpAddress,
                            null,
                            true,
                            "Tokens refreshed successfully",
                            correlationId);

                        // Map to appropriate DTO based on UserType
                        ApplicationUserDTO userDto = user.UserType == UserType.Agent
                            ? _mapper.Map<AgentUserDTO>(user)
                            : _mapper.Map<RegularUserDTO>(user);

                        var authResponse = new AuthResponseDTO
                        {
                            Token = newAccessToken,
                            RefreshToken = newRefreshToken,
                            User = userDto
                        };

                        _logger.LogInformation(
                            "Token refresh successful. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, userId);

                        return OperationResult<AuthResponseDTO>.Successful(
                            authResponse,
                            "Token refresh successful",
                            200
                        );
                    }
                    catch (Exception transactionEx)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(transactionEx,
                            "Transaction failed during token refresh. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, userId);
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error during token refresh. CorrelationId: {CorrelationId}",
                    correlationId);
                return OperationResult<AuthResponseDTO>.Failure(
                    "An error occurred during token refresh",
                    500,
                    ex.Message
                );
            }
        }
        
        public async Task<OperationResult<AuthResponseDTO>> ResendVerificationCodeAsync(ResendCodeRequestDTO request)
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                var validationResult = ValidateVerificationCodeResendInput(request);
                if (!validationResult.IsValid)
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                        validationResult.ErrorMessage,
                        400
                    );
                }

                var normalizedEmail = NormalizeEmail(request.Email);
                var user = await _userManager.FindByEmailAsync(normalizedEmail);

                
                if (user == null)
                {
                    _logger.LogInformation(
                        "Resend code attempted for non-existing account. CorrelationId: {CorrelationId}",
                        correlationId);
                    await Task.Delay(Random.Shared.Next(100, 300));
                    return OperationResult<AuthResponseDTO>.Successful(
                        null,
                        "If an account exists with this email, a verification code has been sent.",
                        200
                    );
                }

                
                if (user.EmailConfirmed)
                {
                    _logger.LogInformation(
                        "Resend code attempted for verified account. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, user.Id);
                    return OperationResult<AuthResponseDTO>.Successful(
                        null,
                        "If an account exists with this email, a verification code has been sent.",
                        200
                    );
                }

                
                if (user.Status != UserStatus.New && user.Status != UserStatus.Pending)
                {
                    _logger.LogInformation(
                        "Resend code attempted for ineligible account. CorrelationId: {CorrelationId}, UserId: {UserId}, Status: {Status}",
                        correlationId, user.Id, user.Status);
                    return OperationResult<AuthResponseDTO>.Successful(
                        null,
                        "If an account exists with this email, a verification code has been sent.",
                        200
                    );
                }


                var cooldownSeconds = _options.ResendCooldownSeconds;

                var recentCode = await _unitOfWork.EmailVerificationTokens
                    .GetLatestTokenForUserAsync(user.Id);

                if (recentCode != null)
                {
                    var elapsedSeconds =
                        (DateTime.UtcNow - recentCode.CreatedAt).TotalSeconds;

                    if (elapsedSeconds < cooldownSeconds)
                    {
                        var remainingSeconds =
                            (int)Math.Ceiling(cooldownSeconds - elapsedSeconds);

                        _logger.LogWarning(
                            "Rate limit hit for verification resend. CorrelationId: {CorrelationId}, UserId: {UserId}, RemainingSeconds: {RemainingSeconds}",
                            correlationId, user.Id, remainingSeconds);

                        return OperationResult<AuthResponseDTO>.Failure(
                            $"Please wait at least {remainingSeconds} seconds before requesting another code.",
                            429
                        );
                    }
                }


                var verificationCode = await GenerateSecureVerificationCode(
                    user.Id,
                    user.Email!,
                    correlationId);

                if (verificationCode == null || string.IsNullOrWhiteSpace(verificationCode.PlainCode))
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                        "Failed to generate verification code. Please try again.",
                        500
                    );
                }
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendEmailVerificationAsync(user.Email!, verificationCode.PlainCode);
                        _logger.LogInformation(
                            "Verification code resent successfully. CorrelationId: {CorrelationId}",
                            correlationId);
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx,
                            "Failed to send verification email. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, user.Id);
                    }
                });

                await LogAuditAsync(
                    user.Id,
                    "VerificationCodeResend",
                    null,
                    null,
                    true,
                    "Verification code resent",
                    correlationId);

                return OperationResult<AuthResponseDTO>.Successful(
                    null,
                    "If an account exists with this email, a verification code has been sent.",
                    200
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error resending verification code. CorrelationId: {CorrelationId}",
                    correlationId);
                return OperationResult<AuthResponseDTO>.Failure(
                    "An error occurred while sending verification code",
                    500
                );
            }
        }

        public async Task<OperationResult<AuthResponseDTO>> VerifyEmailAsync(VerifyEmailRequestDTO request)
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                var validationResult = ValidateVerifyEmailInput(request);
                if (!validationResult.IsValid)
                {
                    return OperationResult<AuthResponseDTO>.Failure(
                        validationResult.ErrorMessage,
                        400
                    );
                }

                var normalizedEmail = NormalizeEmail(request.Email);

                // Include AgentProfile for agents
                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .Include(u => u.AgentProfile)  // Added
                    .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail.ToUpper());

                if (user == null || string.IsNullOrWhiteSpace(user.Email))
                {
                    _logger.LogInformation(
                        "Email verification attempted for non-existing account. CorrelationId: {CorrelationId}",
                        correlationId);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid verification code",
                        400
                    );
                }

                if (user.EmailConfirmed)
                {
                    _logger.LogInformation(
                        "Email verification attempted for already verified account. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, user.Id);

                    var existingRoles = await _userManager.GetRolesAsync(user);
                    var existingAccessToken = _tokenService.GenerateAccessToken(user, existingRoles);
                    var existingRefreshToken = await _tokenService.GenerateRefreshTokenAsync(
                        user.Id, request.DeviceInfo, request.IpAddress);

                    // Map to appropriate DTO based on UserType
                    ApplicationUserDTO existingUserDto = user.UserType == UserType.Agent
                        ? _mapper.Map<AgentUserDTO>(user)
                        : _mapper.Map<RegularUserDTO>(user);

                    return OperationResult<AuthResponseDTO>.Successful(
                        new AuthResponseDTO
                        {
                            Token = existingAccessToken,
                            RefreshToken = existingRefreshToken,
                            User = existingUserDto
                        },
                        "Your email is verified. You are now logged in.",
                        200
                    );
                }

                if (user.Status != UserStatus.New && user.Status != UserStatus.Pending)
                {
                    _logger.LogInformation(
                        "Email verification attempted for ineligible account. CorrelationId: {CorrelationId}, UserId: {UserId}, Status: {Status}",
                        correlationId, user.Id, user.Status);
                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid verification request",
                        400
                    );
                }

                var tokenHash = HashVerificationCode(request.Code);
                var validToken = await _unitOfWork.EmailVerificationTokens
                    .GetValidTokenAsync(user.Email, tokenHash);

                if (validToken == null)
                {
                    _logger.LogWarning(
                        "Invalid verification code provided. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, user.Id);

                    return OperationResult<AuthResponseDTO>.Failure(
                        "Invalid or expired verification code",
                        400
                    );
                }

                var strategy = _context.Database.CreateExecutionStrategy();

                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        validToken.Used = true;
                        validToken.UsedAt = DateTime.UtcNow;

                        user.EmailConfirmed = true;
                        user.Status = UserStatus.Active;
                        user.UpdatedAt = DateTime.UtcNow;
                        await _userManager.UpdateAsync(user);

                        var activeTokens = await _unitOfWork.EmailVerificationTokens
                            .GetActiveTokensByEmailAsync(user.Email);

                        var otherTokens = activeTokens.Where(t => t.Id != validToken.Id).ToList();
                        if (otherTokens.Any())
                        {
                            await _unitOfWork.EmailVerificationTokens.InvalidateTokensAsync(otherTokens);
                        }

                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    catch (Exception transactionEx)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(transactionEx,
                            "Transaction failed during email verification. CorrelationId: {CorrelationId}, Email: {Email}",
                            correlationId, normalizedEmail);
                        throw;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _emailService.SendWelcomeEmailAsync(
                                user.Email!,
                                user.Profile.FirstName);

                            _logger.LogInformation(
                                "Welcome email sent. CorrelationId: {CorrelationId}",
                                correlationId);
                        }
                        catch (Exception emailEx)
                        {
                            _logger.LogError(emailEx,
                                "Background welcome email failed. CorrelationId: {CorrelationId}, UserId: {UserId}",
                                correlationId, user.Id);
                        }
                    });

                    await LogAuditAsync(
                        user.Id,
                        "EmailVerification",
                        request.IpAddress,
                        request.UserAgent,
                        true,
                        $"Email verified successfully for {user.UserType}",
                        correlationId);

                    var roles = await _userManager.GetRolesAsync(user);
                    var accessToken = _tokenService.GenerateAccessToken(user, roles);
                    var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
                        user.Id, request.DeviceInfo, request.IpAddress);

                    // Map to appropriate DTO based on UserType
                    ApplicationUserDTO userDto = user.UserType == UserType.Agent
                        ? _mapper.Map<AgentUserDTO>(user)
                        : _mapper.Map<RegularUserDTO>(user);

                    var authResponse = new AuthResponseDTO
                    {
                        Token = accessToken,
                        RefreshToken = refreshToken,
                        User = userDto
                    };

                    return OperationResult<AuthResponseDTO>.Successful(
                        authResponse,
                        "Email verification successful. You are now logged in.",
                        200
                    );
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error verifying email. CorrelationId: {CorrelationId}",
                    correlationId);
                return OperationResult<AuthResponseDTO>.Failure(
                    "An error occurred while verifying email",
                    500
                );
            }
        }

        public async Task<OperationResult<string>> ChangePasswordAsync(Guid userId, ChangePasswordRequestDTO request)
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                var validationResult = ValidateChangePasswordInput(request);
                if (!validationResult.IsValid)
                {
                    return OperationResult<string>.Failure(
                        validationResult.ErrorMessage,
                        400
                    );
                }

                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogWarning(
                        "Change password attempted for non-existent user. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, userId);

                    return OperationResult<string>.Failure(
                        "User not found",
                        404
                    );
                }

                var passwordValid = await _userManager.CheckPasswordAsync(user, request.CurrentPassword);
                if (!passwordValid)
                {
                    _logger.LogWarning(
                        "Change password failed - incorrect current password. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, userId);

                    await LogAuditAsync(
                        userId,
                        "ChangePasswordFailed",
                        request.IpAddress,
                        null,
                        false,
                        "Incorrect current password",
                        correlationId);

                    return OperationResult<string>.Failure(
                        "Current password is incorrect",
                        400
                    );
                }

                var isSamePassword = await _userManager.CheckPasswordAsync(user, request.NewPassword);
                if (isSamePassword)
                {
                    return OperationResult<string>.Failure(
                        "New password must be different from current password",
                        400
                    );
                }

                var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogWarning(
                        "Change password failed. CorrelationId: {CorrelationId}, UserId: {UserId}, Errors: {Errors}",
                        correlationId, userId, errors);

                    return OperationResult<string>.Failure(
                        $"Failed to change password: {errors}",
                        400,
                        result.Errors
                    );
                }

                var strategy = _context.Database.CreateExecutionStrategy();

                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        user.UpdatedAt = DateTime.UtcNow;
                        await _userManager.UpdateAsync(user);

                        var refreshTokensToRevoke = await _unitOfWork.RefreshTokens
                            .FindAsync(rt => rt.UserId == userId && !rt.Revoked);

                        foreach (var token in refreshTokensToRevoke)
                        {
                            token.Revoked = true;
                            token.RevokedAt = DateTime.UtcNow;
                            _unitOfWork.RefreshTokens.Update(token);
                        }

                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _emailService.SendPasswordChangedNotificationAsync(
                                    user.Email!,
                                    user.Profile.FirstName);

                                _logger.LogInformation(
                                    "Password changed notification sent. CorrelationId: {CorrelationId}",
                                    correlationId);
                            }
                            catch (Exception emailEx)
                            {
                                _logger.LogError(emailEx,
                                    "Failed to send password changed notification. CorrelationId: {CorrelationId}, UserId: {UserId}",
                                    correlationId, userId);
                            }
                        });

                        await LogAuditAsync(
                            userId,
                            "PasswordChanged",
                            request.IpAddress,
                            null,
                            true,
                            "Password changed successfully",
                            correlationId);

                        _logger.LogInformation(
                            "Password changed successfully. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, userId);

                        return OperationResult<string>.Successful(
                            "Password changed successfully",
                            "Password changed successfully. You have been logged out of all other devices.",
                            200
                        );
                    }
                    catch (Exception transactionEx)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(transactionEx,
                            "Transaction failed during password change. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, userId);
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error changing password. CorrelationId: {CorrelationId}, UserId: {UserId}",
                    correlationId, userId);
                return OperationResult<string>.Failure(
                    "An error occurred while changing password",
                    500
                );
            }
        }

        public async Task<OperationResult<string>> ForgotPasswordAsync(ForgotPasswordDTO request)
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                var validationResult = ValidateForgotPasswordInput(request);
                if (!validationResult.IsValid)
                {
                    return OperationResult<string>.Failure(
                        validationResult.ErrorMessage,
                        400
                    );
                }

                var normalizedEmail = NormalizeEmail(request.Email);

                var user = await _userManager.FindByEmailAsync(normalizedEmail);

                if (user == null)
                {
                    _logger.LogInformation(
                        "Password reset requested for non-existent account. CorrelationId: {CorrelationId}",
                        correlationId);
                    await Task.Delay(Random.Shared.Next(100, 300));

                    return OperationResult<string>.Successful(
                        "If an account exists with this email, a password reset code has been sent.",
                        "If an account exists with this email, a password reset code has been sent.",
                        200
                    );
                }

                if (!user.EmailConfirmed)
                {
                    _logger.LogInformation(
                        "Password reset requested for unverified account. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, user.Id);

                    return OperationResult<string>.Successful(
                        "If an account exists with this email, a password reset code has been sent.",
                        "If an account exists with this email, a password reset code has been sent.",
                        200
                    );
                }

                var cooldownSeconds = _options.ResendCooldownSeconds;
                var recentToken = await _unitOfWork.PasswordResetTokens
                    .GetLatestPasswordResetTokenForUserAsync(user.Id);

                if (recentToken != null)
                {
                    var elapsedSeconds = (DateTime.UtcNow - recentToken.CreatedAt).TotalSeconds;
                    if (elapsedSeconds < cooldownSeconds)
                    {
                        var remainingSeconds = (int)Math.Ceiling(cooldownSeconds - elapsedSeconds);

                        _logger.LogWarning(
                            "Rate limit hit for password reset. CorrelationId: {CorrelationId}, UserId: {UserId}, RemainingSeconds: {RemainingSeconds}",
                            correlationId, user.Id, remainingSeconds);

                        return OperationResult<string>.Failure(
                            $"Please wait at least {remainingSeconds} seconds before requesting another reset code.",
                            429
                        );
                    }
                }

                var resetCode = await GenerateSecurePasswordResetCode(
                    user.Id,
                    user.Email!,
                    correlationId);

                if (resetCode == null || string.IsNullOrWhiteSpace(resetCode.PlainCode))
                {
                    return OperationResult<string>.Failure(
                        "Failed to generate password reset code. Please try again.",
                        500
                    );
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendPasswordResetAsync(user.Email!, resetCode.PlainCode);
                        _logger.LogInformation(
                            "Password reset code sent successfully. CorrelationId: {CorrelationId}",
                            correlationId);
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx,
                            "Failed to send password reset email. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, user.Id);
                    }
                });

                await LogAuditAsync(
                    user.Id,
                    "PasswordResetRequested",
                    null,
                    null,
                    true,
                    "Password reset code generated and sent",
                    correlationId);

                return OperationResult<string>.Successful(
                    "If an account exists with this email, a password reset code has been sent.",
                    "If an account exists with this email, a password reset code has been sent.",
                    200
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error requesting password reset. CorrelationId: {CorrelationId}",
                    correlationId);
                return OperationResult<string>.Failure(
                    "An error occurred while requesting password reset",
                    500
                );
            }
        }

        public async Task<OperationResult<string>> ResetPasswordAsync(ResetPasswordDTO request)
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                var validationResult = ValidateResetPasswordInput(request);
                if (!validationResult.IsValid)
                {
                    return OperationResult<string>.Failure(
                        validationResult.ErrorMessage,
                        400
                    );
                }

                var normalizedEmail = NormalizeEmail(request.Email);

                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail.ToUpper());

                if (user == null || string.IsNullOrWhiteSpace(user.Email))
                {
                    _logger.LogInformation(
                        "Password reset attempted for non-existent account. CorrelationId: {CorrelationId}",
                        correlationId);

                    return OperationResult<string>.Failure(
                        "Invalid or expired reset code",
                        400
                    );
                }

                if (!user.EmailConfirmed)
                {
                    _logger.LogInformation(
                        "Password reset attempted for unverified account. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, user.Id);

                    return OperationResult<string>.Failure(
                        "Invalid or expired reset code",
                        400
                    );
                }

                var tokenHash = HashVerificationCode(request.Code);
                var validToken = await _unitOfWork.PasswordResetTokens
                    .GetValidPasswordResetTokenAsync(user.Id, tokenHash);

                if (validToken == null)
                {
                    _logger.LogWarning(
                        "Invalid password reset code provided. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, user.Id);

                    await LogAuditAsync(
                        user.Id,
                        "PasswordResetFailed",
                        request.IpAddress,
                        null,
                        false,
                        "Invalid or expired reset code",
                        correlationId);

                    return OperationResult<string>.Failure(
                        "Invalid or expired reset code",
                        400
                    );
                }

                var isSamePassword = await _userManager.CheckPasswordAsync(user, request.NewPassword);
                if (isSamePassword)
                {
                    return OperationResult<string>.Failure(
                        "New password must be different from current password",
                        400
                    );
                }

                var strategy = _context.Database.CreateExecutionStrategy();

                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        validToken.Used = true;
                        validToken.UsedAt = DateTime.UtcNow;

                        var activeTokens = await _unitOfWork.PasswordResetTokens
                            .GetActivePasswordResetTokensByEmailAsync(user.Id);

                        var otherTokens = activeTokens.Where(t => t.Id != validToken.Id).ToList();
                        if (otherTokens.Any())
                        {
                            await _unitOfWork.PasswordResetTokens.InvalidatePasswordResetTokensAsync(otherTokens);
                        }

                        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

                        if (!result.Succeeded)
                        {
                            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                            await transaction.RollbackAsync();

                            _logger.LogWarning(
                                "Password reset failed. CorrelationId: {CorrelationId}, UserId: {UserId}, Errors: {Errors}",
                                correlationId, user.Id, errors);

                            return OperationResult<string>.Failure(
                                $"Failed to reset password: {errors}",
                                400,
                                result.Errors
                            );
                        }

                        user.UpdatedAt = DateTime.UtcNow;
                        await _userManager.UpdateAsync(user);

                        var refreshTokensToRevoke = await _unitOfWork.RefreshTokens
                            .FindAsync(rt => rt.UserId == user.Id && !rt.Revoked);

                        foreach (var refreshToken in refreshTokensToRevoke)
                        {
                            refreshToken.Revoked = true;
                            refreshToken.RevokedAt = DateTime.UtcNow;
                            _unitOfWork.RefreshTokens.Update(refreshToken);
                        }

                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _emailService.SendPasswordResetConfirmationAsync(
                                    user.Email!,
                                    user.Profile.FirstName);

                                _logger.LogInformation(
                                    "Password reset confirmation sent. CorrelationId: {CorrelationId}",
                                    correlationId);
                            }
                            catch (Exception emailEx)
                            {
                                _logger.LogError(emailEx,
                                    "Failed to send password reset confirmation. CorrelationId: {CorrelationId}, UserId: {UserId}",
                                    correlationId, user.Id);
                            }
                        });

                        await LogAuditAsync(
                            user.Id,
                            "PasswordReset",
                            request.IpAddress,
                            null,
                            true,
                            "Password reset successfully",
                            correlationId);

                        _logger.LogInformation(
                            "Password reset successfully. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, user.Id);

                        return OperationResult<string>.Successful(
                            "Password reset successfully",
                            "Password reset successfully. Please login with your new password.",
                            200
                        );
                    }
                    catch (Exception transactionEx)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(transactionEx,
                            "Transaction failed during password reset. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, user.Id);
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error resetting password. CorrelationId: {CorrelationId}",
                    correlationId);
                return OperationResult<string>.Failure(
                    "An error occurred while resetting password",
                    500
                );
            }
        }

        public async Task<OperationResult<string>> DeleteAccountAsync(Guid userId, DeleteAccountRequestDTO request)
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                var validationResult = ValidateDeleteAccountInput(request);
                if (!validationResult.IsValid)
                {
                    return OperationResult<string>.Failure(
                        validationResult.ErrorMessage,
                        400
                    );
                }

                var user = await _userManager.Users
                    .Include(u => u.Profile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogWarning(
                        "Account deletion attempted for non-existent user. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, userId);

                    return OperationResult<string>.Failure(
                        "User not found",
                        404
                    );
                }

                if (user.Status == UserStatus.Deleted)
                {
                    _logger.LogInformation(
                        "Account deletion attempted for already deleted user. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, userId);

                    return OperationResult<string>.Failure(
                        "Account is already deleted",
                        400
                    );
                }

                var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
                if (!passwordValid)
                {
                    _logger.LogWarning(
                        "Account deletion failed - incorrect password. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, userId);

                    await LogAuditAsync(
                        userId,
                        "AccountDeletionFailed",
                        request.IpAddress,
                        request.UserAgent,
                        false,
                        "Incorrect password provided",
                        correlationId);

                    return OperationResult<string>.Failure(
                        "Incorrect password",
                        400
                    );
                }

                var strategy = _context.Database.CreateExecutionStrategy();

                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        var userEmail = user.Email!;
                        var userName = user.Profile?.FirstName ?? "User";

                        user.DeletedAt = DateTime.UtcNow;
                        user.DeletionReason = SanitizeInput(request.Reason, 500);
                        user.Status = UserStatus.Deleted;
                        user.UpdatedAt = DateTime.UtcNow;

                        user.UserName = $"deleted_{userId}@deleted.local";
                        user.NormalizedUserName = user.UserName.ToUpperInvariant();
                        user.Email = user.UserName;
                        user.NormalizedEmail = user.NormalizedUserName;
                        user.EmailConfirmed = false;
                        user.PhoneNumber = null;
                        user.PhoneNumberConfirmed = false;
                        user.ProfilePhoto = null;


                        await _userManager.UpdateAsync(user);

                        if (user.Profile != null)
                        {
                            user.Profile.FirstName = "Deleted";
                            user.Profile.LastName = "User";
                            user.Profile.City = "N/A";
                            user.Profile.State = "N/A";
                            user.Profile.Country = "N/A";

                            _unitOfWork.UserProfiles.Update(user.Profile);
                        }

                        var agentProfile = await _unitOfWork.AgentProfiles
                            .FirstOrDefaultAsync(ap => ap.UserId == userId);

                        if (agentProfile != null)
                        {
                            agentProfile.LicenseNumber = null;
                            agentProfile.AgencyName = null;

                            _unitOfWork.AgentProfiles.Update(agentProfile);
                        }

                        var refreshTokens = await _unitOfWork.RefreshTokens
                            .FindAsync(rt => rt.UserId == userId && !rt.Revoked);

                        foreach (var token in refreshTokens)
                        {
                            token.Revoked = true;
                            token.RevokedAt = DateTime.UtcNow;
                            _unitOfWork.RefreshTokens.Update(token);
                        }

                        var emailVerificationTokens = await _context.EmailVerificationTokens
                            .Where(t => t.UserId == userId && !t.Used)
                            .ToListAsync();

                        foreach (var token in emailVerificationTokens)
                        {
                            token.Used = true;
                            token.UsedAt = DateTime.UtcNow;
                        }

                        var passwordResetTokens = await _context.PasswordResetTokens
                            .Where(t => t.UserId == userId && !t.Used)
                            .ToListAsync();

                        foreach (var token in passwordResetTokens)
                        {
                            token.Used = true;
                            token.UsedAt = DateTime.UtcNow;
                        }

                        // delete user-generated content or mark as deleted
                        // this depends on business requirements
                        // e.g. properties, reviews, favorites, etc.
                        // you might want to either delete them or anonymize them

                        await _unitOfWork.SaveChangesAsync();

                        await transaction.CommitAsync();

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _emailService.SendAccountDeletionConfirmationAsync(
                                    userEmail,
                                    userName);

                                _logger.LogInformation(
                                    "Account deletion confirmation sent. CorrelationId: {CorrelationId}",
                                    correlationId);
                            }
                            catch (Exception emailEx)
                            {
                                _logger.LogError(emailEx,
                                    "Failed to send account deletion confirmation. CorrelationId: {CorrelationId}, UserId: {UserId}",
                                    correlationId, userId);
                            }
                        });

                        await LogAuditAsync(
                            userId,
                            "AccountDeleted",
                            request.IpAddress,
                            request.UserAgent,
                            true,
                            $"Account deleted. Reason: {SanitizeInput(request.Reason, 200)}",
                            correlationId);

                        _logger.LogInformation(
                            "Account deleted successfully. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, userId);

                        return OperationResult<string>.Successful(
                            "Account deleted successfully",
                            "Your account has been deleted successfully. We're sorry to see you go.",
                            200
                        );
                    }
                    catch (Exception transactionEx)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(transactionEx,
                            "Transaction failed during account deletion. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, userId);
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error deleting account. CorrelationId: {CorrelationId}, UserId: {UserId}",
                    correlationId, userId);
                return OperationResult<string>.Failure(
                    "An error occurred while deleting account",
                    500
                );
            }
        }

        private async Task<PasswordResetToken?> GenerateSecurePasswordResetCode(
            Guid userId,
            string email,
            string? correlationId = null)
        {
            try
            {
                using var rng = RandomNumberGenerator.Create();
                var bytes = new byte[4];
                rng.GetBytes(bytes);
                var number = BitConverter.ToUInt32(bytes, 0);
                var code = (number % 1_000_000).ToString("D6");

                var tokenHash = HashVerificationCode(code);
                var expiresAt = DateTime.UtcNow.AddMinutes(_options.PasswordResetCodeExpiryMinutes);

                var activeTokens = await _unitOfWork.PasswordResetTokens
                    .GetActivePasswordResetTokensByEmailAsync(userId);

                if (activeTokens.Any())
                {
                    await _unitOfWork.PasswordResetTokens
                        .InvalidatePasswordResetTokensAsync(activeTokens);
                }

                var resetToken = new PasswordResetToken
                {
                    UserId = userId,
                    TokenHash = tokenHash,
                    ExpiresAt = expiresAt,
                    Used = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.PasswordResetTokens.AddAsync(resetToken);
                await _unitOfWork.SaveChangesAsync();

                resetToken.PlainCode = code;

                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                {
                    _logger.LogWarning(
                        "DEV password reset code for {Email}: {Code} (CorrelationId: {CorrelationId})",
                        email, code, correlationId);
                }

                return resetToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to generate password reset code. CorrelationId: {CorrelationId}, Email: {Email}",
                    correlationId, email);
                return null;
            }
        }

        /// <summary>
        /// Best-effort OTP email when an unverified user logs in with a valid password.
        /// Respects resend cooldown; never fails the login response if send is skipped.
        /// </summary>
        private async Task TryQueueVerificationEmailOnLoginAsync(
            ApplicationUser user,
            string correlationId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    return;
                }

                if (user.Status != UserStatus.New && user.Status != UserStatus.Pending)
                {
                    _logger.LogInformation(
                        "Skipping login verification email for ineligible status. CorrelationId: {CorrelationId}, UserId: {UserId}, Status: {Status}",
                        correlationId, user.Id, user.Status);
                    return;
                }

                var cooldownSeconds = _options.ResendCooldownSeconds;
                var recentCode = await _unitOfWork.EmailVerificationTokens
                    .GetLatestTokenForUserAsync(user.Id);

                if (recentCode != null)
                {
                    var elapsedSeconds = (DateTime.UtcNow - recentCode.CreatedAt).TotalSeconds;
                    if (elapsedSeconds < cooldownSeconds)
                    {
                        _logger.LogInformation(
                            "Login verification email skipped due to cooldown. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, user.Id);
                        return;
                    }
                }

                var verificationCode = await GenerateSecureVerificationCode(
                    user.Id,
                    user.Email,
                    correlationId);

                if (verificationCode == null || string.IsNullOrWhiteSpace(verificationCode.PlainCode))
                {
                    _logger.LogWarning(
                        "Failed to generate login verification code. CorrelationId: {CorrelationId}, UserId: {UserId}",
                        correlationId, user.Id);
                    return;
                }

                var email = user.Email;
                var plainCode = verificationCode.PlainCode;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendEmailVerificationAsync(email, plainCode);
                        _logger.LogInformation(
                            "Login verification code sent. CorrelationId: {CorrelationId}",
                            correlationId);
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx,
                            "Failed to send login verification email. CorrelationId: {CorrelationId}, UserId: {UserId}",
                            correlationId, user.Id);
                    }
                });

                await LogAuditAsync(
                    user.Id,
                    "VerificationCodeResend",
                    null,
                    null,
                    true,
                    "Verification code resent on login",
                    correlationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error queueing login verification email. CorrelationId: {CorrelationId}, UserId: {UserId}",
                    correlationId, user.Id);
            }
        }

        private async Task<EmailVerificationToken?> GenerateSecureVerificationCode(
              Guid userId,
              string email,
              string? correlationId = null)
        {
            try
            {
                using var rng = RandomNumberGenerator.Create();
                var bytes = new byte[4];
                rng.GetBytes(bytes);
                var number = BitConverter.ToUInt32(bytes, 0);
                var code = (number % 1_000_000).ToString("D6");
                
                var tokenHash = HashVerificationCode(code);

                var expiresAt = DateTime.UtcNow.AddMinutes(_options.EmailCodeExpiryMinutes);

                var activeTokens = await _unitOfWork.EmailVerificationTokens
                     .GetActiveTokensByEmailAsync(email);

                if (activeTokens.Any())
                {
                    await _unitOfWork.EmailVerificationTokens
                        .InvalidateTokensAsync(activeTokens);
                }

                var verificationToken = new EmailVerificationToken
                {
                    UserId = userId,
                    Email = email,
                    TokenHash = tokenHash,
                    ExpiresAt = expiresAt,
                    Used = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.EmailVerificationTokens.AddAsync(verificationToken);
                await _unitOfWork.SaveChangesAsync();

                verificationToken.PlainCode = code;

                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                {
                    _logger.LogWarning(
                        "DEV email verification code for {Email}: {Code} (CorrelationId: {CorrelationId})",
                        email, code, correlationId);
                }

                return verificationToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to generate verification code. CorrelationId: {CorrelationId}, Email: {Email}",
                    correlationId, email);
                return null;
            }
        }

        private async Task LogAuditAsync(Guid userId, string action, string? ipAddress,
          string? userAgent, bool success, string? details, string? correlationId = null)
        {
            try
            {
                var auditLog = new AuditLog
                {
                    UserId = userId,
                    Action = action,
                    IpAddress = ipAddress,
                    UserAgent = SanitizeInput(userAgent, 500),
                    Success = success,
                    Details = SanitizeInput(details, 1000),
                    CreatedAt = DateTime.UtcNow
                };


                await _unitOfWork.AuditLogs.AddAsync(auditLog);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation(
    "Audit log created. CorrelationId: {CorrelationId}, UserId: {UserId}, Action: {Action}, Success: {Success}",
                                    correlationId, userId, action, success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
    "Failed to create audit log. CorrelationId: {CorrelationId}, UserId: {UserId}, Action: {Action}",
    correlationId, userId, action);
            }
        }

        private class ValidationResult
        {
            public bool IsValid { get; }
            public string ErrorMessage { get; }

            public ValidationResult(bool isValid, string? errorMessage = null)
            {
                IsValid = isValid;
                ErrorMessage = errorMessage ?? string.Empty;
            }
        }

        private ValidationResult ValidateRegistrationInput(RegisterRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return new ValidationResult(false, "Email address is required");

            if (!new EmailAddressAttribute().IsValid(request.Email))
                return new ValidationResult(false, "Please provide a valid email address");

            if (request.Email.Length > 254)
                return new ValidationResult(false, "Email address is too long");
            
            if (string.IsNullOrWhiteSpace(request.Password))
                return new ValidationResult(false, "Password is required");

            var passwordValidation = ValidatePassword(request.Password);
            if (!passwordValidation.IsValid)
                return passwordValidation;

       
            if (!Enum.IsDefined(typeof(UserType), request.UserType))
                return new ValidationResult(false, "Invalid role");


            return new ValidationResult(true);
        }

        private ValidationResult ValidateLoginInput(LoginRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return new ValidationResult(false, "Email address is required");

            if (!new EmailAddressAttribute().IsValid(request.Email))
                return new ValidationResult(false, "Please provide a valid email address");

            if (request.Email.Length > 254)
                return new ValidationResult(false, "Email address is too long");

            if (string.IsNullOrWhiteSpace(request.Password))
                return new ValidationResult(false, "Password is required");

            var passwordValidation = ValidatePassword(request.Password);
            if (!passwordValidation.IsValid)
                return passwordValidation;


            return new ValidationResult(true);
        }

        private ValidationResult ValidateVerificationCodeResendInput(ResendCodeRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return new ValidationResult(false, "Email address is required");

            if (!new EmailAddressAttribute().IsValid(request.Email))
                return new ValidationResult(false, "Please provide a valid email address");

            if (request.Email.Length > 254)
                return new ValidationResult(false, "Email address is too long");

            return new ValidationResult(true);
        }

        private ValidationResult ValidateVerifyEmailInput(VerifyEmailRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return new ValidationResult(false, "Email address is required");

            if (!new EmailAddressAttribute().IsValid(request.Email))
                return new ValidationResult(false, "Please provide a valid email address");

            if (request.Email.Length > 254)
                return new ValidationResult(false, "Email address is too long");

            if (string.IsNullOrWhiteSpace(request.Code))
                return new ValidationResult(false, "Verification code is required");

            var code = request.Code.Trim();

            if (code.Length != 6 || !code.All(char.IsDigit))
                return new ValidationResult(false, "Verification code must be exactly 6 digits");

            return new ValidationResult(true);
        }

        private ValidationResult ValidateChangePasswordInput(ChangePasswordRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                return new ValidationResult(false, "Current password is required");

            if (string.IsNullOrWhiteSpace(request.NewPassword))
                return new ValidationResult(false, "New password is required");

            var passwordValidation = ValidatePassword(request.NewPassword);
            if (!passwordValidation.IsValid)
                return passwordValidation;

            if (request.CurrentPassword == request.NewPassword)
                return new ValidationResult(false, "Bad Request");

            return new ValidationResult(true);
        }

        private ValidationResult ValidateForgotPasswordInput(ForgotPasswordDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return new ValidationResult(false, "Email address is required");

            if (!new EmailAddressAttribute().IsValid(request.Email))
                return new ValidationResult(false, "Please provide a valid email address");

            if (request.Email.Length > 254)
                return new ValidationResult(false, "Email address is too long");

            return new ValidationResult(true);
        }

        private ValidationResult ValidateResetPasswordInput(ResetPasswordDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return new ValidationResult(false, "Email address is required");

            if (!new EmailAddressAttribute().IsValid(request.Email))
                return new ValidationResult(false, "Please provide a valid email address");

            if (request.Email.Length > 254)
                return new ValidationResult(false, "Email address is too long");

            if (string.IsNullOrWhiteSpace(request.Code))
                return new ValidationResult(false, "Reset code is required");

            var code = request.Code.Trim();
            if (code.Length != 6 || !code.All(char.IsDigit))
                return new ValidationResult(false, "Reset code must be exactly 6 digits");

            if (string.IsNullOrWhiteSpace(request.NewPassword))
                return new ValidationResult(false, "New password is required");

            var passwordValidation = ValidatePassword(request.NewPassword);
            if (!passwordValidation.IsValid)
                return passwordValidation;

            return new ValidationResult(true);
        }

        private ValidationResult ValidateDeleteAccountInput(DeleteAccountRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
                return new ValidationResult(false, "Password is required to delete account");

            if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length > 500)
                return new ValidationResult(false, "Deletion reason is too long (max 500 characters)");

            return new ValidationResult(true);
        }

        private ValidationResult ValidatePassword(string password)
        {
            if (password.Length < 6)
                return new ValidationResult(false, "Password must be at least 6 characters");

            if (!password.Any(char.IsUpper))
                return new ValidationResult(false, "Password must contain at least one uppercase letter");

            if (!password.Any(char.IsLower))
                return new ValidationResult(false, "Password must contain at least one lowercase letter");

            if (!password.Any(char.IsDigit))
                return new ValidationResult(false, "Password must contain at least one digit");

            return new ValidationResult(true);
        }

        private string? SanitizeInput(string? input, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var sanitized = input.Trim();
            if (sanitized.Length > maxLength)
            {
                sanitized = sanitized.Substring(0, maxLength);
            }

            sanitized = sanitized
                .Replace("<", "")
                .Replace(">", "")
                .Replace("'", "'");

            return sanitized;
        }

        /// <summary>
        /// Canonical form for lookups. Keep <c>+</c> aliases and dots — those are distinct accounts.
        /// (Do not apply Gmail "same mailbox" collapsing here.)
        /// </summary>
        private string NormalizeEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return email;

            return email.Trim().ToLowerInvariant();
        }

        private string SanitizePhoneNumber(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return phoneNumber ?? string.Empty;

            return Regex.Replace(phoneNumber.Trim(), @"[^\d\+]", "");
        }

        private string HashVerificationCode(string code)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(code);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

    }
}