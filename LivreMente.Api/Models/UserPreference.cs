using System;
using System.Collections.Generic;

namespace LivreMente.Api.Models;

/// <summary>
/// Preferências declaradas pelo usuário, usadas pelo sistema de recomendação (RF29/RF30). Estrutura chave-valor: cada linha representa UMA preferência. Um mesmo usuário tem várias linhas.
/// </summary>
public partial class UserPreference
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// Categoria da preferência. Valores aceitos: language (idioma preferido, ex.: &quot;pt&quot;), knowledge_area (área de conhecimento de artigos, ex.: &quot;cond-mat.supr-con&quot;), content_type (tipo de material desejado, &quot;book&quot; ou &quot;scientific_article&quot;).
    /// </summary>
    public string PreferenceType { get; set; } = null!;

    /// <summary>
    /// Valor correspondente ao preference_type da mesma linha — o significado do texto aqui depende do tipo indicado na coluna ao lado.
    /// </summary>
    public string PreferenceValue { get; set; } = null!;

    public virtual AppUser User { get; set; } = null!;
}
