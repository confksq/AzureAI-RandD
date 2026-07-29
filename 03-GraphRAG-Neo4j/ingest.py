"""
ingest.py — build a knowledge graph in Neo4j from unstructured documents.

Pipeline
--------
    docs/*.md ──chunk──> GPT-4o structured extraction ──> py2neo MERGE ──> Neo4j

Graph shape produced:

    (:Entity {name, type, description})
        -[:RELATES {type, description, source}]-> (:Entity)

    (:Chunk {id, text, source, ordinal})
        -[:MENTIONS]-> (:Entity)

Entity nodes are MERGEd on `name`, so the same entity appearing in three
documents collapses to one node with three inbound :MENTIONS edges. That
collapse is the whole point — it is what lets a query hop from a patient in one
document to an appeals policy in another.

Usage
-----
    python ingest.py                 # ingest ./docs, keep existing graph
    python ingest.py --reset         # wipe the graph first
    python ingest.py --docs ./other  # ingest a different folder
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys
from dataclasses import dataclass, field

from dotenv import load_dotenv
from openai import OpenAI
from py2neo import Graph, Node, Relationship

load_dotenv()

CHAT_MODEL = os.getenv("OPENAI_CHAT_MODEL", "gpt-4o")
CHUNK_CHARS = 1800
CHUNK_OVERLAP = 250

# Structured-output schema. strict=True forces the model to return exactly this
# shape, which removes the "parse the JSON out of a prose reply" failure mode
# that makes naive extraction pipelines flaky.
EXTRACTION_SCHEMA = {
    "type": "object",
    "properties": {
        "entities": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "type": {"type": "string"},
                    "description": {"type": "string"},
                },
                "required": ["name", "type", "description"],
                "additionalProperties": False,
            },
        },
        "relationships": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "source": {"type": "string"},
                    "target": {"type": "string"},
                    "type": {"type": "string"},
                    "description": {"type": "string"},
                },
                "required": ["source", "target", "type", "description"],
                "additionalProperties": False,
            },
        },
    },
    "required": ["entities", "relationships"],
    "additionalProperties": False,
}

EXTRACTION_PROMPT = """\
You extract a knowledge graph from a document excerpt.

Rules:
- Entities are concrete, referenceable things: people, organizations, drugs,
  policies, conditions, codes, plans, committees. Not adjectives, not dates.
- `name` must be the canonical surface form as written in the text, so the same
  entity in two documents produces the same string. Do not abbreviate, do not
  add titles. Write "Priya Raman", not "Dr. Raman".
- `type` is a short upper-snake label: PERSON, ORGANIZATION, DRUG, CONDITION,
  POLICY, PLAN, CODE, PROCEDURE.
- Relationships must connect two entity names you listed in `entities`.
- `type` on a relationship is a short upper-snake verb phrase: PRESCRIBED,
  TREATS, REQUIRES, GOVERNED_BY, REVIEWED_BY, ESCALATES_TO, LISTED_ON,
  PREREQUISITE_FOR, EMPLOYED_BY, DIAGNOSED_WITH.
- Prefer fewer, load-bearing relationships over many weak ones.
- Extract only what the excerpt states. Do not infer.
"""


@dataclass
class Chunk:
    id: str
    text: str
    source: str
    ordinal: int


@dataclass
class Extraction:
    entities: list[dict] = field(default_factory=list)
    relationships: list[dict] = field(default_factory=list)


def load_chunks(docs_dir: str) -> list[Chunk]:
    """Read every .md/.txt file under docs_dir and split into overlapping chunks."""
    paths = sorted(
        glob.glob(os.path.join(docs_dir, "**", "*.md"), recursive=True)
        + glob.glob(os.path.join(docs_dir, "**", "*.txt"), recursive=True)
    )
    if not paths:
        raise SystemExit(f"No .md or .txt files found under {docs_dir!r}")

    chunks: list[Chunk] = []
    for path in paths:
        with open(path, encoding="utf-8") as fh:
            text = fh.read().strip()
        source = os.path.basename(path)

        start, ordinal = 0, 0
        while start < len(text):
            piece = text[start : start + CHUNK_CHARS]
            chunks.append(
                Chunk(id=f"{source}#{ordinal}", text=piece, source=source, ordinal=ordinal)
            )
            ordinal += 1
            if start + CHUNK_CHARS >= len(text):
                break
            start += CHUNK_CHARS - CHUNK_OVERLAP

    return chunks


def extract(client: OpenAI, chunk: Chunk) -> Extraction:
    """Ask the LLM for entities and relationships in one structured call."""
    response = client.chat.completions.create(
        model=CHAT_MODEL,
        temperature=0,
        messages=[
            {"role": "system", "content": EXTRACTION_PROMPT},
            {
                "role": "user",
                "content": f"Document: {chunk.source}\n\n---\n{chunk.text}\n---",
            },
        ],
        response_format={
            "type": "json_schema",
            "json_schema": {
                "name": "graph_extraction",
                "strict": True,
                "schema": EXTRACTION_SCHEMA,
            },
        },
    )
    payload = json.loads(response.choices[0].message.content)
    return Extraction(
        entities=payload.get("entities", []),
        relationships=payload.get("relationships", []),
    )


def rel_type(raw: str) -> str:
    """Neo4j relationship types must be identifier-safe."""
    cleaned = re.sub(r"[^A-Za-z0-9_]+", "_", raw or "").strip("_").upper()
    return cleaned or "RELATES"


def connect() -> Graph:
    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    user = os.getenv("NEO4J_USER", "neo4j")
    password = os.getenv("NEO4J_PASSWORD", "password123")
    graph = Graph(uri, auth=(user, password))
    graph.run("RETURN 1").data()  # fail fast if the container is not up yet
    return graph


def ensure_constraints(graph: Graph) -> None:
    """A uniqueness constraint on Entity.name is what makes MERGE cheap and safe."""
    graph.run(
        "CREATE CONSTRAINT entity_name IF NOT EXISTS "
        "FOR (e:Entity) REQUIRE e.name IS UNIQUE"
    )
    graph.run(
        "CREATE CONSTRAINT chunk_id IF NOT EXISTS "
        "FOR (c:Chunk) REQUIRE c.id IS UNIQUE"
    )


def write_chunk(graph: Graph, chunk: Chunk, ext: Extraction) -> tuple[int, int]:
    """MERGE one chunk's entities and relationships into the graph."""
    tx = graph.begin()

    chunk_node = Node(
        "Chunk", id=chunk.id, text=chunk.text, source=chunk.source, ordinal=chunk.ordinal
    )
    tx.merge(chunk_node, "Chunk", "id")

    nodes: dict[str, Node] = {}
    for ent in ext.entities:
        name = (ent.get("name") or "").strip()
        if not name:
            continue
        node = Node(
            "Entity",
            name=name,
            type=(ent.get("type") or "UNKNOWN").strip().upper(),
            description=(ent.get("description") or "").strip(),
        )
        tx.merge(node, "Entity", "name")
        nodes[name.lower()] = node
        tx.merge(Relationship(chunk_node, "MENTIONS", node))

    edges = 0
    for rel in ext.relationships:
        src = nodes.get((rel.get("source") or "").strip().lower())
        dst = nodes.get((rel.get("target") or "").strip().lower())
        if src is None or dst is None:
            # The model referenced an entity it did not list. Skip rather than
            # invent a node — a phantom node is worse than a missing edge.
            continue
        tx.merge(
            Relationship(
                src,
                rel_type(rel.get("type")),
                dst,
                description=(rel.get("description") or "").strip(),
                source=chunk.source,
            )
        )
        edges += 1

    graph.commit(tx)
    return len(nodes), edges


def reset(graph: Graph) -> None:
    graph.run("MATCH (n) DETACH DELETE n")
    print("Graph wiped.")


def summarize(graph: Graph) -> None:
    entities = graph.run("MATCH (e:Entity) RETURN count(e) AS n").evaluate()
    chunks = graph.run("MATCH (c:Chunk) RETURN count(c) AS n").evaluate()
    rels = graph.run(
        "MATCH (:Entity)-[r]->(:Entity) RETURN count(r) AS n"
    ).evaluate()
    print(f"\nGraph now holds {entities} entities, {rels} entity relationships, {chunks} chunks.")
    print("Browse it at http://localhost:7474 — try:  MATCH (n)-[r]->(m) RETURN n,r,m LIMIT 50")


def main() -> int:
    parser = argparse.ArgumentParser(description="Build a Neo4j knowledge graph from documents.")
    parser.add_argument("--docs", default="docs", help="folder of .md/.txt files (default: docs)")
    parser.add_argument("--reset", action="store_true", help="delete the existing graph first")
    args = parser.parse_args()

    if not os.getenv("OPENAI_API_KEY"):
        print("OPENAI_API_KEY is not set. Copy .env.example to .env first.", file=sys.stderr)
        return 1

    client = OpenAI()
    graph = connect()
    if args.reset:
        reset(graph)
    ensure_constraints(graph)

    chunks = load_chunks(args.docs)
    print(f"Ingesting {len(chunks)} chunks from {args.docs}/ using {CHAT_MODEL}\n")

    total_entities = total_edges = 0
    for i, chunk in enumerate(chunks, 1):
        ext = extract(client, chunk)
        n_ent, n_rel = write_chunk(graph, chunk, ext)
        total_entities += n_ent
        total_edges += n_rel
        print(f"  [{i}/{len(chunks)}] {chunk.id:<28} {n_ent:>3} entities  {n_rel:>3} relationships")

    print(f"\nExtracted {total_entities} entity mentions and {total_edges} relationships.")
    summarize(graph)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
