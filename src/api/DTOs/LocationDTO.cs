namespace Habitera.DTOs
{
    public class LocationSuggestionDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class LocationListingsDTO
    {
        public LocationSuggestionDTO Selected { get; set; } = null!;
        public List<PropertyDTO> Local { get; set; } = new();
        public List<PropertyDTO> Nearby { get; set; } = new();
    }
}
