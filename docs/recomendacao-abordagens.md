# Recomendação (RF27) — abordagens e plano de evolução

> Documento de decisão para a tela de recomendações (RF27, #36). Compara o que já
> existe (heurístico content-based) com as opções de **machine learning** (incl. o
> "jeito Netflix"), com foco na realidade de dados do LivreMente e no que cabe no
> TCC. Não é implementação — é base para decidir o rumo.

## 1. Contexto e o que existe hoje

A tela deve recomendar obras com base nas **preferências declaradas** (`user_preference`
+ `user_genre`) e no **histórico de leitura** (`shelf` read/reading), excluindo o que
já está na estante, com comportamento de **cold start**.

O que está implementado (`RecommendationService`) é um **recomendador content-based
heurístico**: pontua candidatos por sobreposição de **gênero/área/idioma** + popularidade
(`download_count`), com pesos fixos. **Não há modelo nem treino** — é determinístico.
Atende os critérios de aceite da issue, mas **não é** um sistema de ML.

> Importante: qualquer sistema de ML **ainda precisa** de uma camada content-based/
> popularidade para *cold start* (usuário novo, obra nova). Ou seja, o heurístico atual
> **não é descartável** — ele é a **linha de base / fallback** de um sistema híbrido.

## 2. Famílias de recomendação (panorama)

| Família | Ideia | Precisa de… | Cold start |
|---|---|---|---|
| **Content-based** | casa o item com o perfil/itens que a pessoa gostou | atributos dos itens (gênero, área, texto) | lida bem com usuário novo se há preferências; sofre com item sem atributos |
| **Colaborativo (CF)** | "quem gostou de X gostou de Y" a partir do comportamento | **muito feedback usuário×item** | sofre com usuário e item novos; **sofre com sistema novo (poucos dados)** |
| **Híbrido** | combina os dois | o dos dois | mitiga os dois |
| **Deep learning** | redes sobre sinais ricos (sequência, texto, imagem) | dados em escala + infra | idem CF, com mais custo |

**O "jeito Netflix":** o núcleo histórico (Netflix Prize) é **CF por fatoração de
matrizes** — aprende fatores latentes de `usuário × item` a partir de feedback implícito
e prevê afinidade. Hoje a Netflix soma camadas de deep learning, mas o alicerce
conceitual citável no TCC é a **fatoração de matrizes**.

## 3. A restrição central: dados

CF depende de **densidade de interações entre usuários**. O LivreMente é uma plataforma
nova: poucos usuários, estante e sessões esparsas → **cold start de sistema**. Treinar CF
agora tende a **overfit / recomendações pobres** (não há sinal coletivo suficiente).

Consequência prática para o cronograma: **CF puro não entrega valor cedo**. O caminho
honesto é começar por content-based (funciona com 1 usuário) e **ligar a camada
colaborativa quando os dados crescerem**, sempre com fallback.

## 4. Opções (detalhe)

### A) Fatoração de matrizes — colaborativo, "jeito Netflix" (ML.NET)
- **Como:** monta a matriz implícita `usuário × publicação` a partir de sinais
  (estado da estante — read/reading/want_to_read — e/ou tempo de `reading_session`),
  treina o `MatrixFactorizationTrainer` do **ML.NET** (fica em C#/.NET), gera fatores
  latentes e prevê score por par (usuário, obra).
- **Arquitetura:** deixa de ser "um endpoint" — vira **pipeline**: treino **offline/batch**
  (job no `LivreMente.Importer` ou um worker/`BackgroundService`), **persistência do
  modelo** (arquivo/`blob`), a API **carrega e serve**, e **retreino periódico** conforme
  os dados mudam.
- **Dados:** precisa de volume; com os atuais, resultado fraco.
- **Prós:** alinhamento direto com a bibliografia (Netflix/matrix factorization); defensável
  no artigo. **Contras:** maior esforço e infra; inútil enquanto os dados forem ralos.

### B) Content-based com embeddings — ML "leve"
- **Como:** vetoriza cada obra (TF-IDF ou embeddings de **título + sinopse + gênero/área**;
  ML.NET tem `FeaturizeText`/TF-IDF, ou embeddings via serviço Python/modelo) e recomenda
  por **similaridade** (cosine / kNN) ao centroide das obras que a pessoa curtiu.
- **Arquitetura:** pré-computar os vetores das obras (batch, no import) + busca por
  similaridade (em memória para catálogos pequenos, ou `pgvector` no Postgres para escala).
- **Dados:** **funciona com 1 usuário** (não depende de outros) — bom pro estágio atual.
- **Prós:** é ML de verdade, robusto a cold start de sistema, aproveita a sinopse (texto
  rico). **Contras:** não é "colaborativo" como a Netflix — é personalização por conteúdo.

### C) Híbrido (faseado) — **recomendado**
- **Fase 1 (agora):** manter o content-based/heurístico atual como **baseline + fallback**;
  opcionalmente trocar os pesos fixos por **similaridade de embeddings** (opção B) para
  melhorar a qualidade sem depender de dados coletivos.
- **Fase 2 (quando houver dados):** adicionar a camada **colaborativa (A)** e **combinar**
  (ex.: score final = α·CF + β·content), caindo no content/popularidade quando o CF não
  tem sinal (usuário/obra novos).
- **Prós:** entrega valor desde já, evolui para o "jeito Netflix", e é o **desenho que a
  literatura recomenda** (bom de justificar no TCC). **Contras:** mais partes móveis ao
  longo do tempo (mas incremental).

## 5. Avaliação (essencial para o TCC)

Independente da abordagem, definir **como medir** — o artigo ganha com isso:
- **Protocolo offline:** separar um *holdout* (ex.: esconder a última obra que a pessoa
  marcou como read) e medir **Precision@k / Recall@k / MAP / nDCG**; comparar
  **baseline (popularidade)** × **content-based** × **CF/híbrido**.
- **Cobertura e diversidade** (não recomendar só best-sellers), e **cold start** explícito.
- Isso transforma "escolhi uns pesos" em **experimento comparável**, que é o que sustenta
  a escolha metodológica no texto.

## 6. Stack: .NET vs Python

- **ML.NET (C#):** mantém tudo no monorepo/stack atual; tem `MatrixFactorizationTrainer`
  (CF) e featurização de texto (content). Menos atrito operacional.
- **Python (scikit-surprise, implicit/ALS, LightFM, sentence-transformers):** ecossistema
  de recomendação mais rico e mais citável academicamente, mas exige **serviço separado**
  (API Python) + integração. Boa opção se o TCC quiser profundidade de ML.

## 7. Recomendação

**Híbrido faseado (opção C)**, começando por content-based (heurístico atual como fallback,
evoluindo para **embeddings/similaridade** na Fase 1) e **fatoração de matrizes (ML.NET)**
na Fase 2 quando houver interações suficientes — com **avaliação offline** comparando as
abordagens para fundamentar o artigo. Mantém o cronograma andando e chega no "jeito
Netflix" de forma incremental e defensável.

## 8. Decisões a tomar (para destravar a implementação)

1. **Fase 1 agora:** manter o heurístico como está, ou já trocar por **embeddings/
   similaridade** (opção B)?
2. **Stack:** ML em **ML.NET** (fica em .NET) ou **serviço Python** à parte?
3. **Sinal de feedback** para o CF (Fase 2): só estado da estante, ou também
   `reading_session` (tempo/percentual)? Pesos por estado (read > reading > want_to_read)?
4. **Infra de treino:** job no `Importer`, um `BackgroundService` agendado, ou pipeline
   externo? Onde persistir o modelo/vetores (arquivo, `pgvector`, blob)?
5. **Avaliação:** incluir o experimento offline (Precision@k etc.) no escopo do TCC?

## 9. Impacto no que já existe

Nenhuma reescrita obrigatória: o `RecommendationService` atual vira a **camada de
fallback/baseline** do híbrido. A evolução é **aditiva** (nova camada + pipeline), não
substitutiva.
