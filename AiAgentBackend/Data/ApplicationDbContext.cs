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
        public DbSet<WhatsAppSession> WhatsAppSessions => Set<WhatsAppSession>();
        public DbSet<GmailWebhook> GmailWebhooks => Set<GmailWebhook>();
        public DbSet<ConversationState> ConversationStates => Set<ConversationState>();

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
            
            // WhatsApp Session
            modelBuilder.Entity<WhatsAppSession>(entity =>
            {
                entity.HasIndex(ws => ws.IsConnected);
                entity.HasIndex(ws => ws.LastCheckedAt);
            });
            
            // Conversation State
            modelBuilder.Entity<ConversationState>(entity =>
            {
                entity.HasIndex(cs => cs.UserId);
                entity.HasIndex(cs => cs.ExpiresAt);
                entity.Property(cs => cs.ContextData).HasColumnType("TEXT");
            });
        }
    }
}