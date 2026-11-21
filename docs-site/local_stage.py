import os
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKER = "<!-- local-mod-stage -->"


def render_local_stage(language="en", local=None):
    if language not in {"en", "ja"}:
        raise ValueError("Unsupported development-stage language")
    if local is None:
        local = os.environ.get("IMPORTER_DOCS_LOCAL_SNAPSHOT") == "1"
    return (HERE / "local-stage" / (language + ".html")).read_text(encoding="utf-8") if local else ""


def prepare_local_stage(pages, language="en"):
    for path in pages.rglob("*.md"):
        text = path.read_text(encoding="utf-8")
        if MARKER in text:
            locale = "ja" if path.relative_to(pages).parts[0] == "ja" else language
            stage = render_local_stage(locale)
            path.write_text(text.replace(MARKER, stage), encoding="utf-8")
