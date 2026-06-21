// ============================================================
// GAP TOPIC: Chunking Strategies — How to split text for RAG
// ============================================================
// WHAT: Splits long policy documents into chunks that are:
//       (a) small enough to embed accurately
//       (b) large enough to contain useful context
// WHY:  The quality of your RAG retrieval is only as good as your chunks
//       Bad chunks = retrieved text misses the key policy sentence
//       Good chunks = retrieved text contains exactly what's needed
// JMA:  Dealer policy PDFs → chunks → embeddings → AI Search index
// HEALTHCARE EQUIVALENT: Clinical guidelines, formulary PDFs,
//       coverage policies → chunks → embeddings → search index
// ============================================================
// INTERVIEW: "What chunking strategy do you use and why?"
// "Paragraph-level with parent-child for policy documents.
//  Policy rules live in paragraphs — splitting by token count
//  risks cutting a rule mid-sentence and losing the context.
//  Paragraph chunking keeps each rule intact.
//  Parent-child means the small chunk (200 tokens) is used for
//  retrieval (precise matching), but we inject the parent section
//  (1000 tokens) into the prompt (more context for reasoning).
//  This gives you precision at retrieval time + context at generation time."
// ============================================================

namespace DealerIntelligence.DocumentPipeline;

public class ChunkingStrategy
{
    // INTERVIEW: Chunk size vs overlap trade-off
    // Smaller chunks = more precise retrieval (better embedding quality per chunk)
    //                  but may lose context (policy rule spans two chunks)
    // Larger chunks = more context per chunk, but embedding is less precise
    // Standard: 200-512 tokens for search chunks; overlap prevents edge splits
    private const int FixedChunkTokens  = 300;
    private const int OverlapTokens     = 50;    // INTERVIEW: overlap prevents cutting sentences

    public List<TextChunk> ChunkDocument(string documentId, string text, string strategy = "paragraph")
    {
        return strategy switch
        {
            "fixed"     => ChunkFixed(documentId, text),
            "paragraph" => ChunkByParagraph(documentId, text),
            "parent-child" => ChunkParentChild(documentId, text),
            _ => ChunkByParagraph(documentId, text)
        };
    }

    // -------------------------------------------------------
    // STRATEGY 1: Fixed-size with overlap
    // -------------------------------------------------------
    // USE WHEN: Homogeneous text (transcripts, notes, logs)
    //           Text has no natural structural boundaries
    // DON'T USE FOR: Policy docs, guidelines (rules get split mid-sentence)
    // -------------------------------------------------------
    private List<TextChunk> ChunkFixed(string documentId, string text)
    {
        // Simplified: split by word count, real impl uses token counter
        var words  = text.Split(' ');
        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

        for (int i = 0; i < words.Length; i += FixedChunkTokens - OverlapTokens)
        {
            var chunk = string.Join(" ", words.Skip(i).Take(FixedChunkTokens));
            if (string.IsNullOrWhiteSpace(chunk)) continue;

            chunks.Add(new TextChunk
            {
                ChunkId    = $"{documentId}::fixed::{chunkIndex++}",
                DocumentId = documentId,
                Text       = chunk,
                Strategy   = "fixed",
                StartIndex = i,
                TokenCount = Math.Min(FixedChunkTokens, words.Length - i)
            });
        }

        return chunks;
    }

    // -------------------------------------------------------
    // STRATEGY 2: Paragraph-level (PREFERRED for policy docs)
    // -------------------------------------------------------
    // USE WHEN: Policy documents, contracts, guidelines
    //           Text has natural paragraph/section boundaries
    //           Each paragraph = one complete policy rule
    // INTERVIEW: "Why paragraph for policy?" →
    // "Policy rules live in paragraphs. Each paragraph is a complete
    //  rule: conditions, exceptions, limits. Fixed-size chunks can split
    //  'Maximum claim is $5,000 provided the [next chunk] dealer enrolled
    //  for 24+ months' — you lose the condition. Paragraphs keep rules intact."
    // -------------------------------------------------------
    private List<TextChunk> ChunkByParagraph(string documentId, string text)
    {
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<TextChunk>();

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var para = paragraphs[i].Trim();
            if (para.Length < 20) continue;   // skip headers/blank lines

            chunks.Add(new TextChunk
            {
                ChunkId    = $"{documentId}::para::{i}",
                DocumentId = documentId,
                Text       = para,
                Strategy   = "paragraph",
                StartIndex = i,
                TokenCount = EstimateTokens(para)
            });
        }

        return chunks;
    }

    // -------------------------------------------------------
    // STRATEGY 3: Parent-Child (BEST for RAG quality)
    // -------------------------------------------------------
    // USE WHEN: You need precision at retrieval + context at generation
    // HOW: Small child chunk (200 tokens) → used for embedding + retrieval
    //      Large parent chunk (1000 tokens, the full section) → injected into prompt
    // WHY: Small chunks embed precisely (the needle is in the chunk)
    //      Large parent gives LLM the full section for reasoning
    // INTERVIEW: "Parent-child = precision at retrieval, context at generation"
    //            "Child finds the match; parent gives the LLM enough to reason about it"
    // -------------------------------------------------------
    private List<TextChunk> ChunkParentChild(string documentId, string text)
    {
        // Step 1: Split into parent sections (larger — by heading/section boundary)
        var sections = text.Split("##", StringSplitOptions.RemoveEmptyEntries);  // markdown sections
        var chunks   = new List<TextChunk>();

        for (int sectionIdx = 0; sectionIdx < sections.Length; sectionIdx++)
        {
            var parentText = sections[sectionIdx].Trim();
            var parentId   = $"{documentId}::parent::{sectionIdx}";

            // Step 2: Split each parent into smaller child chunks (for precise retrieval)
            var childParagraphs = parentText.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

            for (int childIdx = 0; childIdx < childParagraphs.Length; childIdx++)
            {
                var childText = childParagraphs[childIdx].Trim();
                if (childText.Length < 20) continue;

                chunks.Add(new TextChunk
                {
                    ChunkId    = $"{documentId}::child::{sectionIdx}::{childIdx}",
                    DocumentId = documentId,
                    ParentId   = parentId,     // INTERVIEW: Link back to parent for retrieval expansion
                    Text       = childText,    // INTERVIEW: CHILD is what gets embedded + searched
                    ParentText = parentText,   // INTERVIEW: PARENT is what gets injected into prompt
                    Strategy   = "parent-child",
                    StartIndex = childIdx,
                    TokenCount = EstimateTokens(childText)
                });
            }
        }

        return chunks;
    }

    private static int EstimateTokens(string text) => text.Split(' ').Length;  // rough: 1 token ≈ 0.75 words
}

public record TextChunk
{
    public string ChunkId    { get; init; } = string.Empty;
    public string DocumentId { get; init; } = string.Empty;
    public string ParentId   { get; init; } = string.Empty;   // null for non-parent-child strategies
    public string Text       { get; init; } = string.Empty;   // child text (embedded + searched)
    public string ParentText { get; init; } = string.Empty;   // parent text (injected into prompt)
    public string Strategy   { get; init; } = string.Empty;
    public int    StartIndex { get; init; }
    public int    TokenCount { get; init; }
}
