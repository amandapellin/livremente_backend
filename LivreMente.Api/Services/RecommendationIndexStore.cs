using Microsoft.ML;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Services;

/// <summary>
/// Persiste e recarrega o índice TF-IDF de recomendação em disco (RF27, #36), para
/// não refazê-lo a cada reinicialização do serviço. Guarda:
/// <list type="bullet">
/// <item>o modelo ML.NET treinado (<c>model.zip</c> — inclui o vocabulário TF-IDF);</item>
/// <item>os vetores esparsos e o tipo por obra + uma ASSINATURA do corpus (<c>index.bin</c>).</item>
/// </list>
/// Na carga, o índice só é reusado quando a assinatura bate com o estado atual do
/// catálogo (contagem + maior id de publicação + o teto de itens). Se o catálogo
/// mudou (nova importação) ou o formato é antigo, retorna <c>false</c> e o chamador
/// reconstrói. Gravação atômica (arquivo temporário + rename).
/// </summary>
public static class RecommendationIndexStore
{
    private const int FormatVersion = 1;
    private const string ModelFile = "model.zip";
    private const string DataFile = "index.bin";

    /// <summary>
    /// Impressão do corpus usada para decidir se o índice persistido ainda vale.
    /// Nova importação muda a contagem/maior id; mudar o teto invalida o cache.
    /// </summary>
    public readonly record struct Signature(long PublicationCount, int MaxPublicationId, int MaxItems);

    public static void Save(
        string dir, MLContext ml, ITransformer model, DataViewSchema inputSchema,
        IReadOnlyDictionary<int, SparseVector> vectors, IReadOnlyDictionary<int, PublicationType> types,
        Signature signature)
    {
        Directory.CreateDirectory(dir);

        var modelTmp = Path.Combine(dir, ModelFile + ".tmp");
        ml.Model.Save(model, inputSchema, modelTmp);
        File.Move(modelTmp, Path.Combine(dir, ModelFile), overwrite: true);

        var dataTmp = Path.Combine(dir, DataFile + ".tmp");
        using (var fs = File.Create(dataTmp))
        using (var w = new BinaryWriter(fs))
        {
            w.Write(FormatVersion);
            w.Write(signature.PublicationCount);
            w.Write(signature.MaxPublicationId);
            w.Write(signature.MaxItems);

            w.Write(types.Count);
            foreach (var (id, type) in types)
            {
                w.Write(id);
                w.Write((int)type);
            }

            w.Write(vectors.Count);
            foreach (var (id, v) in vectors)
            {
                w.Write(id);
                w.Write(v.Indices.Length);
                foreach (var ix in v.Indices) w.Write(ix);
                foreach (var val in v.Values) w.Write(val);
            }
        }
        File.Move(dataTmp, Path.Combine(dir, DataFile), overwrite: true);
    }

    /// <summary>
    /// Tenta carregar o índice persistido. Só devolve <c>true</c> (preenchendo os
    /// <c>out</c>) quando os arquivos existem, o formato é o atual e a assinatura é
    /// IGUAL à <paramref name="expected"/>. Qualquer falha (arquivo ausente/corrompido,
    /// versão/assinatura diferente) retorna <c>false</c> para o chamador reconstruir.
    /// </summary>
    public static bool TryLoad(
        string dir, Signature expected,
        out MLContext ml, out ITransformer model,
        out Dictionary<int, SparseVector> vectors, out Dictionary<int, PublicationType> types)
    {
        ml = null!;
        model = null!;
        vectors = null!;
        types = null!;

        var modelPath = Path.Combine(dir, ModelFile);
        var dataPath = Path.Combine(dir, DataFile);
        if (!File.Exists(modelPath) || !File.Exists(dataPath))
            return false;

        using var fs = File.OpenRead(dataPath);
        using var r = new BinaryReader(fs);

        if (r.ReadInt32() != FormatVersion)
            return false;
        var sig = new Signature(r.ReadInt64(), r.ReadInt32(), r.ReadInt32());
        if (sig != expected)
            return false;

        var typeCount = r.ReadInt32();
        var t = new Dictionary<int, PublicationType>(typeCount);
        for (var i = 0; i < typeCount; i++)
        {
            var id = r.ReadInt32();
            t[id] = (PublicationType)r.ReadInt32();
        }

        var vecCount = r.ReadInt32();
        var v = new Dictionary<int, SparseVector>(vecCount);
        for (var i = 0; i < vecCount; i++)
        {
            var id = r.ReadInt32();
            var len = r.ReadInt32();
            var indices = new int[len];
            for (var j = 0; j < len; j++) indices[j] = r.ReadInt32();
            var values = new float[len];
            for (var j = 0; j < len; j++) values[j] = r.ReadSingle();
            v[id] = new SparseVector(indices, values);
        }

        var mlCtx = new MLContext(seed: 0);
        model = mlCtx.Model.Load(modelPath, out _);
        ml = mlCtx;
        vectors = v;
        types = t;
        return true;
    }
}
