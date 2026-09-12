using System;
using System.Collections.Generic;
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

    public virtual DbSet<AppUser> AppUsers { get; set; }

    public virtual DbSet<Author> Authors { get; set; }

    public virtual DbSet<Genre> Genres { get; set; }

    public virtual DbSet<Highlight> Highlights { get; set; }

    public virtual DbSet<Material> Materials { get; set; }

    public virtual DbSet<Shelf> Shelves { get; set; }

    public virtual DbSet<UserPreference> UserPreferences { get; set; }

    public virtual DbSet<WordLookup> WordLookups { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Annotation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("annotation_pkey");

            entity.ToTable("annotation");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.EpubPosition)
                .HasMaxLength(255)
                .HasColumnName("epub_position");
            entity.Property(e => e.LinkedExcerpt).HasColumnName("linked_excerpt");
            entity.Property(e => e.MaterialId).HasColumnName("material_id");
            entity.Property(e => e.RegistryDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("registry_date");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Material).WithMany(p => p.Annotations)
                .HasForeignKey(d => d.MaterialId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("annotation_material_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Annotations)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("annotation_user_id_fkey");
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("app_user_pkey");

            entity.ToTable("app_user");

            entity.HasIndex(e => e.Email, "app_user_email_key").IsUnique();

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.BirthDate).HasColumnName("birth_date");
            entity.Property(e => e.Email)
                .HasMaxLength(254)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.Gender)
                .HasMaxLength(30)
                .HasColumnName("gender");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(70)
                .HasColumnName("password_hash");
            entity.Property(e => e.RegistryDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("registry_date");

            entity.HasMany(d => d.Genres).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserGenrePreference",
                    r => r.HasOne<Genre>().WithMany()
                        .HasForeignKey("GenreId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("user_genre_preference_genre_id_fkey"),
                    l => l.HasOne<AppUser>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("user_genre_preference_user_id_fkey"),
                    j =>
                    {
                        j.HasKey("UserId", "GenreId").HasName("user_genre_preference_pkey");
                        j.ToTable("user_genre_preference");
                        j.IndexerProperty<int>("UserId").HasColumnName("user_id");
                        j.IndexerProperty<int>("GenreId").HasColumnName("genre_id");
                    });
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
            entity.Property(e => e.EpubPosition)
                .HasMaxLength(255)
                .HasColumnName("epub_position");
            entity.Property(e => e.Excerpt).HasColumnName("excerpt");
            entity.Property(e => e.MaterialId).HasColumnName("material_id");
            entity.Property(e => e.RegistryDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("registry_date");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Material).WithMany(p => p.Highlights)
                .HasForeignKey(d => d.MaterialId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("highlight_material_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Highlights)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("highlight_user_id_fkey");
        });

        modelBuilder.Entity<Material>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("material_pkey");

            entity.ToTable("material");

            entity.HasIndex(e => new { e.Source, e.ExternalId }, "uq_material_source_external").IsUnique();

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
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
            entity.Property(e => e.Source)
                .HasMaxLength(20)
                .HasColumnName("source");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Type)
                .HasMaxLength(20)
                .HasColumnName("type");
            entity.Property(e => e.Year).HasColumnName("year");

            entity.HasMany(d => d.Authors).WithMany(p => p.Materials)
                .UsingEntity<Dictionary<string, object>>(
                    "MaterialAuthor",
                    r => r.HasOne<Author>().WithMany()
                        .HasForeignKey("AuthorId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("material_author_author_id_fkey"),
                    l => l.HasOne<Material>().WithMany()
                        .HasForeignKey("MaterialId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("material_author_material_id_fkey"),
                    j =>
                    {
                        j.HasKey("MaterialId", "AuthorId").HasName("material_author_pkey");
                        j.ToTable("material_author");
                        j.IndexerProperty<int>("MaterialId").HasColumnName("material_id");
                        j.IndexerProperty<int>("AuthorId").HasColumnName("author_id");
                    });

            entity.HasMany(d => d.Genres).WithMany(p => p.Materials)
                .UsingEntity<Dictionary<string, object>>(
                    "MaterialGenre",
                    r => r.HasOne<Genre>().WithMany()
                        .HasForeignKey("GenreId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("material_genre_genre_id_fkey"),
                    l => l.HasOne<Material>().WithMany()
                        .HasForeignKey("MaterialId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("material_genre_material_id_fkey"),
                    j =>
                    {
                        j.HasKey("MaterialId", "GenreId").HasName("material_genre_pkey");
                        j.ToTable("material_genre");
                        j.IndexerProperty<int>("MaterialId").HasColumnName("material_id");
                        j.IndexerProperty<int>("GenreId").HasColumnName("genre_id");
                    });
        });

        modelBuilder.Entity<Shelf>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shelf_pkey");

            entity.ToTable("shelf");

            entity.HasIndex(e => new { e.UserId, e.MaterialId }, "uq_shelf_user_material").IsUnique();

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.BookmarkedPage).HasColumnName("bookmarked_page");
            entity.Property(e => e.CurrentSessionTime).HasColumnName("current_session_time");
            entity.Property(e => e.LastPageRead).HasColumnName("last_page_read");
            entity.Property(e => e.MaterialId).HasColumnName("material_id");
            entity.Property(e => e.ReadPercentage)
                .HasPrecision(5, 2)
                .HasColumnName("read_percentage");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Material).WithMany(p => p.Shelves)
                .HasForeignKey(d => d.MaterialId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("shelf_material_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Shelves)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("shelf_user_id_fkey");
        });

        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_preference_pkey");

            entity.ToTable("user_preference", tb => tb.HasComment("Preferências declaradas pelo usuário, usadas pelo sistema de recomendação (RF29/RF30). Estrutura chave-valor: cada linha representa UMA preferência. Um mesmo usuário tem várias linhas."));

            entity.HasIndex(e => new { e.UserId, e.PreferenceType, e.PreferenceValue }, "uq_user_preference").IsUnique();

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.PreferenceType)
                .HasMaxLength(30)
                .HasComment("Categoria da preferência. Valores aceitos: language (idioma preferido, ex.: \"pt\"), knowledge_area (área de conhecimento de artigos, ex.: \"cond-mat.supr-con\"), content_type (tipo de material desejado, \"book\" ou \"scientific_article\").")
                .HasColumnName("preference_type");
            entity.Property(e => e.PreferenceValue)
                .HasMaxLength(100)
                .HasComment("Valor correspondente ao preference_type da mesma linha — o significado do texto aqui depende do tipo indicado na coluna ao lado.")
                .HasColumnName("preference_value");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UserPreferences)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("user_preference_user_id_fkey");
        });

        modelBuilder.Entity<WordLookup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("word_lookup_pkey");

            entity.ToTable("word_lookup");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.ConsultedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("consulted_at");
            entity.Property(e => e.MaterialId).HasColumnName("material_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Word)
                .HasMaxLength(100)
                .HasColumnName("word");

            entity.HasOne(d => d.Material).WithMany(p => p.WordLookups)
                .HasForeignKey(d => d.MaterialId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("word_lookup_material_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.WordLookups)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("word_lookup_user_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
