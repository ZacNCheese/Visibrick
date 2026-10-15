-- Visibrick: profile settings (profile pictures, account deletion)
--
-- Run once in the Supabase dashboard: SQL Editor → New query → paste this → Run (after 001_profiles.sql).
-- Safe to run again; it only adds things and never deletes data.

-- 1. Let a signed-in user create their own profile row. Normally the sign-up trigger in 001 does this,
--    but accounts created before 001 was run have no row yet, and the app creates it on first sign-in.
do $$
begin
    if not exists (select 1 from pg_policies where tablename = 'profiles' and policyname = 'Users can create their own profile') then
        create policy "Users can create their own profile" on public.profiles
            for insert to authenticated
            with check ((select auth.uid()) = id);
    end if;
end $$;

-- 2. Profile pictures: a public "avatars" bucket (anyone can view a picture by its address, which is how the
--    app shows them). Each user can only upload, replace or delete files in their own folder, avatars/<user id>/.
--    Pictures are limited to 5 MB and common image types.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('avatars', 'avatars', true, 5242880, array['image/jpeg', 'image/png', 'image/webp'])
on conflict (id) do nothing;

do $$
begin
    if not exists (select 1 from pg_policies where tablename = 'objects' and policyname = 'Users can read their own avatar files') then
        create policy "Users can read their own avatar files" on storage.objects
            for select to authenticated
            using (bucket_id = 'avatars' and (storage.foldername(name))[1] = (select auth.uid())::text);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'objects' and policyname = 'Users can upload their own avatar') then
        create policy "Users can upload their own avatar" on storage.objects
            for insert to authenticated
            with check (bucket_id = 'avatars' and (storage.foldername(name))[1] = (select auth.uid())::text);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'objects' and policyname = 'Users can replace their own avatar') then
        create policy "Users can replace their own avatar" on storage.objects
            for update to authenticated
            using (bucket_id = 'avatars' and (storage.foldername(name))[1] = (select auth.uid())::text)
            with check (bucket_id = 'avatars' and (storage.foldername(name))[1] = (select auth.uid())::text);
    end if;

    if not exists (select 1 from pg_policies where tablename = 'objects' and policyname = 'Users can delete their own avatar') then
        create policy "Users can delete their own avatar" on storage.objects
            for delete to authenticated
            using (bucket_id = 'avatars' and (storage.foldername(name))[1] = (select auth.uid())::text);
    end if;
end $$;

-- 3. Account deletion (Apple requires apps that let people create accounts to let them delete them too).
--    Deletes only the account of whoever calls it; their profile goes with it (on delete cascade), and
--    their collection will too once it exists. The app deletes the profile picture file first.
create or replace function public.delete_my_account()
returns void
language plpgsql
security definer
set search_path = ''
as $$
begin
    if auth.uid() is null then
        raise exception 'Not signed in';
    end if;

    delete from auth.users where id = auth.uid();
end;
$$;

revoke execute on function public.delete_my_account() from public, anon;
grant execute on function public.delete_my_account() to authenticated;
