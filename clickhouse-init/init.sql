CREATE DATABASE IF NOT EXISTS reports;
USE reports;

CREATE TABLE IF NOT EXISTS reports.report_view (
    date Date,
    user_id String,
    user_name String,
    total_movements UInt32,
    errors UInt32
) ENGINE = MergeTree()
ORDER BY (date, user_id);

CREATE TABLE reports.crm_users_stream
(
    id String,
    full_name String,
    email String,
    phone String
) ENGINE = Kafka()
SETTINGS
    kafka_broker_list = 'kafka:9092',
    kafka_topic_list = 'crm_cdc.public.crm_users',
    kafka_group_name = 'clickhouse_crm_consumer',
    kafka_format = 'JSONEachRow';

CREATE TABLE reports.crm_users_final
(
    id String,
    full_name String,
    email String,
    phone String
) ENGINE = MergeTree()
ORDER BY id;

CREATE MATERIALIZED VIEW reports.crm_users_mv TO reports.crm_users_final AS
SELECT
    id,
    full_name,
    email,
    phone
FROM reports.crm_users_stream;

CREATE TABLE reports.telemetry_final
(
    date Date,
    user_id String,
    total_movements UInt32,
    errors UInt32
) ENGINE = MergeTree()
ORDER BY (date, user_id);

CREATE MATERIALIZED VIEW reports.report_view_v2
ENGINE = MergeTree()
ORDER BY (date, user_id)
AS
SELECT
    t.date,
    t.user_id,
    u.full_name AS user_name,
    t.total_movements,
    t.errors
FROM reports.telemetry_final AS t
LEFT JOIN reports.crm_users_final AS u ON t.user_id = u.id;