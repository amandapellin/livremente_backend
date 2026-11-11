namespace LivreMente.Api.Mappings;

/// <summary>
/// Dicionários de tradução entre o vocabulário do FRONT (o <c>value</c> de cada
/// opção enviada no cadastro) e o vocabulário das FONTES de dados:
///   • livros  → nomes de bookshelves do Gutendex, gravados em <c>genre.name</c>;
///   • artigos → identificadores de arquivo (archive) do arXiv, casados contra <c>publication.knowledge_area</c>.
///
/// As CHAVES são exatamente os <c>value</c> do front (categoriasLivros, generosLiterarios, areasArtigos).
/// Os VALORES foram derivados dos dados reais em livre_mente_dev (bookshelves com maior volume).
/// É um mapeamento vivo: estenda conforme novos bookshelves aparecerem no import.
/// </summary>
public static class PreferenceCatalog
{
    /// <summary>
    /// Front (areasArtigos.value) → identificadores de archive do arXiv.
    /// Casamento por ARCHIVE, não por LIKE cru: extraia o trecho antes do primeiro '.'
    /// de <c>knowledge_area</c> (ex.: "cond-mat.supr-con" → "cond-mat", "cs.AI" → "cs")
    /// e compare exatamente com esta lista — ver <see cref="ArchiveOf"/>.
    /// Isso evita que "mathematics" (math) capture indevidamente "math-ph" (física).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ArticleArchives = new Dictionary<string, string[]>
    {
        ["physics"] =
        [
            "astro-ph", "cond-mat", "gr-qc", "hep-ex", "hep-lat", "hep-ph",
            "hep-th", "math-ph", "nlin", "nucl-ex", "nucl-th", "physics", "quant-ph",
        ],
        ["mathematics"]            = ["math"],
        ["computer_science"]       = ["cs"],
        ["quantitative_biology"]   = ["q-bio"],
        ["statistics"]             = ["stat"],
        ["quantitative_finance"]   = ["q-fin"],
        ["economics"]              = ["econ"],
        ["electrical_engineering"] = ["eess"],
    };

    /// <summary>
    /// Front (categoriasLivros.value + generosLiterarios.value) → nomes EXATOS de
    /// bookshelves do Gutendex (<c>genre.name</c>). Inclui variantes ("Category: X"
    /// e o nome legado "X") de propósito, porque ambas coexistem no banco.
    /// Uma publicação casa se tiver QUALQUER um desses gêneros.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> BookGenres = new Dictionary<string, string[]>
    {
        // ---- categoriasLivros ----
        ["literature"] =
        [
            "Category: British Literature", "Category: American Literature",
            "Category: French Literature", "Category: German Literature",
            "Category: Russian Literature", "Category: Literature - Other",
            "Category: Classics of Literature", "FR Littérature",
        ],
        ["science_technology"] =
        [
            "Category: Science - Biology", "Category: Science - Physics",
            "Category: Science - Chemistry/Biochemistry", "Category: Science - Earth/Agricultural/Farming",
            "Category: Engineering & Technology", "Category: Mathematics",
            "Category: Research Methods/Statistics/Information Sys",
            "Technology", "Biology", "Chemistry", "Botany", "Astronomy", "Mathematics",
            "FR Sciences et Techniques",
        ],
        ["history"] =
        [
            "Category: History - Modern (1750+)", "Category: History - American",
            "Category: History - Other", "Category: History - European",
            "Category: History - British", "Category: History - Warfare",
            "Category: History - Religious", "Category: History - Ancient",
            "Category: History - Early Modern (c. 1450-1750)", "Category: History - Medieval/Middle Ages",
            "Category: History - Royalty", "Category: History - Schools & Universities",
            "Category: Archaeology & Anthropology", "Classical Antiquity",
            "US Civil War", "World War I", "World War II", "Boer War", "Napoleonic(Bookshelf)",
            "FR Histoire",
        ],
        ["social_sciences"] =
        [
            "Category: Sociology", "Category: Politics", "Category: Economics",
            "Category: Law & Criminology", "Category: Gender & Sexuality Studies",
            "Category: Psychiatry/Psychology", "Category: Environmental Issues",
            "Category: Business/Management", "Category: Journalism/Media/Writing",
            "Category: Language & Communication", "Category: Parenthood & Family Relations",
            "Sociology", "Politics", "Psychology", "Anthropology",
        ],
        ["art_culture"] =
        [
            "Category: Art", "Category: Music", "Category: Architecture", "Category: Fashion",
            "Art", "Music", "Opera", "Architecture", "Movie Books",
        ],
        ["religion_philosophy"] =
        [
            "Category: Philosophy & Ethics", "Category: Religion/Spirituality",
            "Philosophy", "Christianity", "Islam", "Judaism", "Buddhism", "Hinduism",
            "Atheism", "Paganism", "Witchcraft", "FR Philosophie, Religion et Morale",
        ],
        ["hobbies"] =
        [
            "Category: Sports/Hobbies", "Category: Cooking & Drinking",
            "Category: Nature/Gardening/Animals", "Category: How To ...",
            "Category: Travel Writing", "Cookbooks and Cooking", "Crafts",
            "Woodwork", "Horticulture", "Travel",
        ],
        ["health_medicine"] =
        [
            "Category: Health & Medicine", "Category: Nutrition",
            "Category: Drugs/Alcohol/Pharmacology", "Medicine",
        ],
        ["education"] =
        [
            "Category: Teaching & Education", "Category: Encyclopedias/Dictionaries/Reference",
            "Language Education", "Reference", "FR Langues",
        ],

        // ---- generosLiterarios ----
        ["adventure"] =
        [
            "Category: Adventure", "Adventure", "Western", "Pirates, Buccaneers, Corsairs, etc.",
        ],
        ["classics"] =
        [
            "Category: Classics of Literature", "Harvard Classics",
            "Best Books Ever Listings", "Nobel Prizes in Literature",
            "Banned Books from Anne Haight's list", "Bestsellers, American, 1895-1923",
        ],
        ["biographies"] =
        [
            "Category: Biographies", "Biographies", "Category: Essays, Letters & Speeches",
            "FR Biographie, Mémoires, Journal intime, Correspondance",
        ],
        ["poetry"] =
        [
            "Category: Poetry", "Poetry", "FR Poésie", "DE Lyrik",
        ],
        ["romance"] =
        [
            "Category: Romance",
        ],
        ["science_fiction_fantasy"] =
        [
            "Category: Science-Fiction & Fantasy", "Science Fiction", "Fantasy",
            "Science Fiction by Women", "Precursors of Science Fiction", "Astounding Stories",
            "FR Science fiction",
        ],
        ["crime_thriller_mystery"] =
        [
            "Category: Crime, Thrillers and Mystery", "Detective Fiction", "Mystery Fiction",
            "Crime Fiction", "Crime Nonfiction", "Gothic Fiction", "Horror",
        ],
        ["mythology"] =
        [
            "Category: Mythology, Legends & Folklore", "Mythology", "Folklore", "Arthurian Legends",
        ],
        ["drama"] =
        [
            "Category: Plays/Films/Dramas", "One Act Plays", "FR Théâtre", "DE Drama",
        ],
        ["novels"] =
        [
            "Category: Novels", "Category: Historical Novels", "Historical Fiction", "DE Prosa",
        ],
        ["short_stories"] =
        [
            "Category: Short Stories", "Short Stories",
        ],
        ["children_young_adult"] =
        [
            "Category: Children & Young Adult Reading", "Children's Literature",
            "Children's Book Series", "Children's Picture Books", "Children's Fiction",
            "Children's History", "Children's Instructional Books", "Children's Anthologies",
            "Children's Myths, Fairy Tales, etc.", "School Stories", "Scouts", "DE Kinderbuch",
        ],
        ["humor"] =
        [
            "Category: Humour", "Humor",
        ],
        ["other"] = [],
    };

    /// <summary>
    /// Extrai o archive do arXiv de um <c>knowledge_area</c> (o trecho antes do primeiro '.').
    /// Ex.: "cond-mat.supr-con" → "cond-mat"; "hep-th" → "hep-th".
    /// Use para casar contra <see cref="ArticleArchives"/> sem o falso-positivo de math/math-ph.
    /// </summary>
    public static string ArchiveOf(string knowledgeArea)
    {
        var dot = knowledgeArea.IndexOf('.');
        return dot < 0 ? knowledgeArea : knowledgeArea[..dot];
    }
}
