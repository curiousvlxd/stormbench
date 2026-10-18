from __future__ import annotations

from pathlib import Path

from stormbench_sim.runner import parse_report

HEADER = """
  Runoff Quantity Continuity     hectare-m        mm
  Continuity Error (%) .....        -0.429

  Flow Routing Continuity      hectare-m   10^6 ltr
  Continuity Error (%) .....        -0.044
"""

STORAGE = """
  **********************
  Storage Volume Summary
  **********************

  ------------------------------------------------------------------------------------------------
                         Average    Avg   Evap  Exfil     Maximum    Max    Time of Max    Maximum
  Storage Unit           1000 m³   Full   Loss   Loss     1000 m³   Full    days hr:min        CMS
  ------------------------------------------------------------------------------------------------
  T5                       0.123      8      0      0       0.600     86      0  06:00      0.100
"""

WET = """
  *********************
  Node Flooding Summary
  *********************

  Flooding refers to all water that overflows a node, whether it ponds or not.
  --------------------------------------------------------------------------
                                                             Total   Maximum
                                 Maximum   Time of Max       Flood    Ponded
                        Hours       Rate    Occurrence      Volume     Depth
  Node                 Flooded       CMS   days hr:min    10^6 ltr    Meters
  --------------------------------------------------------------------------
  CSO7                   13.43     0.486    288  18:20       3.972     0.000
  T1                    102.10     3.558    237  17:38      79.461     0.000

"""

DRY = """
  *********************
  Node Flooding Summary
  *********************

  No nodes were flooded.

"""


def write(tmp_path: Path, text: str) -> Path:
    rpt = tmp_path / "run.rpt"
    rpt.write_text(text, encoding="latin-1")
    return rpt


def test_flooded_nodes_are_read_in_cubic_metres(tmp_path: Path) -> None:
    report = parse_report(write(tmp_path, HEADER + WET + STORAGE))

    assert report["report_flood_m3"] == {"CSO7": 3972.0, "T1": 79461.0}
    assert report["runoff_continuity_error_pct"] == -0.429
    assert report["routing_continuity_error_pct"] == -0.044


def test_no_flooding_does_not_read_the_next_table(tmp_path: Path) -> None:
    report = parse_report(write(tmp_path, HEADER + DRY + STORAGE))

    assert report["report_flood_m3"] == {}
