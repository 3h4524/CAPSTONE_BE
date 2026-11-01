BEGIN;

CREATE TABLE IF NOT EXISTS public.assistant_conversations (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    title varchar(200) NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone NULL,
    CONSTRAINT chk_assistant_conversations_title CHECK (char_length(btrim(title)) BETWEEN 1 AND 200)
);

CREATE INDEX IF NOT EXISTS idx_assistant_conversations_user_updated
    ON public.assistant_conversations (user_id, updated_at DESC)
    WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS public.assistant_messages (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    conversation_id uuid NOT NULL REFERENCES public.assistant_conversations(id) ON DELETE CASCADE,
    role varchar(20) NOT NULL,
    content text NOT NULL DEFAULT '',
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    model varchar(100) NULL,
    input_tokens integer NULL,
    output_tokens integer NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_assistant_messages_role CHECK (role IN ('user', 'assistant')),
    CONSTRAINT chk_assistant_messages_metadata CHECK (jsonb_typeof(metadata) = 'object'),
    CONSTRAINT chk_assistant_messages_tokens CHECK ((input_tokens IS NULL OR input_tokens >= 0) AND (output_tokens IS NULL OR output_tokens >= 0))
);

CREATE INDEX IF NOT EXISTS idx_assistant_messages_conversation_created
    ON public.assistant_messages (conversation_id, created_at);

CREATE TABLE IF NOT EXISTS public.assistant_pending_actions (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    conversation_id uuid NOT NULL REFERENCES public.assistant_conversations(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    tool_name varchar(100) NOT NULL,
    arguments jsonb NOT NULL,
    title varchar(200) NOT NULL,
    summary text NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'pending',
    result jsonb NULL,
    error_message text NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    resolved_at timestamp with time zone NULL,
    CONSTRAINT chk_assistant_pending_actions_status CHECK (status IN ('pending', 'executing', 'executed', 'rejected', 'expired', 'failed')),
    CONSTRAINT chk_assistant_pending_actions_arguments CHECK (jsonb_typeof(arguments) = 'object')
);

CREATE INDEX IF NOT EXISTS idx_assistant_pending_actions_user_status
    ON public.assistant_pending_actions (user_id, status);

CREATE INDEX IF NOT EXISTS idx_assistant_pending_actions_conversation
    ON public.assistant_pending_actions (conversation_id);

COMMIT;
