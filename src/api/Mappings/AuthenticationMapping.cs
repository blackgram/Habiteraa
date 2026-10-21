using AutoMapper;
using Habitera.Models;
using Habitera.DTOs;
using Habitera.Services;

namespace Habitera.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Map ApplicationUser to RegularUserDTO
            CreateMap<ApplicationUser, RegularUserDTO>()
                .IncludeBase<ApplicationUser, ApplicationUserDTO>()
                .ForMember(dest => dest.FirstName, opt => opt.MapFrom(src => src.Profile.FirstName))
                .ForMember(dest => dest.LastName, opt => opt.MapFrom(src => src.Profile.LastName))
                .ForMember(dest => dest.PhoneNumber, opt => opt.MapFrom(src => src.PhoneNumber))
                .ForMember(dest => dest.City, opt => opt.MapFrom(src => src.Profile.City))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.Profile.State))
                .ForMember(dest => dest.Country, opt => opt.MapFrom(src => src.Profile.Country))
                .ForMember(dest => dest.Latitude, opt => opt.MapFrom(src =>
                    src.Profile.Location != null && !double.IsNaN(src.Profile.Location.Y)
                        ? src.Profile.Location.Y
                        : (double?)null))
                .ForMember(dest => dest.Longitude, opt => opt.MapFrom(src =>
                    src.Profile.Location != null && !double.IsNaN(src.Profile.Location.X)
                        ? src.Profile.Location.X
                        : (double?)null))
                .ForMember(dest => dest.PreferredLanguage, opt => opt.MapFrom(src => src.Profile.PreferredLanguage))
                .ForMember(dest => dest.EmailNotifications, opt => opt.MapFrom(src => src.Profile.EmailNotifications))
                .ForMember(dest => dest.PushNotifications, opt => opt.MapFrom(src => src.Profile.PushNotifications))
                .AfterMap((src, dest) => ProfileCompletionHelper.ApplyToDto(src, dest));

            // Map ApplicationUser to AgentUserDTO
            CreateMap<ApplicationUser, AgentUserDTO>()
                .IncludeBase<ApplicationUser, ApplicationUserDTO>()
                 .ForMember(dest => dest.FirstName, opt => opt.MapFrom(src => src.Profile.FirstName))
                .ForMember(dest => dest.LastName, opt => opt.MapFrom(src => src.Profile.LastName))
                .ForMember(dest => dest.PhoneNumber, opt => opt.MapFrom(src => src.PhoneNumber))
                .ForMember(dest => dest.City, opt => opt.MapFrom(src => src.Profile.City))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.Profile.State))
                .ForMember(dest => dest.Country, opt => opt.MapFrom(src => src.Profile.Country))
                .ForMember(dest => dest.LicenseNumber, opt => opt.MapFrom(src =>
                    src.AgentProfile != null ? src.AgentProfile.LicenseNumber : null))
                .ForMember(dest => dest.AgencyName, opt => opt.MapFrom(src =>
                    src.AgentProfile != null ? src.AgentProfile.AgencyName : null))
                .ForMember(dest => dest.AverageRating, opt => opt.MapFrom(src =>
                    src.AgentProfile != null ? src.AgentProfile.AverageRating : 0))
                .ForMember(dest => dest.TotalReviews, opt => opt.MapFrom(src =>
                    src.AgentProfile != null ? src.AgentProfile.TotalReviews : 0))
                .AfterMap((src, dest) => ProfileCompletionHelper.ApplyToDto(src, dest));

            // Base mapping for common properties
            CreateMap<ApplicationUser, ApplicationUserDTO>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email ?? string.Empty))
                .ForMember(dest => dest.FirstName, opt => opt.MapFrom(src => src.Profile.FirstName ?? string.Empty))
                .ForMember(dest => dest.LastName, opt => opt.MapFrom(src => src.Profile.LastName ?? string.Empty))
                .ForMember(dest => dest.PhoneNumber, opt => opt.MapFrom(src => src.PhoneNumber ?? string.Empty))
                .ForMember(dest => dest.ProfilePhoto, opt => opt.MapFrom(src => src.ProfilePhoto))
                .ForMember(dest => dest.UserType, opt => opt.MapFrom(src => src.UserType))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => src.UpdatedAt))
                .ForMember(dest => dest.LastLoginAt, opt => opt.MapFrom(src => src.LastLoginAt))
                .ForMember(dest => dest.ProfileCompleted, opt => opt.MapFrom(src => src.ProfileCompleted))
                .ForMember(dest => dest.ProfileCompletedAt, opt => opt.MapFrom(src => src.ProfileCompletedAt))
                .AfterMap((src, dest) => ProfileCompletionHelper.ApplyToDto(src, dest));
        }
    }
}