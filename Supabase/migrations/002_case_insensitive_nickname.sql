-- PongAI migration 002: case-insensitive nickname uniqueness.
-- Run once in the Supabase dashboard: SQL Editor -> New query -> paste -> Run.
-- Safe to re-run. Does not touch the existing table, trigger, score rules or RLS policies.
--
-- Why: the original `nickname text not null unique` constraint is case-sensitive, so
-- 'Itamar' and 'itamar' could still become two rows. This adds a unique index on lower(nickname).

-- 1) Check for existing case-insensitive duplicates first (must return 0 rows).
--    If it returns rows, delete or rename the extra rows before step 2.
select lower(nickname) as nickname_key, count(*) as rows, array_agg(nickname) as spellings
from public.leaderboard
group by lower(nickname)
having count(*) > 1;

-- 2) Enforce one row per nickname regardless of capitalization.
create unique index if not exists leaderboard_nickname_lower_key
  on public.leaderboard (lower(nickname));
