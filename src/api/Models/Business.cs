using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;
using NetTopologySuite;

namespace Habitera.Models
{
    public class Property
    {
        public Guid Id { get; set; }
        public Guid AgentId { get; set; }

        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;

        public PropertyTenor Tenor { get; set; } = PropertyTenor.Annually;
        public PropertyType PropertyType { get; set; }
        public ListingType ListingType { get; set; }

        public string Street { get; set; } = null!;
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string Country { get; set; } = null!;
        public string PostalCode { get; set; } = null!;

        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }

        public int Bedrooms { get; set; } = 0;
        public decimal Bathrooms { get; set; } = 0;
        public decimal SquareFeet { get; set; } = 0;
        public decimal LotSize { get; set; } = 0;
        public int? YearBuilt { get; set; }

        public decimal Price { get; set; }
        public string Currency { get; set; } = "NGN";

        public PropertyStatus Status { get; set; }
        public bool IsPublished { get; set; }
        public bool IsFeatured { get; set; }

        public int ViewCount { get; set; }
        public int FavoriteCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }

        /// <summary>
        /// When the agent submitted the listing for admin review.
        /// Null means draft / not yet submitted.
        /// </summary>
        public DateTime? ReviewSubmittedAt { get; set; }

        public string? RejectionReason { get; set; }

        public ICollection<PropertyImage> Images { get; set; } = new List<PropertyImage>();
        public ICollection<PropertyAmenity> Amenities { get; set; } = new List<PropertyAmenity>();
        public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
        public ICollection<ViewingBooking> ViewingBookings { get; set; } = new List<ViewingBooking>();
    }

    public class PropertyImage
    {
        public Guid Id { get; set; }
        public Guid PropertyId { get; set; }

        public string ImageUrl { get; set; } = null!;
        public string? ThumbnailUrl { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsPrimary { get; set; }

        public DateTime CreatedAt { get; set; }

        public Property Property { get; set; } = null!;
    }

    public class PropertyAmenity
    {
        public Guid Id { get; set; }
        public Guid PropertyId { get; set; }

        public AmenityCategory Category { get; set; }
        public Dictionary<string, object> Amenities { get; set; } = new();

        public Property Property { get; set; } = null!;
    }

    public class Favorite
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid PropertyId { get; set; }

        public DateTime CreatedAt { get; set; }

        public Property Property { get; set; } = null!;
    }

    public class ViewingBooking
    {
        public Guid Id { get; set; }
        public Guid PropertyId { get; set; }
        public Guid UserId { get; set; }
        public Guid AgentId { get; set; }

        public DateTime ScheduledDate { get; set; }
        public BookingStatus Status { get; set; }

        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public string? UserPhone { get; set; }
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Property Property { get; set; } = null!;
    }
    public class SavedSearch
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
        public string SearchCriteria { get; set; } = null!; // JSON serialized
        public bool AlertsEnabled { get; set; }
        public AlertFrequency Frequency { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastAlertSent { get; set; }
    }
    public class PropertyPriceHistory
    {
        public Guid Id { get; set; }
        public Guid PropertyId { get; set; }
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }
        public DateTime ChangedAt { get; set; }
        public Property Property { get; set; } = null!;
    }
    public class PriceAnalytics
    {
        public decimal CurrentPrice { get; set; }
        public decimal? PriceChange { get; set; }
        public decimal? PercentageChange { get; set; }
        //public List<PricePoint> History { get; set; } = new();
        public decimal? EstimatedValue { get; set; }
        public string? Recommendation { get; set; }
    }
    public class MarketTrends
    {
        public string Location { get; set; } = null!;
        public decimal AveragePrice { get; set; }
        public decimal MedianPrice { get; set; }
        public int TotalListings { get; set; }
        public decimal AveragePricePerSqFt { get; set; }
        public Dictionary<string, decimal> PriceByPropertyType { get; set; } = new();
    }
    public enum AlertFrequency
    {
        Immediate = 0,
        Daily = 1,
        Weekly = 2
    }

    public enum PropertyType
    {
        House = 0,
        Apartment = 1,
        Condo = 2,
        Townhouse = 3,
        Land = 4,
        Commercial =5
    }

    public enum PropertyTenor
    {
        Daily = 0,
        Weekly = 1,
        BiWeekly = 2,
        Monthly = 3,
        Annually = 4,
        BiAnnually = 5,
        Outright = 6
    }

    public enum ListingType
    {
        ForSale = 0,
        ForRent = 1
    }

    public enum PropertyStatus
    {
        Active = 0,
        Pending = 1,
        Sold = 2,
        Inactive = 3,
        Withdrawn = 4,
        Expired = 5,
        Deleted = 6
    }

    public enum AmenityCategory
    {
        Interior = 0,
        Exterior = 1,
        Community = 2,
        Utilities = 3
    }

    public enum BookingStatus
    {
        Pending = 0,
        Confirmed = 1,
        Cancelled = 2,
        Completed = 3
    }
}