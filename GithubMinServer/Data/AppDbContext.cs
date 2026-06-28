using GithubMinServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GithubMinServer.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Commit> Commits => Set<Commit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .Property(user => user.Username)
            .HasMaxLength(50);

        modelBuilder.Entity<User>()
            .Property(user => user.Email)
            .HasMaxLength(100);

        modelBuilder.Entity<User>()
            .Property(user => user.PasswordHash)
            .HasMaxLength(500);

        modelBuilder.Entity<User>()
            .HasIndex(user => user.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(user => user.Email)
            .IsUnique();

        modelBuilder.Entity<Project>()
            .Property(project => project.Name)
            .HasMaxLength(100);

        modelBuilder.Entity<Project>()
            .Property(project => project.Description)
            .HasMaxLength(1000);

        modelBuilder.Entity<Project>()
            .HasOne(project => project.Owner)
            .WithMany(user => user.OwnedProjects)
            .HasForeignKey(project => project.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Project>()
            .HasOne(project => project.DefaultBranch)
            .WithMany()
            .HasForeignKey(project => project.DefaultBranchId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Project>()
            .HasOne(project => project.ActiveBranch)
            .WithMany()
            .HasForeignKey(project => project.ActiveBranchId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Branch>()
            .Property(branch => branch.Name)
            .HasMaxLength(100);

        modelBuilder.Entity<Branch>()
            .HasIndex(branch => new { branch.ProjectId, branch.Name })
            .IsUnique();

        modelBuilder.Entity<Branch>()
            .HasOne(branch => branch.Project)
            .WithMany(project => project.Branches)
            .HasForeignKey(branch => branch.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Branch>()
            .HasOne(branch => branch.HeadCommit)
            .WithMany()
            .HasForeignKey(branch => branch.HeadCommitId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Branch>()
            .HasOne(branch => branch.CreatedFromCommit)
            .WithMany()
            .HasForeignKey(branch => branch.CreatedFromCommitId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Commit>()
            .Property(commit => commit.Message)
            .HasMaxLength(300);

        modelBuilder.Entity<Commit>()
            .Property(commit => commit.SnapshotPath)
            .HasMaxLength(500);

        modelBuilder.Entity<Commit>()
            .HasOne(commit => commit.Project)
            .WithMany(project => project.Commits)
            .HasForeignKey(commit => commit.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Commit>()
            .HasOne(commit => commit.Branch)
            .WithMany(branch => branch.Commits)
            .HasForeignKey(commit => commit.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Commit>()
            .HasOne(commit => commit.Author)
            .WithMany(user => user.AuthoredCommits)
            .HasForeignKey(commit => commit.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Commit>()
            .HasOne(commit => commit.ParentCommit)
            .WithMany()
            .HasForeignKey(commit => commit.ParentCommitId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Commit>()
            .HasOne(commit => commit.MergeParentCommit)
            .WithMany()
            .HasForeignKey(commit => commit.MergeParentCommitId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
