import html
import os
from pathlib import Path

KINDS = {"avatar", "compatibility", "diagnostic"}


def validate_composition(composition):
    if composition.get("kind") not in KINDS:
        raise ValueError("Unknown documentation composition")
    for field in ["kicker", "headline", "description", "image", "alt", "caption", "verdict"]:
        if not composition.get(field):
            raise ValueError("Incomplete documentation composition: " + field)
    if not all(composition["verdict"].get(field) for field in ["title", "text"]):
        raise ValueError("Composition requires an explicit boundary")
    if composition["kind"] == "avatar" and not composition.get("workflow"):
        raise ValueError("Avatar composition requires workflow")
    if composition["kind"] == "compatibility" and not composition.get("outcomes"):
        raise ValueError("Compatibility composition requires outcomes")
    if composition["kind"] == "diagnostic" and len(composition.get("cases", [])) != 2:
        raise ValueError("Diagnostic composition requires two distinct causes")
    for outcome in composition.get("outcomes", []):
        if outcome.get("state") not in {"yes", "partial", "no"}:
            raise ValueError("Invalid composition outcome state")


def render_composition(root, topic, host, technical, guide):
    content = guide["composition"]
    escape = html.escape
    kind = content["kind"]

    def asset(value):
        return escape(Path(os.path.relpath(root / value, host.parent)).as_posix(), quote=True)

    def image(filename, alternative, extra=""):
        return '<img src="' + asset("images/" + filename) + '" alt="' + escape(alternative, quote=True) + '" decoding="async" ' + extra + '>'

    heading = escape(content["headline"]).replace("\n", "<br>")
    parts = ['<div class="visual-guide field-guide field-guide--' + kind + '" data-visual-guide markdown="0">']
    parts.append('<div class="field-stage"><div class="field-copy">')
    parts.append('<h1 id="answer-title-' + topic["id"] + '" tabindex="-1">' + heading + '</h1>')
    parts.append('<p class="visual-lead field-description">' + escape(content["description"]) + '</p>')
    if kind == "compatibility":
        parts.append('<dl class="field-outcomes">')
        for outcome in content["outcomes"]:
            parts.append('<div class="field-outcome--' + outcome["state"] + '"><dt>' + escape(outcome["label"]) + '</dt><dd>' + escape(outcome["value"]) + '</dd></div>')
        parts.append('</dl>')
    if kind == "avatar":
        parts.append('<a class="field-primary" href="#demo-' + topic["id"] + '">Watch the import <span aria-hidden="true">&#8599;</span></a>')
    parts.append('</div><figure class="field-art">')
    if kind == "compatibility":
        parts.append('<div class="field-comparison" data-field-comparison style="--field-position:50%"><div class="field-comparison__images">')
        parts.append(image("avatar-vrchat.png", "Original VRChat appearance of the same avatar.", 'class="field-comparison__source"'))
        parts.append(image("avatar-resonite.png", "Resonite conversion of the example avatar.", 'class="field-comparison__result"'))
        parts.append('<span class="field-comparison__seam" aria-hidden="true"></span><input type="range" min="0" max="100" value="50" aria-label="Compare source and Resonite appearance" aria-valuetext="50 percent source appearance" hidden></div><div class="field-comparison__labels"><span>VRChat original</span><span>Resonite conversion</span></div></div>')
    else:
        parts.append(image(content["image"], content["alt"], 'class="field-art__image"'))
    parts.append('<figcaption>' + escape(content["caption"]) + '</figcaption></figure></div>')
    verdict = content["verdict"]
    if kind == "compatibility":
        parts.append('<p class="field-boundary"><strong>' + escape(verdict["title"]) + '</strong> ' + escape(verdict["text"]) + '</p>')
    else:
        parts.append('<div class="field-verdict"><strong>' + escape(verdict["title"]) + '</strong><p>' + escape(verdict["text"]) + '</p></div>')
    if kind == "avatar":
        parts.append('<div class="field-process"><div class="field-process__intro"><p class="field-kicker">In the station</p><h2>Preview first.<br>Create when ready.</h2><p>' + escape(content["detail"]["text"]) + '</p></div><ol class="field-workflow">')
        for step in content["workflow"]:
            parts.append('<li><strong>' + escape(step["title"]) + '</strong><p>' + escape(step["text"]) + '</p></li>')
        parts.append('</ol></div>')
        poster = Path(os.path.relpath(root / "images/landing-poster.jpg", root / topic["route"])).as_posix()
        parts.append('<div class="field-film" id="demo-' + topic["id"] + '"><div><p class="field-kicker">Actual demo footage</p><h2>See the station in use.</h2><p>One recorded import, not a promise of full compatibility.</p></div><video controls playsinline preload="none" poster="' + escape(poster, quote=True) + '"><source src="' + asset("assets/landing-background.webm") + '" type="video/webm"><source src="' + asset("assets/landing-background.mp4") + '" type="video/mp4">Your browser does not support video.</video></div>')
    if kind == "diagnostic":
        parts.append('<div class="field-cases">')
        for case in content["cases"]:
            parts.append('<div><p class="field-kicker">' + escape(case["label"]) + '</p><h2>' + escape(case["title"]) + '</h2><p>' + escape(case["text"]) + '</p></div>')
        parts.append('</div>')
    if guide.get("notice"):
        parts.append('<p class="visual-notice">' + escape(guide["notice"]) + '</p>')
    parts.append('</div>')
    primary = '\n'.join(parts).replace('><', '>\n<')
    reference = '<details class="visual-reference" id="reference-' + topic["id"] + '" markdown="1">\n<summary>' + escape(guide.get("reference", "Technical reference")) + '</summary>\n\n' + technical + '\n\n</details>'
    return primary + '\n\n' + reference
