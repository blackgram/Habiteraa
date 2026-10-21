using System.Globalization;
using System.Text.Json;
using Habitera.DTOs;

namespace Habitera.Services
{
    public interface ILocationSuggestionService
    {
        Task<OperationResult<List<LocationSuggestionDTO>>> SuggestAsync(string query, int limit = 8);
    }

    public class LocationSuggestionService : ILocationSuggestionService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LocationSuggestionService> _logger;

        public LocationSuggestionService(
            IHttpClientFactory httpClientFactory,
            ILogger<LocationSuggestionService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<OperationResult<List<LocationSuggestionDTO>>> SuggestAsync(
            string query,
            int limit = 8)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                return OperationResult<List<LocationSuggestionDTO>>.Successful(
                    new List<LocationSuggestionDTO>());
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Nominatim");
                var encoded = Uri.EscapeDataString(query.Trim());
                var url =
                    $"search?q={encoded}&format=jsonv2&addressdetails=1&limit={Math.Clamp(limit, 1, 15)}" +
                    "&countrycodes=ng&viewbox=2.6,4.2,14.8,13.9&bounded=0";

                using var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Nominatim suggest failed with {StatusCode}",
                        response.StatusCode);
                    return OperationResult<List<LocationSuggestionDTO>>.Failure(
                        "Unable to fetch location suggestions",
                        (int)response.StatusCode);
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);
                var suggestions = new List<LocationSuggestionDTO>();

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("lat", out var latEl) ||
                        !item.TryGetProperty("lon", out var lonEl))
                        continue;

                    if (!double.TryParse(
                            latEl.GetString(),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var lat) ||
                        !double.TryParse(
                            lonEl.GetString(),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var lon))
                        continue;

                    string? city = null;
                    string? state = null;
                    string? country = null;

                    if (item.TryGetProperty("address", out var address) &&
                        address.ValueKind == JsonValueKind.Object)
                    {
                        city = FirstAddressValue(address, "city", "town", "village", "suburb", "county");
                        state = FirstAddressValue(address, "state");
                        country = FirstAddressValue(address, "country");
                    }

                    var displayName = item.TryGetProperty("display_name", out var display)
                        ? display.GetString()
                        : null;

                    var label = BuildLabel(city, state, country, displayName);
                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    suggestions.Add(new LocationSuggestionDTO
                    {
                        Id = item.TryGetProperty("place_id", out var placeId)
                            ? placeId.ToString()
                            : $"{lat:F5},{lon:F5}",
                        Label = label,
                        City = city,
                        State = state,
                        Country = country ?? "Nigeria",
                        Latitude = lat,
                        Longitude = lon
                    });
                }

                return OperationResult<List<LocationSuggestionDTO>>.Successful(suggestions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error suggesting locations for query {Query}", query);
                return OperationResult<List<LocationSuggestionDTO>>.Failure(
                    "An error occurred while searching locations",
                    500);
            }
        }

        private static string? FirstAddressValue(JsonElement address, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (address.TryGetProperty(key, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private static string BuildLabel(
            string? city,
            string? state,
            string? country,
            string? displayName)
        {
            var parts = new[] { city, state, country }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parts.Count >= 2)
                return string.Join(", ", parts);

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                var compact = string.Join(
                    ", ",
                    displayName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Take(3));
                if (!string.IsNullOrWhiteSpace(compact))
                    return compact;
            }

            return parts.FirstOrDefault() ?? string.Empty;
        }
    }
}
