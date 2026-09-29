-- PongAI global leaderboard (Supabase / Postgres).
-- Run once in the Supabase dashboard: SQL Editor -> New query -> paste -> Run.
-- Safe to re-run.

create table if not exists public.leaderboard (
  id          bigint generated always as identity primary key,
  nickname    text        not null unique,
  high_score  integer     not null default 0,
  updated_at  timestamptz not null default now(),
  constraint leaderboard_score_range check (high_score between 0 and 1000000),
  -- Mirrors the game's nickname rules: trimmed, 2-12 chars, letters/digits/_/space.
  constraint leaderboard_nickname_format check (
    nickname = btrim(nickname)
    and char_length(nickname) between 2 and 12
    and nickname ~ '^[A-Za-z0-9_ ]+$'
  )
);

-- Top 10 query: order by high_score desc, updated_at asc.
create index if not exists leaderboard_rank_idx
  on public.leaderboard (high_score desc, updated_at asc);

-- Server-side guard: an update can never lower a best score or rename a row,
-- and updated_at always comes from the database clock.
create or replace function public.leaderboard_before_write()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  if tg_op = 'UPDATE' then
    new.nickname := old.nickname;
    if new.high_score <= old.high_score then
      new.high_score := old.high_score;
      new.updated_at := old.updated_at;
      return new;
    end if;
  end if;
  new.updated_at := now();
  return new;
end;
$$;

drop trigger if exists leaderboard_before_write on public.leaderboard;
create trigger leaderboard_before_write
  before insert or update on public.leaderboard
  for each row execute function public.leaderboard_before_write();

-- Row Level Security: the game (anon key) may read, add and raise scores. No deletes.
alter table public.leaderboard enable row level security;

drop policy if exists "leaderboard_select" on public.leaderboard;
create policy "leaderboard_select" on public.leaderboard
  for select to anon, authenticated using (true);

drop policy if exists "leaderboard_insert" on public.leaderboard;
create policy "leaderboard_insert" on public.leaderboard
  for insert to anon, authenticated with check (true);

drop policy if exists "leaderboard_update" on public.leaderboard;
create policy "leaderboard_update" on public.leaderboard
  for update to anon, authenticated using (true) with check (true);

grant select, insert, update on public.leaderboard to anon, authenticated;
revoke delete on public.leaderboard from anon, authenticated;
