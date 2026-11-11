using System;
using System.Collections.Generic;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Models;

public partial class Publication
{
    public int Id { get; set; }

    public string ExternalId { get; set; } = null!;

    public PublicationSource Source { get; set; }
    
    public PublicationType Type { get; set; }

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

    public virtual ICollection<Annotation> Annotations { get; set; } = [];

    public virtual ICollection<Highlight> Highlights { get; set; } = [];

    public virtual ICollection<ReadingSession> ReadingSessions { get; set; } = [];

    public virtual ICollection<Shelf> Shelves { get; set; } = [];

    public virtual ICollection<WordLookup> WordLookups { get; set; } = [];

    public virtual ICollection<Author> Authors { get; set; } = [];

    public virtual ICollection<Genre> Genres { get; set; } = [];
}
