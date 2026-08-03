using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaPOS.Domain.Entities.System;

namespace PharmaPOS.Persistence.Configurations;

public class SyncOutboxEntryConfiguration : IEntityTypeConfiguration<SyncOutboxEntry>
{
    public void Configure(EntityTypeBuilder<SyncOutboxEntry> b)
    {
        b.ToTable("SyncOutboxEntries");
        b.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        b.Property(x => x.StoreCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.PayloadJson).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.CreatedAtUtc });
        b.HasIndex(x => new { x.EntityType, x.StoreCode, x.LocalId });
    }
}
