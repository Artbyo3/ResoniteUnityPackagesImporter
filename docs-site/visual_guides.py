import html
import json
import os
from pathlib import Path
from urllib.parse import urlsplit

from docs_compositions import render_composition, validate_composition
from docs_navigation import is_section_link

HERE = Path(__file__).resolve().parent
DIAGRAMS = {"install", "settings", "comparison", "package", "shader", "initial-state", "readiness", "conflict", "report"}


def load_visual_guides(path=HERE / "visual-guides.json"):
    guides = json.loads(path.read_text(encoding="utf-8"))["guides"]
    for identifier, guide in guides.items():
        if not guide.get("lead") or len(guide.get("steps", [])) > 3:
            raise ValueError("Invalid visual guide: " + identifier)
        if guide.get("walkthrough") and (not guide.get("steps") or len(guide["steps"]) != len(guide.get("media", [])) or not all(item.get("image") for item in guide["media"])):
            raise ValueError("Walkthrough requires one screenshot per step: " + identifier)
        demo = guide.get("demo")
        if demo is not None and (not isinstance(demo, dict) or any(not isinstance(demo.get(key), str) or not demo[key].strip() for key in ["title", "video", "fallback", "caption"])):
            raise ValueError("Incomplete walkthrough demo: " + identifier)
        if guide.get("diagram") and guide["diagram"] not in DIAGRAMS:
            raise ValueError("Unknown visual diagram: " + identifier)
        capture = guide.get("capture")
        if capture is not None and (not isinstance(capture, dict) or any(not isinstance(capture.get(key), str) or not capture[key].strip() for key in ["title", "caption"])):
            raise ValueError("Invalid capture placeholder: " + identifier)
        composition = guide.get("composition")
        if composition:
            validate_composition(composition)
            media_items = [*guide.get("media", []), {"image": composition["image"]}]
        else:
            media_items = guide.get("media", [])
        if demo:
            media_items = [*media_items, demo]
        for media in media_items:
            for key in ["image", "video", "fallback"]:
                if media.get(key):
                    folder = "images" if key == "image" else "assets"
                    asset = (HERE / "pages" / folder / media[key]).resolve()
                    if not asset.is_relative_to((HERE / "pages" / folder).resolve()) or not asset.is_file():
                        raise ValueError("Missing visual asset: " + media[key])
        for pair in guide.get("pairs", []):
            if pair.get("state", "neutral") not in {"yes", "partial", "no", "neutral"}:
                raise ValueError("Invalid visual state")
    return guides


def render_diagram(kind, asset_url):
    if kind == "install":
        return '<div class="visual-files" aria-label="Files to copy into the Resonite installation"><strong>Resonite installation</strong><div><span>rml_mods/</span><code>UnityPackageImporter.dll</code></div><div><span>rml_libs/</span><code>YamlDotNet.dll</code></div></div>'
    if kind == "settings":
        return '<dl class="visual-settings"><div><dt><code>importPrefab</code></dt><dd><strong>ON</strong> Station import</dd></div><div><dt><code>dumpPackageContents</code></dt><dd><strong>OFF</strong> Do not dump all files</dd></div><div><dt><code>importAsRawFiles</code></dt><dd><strong>OFF</strong> Do not bypass reconstruction</dd></div></dl>'
    if kind == "comparison":
        return '<div class="visual-comparison">' + ''.join('<figure><img src="' + asset_url("images/" + image) + '" alt="' + label + '" loading="lazy" decoding="async"><figcaption>' + label + '</figcaption></figure>' for image, label in [("avatar-vrchat.png", "VRChat original"), ("avatar-resonite.png", "Resonite conversion")]) + '</div>'
    if kind == "readiness":
        return '<div class="visual-readiness">' + ''.join('<div class="visual-state--' + state + '"><strong>' + title + '</strong><p>' + text + '</p></div>' for state, title, text in [("yes", "Ready", "Create when the action is available."), ("partial", "Partial", "Read the item warning. Creation still depends on readiness."), ("no", "Blocked", "Resolve the structural problem shown by the station.")]) + '</div>'
    if kind == "initial-state":
        return '<div class="visual-boundary"><div><span>At import</span><strong>Structure + starting appearance</strong><p>Only the supported operations and initial values below.</p></div><div><span>During use</span><strong>No full Unity behavior</strong><p>Live menus, toggles and parameter systems need separate translation.</p></div></div>'
    if kind == "conflict":
        return '<div class="visual-conflict"><div><strong>Package A</strong><span>Same asset ID</span><code>Content A</code></div><span aria-label="does not equal">&#8800;</span><div><strong>Package B</strong><span>Same asset ID</span><code>Content B</code></div><p>Conflict rejected. No silent overwrite.</p></div>'
    stages = {
        "package": [(".unitypackage", "Source assets"), ("Import station", "Reconstruct supported data"), ("Resonite", "Review before creating")],
        "shader": [("Unity material", "Textures, tint and supported properties"), ("Conversion", "Supported mappings only"), ("Native material", "PBS or Toon; not Unity shader code")],
        "report": [("Copy report", "Use the report panel when available"), ("Review draft", "Remove identifying information"), ("You submit", "Nothing uploads automatically")],
    }
    return '<ol class="visual-flow">' + ''.join('<li><strong>' + title + '</strong><span>' + text + '</span></li>' for title, text in stages[kind]) + '</ol>'


def render_visual_guide(root, topic, host, technical, guide):
    if guide.get("composition"):
        return render_composition(root, topic, host, technical, guide)
    escape = html.escape
    def asset_url(value):
        return escape(Path(os.path.relpath(root / value, host.parent)).as_posix(), quote=True)
    def target_url(value):
        parsed = urlsplit(value)
        if parsed.scheme or parsed.netloc:
            return escape(value, quote=True)
        return asset_url(parsed.path) + ("#" + escape(parsed.fragment, quote=True) if parsed.fragment else "")
    def video(item):
        return '<video controls playsinline preload="none" poster="' + asset_url("images/landing-poster.jpg") + '"><source src="' + asset_url("assets/" + item["video"]) + '" type="video/webm"><source src="' + asset_url("assets/" + item["fallback"]) + '" type="video/mp4">Your browser does not support this video.</video>'
    walkthrough = bool(guide.get("walkthrough"))
    walkthrough_attribute = ' data-import-walkthrough' if walkthrough else ''
    parts = ['<div class="visual-guide" data-visual-guide' + walkthrough_attribute + ' markdown="0">', '<p class="visual-lead">' + escape(guide["lead"]) + '</p>']
    step_items = []
    for index, step in enumerate(guide.get("steps", [])):
        if walkthrough:
            frame_id = "visual-" + topic["id"] + "-" + str(index)
            step_items.append('<li><a class="import-step" href="#' + frame_id + '" data-visual-select="' + str(index) + '"><span class="import-step__number" aria-hidden="true">' + str(index + 1).zfill(2) + '</span><span class="import-step__copy"><strong>' + escape(step["title"]) + '</strong><span class="import-step__description">' + escape(step["text"]) + '</span></span><svg class="import-step__arrow" width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true"><path d="M3 8h9M8 4l4 4-4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg></a></li>')
        else:
            step_items.append('<li><strong>' + escape(step["title"]) + '</strong><p>' + escape(step["text"]) + '</p></li>')
    steps = '<ol class="visual-steps">' + ''.join(step_items) + '</ol>' if step_items else ''
    if walkthrough:
        steps = '<nav class="import-walkthrough__steps" aria-label="Views"><p class="import-walkthrough__hint">Select a step to see it</p>' + steps + '</nav>'
    if guide.get("steps_first"):
        parts.append(steps)
    media = guide.get("media", [])
    if media:
        frames = []
        links = []
        for index, item in enumerate(media):
            frame_id = "visual-" + topic["id"] + "-" + str(index)
            if item.get("image"):
                visual = '<img src="' + asset_url("images/" + item["image"]) + '" alt="' + escape(item.get("alt", item["caption"]), quote=True) + '" loading="lazy" decoding="async">'
            else:
                visual = video(item)
            if walkthrough:
                visual = '<div class="import-stage__topline"><span>In Resonite</span><span aria-hidden="true">' + str(index + 1).zfill(2) + ' / ' + str(len(media)).zfill(2) + '</span></div><div class="import-stage__image">' + visual + '</div>'
                caption = '<strong>' + escape(item["title"]) + '</strong><span>' + escape(item["caption"]) + '</span>'
            else:
                caption = escape(item["caption"])
            frames.append('<figure id="' + frame_id + '" data-visual-frame>' + visual + '<figcaption>' + caption + '</figcaption></figure>')
            links.append('<a href="#' + frame_id + '" data-visual-select="' + str(index) + '">' + escape(item["title"]) + '</a>')
        paired = ' data-visual-paired' if not walkthrough and len(guide['media']) == 2 and all(item.get("image") for item in guide['media']) else ''
        controls = '<nav aria-label="Views">' + ''.join(links) + '</nav>' if len(media) > 1 and not walkthrough else ''
        stage_attributes = ' role="region" aria-label="Selected import step" aria-live="polite"' if walkthrough else ''
        parts.append('<div class="visual-media"' + paired + '>' + controls + '<div class="visual-stage"' + stage_attributes + '>' + ''.join(frames) + '</div></div>')
    if guide.get("diagram"):
        parts.append(render_diagram(guide["diagram"], asset_url))
    if not guide.get("steps_first"):
        parts.append(steps)
    if guide.get("pairs"):
        parts.append('<dl class="visual-facts">' + ''.join('<div class="visual-state--' + pair.get("state", "neutral") + '"><dt>' + escape(pair["label"]) + '</dt><dd>' + escape(pair["value"]) + '</dd></div>' for pair in guide["pairs"]) + '</dl>')
    if guide.get("notice"):
        parts.append('<p class="visual-notice">' + escape(guide["notice"]) + '</p>')
    if guide.get("demo"):
        demo = guide["demo"]
        parts.append('<details class="import-demo"><summary>' + escape(demo["title"]) + '<span>Demo video</span></summary><figure>' + video(demo) + '<figcaption>' + escape(demo["caption"]) + '</figcaption></figure></details>')
    actions = [action for action in guide.get("actions", []) if not is_section_link(action["target"])]
    if actions:
        parts.append('<nav class="visual-actions" aria-label="Next action">' + ''.join('<a href="' + target_url(action["target"]) + '">' + escape(action["label"]) + '</a>' for action in actions) + '</nav>')
    parts.append('</div>')
    capture_only = guide.get("capture") and not any(guide.get(key) for key in ["media", "diagram", "steps", "pairs"]) and not actions
    parts = [] if capture_only else ['\n'.join(parts).replace('><', '>\n<')]
    parts.append('<details class="visual-reference" id="reference-' + topic["id"] + '" markdown="1">\n<summary>' + escape(guide.get("reference", "Technical reference")) + '</summary>\n\n' + technical + '\n\n</details>')
    return '\n\n'.join(parts)
