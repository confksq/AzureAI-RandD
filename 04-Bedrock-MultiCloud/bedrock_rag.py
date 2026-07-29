"""
bedrock_rag.py — end-to-end RAG on Amazon Bedrock.

    docs/*.md ──chunk──> Titan Embeddings v2 ──> FAISS ──> Claude (Bedrock) ──> answer

Every hop runs on AWS except the vector store, which is local FAISS. That is
deliberate: it isolates the *model* comparison against Azure from the *vector
store* comparison. Swap FAISS for OpenSearch Serverless or Aurora pgvector and
the retrieval code below is the only thing that changes.

`TitanEmbeddings` implements LangChain's `Embeddings` interface directly over
boto3 rather than pulling in `langchain-aws`, which keeps the dependency list
to what the module actually declares and leaves the Bedrock call visible.

Usage
-----
    python bedrock_rag.py ingest                    # build and persist the index
    python bedrock_rag.py query "your question"     # single question
    python bedrock_rag.py chat                      # interactive loop
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import sys

from botocore.exceptions import ClientError
from dotenv import load_dotenv
from langchain_community.vectorstores import FAISS
from langchain_core.documents import Document
from langchain_core.embeddings import Embeddings
from langchain_text_splitters import RecursiveCharacterTextSplitter

from bedrock_client import explain_error, invoke_stream, make_client

load_dotenv()

EMBED_MODEL_ID = os.getenv("BEDROCK_EMBED_MODEL_ID", "amazon.titan-embed-text-v2:0")
INDEX_DIR = "faiss_index"
CHUNK_CHARS = 1500
CHUNK_OVERLAP = 200
TOP_K = 4
EMBED_DIMENSIONS = 1024  # Titan v2 supports 256 / 512 / 1024

SYSTEM_PROMPT = """\
You answer questions using only the context provided in the user message. If the
context does not contain the answer, say so plainly rather than filling the gap
from general knowledge. Cite the source filename in brackets after each claim.
Be direct and concise."""

ANSWER_TEMPLATE = """\
Context:
{context}

Question: {question}"""


class TitanEmbeddings(Embeddings):
    """Amazon Titan Text Embeddings v2 via bedrock-runtime.

    Titan embeds one string per call — there is no batch endpoint — so
    `embed_documents` loops. On a large corpus this is the slow step and the
    place to add concurrency; it is left sequential here so the per-call
    request shape stays readable.
    """

    def __init__(self, client=None, model_id: str = EMBED_MODEL_ID, dimensions: int = EMBED_DIMENSIONS):
        self.client = client or make_client()
        self.model_id = model_id
        self.dimensions = dimensions

    def _embed(self, text: str) -> list[float]:
        response = self.client.invoke_model(
            modelId=self.model_id,
            body=json.dumps(
                {
                    "inputText": text,
                    "dimensions": self.dimensions,
                    # Normalized vectors make cosine similarity equivalent to a
                    # dot product, which is what FAISS IndexFlatL2 wants.
                    "normalize": True,
                }
            ),
            contentType="application/json",
            accept="application/json",
        )
        return json.loads(response["body"].read())["embedding"]

    def embed_documents(self, texts: list[str]) -> list[list[float]]:
        out = []
        for i, text in enumerate(texts, 1):
            out.append(self._embed(text))
            print(f"  embedded {i}/{len(texts)}", end="\r", file=sys.stderr)
        print(file=sys.stderr)
        return out

    def embed_query(self, text: str) -> list[float]:
        return self._embed(text)


def load_chunks(docs_dir: str = "docs") -> list[Document]:
    paths = sorted(
        glob.glob(os.path.join(docs_dir, "**", "*.md"), recursive=True)
        + glob.glob(os.path.join(docs_dir, "**", "*.txt"), recursive=True)
    )
    if not paths:
        raise SystemExit(f"No .md or .txt files found under {docs_dir!r}")

    docs = []
    for path in paths:
        with open(path, encoding="utf-8") as fh:
            docs.append(Document(page_content=fh.read(), metadata={"source": os.path.basename(path)}))

    splitter = RecursiveCharacterTextSplitter(chunk_size=CHUNK_CHARS, chunk_overlap=CHUNK_OVERLAP)
    return splitter.split_documents(docs)


def ingest(docs_dir: str = "docs", index_dir: str = INDEX_DIR) -> FAISS:
    chunks = load_chunks(docs_dir)
    print(f"Embedding {len(chunks)} chunks with {EMBED_MODEL_ID}", file=sys.stderr)
    store = FAISS.from_documents(chunks, TitanEmbeddings())
    store.save_local(index_dir)
    print(f"Index saved to {index_dir}/", file=sys.stderr)
    return store


def load_index(index_dir: str = INDEX_DIR) -> FAISS:
    if not os.path.isdir(index_dir):
        raise SystemExit(f"No index at {index_dir}/ — run:  python bedrock_rag.py ingest")
    # The index is a pickle we wrote ourselves one step earlier in this same
    # process tree; the flag is required because FAISS deserialization is unsafe
    # for indexes from untrusted sources. Never set it on a downloaded index.
    return FAISS.load_local(index_dir, TitanEmbeddings(), allow_dangerous_deserialization=True)


def answer(store: FAISS, question: str, k: int = TOP_K) -> None:
    hits = store.similarity_search(question, k=k)
    context = "\n\n".join(f"[{d.metadata.get('source', '?')}]\n{d.page_content}" for d in hits)

    client = make_client()
    for piece in invoke_stream(
        client,
        ANSWER_TEMPLATE.format(context=context, question=question),
        system=SYSTEM_PROMPT,
    ):
        print(piece, end="", flush=True)
    print()

    print("\n--- retrieved ---", file=sys.stderr)
    for d in hits:
        print(f"  · {d.metadata.get('source', '?')}: {d.page_content[:90]}...", file=sys.stderr)


def main() -> int:
    parser = argparse.ArgumentParser(description="RAG over Amazon Bedrock (Titan + FAISS + Claude).")
    sub = parser.add_subparsers(dest="command", required=True)

    p_ingest = sub.add_parser("ingest", help="embed docs/ and persist the FAISS index")
    p_ingest.add_argument("--docs", default="docs")

    p_query = sub.add_parser("query", help="ask one question")
    p_query.add_argument("question", nargs="+")

    sub.add_parser("chat", help="interactive question loop")

    args = parser.parse_args()

    try:
        if args.command == "ingest":
            ingest(args.docs)
        elif args.command == "query":
            answer(load_index(), " ".join(args.question))
        elif args.command == "chat":
            store = load_index()
            print("Ask a question, or Ctrl-C to exit.\n", file=sys.stderr)
            while True:
                try:
                    question = input("> ").strip()
                except (EOFError, KeyboardInterrupt):
                    print(file=sys.stderr)
                    break
                if question:
                    answer(store, question)
                    print()
    except ClientError as err:
        print(explain_error(err), file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
