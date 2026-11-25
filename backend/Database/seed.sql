-- Optional sample data, useful for screenshots of the tables.

INSERT INTO UserProfile (display_name, preferred_temperature_unit, preferred_locale)
VALUES ('Radit', 'C', 'en-US');

INSERT INTO City (name, country_code, admin_region, latitude, longitude, timezone, elevation_m) VALUES
('Yogyakarta', 'ID', 'DI Yogyakarta',  -7.7956, 110.3695, 'Asia/Jakarta', 114),
('Jakarta',    'ID', 'DKI Jakarta',    -6.2088, 106.8456, 'Asia/Jakarta',   8),
('Tokyo',      'JP', 'Tokyo',          35.6762, 139.6503, 'Asia/Tokyo',    40);

INSERT INTO Watchlist (profile_id, city_id, alert_threshold_zscore, notes) VALUES
(1, 1, 2.0, 'Home city'),
(1, 2, 2.5, NULL);

INSERT INTO Baseline (city_id, metric_type, month, mean_value, stddev_value, sample_size, baseline_start_year, baseline_end_year) VALUES
(1, 'temperature_2m', 9, 26.4, 0.8, 900, 1995, 2024),
(2, 'temperature_2m', 9, 28.1, 0.7, 900, 1995, 2024);

INSERT INTO Advisory (metric_type, severity_tier, locale, category, title, body) VALUES
('temperature_2m', 'warning', 'en-US', 'health',
 'Extreme heat',
 'Drink plenty of water, avoid outdoor activity at midday, and check on vulnerable people.'),
-- Readings can be unusual in either direction (hot or cold, wet or dry), so the text stays neutral.
('temperature_2m', 'watch',   'en-US', 'health',
 'Unusual temperature',
 'Dress for the weather and drink water regularly. Take extra care with babies, older people and anyone who is ill.'),
('temperature_2m', 'watch',   'en-US', 'climate',
 'Temperature out of the usual range',
 'Today is noticeably warmer or colder than the 30-year normal for this month. One unusual day is common; watch whether it lasts.'),
('temperature_2m', 'warning', 'en-US', 'climate',
 'Temperature far from normal',
 'Today is about three standard deviations from the normal for this month. Readings like this happen only a few times a year.'),
('temperature_2m', 'extreme', 'en-US', 'health',
 'Dangerous temperature',
 'Stay indoors in a cooled or heated room when you can, avoid hard physical work, and check on vulnerable people several times a day.'),
('temperature_2m', 'extreme', 'en-US', 'climate',
 'Record-level temperature',
 'Today is four or more standard deviations from the normal. In the 30-year history this almost never happens.'),
('precipitation',  'watch',   'en-US', 'health',
 'Unusual rainfall',
 'Watch for slippery roads and standing water, and keep an umbrella or rain gear with you.'),
('precipitation',  'watch',   'en-US', 'climate',
 'Rainfall out of the usual range',
 'Rain today is well away from the normal for this month. Rain varies a lot from day to day, so treat this tier as a rough signal.'),
('precipitation',  'warning', 'en-US', 'health',
 'Heavy rain',
 'Avoid flooded roads and rivers, keep drains near your home clear, and follow local flood warnings.'),
('precipitation',  'warning', 'en-US', 'climate',
 'Rainfall far from normal',
 'Rain today is about three standard deviations from the normal for this month. Local flooding is more likely.'),
('precipitation',  'extreme', 'en-US', 'health',
 'Severe rain',
 'Stay away from rivers, slopes and low ground, prepare to move to higher ground, and follow official BMKG warnings.'),
('precipitation',  'extreme', 'en-US', 'climate',
 'Record-level rainfall',
 'Rain today is four or more standard deviations from the normal. Floods and landslides are possible.'),
('wind_speed_10m', 'watch',   'en-US', 'health',
 'Unusual wind',
 'Secure loose objects outdoors and take care on two-wheelers.'),
('wind_speed_10m', 'watch',   'en-US', 'climate',
 'Wind out of the usual range',
 'Today''s strongest wind is well away from the normal for this month.'),
('wind_speed_10m', 'warning', 'en-US', 'health',
 'Strong wind',
 'Stay away from trees, billboards and power lines, and avoid riding a motorbike in open areas.'),
('wind_speed_10m', 'warning', 'en-US', 'climate',
 'Wind far from normal',
 'Today''s strongest wind is about three standard deviations from the normal for this month.'),
('wind_speed_10m', 'extreme', 'en-US', 'health',
 'Dangerous wind',
 'Stay indoors, away from windows, and follow official BMKG warnings.'),
('wind_speed_10m', 'extreme', 'en-US', 'climate',
 'Record-level wind',
 'Today''s strongest wind is four or more standard deviations from the normal. In the 30-year history this almost never happens.');

INSERT INTO AnomalyEvent (city_id, baseline_id, advisory_id, metric_type, severity_tier, started_at, ended_at, peak_at,
                          peak_observed_value, peak_zscore, baseline_mean_snapshot, baseline_stddev_snapshot, status)
VALUES (1, 1, 1, 'temperature_2m', 'warning', '2026-09-18T05:00:00Z', '2026-09-19T10:00:00Z', '2026-09-18T07:00:00Z',
        29.1, 3.38, 26.4, 0.8, 'closed');
