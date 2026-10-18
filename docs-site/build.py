"""Build both English and Japanese documentation sites into docs-site/site."""

import os
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def get_zensical_cmd() -> list[str]:
    if shutil.which("zensical"):
        return ["zensical"]
    if shutil.which("uv"):
        return ["uv", "run", "--with", "zensical==0.0.67", "zensical"]
    return [sys.executable, "-m", "zensical"]


def run_build(config_file: str, clean: bool = False) -> None:
    base_cmd = get_zensical_cmd()
    cmd = [*base_cmd, "build", "-f", config_file]
    if clean:
        cmd.append("--clean")
    print(f"Running: {' '.join(cmd)}")
    res = subprocess.run(cmd, cwd=str(HERE))
    if res.returncode != 0:
        print(f"Build failed for {config_file} with code {res.returncode}")
        sys.exit(res.returncode)


def main() -> None:
    # 1. Build English site to docs-site/site
    print("\n--- [1/3] Building English documentation site ---")
    run_build("zensical.toml", clean=True)

    # 2. Build Japanese site to docs-site/site/ja
    print("\n--- [2/3] Building Japanese documentation site ---")
    run_build("zensical.ja.toml", clean=False)

    # 3. Share media assets to site/ja
    print("\n--- [3/3] Synchronizing shared media assets ---")
    site_images = HERE / "site" / "images"
    ja_images = HERE / "site" / "ja" / "images"
    if site_images.exists():
        ja_images.mkdir(parents=True, exist_ok=True)
        shutil.copytree(site_images, ja_images, dirs_exist_ok=True)
        print(f"Synchronized {len(list(ja_images.glob('*')))} images to site/ja/images")

    site_assets = HERE / "site" / "assets"
    ja_assets = HERE / "site" / "ja" / "assets"
    if site_assets.exists():
        ja_assets.mkdir(parents=True, exist_ok=True)
        shutil.copytree(site_assets, ja_assets, dirs_exist_ok=True)
        print("Synchronized landing video/assets to site/ja/assets")

    print("\n=== Documentation build complete! ===")
    print("English site:  docs-site/site/index.html")
    print("Japanese site: docs-site/site/ja/index.html\n")


if __name__ == "__main__":
    main()
