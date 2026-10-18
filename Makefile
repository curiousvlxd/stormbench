.PHONY: demo plc test test-model test-plc lint reuse proto-py clean

demo:            ## everything: model download, PLC, reference runs, PLC-in-the-loop year, report, animation
	docker compose up --build --exit-code-from simulator

plc:             ## only the PLC, Modbus TCP on localhost:1502
	docker compose up --build --detach --wait plc

test:            ## unit tests, no model or PLC needed
	cd simulator && uv run pytest -m "not model and not plc"

test-model:      ## reference and regression tests on a short storm (needs the model)
	python models/fetch_astlingen.py
	cd simulator && uv run pytest -m "model and not plc"

test-plc: plc    ## every PLC answer against the Python reference on a short storm
	python models/fetch_astlingen.py
	cd simulator && PLC_HOST=localhost PLC_PORT=1502 uv run pytest -m "plc"

lint:
	cd simulator && uv run ruff check stormbench_sim tests && uv run ruff format --check stormbench_sim tests

reuse:
	reuse lint

proto-py:        ## regenerate the Python Sparkplug B bindings (gencode 7.35.1: keep protobuf>=7.35.1,<8)
	cd simulator && uvx --from grpcio-tools==1.84.0 python -m grpc_tools.protoc \
		-I../src/StormBench.Sparkplug/Protos --python_out=stormbench_sim/sparkplug sparkplug_b.proto

clean:
	docker compose down --remove-orphans
	rm -rf work results/runs
