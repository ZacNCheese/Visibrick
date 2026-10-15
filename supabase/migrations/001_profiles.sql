-- Visibrick: user profiles
--
-- Run once in the Supabase dashboard: SQL Editor → New query → paste this → Run.
-- Safe to run again; it only adds things and never deletes data.
--
-- Supabase Auth keeps accounts (emails, password hashes, Google/Apple links) in its own auth.users table.
-- This adds a public "profiles" row per user for app data like a display name. Collections will reference
-- profiles.id later, so deleting an account removes everything that belongs to it.

create table if not exists public.profiles (
    id            uuid        primary key references auth.users (id) on delete cascade,
    display_name  text,
    avatar_url    text,
    created_at    timestamptz not null default now(),
    updated_at    timestamptz not null default now()
);

-- Row Level Security: the app (with a signed-in user's token) can only read and change that user's own profile.
-- Nobody can read other people's profiles, and rows are only created by the trigger below.
alter table public.profiles enable row level security;

do $$
begin
    if not exists (select 1 from pg_policies where tablename = 'profiles' and policyname = 'Users can read their own profile') then
        create policy "Users can read their own profile" on public.profiles
            for select to authenticated
            using ((select auth.uid()) = id);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'profiles' and policyname = 'Users can update their own profile') then
        create policy "Users can update their own profile" on public.profiles
            for update to authenticated
            using ((select auth.uid()) = id)
            with check ((select auth.uid()) = id);
    end if;
end $$;

-- Create a profile automatically when someone signs up, using the name/picture Google or Apple shared (if any).
-- "security definer" lets it insert despite RLS; "search_path = ''" is Supabase's recommended hardening for that.
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    insert into public.profiles (id, display_name, avatar_url)
    values (
        new.id,
        coalesce(new.raw_user_meta_data ->> 'full_name', new.raw_user_meta_data ->> 'name'),
        new.raw_user_meta_data ->> 'avatar_url'
    );
    return new;
end;
$$;

create or replace trigger on_auth_user_created
    after insert on auth.users
    for each row execute procedure public.handle_new_user();
