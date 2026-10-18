from __future__ import annotations

from enum import StrEnum

GROUP_ID = "Astlingen"
SIM_NODE_ID = "Sim01"
PRIMARY_HOST_ID = "Scada01"

BD_SEQ = "bdSeq"
REBIRTH = "Node Control/Rebirth"

NETWORK = "Model/Network"
CATALOG = "Scenario/Catalog"

STATE = "Scenario/State"
RUN = "Scenario/Run"
STORM = "Scenario/Storm"
ALGORITHM = "Scenario/Algorithm"
PACE = "Scenario/Pace"
STEP = "Scenario/Step"
STEPS = "Scenario/Steps"
CLOCK = "Scenario/Clock"
MESSAGE = "Scenario/Message"

START = "Scenario/Start"
STOP = "Scenario/Stop"

RAIN_INTENSITY = "Rain/Intensity"
OVERFLOW_TOTAL = "Overflow/Total"
WWTP_INFLOW = "WWTP/Inflow"
BASE_CASE_OVERFLOW_TOTAL = "Reference/BaseCase/OverflowTotal"
MISMATCHES = "Check/Mismatches"

CENTER_LINK_CUT = "Fault/CenterLinkCut"
CENTER_LINK_UP = "Center/LinkUp"
STUCK_GATE = "Fault/StuckGate"
FROZEN_SENSOR = "Fault/FrozenSensor"
PLC_STEP = "Lockstep/PlcStep"


def overflow_active(point: str) -> str:
    """Boolean: more than 0.1 m³ spilled at the point during the last control step."""
    return f"Overflow/{point}/Active"


def valve_opening(valve: str) -> str:
    """Double: opening of a controlled outlet as the model applies it (position feedback)."""
    return f"Outlets/{valve}/Opening"


def true_level(tank: str) -> str:
    """Double: the tank's real level in the model, whatever its sensor reports."""
    return f"Storage/{tank}/TrueLevel"


def overflow_volume(point: str) -> str:
    """Double: m³ spilled at the point since the run started."""
    return f"Overflow/{point}/Volume"


class ScenarioState(StrEnum):
    IDLE = "idle"
    PREPARING = "preparing"
    RUNNING = "running"
    FINISHED = "finished"
    STOPPED = "stopped"
    FAILED = "failed"
