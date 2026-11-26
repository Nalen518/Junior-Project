-- Black-box test data for ClimaPulse. Load ONLY into the separate database climapulse_bbt,
-- after schema.sql and seed.sql. The first statement refuses any other database.
--
-- PowerShell, PostgreSQL 18 on port 3000 (psql asks for the postgres password; none is stored here):
--   & "C:\Program Files\PostgreSQL\18\bin\createdb.exe" -h localhost -p 3000 -U postgres climapulse_bbt
--   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -p 3000 -U postgres -d climapulse_bbt -v ON_ERROR_STOP=1 -f backend/Database/schema.sql
--   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -p 3000 -U postgres -d climapulse_bbt -v ON_ERROR_STOP=1 -f backend/Database/seed.sql
--   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -p 3000 -U postgres -d climapulse_bbt -v ON_ERROR_STOP=1 -f backend/Database/blackbox-fixtures.sql
-- Then start the app against it for this PowerShell window only:
--   $env:CLIMAPULSE_DB_CONNECTION = 'Host=localhost;Port=3000;Database=climapulse_bbt;Username=postgres;Password=<your password>'
--
-- What it adds:
--   (a) Jakarta's temperature baseline for the current month (Asia/Jakarta) gets mean 15.0 and
--       standard deviation 1.0, so Refresh on Jakarta scores any real reading as Extreme.
--       Do not run "Refresh baselines" on Jakarta during the test: it recomputes and undoes this.
--       Jakarta temperature has no active event, so that Refresh opens a new one.
--   (b) 25 ledger events over 2026 for Yogyakarta, Jakarta and Tokyo, all 3 metrics, tiers
--       watch, warning and extreme, 20 closed and 5 active (at most one active per city and metric),
--       each with baseline snapshots and the health advisory for its metric and tier.
-- Load it once, into a fresh database: a second run refuses.

DO $$
BEGIN
    IF current_database() <> 'climapulse_bbt' THEN
        RAISE EXCEPTION 'blackbox-fixtures.sql only loads into climapulse_bbt, not %', current_database();
    END IF;
    IF (SELECT count(*) FROM AnomalyEvent) > 1 THEN
        RAISE EXCEPTION 'Fixtures already loaded: load into a fresh climapulse_bbt (schema.sql, seed.sql, then this file).';
    END IF;
END $$;

BEGIN;

-- 25 events. z is signed: temperature and wind can be low (cold, calm); rain is only high.
-- |z| matches the tier: watch 2 to 3, warning 3 to 4, extreme 4 and up (SRD OPEN-1).
CREATE TEMP TABLE bbt_event (
    city        TEXT,
    metric      TEXT,
    tier        TEXT,
    z           DOUBLE PRECISION,
    started     TIMESTAMPTZ,
    peak_hours  INTEGER,
    ended       TIMESTAMPTZ   -- NULL means still active
) ON COMMIT DROP;

INSERT INTO bbt_event VALUES
('Yogyakarta', 'temperature_2m', 'watch',    2.4, '2026-01-14 06:00Z',  6, '2026-01-16 09:00Z'),
('Yogyakarta', 'temperature_2m', 'watch',   -2.2, '2026-04-08 05:00Z',  3, '2026-04-08 18:00Z'),
('Yogyakarta', 'temperature_2m', 'extreme',  4.3, '2026-10-21 05:00Z', 30, '2026-10-24 11:00Z'),
('Yogyakarta', 'temperature_2m', 'warning',  3.1, '2026-11-23 05:00Z',  8, NULL),
('Yogyakarta', 'precipitation',  'warning',  3.4, '2026-02-03 08:00Z', 10, '2026-02-05 07:00Z'),
('Yogyakarta', 'precipitation',  'watch',    2.1, '2026-03-17 09:00Z',  2, '2026-03-17 20:00Z'),
('Yogyakarta', 'precipitation',  'extreme',  4.6, '2026-11-19 07:00Z', 12, '2026-11-20 15:00Z'),
('Yogyakarta', 'wind_speed_10m', 'watch',    2.6, '2026-06-11 04:00Z',  5, '2026-06-12 06:00Z'),
('Yogyakarta', 'wind_speed_10m', 'watch',   -2.3, '2026-08-29 04:00Z',  4, '2026-08-30 05:00Z'),
('Jakarta',    'temperature_2m', 'warning',  3.6, '2026-05-02 05:00Z', 26, '2026-05-04 10:00Z'),
('Jakarta',    'temperature_2m', 'watch',    2.2, '2026-09-09 05:00Z',  4, '2026-09-10 09:00Z'),
('Jakarta',    'precipitation',  'extreme',  4.8, '2026-01-07 10:00Z', 20, '2026-01-09 08:00Z'),
('Jakarta',    'precipitation',  'warning',  3.2, '2026-02-14 09:00Z',  6, '2026-02-15 12:00Z'),
('Jakarta',    'precipitation',  'watch',    2.5, '2026-11-24 08:00Z',  3, NULL),
('Jakarta',    'wind_speed_10m', 'warning',  3.0, '2026-03-22 05:00Z',  7, '2026-03-23 08:00Z'),
('Jakarta',    'wind_speed_10m', 'watch',    2.1, '2026-07-15 06:00Z',  2, '2026-07-15 22:00Z'),
('Jakarta',    'wind_speed_10m', 'extreme',  4.1, '2026-11-20 06:00Z', 30, NULL),
('Tokyo',      'temperature_2m', 'warning', -3.3, '2026-01-26 20:00Z', 10, '2026-01-28 23:00Z'),
('Tokyo',      'temperature_2m', 'extreme',  4.2, '2026-07-30 03:00Z', 54, '2026-08-02 06:00Z'),
('Tokyo',      'temperature_2m', 'watch',   -2.4, '2026-11-22 21:00Z',  9, NULL),
('Tokyo',      'precipitation',  'watch',    2.8, '2026-06-18 00:00Z',  5, '2026-06-19 03:00Z'),
('Tokyo',      'precipitation',  'extreme',  4.4, '2026-09-05 02:00Z', 15, '2026-09-06 09:00Z'),
('Tokyo',      'precipitation',  'warning',  3.5, '2026-10-12 01:00Z',  4, '2026-10-12 21:00Z'),
('Tokyo',      'wind_speed_10m', 'warning',  3.8, '2026-03-03 03:00Z',  6, '2026-03-04 05:00Z'),
('Tokyo',      'wind_speed_10m', 'watch',    2.3, '2026-09-28 04:00Z', 20, NULL);

-- Plausible monthly normals per city and metric, used only where seed.sql has no baseline.
CREATE TEMP TABLE bbt_normal (city TEXT, metric TEXT, mean DOUBLE PRECISION, stddev DOUBLE PRECISION) ON COMMIT DROP;
INSERT INTO bbt_normal VALUES
('Yogyakarta', 'temperature_2m', 26.5, 0.9), ('Yogyakarta', 'precipitation', 9.0, 11.0), ('Yogyakarta', 'wind_speed_10m', 14.0, 4.0),
('Jakarta',    'temperature_2m', 28.0, 0.8), ('Jakarta',    'precipitation', 8.0, 10.0), ('Jakarta',    'wind_speed_10m', 13.0, 3.5),
('Tokyo',      'temperature_2m', 16.0, 3.0), ('Tokyo',      'precipitation', 4.5,  7.0), ('Tokyo',      'wind_speed_10m', 18.0, 5.0);

-- One baseline per city, metric and month an event needs. Existing seed baselines are kept.
INSERT INTO Baseline (city_id, metric_type, month, mean_value, stddev_value, sample_size, baseline_start_year, baseline_end_year)
SELECT DISTINCT c.city_id, e.metric, EXTRACT(MONTH FROM e.started AT TIME ZONE 'UTC')::INTEGER,
       n.mean, n.stddev, 900, 1995, 2024
FROM bbt_event e
JOIN City c ON c.name = e.city
JOIN bbt_normal n ON n.city = e.city AND n.metric = e.metric
ON CONFLICT (city_id, metric_type, month) DO NOTHING;

-- (a) Jakarta temperature this month: mean far below any real reading, standard deviation above 0.
INSERT INTO Baseline (city_id, metric_type, month, mean_value, stddev_value, sample_size, baseline_start_year, baseline_end_year)
SELECT city_id, 'temperature_2m', EXTRACT(MONTH FROM now() AT TIME ZONE 'Asia/Jakarta')::INTEGER, 15.0, 1.0, 900, 1995, 2024
FROM City WHERE name = 'Jakarta'
ON CONFLICT (city_id, metric_type, month) DO UPDATE SET
    mean_value = excluded.mean_value, stddev_value = excluded.stddev_value;

-- (b) The events, with snapshots of their baseline and the latest en-US health advisory for metric and tier.
INSERT INTO AnomalyEvent (city_id, baseline_id, advisory_id, metric_type, severity_tier, started_at, ended_at, peak_at,
                          peak_observed_value, peak_zscore, baseline_mean_snapshot, baseline_stddev_snapshot, status, created_at)
SELECT c.city_id,
       b.baseline_id,
       (SELECT a.advisory_id FROM Advisory a
        WHERE a.metric_type = e.metric AND a.severity_tier = e.tier AND a.category = 'health' AND a.locale = 'en-US'
        ORDER BY a.version DESC LIMIT 1),
       e.metric,
       e.tier,
       to_char(e.started AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
       to_char(e.ended AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
       to_char((e.started + make_interval(hours => e.peak_hours)) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
       round((b.mean_value + e.z * b.stddev_value)::numeric, 1)::DOUBLE PRECISION,
       e.z,
       b.mean_value,
       b.stddev_value,
       CASE WHEN e.ended IS NULL THEN 'active' ELSE 'closed' END,
       to_char(e.started AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM bbt_event e
JOIN City c ON c.name = e.city
JOIN Baseline b ON b.city_id = c.city_id AND b.metric_type = e.metric
               AND b.month = EXTRACT(MONTH FROM e.started AT TIME ZONE 'UTC')::INTEGER
ORDER BY e.started;

-- Self-checks: stop (and roll back) if anything above did not land as intended.
DO $$
BEGIN
    IF (SELECT count(*) FROM AnomalyEvent) <> 26 THEN   -- 25 fixtures + 1 from seed.sql
        RAISE EXCEPTION 'Expected 26 events, found %', (SELECT count(*) FROM AnomalyEvent);
    END IF;
    IF EXISTS (SELECT 1 FROM AnomalyEvent WHERE advisory_id IS NULL) THEN
        RAISE EXCEPTION 'An event has no health advisory';
    END IF;
    IF EXISTS (SELECT 1 FROM AnomalyEvent WHERE status = 'active'
               GROUP BY city_id, metric_type HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'More than one active event for a city and metric';
    END IF;
    IF EXISTS (SELECT 1 FROM AnomalyEvent WHERE peak_at < started_at OR (ended_at IS NOT NULL AND peak_at > ended_at)) THEN
        RAISE EXCEPTION 'A peak lies outside its event';
    END IF;
END $$;

COMMIT;
