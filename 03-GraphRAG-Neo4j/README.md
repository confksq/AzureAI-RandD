# 03 — GraphRAG with Neo4j

> Knowledge-graph-enhanced RAG: entity/relationship extraction into Neo4j 5.x,
> Cypher traversal fused with vector search, benchmarked head-to-head against a
> standard FAISS baseline.

The interesting claim in GraphRAG is not "graphs are better." It is narrower and
more defensible: **vector search retrieves passages, and some answers do not
live in a passage.** When the chain runs patient → drug → formulary tier →
policy → appeal path → medical director, no single chunk's embedding sits near
the question, because no single chunk contains the chain. That is the gap this
module demonstrates, measures, and — in `compare.py` — also shows the limits of.

---

## What's here

| File | Role |
|---|---|
| `docker-compose.yml` | Neo4j 5 Community, Bolt + Browser, APOC preloaded |
| `ingest.py` | Documents → GPT-4o structured extraction → py2neo MERGE → Neo4j |
| `graph_rag.py` | Cypher traversal + FAISS, fused context → GPT-4o |
| `vector_rag.py` | FAISS baseline (the control arm) |
| `compare.py` | Both arms, same index, side-by-side with retrieval traces |
| `docs/` | Sample corpus — a payer prior-authorization scenario across 3 documents |

The sample corpus is deliberately built so facts *span* documents: the patient
is in the clinical note, the drug's tier is in the formulary, the appeal path is
in the policy. One-hop questions are answerable from one file; the multi-hop
ones are not.

---

## Architecture

```
                        ┌──────────────────────────────┐
   docs/*.md ──────────►│  ingest.py                   │
                        │                              │
                        │  chunk (1800 / 250 overlap)  │
                        │           ↓                  │
                        │  GPT-4o structured output    │
                        │  (strict JSON schema)        │
                        │           ↓                  │
                        │  entities + relationships    │
                        └───────────┬──────────────────┘
                                    │ py2neo MERGE
                                    ▼
              ┌─────────────────────────────────────────────┐
              │            Neo4j 5.x  (:7687 bolt)          │
              │                                             │
              │   (:Entity {name,type,description})         │
              │        │                                    │
              │        ├─[:RELATES {type,description}]─►(:Entity)
              │        ▲                                    │
              │   [:MENTIONS]                               │
              │        │                                    │
              │   (:Chunk {id,text,source})                 │
              └─────────────────────┬───────────────────────┘
                                    │
   question ─────┬──────────────────┼──────────────────────────────┐
                 │                  │                              │
                 ▼                  ▼                              ▼
        ┌────────────────┐  ┌──────────────────┐          ┌────────────────┐
        │ LLM: which     │  │ Cypher           │          │ FAISS          │
        │ entities are   │─►│  seed match      │          │ top-k cosine   │
        │ named here?    │  │  1..2 hop walk   │          │ (same corpus)  │
        └────────────────┘  │  :MENTIONS chunks│          └───────┬────────┘
                            └────────┬─────────┘                  │
                                     │  graph facts + text        │
                                     └──────────┬─────────────────┘
                                                ▼
                                    ┌───────────────────────┐
                                    │  fused context        │
                                    │  → GPT-4o             │
                                    │  → grounded answer    │
                                    └───────────────────────┘
```

Both retrieval arms read the **same** FAISS index (`compare.py` builds one and
injects it into both), so any measured difference comes from the graph and not
from a chunking accident.

---

## When to use GraphRAG vs standard vector RAG

| Signal in your workload | Vector RAG | GraphRAG | Why |
|---|---|---|---|
| Answer sits in one passage | ✅ **Use this** | Overkill | Embedding similarity is already the right tool |
| Multi-hop: A→B→C across documents | ❌ Structurally fails | ✅ **Use this** | No chunk embeds the whole chain |
| "How are X and Y related?" | ❌ Weak | ✅ **Use this** | Relationship *is* the answer; it is an edge, not text |
| Aggregation: "all drugs governed by PA-114" | ❌ Recall-capped by k | ✅ **Use this** | Cypher enumerates exhaustively; top-k truncates |
| Summarize a whole corpus theme | ⚠️ Chunk-limited | ✅ Community detection | Microsoft's global-search pattern |
| Contradiction / consistency checks | ❌ | ✅ | Conflicting edges are visible; conflicting chunks are not |
| Corpus < ~50 documents | ✅ **Use this** | Not worth it | Extraction cost exceeds the benefit |
| Corpus churns hourly | ✅ **Use this** | ⚠️ Painful | Re-extraction is expensive; graphs drift stale |
| Entities are fuzzy / not nameable | ✅ **Use this** | ❌ Fails | Extraction needs canonical, referenceable entities |
| Latency budget < 500 ms | ✅ **Use this** | ⚠️ Tight | Traversal adds an LLM call plus round-trips |
| Need provenance / auditability | ⚠️ Chunk-level | ✅ **Use this** | Every edge carries its source document |
| Team has no graph skills | ✅ **Use this** | ⚠️ Real cost | Cypher and modeling are genuine ramp-up |

**The honest summary:** GraphRAG costs one extraction pass over the corpus, a
database to run, and an extra LLM call per query. It buys multi-hop and
aggregation. If your questions are single-hop lookups, it is a strictly worse
vector RAG with more moving parts. `compare.py` includes questions in both
categories on purpose.

**Hybrid is usually the real answer.** `graph_rag.py` runs both arms and fuses
them — the graph half catches structure, the vector half catches vocabulary the
extractor never entitized. Shipping only the graph arm is a common and costly
mistake.

---

## Docker setup

```bash
# 1. Start Neo4j
docker compose up -d

# 2. Wait for health (Bolt lags the HTTP port by a few seconds)
docker compose ps
# wait until STATUS shows (healthy)

# 3. Browser UI — login neo4j / password123
open http://localhost:7474
```

```bash
# 4. Python environment
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt

# 5. Configure
cp .env.example .env
# edit .env — set OPENAI_API_KEY

# 6. Build the graph (~10 LLM calls on the sample corpus)
python ingest.py --reset

# 7. Query
python vector_rag.py "What tier is adalimumab on?"
python graph_rag.py  "If Maria Delgado is denied for step therapy, who reviews the appeal?"

# 8. The point of the module
python compare.py
```

Inspect what was built, in the Neo4j Browser:

```cypher
// everything
MATCH (n)-[r]->(m) RETURN n, r, m LIMIT 100

// entity graph only
MATCH (a:Entity)-[r]->(b:Entity) RETURN a, r, b

// the multi-hop chain the benchmark question walks
MATCH path = (p:Entity {name: "Maria Delgado"})-[*1..4]-(d:Entity)
WHERE d.type = "PERSON" AND d.name <> "Maria Delgado"
RETURN path LIMIT 25
```

Teardown — `docker compose down` keeps the volume, `-v` destroys it:

```bash
docker compose down       # stop, keep graph
docker compose down -v    # stop, delete graph
```

---

## Implementation notes

**On "Microsoft GraphRAG."** This module implements the GraphRAG *pattern* from
Microsoft Research — LLM entity/relationship extraction, graph-structured
retrieval, fused context — backed by Neo4j. It does not vendor the
[`microsoft/graphrag`](https://github.com/microsoft/graphrag) pip package, which
is a different animal: it runs its own indexing pipeline into Parquet files,
uses Leiden community detection for global search, and does not target Neo4j as
a store. Building the pattern directly is the better teaching artifact — you can
see every Cypher query — but if the question is "have you used the Microsoft
library," the accurate answer is that this is the pattern, not that package.
Community detection / global search is the main capability the package has that
this module does not.

**On py2neo.** `ingest.py` uses py2neo as specified. Worth knowing: py2neo was
archived by its maintainer in 2023 and is not officially supported against
Neo4j 5.x — it generally works for MERGE-style writes but is not something to
put in production in 2026. `graph_rag.py` therefore reads through the official
`neo4j` driver, which is maintained and is what a real deployment should use for
both paths. Both libraries are in `requirements.txt`. If py2neo throws on your
Neo4j 5 build, the fix is to port `write_chunk()` to parameterized Cypher via
the official driver — roughly a 20-line change.

**On extraction quality.** Entity resolution here is `MERGE` on an exact
lowercase name, which is the naive approach. Real corpora need alias resolution
("Dr. Raman" / "Priya Raman" / "P. Raman" are one node). That is the single
biggest quality lever in any production GraphRAG system and is deliberately left
visible rather than hidden behind a library.

---

## Skills demonstrated

- **Knowledge graph construction from unstructured text** — LLM extraction with
  strict JSON schema output, so parsing never becomes the failure mode
- **Neo4j 5.x data modeling** — entity/chunk dual model, uniqueness constraints,
  `MENTIONS` provenance edges linking every fact back to its source document
- **Cypher** — fuzzy seed matching, variable-length path traversal (`*1..2`),
  aggregation-ordered retrieval
- **Hybrid retrieval design** — fusing symbolic traversal with dense vector
  search, and understanding why neither alone suffices
- **Structured LLM output** — `json_schema` with `strict: true` for reliable
  extraction and question-entity parsing
- **Honest baseline benchmarking** — shared index across both arms, retrieval
  traces printed, questions included where the sophisticated approach does *not*
  win
- **Containerized infrastructure** — Docker Compose with health checks and
  volume-backed persistence
- **Production judgment** — documented deprecation risk (py2neo), entity
  resolution as the real quality lever, and an explicit cost/benefit table
  rather than uncritical advocacy

---

## Stack

Python 3.11+ · Neo4j 5.x Community · py2neo · neo4j-driver · OpenAI GPT-4o ·
text-embedding-3-small · FAISS · LangChain · Docker Compose
