using Cronos;
using Habitera.Services;
using Microsoft.Extensions.Options;

namespace Habitera.Background
{
    public class ListingModerationOptions
    {
        public const string SectionName = "ListingModeration";

        /// <summary>
        /// Auto-approve pending listings after this many minutes with no admin action.
        /// </summary>
        public int AutoApproveAfterMinutes { get; set; } = 30;

        /// <summary>
        /// Cron expression (UTC) for checking unapproved listings.
        /// Default: every 5 minutes.
        /// </summary>
        public string AutoApproveCron { get; set; } = "*/5 * * * *";
    }

    /// <summary>
    /// Cron-scheduled job that auto-approves listings still pending past the configured timeout.
    /// </summary>
    public class ListingAutoApproveJob : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ListingAutoApproveJob> _logger;
        private readonly ListingModerationOptions _options;
        private readonly CronExpression _cron;

        public ListingAutoApproveJob(
            IServiceProvider serviceProvider,
            IOptions<ListingModerationOptions> options,
            ILogger<ListingAutoApproveJob> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;

            try
            {
                _cron = CronExpression.Parse(
                    string.IsNullOrWhiteSpace(_options.AutoApproveCron)
                        ? "*/5 * * * *"
                        : _options.AutoApproveCron.Trim());
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Invalid ListingModeration:AutoApproveCron '{Cron}'. Falling back to */5 * * * *",
                    _options.AutoApproveCron);
                _cron = CronExpression.Parse("*/5 * * * *");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var autoApproveAfter = TimeSpan.FromMinutes(Math.Max(1, _options.AutoApproveAfterMinutes));

            _logger.LogInformation(
                "Listing auto-approve cron job started. Cron={Cron} (UTC), Timeout={TimeoutMinutes}m",
                _options.AutoApproveCron,
                autoApproveAfter.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var next = _cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

                    if (next == null)
                    {
                        _logger.LogWarning("Cron expression produced no next occurrence; stopping job.");
                        break;
                    }

                    var delay = next.Value - now;
                    if (delay > TimeSpan.Zero)
                    {
                        _logger.LogDebug("Next listing auto-approve run at {NextRunUtc}", next.Value);
                        await Task.Delay(delay, stoppingToken);
                    }

                    await RunAutoApproveAsync(autoApproveAfter, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in listing auto-approve cron job");
                    // Avoid tight failure loops if something unexpected breaks scheduling.
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        private async Task RunAutoApproveAsync(TimeSpan autoApproveAfter, CancellationToken stoppingToken)
        {
            stoppingToken.ThrowIfCancellationRequested();

            using var scope = _serviceProvider.CreateScope();
            var propertyService = scope.ServiceProvider.GetRequiredService<IPropertyService>();
            var approved = await propertyService.AutoApproveDueListingsAsync(autoApproveAfter);

            if (approved > 0)
            {
                _logger.LogInformation("Auto-approved {Count} listing(s)", approved);
            }
            else
            {
                _logger.LogDebug("Listing auto-approve cron tick: no due listings");
            }
        }
    }
}
