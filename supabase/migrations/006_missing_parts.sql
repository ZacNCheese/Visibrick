-- Visibrick: track missing pieces in the sets and minifigs you own
--
-- Run once in the Supabase dashboard: SQL Editor → New query → paste this → Run (after 001–005).
-- Safe to run again. It deletes nothing.

-- Only missing pieces are stored. Everything else counts as complete, so a new set starts with every box ticked.
-- Rows hang off the collection entry: removing a set from your collection removes its missing pieces too, and
-- renumbering an entry (as 005 did) carries them along.
create table if not exists public.collection_missing_parts (
    id           bigint      generated always as identity primary key,
    user_id      uuid        not null default auth.uid(),
    item_type    text        not null,              -- S (set) or M (minifig)
    item_number  text        not null,
    part_num     text        not null,              -- Rebrickable part number (as in set_parts / minifig_parts), or for a
                                                    -- minifig missing from a set, its Rebrickable number (fig-...) with color_id -1
    color_id     integer     not null,
    missing      integer     not null check (missing > 0),
    updated_at   timestamptz not null default now(),
    unique (user_id, item_type, item_number, part_num, color_id),
    foreign key (user_id, item_type, item_number)
        references public.collection_items (user_id, item_type, item_number)
        on delete cascade on update cascade
);

-- Row Level Security: everyone can only see and change their own missing pieces.
alter table public.collection_missing_parts enable row level security;

do $$
begin
    if not exists (select 1 from pg_policies where tablename = 'collection_missing_parts' and policyname = 'Users can read their own missing parts') then
        create policy "Users can read their own missing parts" on public.collection_missing_parts
            for select to authenticated
            using ((select auth.uid()) = user_id);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'collection_missing_parts' and policyname = 'Users can add their own missing parts') then
        create policy "Users can add their own missing parts" on public.collection_missing_parts
            for insert to authenticated
            with check ((select auth.uid()) = user_id);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'collection_missing_parts' and policyname = 'Users can change their own missing parts') then
        create policy "Users can change their own missing parts" on public.collection_missing_parts
            for update to authenticated
            using ((select auth.uid()) = user_id)
            with check ((select auth.uid()) = user_id);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'collection_missing_parts' and policyname = 'Users can remove their own missing parts') then
        create policy "Users can remove their own missing parts" on public.collection_missing_parts
            for delete to authenticated
            using ((select auth.uid()) = user_id);
    end if;
end $$;

-- My Collection: how many pieces each item is missing, so it can show them and filter to incomplete items
create or replace view public.my_collection
with (security_invoker = on) as
select
    c.item_type,
    c.item_number,
    c.quantity,
    c.updated_at,
    i.name,
    i.category_name,
    i.year_released,
    i.bricklink_number,
    i.image_url,
    i.num_parts,
    coalesce((
        select sum(m.missing)
        from public.collection_missing_parts m
        where m.user_id = c.user_id and m.item_type = c.item_type and m.item_number = c.item_number
    ), 0)::integer as missing_pieces
from public.collection_items c
left join public.catalog_items i
    on i.item_type = c.item_type and i.number = c.item_number;

grant select on public.my_collection to authenticated;
