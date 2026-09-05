using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class DashboardDbContext(DbContextOptions<DashboardDbContext> options) : DbContext(options)
{
    public DbSet<SpeedTestJobEntity> SpeedTestJobs => Set<SpeedTestJobEntity>();
    public DbSet<SpeedTestResultEntity> SpeedTestResults => Set<SpeedTestResultEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SpeedTestJobEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SpeedTestResultEntityConfiguration());
    }
}

internal sealed class SpeedTestJobEntityConfiguration : IEntityTypeConfiguration<SpeedTestJobEntity>
{
    public void Configure(EntityTypeBuilder<SpeedTestJobEntity> builder)
    {
        builder.ToTable("SpeedTestJobs");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.ProviderId).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.Stage).HasMaxLength(120).IsRequired();
        builder.Property(entity => entity.RequestedServerId).HasMaxLength(128);
        builder.Property(entity => entity.FailureCode).HasMaxLength(64);
        builder.Property(entity => entity.FailureMessage).HasMaxLength(240);
    }
}

internal sealed class SpeedTestResultEntityConfiguration : IEntityTypeConfiguration<SpeedTestResultEntity>
{
    public void Configure(EntityTypeBuilder<SpeedTestResultEntity> builder)
    {
        builder.ToTable("SpeedTestResults");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
        builder.Property(entity => entity.ProviderId).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.RequestedServerId).HasMaxLength(128);
        builder.Property(entity => entity.ServerId).HasMaxLength(128);
        builder.Property(entity => entity.ServerName).HasMaxLength(256);
        builder.Property(entity => entity.ServerLocation).HasMaxLength(256);
        builder.Property(entity => entity.ServerCountry).HasMaxLength(128);
        builder.Property(entity => entity.ResultUrl).HasMaxLength(780);
        builder.Property(entity => entity.FailureCode).HasMaxLength(64);
        builder.Property(entity => entity.FailureMessage).HasMaxLength(240);
        builder.Property(entity => entity.ProviderMetadataJson).HasMaxLength(StorageOptions.MaximumProviderMetadataBytes);

        ConfigureNetworkColumns(builder);

        builder.HasOne(entity => entity.Job)
            .WithOne(entity => entity.Result)
            .HasForeignKey<SpeedTestResultEntity>(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(entity => entity.JobId).IsUnique();
        builder.HasIndex(entity => new { entity.CompletedAtUtc, entity.Id }).IsDescending();
        builder.HasIndex(entity => new { entity.ProviderId, entity.CompletedAtUtc, entity.Id })
            .IsDescending(false, true, true);
        builder.HasIndex(entity => new { entity.Status, entity.CompletedAtUtc, entity.Id })
            .IsDescending(false, true, true);
    }

    private static void ConfigureNetworkColumns(EntityTypeBuilder<SpeedTestResultEntity> builder)
    {
        builder.Property(entity => entity.NetworkState).HasMaxLength(16);
        builder.Property(entity => entity.NetworkWarning).HasMaxLength(32);
        builder.Property(entity => entity.IPv4Address).HasMaxLength(45);
        builder.Property(entity => entity.IPv4Asn).HasMaxLength(32);
        builder.Property(entity => entity.IPv4AsName).HasMaxLength(256);
        builder.Property(entity => entity.IPv4Isp).HasMaxLength(256);
        builder.Property(entity => entity.IPv4CountryCode).HasMaxLength(2);
        builder.Property(entity => entity.IPv4CountryName).HasMaxLength(128);
        builder.Property(entity => entity.IPv4Region).HasMaxLength(128);
        builder.Property(entity => entity.IPv4City).HasMaxLength(128);
        builder.Property(entity => entity.IPv4AddressSource).HasMaxLength(64);
        builder.Property(entity => entity.IPv4MetadataSource).HasMaxLength(64);
        builder.Property(entity => entity.IPv6Address).HasMaxLength(45);
        builder.Property(entity => entity.IPv6Asn).HasMaxLength(32);
        builder.Property(entity => entity.IPv6AsName).HasMaxLength(256);
        builder.Property(entity => entity.IPv6Isp).HasMaxLength(256);
        builder.Property(entity => entity.IPv6CountryCode).HasMaxLength(2);
        builder.Property(entity => entity.IPv6CountryName).HasMaxLength(128);
        builder.Property(entity => entity.IPv6Region).HasMaxLength(128);
        builder.Property(entity => entity.IPv6City).HasMaxLength(128);
        builder.Property(entity => entity.IPv6AddressSource).HasMaxLength(64);
        builder.Property(entity => entity.IPv6MetadataSource).HasMaxLength(64);
    }
}
