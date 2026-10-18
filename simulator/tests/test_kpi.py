import numpy as np

from stormbench_sim.kpi import count_events, interval_volumes


def test_interval_volumes_start_from_zero():
    cum = np.array([[0.0, 1.0], [0.5, 1.0], [2.0, 4.0]])
    assert interval_volumes(cum).tolist() == [[0.0, 1.0], [0.5, 0.0], [1.5, 3.0]]


def test_events_merge_short_gaps():
    step = 300
    spilling = np.zeros(100, dtype=bool)
    spilling[[10, 11, 12]] = True
    spilling[[20, 21]] = True
    spilling[[40]] = True
    assert count_events(spilling, step, gap_s=3600) == 2
    assert count_events(spilling, step, gap_s=1800) == 3


def test_no_spill_no_event():
    assert count_events(np.zeros(10, dtype=bool), 300) == 0
