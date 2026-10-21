using Habitera.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Habitera.Data
{
    public class ApplicationDbContext
        : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<AgentProfile> AgentProfiles { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        public DbSet<Property> Properties { get; set; }
        public DbSet<PropertyImage> PropertyImages { get; set; }
        public DbSet<PropertyAmenity> PropertyAmenities { get; set; }
        public DbSet<Favorite> Favorites { get; set; }
        public DbSet<ViewingBooking> ViewingBookings { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.UserType)
                      .HasConversion<int>()
                      .IsRequired();

                entity.Property(u => u.Status)
                      .HasConversion<int>()
                      .IsRequired();

                entity.HasIndex(u => u.UserType);
                entity.HasIndex(u => u.Status);
            });

            builder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(p => p.UserId);

                entity.HasOne(p => p.User)
                      .WithOne(u => u.Profile)
                      .HasForeignKey<UserProfile>(p => p.UserId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(p => p.FirstName)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(p => p.LastName)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(p => p.City)
                      .HasMaxLength(100);

                entity.Property(p => p.State)
                      .HasMaxLength(100);

                entity.Property(p => p.Country)
                      .HasMaxLength(100);


                entity.Property(p => p.Location);
            });

            builder.Entity<AgentProfile>(entity =>
            {
                entity.HasKey(a => a.UserId);

                entity.HasOne(a => a.User)
                      .WithOne(u => u.AgentProfile)
                      .HasForeignKey<AgentProfile>(a => a.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(a => a.AgencyName)
                      .HasMaxLength(200);

                entity.HasIndex(a => a.LicenseNumber)
                      .IsUnique()
                      .HasFilter("\"LicenseNumber\" IS NOT NULL");
            });

            builder.Entity<Property>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
                entity.Property(e => e.AgentId).IsRequired();
                entity.Property(e => e.City).HasMaxLength(100).IsRequired();
                entity.Property(e => e.State).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Country).HasMaxLength(100).IsRequired();

                entity.Property(e => e.Price).HasPrecision(18, 2);
                entity.Property(e => e.Latitude).HasPrecision(10, 8);
                entity.Property(e => e.Longitude).HasPrecision(11, 8);
                entity.Property(e => e.Bathrooms).HasPrecision(3, 1);
                entity.Property(e => e.SquareFeet).HasPrecision(10, 2);

                entity.Property(e => e.RejectionReason).HasMaxLength(500);

                entity.HasIndex(e => e.AgentId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.ReviewSubmittedAt);
                entity.HasIndex(e => new { e.City, e.State, e.Country });
                entity.HasIndex(e => e.Price);
                entity.HasIndex(e => e.CreatedAt);

                entity.HasOne<ApplicationUser>()
                      .WithMany()
                      .HasForeignKey(p => p.AgentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PropertyImage>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ImageUrl).HasMaxLength(500).IsRequired();

                entity.HasOne(e => e.Property)
                    .WithMany(p => p.Images)
                    .HasForeignKey(e => e.PropertyId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.PropertyId);
            });

            builder.Entity<PropertyAmenity>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Amenities)
                    .HasColumnType("jsonb");

                entity.HasOne(e => e.Property)
                    .WithMany(p => p.Amenities)
                    .HasForeignKey(e => e.PropertyId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.PropertyId);
            });

            builder.Entity<Conversation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.LastMessagePreview).HasMaxLength(200);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Agent)
                    .WithMany()
                    .HasForeignKey(e => e.AgentId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Property)
                    .WithMany()
                    .HasForeignKey(e => e.PropertyId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => new { e.UserId, e.AgentId, e.PropertyId }).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.AgentId);
                entity.HasIndex(e => e.LastMessageAt);
            });

            builder.Entity<ChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Body).HasMaxLength(4000).IsRequired();

                entity.HasOne(e => e.Conversation)
                    .WithMany(c => c.Messages)
                    .HasForeignKey(e => e.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.ConversationId, e.CreatedAt });
                entity.HasIndex(e => e.SenderId);
            });

            builder.Entity<Favorite>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.UserId).IsRequired();

                entity.HasOne(e => e.Property)
                    .WithMany(p => p.Favorites)
                    .HasForeignKey(e => e.PropertyId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.UserId, e.PropertyId }).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.PropertyId);

                entity.HasOne<ApplicationUser>()
                      .WithMany()
                      .HasForeignKey(f => f.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ViewingBooking>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.AgentId).IsRequired();

                entity.HasOne(e => e.Property)
                    .WithMany(p => p.ViewingBookings)
                    .HasForeignKey(e => e.PropertyId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.PropertyId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.AgentId);
                entity.HasIndex(e => e.ScheduledDate);

                entity.HasOne<ApplicationUser>()
                      .WithMany()
                      .HasForeignKey(vb => vb.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<ApplicationUser>()
                      .WithMany()
                      .HasForeignKey(vb => vb.AgentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            UpdateTimestamps();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            UpdateTimestamps();
            return base.SaveChanges();
        }

        private void UpdateTimestamps()
        {
            var entries = ChangeTracker.Entries();
            var now = DateTime.UtcNow;

            foreach (var entry in entries)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        if (entry.Entity is Property property)
                        {
                            property.CreatedAt = now;
                            property.UpdatedAt = now;
                        }
                        else if (entry.Entity is ViewingBooking booking)
                        {
                            booking.CreatedAt = now;
                            booking.UpdatedAt = now;
                        }
                        else if (entry.Entity is ApplicationUser user)
                        {
                            user.CreatedAt = now;
                            user.UpdatedAt = now;
                        }
                        break;

                    case EntityState.Modified:
                        if (entry.Entity is Property prop)
                            prop.UpdatedAt = now;
                        else if (entry.Entity is ViewingBooking bk)
                            bk.UpdatedAt = now;
                        else if (entry.Entity is ApplicationUser usr)
                            usr.UpdatedAt = now;
                        break;
                }
            }
        }
    }

}