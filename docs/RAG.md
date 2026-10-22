# Local knowledge and RAG

[Pipeline diagram](architecture/KNOWLEDGE_AUTOMATION.md#rag-pipeline) · [Complete implementation, security and setup](ENTERPRISE_RAG.md)

The implemented Knowledge Center accepts authenticated PDF, DOCX, TXT and Markdown documents. It validates/extracts them, creates bounded chunks and indexes immutable versions with local Ollama embeddings. Hybrid search combines pgvector similarity with PostgreSQL full-text ranking over the authorized document set.

The Knowledge Agent selects **literal source excerpts** and returns verified document/version/quote citations. It does not claim unrestricted generated paraphrases are verified policy. Unsupported questions return explicit insufficient evidence. Current operational stock facts remain separate from source policy in Inventory → Knowledge collaboration.

## Access and processing boundaries

- Documents are owner-private or explicitly Admin-published to the shared workspace. The repository has no tenant domain.
- Authorization filters both search branches before ranking; source version and permission are rechecked before delivery.
- Original files remain private database values, not public web assets. Context is bounded and treated as untrusted data.
- The knowledge model receives no ERP write tools and cannot execute instructions embedded in documents.
- File/character/page/chunk/context limits, version/digest/dimension checks, indexing leases and safe failures are implemented.

## Requirements and limits

Use a PostgreSQL image with pgvector available and the existing additive migrations, plus local `nomic-embed-text`. Native options are `Knowledge__Enabled=true` and `Knowledge__EmbeddingModel=nomic-embed-text`; the feature is disabled by default. [Exact isolated setup](ENTERPRISE_RAG.md#windows-isolated-review-setup).

No OCR, image understanding, arbitrary web crawling, ANN indexing claim, parser process sandbox or factual guarantee about source content is made. Exact quote verification establishes provenance, not semantic correctness of every retrieved policy. A separate parser process and operational document/backup policies remain deployment work.

Recorded actual local embedding, hybrid retrieval and citation results are in [verification](../VERIFICATION.md). No refreshed Knowledge Center screenshot is included in this documentation-only change: no new document records or queries were submitted merely to manufacture a showcase image.
