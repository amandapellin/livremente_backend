using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

public partial class Material
{
    public int Id { get; set; }

    public string ExternalId { get; set; } = null!;

    public string Source { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public int? Year { get; set; }

    public string? Language { get; set; }

    public string? Summary { get; set; }

    public string? KnowledgeArea { get; set; }

    public string? CoverUrl { get; set; }

    public string? EpubFileUrl { get; set; }

    public string? PdfFileUrl { get; set; }

    public int? DownloadCount { get; set; }

    public bool CopyrightFlag { get; set; }

    public virtual ICollection<Annotation> Annotations { get; set; } = new List<Annotation>();

    public virtual ICollection<Highlight> Highlights { get; set; } = new List<Highlight>();

    public virtual ICollection<Shelf> Shelves { get; set; } = new List<Shelf>();

    public virtual ICollection<WordLookup> WordLookups { get; set; } = new List<WordLookup>();

    public virtual ICollection<Author> Authors { get; set; } = new List<Author>();

    public virtual ICollection<Genre> Genres { get; set; } = new List<Genre>();
}
