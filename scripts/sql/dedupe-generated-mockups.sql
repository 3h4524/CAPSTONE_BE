-- One-off clean-up: a composited mock-up stored more than once.
-- Before mock-ups were reused, "Preview on a mock-up" added a new row every time, even for one made exactly the same
-- way, and "Add this product's mock-ups" then imported every copy as a video source. Rows made the same way have the
-- same design, template and composite URL (the URL encodes the template photo, print area, color and background).
-- Keeps one row of each: the approved one, else one a video or workflow uses, else the imported one, else the oldest.
-- The other copies are soft-deleted, except a copy that is approved or that a video scene, workflow, run or media job
-- refers to. Mock-ups that only look alike (made with a different print area or template photo) are left for the
-- seller to reject at Mockup Approval. Safe to run more than once.

BEGIN;

WITH referenced AS (
    SELECT mockup_image_id AS id FROM public.promo_video_scenes WHERE mockup_image_id IS NOT NULL
    UNION
    SELECT m.id FROM public.mockup_images m
    WHERE EXISTS (SELECT 1 FROM public.workflows w WHERE w::text LIKE '%' || m.id::text || '%')
       OR EXISTS (SELECT 1 FROM public.workflow_runs r WHERE r::text LIKE '%' || m.id::text || '%')
       OR EXISTS (SELECT 1 FROM public.workflow_node_runs n WHERE n::text LIKE '%' || m.id::text || '%')
       OR EXISTS (SELECT 1 FROM public.media_jobs j WHERE j::text LIKE '%' || m.id::text || '%')
),
ranked AS (
    SELECT m.id,
           m.approval_status = 'approved' AS approved,
           m.id IN (SELECT id FROM referenced) AS in_use,
           row_number() OVER (
               PARTITION BY m.design_image_id, m.mockup_template_id, m.mockup_image_url
               ORDER BY (m.approval_status = 'approved') DESC, (m.id IN (SELECT id FROM referenced)) DESC,
                        (m.content_hash IS NOT NULL) DESC, m.created_at, m.id) AS copy
    FROM public.mockup_images m
    WHERE m.deleted_at IS NULL AND m.source_type = 'generated'
      AND m.design_image_id IS NOT NULL AND m.mockup_template_id IS NOT NULL AND m.mockup_image_url IS NOT NULL
)
UPDATE public.mockup_images m
SET deleted_at = now()
FROM ranked r
WHERE m.id = r.id AND r.copy > 1 AND NOT r.approved AND NOT r.in_use;

COMMIT;
