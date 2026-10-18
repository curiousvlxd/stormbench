import math

import pytest

from stormbench_sim import registers as reg


@pytest.mark.parametrize("value", [0.0, 1.0, 0.2366, 4.999999999, 1e-300, -2.5, math.pi])
def test_lreal_roundtrip_is_exact(value):
    assert reg.unpack_lreal(reg.pack_lreal(value)) == value


def test_lreal_nan_survives():
    assert math.isnan(reg.unpack_lreal(reg.pack_lreal(float("nan"))))


@pytest.mark.parametrize("value", [0, 1, 105_406, 2**53 + 1, -1, 2**63 - 1])
def test_lint_roundtrip(value):
    assert reg.unpack_lint(reg.pack_lint(value)) == value


def test_word_order_is_big_endian_high_word_first():
    assert reg.pack_lreal(1.0) == [0x3FF0, 0, 0, 0]
    assert reg.pack_lint(1) == [0, 0, 0, 1]


def test_register_map_matches_plc_program():
    assert reg.ml(0) == 4096
    assert reg.ml(reg.IN_DEPTH_FIRST) == 4108
    assert reg.ml(reg.IN_STEP_REQ) == 4132
    assert reg.ml(reg.OUT_SETPOINT_FIRST) == 4160
    assert reg.ml(reg.OUT_SCAN_COUNT) == 4184
    assert reg.ml(reg.OUT_STEP_ACK) == 4188
    assert reg.ml(reg.CENTER_SETPOINT_FIRST) == 4136
    assert reg.ml(reg.CENTER_STEP) == 4152
    assert reg.IN_COUNT * reg.REGS_PER_VALUE <= 123
    assert reg.OUT_COUNT * reg.REGS_PER_VALUE <= 125


def test_step_ack_is_the_last_located_variable():
    """OpenPLC v4 commits outputs in declaration order; the ACK must be committed after the setpoints."""
    from pathlib import Path

    st = (Path(__file__).resolve().parents[2] / "plc" / "src" / "main.st").read_text(encoding="utf-8")
    located = [line.split()[0] for line in st.splitlines() if " AT %" in line]
    assert located[-1] == "StepAck"
    assert located.index("StepReq") < located.index("SetpointV2")


def test_plc_source_declares_the_same_addresses():
    from pathlib import Path

    st = (Path(__file__).resolve().parents[2] / "plc" / "src" / "main.st").read_text(encoding="utf-8")
    for name, index in [
        ("StepReq", reg.IN_STEP_REQ),
        ("StepAck", reg.OUT_STEP_ACK),
        ("SetpointV2", reg.OUT_SETPOINT_FIRST),
        ("Status", reg.OUT_STATUS),
        ("DepthT1", reg.IN_DEPTH_FIRST),
        ("CmdMode", reg.IN_CMD_MODE),
        ("CenterSetpointV2", reg.CENTER_SETPOINT_FIRST),
        ("CenterStep", reg.CENTER_STEP),
    ]:
        assert f"{name} AT %ML{index} :" in st


def test_encode_decode_blocks():
    regs = reg.encode_inputs(7, 300, 1, True, (0.0, 1.0, 2.0, 3.0, 4.0, 5.0))
    assert len(regs) == reg.IN_COUNT * reg.REGS_PER_VALUE
    assert reg.unpack_lint(regs[-4:]) == 7
    out = []
    for words in (
        *(reg.pack_lreal(v) for v in (1.0, 0.6508, 0.3523, 0.4303)),
        reg.pack_lint(1),
        reg.pack_lint(0),
        reg.pack_lint(123),
        reg.pack_lint(7),
    ):
        out += words
    assert reg.decode_outputs(out) == (7, (1.0, 0.6508, 0.3523, 0.4303), 1, 0, 123)


def test_encode_rejects_wrong_tank_count():
    with pytest.raises(ValueError):
        reg.encode_inputs(1, 0, 1, True, (1.0, 2.0))


def test_encode_center_block():
    regs = reg.encode_center(42, (1.0, 0.6508, 0.3523, 0.4303))
    assert len(regs) == reg.CENTER_COUNT * reg.REGS_PER_VALUE
    assert reg.unpack_lint(regs[-4:]) == 42
    assert reg.unpack_lreal(regs[:4]) == 1.0
