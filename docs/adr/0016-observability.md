# 0016. Observability: OpenTelemetry into VictoriaMetrics and VictoriaLogs, Grafana for the comparison

Status: accepted, 2026-10-17

## Context

The .NET services emit OpenTelemetry (ASP.NET Core, HTTP client and runtime metrics, the gateway's and the optimiser's own meters, traces, logs), but only to the Aspire dashboard, which keeps them in memory while the AppHost runs. Under docker compose they went nowhere. The historian of ADR 0015 needs a place to show its runs. Both should use the same few components, run locally without accounts, and leave the Aspire dashboard unchanged.

## Decision

- **OpenTelemetry through `StormBench.ServiceDefaults`, per configured endpoint.**
  - `OTEL_EXPORTER_OTLP_ENDPOINT` set (Aspire sets it): logs, metrics and traces go to the dashboard as before.
  - `VictoriaMetrics:Url` set: metrics also go over OTLP/HTTP to VictoriaMetrics (`/opentelemetry/v1/metrics`, every 10 s, cumulative).
  - `VictoriaLogs:Url` set: logs also go to VictoriaLogs (`/insert/opentelemetry/v1/logs`) ([VictoriaMetrics](https://docs.victoriametrics.com/victoriametrics/integrations/opentelemetry/), [VictoriaLogs](https://docs.victoriametrics.com/victorialogs/data-ingestion/opentelemetry/)).
  - Nothing set, nothing exported. Traces stay with the dashboard.
- **The Python simulator's logs too.** With `VictoriaLogs__Url` set (both orchestrations set it), `stormbench_sim.telemetry` adds an OpenTelemetry log handler (`opentelemetry-sdk` with the OTLP/HTTP exporter, Apache-2.0) to Python's root logger, so the simulator's lines land in VictoriaLogs next to the services' under `service.name` `simulator`. Without it the simulator logs to its console only.
- **Signal-specific exporters.** `UseOtlpExporter()` cannot be combined with further OTLP exporters (it throws `NotSupportedException`, as documented), so the dashboard exporter is added per signal and the two stores as named exporters next to it. They get their own plain `HttpClient` without the dashboard's headers: through the service defaults' `IHttpClientFactory`, every export was logged and retried, and those log lines were exported again.
- **VictoriaMetrics** (Apache-2.0) is both the historian of ADR 0015 and the metrics store, in one container. `-opentelemetry.usePrometheusNaming` turns `http.server.request.duration` into `http_server_request_duration_seconds`, so the same PromQL works for both. **VictoriaLogs** (Apache-2.0) keeps the logs, searchable in its own UI at `/select/vmui`.
- **Grafana, official image, unmodified** (AGPL-3.0, run as a separate container, never linked into our code). Data sources and the dashboard are provisioned from `deploy/grafana` ([provisioning](https://grafana.com/docs/grafana/latest/administration/provisioning/)): VictoriaMetrics through its Prometheus query API, and the run store in PostgreSQL. Anonymous users are viewers ([anonymous access](https://grafana.com/docs/grafana/latest/setup-grafana/configure-access/configure-authentication/anonymous-auth/)) and the dashboard is the home page, so a visitor sees it without logging in. Usage reporting, update checks and the news feed are off.
- **One dashboard, "Algorithm comparison"**, in UTC so model times read as the model writes them:
  - key figures of the selected run (overflow, Base Case, change, steps, mismatches, alarms raised);
  - the cumulative overflow of every run of the selected storm, with the Base Case as a dashed grey line;
  - the table of runs with their change against the Base Case (each run links to itself with its storm window);
  - the tank levels and the alarm journal of the selected run.

  Colour only where it carries meaning: less than the Base Case is green, more is red, alarm priorities keep their HMI colours.
- **Wired into both orchestrations, pinned by version and digest.** VictoriaMetrics v1.153.0, VictoriaLogs v1.53.0, PostgreSQL 17.11-alpine, Grafana 13.2.4. Host ports avoid the defaults a machine may already use (3000, 5432, 8428, 9428): Aspire 23000, 28428, 29428, 25432; compose 33000, 38428, 39428, 35432, on 127.0.0.1 only. Data live in named volumes. In Aspire, PostgreSQL comes from `Aspire.Hosting.PostgreSQL` with a generated password; the Grafana resource and the `scada` resource both link straight to the dashboard.
- **Nothing on the lockstep path waits for them.** Scada and the gateway start without the stores; Scada applies its schema migrations when PostgreSQL answers (ADR 0015).

## Consequences

- The Aspire dashboard works as before; in compose, which has none, the services' metrics and logs are now kept. Grafana is at http://localhost:23000 (Aspire) or http://localhost:33000 (compose).
- Grafana's admin login is the image default (`admin`, change requested on first login); viewers need none. This is a local demo setup, not a hardened one.
- Grafana has no VictoriaLogs data source without a plugin download at start, so logs are read in VictoriaLogs' own UI; the dashboard needs no plugin.
- Four more images are pulled on the first start, about 2.5 GB on disk once unpacked (Grafana 1.9 GB, PostgreSQL 0.4 GB, VictoriaMetrics and VictoriaLogs about 50 MB each, measured with Docker Desktop).
