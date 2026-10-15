-- Visibrick: each user's collection
--
-- Run once in the Supabase dashboard: SQL Editor → New query → paste this → Run (after 001 and 002).
-- Safe to run again; it only adds things and never deletes data.

-- One row per item a user owns. Items are identified the same way as in catalog_items
-- (BrickLink item type letter + number), but deliberately WITHOUT a foreign key to catalog_items:
-- the catalog is deleted and re-imported from BrickLink files, and a foreign key would either block
-- the import or wipe everyone's collection along with it.
create table if not exists public.collection_items (
    id           bigint      generated always as identity primary key,
    user_id      uuid        not null default auth.uid() references auth.users (id) on delete cascade,
    item_type    text        not null check (item_type in ('M', 'S', 'P', 'G', 'B')),
    item_number  text        not null,
    quantity     integer     not null default 1 check (quantity > 0),
    created_at   timestamptz not null default now(),
    updated_at   timestamptz not null default now(),
    unique (user_id, item_type, item_number)
);

-- Row Level Security: everyone can only see and change their own collection.
alter table public.collection_items enable row level security;

do $$
begin
    if not exists (select 1 from pg_policies where tablename = 'collection_items' and policyname = 'Users can read their own collection') then
        create policy "Users can read their own collection" on public.collection_items
            for select to authenticated
            using ((select auth.uid()) = user_id);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'collection_items' and policyname = 'Users can add to their own collection') then
        create policy "Users can add to their own collection" on public.collection_items
            for insert to authenticated
            with check ((select auth.uid()) = user_id);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'collection_items' and policyname = 'Users can change their own collection') then
        create policy "Users can change their own collection" on public.collection_items
            for update to authenticated
            using ((select auth.uid()) = user_id)
            with check ((select auth.uid()) = user_id);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'collection_items' and policyname = 'Users can remove from their own collection') then
        create policy "Users can remove from their own collection" on public.collection_items
            for delete to authenticated
            using ((select auth.uid()) = user_id);
    end if;
end $$;
