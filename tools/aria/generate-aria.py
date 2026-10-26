#!/usr/bin/env python3
"""Generates the WAI-ARIA taxonomy and the HTML-AAM extracts used by RecursiveAI.OneHtml.Accessibility.

Inputs are the pinned specification documents (see pins.json and fetch-specs.py). Output is JSON under
src/OneHtml.Accessibility/Aria/Data/, embedded in the assembly.

usage: generate-aria.py SPEC_CACHE_DIR [OUT_DIR]
"""
import hashlib, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from htmltree import parse

here = os.path.dirname(os.path.abspath(__file__))
cache = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, "../../src/OneHtml.Accessibility/Aria/Data")
os.makedirs(out, exist_ok=True)
pins = json.load(open(os.path.join(here, "pins.json")))


def load(name):
    path = os.path.join(cache, pins["specs"][name]["file"])
    data = open(path, "rb").read()
    if hashlib.sha256(data).hexdigest() != pins["specs"][name]["sha256"]:
        sys.exit("pin mismatch for " + name)
    return parse(data.decode("utf-8"))


def squash(s):
    return re.sub(r"\s+", " ", s).strip()


def rows_of(section):
    rows = {}
    for tr in section.find_all("tr"):
        th, td = tr.find("th"), tr.find("td")
        if th and td:
            rows[squash(th.text())] = td
    return rows


def items(td):
    """List entries of a characteristics cell as (name, text) pairs; a name is the first code element."""
    res = []
    lis = list(td.find_all("li"))
    if lis:
        for li in lis:
            code = li.find("code")
            res.append((re.match(r"[\w-]+", squash(code.text() if code else li.text())).group(0), squash(li.text())))
    else:
        for a in td.find_all("a"):
            code = a.find("code")
            res.append((re.match(r"[\w-]+", squash(code.text() if code else a.text())).group(0), squash(a.text())))
    return res


def parse_roles(doc, module, only=None):
    roles = {}
    for sec in doc.find_all("section"):
        if "role" not in sec.classes():
            continue
        name = sec.get("id")
        if only is not None and name not in only:
            continue
        rows = rows_of(sec)
        r = {"name": name, "module": module}
        r["abstract"] = "Is Abstract:" in rows and squash(rows["Is Abstract:"].text()).lower() == "true"
        r["superclasses"] = [n for n, _ in items(rows["Superclass Role:"])] if "Superclass Role:" in rows else []
        r["requiredContext"] = [n for n, _ in items(rows["Required Context Role:"])] if "Required Context Role:" in rows else []
        owned = []
        if "Required Owned Elements:" in rows:
            for n, t in items(rows["Required Owned Elements:"]):
                owned.append([p.strip().strip("`") for p in t.split("→")])
        if name == "list" and ["listitem"] in owned and ["group", "listitem"] not in owned:
            # A list owns its items through a group (group > listitem): the shape WAI-ARIA 1.1 listed and ACT rule
            # bc4a75 still passes. The 1.2 role table names only listitem, so the chain is added here.
            owned.append(["group", "listitem"])
        r["requiredOwned"] = owned
        req, req_focus = [], []
        if "Required States and Properties:" in rows:
            conditional = "(if focusable)" in squash(rows["Required States and Properties:"].text())
            for n, t in items(rows["Required States and Properties:"]):
                (req_focus if conditional else req).append(n)
        r["required"] = req
        r["requiredWhenFocusable"] = req_focus
        supported, deprecated = [], []
        for key in ("Required States and Properties:", "Supported States and Properties:", "Inherited States and Properties:"):
            if key in rows:
                for n, t in items(rows[key]):
                    if n not in supported:
                        supported.append(n)
                    if "deprecated" in t and key.startswith("Inherited") and n not in deprecated:
                        deprecated.append(n)
        r["supported"] = supported
        r["deprecated"] = deprecated
        r["prohibited"] = [n for n, _ in items(rows["Prohibited States and Properties:"])] if "Prohibited States and Properties:" in rows else []
        r["nameFrom"] = [w for w in squash(rows["Name From:"].text()).split() if w in ("author", "contents", "prohibited")] if "Name From:" in rows else []
        nr = rows.get("Accessible Name Required:")
        r["nameRequired"] = bool(nr is not None and squash(nr.text()).lower() == "true")
        cp = rows.get("Children Presentational:")
        r["childrenPresentational"] = bool(cp is not None and squash(cp.text()).lower() == "true")
        implicit = {}
        if "Implicit Value for Role:" in rows:
            for m in re.finditer(r"Default for (aria-[a-z]+) is ([a-z0-9]+)", squash(rows["Implicit Value for Role:"].text())):
                implicit[m.group(1)] = m.group(2)
        r["implicit"] = implicit
        roles[name] = r
    return roles


TYPES = {
    "true/false": "boolean", "true/false/undefined": "booleanUndefined", "tristate": "tristate",
    "ID reference": "idref", "ID reference list": "idrefList", "integer": "integer", "number": "number",
    "string": "string", "token": "token", "token list": "tokenList",
}


def parse_props(doc, module, only=None):
    props = {}
    for sec in doc.find_all("section"):
        cl = sec.classes()
        if "property" not in cl and "state" not in cl:
            continue
        name = sec.get("id")
        if only is not None and name not in only:
            continue
        rows = rows_of(sec)
        used = squash(rows["Used in Roles:"].text())
        glob = used.startswith("All elements of the base markup")
        except_roles = []
        if glob and "except for the following roles:" in used:
            except_roles = [x.strip() for x in used.split("except for the following roles:")[1].split(",")]
        used_roles = [] if glob else [n for n, _ in items(rows["Used in Roles:"])]
        inherits = [n for n, _ in items(rows["Inherits into Roles:"])] if "Inherits into Roles:" in rows else []
        vtype = TYPES[squash(rows["Value:"].text())]
        tokens, default = [], None
        if vtype in ("token", "tokenList"):
            for tr in sec.find_all("tr"):
                th = tr.find("th")
                if th and tr.find("td") and "code" not in [c.tag for c in th.children if hasattr(c, "tag")]:
                    t = squash(th.text()).rstrip(":")
                    if (t + ":") in rows or t in ("Description", "Value", "Characteristic"):
                        continue
                    m = re.match(r"^(\S+?)(?: \(default\))?$", t)
                    if t.endswith("(default)"):
                        default = t[: -len("(default)")].strip()
                        tokens.append(default)
                    else:
                        tokens.append(t)
        else:
            for tr in sec.find_all("tr"):
                th = tr.find("th")
                if th and tr.find("td"):
                    t = squash(th.text()).rstrip(":")
                    if t.endswith("(default)"):
                        default = t[: -len("(default)")].strip()
        if vtype == "tristate":
            tokens = ["true", "false", "mixed", "undefined"]
        elif vtype in ("boolean", "booleanUndefined"):
            tokens = ["true", "false"] + (["undefined"] if vtype == "booleanUndefined" else [])
        props[name] = {
            "name": name, "module": module, "isState": "state" in cl, "type": vtype, "tokens": tokens, "default": default,
            "global": glob, "globalExceptRoles": except_roles, "usedInRoles": used_roles, "inheritsIntoRoles": inherits,
            "deprecated": "deprecated" in cl,
        }
    return props


aria12 = load("aria-1.2")
dpub = load("dpub-aria")
gfx = load("graphics-aria")
ed = load("aria-1.3-ed")

roles = parse_roles(aria12, "aria-1.2")
roles.update(parse_roles(dpub, "dpub-aria-1.1"))
roles.update(parse_roles(gfx, "graphics-aria-1.0"))
props = parse_props(aria12, "aria-1.2")

# WAI-ARIA 1.3 editor's draft additions. Chrome already exposes these; see docs/accessibility-roles.md.
ED_ROLES = ["comment", "mark", "suggestion"]
ED_PROPS = ["aria-braillelabel", "aria-brailleroledescription", "aria-description", "aria-colindextext", "aria-rowindextext"]
ed_roles_all = parse_roles(ed, "aria-1.3-ed")
for n in ED_ROLES:
    roles[n] = ed_roles_all[n]
for n, p in parse_props(ed, "aria-1.3-ed", set(ED_PROPS)).items():
    props[n] = p
# Where 1.2 gives no implicit value for a role and the draft does (for example heading's aria-level), take the draft's.
implicit_from_draft = []
for n, r in roles.items():
    if r["module"] == "aria-1.2" and n in ed_roles_all:
        for k, v in ed_roles_all[n]["implicit"].items():
            if k not in r["implicit"]:
                r["implicit"][k] = v
                implicit_from_draft.append(n + ":" + k)

# Draft properties join the supported lists: global ones everywhere except where a role is prohibited from naming itself
# is not relevant to them; role-specific ones on the roles that use or inherit them.
for pn in ED_PROPS:
    p = props[pn]
    targets = None if p["global"] else set(p["usedInRoles"]) | set(p["inheritsIntoRoles"])
    for r in roles.values():
        if r["module"] == "aria-1.3-ed" or pn in r["supported"]:
            continue
        if p["global"] or r["name"] in targets:
            r["supported"].append(pn)
        # Subclasses of a role that uses the property inherit it.
for r in roles.values():
    # A role inherits the supported properties of its superclasses even where the spec's own lists omit them.
    pass

# Core-AAM / ARIA synonyms handled in code: presentation/none, img/image, directory/list.
for r in roles.values():
    r["supported"].sort()
    r["deprecated"].sort()

order = sorted(roles.values(), key=lambda r: (r["module"], r["name"]))
sources = {k: {kk: vv for kk, vv in v.items() if kk != "file"} for k, v in pins["specs"].items()}
json.dump({"version": 1, "retrieved": pins["retrieved"], "sources": {k: sources[k] for k in ("aria-1.2", "dpub-aria", "graphics-aria", "aria-1.3-ed")},
           "implicitFromDraft": implicit_from_draft, "roles": order}, open(os.path.join(out, "aria-roles.json"), "w"), indent=1)
json.dump({"version": 1, "retrieved": pins["retrieved"], "properties": sorted(props.values(), key=lambda p: p["name"])},
          open(os.path.join(out, "aria-properties.json"), "w"), indent=1)

# ---- HTML-AAM extract ----
aam = load("html-aam")


def table_after(h):
    w = h.parent
    sib = w.parent.children
    for x in sib[sib.index(w) + 1: sib.index(w) + 8]:
        if hasattr(x, "tag") and x.tag == "table":
            return x
    return None


def extract(prefix):
    res = []
    for h in aam.find_all("h4"):
        i = h.get("id", "")
        if not i.startswith(prefix):
            continue
        t = table_after(h)
        rows = {}
        if t is None:
            print("no table after", i)
        for tr in (t.find_all("tr") if t is not None else []):
            c = [squash(x.text()) for x in tr.children if hasattr(x, "tag") and x.tag in ("th", "td")]
            if len(c) == 2:
                rows[c[0]] = c[1]
        ariakey = next((k for k in rows if k.lower().startswith("[wai-aria")), None)
        res.append({
            "id": i[len(prefix):],
            "title": re.sub(r"^\d+(\.\d+)* ", "", squash(h.text())),
            "html": rows.get("HTML Specification", ""),
            "elements": rows.get("Element(s)", ""),
            "aria": rows.get(ariakey, "") if ariakey else "",
            "computedRole": rows.get("Computed Role", ""),
            "comments": rows.get("Comments", ""),
        })
    return res


elements, attrs = extract("el-"), extract("att-")
json.dump({"version": 1, "source": sources["html-aam"], "elements": elements}, open(os.path.join(out, "html-aam-elements.json"), "w"), indent=1)
json.dump({"version": 1, "source": sources["html-aam"], "attributes": attrs}, open(os.path.join(out, "html-aam-attributes.json"), "w"), indent=1)

by_module = {}
for r in roles.values():
    by_module[r["module"]] = by_module.get(r["module"], 0) + 1
print("roles", len(roles), by_module, "abstract", sum(r["abstract"] for r in roles.values()))
print("properties", len(props), "states", sum(p["isState"] for p in props.values()), "global", sum(p["global"] for p in props.values()))
print("html-aam elements", len(elements), "attributes", len(attrs), "implicitFromDraft", implicit_from_draft)
