"""
vector_rag.py — standard vector RAG baseline (FAISS + GPT-4o).

This is the control arm of the experiment. It is deliberately a *good* vector
RAG implementation, not a strawman: recursive chunking, cosine similarity over
text-embedding-3-small, top-k retrieval, grounded answer prompt. If GraphRAG is
going to justify its extra infrastructure, it has to beat this.

`build_vector_store()` is imported by graph_rag.py so both arms retrieve from
exactly the same index. Any quality difference then comes from the graph, not
from a chunking accident.

Usage
-----
    python vector_rag.py "Why does Maria Delgado need prior authorization?"
"""

from __future__ import annotations

import os
import sys
from dataclasses import dataclass, field

from dotenv import load_dotenv
from langchain_community.vectorstores import FAISS
from langchain_core.documents import Document
from langchain_openai import ChatOpenAI, OpenAIEmbeddings
from langchain_text_splitters import RecursiveCharacterTextSplitter

load_dotenv()

CHAT_MODEL = os.getenv("OPENAI_CHAT_MODEL", "gpt-4o")
EMBED_MODEL = os.getenv("OPENAI_EMBED_MODEL", "text-embedding-3-small")
CHUNK_CHARS = 1800
CHUNK_OVERLAP = 250
TOP_K = 4

ANSWER_PROMPT = """\
Answer the question using only the context below. If the context does not
contain the answer, say so plainly — do not fill the gap from general
knowledge. Cite the source filename in brackets after each claim.

Context:
{context}

Question: {question}
"""


@dataclass
class RAGResult:
    """Shared return shape so compare.py can treat both arms identically."""

    answer: str
    contexts: list[str] = field(default_factory=list)
    label: str = "Vector RAG"
    notes: list[str] = field(default_factory=list)


def load_documents(docs_dir: str = "docs") -> list[Document]:
    """Read docs_dir into LangChain Documents, one per file, split into chunks."""
    import glob

    paths = sorted(
        glob.glob(os.path.join(docs_dir, "**", "*.md"), recursive=True)
        + glob.glob(os.path.join(docs_dir, "**", "*.txt"), recursive=True)
    )
    if not paths:
        raise SystemExit(f"No .md or .txt files found under {docs_dir!r}")

    docs: list[Document] = []
    for path in paths:
        with open(path, encoding="utf-8") as fh:
            docs.append(
                Document(page_content=fh.read(), metadata={"source": os.path.basename(path)})
            )

    splitter = RecursiveCharacterTextSplitter(
        chunk_size=CHUNK_CHARS, chunk_overlap=CHUNK_OVERLAP
    )
    return splitter.split_documents(docs)


def build_vector_store(docs_dir: str = "docs") -> FAISS:
    """Embed every chunk into an in-memory FAISS index."""
    chunks = load_documents(docs_dir)
    embeddings = OpenAIEmbeddings(model=EMBED_MODEL)
    return FAISS.from_documents(chunks, embeddings)


class VectorRAG:
    def __init__(self, docs_dir: str = "docs", store: FAISS | None = None):
        self.store = store or build_vector_store(docs_dir)
        self.llm = ChatOpenAI(model=CHAT_MODEL, temperature=0)

    def retrieve(self, question: str, k: int = TOP_K) -> list[Document]:
        return self.store.similarity_search(question, k=k)

    def answer(self, question: str, k: int = TOP_K) -> RAGResult:
        hits = self.retrieve(question, k=k)
        context = "\n\n".join(
            f"[{d.metadata.get('source', '?')}]\n{d.page_content}" for d in hits
        )
        reply = self.llm.invoke(
            ANSWER_PROMPT.format(context=context, question=question)
        )
        return RAGResult(
            answer=reply.content.strip(),
            contexts=[f"{d.metadata.get('source', '?')} — {d.page_content[:120]}..." for d in hits],
            label="Vector RAG",
            notes=[f"Retrieved top-{k} chunks by cosine similarity."],
        )


def main() -> int:
    if len(sys.argv) < 2:
        print('Usage: python vector_rag.py "your question"', file=sys.stderr)
        return 1
    if not os.getenv("OPENAI_API_KEY"):
        print("OPENAI_API_KEY is not set. Copy .env.example to .env first.", file=sys.stderr)
        return 1

    question = " ".join(sys.argv[1:])
    rag = VectorRAG()
    result = rag.answer(question)

    print(f"\nQ: {question}\n")
    print(result.answer)
    print("\n--- retrieved chunks ---")
    for c in result.contexts:
        print(f"  · {c}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
