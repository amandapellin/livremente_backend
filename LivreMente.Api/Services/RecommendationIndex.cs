using Microsoft.ML;
using Microsoft.ML.Data;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Services;

/// <summary>Linha de texto de uma publicação para a ML.NET (TF-IDF).</summary>
public sealed class PublicationTextRow
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Vetor TF-IDF <b>esparso</b> e L2-normalizado (índices em ordem crescente). Como
/// os vetores são normalizados, o <b>cosseno</b> entre dois é o produto interno.
/// </summary>
public sealed class SparseVector(int[] indices, float[] values)
{
    public int[] Indices { get; } = indices;
    public float[] Values { get; } = values;
    public static readonly SparseVector Empty = new([], []);
    public bool IsEmpty => Indices.Length == 0;

    /// <summary>Produto interno (= cosseno, pois ambos são L2-normalizados). Merge-join nos índices ordenados.</summary>
    public float Dot(SparseVector other)
    {
        int i = 0, j = 0;
        float sum = 0f;
        var ai = Indices;
        var av = Values;
        var bi = other.Indices;
        var bv = other.Values;
        while (i < ai.Length && j < bi.Length)
        {
            var diff = ai[i] - bi[j];
            if (diff == 0) { sum += av[i] * bv[j]; i++; j++; }
            else if (diff < 0) i++;
            else j++;
        }
        return sum;
    }

    /// <summary>Centroide L2-normalizado de vários vetores (vetor de "gosto" do usuário).</summary>
    public static SparseVector Centroid(IEnumerable<SparseVector> vectors)
    {
        var acc = new Dictionary<int, float>();
        var any = false;
        foreach (var v in vectors)
        {
            any = true;
            for (var k = 0; k < v.Indices.Length; k++)
                acc[v.Indices[k]] = acc.GetValueOrDefault(v.Indices[k]) + v.Values[k];
        }
        if (!any || acc.Count == 0) return Empty;

        double norm = 0;
        foreach (var val in acc.Values) norm += (double)val * val;
        if (norm <= 0) return Empty;

        var inv = (float)(1.0 / Math.Sqrt(norm));
        var idx = acc.Keys.OrderBy(k => k).ToArray();
        var vals = new float[idx.Length];
        for (var k = 0; k < idx.Length; k++) vals[k] = acc[idx[k]] * inv;
        return new SparseVector(idx, vals);
    }

    /// <summary>Lê o VBuffer "Features" da ML.NET como vetor esparso (ambos os casos: denso/esparso).</summary>
    internal static SparseVector FromVBuffer(in VBuffer<float> buf)
    {
        var values = buf.GetValues();
        if (buf.IsDense)
        {
            var idx = new List<int>();
            var vals = new List<float>();
            for (var i = 0; i < values.Length; i++)
                if (values[i] != 0f) { idx.Add(i); vals.Add(values[i]); }
            return new SparseVector([.. idx], [.. vals]);
        }
        // Esparso: índices já vêm ordenados pela ML.NET.
        return new SparseVector(buf.GetIndices().ToArray(), values.ToArray());
    }
}

/// <summary>
/// Índice de recomendação em memória (RF27, #36, abordagem content-based com ML): os
/// vetores TF-IDF das obras + o modelo ML.NET para vetorizar texto arbitrário (o
/// pseudo-documento de preferências do usuário). Preenchido em background por
/// <see cref="RecommendationIndexBuilder"/>; <see cref="IsReady"/> enquanto não carrega.
/// </summary>
public interface IRecommendationIndex
{
    bool IsReady { get; }
    IReadOnlyDictionary<int, SparseVector> Vectors { get; }
    IReadOnlyDictionary<int, PublicationType> Types { get; }

    /// <summary>Vetoriza um texto no mesmo espaço TF-IDF das obras (perfil por preferências).</summary>
    SparseVector Vectorize(string text);

    void Load(MLContext ml, ITransformer model, Dictionary<int, SparseVector> vectors, Dictionary<int, PublicationType> types);
}

public sealed class RecommendationIndex : IRecommendationIndex
{
    private volatile bool _ready;
    private Dictionary<int, SparseVector> _vectors = new();
    private Dictionary<int, PublicationType> _types = new();
    private MLContext? _ml;
    private ITransformer? _model;
    private readonly object _vectorizeLock = new();

    public bool IsReady => _ready;
    public IReadOnlyDictionary<int, SparseVector> Vectors => _vectors;
    public IReadOnlyDictionary<int, PublicationType> Types => _types;

    public void Load(MLContext ml, ITransformer model, Dictionary<int, SparseVector> vectors, Dictionary<int, PublicationType> types)
    {
        _ml = ml;
        _model = model;
        _vectors = vectors;
        _types = types;
        _ready = true;
    }

    public SparseVector Vectorize(string text)
    {
        if (!_ready || _ml is null || _model is null || string.IsNullOrWhiteSpace(text))
            return SparseVector.Empty;

        // Transformers da ML.NET são imutáveis, mas serializamos a chamada por garantia.
        lock (_vectorizeLock)
        {
            var data = _ml.Data.LoadFromEnumerable(new[] { new PublicationTextRow { Id = 0, Text = text } });
            var transformed = _model.Transform(data);
            var col = transformed.Schema["Features"];
            using var cursor = transformed.GetRowCursor(new[] { col });
            var getter = cursor.GetGetter<VBuffer<float>>(col);
            VBuffer<float> buf = default;
            if (!cursor.MoveNext()) return SparseVector.Empty;
            getter(ref buf);
            return SparseVector.FromVBuffer(buf);
        }
    }
}
