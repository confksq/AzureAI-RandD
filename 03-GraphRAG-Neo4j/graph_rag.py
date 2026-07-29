"""
graph_rag.py — hybrid retrieval: Cypher graph traversal + FAISS vector search.

Retrieval strategy
------------------
    question
       ├── LLM extracts the entities named in the question
       ├── Cypher: fuzzy-match those entities in Neo4j                → seed nodes
       ├── Cypher: traverse 1..2 hops from the seeds                  → fact triples
       ├── Cypher: pull :Chunk nodes that MENTION any touched entity  → grounded text
       └── FAISS: top-k cosine similarity over the same corpus        → semantic text
                                    ↓
                     merged context ──> GPT-4o ──> answer

The graph half answers "what is connected to what" — the part vector search
structurally cannot do, because a two-hop connection has no single chunk whose
embedding is near the question. The vector half keeps recall up when the
question uses vocabulary the extractor never turned into an entity. Neither
alone is enough, which is the actual lesson of this module.

Uses the official `neo4j` driver (not py2neo) for reads — see README.

Usage
-----
    python graph_rag.py "If Maria Delgado is denied, who reviews the appeal?"
"""

from __future__ import annotations

import json
import os
import sys

from dotenv import load_dotenv
from langchain_openai import ChatOpenAI
from neo4j import GraphDatabase

from vector_rag import RAGResult, build_vector_store

load_dotenv()

CHAT_MODEL = os.getenv("OPENAI_CHAT_MODEL", "gpt-4o")
MAX_HOPS = 2
MAX_FACTS = 60
MAX_GRAPH_CHUNKS = 4
VECTOR_K = 4

ENTITY_SCHEMA = {
    "type": "object",
    "properties": {
        "entities": {"type": "array", "items": {"type": "string"}},
    },
    "required": ["entities"],
    "additionalProperties": False,
}

# Seed lookup. Exact match first, substring match as a fallback, because the
# question rarely spells an entity exactly the way the extractor stored it.
SEED_CYPHER = """
UNWIND $names AS name
MATCH (e:Entity)
WHERE toLower(e.name) = toLower(name)
   OR toLower(e.name) CONTAINS toLower(name)
   OR toLower(name) CONTAINS toLower(e.name)
RETURN DISTINCT e.name AS name, e.type AS type, e.description AS description
LIMIT 25
"""

# Variable-length traversal from the seeds. Returns each hop as a readable
# triple so the LLM sees relationships, not adjacency matrices.
TRAVERSE_CYPHER = """
MATCH (seed:Entity) WHERE seed.name IN $seeds
MATCH path = (seed)-[rels*1..%d]-(other:Entity)
UNWIND relationships(path) AS r
WITH DISTINCT startNode(r) AS a, r, endNode(r) AS b
RETURN a.name AS source,
       type(r) AS relationship,
       b.name AS target,
       coalesce(r.description, '') AS description,
       coalesce(r.source, '') AS document
LIMIT $limit
"""

# Text evidence attached to whatever the traversal touched.
CHUNK_CYPHER = """
MATCH (c:Chunk)-[:MENTIONS]->(e:Entity)
WHERE e.name IN $names
WITH c, count(DISTINCT e) AS hits
RETURN c.text AS text, c.source AS source, hits
ORDER BY hits DESC
LIMIT $limit
"""

ANSWER_PROMPT = """\
Answer the question using only the evidence below. If the evidence does not
contain the answer, say so plainly — do not fill the gap from general
knowledge. Cite the source filename in brackets after each claim.

You are given two kinds of evidence. GRAPH FACTS are extracted relationships
and are authoritative for how things connect. DOCUMENT EXCERPTS carry the
detail and wording. Use the facts to follow the chain, the excerpts to justify
it.

=== GRAPH FACTS ===
{facts}

=== DOCUMENT EXCERPTS ===
{context}

Question: {question}
"""


class GraphRAG:
    def __init__(self, docs_dir: str = "docs", store=None):
        uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
        user = os.getenv("NEO4J_USER", "neo4j")
        password = os.getenv("NEO4J_PASSWORD", "password123")
        self.driver = GraphDatabase.driver(uri, auth=(user, password))
        self.driver.verify_connectivity()
        self.llm = ChatOpenAI(model=CHAT_MODEL, temperature=0)
        self.store = store or build_vector_store(docs_dir)

    def close(self) -> None:
        self.driver.close()

    # -- retrieval steps ---------------------------------------------------

    def question_entities(self, question: str) -> list[str]:
        """Ask the LLM which entities the question actually names."""
        reply = self.llm.invoke(
            [
                {
                    "role": "system",
                    "content": (
                        "List the named entities in the user's question — people, "
                        "drugs, policies, organizations, conditions, plans. Return "
                        "the surface forms as written. No titles, no adjectives."
                    ),
                },
                {"role": "user", "content": question},
            ],
            response_format={
                "type": "json_schema",
                "json_schema": {
                    "name": "question_entities",
                    "strict": True,
                    "schema": ENTITY_SCHEMA,
                },
            },
        )
        try:
            return json.loads(reply.content).get("entities", [])
        except json.JSONDecodeError:
            return []

    def seeds(self, names: list[str]) -> list[dict]:
        if not names:
            return []
        with self.driver.session() as session:
            return [dict(r) for r in session.run(SEED_CYPHER, names=names)]

    def traverse(self, seed_names: list[str]) -> list[dict]:
        if not seed_names:
            return []
        cypher = TRAVERSE_CYPHER % MAX_HOPS
        with self.driver.session() as session:
            return [dict(r) for r in session.run(cypher, seeds=seed_names, limit=MAX_FACTS)]

    def graph_chunks(self, names: list[str]) -> list[dict]:
        if not names:
            return []
        with self.driver.session() as session:
            return [
                dict(r)
                for r in session.run(CHUNK_CYPHER, names=names, limit=MAX_GRAPH_CHUNKS)
            ]

    # -- orchestration -----------------------------------------------------

    def answer(self, question: str) -> RAGResult:
        asked = self.question_entities(question)
        seeds = self.seeds(asked)
        seed_names = [s["name"] for s in seeds]

        facts = self.traverse(seed_names)

        # Every entity the traversal reached is fair game for text evidence —
        # this is how a chunk from a document the question never named gets
        # pulled in. That is the multi-hop payoff.
        touched = sorted({f["source"] for f in facts} | {f["target"] for f in facts} | set(seed_names))
        g_chunks = self.graph_chunks(touched)

        v_hits = self.store.similarity_search(question, k=VECTOR_K)

        fact_lines = (
            "\n".join(
                f"- ({f['source']}) -[{f['relationship']}]-> ({f['target']})"
                + (f"  // {f['description']}" if f["description"] else "")
                + (f"  [{f['document']}]" if f["document"] else "")
                for f in facts
            )
            or "(no graph facts matched)"
        )

        seen: set[str] = set()
        blocks: list[str] = []
        for c in g_chunks:
            key = c["text"][:80]
            if key not in seen:
                seen.add(key)
                blocks.append(f"[{c['source']}] (via graph)\n{c['text']}")
        for d in v_hits:
            key = d.page_content[:80]
            if key not in seen:
                seen.add(key)
                blocks.append(f"[{d.metadata.get('source', '?')}] (via vector)\n{d.page_content}")

        reply = self.llm.invoke(
            ANSWER_PROMPT.format(
                facts=fact_lines, context="\n\n".join(blocks), question=question
            )
        )

        contexts = [f"{c['source']} — graph, {c['hits']} entity hits" for c in g_chunks]
        contexts += [f"{d.metadata.get('source', '?')} — vector" for d in v_hits]

        return RAGResult(
            answer=reply.content.strip(),
            contexts=contexts,
            label="GraphRAG",
            notes=[
                f"Question entities: {', '.join(asked) or '(none)'}",
                f"Seed nodes matched: {len(seeds)}",
                f"Graph facts traversed ({MAX_HOPS} hops): {len(facts)}",
                f"Entities reached: {len(touched)}",
                f"Chunks: {len(g_chunks)} via graph + {len(v_hits)} via vector",
            ],
        )


def main() -> int:
    if len(sys.argv) < 2:
        print('Usage: python graph_rag.py "your question"', file=sys.stderr)
        return 1
    if not os.getenv("OPENAI_API_KEY"):
        print("OPENAI_API_KEY is not set. Copy .env.example to .env first.", file=sys.stderr)
        return 1

    question = " ".join(sys.argv[1:])
    rag = GraphRAG()
    try:
        result = rag.answer(question)
    finally:
        rag.close()

    print(f"\nQ: {question}\n")
    print(result.answer)
    print("\n--- retrieval trace ---")
    for note in result.notes:
        print(f"  {note}")
    for c in result.contexts:
        print(f"  · {c}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
