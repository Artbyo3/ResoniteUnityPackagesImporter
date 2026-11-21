

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import tomllib
from pathlib import Path

from docs_workspace import export_workspace, prepare_workspace
from local_stage import prepare_local_stage

HERE = Path(__file__).resolve().parent


def get_zensical_cmd() -> list[str]:
    if shutil.which("zensical"):
        return ["zensical"]
    if shutil.which("uv"):
        return ["uv", "run", "--with", "zensical==0.0.67", "zensical"]
    return [sys.executable, "-m", "zensical"]


def run_build(config_file: str, clean: bool = False) -> None:
    config = (HERE / config_file).read_text(encoding="utf-8")
    project = tomllib.loads(config)["project"]
    with tempfile.TemporaryDirectory(prefix=".inline-pages-", dir=HERE) as temporary:
        staging = Path(temporary)
        if config_file == "zensical.toml":
            prepare_workspace(HERE / project["docs_dir"], staging / "pages")
        else:
            shutil.copytree(HERE / project["docs_dir"], staging / "pages")
        prepare_local_stage(staging / "pages", "en" if config_file == "zensical.toml" else "ja")
        paths = {
            "docs_dir": (staging / "pages").relative_to(HERE).as_posix(),
            "site_dir": project["site_dir"],
            "custom_dir": project["theme"]["custom_dir"],
        }
        config = re.sub(r'^(docs_dir|site_dir|custom_dir) = "[^"]+"$',
                        lambda match: f"{match[1]} = {json.dumps(str(paths[match[1]]))}",
                        config, flags=re.MULTILINE)
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", suffix=".toml",
                                         prefix=".inline-", dir=HERE, delete=False) as temporary_config:
            temporary_config.write(config)
            generated = Path(temporary_config.name)
        try:
            cmd = [*get_zensical_cmd(), "build", "-f", str(generated)]
            if clean:
                cmd.append("--clean")
            print(f"Building {config_file} with the documentation workspace")
            res = subprocess.run(cmd, cwd=str(HERE))
            if res.returncode != 0:
                sys.exit(res.returncode)
        finally:
            generated.unlink()


def main() -> None:
    parser = argparse.ArgumentParser(description='Build both English and Japanese documentation sites into docs-site/site.')
    parser.add_argument("--local", action="store_true", help="Preview unreleased working-tree component cards; do not publish this build.")
    args = parser.parse_args()
    os.environ["IMPORTER_DOCS_LOCAL_SNAPSHOT"] = "1" if args.local else "0"

    print("\n--- [1/3] Building English documentation site ---")
    run_build("zensical.toml", clean=True)
    export_workspace(HERE / "site")


    print("\n--- [2/3] Building Japanese documentation site ---")
    run_build("zensical.ja.toml", clean=False)


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
