using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Entities;
using SchoolAdmission.Api.Enums;

namespace SchoolAdmission.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Class> Classes => Set<Class>();

    public DbSet<Hold> Holds => Set<Hold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Class>(entity =>
        {
            entity.ToTable("Classes", table =>
            {
                table.HasCheckConstraint("CK_Classes_TotalSeats_Positive", "\"TotalSeats\" > 0");
                table.HasCheckConstraint("CK_Classes_AvailableSeats_NonNegative", "\"AvailableSeats\" >= 0");
                table.HasCheckConstraint(
                    "CK_Classes_AvailableSeats_Lte_TotalSeats",
                    "\"AvailableSeats\" <= \"TotalSeats\"");
            });

            entity.HasKey(schoolClass => schoolClass.Id);

            entity.Property(schoolClass => schoolClass.Id)
                .HasMaxLength(64)
                .ValueGeneratedNever();

            entity.Property(schoolClass => schoolClass.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(schoolClass => schoolClass.TotalSeats)
                .IsRequired();

            entity.Property(schoolClass => schoolClass.AvailableSeats)
                .IsRequired();

            entity.Property(schoolClass => schoolClass.CreatedAt)
                .IsRequired();

            entity.Property(schoolClass => schoolClass.UpdatedAt)
                .IsRequired();
        });

        modelBuilder.Entity<Hold>(entity =>
        {
            entity.ToTable("Holds");

            entity.HasKey(hold => hold.Id);

            entity.Property(hold => hold.Id)
                .HasMaxLength(64)
                .ValueGeneratedNever();

            entity.Property(hold => hold.StudentId)
                .HasMaxLength(17)
                .IsRequired();

            entity.Property(hold => hold.ClassId)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(hold => hold.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(hold => hold.ExpiresAt)
                .IsRequired();

            entity.Property(hold => hold.CreatedAt)
                .IsRequired();

            entity.Property(hold => hold.UpdatedAt)
                .IsRequired();

            entity.HasOne(hold => hold.Class)
                .WithMany(schoolClass => schoolClass.Holds)
                .HasForeignKey(hold => hold.ClassId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(hold => hold.ClassId);
            entity.HasIndex(hold => hold.Status);
            entity.HasIndex(hold => hold.ExpiresAt);

            // One active (HELD) or confirmed application per student and class.
            // now() is not immutable, so expiration cannot be part of the index predicate.
            entity.HasIndex(hold => new { hold.StudentId, hold.ClassId })
                .IsUnique()
                .HasDatabaseName("IX_Holds_StudentId_ClassId_ActiveOrConfirmed")
                .HasFilter($"\"Status\" IN ('{HoldStatus.HELD}', '{HoldStatus.CONFIRMED}')");
        });
    }
}
