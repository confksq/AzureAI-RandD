# 04 — Bedrock Multi-Cloud

> Amazon Bedrock running alongside Azure AI Foundry: a full RAG pipeline on AWS
> (Titan Embeddings → FAISS → Claude), plus a 15-dimension platform comparison
> that argues for choosing on data gravity rather than benchmark scores.

Most "multi-cloud AI" material compares model quality. That is the least
decision-relevant axis — both platforms serve the same frontier models. What
actually differs is the shape of the work: how identity is granted, how RAG is
assembled, how guardrails attach, how many SDKs a team has to learn. This module
builds the AWS side end-to-end so the comparison is grounded in code that runs.

---

## What's here

| File | Role |
|---|---|
| `bedrock_client.py` | boto3 → `bedrock-runtime`, Claude invocation, streaming, real error handling |
| `bedrock_rag.py` | Titan Embeddings v2 → FAISS → Claude; ingest and query via CLI |
| `azure_vs_bedrock.md` | The 15-dimension comparison in full, with where the differences bite |
| `docs/` | Sample corpus — a multi-cloud ADR and Bedrock operational notes |

---

## Architecture

```
                        ┌──────────────────────────────────────┐
                        │            AWS account               │
                        │                                      │
   docs/*.md ──────────►│  bedrock_rag.py ingest               │
                        │    chunk (1500 / 200 overlap)        │
                        │           ↓                          │
                        │  ┌────────────────────────────────┐  │
                        │  │ amazon.titan-embed-text-v2:0   │  │
                        │  │ 1024-dim, normalized           │  │
                        │  └────────────┬───────────────────┘  │
                        │               ↓                      │
                        │        FAISS (local, on disk)        │
                        └───────────────┬──────────────────────┘
                                        │
   question ────────────────────────────┤
                                        ▼
                            top-k cosine similarity
                                        │
                                        ▼
                        ┌──────────────────────────────────────┐
                        │  bedrock-runtime                     │
                        │  invoke_model_with_response_stream   │
                        │    modelId: anthropic.claude-*       │
                        │    body:    Messages API shape       │
                        │             + anthropic_version      │
                        └───────────────┬──────────────────────┘
                                        ▼
                            streamed, grounded answer
                            + per-request token metrics
```

Everything runs on AWS except the vector store, which is local FAISS on purpose:
it isolates the *model* comparison from the *vector store* comparison. Swapping
FAISS for OpenSearch Serverless or Aurora pgvector touches only the retrieval
call.

---

## Azure AI Foundry vs Amazon Bedrock — 15 dimensions

| # | Dimension | Azure AI Foundry | Amazon Bedrock | Edge |
|---|---|---|---|---|
| 1 | Model selection | OpenAI (exclusive), Anthropic, Meta, Mistral, Cohere, Phi, wide Hugging Face surface | Anthropic, Amazon Nova/Titan, Meta, Mistral, Cohere, AI21, Stability | Azure |
| 2 | RAG support | Azure AI Search — integrated vectorization, hybrid retrieval, semantic reranker | Knowledge Bases over OpenSearch Serverless / Aurora pgvector / Pinecone | Azure |
| 3 | Agent framework | Foundry Agent Service, Semantic Kernel, AutoGen | Bedrock Agents / AgentCore, Strands Agents SDK | Even |
| 4 | Pricing model | Per-token or Provisioned Throughput Units; Claude billed via Microsoft Marketplace | Per-token, Provisioned Throughput (hourly, committed), Batch discount | Even |
| 5 | Security / compliance | Entra ID, private endpoints, VNet injection, CMK, Purview | IAM, PrivateLink, VPC endpoints, KMS, CloudTrail | Even |
| 6 | Fine-tuning | Broad — OpenAI plus much of the open-weight catalog | Select models only (Nova/Titan, Llama, Cohere); continued pre-training | Azure |
| 7 | Evaluation tools | Foundry Evaluations — groundedness, relevance, coherence, safety; CI-runnable | Model Evaluation (automatic + human); RAG evaluation for Knowledge Bases | Azure |
| 8 | SDK / API | `azure-ai-projects`, `azure-ai-inference`, OpenAI SDK, Semantic Kernel | `boto3` — one SDK, one auth model, one error taxonomy | Bedrock |
| 9 | Regional availability | Larger footprint; per-model availability varies sharply by region | Fewer regions; cross-region inference profiles trade residency for availability | Azure |
| 10 | Enterprise support | Microsoft EA, support tiers, FastTrack | AWS Enterprise Support, SAs, Enterprise Discount Program | Even |
| 11 | Vector search | Azure AI Search — hybrid (BM25 + vector, RRF) plus semantic reranker | OpenSearch Serverless default; pgvector / Pinecone / Redis / MongoDB | Azure |
| 12 | Observability | Azure Monitor, App Insights, OpenTelemetry Foundry tracing | CloudWatch, CloudTrail, S3 invocation logging, AgentCore observability | Even |
| 13 | Open-source models | Large catalog; serverless and managed-compute deployment | Curated set; long tail lives in SageMaker JumpStart instead | Azure |
| 14 | Responsible AI | AI Content Safety — prompt shields, groundedness, protected material | Guardrails — content filters, denied topics, PII redaction, contextual grounding, Automated Reasoning checks | Bedrock |
| 15 | Integration ecosystem | Microsoft 365, Fabric, Power Platform, Logic Apps, Purview | Lambda, Step Functions, S3, SageMaker, Kendra, Connect | Tie — decided by where your data already is |

Full version with reasoning, the four differences that actually bite, and a
selection table: **[`azure_vs_bedrock.md`](./azure_vs_bedrock.md)**.

---

## Setup

**Prerequisites.** An AWS account with Bedrock model access granted for both
Claude and Titan Embeddings — **in the region you intend to call**. Model access
is a separate gate from IAM and is per-account, per-region. Grant it in the
Bedrock console under *Model access → Manage model access*; without it every
call returns `AccessDeniedException` no matter how permissive the IAM policy is.

Minimum IAM policy:

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Action": ["bedrock:InvokeModel", "bedrock:InvokeModelWithResponseStream"],
    "Resource": [
      "arn:aws:bedrock:*::foundation-model/anthropic.claude-*",
      "arn:aws:bedrock:*::foundation-model/amazon.titan-embed-*"
    ]
  }]
}
```

```bash
# 1. Environment
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt

# 2. Credentials — boto3 uses the standard chain, so an IAM role or
#    `aws configure` profile works and needs no .env at all.
cp .env.example .env      # only if you have no profile/role available

# 3. Smoke-test the connection
python bedrock_client.py "Reply with the single word: connected"

# 4. Build the index (one Titan call per chunk)
python bedrock_rag.py ingest

# 5. Ask
python bedrock_rag.py query "Why did the platform team reject a universal provider abstraction?"
python bedrock_rag.py chat
```

---

## Implementation notes

**On Claude 3 Sonnet.** This module was specified against Claude 3 Sonnet.
`anthropic.claude-3-sonnet-20240229-v1:0` reached end of life on **2025-07-21**
and now returns an error on Bedrock, so code written against it cannot run. The
default here is `anthropic.claude-sonnet-5` — the current Sonnet-tier model —
and the ID is read from `BEDROCK_MODEL_ID`, so a future retirement is a config
change rather than a code change. That configurability is the actual lesson:
pinning model IDs in source is how a retirement becomes an outage.

**On boto3 vs the Anthropic Bedrock client.** `boto3` is used as specified, and
it is the right choice when you want one SDK across all AWS services. Anthropic
also ships a dedicated Bedrock client (`AnthropicBedrockMantle`) that exposes the
first-party Messages API surface against Bedrock and tracks new Claude features
sooner. The trade: `boto3` gives you AWS-native credentials, retries, and error
handling for free; the Anthropic client gives you feature parity sooner. For a
shop already deep in AWS tooling, `boto3` usually wins.

**On sampling parameters.** `build_body()` deliberately sets no `temperature`,
`top_p`, or `top_k`. Current Claude models reject them outright — this is the
most common failure when porting older Bedrock sample code, and the error
message (`ValidationException`) does not name the offending field clearly.

**On `allow_dangerous_deserialization`.** Loading a FAISS index unpickles it.
The flag is safe here because the process wrote the index itself one step
earlier; it would not be safe for an index downloaded from anywhere else.

---

## Skills demonstrated

- **Multi-cloud AI architecture** — running a second inference provider
  alongside an incumbent, and knowing when *not* to
- **Amazon Bedrock, hands-on** — control plane vs data plane, model access as a
  separate gate from IAM, provider-prefixed model IDs, quota and throttling
  behavior
- **boto3 in anger** — adaptive retries, extended read timeouts for streaming,
  and error handling that translates AWS exceptions into an actionable cause
- **Streaming inference** — consuming an EventStream, filtering typed content
  blocks, and capturing `amazon-bedrock-invocationMetrics` for per-request cost
  attribution
- **Embeddings and vector retrieval** — Titan v2 with explicit dimension and
  normalization choices, wired into FAISS through LangChain's `Embeddings`
  interface rather than an extra dependency
- **Grounded generation** — context-only system prompt with per-claim source
  citation and an explicit instruction to admit missing information
- **Platform evaluation** — a 15-dimension comparison that names an edge per
  dimension and states plainly where the answer is "whichever cloud holds your
  data"
- **Production judgment** — model retirement handled through configuration,
  guardrail enforcement centralized rather than per-call-site, and residency
  risk in cross-region inference profiles called out explicitly

---

## Stack

Python 3.11+ · boto3 1.43 · Amazon Bedrock (`bedrock-runtime`) · Claude ·
Amazon Titan Text Embeddings v2 · FAISS · LangChain
