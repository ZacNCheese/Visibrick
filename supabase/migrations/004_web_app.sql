-- Visibrick: support for the website (fast catalog search, and the collection joined with the catalog)
--
-- Run once in the Supabase dashboard: SQL Editor → New query → paste this → Run (after 001–003).
-- Safe to run again; it only adds things and never deletes data.
--
-- The website doesn't download the whole catalog like the app did; it asks Supabase for one page at a time.
-- These indexes keep that fast for every catalog type, including ~97,000 parts.

-- 1. Text search: trigram indexes make "name contains ..." searches fast instead of scanning every row
create extension if not exists pg_trgm with schema extensions;

create index if not exists catalog_items_name_trgm
    on public.catalog_items using gin (name extensions.gin_trgm_ops);

create index if not exists catalog_items_number_trgm
    on public.catalog_items using gin (number extensions.gin_trgm_ops);

-- 2. Browsing order: newest first within a type
create index if not exists catalog_items_type_year_number
    on public.catalog_items (item_type, year_released desc nulls last, number);

-- 3. The signed-in user's collection with each item's catalog details, so My Collection can search, filter
--    and page in one request. security_invoker makes the view run as the person asking, so the Row Level
--    Security on collection_items still limits everyone to their own collection.
create or replace view public.my_collection
with (security_invoker = on) as
select
    c.item_type,
    c.item_number,
    c.quantity,
    c.updated_at,
    i.name,
    i.category_name,
    i.year_released
from public.collection_items c
left join public.catalog_items i
    on i.item_type = c.item_type and i.number = c.item_number;

grant select on public.my_collection to authenticated;
