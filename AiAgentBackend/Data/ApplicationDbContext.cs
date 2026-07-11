using Microsoft.EntityFrameworkCore;
using AiAgentBackend.Models;

namespace AiAgentBackend.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<TaskItem> Tasks => Set<TaskItem>();
        public DbSet<ProviderToken> ProviderTokens => Set<ProviderToken>();
        public DbSet<Preference> Preferences => Set<Preference>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<GmailWebhook> GmailWebhooks => Set<GmailWebhook>();
        public DbSet<ConversationState> ConversationStates => Set<ConversationState>();
        public DbSet<ConversationHistory> ConversationHistory => Set<ConversationHistory>();
        public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
        public DbSet<Document> Documents => Set<Document>();
        public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
        public DbSet<Team> Teams => Set<Team>();
        public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
        public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
        public DbSet<UserMessagingPreference> UserMessagingPreferences => Set<UserMessagingPreference>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // User configurations
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();
                entity.HasIndex(u => u.PhoneNumber).IsUnique();
                
                entity.HasMany(u => u.Tasks)
                    .WithOne(t => t.User)
                    .HasForeignKey(t => t.UserId);
                    
                entity.HasMany(u => u.Events)
                    .WithOne(e => e.User)
                    .HasForeignKey(e => e.UserId);
                    
                entity.HasMany(u => u.ProviderTokens)
                    .WithOne(pt => pt.User)
                    .HasForeignKey(pt => pt.UserId);
                    
                entity.HasOne(u => u.Preference)
                    .WithOne(p => p.User)
                    .HasForeignKey<Preference>(p => p.UserId);
                    
                entity.HasMany(u => u.RefreshTokens)
                    .WithOne(rt => rt.User)
                    .HasForeignKey(rt => rt.UserId);
                    
                entity.HasMany(u => u.ChatMessages)
                    .WithOne(cm => cm.User)
                    .HasForeignKey(cm => cm.UserId);
            });
            
            // Event configurations
            modelBuilder.Entity<Event>(entity =>
            {
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.ExternalId);
                entity.HasIndex(e => e.StartUtc);
                entity.HasIndex(e => new { e.UserId, e.StartUtc });
            });
            
            // Task configurations
            modelBuilder.Entity<TaskItem>(entity =>
            {
                entity.HasIndex(t => t.UserId);
                entity.HasIndex(t => t.ExternalId);
                entity.HasIndex(t => t.DueUtc);
                entity.HasIndex(t => t.Status);
                entity.HasIndex(t => new { t.UserId, t.Status });
            });
            
            // Conversation State
            modelBuilder.Entity<ConversationState>(entity =>
            {
                entity.HasIndex(cs => cs.UserId);
                entity.HasIndex(cs => cs.ExpiresAt);
                entity.Property(cs => cs.ContextData).HasColumnType("TEXT");
            });

            modelBuilder.Entity<UserMessagingPreference>(entity =>
            {
                entity.HasIndex(ump => new { ump.UserId, ump.Platform });
                entity.HasIndex(ump => ump.PlatformUserId);
                entity.HasIndex(ump => ump.PreferredPlatform);
            });

            modelBuilder.Entity<ConversationHistory>(entity =>
            {
                entity.HasIndex(ch => ch.UserId);
                entity.HasIndex(ch => ch.CreatedAt);
                entity.HasIndex(ch => new { ch.UserId, ch.CreatedAt });
            });

            modelBuilder.Entity<AutomationRule>(entity =>
            {
                entity.HasIndex(ar => ar.UserId);
                entity.HasIndex(ar => ar.IsActive);
                entity.Property(ar => ar.TriggerConfig).HasColumnType("TEXT");
                entity.Property(ar => ar.ActionsJson).HasColumnType("TEXT");
                entity.HasOne(ar => ar.User)
                    .WithMany()
                    .HasForeignKey(ar => ar.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Document>(entity =>
            {
                entity.HasIndex(d => d.UserId);
                entity.HasIndex(d => d.CreatedAt);
                entity.HasOne(d => d.User)
                    .WithMany()
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<DocumentChunk>(entity =>
            {
                entity.HasIndex(dc => dc.DocumentId);
                entity.HasOne(dc => dc.Document)
                    .WithMany(d => d.Chunks)
                    .HasForeignKey(dc => dc.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Team>(entity =>
            {
                entity.HasIndex(t => t.OwnerId);
                entity.HasOne(t => t.Owner)
                    .WithMany()
                    .HasForeignKey(t => t.OwnerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TeamMember>(entity =>
            {
                entity.HasIndex(tm => new { tm.TeamId, tm.UserId }).IsUnique();
                entity.HasOne(tm => tm.Team)
                    .WithMany(t => t.Members)
                    .HasForeignKey(tm => tm.TeamId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(tm => tm.User)
                    .WithMany()
                    .HasForeignKey(tm => tm.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AuditEntry>(entity =>
            {
                entity.HasIndex(ae => ae.UserId);
                entity.HasIndex(ae => ae.CreatedAt);
                entity.HasOne(ae => ae.User)
                    .WithMany()
                    .HasForeignKey(ae => ae.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });            
        }
    }
}