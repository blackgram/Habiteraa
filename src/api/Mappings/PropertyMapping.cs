using AutoMapper;
using Habitera.Models;
using Habitera.DTOs;

namespace Habitera.Mappings
{
    public class PropertyMappingProfile : Profile
    {
        public PropertyMappingProfile()
        {
            CreateMap<Property, PropertyDTO>()
                .ForMember(dest => dest.AgentName, opt => opt.Ignore()) // Will be set manually if needed
                .ForMember(dest => dest.FullAddress, opt => opt.MapFrom(src =>
                    BuildFullAddress(src)))
                .ForMember(dest => dest.IsFavorited, opt => opt.Ignore()) // Will be set manually based on user
                .ForMember(dest => dest.Images, opt => opt.MapFrom(src =>
                    src.Images.OrderBy(i => i.DisplayOrder)))
                .ForMember(dest => dest.PrimaryImageUrl, opt => opt.MapFrom(src =>
                    GetPrimaryImageUrl(src)))
                .ForMember(dest => dest.Location, opt => opt.Ignore())
                .ForMember(dest => dest.Amenities, opt => opt.MapFrom(src => src.Amenities))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()));

            CreateMap<Property, PropertySummaryDTO>()
                .ForMember(dest => dest.FullAddress, opt => opt.MapFrom(src =>
                    BuildFullAddress(src)))
                .ForMember(dest => dest.PrimaryImageUrl, opt => opt.MapFrom(src =>
                    GetPrimaryImageUrl(src)));

            CreateMap<CreatePropertyDTO, Property>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.AgentId, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => PropertyStatus.Pending))
                .ForMember(dest => dest.IsPublished, opt => opt.MapFrom(src => false))
                .ForMember(dest => dest.IsFeatured, opt => opt.MapFrom(src => false))
                .ForMember(dest => dest.ViewCount, opt => opt.MapFrom(src => 0))
                .ForMember(dest => dest.FavoriteCount, opt => opt.MapFrom(src => 0))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.PublishedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ReviewSubmittedAt, opt => opt.Ignore())
                .ForMember(dest => dest.RejectionReason, opt => opt.Ignore())
                .ForMember(dest => dest.Images, opt => opt.Ignore())
                .ForMember(dest => dest.Amenities, opt => opt.Ignore())
                .ForMember(dest => dest.Favorites, opt => opt.Ignore())
                .ForMember(dest => dest.ViewingBookings, opt => opt.Ignore());

            CreateMap<UpdatePropertyDTO, Property>()
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            CreateMap<PropertyImage, PropertyImageDTO>();

            CreateMap<AddPropertyImageDTO, PropertyImage>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.PropertyId, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.Property, opt => opt.Ignore());

            CreateMap<PropertyAmenity, PropertyAmenityDTO>();

            CreateMap<PropertyAmenityDTO, PropertyAmenity>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.PropertyId, opt => opt.Ignore())
                .ForMember(dest => dest.Property, opt => opt.Ignore());
        }

        private static string BuildFullAddress(Property property)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(property.Street))
                parts.Add(property.Street);
            if (!string.IsNullOrWhiteSpace(property.City))
                parts.Add(property.City);
            if (!string.IsNullOrWhiteSpace(property.State))
                parts.Add(property.State);
            if (!string.IsNullOrWhiteSpace(property.PostalCode))
                parts.Add(property.PostalCode);
            if (!string.IsNullOrWhiteSpace(property.Country))
                parts.Add(property.Country);

            return string.Join(", ", parts);
        }

        private static string? GetPrimaryImageUrl(Property property)
        {
            var primaryImage = property.Images?.FirstOrDefault(i => i.IsPrimary);
            if (primaryImage != null)
                return primaryImage.ImageUrl;

            var firstImage = property.Images?.OrderBy(i => i.DisplayOrder).FirstOrDefault();
            return firstImage?.ImageUrl;
        }
    }
}