import re
from html.parser import HTMLParser
from pathlib import PurePosixPath
from urllib.parse import unquote, urlsplit


def is_section_link(value):
    parsed = urlsplit(value)
    if parsed.scheme or parsed.netloc or not parsed.path:
        return False
    return PurePosixPath(unquote(parsed.path)).suffix.lower() in {"", ".md", ".html"}


class SectionLinkRemover(HTMLParser):
    def __init__(self, text):
        super().__init__(convert_charrefs=False)
        self.text = text
        self.offsets = [0]
        for line in text.splitlines(keepends=True):
            self.offsets.append(self.offsets[-1] + len(line))
        self.anchors = []
        self.edits = []
        self.feed(text)

    def position(self):
        line, column = self.getpos()
        return self.offsets[line - 1] + column

    def handle_starttag(self, tag, attributes):
        if tag == "a":
            remove = is_section_link(dict(attributes).get("href", ""))
            self.anchors.append(remove)
            if remove:
                start = self.position()
                self.edits.append((start, start + len(self.get_starttag_text())))

    def handle_endtag(self, tag):
        if tag == "a" and self.anchors and self.anchors.pop():
            start = self.position()
            self.edits.append((start, self.text.index(">", start) + 1))

    def render(self):
        text = self.text
        for start, end in sorted(self.edits, reverse=True):
            text = text[:start] + text[end:]
        return text


def strip_section_links(text):
    blocks = re.split(r"(```[^\n]*\n.*?```|~~~[^\n]*\n.*?~~~)", text, flags=re.DOTALL)
    for index in range(0, len(blocks), 2):
        blocks[index] = re.sub(
            r'(?<!!)\[([^\]\n]+)\]\(([^\s)]+)(?:\s+"[^"\n]*")?\)(?:\{[^}\n]*\})?',
            lambda match: match[1] if is_section_link(match[2]) else match[0],
            blocks[index],
        )
        blocks[index] = SectionLinkRemover(blocks[index]).render()
    return "".join(blocks)
