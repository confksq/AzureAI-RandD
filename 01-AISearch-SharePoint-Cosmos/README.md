# AI Search — SharePoint + Cosmos DB Indexing

Enterprise Azure AI Search solution that indexes SharePoint Online documents and Azure Cosmos DB records into a unified hybrid search index with AI enrichment.

## Architecture

```
SharePoint Online                    Azure Cosmos DB (SQL API)
(Invoice Docs, Policy PDFs)          (Invoice Records, Dealer Data)
        │                                        │
        ▼                                        ▼
┌─────────────────────────────────────────────────────────┐
│              Azure AI Search Indexers                    │
│  jmf-sharepoint-indexer (every 6h)                       │
│  jmf-cosmos-indexer     (every 1h)                       │
└─────────────────────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────────────────────┐
│              AI Enrichment Skillset                      │
│  Language Detection → Document Split → Entity Recognition│
│  Key Phrase Extraction → Vector Embedding (1536-dim)     │
│  (powered by Azure AI Services + Azure OpenAI)           │
└─────────────────────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────────────────────┐
│              jmf-documents Index                         │
│  Hybrid Search: keyword + vector (HNSW cosine)           │
│  Semantic re-ranking enabled                             │
│  Scoring profile: boost recent documents                 │
└─────────────────────────────────────────────────────────┘
```

## Project Structure

```
01-AISearch-SharePoint-Cosmos/
├── infra/
│   ├── index/
│   │   └── jmf-documents-index.json       ← Index schema (vector + semantic + scoring)
│   ├── datasources/
│   │   ├── sharepoint-datasource.json     ← SharePoint Online connector
│   │   └── cosmos-datasource.json         ← Cosmos DB SQL API connector
│   ├── skillsets/
│   │   └── jmf-enrichment-skillset.json   ← AI enrichment pipeline
│   └── indexers/
│       ├── sharepoint-indexer.json        ← SharePoint indexer (6h schedule)
│       └── cosmos-indexer.json            ← Cosmos DB indexer (1h schedule)
└── src/
    └── JMF.AISearch.Setup/                ← C# console app to provision resources
        ├── Program.cs
        ├── Configuration/AzureSearchSettings.cs
        ├── Services/
        │   ├── SearchSetupService.cs      ← Creates/updates all search resources
        │   └── IndexerService.cs          ← Run, reset, and monitor indexers
        └── Models/InvoiceDocument.cs      ← Strongly-typed index document model
```

## Prerequisites

- Azure AI Search (Standard tier or above — required for semantic search + vector)
- Azure AI Services multi-service account (for skillset enrichment)
- Azure OpenAI with `text-embedding-3-small` deployed (for vector embeddings)
- Azure Cosmos DB SQL API account with `jmf-invoices` database
- SharePoint Online site with document libraries
- Managed Identity assigned to the search service with:
  - `Storage Blob Data Reader` on the cache storage account
  - `Cosmos DB Account Reader` on the Cosmos DB account

## Setup

### 1. Configure settings

```json
// appsettings.Development.json
{
  "AzureSearch": {
    "ServiceEndpoint": "https://your-search.search.windows.net",
    "OpenAIEndpoint":  "https://your-openai.openai.azure.com"
  }
}
```

### 2. Provision all resources

```bash
dotnet run --project src/JMF.AISearch.Setup -- provision
```

### 3. Trigger indexers manually

```bash
dotnet run --project src/JMF.AISearch.Setup -- run-indexers
```

### 4. Check indexer status

```bash
dotnet run --project src/JMF.AISearch.Setup -- status
```

### 5. Reset an indexer (full reindex)

```bash
dotnet run --project src/JMF.AISearch.Setup -- reset jmf-cosmos-indexer
```

## Index Features

| Feature | Detail |
|---------|--------|
| Vector search | HNSW, cosine similarity, 1536 dimensions (text-embedding-3-small) |
| Hybrid search | BM25 keyword + vector, fused with RRF |
| Semantic ranking | Azure AI semantic re-ranker on top of hybrid results |
| AI enrichment | Entity recognition, key phrases, language detection |
| Change tracking | SharePoint: high-water mark on `metadata_spo_last_modified` |
| Change tracking | Cosmos DB: high-water mark on `_ts` field |
| Soft delete | Both sources support soft-delete detection |
| Skillset cache | Blob storage cache — avoids re-enriching unchanged documents |

## Auth

Zero secrets in code. Uses `DefaultAzureCredential` — works with:
- Local dev: Azure CLI (`az login`)
- Azure: System-assigned or user-assigned Managed Identity
