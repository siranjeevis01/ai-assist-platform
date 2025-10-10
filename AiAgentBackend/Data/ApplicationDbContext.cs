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
        public DbSet<WhatsAppSession> WhatsAppSessions { get; set; }
        public DbSet<GmailWebhook> GmailWebhooks { get; set; }
        public DbSet<ConversationState> ConversationStates { get; set; }

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
            
            // ProviderToken configurations
            modelBuilder.Entity<ProviderToken>(entity =>
            {
                entity.HasIndex(pt => new { pt.UserId, pt.Provider }).IsUnique();
                entity.HasIndex(pt => pt.ExpiresAt);
            });
            
            // RefreshToken configurations
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasIndex(rt => rt.Token).IsUnique();
                entity.HasIndex(rt => rt.UserId);
                entity.HasIndex(rt => rt.ExpiresAt);
            });
            
            // Message configurations
            modelBuilder.Entity<Message>(entity =>
            {
                entity.HasIndex(m => m.UserId);
                entity.HasIndex(m => m.Channel);
                entity.HasIndex(m => m.CreatedAt);
                entity.HasIndex(m => new { m.UserId, m.CreatedAt });
            });
            
            // ChatMessage configurations
            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasIndex(cm => cm.UserId);
                entity.HasIndex(cm => cm.CreatedAt);
                entity.HasIndex(cm => new { cm.UserId, cm.CreatedAt });
            });
            
            // AuditLog configurations
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(al => al.UserId);
                entity.HasIndex(al => al.Timestamp);
                entity.HasIndex(al => new { al.Entity, al.Action });
            });

            // ConversationState method
            modelBuilder.Entity<ConversationState>(entity =>
            {
                entity.HasIndex(cs => cs.UserId);
                entity.HasIndex(cs => cs.ExpiresAt);
                entity.Property(cs => cs.ContextData).HasColumnType("TEXT");
            });            
        }
    }
}