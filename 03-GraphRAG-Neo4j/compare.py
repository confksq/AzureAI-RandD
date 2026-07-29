"""
compare.py — run one question through both arms and print them side by side.

The point of this file is honesty. It shares a single FAISS index between both
arms, so the only variable is whether graph traversal is in the loop. Questions
where GraphRAG wins and questions where it does not both get printed, because
"when does this *not* pay for itself" is the part interviewers actually probe.

Usage
-----
    python compare.py                       # run the built-in question set
    python compare.py "your own question"   # run one question
    python compare.py --width 60            # narrower columns
"""

from __future__ import annotations

import argparse
import os
import sys
import textwrap

from dotenv import load_dotenv

from graph_rag import GraphRAG
from vector_rag import VectorRAG, build_vector_store

load_dotenv()

# Ordered from "vector RAG handles this fine" to "vector RAG structurally cannot".
BENCHMARK = [
    (
        "What tier is adalimumab on?",
        "Single fact, single chunk. Vector RAG should tie or win — it is cheaper.",
    ),
    (
        "Why does Maria Delgado's prescription need prior authorization?",
        "Two documents, one hop. Vector RAG often gets this if both chunks retrieve.",
    ),
    (
        "If Maria Delgado's adalimumab request is denied for step therapy, "
        "who reviews the appeal and what evidence must it include?",
        "Three documents, multi-hop. No single chunk contains the chain: "
        "patient -> drug -> tier -> PA-114 -> appeal path -> medical director.",
    ),
    (
        "Does Dr. Priya Raman meet the prescriber requirement for this drug?",
        "Requires joining a fact in the clinical note to a criterion in the policy. "
        "The two never co-occur in one chunk.",
    ),
]


def render(text: str, width: int) -> list[str]:
    lines: list[str] = []
    for para in text.splitlines():
        if not para.strip():
            lines.append("")
            continue
        lines.extend(textwrap.wrap(para, width=width) or [""])
    return lines


def side_by_side(left: str, right: str, width: int) -> str:
    l_lines = render(left, width)
    r_lines = render(right, width)
    height = max(len(l_lines), len(r_lines))
    l_lines += [""] * (height - len(l_lines))
    r_lines += [""] * (height - len(r_lines))
    return "\n".join(f"{l:<{width}}  │  {r:<{width}}" for l, r in zip(l_lines, r_lines))


def run_one(question: str, why: str, vector: VectorRAG, graph: GraphRAG, width: int) -> None:
    total = width * 2 + 5
    print("\n" + "═" * total)
    print(textwrap.fill(f"Q: {question}", width=total))
    if why:
        print(textwrap.fill(f"   ({why})", width=total))
    print("═" * total)

    v = vector.answer(question)
    g = graph.answer(question)

    print(f"{'VECTOR RAG (FAISS)':<{width}}  │  {'GRAPHRAG (Neo4j + FAISS)':<{width}}")
    print("─" * width + "──┼──" + "─" * width)
    print(side_by_side(v.answer, g.answer, width))

    print("\n" + "─" * width + "──┼──" + "─" * width)
    v_trace = "\n".join(f"· {c}" for c in v.contexts)
    g_trace = "\n".join(g.notes + [f"· {c}" for c in g.contexts])
    print(side_by_side(v_trace, g_trace, width))

    v_docs = {c.split(" — ")[0] for c in v.contexts}
    g_docs = {c.split(" — ")[0] for c in g.contexts}
    extra = g_docs - v_docs
    if extra:
        print(f"\n  → GraphRAG surfaced documents vector search missed: {', '.join(sorted(extra))}")
    else:
        print("\n  → Both arms retrieved the same documents. Any difference is in synthesis, "
              "not retrieval — the graph is not earning its keep on this question.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare GraphRAG against vector RAG.")
    parser.add_argument("question", nargs="*", help="a single question (default: benchmark set)")
    parser.add_argument("--width", type=int, default=52, help="column width (default: 52)")
    parser.add_argument("--docs", default="docs", help="document folder (default: docs)")
    args = parser.parse_args()

    if not os.getenv("OPENAI_API_KEY"):
        print("OPENAI_API_KEY is not set. Copy .env.example to .env first.", file=sys.stderr)
        return 1

    # One shared index — both arms see identical chunks, so the comparison is fair.
    print("Building shared FAISS index...")
    store = build_vector_store(args.docs)
    vector = VectorRAG(store=store)
    graph = GraphRAG(store=store)

    try:
        if args.question:
            run_one(" ".join(args.question), "", vector, graph, args.width)
        else:
            for question, why in BENCHMARK:
                run_one(question, why, vector, graph, args.width)
    finally:
        graph.close()

    print("\nRead the traces, not just the answers. GraphRAG wins when the retrieval")
    print("trace shows documents the vector arm never touched.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
