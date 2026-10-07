// Requer Node 18+ e a API conectada a um catálogo importado. Não altera dados.
// Uso: node tests/catalog.http.mjs http://localhost:5092
import assert from 'node:assert/strict';

const base = process.argv[2] ?? 'http://localhost:5091';
let checks = 0;
async function get(params = {}, status = 200, path = '/api/publications') {
  const response = await fetch(`${base}${path}?${new URLSearchParams(params)}`, {
    signal: AbortSignal.timeout(60000),
  });
  assert.equal(response.status, status, `${path} ${JSON.stringify(params)}`);
  const body = await response.json();
  if (status === 400) assert.equal(body.code, 'VALIDATION_ERROR');
  checks++;
  return body;
}
function pageContract(body) {
  assert.equal(body.total, body.counts.all);
  assert.equal(body.counts.all, body.counts.book + body.counts.scientific_article);
  assert.equal(body.totalPages, Math.ceil(body.total / body.pageSize));
  assert.ok(body.items.length <= body.pageSize);
  assert.equal(new Set(body.items.map(item => item.id)).size, body.items.length);
  for (const item of body.items) {
    assert.equal(typeof item.id, 'string');
    assert.ok(['book', 'scientific_article'].includes(item.type));
    assert.ok(['gutendex', 'arxiv'].includes(item.source));
    assert.ok(Array.isArray(item.authors));
    assert.ok(Array.isArray(item.genres));
    assert.ok(item.genres.every(slug => /^[a-z_]+$/.test(slug)));
    if (item.type === 'scientific_article') assert.equal(item.coverUrl, null);
  }
}

const first = await get();
pageContract(first);
assert.equal(first.page, 1);
assert.equal(first.pageSize, 10);
assert.ok(first.total > 10, 'Importe o catálogo antes de executar estes testes.');
const second = await get({ page: 2 });
pageContract(second);
assert.ok(second.items.every(item => !first.items.some(other => other.id === item.id)));
assert.deepEqual(await get({ q: '   ' }), first);
assert.deepEqual(await get({ sort: 'unknown' }), first);
assert.deepEqual(await get({ sort: 'relevance' }), first);
assert.deepEqual(await get({ sort: 'popularity' }), first);
const recent = await get({ sort: 'recent', pageSize: 50 });
pageContract(recent);
assert.ok(recent.items.some(item => item.type === 'scientific_article'),
  'O catálogo de teste deve incluir artigos recentes para validar os cards.');
assert.ok(recent.items.every((item, index, items) => index === 0
  || (items[index - 1].year ?? -Infinity) >= (item.year ?? -Infinity)));
pageContract(await get({ sort: 'title', pageSize: 50 }));
const beyond = await get({ page: 2147483647 });
pageContract(beyond);
assert.deepEqual(beyond.items, []);
for (const params of [{ page: 0 }, { page: -1 }, { pageSize: 0 }, { pageSize: -1 }, { pageSize: 51 }]) {
  await get(params, 400);
}
const missing = await get({ q: 'livremente_no_match_91a47e2b' });
pageContract(missing);
assert.equal(missing.total, 0);
assert.deepEqual(missing.items, []);
const normalize = text => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
const title = first.items[0].title;
const titleResult = await get({ q: title });
assert.ok(titleResult.total > 0);
pageContract(titleResult);
assert.deepEqual(await get({ q: normalize(title).toUpperCase() }), titleResult);
const accentedTitle = normalize(title).replace(/[aeiou]/g,
  vowel => ({ a: 'á', e: 'é', i: 'í', o: 'ó', u: 'ú' })[vowel]);
assert.deepEqual(await get({ q: accentedTitle }), titleResult);
assert.ok(titleResult.total < first.total, 'A busca deve restringir também as contagens.');
const author = first.items.find(item => item.authors.length)?.authors[0];
assert.ok(author);
const authorResult = await get({ q: author });
assert.ok(authorResult.total > 0);
pageContract(authorResult);
for (const literal of ['%', '_', '\\']) {
  const body = await get({ q: literal });
  pageContract(body);
  assert.ok(body.items.every(item => item.title.includes(literal)
    || item.authors.some(name => name.includes(literal))));
}
const legacy = await get({ query: title, pageSize: 10 }, 200, '/api/publications/search');
assert.equal(legacy.total, titleResult.total);
assert.ok(legacy.items.every(item => typeof item.id === 'number'));
const spec = await get({}, 200, '/swagger/v1/swagger.json');
const operation = spec.paths['/api/publications'].get;
assert.deepEqual(operation.parameters.map(p => p.name).sort(), ['page', 'pageSize', 'q', 'sort']);
assert.ok(operation.responses['200']);
assert.ok(operation.responses['400']);
console.log(`OK: ${checks} requisições; contrato, paginação, busca, ordenações e rota legada.`);
