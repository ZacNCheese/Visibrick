-- Visibrick: new (sealed) or used, for the sets and minifigs you own
--
-- Run once in the Supabase dashboard: SQL Editor → New query → paste this → Run (after 001–006).
-- Safe to run again. It deletes nothing; everything already in collections counts as used, as before.

-- How many of the copies you own are new. The rest (quantity - new_quantity) are used.
-- One row per item still, so a sealed copy and an opened copy of the same set live side by side, and missing
-- pieces (006) keep pointing at the same row. Only used copies can be missing pieces.
alter table public.collection_items
    add column if not exists new_quantity integer not null default 0;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'collection_items_new_quantity_check') then
        alter table public.collection_items
            add constraint collection_items_new_quantity_check check (new_quantity >= 0 and new_quantity <= quantity);
    end if;
end $$;

-- When every copy is new there's nothing to be missing (a sealed box is complete), so forget any missing pieces.
-- The website does this too; the trigger makes sure it holds however the row is changed.
create or replace function public.clear_missing_when_all_new()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if new.new_quantity >= new.quantity then
        delete from public.collection_missing_parts m
        where m.user_id = new.user_id and m.item_type = new.item_type and m.item_number = new.item_number;
    end if;
    return new;
end;
$$;

drop trigger if exists clear_missing_when_all_new on public.collection_items;
create trigger clear_missing_when_all_new
    after insert or update of quantity, new_quantity on public.collection_items
    for each row execute function public.clear_missing_when_all_new();

-- My Collection: how many new and used copies, so it can show them and filter by condition
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
    ), 0)::integer as missing_pieces,
    c.new_quantity,
    c.quantity - c.new_quantity as used_quantity
from public.collection_items c
left join public.catalog_items i
    on i.item_type = c.item_type and i.number = c.item_number;

grant select on public.my_collection to authenticated;
