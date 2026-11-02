-- Mishka Helper support chat. Run once in the Supabase SQL editor (or as a migration).
-- The app holds only the public anon key, so these rules are the whole security model:
--   * a user sees and writes only their own ticket, and can never post as support;
--   * admins are listed in public.admins, which nobody can edit through the API.
-- Dashboard settings: enable Anonymous sign-ins. The admin account is created by hand in Authentication -> Users.
-- Someone signing up by email on their own only gets an ordinary user: admin rights come from public.admins alone.

create table public.admins (
  user_id uuid primary key references auth.users(id) on delete cascade
);
alter table public.admins enable row level security;
-- No policies on purpose: reachable only through is_admin() and the dashboard.

create or replace function public.is_admin() returns boolean
language sql stable security definer set search_path = '' as $$
  select exists (select 1 from public.admins where user_id = auth.uid());
$$;
revoke execute on function public.is_admin() from public, anon;
grant execute on function public.is_admin() to authenticated;

create table public.tickets (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id) on delete cascade,
  created_at timestamptz not null default now(),
  last_at timestamptz not null default now(),
  status text not null default 'open' check (status in ('open', 'closed')),
  admin_note text not null default '',
  unread_admin int not null default 0,
  app_version text not null default '' check (length(app_version) <= 30),
  edition text not null default '' check (length(edition) <= 30)
);
create index on public.tickets (last_at desc);
create index on public.tickets (user_id);

create table public.messages (
  id bigint generated always as identity primary key,
  ticket_id uuid not null references public.tickets(id) on delete cascade,
  from_admin boolean not null default false,
  body text not null,
  created_at timestamptz not null default now(),
  constraint body_not_empty check (length(btrim(body)) > 0)
);
create index on public.messages (ticket_id, id);

-- Server decides who wrote it and enforces limits: a client can't fake support or flood the free tier.
create or replace function public.before_message() returns trigger
language plpgsql security definer set search_path = '' as $$
declare
  owner uuid;
  recent int;
begin
  select user_id into owner from public.tickets where id = new.ticket_id;
  if public.is_admin() then
    new.from_admin := true;
    return new;
  end if;
  if owner is distinct from auth.uid() then
    raise exception 'not_your_ticket';
  end if;
  new.from_admin := false;
  if length(new.body) > 8000 then
    raise exception 'too_long';
  end if;
  if new.attachment <> '' and new.attachment not like auth.uid()::text || '/%' then
    raise exception 'not_your_file';
  end if;
  select count(*) into recent from public.messages m join public.tickets t on t.id = m.ticket_id
    where t.user_id = auth.uid() and not m.from_admin and m.created_at > now() - interval '1 minute';
  if recent >= 5 then
    raise exception 'rate_limit';
  end if;
  return new;
end;
$$;
create trigger before_message before insert on public.messages
  for each row execute function public.before_message();

create or replace function public.after_message() returns trigger
language plpgsql security definer set search_path = '' as $$
begin
  update public.tickets
    set last_at = now(),
        status = 'open',
        unread_admin = case when new.from_admin then 0 else unread_admin + 1 end
    where id = new.ticket_id;
  return null;
end;
$$;
create trigger after_message after insert on public.messages
  for each row execute function public.after_message();

-- Triggers run without EXECUTE; through the API these must not be callable at all.
revoke execute on function public.before_message() from public, anon, authenticated;
revoke execute on function public.after_message() from public, anon, authenticated;

-- One ticket per person is enough, and stops ticket spam from one account.
create unique index one_ticket_per_user on public.tickets (user_id);

alter table public.tickets enable row level security;
alter table public.messages enable row level security;

create policy "own or admin read" on public.tickets for select to authenticated
  using (user_id = auth.uid() or public.is_admin());
create policy "create own" on public.tickets for insert to authenticated
  with check (user_id = auth.uid());
create policy "admin updates" on public.tickets for update to authenticated
  using (public.is_admin()) with check (public.is_admin());
create policy "admin deletes" on public.tickets for delete to authenticated
  using (public.is_admin());

create policy "own or admin read" on public.messages for select to authenticated
  using (public.is_admin() or exists (select 1 from public.tickets t where t.id = ticket_id and t.user_id = auth.uid()));
create policy "write to own or as admin" on public.messages for insert to authenticated
  with check (public.is_admin() or exists (select 1 from public.tickets t where t.id = ticket_id and t.user_id = auth.uid()));

-- Users must not set internal fields of a ticket on insert.
revoke all on public.tickets, public.messages, public.admins from anon;
revoke insert on public.tickets from authenticated;
grant insert (app_version, edition) on public.tickets to authenticated;
revoke insert on public.messages from authenticated;
grant insert (ticket_id, body) on public.messages to authenticated;

-- Screenshots: private bucket, each person writes only to a folder named after their id. See the migration support_screenshots; before_message above also checks
--   new.attachment like auth.uid()::text || '/%' for non-admins.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('support', 'support', false, 5242880, array['image/png', 'image/jpeg']) on conflict (id) do nothing;
create policy "support upload own folder" on storage.objects for insert to authenticated
  with check (bucket_id = 'support' and (storage.foldername(name))[1] = auth.uid()::text);
create policy "support read own or admin" on storage.objects for select to authenticated
  using (bucket_id = 'support' and ((storage.foldername(name))[1] = auth.uid()::text or public.is_admin()));
alter table public.messages add column attachment text not null default '' check (length(attachment) <= 200);
grant insert (attachment) on public.messages to authenticated;

-- For .github/workflows/keepalive.yml: a real database call that reads nothing.
create or replace function public.ping() returns boolean
language sql stable security invoker set search_path = '' as $$ select true $$;
revoke execute on function public.ping() from public;
grant execute on function public.ping() to anon;

-- After creating your admin account in Authentication → Users, run with its id:
--   insert into public.admins (user_id) values ('<your user id>');
