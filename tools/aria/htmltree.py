"""Minimal HTML tree built on the standard library parser, enough to read W3C ReSpec documents."""
from html.parser import HTMLParser

VOID = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"}


class El:
    __slots__ = ("tag", "attrs", "children", "parent")

    def __init__(self, tag, attrs, parent):
        self.tag = tag
        self.attrs = dict(attrs)
        self.children = []
        self.parent = parent

    def get(self, name, default=""):
        return self.attrs.get(name, default)

    def classes(self):
        return self.get("class").split()

    def text(self):
        out = []
        stack = [self]
        while stack:
            n = stack.pop()
            if isinstance(n, str):
                out.append(n)
            else:
                stack.extend(reversed(n.children))
        return "".join(out)

    def walk(self):
        stack = [self]
        while stack:
            n = stack.pop()
            if isinstance(n, El):
                yield n
                stack.extend(reversed(n.children))

    def find_all(self, tag=None, cls=None):
        for n in self.walk():
            if n is self:
                continue
            if tag and n.tag != tag:
                continue
            if cls and cls not in n.classes():
                continue
            yield n

    def find(self, tag=None, cls=None):
        return next(self.find_all(tag, cls), None)


class _Builder(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = El("#root", [], None)
        self.cur = self.root

    def handle_starttag(self, tag, attrs):
        e = El(tag, [(k, v or "") for k, v in attrs], self.cur)
        self.cur.children.append(e)
        if tag not in VOID:
            self.cur = e

    def handle_startendtag(self, tag, attrs):
        self.cur.children.append(El(tag, [(k, v or "") for k, v in attrs], self.cur))

    def handle_endtag(self, tag):
        n = self.cur
        while n is not None and n.tag != tag:
            n = n.parent
        if n is not None and n.parent is not None:
            self.cur = n.parent

    def handle_data(self, data):
        self.cur.children.append(data)


def parse(text):
    b = _Builder()
    b.feed(text)
    return b.root
