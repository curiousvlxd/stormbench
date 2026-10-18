from stormbench_sim.spills import count_12_24, discharging_steps

H = 3600


def test_one_short_discharge_is_one_spill():
    assert count_12_24([0, 300, 600]) == 1


def test_discharge_within_the_first_twelve_hours_stays_one_spill():
    assert count_12_24([0, 11 * H]) == 1


def test_each_following_24_hour_block_with_discharge_counts_once():
    assert count_12_24([0, 13 * H, 20 * H, 40 * H]) == 3


def test_an_empty_24_hour_block_starts_a_new_sequence():
    assert count_12_24([0, 50 * H, 55 * H]) == 2


def test_no_discharge_no_spill():
    assert count_12_24([]) == 0


def test_discharging_steps_reads_growth_of_the_cumulative_volume():
    times = [300, 600, 900, 1200]
    volume = [0.0, 5.0, 5.2, 9.0]
    assert discharging_steps(times, volume) == [300, 900]
