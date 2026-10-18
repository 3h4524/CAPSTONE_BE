CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS rag_documents (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    title varchar(500) NOT NULL,
    source_type varchar(100) NOT NULL,
    source_id varchar(200) NOT NULL,
    content_type varchar(50) NOT NULL DEFAULT 'listing',
    source_url text NULL,
    version varchar(100) NULL,
    language varchar(20) NOT NULL,
    content_hash varchar(128) NOT NULL,
    ingestion_content text NOT NULL,
    ingestion_metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    status varchar(30) NOT NULL DEFAULT 'queued',
    ingestion_error text NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE rag_documents ADD COLUMN IF NOT EXISTS content_type varchar(50) NOT NULL DEFAULT 'listing';
ALTER TABLE rag_documents ADD COLUMN IF NOT EXISTS ingestion_content text NOT NULL DEFAULT '';
ALTER TABLE rag_documents ADD COLUMN IF NOT EXISTS ingestion_metadata jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE rag_documents ADD COLUMN IF NOT EXISTS source_id varchar(200) NOT NULL DEFAULT '';
ALTER TABLE rag_documents ADD COLUMN IF NOT EXISTS status varchar(30) NOT NULL DEFAULT 'ready';
ALTER TABLE rag_documents ADD COLUMN IF NOT EXISTS ingestion_error text NULL;
ALTER TABLE rag_documents ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;
UPDATE rag_documents SET source_id = source_type WHERE source_id = '';
ALTER TABLE rag_documents DROP CONSTRAINT IF EXISTS uq_rag_documents_source_hash;
CREATE INDEX IF NOT EXISTS idx_rag_documents_source_hash ON rag_documents (source_id, content_hash);
CREATE INDEX IF NOT EXISTS idx_rag_documents_content_type ON rag_documents (content_type);
CREATE INDEX IF NOT EXISTS idx_rag_documents_source_status ON rag_documents (source_id, status);

CREATE TABLE IF NOT EXISTS rag_chunks (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    document_id uuid NOT NULL REFERENCES rag_documents(id) ON DELETE CASCADE,
    content text NOT NULL,
    chunk_index integer NOT NULL,
    token_count integer NOT NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT uq_rag_chunks_document_index UNIQUE (document_id, chunk_index)
);

-- Replace 1536 with the selected embedding model dimension.
CREATE TABLE IF NOT EXISTS rag_embeddings (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    chunk_id uuid NOT NULL REFERENCES rag_chunks(id) ON DELETE CASCADE,
    embedding vector(1536) NOT NULL,
    embedding_model varchar(200) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_rag_embeddings_chunk_model UNIQUE (chunk_id, embedding_model)
);

CREATE INDEX IF NOT EXISTS idx_rag_embeddings_embedding_cosine
    ON rag_embeddings USING hnsw (embedding vector_cosine_ops);
