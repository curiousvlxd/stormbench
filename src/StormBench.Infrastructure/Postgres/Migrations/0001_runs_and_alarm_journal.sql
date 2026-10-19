-- Run summaries and the alarm journal (ADR 0015). IF NOT EXISTS: databases made before the
-- migrations were versioned already have these tables.
CREATE TABLE IF NOT EXISTS runs (
    id              text PRIMARY KEY,
    sim_run         bigint NOT NULL,
    storm           text NOT NULL,
    storm_label     text,
    algorithm       text NOT NULL,
    algorithm_label text,
    pace_s          double precision,
    state           text NOT NULL,
    storm_start     timestamp,
    storm_end       timestamp,
    started_at      timestamptz NOT NULL,
    finished_at     timestamptz,
    steps           bigint NOT NULL,
    steps_planned   bigint NOT NULL,
    overflow_m3     double precision NOT NULL,
    base_case_m3    double precision,
    change_pct      double precision,
    wwtp_inflow_m3  double precision,
    mismatches      bigint NOT NULL
);

CREATE TABLE IF NOT EXISTS alarm_journal (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id      text,
    alarm_id    text NOT NULL,
    source      text NOT NULL,
    message     text NOT NULL,
    priority    smallint NOT NULL,
    from_state  text NOT NULL,
    to_state    text NOT NULL,
    wall_time   timestamptz NOT NULL,
    sim_time    timestamp
);

CREATE INDEX IF NOT EXISTS alarm_journal_run ON alarm_journal (run_id, id);
