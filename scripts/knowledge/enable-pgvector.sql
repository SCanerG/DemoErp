-- Operator-only completion after installing pgvector binaries in an existing PostgreSQL server.
-- Back up first. No ERP table is dropped, replaced, seeded or modified.
BEGIN;
CREATE EXTENSION IF NOT EXISTS vector;
ALTER TABLE "KnowledgeChunks" ADD COLUMN IF NOT EXISTS "Embedding" vector;
COMMIT;
-- Reindex documents through their authorized API/UI after this step.
-- Embedding model, digest and actual vector dimension are enforced per chunk/query.
