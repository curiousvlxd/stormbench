from __future__ import annotations

import argparse
import hashlib
import sys
import urllib.request
from pathlib import Path

REPO = "open-toolbox/SWMM-Astlingen"
COMMIT = "e226b03fd4a3b91542476c314dc533c7549d1824"
BASE_URL = f"https://raw.githubusercontent.com/{REPO}/{COMMIT}"

FILES = {
    "Astlingen_SWMM.inp": "cb05a61feb67093573094eb29bf45172a83aee222d19fa81d38ab755fb7289c0",
    "EFD_Control.txt": "4c74a525ee6b483ef4e604b294ef353c0bdc36f05cf19be462196354ffb23d8f",
    "Readme.txt": "301b38a207df14e565c25096baa9d8ca5eb6f14d5ab244145f081c858349dbfc",
    "1Astlingen_Erft1.txt": "8b396a9bfa4b5f384be48af718a4f32dddedaa5e6cbc00f64d02f62a06ee8297",
    "2Astlingen_Erft2.txt": "b67862615b4158945ba942c6b9a906d5ce936550b839b7379549a09a1338f4d3",
    "3Astlingen_Erft3.txt": "d50b12efdd27dd1c6ee0703d2df2bcf7cfe07f7d7bd9d6f17fd19703923b9d0f",
    "4Astlingen_Erft4.txt": "700510061e2a675f51c2850734bb554315bbd7ec17734ad74f7883efd8bcdee0",
    "astingen.jpg": "223fde3188c3eb346954d69a538d95b0536fad1e22bd6ee2d3f849db94c0afb7",
}

ATTRIBUTION = (
    "Astlingen SWMM model by Congcong Sun (IRI, CSIC-UPC), supervised by Manfred Schuetze (ifak),\n"
    "Morten Borup and Luca Vezzaro (DTU). Please quote and mention the authors when using it:\n"
    "Sun et al. (2020), Water 12(4), 1034, https://doi.org/10.3390/w12041034"
)


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def fetch(dest: Path) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    for name, expected in FILES.items():
        target = dest / name
        if target.exists() and sha256_of(target) == expected:
            print(f"ok       {name}")
            continue
        partial = target.with_suffix(target.suffix + ".part")
        print(f"download {name}")
        with urllib.request.urlopen(f"{BASE_URL}/{name}", timeout=120) as response, partial.open("wb") as out:
            while chunk := response.read(1 << 20):
                out.write(chunk)
        actual = sha256_of(partial)
        if actual != expected:
            partial.unlink()
            raise SystemExit(f"hash mismatch for {name}: expected {expected}, got {actual}")
        partial.replace(target)
    print(f"\nAstlingen benchmark ready in {dest} (commit {COMMIT[:7]}).\n{ATTRIBUTION}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Download the Astlingen SWMM benchmark from its original repository and verify it.")
    parser.add_argument("--dest", type=Path, default=Path(__file__).resolve().parent / "astlingen")
    args = parser.parse_args(argv)
    fetch(args.dest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
