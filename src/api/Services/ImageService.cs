using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Habitera.DTOs;

namespace Habitera.Services
{
    public interface ICloudinaryService
    {
        Task<OperationResult<ImageUploadResultDTO>> UploadImageAsync(IFormFile file, string folder = "properties");
        Task<OperationResult<ImageUploadResultDTO>> UploadImageWithThumbnailAsync(IFormFile file, string folder = "properties");
        Task<OperationResult<ImageUploadResultDTO>> UploadAvatarAsync(IFormFile file, string folder = "profiles");
        Task<OperationResult<string>> DeleteImageAsync(string publicId);
        Task<OperationResult<List<ImageUploadResultDTO>>> UploadMultipleImagesAsync(IFormFileCollection files, string folder = "properties");
    }

    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary? _cloudinary;
        private readonly ILogger<CloudinaryService> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;
        private readonly bool _useLocalStorage;

        public CloudinaryService(
            IConfiguration configuration,
            ILogger<CloudinaryService> logger,
            IWebHostEnvironment environment,
            IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _logger = logger;
            _environment = environment;
            _httpContextAccessor = httpContextAccessor;

            var cloudName = configuration["Cloudinary:CloudName"];
            var apiKey = configuration["Cloudinary:ApiKey"];
            var apiSecret = configuration["Cloudinary:ApiSecret"];

            _useLocalStorage = ShouldUseLocalStorage(cloudName, apiKey, apiSecret, environment);

            if (_useLocalStorage)
            {
                _logger.LogWarning(
                    "Cloudinary credentials look like placeholders or are missing. Using local wwwroot uploads.");
                return;
            }

            if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                throw new InvalidOperationException("Cloudinary credentials are not configured");
            }

            var account = new Account(cloudName, apiKey, apiSecret);
            _cloudinary = new Cloudinary(account);
            _cloudinary.Api.Secure = true;
        }

        public async Task<OperationResult<ImageUploadResultDTO>> UploadImageAsync(
            IFormFile file,
            string folder = "properties")
        {
            try
            {
                var validation = ValidateImageFile(file);
                if (!validation.Success)
                {
                    return OperationResult<ImageUploadResultDTO>.Failure(validation.Message, 400);
                }

                if (_useLocalStorage || _cloudinary == null)
                {
                    return await SaveLocalImageAsync(file, folder);
                }

                using var stream = file.OpenReadStream();

                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder,
                    Transformation = new Transformation()
                        .Quality("auto:good")
                        .FetchFormat("auto"),
                    UniqueFilename = true,
                    Overwrite = false
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                if (uploadResult.Error != null)
                {
                    _logger.LogError("Cloudinary upload error: {Error}", uploadResult.Error.Message);
                    if (_environment.IsDevelopment())
                    {
                        return await SaveLocalImageAsync(file, folder);
                    }

                    return OperationResult<ImageUploadResultDTO>.Failure(
                        "Failed to upload image", 500);
                }

                var result = new ImageUploadResultDTO
                {
                    PublicId = uploadResult.PublicId,
                    Url = uploadResult.SecureUrl.ToString(),
                    Width = uploadResult.Width,
                    Height = uploadResult.Height,
                    Format = uploadResult.Format,
                    ResourceType = uploadResult.ResourceType
                };

                return OperationResult<ImageUploadResultDTO>.Successful(
                    result,
                    "Image uploaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading image to Cloudinary");
                if (_environment.IsDevelopment())
                {
                    try
                    {
                        return await SaveLocalImageAsync(file, folder);
                    }
                    catch (Exception localEx)
                    {
                        _logger.LogError(localEx, "Local image fallback also failed");
                    }
                }

                return OperationResult<ImageUploadResultDTO>.Failure(
                    "An error occurred while uploading the image", 500);
            }
        }

        public async Task<OperationResult<ImageUploadResultDTO>> UploadImageWithThumbnailAsync(
            IFormFile file,
            string folder = "properties")
        {
            try
            {
                var validation = ValidateImageFile(file);
                if (!validation.Success)
                {
                    return OperationResult<ImageUploadResultDTO>.Failure(validation.Message, 400);
                }

                if (_useLocalStorage || _cloudinary == null)
                {
                    return await SaveLocalImageAsync(file, folder);
                }

                using var stream = file.OpenReadStream();

                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder,
                    Transformation = new Transformation()
                        .Width(1920)
                        .Height(1080)
                        .Crop("limit")
                        .Quality("auto:good")
                        .FetchFormat("auto"),
                    EagerTransforms = new List<Transformation>
                    {
                        new Transformation()
                            .Width(400)
                            .Height(300)
                            .Crop("fill")
                            .Gravity("auto")
                            .Quality("auto:good")
                            .FetchFormat("auto")
                    },
                    UniqueFilename = true,
                    Overwrite = false
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                if (uploadResult.Error != null)
                {
                    _logger.LogError("Cloudinary upload error: {Error}", uploadResult.Error.Message);
                    if (_environment.IsDevelopment())
                    {
                        return await SaveLocalImageAsync(file, folder);
                    }

                    return OperationResult<ImageUploadResultDTO>.Failure(
                        "Failed to upload image", 500);
                }

                string thumbnailUrl = uploadResult.SecureUrl.ToString();
                if (uploadResult.Eager != null && uploadResult.Eager.Count() > 0)
                {
                    thumbnailUrl = uploadResult.Eager[0].SecureUrl.ToString();
                }

                var result = new ImageUploadResultDTO
                {
                    PublicId = uploadResult.PublicId,
                    Url = uploadResult.SecureUrl.ToString(),
                    ThumbnailUrl = thumbnailUrl,
                    Width = uploadResult.Width,
                    Height = uploadResult.Height,
                    Format = uploadResult.Format,
                    ResourceType = uploadResult.ResourceType
                };

                return OperationResult<ImageUploadResultDTO>.Successful(
                    result,
                    "Image uploaded successfully with thumbnail");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading image with thumbnail");
                if (_environment.IsDevelopment())
                {
                    try
                    {
                        return await SaveLocalImageAsync(file, folder);
                    }
                    catch (Exception localEx)
                    {
                        _logger.LogError(localEx, "Local image fallback also failed");
                    }
                }

                return OperationResult<ImageUploadResultDTO>.Failure(
                    "An error occurred while uploading the image", 500);
            }
        }

        public async Task<OperationResult<ImageUploadResultDTO>> UploadAvatarAsync(
            IFormFile file,
            string folder = "profiles")
        {
            try
            {
                var validation = ValidateImageFile(file);
                if (!validation.Success)
                {
                    return OperationResult<ImageUploadResultDTO>.Failure(validation.Message, 400);
                }

                if (_useLocalStorage || _cloudinary == null)
                {
                    return await SaveLocalImageAsync(file, folder);
                }

                using var stream = file.OpenReadStream();

                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder,
                    Transformation = new Transformation()
                        .Width(400)
                        .Height(400)
                        .Crop("fill")
                        .Gravity("face")
                        .Quality("auto:good")
                        .FetchFormat("auto"),
                    UniqueFilename = true,
                    Overwrite = false
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                if (uploadResult.Error != null)
                {
                    _logger.LogError("Cloudinary avatar upload error: {Error}", uploadResult.Error.Message);
                    if (_environment.IsDevelopment())
                    {
                        return await SaveLocalImageAsync(file, folder);
                    }

                    return OperationResult<ImageUploadResultDTO>.Failure(
                        uploadResult.Error.Message ?? "Failed to upload avatar", 500);
                }

                var result = new ImageUploadResultDTO
                {
                    PublicId = uploadResult.PublicId,
                    Url = uploadResult.SecureUrl.ToString(),
                    Width = uploadResult.Width,
                    Height = uploadResult.Height,
                    Format = uploadResult.Format,
                    ResourceType = uploadResult.ResourceType
                };

                return OperationResult<ImageUploadResultDTO>.Successful(
                    result,
                    "Avatar uploaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading avatar to Cloudinary");
                if (_environment.IsDevelopment())
                {
                    try
                    {
                        return await SaveLocalImageAsync(file, folder);
                    }
                    catch (Exception localEx)
                    {
                        _logger.LogError(localEx, "Local avatar fallback also failed");
                    }
                }

                return OperationResult<ImageUploadResultDTO>.Failure(
                    "An error occurred while uploading the avatar", 500);
            }
        }

        public async Task<OperationResult<string>> DeleteImageAsync(string publicId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(publicId))
                {
                    return OperationResult<string>.Failure("Public ID is required", 400);
                }

                if (publicId.StartsWith("local/", StringComparison.OrdinalIgnoreCase))
                {
                    var relative = publicId["local/".Length..];
                    var webRoot = ResolveWebRoot();
                    var filePath = Path.Combine(webRoot, "uploads", relative.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    return OperationResult<string>.Successful(
                        publicId,
                        "Local image deleted successfully");
                }

                if (_cloudinary == null)
                {
                    return OperationResult<string>.Successful(publicId, "No remote image to delete");
                }

                var deleteParams = new DeletionParams(publicId)
                {
                    ResourceType = ResourceType.Image
                };

                var result = await _cloudinary.DestroyAsync(deleteParams);

                if (result.Result == "ok")
                {
                    return OperationResult<string>.Successful(
                        publicId,
                        "Image deleted successfully");
                }

                _logger.LogWarning("Cloudinary delete failed: {Result}", result.Result);
                return OperationResult<string>.Failure(
                    $"Failed to delete image: {result.Result}", 500);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting image from Cloudinary");
                return OperationResult<string>.Failure(
                    "An error occurred while deleting the image", 500);
            }
        }

        public async Task<OperationResult<List<ImageUploadResultDTO>>> UploadMultipleImagesAsync(
            IFormFileCollection files,
            string folder = "properties")
        {
            try
            {
                if (files == null || files.Count == 0)
                {
                    return OperationResult<List<ImageUploadResultDTO>>.Failure(
                        "No files provided", 400);
                }

                if (files.Count > 12)
                {
                    return OperationResult<List<ImageUploadResultDTO>>.Failure(
                        "Maximum 12 images allowed per upload", 400);
                }

                var results = new List<ImageUploadResultDTO>();
                var errors = new List<string>();

                foreach (var file in files)
                {
                    var uploadResult = await UploadImageWithThumbnailAsync(file, folder);

                    if (uploadResult.Success && uploadResult.Data != null)
                    {
                        results.Add(uploadResult.Data);
                    }
                    else
                    {
                        errors.Add($"{file.FileName}: {uploadResult.Message}");
                    }
                }

                if (results.Count == 0)
                {
                    return OperationResult<List<ImageUploadResultDTO>>.Failure(
                        "Failed to upload any images", 500, errors);
                }

                var message = results.Count == files.Count
                    ? "All images uploaded successfully"
                    : $"{results.Count} of {files.Count} images uploaded successfully";

                return OperationResult<List<ImageUploadResultDTO>>.Successful(
                    results, message, 200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading multiple images");
                return OperationResult<List<ImageUploadResultDTO>>.Failure(
                    "An error occurred while uploading images", 500);
            }
        }

        private async Task<OperationResult<ImageUploadResultDTO>> SaveLocalImageAsync(
            IFormFile file,
            string folder)
        {
            var validation = ValidateImageFile(file);
            if (!validation.Success)
            {
                return OperationResult<ImageUploadResultDTO>.Failure(validation.Message, 400);
            }

            var safeFolder = string.Join(
                "/",
                folder
                    .Replace('\\', '/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part =>
                    {
                        var cleaned = string.Concat(part.Where(ch =>
                            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));
                        return string.IsNullOrWhiteSpace(cleaned) ? "misc" : cleaned;
                    }));

            if (string.IsNullOrWhiteSpace(safeFolder))
            {
                safeFolder = "properties";
            }

            var webRoot = ResolveWebRoot();
            var uploadsRoot = Path.Combine(webRoot, "uploads", safeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(uploadsRoot);

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }

            extension = extension.ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsRoot, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/{safeFolder}/{fileName}";
            var publicUrl = BuildPublicUrl(relativePath);
            var publicId = $"local/{safeFolder}/{fileName}";

            _logger.LogInformation("Saved local upload {PublicId} -> {Url}", publicId, publicUrl);

            return OperationResult<ImageUploadResultDTO>.Successful(
                new ImageUploadResultDTO
                {
                    PublicId = publicId,
                    Url = publicUrl,
                    ThumbnailUrl = publicUrl,
                    Format = extension.TrimStart('.'),
                    ResourceType = "image"
                },
                "Image uploaded locally");
        }

        private string ResolveWebRoot()
        {
            if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
            {
                Directory.CreateDirectory(_environment.WebRootPath);
                return _environment.WebRootPath;
            }

            var fallback = Path.Combine(_environment.ContentRootPath, "wwwroot");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private string BuildPublicUrl(string relativePath)
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request != null)
            {
                return $"{request.Scheme}://{request.Host}{relativePath}";
            }

            var configured = _configuration["App:PublicBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return $"{configured.TrimEnd('/')}{relativePath}";
            }

            return relativePath;
        }

        private static bool ShouldUseLocalStorage(
            string? cloudName,
            string? apiKey,
            string? apiSecret,
            IWebHostEnvironment environment)
        {
            if (IsPlaceholder(cloudName) || IsPlaceholder(apiKey) || IsPlaceholder(apiSecret))
            {
                return true;
            }

            // Development without real credentials → local disk.
            return environment.IsDevelopment()
                && (string.IsNullOrWhiteSpace(apiKey) || LooksLikeDevPlaceholder(apiKey));
        }

        private static bool IsPlaceholder(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return LooksLikeDevPlaceholder(value);
        }

        private static bool LooksLikeDevPlaceholder(string value)
        {
            var normalized = value.Trim().ToLowerInvariant();
            return normalized.Contains("local-dev")
                || normalized.Contains("your-")
                || normalized.Contains("changeme")
                || normalized.Contains("example")
                || normalized is "test" or "demo" or "xxx";
        }

        private OperationResult<string> ValidateImageFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return OperationResult<string>.Failure("File is empty", 400);
            }

            const long maxFileSize = 10 * 1024 * 1024;
            if (file.Length > maxFileSize)
            {
                return OperationResult<string>.Failure(
                    "File size must not exceed 10MB", 400);
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            var allowedContentTypes = new[]
            {
                "image/jpeg",
                "image/jpg",
                "image/png",
                "image/webp",
                "image/gif"
            };

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();

            // React Native FormData sometimes omits Content-Type; allow by extension then.
            var extensionOk = allowedExtensions.Contains(extension);
            var contentTypeOk = string.IsNullOrWhiteSpace(contentType)
                || allowedContentTypes.Contains(contentType);

            if (!extensionOk || !contentTypeOk)
            {
                return OperationResult<string>.Failure(
                    "Invalid file type. Only JPG, PNG, WEBP, and GIF are allowed", 400);
            }

            return OperationResult<string>.Successful("Valid");
        }
    }
}
