import pytest

from stormbench_sim.network import read_map

pytestmark = pytest.mark.model


def test_subcatchments_and_gauges_come_from_the_model(network):
    town = read_map(network)
    assert len(town["catchments"]) == 10
    assert all(len(c["polygon"]) >= 4 for c in town["catchments"])
    assert {g["id"] for g in town["gauges"]} == {"RG1", "RG2", "RG3", "RG4"}


def test_main_river_crosses_the_map_and_the_creek_joins_it(network):
    town = read_map(network)
    river, creek = (next(w for w in town["waters"] if w["name"] == n) for n in ("Main River", "Park Creek"))
    xs = [p[0] for p in river["line"]]
    assert min(xs) < 0 and max(xs) > 10000
    mouth = creek["line"][-1]
    nearest = min(((x - mouth[0]) ** 2 + (y - mouth[1]) ** 2) ** 0.5 for x, y in river["line"])
    assert nearest < 400


def test_the_park_and_the_place_names_are_on_the_map(network):
    town = read_map(network)
    park = town["areas"][0]["polygon"]
    assert len(park) >= 5
    names = {p["name"] for p in town["places"]}
    assert {"Main River", "Park Creek", "Park", "Town Centre"} <= names
    assert set(town["tints"]) == {c["id"] for c in town["catchments"]}
