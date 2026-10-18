FROM ghcr.io/astral-sh/uv:0.11.6@sha256:b1e699368d24c57cda93c338a57a8c5a119009ba809305cc8e86986d4a006754 AS uv

FROM python:3.13-slim@sha256:7c61056e61ac89e852de05f3dc6fa51a6dd2181797bceed46aa725dd7cb2cd3b
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PYTHON_DOWNLOADS=never
COPY --from=uv /uv /usr/local/bin/uv
WORKDIR /app
COPY simulator/pyproject.toml simulator/uv.lock simulator/
RUN cd simulator && uv sync --frozen --no-dev --no-install-project
COPY simulator/stormbench_sim simulator/stormbench_sim
RUN cd simulator && uv sync --frozen --no-dev
COPY models/fetch_astlingen.py models/
COPY fonts/ fonts/
ENV PATH=/app/simulator/.venv/bin:$PATH \
    STORMBENCH_MODEL_DIR=/app/models/astlingen \
    STORMBENCH_RESULTS_DIR=/app/results \
    STORMBENCH_WORK_DIR=/tmp/work \
    MPLCONFIGDIR=/tmp/matplotlib
ENTRYPOINT ["python", "-m", "stormbench_sim"]
CMD ["--help"]
