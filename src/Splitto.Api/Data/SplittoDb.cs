using Microsoft.EntityFrameworkCore;

namespace Splitto.Api.Data;

public sealed class SplittoDb(DbContextOptions<SplittoDb> options) : DbContext(options)
{
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseShare> ExpenseShares => Set<ExpenseShare>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The database enforces the rules too, not only the API: a bug can't store a negative expense.
        modelBuilder.Entity<Group>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(80);
            e.Property(g => g.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(g => g.Token).HasMaxLength(32);
            e.HasIndex(g => g.Token).IsUnique();
            e.HasMany(g => g.Members).WithOne().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Member>(e =>
        {
            e.Property(m => m.Name).HasMaxLength(60);
            e.HasIndex(m => new { m.GroupId, m.Name }).IsUnique();
        });
        modelBuilder.Entity<Expense>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(120);
            e.Property(x => x.OriginalCurrency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.ExchangeRate).HasPrecision(18, 8);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.GroupId, x.Date });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Member>().WithMany().HasForeignKey(x => x.PaidById).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Shares).WithOne().HasForeignKey(s => s.ExpenseId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Expenses_Amount", "\"AmountCents\" > 0 AND \"OriginalAmountCents\" > 0");
                t.HasCheckConstraint("CK_Expenses_Rate", "\"ExchangeRate\" > 0");
            });
        });
        modelBuilder.Entity<ExpenseShare>(e =>
        {
            e.HasKey(s => new { s.ExpenseId, s.MemberId });
            e.HasOne<Member>().WithMany().HasForeignKey(s => s.MemberId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ExpenseShares_Cents", "\"Cents\" >= 0"));
        });
        modelBuilder.Entity<Payment>(e =>
        {
            e.HasOne<Group>().WithMany().HasForeignKey(p => p.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Member>().WithMany().HasForeignKey(p => p.FromId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Member>().WithMany().HasForeignKey(p => p.ToId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Payments_Amount", "\"AmountCents\" > 0");
                t.HasCheckConstraint("CK_Payments_Parties", "\"FromId\" <> \"ToId\"");
            });
        });
    }
}
