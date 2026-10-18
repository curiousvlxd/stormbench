ARG RUNTIME_IMAGE=ghcr.io/autonomy-logic/openplc-runtime:v4.2.4@sha256:8412ffa8a5de98caba7a2b1cc03009f944e8153175361b61297f34f7a501fc4f

FROM node:22-bookworm-slim@sha256:43ac6c60b8f89723f746e8a92ce91abd5017e627ce1ddfe4238355d3a30b772c AS bundler
ARG STRUCPP_VERSION=0.6.9
WORKDIR /opt/bundler
RUN npm init -y >/dev/null \
 && npm install --no-audit --no-fund \
      "https://github.com/Autonomy-Logic/STruCpp/releases/download/v${STRUCPP_VERSION}/strucpp-${STRUCPP_VERSION}.tgz"
COPY build-bundle.mjs ./
COPY src/ /src/plc/
ARG TASK_INTERVAL=T#1ms
ARG OPCUA_PORT=4840
ARG OPCUA_CYCLE_MS=100
RUN node build-bundle.mjs /src/plc /out/bundle --interval "${TASK_INTERVAL}" --modbus-port 502 \
      ${OPCUA_PORT:+--opcua-port "${OPCUA_PORT}" --opcua-cycle-ms "${OPCUA_CYCLE_MS}"}

FROM ${RUNTIME_IMAGE}
WORKDIR /workdir
COPY --from=bundler /out/bundle/ core/generated/
RUN cp plugins_default.conf plugins.conf \
 && bash scripts/compile.sh \
 && bash scripts/compile-clean.sh \
 && venvs/runtime/bin/python -c "from webserver.plcapp_management import update_plugin_configurations as u; u('core/generated')" \
 && rm -rf /var/lib/openplc-runtime \
 && grep -Eq '^modbus_slave,[^,]*,1,' plugins.conf \
 && { [ ! -f core/generated/conf/opcua.json ] || grep -Eq '^opcua,[^,]*,1,' plugins.conf; } \
 && ls build/libplc_*.so
EXPOSE 502 4840
