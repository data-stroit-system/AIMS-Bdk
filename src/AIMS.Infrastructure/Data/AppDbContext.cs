using AIMS.Core.Entities;
using AIMS.SharedKernel;
using AIMS.SharedKernel.Interfaces;
using Ardalis.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Infrastructure.IdentityClass;
using AIMS.Infrastructure.Audit;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AIMS.Infrastructure.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        private readonly IDomainEventDispatcher _dispatcher;
        private readonly IAuditUserProvider? _auditUserProvider;

        public AppDbContext(DbContextOptions<AppDbContext> options, IDomainEventDispatcher dispatcher)
            : base(options)
        {
            _dispatcher = dispatcher;
        }

        public AppDbContext(DbContextOptions<AppDbContext> options, IDomainEventDispatcher dispatcher, IAuditUserProvider auditUserProvider)
            : base(options)
        {
            _dispatcher = dispatcher;
            _auditUserProvider = auditUserProvider;
        }

        public DbSet<ToDoItem> ToDoItems { get; set; }
        public DbSet<AssetItem> AssetItems { get; set; }
        public DbSet<AssetItemRemarks> AssetRemarks { get; set; }
        public DbSet<AssetItemDocuments> AssetItemDocuments { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyAllConfigurationsFromCurrentAssembly();

            // Configure AuditLog entity
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(e => e.EntityName);
                entity.HasIndex(e => e.EntityId);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.UserId);
            });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            // Capture audit entries before saving
            var auditEntries = OnBeforeSaveChanges();

            int result = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Save audit entries after successful save (to get generated IDs)
            await OnAfterSaveChanges(auditEntries, cancellationToken);

            // ignore events if no dispatcher provided
            if (_dispatcher == null) return result;

            // dispatch events only if save was successful
            var entitiesWithEvents = ChangeTracker.Entries<BaseEntity>()
                .Select(e => e.Entity)
                .Where(e => e.Events.Any())
                .ToArray();

            foreach (var entity in entitiesWithEvents)
            {
                var events = entity.Events.ToArray();
                entity.Events.Clear();
                foreach (var domainEvent in events)
                {
                    await _dispatcher.Dispatch(domainEvent).ConfigureAwait(false);
                }
            }

            return result;
        }

        public override int SaveChanges()
        {
            return SaveChangesAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Captures changes before saving to create audit entries.
        /// </summary>
        private List<AuditEntry> OnBeforeSaveChanges()
        {
            ChangeTracker.DetectChanges();
            var auditEntries = new List<AuditEntry>();

            foreach (var entry in ChangeTracker.Entries())
            {
                // Skip audit logs themselves and unchanged/detached entities
                if (entry.Entity is AuditLog || 
                    entry.State == EntityState.Detached || 
                    entry.State == EntityState.Unchanged)
                {
                    continue;
                }

                var auditEntry = new AuditEntry(entry)
                {
                    EntityName = entry.Entity.GetType().Name,
                    UserId = _auditUserProvider?.GetUserId(),
                    UserName = _auditUserProvider?.GetUserName(),
                    IpAddress = _auditUserProvider?.GetIpAddress()
                };

                auditEntries.Add(auditEntry);

                foreach (var property in entry.Properties)
                {
                    // Skip temporary values (will be filled after save)
                    if (property.IsTemporary)
                    {
                        auditEntry.TemporaryProperties.Add(property);
                        continue;
                    }

                    string propertyName = property.Metadata.Name;

                    // Handle primary key
                    if (property.Metadata.IsPrimaryKey())
                    {
                        auditEntry.NewValues[propertyName] = property.CurrentValue;
                        continue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            auditEntry.Action = AuditAction.Created.ToString();
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                            break;

                        case EntityState.Deleted:
                            auditEntry.Action = AuditAction.Deleted.ToString();
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            break;

                        case EntityState.Modified:
                            if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                            {
                                auditEntry.Action = AuditAction.Updated.ToString();
                                auditEntry.ChangedColumns.Add(propertyName);
                                auditEntry.OldValues[propertyName] = property.OriginalValue;
                                auditEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                            break;
                    }
                }
            }

            // Keep entries that have changes
            return auditEntries.Where(e => !string.IsNullOrEmpty(e.Action)).ToList();
        }

        /// <summary>
        /// Saves audit entries after the main save to capture auto-generated IDs.
        /// </summary>
        private async Task OnAfterSaveChanges(List<AuditEntry> auditEntries, CancellationToken cancellationToken)
        {
            if (auditEntries == null || auditEntries.Count == 0)
                return;

            foreach (var auditEntry in auditEntries)
            {
                // Fill in temporary values (like auto-generated primary keys)
                foreach (var prop in auditEntry.TemporaryProperties)
                {
                    if (prop.Metadata.IsPrimaryKey())
                    {
                        auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                    else
                    {
                        auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                }

                // Get the entity ID
                var entityId = auditEntry.NewValues.ContainsKey("Id") 
                    ? auditEntry.NewValues["Id"]?.ToString() 
                    : auditEntry.OldValues.ContainsKey("Id") 
                        ? auditEntry.OldValues["Id"]?.ToString() 
                        : string.Empty;

                // Create the audit log entry
                var auditLog = new AuditLog
                {
                    Category = AuditCategory.Entity,
                    EntityName = auditEntry.EntityName,
                    EntityId = entityId ?? string.Empty,
                    Action = auditEntry.Action,
                    UserId = auditEntry.UserId,
                    UserName = auditEntry.UserName,
                    IpAddress = auditEntry.IpAddress,
                    Timestamp = DateTime.UtcNow,
                    OldValues = auditEntry.OldValues.Count == 0 ? null : JsonSerializer.Serialize(auditEntry.OldValues),
                    NewValues = auditEntry.NewValues.Count == 0 ? null : JsonSerializer.Serialize(auditEntry.NewValues),
                    ChangedColumns = auditEntry.ChangedColumns.Count == 0 ? null : JsonSerializer.Serialize(auditEntry.ChangedColumns),
                    Result = "Success"
                };

                AuditLogs.Add(auditLog);
            }

            await base.SaveChangesAsync(cancellationToken);
        }
    }
}
