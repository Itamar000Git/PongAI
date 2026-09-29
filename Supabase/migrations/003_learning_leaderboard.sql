-- PongAI migration 003: separate leaderboard for AGAINST LEARNING AI.
-- Run once in the Supabase dashboard: SQL Editor -> New query -> paste -> Run.
-- Safe to re-run. Creates a NEW table only; the Classic table public.leaderboard,
-- its trigger, function, indexes and RLS policies are not touched.
--
-- Same schema and rules as Classic:
--   id, nickname, high_score, updated_at
--   one row per nickname, case-insensitive (Itamar = ITAMAR = itamar)
--   a best score can only go up, never down; nickname cannot be renamed
--   Top 10 = high_score desc, updated_at asc
--   anon key may read / insert / raise scores, never delete

create table if not exists public.learning_leaderboard (
  id          bigint generated always as identity primary key,
  nickname    text        not null unique,
  high_score  integer     not null default 0,
  updated_at  timestamptz not null default now(),
  constraint learning_leaderboard_score_range check (high_score between 0 and 1000000),
  -- Mirrors the game's nickname rules: trimmed, 2-12 chars, letters/digits/_/space.
  constraint learning_leaderboard_nickname_format check (
    nickname = btrim(nickname)
    and char_length(nickname) between 2 and 12
    and nickname ~ '^[A-Za-z0-9_ ]+$'
  )
);

-- Case-insensitive uniqueness (same as Classic migration 002).
create unique index if not exists learning_leaderboard_nickname_lower_key
  on public.learning_leaderboard (lower(nickname));

-- Top 10 query: order by high_score desc, updated_at asc.
create index if not exists learning_leaderboard_rank_idx
  on public.learning_leaderboard (high_score desc, updated_at asc);

-- Server-side guard (own function, independent of the Classic one): an update can never lower
-- a best score or rename a row, and updated_at always comes from the database clock.
create or replace function public.learning_leaderboard_before_write()
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

drop trigger if exists learning_leaderboard_before_write on public.learning_leaderboard;
create trigger learning_leaderboard_before_write
  before insert or update on public.learning_leaderboard
  for each row execute function public.learning_leaderboard_before_write();

-- Row Level Security: the game (anon key) may read, add and raise scores. No deletes.
alter table public.learning_leaderboard enable row level security;

drop policy if exists "learning_leaderboard_select" on public.learning_leaderboard;
create policy "learning_leaderboard_select" on public.learning_leaderboard
  for select to anon, authenticated using (true);

drop policy if exists "learning_leaderboard_insert" on public.learning_leaderboard;
create policy "learning_leaderboard_insert" on public.learning_leaderboard
  for insert to anon, authenticated with check (true);

drop policy if exists "learning_leaderboard_update" on public.learning_leaderboard;
create policy "learning_leaderboard_update" on public.learning_leaderboard
  for update to anon, authenticated using (true) with check (true);

grant select, insert, update on public.learning_leaderboard to anon, authenticated;
revoke delete on public.learning_leaderboard from anon, authenticated;
