# 0015. Historian in VictoriaMetrics, runs and alarm journal in PostgreSQL

Status: accepted, 2026-10-17

## Context

Phase 3 compares control algorithms over many runs, not only the current one. The HMI's trend lives in memory and ends with the run (ADR 0010), and alarms are forgotten once acknowledged and cleared. Two kinds of data must outlive a run:

- Time series: every control step of a run, with tank levels, setpoints, overflow per point and in total, the inflow to the treatment plant, the Base Case reference, rain, mode, mismatches and alarm counts.
- Records: one summary per run, so that runs can be listed and compared, and every ISA-18.2 state change of every alarm.

Both are written from the UNS host's path, which also carries the lockstep, so a slow or missing store must never hold up a step.

A test console run takes seconds to minutes of wall time for 36 hours to a year of model time (ADR 0006). The model time of the Astlingen benchmark is the year 2000.

## Decision

- **Time series in VictoriaMetrics, stamped with model time.** A new feature `Historian/Features/RunSeries` turns every new control step of the current run (Sim01's `Scenario/Step`) into samples at the step's `Scenario/Clock`, plus one set at the end of the window with the final totals. A run is then a time series over its storm window, and runs of the same storm overlay each other, which is the comparison Grafana draws. The alternative, wall time plus a `sim_time` value, would put runs of the same storm at different places on the time axis and make every comparison a query over a value instead of over time.
- **What model time requires of VictoriaMetrics.**
  - VictoriaMetrics rejects samples older than its retention (default one month): with the default, our test instance logged `cannot insert row with too small timestamp 959644800000` for 30 May 2000 and stored nothing. It runs with `-retentionPeriod=100y` ([retention](https://docs.victoriametrics.com/victoriametrics/single-server-victoriametrics/#retention)).
  - Out-of-order samples are accepted, but the query cache assumes current timestamps, so it runs with `-search.disableCache`, as the documentation recommends while samples from the past arrive ([backfilling](https://docs.victoriametrics.com/victoriametrics/single-server-victoriametrics/#backfilling)).
  - Each run has its own label value, so runs never overwrite each other.
- **Names and labels.** `stormbench_step`, `stormbench_rain_intensity_mm_per_hour`, `stormbench_overflow_total_cubic_meters`, `stormbench_basecase_overflow_total_cubic_meters`, `stormbench_overflow_volume_cubic_meters{point}`, `stormbench_wwtp_inflow_cubic_meters`, `stormbench_tank_level_meters{tank}`, `stormbench_valve_setpoint_ratio{valve}`, `stormbench_plc_mode`, `stormbench_shadow_mismatches`, `stormbench_alarms_active`, `stormbench_alarms_unacknowledged`. Every series also carries `run`, `storm` and `algorithm`. Volumes are cumulative since the start of the run, as Sim01 reports them. Tank levels and setpoints are the PLC's as Scada sees them, good quality only.
- **Prometheus text import.** Samples go as `name{labels} value timestamp_ms` lines to `/api/v1/import/prometheus` ([import](https://docs.victoriametrics.com/victoriametrics/single-server-victoriametrics/#how-to-import-data-in-prometheus-exposition-format)): plain HTTP, human readable, testable as a string, with explicit timestamps. Remote write and OTLP also carry timestamps, but need protobuf and more machinery.
- **Off the hot path.** Every store sits behind a small interface (`ISampleSink`, `IRunStore`, `IAlarmJournalStore`) whose methods only queue. A `BatchWriter` holds a bounded queue (records beyond it are dropped and counted) and one background loop that writes batches; a failed batch is logged and dropped. Nothing is retried beyond the HTTP client's standard resilience, and nothing waits: the UNS host's work per message is unchanged apart from building the samples.
- **Run summaries and the alarm journal in PostgreSQL 17.** Both are records with a key and a few columns, read as tables (the runs list, the journal of one run) and joined (alarms per run), which suits SQL; in VictoriaMetrics a summary would be a set of series with one sample each.
  - Two plain tables, `runs` and `alarm_journal`, read and written with Dapper over Npgsql: rows map to small row classes, a batch is one `INSERT ... SELECT FROM unnest(...)` over arrays of its columns. No ORM, no TimescaleDB.
  - Versioned schema: SQL files in `Postgres/Migrations` (`NNNN_name.sql`, embedded in the assembly), applied in order on first use, each recorded in `schema_migrations`, all in one transaction under a PostgreSQL advisory lock so that two Scadas starting together do not both migrate. The first migration is the original schema with `IF NOT EXISTS`, so databases created before versioning accept it unchanged.
  - The data source is named `runs` (its metrics carry that name, not the connection string). Npgsql's spans and metrics go to the same OpenTelemetry exporters as the rest (ADR 0016), and a health check reports an unresponsive store as degraded, not unhealthy.
  - Model times are `timestamp`, wall times `timestamptz`.
- **One row per run.** `RunRecorder` follows `Scenario/Run` and `Scenario/State` and saves the summary when a run starts and when it finishes, stops or fails: storm and algorithm with their labels, the storm window, started and finished (wall time), steps done and planned, total overflow, Base Case total, change in percent, inflow to the treatment plant, mismatches. The id combines the start time and the simulator's run number (`20261017-142530-3`), because the simulator counts from 1 again after a restart. A run whose end Scada did not see is closed as `interrupted` on the next run or when Scada restarts.
- **Change against the Base Case** is `(overflow - base) / base × 100`, left empty while the Base Case spills less than 1 m³ (the KPI tile's threshold).
- **Every transition in the journal.** The `AlarmEngine` reports every state change to its observers (normal to unacknowledged, acknowledged, returned unacknowledged, back to normal), in order, under its lock; the journal only queues. Each entry has the wall time, the model time of the data that caused it (for an acknowledgement, the model time last seen) and the run current at that moment. The run is passed in as a function, so the Alarms module does not depend on the historian.
- **Read without the HMI.** `GET /api/runs` lists the summaries, newest first; `GET /api/alarms/journal?run=` returns the journal of one run (or the newest entries). Both return 503 when no database is configured or reachable.
- **Optional.** Without `VictoriaMetrics:Url` and `ConnectionStrings:runs`, Scada runs as before. The stores are always registered and read their options: without a URL or connection string they keep nothing, and the run store reports that it is not configured. The composition root has no branch. The HMI's trend of the current run stays in memory: it needs no store and costs no round trip.

## Consequences

- Grafana compares any number of runs of a storm on one time axis, with the Base Case as a reference (ADR 0016). A dashboard's time range is the storm window in 2000, not "the last hour"; the runs table links each run to its window.
- One step writes about 30 samples, the rain year (105,406 steps) about three million, which VictoriaMetrics stores easily. The journal grows by a few rows per alarm.
- With `-search.disableCache` every query is computed afresh. For testbed data the cost is not measurable; a real plant historian, written in wall time, would keep the cache.
- The format, the sampling, the run summaries and the journal are unit tested. The stores are tested against PostgreSQL 17 and VictoriaMetrics in Testcontainers, including reading back samples from May 2000.
- If PostgreSQL or VictoriaMetrics is down, data from that period is lost and the log records how much. The lockstep and the HMI are not affected.
