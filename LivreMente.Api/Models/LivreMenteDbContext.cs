using System;
using System.Collections.Generic;
using LivreMente.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace LivreMente.Api.Models;

public partial class LivreMenteDbContext : DbContext
{
    public LivreMenteDbContext()
    {
    }

    public LivreMenteDbContext(DbContextOptions<LivreMenteDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Annotation> Annotations { get; set; }

    public virtual DbSet<Author> Authors { get; set; }

    public virtual DbSet<Genre> Genres { get; set; }

    public virtual DbSet<Highlight> Highlights { get; set; }

    public virtual DbSet<Publication> Publications { get; set; }

    public virtual DbSet<ReadingSession> ReadingSessions { get; set; }

    public virtual DbSet<Shelf> Shelves { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserPreference> UserPreferences { get; set; }

    public virtual DbSet<WordLookup> WordLookups { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum<Gender>("gender_enum")
            .HasPostgresEnum<PreferenceType>("preference_type_enum")
            .HasPostgresEnum<ReadingStatus>("reading_status_enum")
            .HasPostgresEnum<PublicationSource>("source_enum")
            .HasPostgresEnum<PublicationType>("type_enum");

        modelBuilder.Entity<Annotation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("annotation_pkey");

            entity.ToTable("annotation");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("create_date");
            entity.Property(e => e.EpubPosition)
                .HasMaxLength(255)
                .HasColumnName("epub_position");
            entity.Property(e => e.LinkedExcerpt).HasColumnName("linked_excerpt");
            entity.Property(e => e.PublicationId).HasColumnName("publication_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Publication).WithMany(p => p.Annotations)
                .HasForeignKey(d => d.PublicationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("annotation_publication_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Annotations)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("annotation_user_id_fkey");
        });

        modelBuilder.Entity<Author>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("author_pkey");

            entity.ToTable("author");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.BirthYear).HasColumnName("birth_year");
            entity.Property(e => e.DeathYear).HasColumnName("death_year");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Genre>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("genre_pkey");

            entity.ToTable("genre");

            entity.HasIndex(e => e.Name, "genre_name_key").IsUnique();

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Highlight>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("highlight_pkey");

            entity.ToTable("highlight");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("create_date");
            entity.Property(e => e.EpubPosition)
                .HasMaxLength(255)
                .HasColumnName("epub_position");
            entity.Property(e => e.Excerpt).HasColumnName("excerpt");
            entity.Property(e => e.PublicationId).HasColumnName("publication_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Publication).WithMany(p => p.Highlights)
                .HasForeignKey(d => d.PublicationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("highlight_publication_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Highlights)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("highlight_user_id_fkey");
        });

        modelBuilder.Entity<Publication>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("publication_pkey");

            entity.ToTable("publication");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.Source).HasColumnName("source");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.CopyrightFlag).HasColumnName("copyright_flag");
            entity.Property(e => e.CoverUrl)
                .HasMaxLength(500)
                .HasColumnName("cover_url");
            entity.Property(e => e.DownloadCount).HasColumnName("download_count");
            entity.Property(e => e.EpubFileUrl)
                .HasMaxLength(500)
                .HasColumnName("epub_file_url");
            entity.Property(e => e.ExternalId)
                .HasMaxLength(255)
                .HasColumnName("external_id");
            entity.Property(e => e.KnowledgeArea)
                .HasMaxLength(150)
                .HasColumnName("knowledge_area");
            entity.Property(e => e.Language)
                .HasMaxLength(20)
                .HasColumnName("language");
            entity.Property(e => e.PdfFileUrl)
                .HasMaxLength(500)
                .HasColumnName("pdf_file_url");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Year).HasColumnName("year");

            entity.HasMany(d => d.Authors).WithMany(p => p.Publications)
                .UsingEntity<Dictionary<string, object>>(
                    "PublicationAuthor",
                    r => r.HasOne<Author>().WithMany()
                        .HasForeignKey("AuthorId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("publication_author_author_id_fkey"),
                    l => l.HasOne<Publication>().WithMany()
                        .HasForeignKey("PublicationId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("publication_author_publication_id_fkey"),
                    j =>
                    {
                        j.HasKey("PublicationId", "AuthorId").HasName("publication_author_pkey");
                        j.ToTable("publication_author");
                        j.IndexerProperty<int>("PublicationId").HasColumnName("publication_id");
                        j.IndexerProperty<int>("AuthorId").HasColumnName("author_id");
                    });

            entity.HasMany(d => d.Genres).WithMany(p => p.Publications)
                .UsingEntity<Dictionary<string, object>>(
                    "PublicationGenre",
                    r => r.HasOne<Genre>().WithMany()
                        .HasForeignKey("GenreId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("publication_genre_genre_id_fkey"),
                    l => l.HasOne<Publication>().WithMany()
                        .HasForeignKey("PublicationId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("publication_genre_publication_id_fkey"),
                    j =>
                    {
                        j.HasKey("PublicationId", "GenreId").HasName("publication_genre_pkey");
                        j.ToTable("publication_genre");
                        j.IndexerProperty<int>("PublicationId").HasColumnName("publication_id");
                        j.IndexerProperty<int>("GenreId").HasColumnName("genre_id");
                    });

            entity.HasIndex(p => new { p.Source, p.ExternalId })
              .IsUnique()
              .HasDatabaseName("uq_publication_source_external");
        });

        modelBuilder.Entity<ReadingSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("reading_session_pkey");

            entity.ToTable("reading_session");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.EndTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("end_time");
            entity.Property(e => e.PublicationId).HasColumnName("publication_id");
            entity.Property(e => e.StartTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("start_time");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Publication).WithMany(p => p.ReadingSessions)
                .HasForeignKey(d => d.PublicationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reading_session_publication_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ReadingSessions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reading_session_user_id_fkey");
        });

        modelBuilder.Entity<Shelf>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shelf_pkey");

            entity.ToTable("shelf");

            entity.HasIndex(e => new { e.UserId, e.PublicationId }, "uq_shelf_user_publication").IsUnique();

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.BookmarkedPage).HasColumnName("bookmarked_page");
            entity.Property(e => e.LastPageRead).HasColumnName("last_page_read");
            entity.Property(e => e.PublicationId).HasColumnName("publication_id");
            entity.Property(e => e.ReadPercentage).HasColumnName("read_percentage");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Publication).WithMany(p => p.Shelves)
                .HasForeignKey(d => d.PublicationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("shelf_publication_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Shelves)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("shelf_user_id_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.BirthDate).HasColumnName("birth_date");
            entity.Property(e => e.CreateDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("create_date");
            entity.Property(e => e.Email)
                .HasMaxLength(254)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.LastLoginDate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_login_date");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(70)
                .HasColumnName("password_hash");
            entity.Property(e => e.UpdateDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("update_date");

            entity.HasMany(d => d.Genres).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserGenre",
                    r => r.HasOne<Genre>().WithMany()
                        .HasForeignKey("GenreId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("user_genre_genre_id_fkey"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("user_genre_user_id_fkey"),
                    j =>
                    {
                        j.HasKey("UserId", "GenreId").HasName("user_genre_pkey");
                        j.ToTable("user_genre");
                        j.IndexerProperty<int>("UserId").HasColumnName("user_id");
                        j.IndexerProperty<int>("GenreId").HasColumnName("genre_id");
                    });
        });

        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_preference_pkey");

            entity.ToTable("user_preference");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PreferenceType).HasColumnName("preference_type");
            entity.Property(e => e.PreferenceValue)
                .HasMaxLength(100)
                .HasColumnName("preference_value");

            entity.HasOne(d => d.User).WithMany(p => p.UserPreferences)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("user_preference_user_id_fkey");
            
            entity.HasIndex(u => new { u.UserId, u.PreferenceType, u.PreferenceValue })
              .IsUnique()
              .HasDatabaseName("uq_user_preference");
        });

        modelBuilder.Entity<WordLookup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("word_lookup_pkey");

            entity.ToTable("word_lookup");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.ConsultDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("consult_date");
            entity.Property(e => e.PublicationId).HasColumnName("publication_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Word)
                .HasMaxLength(100)
                .HasColumnName("word");

            entity.HasOne(d => d.Publication).WithMany(p => p.WordLookups)
                .HasForeignKey(d => d.PublicationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("word_lookup_publication_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.WordLookups)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("word_lookup_user_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
