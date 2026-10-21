using Elastic.Clients.Elasticsearch;
using Habitera.Models;
using System.ComponentModel.DataAnnotations;

namespace Habitera.DTOs
{
    public class PropertyDTO
    {
        public Guid Id { get; set; }
        public Guid AgentId { get; set; }

        public string AgentName { get; set; } = string.Empty;

        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;

        public PropertyType PropertyType { get; set; }
        public ListingType ListingType { get; set; }
        public PropertyTenor Tenor { get; set; }

        public string Street { get; set; } = null!;
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string Country { get; set; } = null!;
        public string PostalCode { get; set; } = null!;
        public string FullAddress { get; set; } = null!;

        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }

        /// <summary>
        /// Distance in km from the requested reference point (when geo-sorted).
        /// </summary>
        public double? DistanceKm { get; set; }

        public GeoLocation? Location { get; set; }

        public int Bedrooms { get; set; } = 0;
        public decimal Bathrooms { get; set; } = 0;
        public decimal SquareFeet { get; set; } = 0;
        public decimal LotSize { get; set; } = 0;
        public int? YearBuilt { get; set; }

        public decimal Price { get; set; }
        public string Currency { get; set; } = "NGN";

        public string Status { get; set; } = null!;
        public bool IsPublished { get; set; }
        public bool IsFeatured { get; set; }

        public bool IsFavorited { get; set; }

        public int ViewCount { get; set; }
        public int FavoriteCount { get; set; }
        public int ContactCount { get; set; }
        public List<PropertyImageDTO> Images { get; set; } = new();
        public string? PrimaryImageUrl { get; set; }

        public List<PropertyAmenityDTO> Amenities { get; set; } = new();

        public List<string> AmenityTags { get; set; } = new();
        public Dictionary<string, object> AmenitiesData { get; set; } = new();

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public DateTime? ReviewSubmittedAt { get; set; }
        public string? RejectionReason { get; set; }

        public double PricePerSquareFoot { get; set; }
        public int DaysOnMarket { get; set; }
    }

    public class RejectListingRequestDTO
    {
        [MaxLength(500)]
        public string? Reason { get; set; }
    }


    public class CreatePropertyDTO
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        //[Required]
        public string Description { get; set; } = string.Empty;

        [Required]
        public PropertyType PropertyType { get; set; }

        [Required]
        public ListingType ListingType { get; set; }

        public PropertyTenor Tenor { get; set; } = PropertyTenor.Annually;

        [Required]
        public string Street { get; set; } = string.Empty;

        [Required]
        public string City { get; set; } = string.Empty;

        [Required]
        public string State { get; set; } = string.Empty;

        [Required]
        public string Country { get; set; } = string.Empty;

        [Required]
        public string PostalCode { get; set; } = string.Empty;

        [Required]
        public decimal Latitude { get; set; }

        [Required]
        public decimal Longitude { get; set; }

        public PropertyAmenityDTO? Amenities { get; set; }

        public int Bedrooms { get; set; }
        public decimal Bathrooms { get; set; }
        public decimal SquareFeet { get; set; }
        public decimal LotSize { get; set; }
        public int? YearBuilt { get; set; }

        [Required]
        public decimal Price { get; set; }

        public string Currency { get; set; } = "NGN";
    }

    public class UpdatePropertyDTO
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public PropertyType? PropertyType { get; set; }
        public ListingType? ListingType { get; set; }
        public PropertyTenor? Tenor { get; set; }
        public decimal? Price { get; set; }
        public int? Bedrooms { get; set; }
        public decimal? Bathrooms { get; set; }
        public decimal? SquareFeet { get; set; }
    }

    public class PropertyImageDTO
    {
        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class AddPropertyImageDTO
    {
        [Required]
        public string ImageUrl { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class PropertyAmenityDTO
    {
        public AmenityCategory Category { get; set; }
        public Dictionary<string, object> Amenities { get; set; } = new();
    }

    public class AgentDashboardDTO
    {
        public int TotalProperties { get; set; }
        public int ActiveProperties { get; set; }
        public int PendingProperties { get; set; }
        public int SoldProperties { get; set; }
        public List<PropertyDTO> RecentProperties { get; set; } = new();
        public Dictionary<PropertyStatus, int> PropertyStatusBreakdown { get; set; } = new();
    }

    public class PropertyAnalyticsDTO
    {
        public Guid PropertyId { get; set; }
        public int TotalViews { get; set; }
        public int TotalFavorites { get; set; }
        public int TotalBookings { get; set; }
        public int DaysOnMarket { get; set; }
        public List<BookingDTO> RecentBookings { get; set; } = new();
    }

}
