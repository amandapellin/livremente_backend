using System.Text.RegularExpressions;

namespace LivreMente.Api.Importing;

/// <summary>
/// Converte títulos com marcação LaTeX/TeX (comum nos artigos do arXiv) em texto
/// simples e legível. Os títulos do arXiv chegam com matemática embutida — ex.:
/// <c>$SU(n) \times \mathbb{Z}_2$</c>, <c>$C^\infty$</c>, <c>$\mathbb{F}_1$</c> — que,
/// gravada verbatim, aparece quebrada na UI e ainda bagunça a ordenação A-Z no banco
/// (títulos começando com <c>$</c> ordenam antes das letras). Issue #85 (RF/front).
///
/// A conversão é intencionalmente <b>com perda</b>: o alvo é um catálogo de leitura,
/// não renderização fiel de fórmula. Um conjunto curado de comandos vira Unicode
/// (ex.: <c>\times</c> → ×, <c>\alpha</c> → α); o restante da marcação é removido,
/// preservando o conteúdo textual.
/// </summary>
public static class TitleSanitizer
{
    // Caracteres que denunciam marcação TeX. Sem nenhum deles, o título é devolvido
    // intacto (caminho rápido) — a esmagadora maioria dos títulos de livros.
    private static readonly char[] TexMarkers = { '$', '\\', '{', '}', '^', '_', '~' };

    // Comandos \nome -> símbolo Unicode. A troca exige que o nome não seja seguido de
    // letra (\to não deve casar dentro de \topology), então usamos fronteira (?![A-Za-z]).
    private static readonly Dictionary<string, string> CommandMap = new(StringComparer.Ordinal)
    {
        // operadores / relações
        ["times"] = "×", ["cdot"] = "·", ["ast"] = "∗", ["star"] = "⋆",
        ["pm"] = "±", ["mp"] = "∓", ["leq"] = "≤", ["le"] = "≤",
        ["geq"] = "≥", ["ge"] = "≥", ["neq"] = "≠", ["ne"] = "≠",
        ["approx"] = "≈", ["sim"] = "∼", ["simeq"] = "≃", ["cong"] = "≅",
        ["equiv"] = "≡", ["propto"] = "∝", ["ll"] = "≪", ["gg"] = "≫",
        ["to"] = "→", ["rightarrow"] = "→", ["longrightarrow"] = "→",
        ["leftarrow"] = "←", ["longleftarrow"] = "←", ["leftrightarrow"] = "↔",
        ["mapsto"] = "↦", ["implies"] = "⇒", ["iff"] = "⇔",
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇",
        ["times "] = "× ", ["oplus"] = "⊕", ["otimes"] = "⊗", ["odot"] = "⊙",
        ["subset"] = "⊂", ["subseteq"] = "⊆", ["supset"] = "⊃", ["supseteq"] = "⊇",
        ["in"] = "∈", ["notin"] = "∉", ["cup"] = "∪", ["cap"] = "∩",
        ["setminus"] = "∖", ["emptyset"] = "∅", ["forall"] = "∀", ["exists"] = "∃",
        ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["sqrt"] = "√",
        ["cdots"] = "…", ["ldots"] = "…", ["dots"] = "…", ["dot"] = "·",
        ["prime"] = "′", ["ell"] = "ℓ", ["hbar"] = "ℏ", ["deg"] = "°",
        ["langle"] = "⟨", ["rangle"] = "⟩", ["dagger"] = "†",
        // grego minúsculo
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["varepsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η",
        ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ",
        ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ",
        ["pi"] = "π", ["varpi"] = "ϖ", ["rho"] = "ρ", ["varrho"] = "ϱ",
        ["sigma"] = "σ", ["varsigma"] = "ς", ["tau"] = "τ", ["upsilon"] = "υ",
        ["phi"] = "φ", ["varphi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        // grego maiúsculo
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ",
        ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Upsilon"] = "Υ",
        ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
    };

    // \frac{a}{b} (e \dfrac/\tfrac) -> "a/b". Precede os wrappers/remoção genérica.
    private static readonly Regex Frac = new(
        @"\\[dt]?frac\s*\{([^{}]*)\}\s*\{([^{}]*)\}", RegexOptions.Compiled);

    // \mathbb{Z}, \mathcal{O}, \mathrm{d}, \text{...}, \widehat{D}... -> mantém o argumento.
    private static readonly Regex Wrappers = new(
        @"\\(?:math[a-z]+|text[a-z]*|operatorname|bm|boldsymbol|hat|widehat|bar|overline|underline|tilde|widetilde|vec|dot|ddot|rm|bf|it|sf|tt|emph|mbox|mathop)\s*\{([^{}]*)\}",
        RegexOptions.Compiled);

    // Acentos em modo TeX: \'e \"o \^a \~n \`a \=o -> mantém a letra (normalmente o XML
    // já traz o acento decodificado, mas cobre o caso de a fonte mandar a forma TeX).
    private static readonly Regex TexAccents = new(
        @"\\['""\^`~=.]\s*\{?([A-Za-z])\}?", RegexOptions.Compiled);

    private static readonly Regex CommandAlternation = new(
        @"\\(" + string.Join("|", CommandMap.Keys.Select(Regex.Escape)) + @")(?![A-Za-z])",
        RegexOptions.Compiled);

    private static readonly Regex RemainingCommand = new(@"\\[A-Za-z]+\*?", RegexOptions.Compiled);
    private static readonly Regex EscapedSymbol = new(@"\\([&%_#{}$])", RegexOptions.Compiled);
    private static readonly Regex SpacingCommand = new(@"\\[\s,;:!>]", RegexOptions.Compiled);
    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunct = new(@"\s+([,.;:!?)\]])", RegexOptions.Compiled);

    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (raw ?? string.Empty).Trim();

        // Normaliza espaços/quebras primeiro (o arXiv quebra títulos em várias linhas).
        var s = MultiSpace.Replace(raw, " ").Trim();

        // Caminho rápido: título sem marcação TeX sai como está.
        if (s.IndexOfAny(TexMarkers) < 0) return s;

        // Delimitadores de modo matemático: some sem deixar espaço (a matemática já vem
        // cercada de espaço/pontuação), senão "$G$-Higgs" viraria "G -Higgs".
        s = s.Replace("$$", "").Replace("$", "");
        s = Regex.Replace(s, @"\\[()\[\]]", ""); // \( \) \[ \]

        // Comandos espaçadores (\, \; \! \> e barra-espaço) viram espaço.
        s = SpacingCommand.Replace(s, " ");

        // Acentos TeX -> letra.
        s = TexAccents.Replace(s, "$1");

        // Frações: \frac{1}{2} -> "1/2".
        s = Frac.Replace(s, "$1/$2");

        // Wrappers (\mathbb{Z}, \text{...}, \widehat{D}) -> argumento. Repete para aninhados.
        string prev;
        do { prev = s; s = Wrappers.Replace(s, "$1"); } while (!ReferenceEquals(prev, s) && prev != s);

        // Comandos curados -> Unicode.
        s = CommandAlternation.Replace(s, m => CommandMap[m.Groups[1].Value]);

        // Símbolos escapados (\_ \% \& \# \{ \} \$) -> o próprio símbolo.
        s = EscapedSymbol.Replace(s, "$1");

        // Qualquer \comando remanescente não mapeado -> removido (perda assumida).
        s = RemainingCommand.Replace(s, " ");

        // Chaves e marcadores de sub/sobrescrito somem; o operando textual permanece
        // (ex.: "Z_2" -> "Z2", "C^∞" -> "C∞").
        s = s.Replace("{", "").Replace("}", "").Replace("^", "").Replace("_", "").Replace("~", " ");

        // Arremate: colapsa espaços e remove espaço antes de pontuação.
        s = MultiSpace.Replace(s, " ").Trim();
        s = SpaceBeforePunct.Replace(s, "$1");
        return s;
    }
}
