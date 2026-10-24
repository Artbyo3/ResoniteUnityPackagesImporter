import html
import json
import os
import re
import shutil
import tomllib
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlsplit

from docs_navigation import strip_section_links
from support_map import render_support_map
from visual_guides import load_visual_guides, render_visual_guide

HERE = Path(__file__).resolve().parent
HEADING = re.compile(r"^(#{1,6}) (.+)$", re.MULTILINE)


def load_workspace(config_path=HERE / "workspace.toml"):
    config = tomllib.loads(config_path.read_text(encoding="utf-8"))
    groups = {group["id"]: group for group in config["groups"]}
    topics = {topic["id"]: topic for topic in config["topics"]}
    if len(groups) != len(config["groups"]) or len(topics) != len(config["topics"]):
        raise ValueError("Documentation group and topic IDs must be unique")
    for topic in topics.values():
        if topic["group"] not in groups:
            raise ValueError(f"Unknown documentation group: {topic['group']}")
    for group in groups.values():
        if group["default"] not in topics or topics[group["default"]]["group"] != group["id"]:
            raise ValueError(f"Invalid default topic: {group['id']}")
    return config


def source_path(root, value):
    path = (root / value).resolve()
    if not path.is_relative_to(root.resolve()) or path.suffix != ".md" or not path.is_file():
        raise ValueError(f"Invalid documentation source: {value}")
    return path


def read_section(root, specification):
    path = source_path(root, specification["file"])
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"\A---\r?\n.*?\r?\n---\r?\n", "", text, count=1, flags=re.DOTALL)
    section = specification.get("section")
    if section:
        matches = [heading for heading in HEADING.finditer(text) if heading[2] == section]
        if len(matches) != 1:
            raise ValueError(f"Expected one section '{section}' in {path.name}")
        start = matches[0]
        end = next((heading.start() for heading in HEADING.finditer(text, start.end())
                    if len(heading[1]) <= len(start[1])), len(text))
        text = text[start.end():end].strip()
        if specification.get("heading"):
            text = f"## {specification['heading']}\n\n{text}"
    else:
        text = re.sub(r"^# [^\n]*\n", "", text, count=1, flags=re.MULTILINE).strip()
    return text, path


def rebase_links(text, source, host):
    def destination(value):
        parsed = urlsplit(html.unescape(value))
        if parsed.scheme or parsed.netloc or value.startswith("/"):
            return value
        target = source.parent / parsed.path if parsed.path else source
        relative = Path(os.path.relpath(target, host.parent)).as_posix()
        if parsed.path.endswith("/"):
            relative += "/"
        return relative + ("?" + parsed.query if parsed.query else "") + ("#" + parsed.fragment if parsed.fragment else "")
    text = re.sub(r'(!?\[[^\]]*\]\()([^\s)]+)([^)]*\))',
                  lambda match: match[1] + destination(match[2]) + match[3], text)
    return re.sub(r'((?:href|src)=")([^"<>]+)(")',
                  lambda match: match[1] + html.escape(destination(match[2]), quote=True) + match[3], text)


def topic_content(root, topic, host, include_capabilities=True):
    blocks = []
    for specification in topic["sources"]:
        text, source = read_section(root, specification)
        text = strip_section_links(rebase_links(text, source, host))
        text = re.sub(r"\A\*\*(?:Overall status|Status): [^*]+\*\*\s*", "", text)
        blocks.append(text)
    body = "\n\n".join(blocks)
    if topic.get("capabilities") and include_capabilities:
        support = render_support_map(topic["capabilities"])
        if "<!-- component-support-map -->" in body:
            body = body.replace("<!-- component-support-map -->", support)
        else:
            body += "\n\n" + support
    return body


def render_answer(root, topic, host):
    identifier = html.escape(topic.get("anchor") or topic["id"], quote=True)
    status = topic.get("status")
    badge = f'<p class="docs-answer__status">{html.escape(status)}</p>' if status else ""
    guide = load_visual_guides().get(topic["id"])
    body = topic_content(root, topic, host, include_capabilities=not bool(guide))
    if guide:
        body = body.replace("<!-- component-support-map -->", "")
        body = render_visual_guide(root, topic, host, body, guide)
        badge = ""
        if topic.get("capabilities"):
            support = render_support_map(topic["capabilities"])
            if topic["capabilities"] in {"ma", "vrcfury"}:
                body = support + "\n\n" + body
            else:
                marker = '<details class="visual-reference"'
                body = body.replace(marker, support + "\n\n" + marker, 1)
    composed = bool(guide and guide.get("composition"))
    answer_class = "docs-answer docs-answer--composed" if composed else "docs-answer"
    heading = "" if composed else f'<h1 id="answer-title-{topic["id"]}" tabindex="-1">{html.escape(topic["title"])}</h1>'
    return (f'<section class="{answer_class}" data-doc-answer="{topic["id"]}" id="{identifier}" markdown="1">\n'
            f'{badge}\n{heading}\n\n{body}\n\n</section>')


def render_page(root, config, active, page_topics):
    host = source_path(root, active["page"])
    groups = {group["id"]: group for group in config["groups"]}
    group = groups[active["group"]]
    answers = "\n\n".join(render_answer(root, topic, host) for topic in page_topics)
    return f'''---
title: {json.dumps(active["title"])}
hide:
  - toc
---

<div class="docs-workspace" data-doc-workspace data-doc-initial="{active['id']}" markdown="1">

<div class="docs-workspace__main" markdown="1">
<div class="docs-find" data-doc-find hidden>
<label for="docs-find-input">Find an answer</label>
<div class="docs-find__field"><input id="docs-find-input" type="search" placeholder="Try missing textures, Poiyomi, installation..." autocomplete="off" aria-controls="docs-find-results"><kbd aria-hidden="true">Ctrl K</kbd></div>
<div class="docs-find__results" id="docs-find-results" aria-label="Search results" hidden></div>
<p class="docs-sr-only" data-doc-search-status role="status" aria-live="polite"></p>
</div>
<p class="docs-context" data-doc-context hidden>{html.escape(group['label'])}</p>
<div class="docs-reader" data-doc-reader markdown="1">
{answers}
</div>
<p class="docs-sr-only" data-doc-announcement role="status" aria-live="polite"></p>
</div>
</div>
'''


def prepare_workspace(source, destination, config=None):
    config = config or load_workspace()
    shutil.copytree(source, destination)
    pages = {}
    for topic in config["topics"]:
        source_path(source, topic["page"])
        pages.setdefault(topic["page"], []).append(topic)
    for name, page_topics in pages.items():
        rendered = render_page(source, config, page_topics[0], page_topics)
        (destination / name).write_text(rendered, encoding="utf-8")


class AnswerExtractor(HTMLParser):
    def __init__(self, text):
        super().__init__(convert_charrefs=False)
        self.text = text
        self.offsets = [0]
        for line in text.splitlines(keepends=True):
            self.offsets.append(self.offsets[-1] + len(line))
        self.answers = {}
        self.active = None
        self.depth = 0
        self.feed(text)
        if self.active:
            raise ValueError("Unclosed documentation answer")

    def position(self):
        line, column = self.getpos()
        return self.offsets[line - 1] + column

    def handle_starttag(self, tag, attributes):
        if tag != "section":
            return
        attributes = dict(attributes)
        if self.active:
            self.depth += 1
        elif "data-doc-answer" in attributes:
            self.active = (attributes["data-doc-answer"], self.position())
            self.depth = 1

    def handle_endtag(self, tag):
        if tag != "section" or not self.active:
            return
        self.depth -= 1
        if self.depth == 0:
            identifier, start = self.active
            if identifier in self.answers:
                raise ValueError(f"Duplicate documentation answer: {identifier}")
            self.answers[identifier] = self.text[start:self.position() + len("</section>")]
            self.active = None


def export_workspace(site, config=None):
    config = config or load_workspace()
    answers = {}
    for page in {topic["route"] for topic in config["topics"]}:
        path = site / page / "index.html"
        answers.update(AnswerExtractor(path.read_text(encoding="utf-8")).answers)
    topics = []
    for topic in config["topics"]:
        if topic["id"] not in answers:
            raise ValueError(f"Missing rendered answer: {topic['id']}")
        exported = {key: value for key, value in topic.items() if key != "sources"}
        body = answers[topic["id"]]
        if "<docs-inline" in body or re.search(r"<[^>]+\bmarkdown=", body):
            raise ValueError(f"Unrendered documentation answer: {topic['id']}")
        exported["html"] = body
        topics.append(exported)
    payload = {"groups": config["groups"], "topics": topics, "repository": config["repository"]}
    destination = site / "assets" / "docs-workspace.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
