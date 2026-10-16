-- Visibrick: sets and parts now come from Rebrickable
--
-- Run once in the Supabase dashboard: SQL Editor → New query → paste this → Run,
-- AFTER running Visibrick.Sync with the Rebrickable zip (it creates the new columns and tables this uses).
-- Safe to run again. It deletes nothing.

-- 1. My Collection also needs each item's picture, BrickLink number and piece count
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
    i.num_parts
from public.collection_items c
left join public.catalog_items i
    on i.item_type = c.item_type and i.number = c.item_number;

grant select on public.my_collection to authenticated;

-- 2. Sets and parts added to collections before the switch were saved with BrickLink numbers. Most sets have
--    the same number on Rebrickable; parts often don't (BrickLink 3960pb024 is Rebrickable 3960pr0001).
--    Move each one to its Rebrickable number, unless:
--      - it's already a valid Rebrickable number
--      - the person already has the Rebrickable item, which would make a duplicate
--      - several Rebrickable items share that BrickLink number; the first one is used
update public.collection_items c
set item_number = i.number,
    updated_at = now()
from (
    select distinct on (item_type, bricklink_number) item_type, bricklink_number, number
    from public.catalog_items
    where item_type in ('S', 'P') and bricklink_number is not null
    order by item_type, bricklink_number, number
) i
where c.item_type = i.item_type
  and c.item_number = i.bricklink_number
  and c.item_number <> i.number
  and not exists (
      select 1 from public.catalog_items x
      where x.item_type = c.item_type and x.number = c.item_number)
  and not exists (
      select 1 from public.collection_items d
      where d.user_id = c.user_id and d.item_type = c.item_type and d.item_number = i.number);

-- 3. Anything left that doesn't match a catalog item (shown as "unknown" in My Collection). Usually empty.
select c.item_type, c.item_number, c.quantity
from public.collection_items c
left join public.catalog_items i on i.item_type = c.item_type and i.number = c.item_number
where i.number is null;
