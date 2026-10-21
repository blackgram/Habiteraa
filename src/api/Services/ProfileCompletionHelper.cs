using Habitera.DTOs;
using Habitera.Models;

namespace Habitera.Services
{
    public static class ProfileCompletionHelper
    {
        public static (List<string> MissingFields, int Percentage) Evaluate(ApplicationUser user)
        {
            var missing = new List<string>();

            // Email is collected at registration and should always count toward progress.
            if (string.IsNullOrWhiteSpace(user.Email))
                missing.Add("Email");

            if (user.UserType == UserType.Agent)
            {
                if (string.IsNullOrWhiteSpace(user.Profile?.FirstName))
                    missing.Add("FirstName");
                if (string.IsNullOrWhiteSpace(user.Profile?.LastName))
                    missing.Add("LastName");
                if (string.IsNullOrWhiteSpace(user.Profile?.City))
                    missing.Add("City");
                if (string.IsNullOrWhiteSpace(user.Profile?.State))
                    missing.Add("State");
                if (string.IsNullOrWhiteSpace(user.Profile?.Country))
                    missing.Add("Country");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(user.Profile?.FirstName))
                    missing.Add("FirstName");
                if (string.IsNullOrWhiteSpace(user.Profile?.LastName))
                    missing.Add("LastName");
                if (string.IsNullOrWhiteSpace(user.Profile?.City))
                    missing.Add("City");
                if (string.IsNullOrWhiteSpace(user.Profile?.State))
                    missing.Add("State");
                if (string.IsNullOrWhiteSpace(user.Profile?.Country))
                    missing.Add("Country");
                if (user.Profile?.Location == null ||
                    (user.Profile.Location.X == 0 && user.Profile.Location.Y == 0))
                    missing.Add("Location");
            }

            var totalFields = user.UserType == UserType.Agent ? 6 : 7;
            var completedFields = totalFields - missing.Count;
            var percentage = (int)((completedFields / (double)totalFields) * 100);
            return (missing, percentage);
        }

        public static void ApplyToDto(ApplicationUser user, ApplicationUserDTO dto)
        {
            var (_, percentage) = Evaluate(user);
            dto.CompletionPercentage = percentage;
        }

        public static bool TryMarkCompleted(ApplicationUser user)
        {
            var (missing, _) = Evaluate(user);
            if (missing.Count > 0 || user.ProfileCompleted)
                return false;

            user.ProfileCompleted = true;
            user.ProfileCompletedAt = DateTime.UtcNow;
            return true;
        }
    }
}
