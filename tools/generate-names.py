#!/usr/bin/env python3
"""Generates the table of well-known names (src/OneHtml/Dom/Generated/KnownNames.g.cs).

The tree builder compares element and attribute names as small integers. The names the HTML parsing algorithm
mentions (WHATWG HTML 13.2.6) get fixed ids here, in the order of the lists below; every other name gets an id when
a document first uses it. The lists are the ones in the specification: HTML elements named by the tree construction
rules, the SVG tag and attribute names whose case the parser repairs (13.2.6.1 and 13.2.6.5), and a few attributes.

Usage: tools/generate-names.py            (rewrites the output; running it twice changes nothing)
"""
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

HTML = """a abbr address applet area article aside b base basefont bgsound big blockquote body br button caption center
code col colgroup dd details dialog dir div dl dt em embed fieldset figcaption figure font footer form frame frameset
h1 h2 h3 h4 h5 h6 head header hgroup hr html i iframe image img input keygen li link listing main marquee menu meta
nav nobr noembed noframes noscript object ol optgroup option p param plaintext pre rb rp rt rtc ruby s script search
section select small source span strike strong style sub summary sup table tbody td template textarea tfoot th thead
title tr track tt u ul var wbr xmp math svg
mi mo mn ms mtext annotation-xml mglyph malignmark foreignobject desc""".split()

# Elements whose spelling the parser repairs inside SVG (lowercase form, repaired form).
SVG_TAGS = """altglyph altGlyph|altglyphdef altGlyphDef|altglyphitem altGlyphItem|animatecolor animateColor|
animatemotion animateMotion|animatetransform animateTransform|clippath clipPath|feblend feBlend|
fecolormatrix feColorMatrix|fecomponenttransfer feComponentTransfer|fecomposite feComposite|
feconvolvematrix feConvolveMatrix|fediffuselighting feDiffuseLighting|fedisplacementmap feDisplacementMap|
fedistantlight feDistantLight|fedropshadow feDropShadow|feflood feFlood|fefunca feFuncA|fefuncb feFuncB|
fefuncg feFuncG|fefuncr feFuncR|fegaussianblur feGaussianBlur|feimage feImage|femerge feMerge|
femergenode feMergeNode|femorphology feMorphology|feoffset feOffset|fepointlight fePointLight|
fespecularlighting feSpecularLighting|fespotlight feSpotLight|fetile feTile|feturbulence feTurbulence|
foreignobject foreignObject|glyphref glyphRef|lineargradient linearGradient|radialgradient radialGradient|
textpath textPath"""

SVG_ATTRS = """attributename attributeName|attributetype attributeType|basefrequency baseFrequency|
baseprofile baseProfile|calcmode calcMode|clippathunits clipPathUnits|diffuseconstant diffuseConstant|
edgemode edgeMode|filterunits filterUnits|glyphref glyphRef|gradienttransform gradientTransform|
gradientunits gradientUnits|kernelmatrix kernelMatrix|kernelunitlength kernelUnitLength|keypoints keyPoints|
keysplines keySplines|keytimes keyTimes|lengthadjust lengthAdjust|limitingconeangle limitingConeAngle|
markerheight markerHeight|markerunits markerUnits|markerwidth markerWidth|maskcontentunits maskContentUnits|
maskunits maskUnits|numoctaves numOctaves|pathlength pathLength|patterncontentunits patternContentUnits|
patterntransform patternTransform|patternunits patternUnits|pointsatx pointsAtX|pointsaty pointsAtY|
pointsatz pointsAtZ|preservealpha preserveAlpha|preserveaspectratio preserveAspectRatio|
primitiveunits primitiveUnits|refx refX|refy refY|repeatcount repeatCount|repeatdur repeatDur|
requiredextensions requiredExtensions|requiredfeatures requiredFeatures|specularconstant specularConstant|
specularexponent specularExponent|spreadmethod spreadMethod|startoffset startOffset|stddeviation stdDeviation|
stitchtiles stitchTiles|surfacescale surfaceScale|systemlanguage systemLanguage|tablevalues tableValues|
targetx targetX|targety targetY|textlength textLength|viewbox viewBox|viewtarget viewTarget|
xchannelselector xChannelSelector|ychannelselector yChannelSelector|zoomandpan zoomAndPan"""

MATHML_ATTRS = [("definitionurl", "definitionURL")]

# Attribute names the parser looks at or rewrites, and a few every consumer wants.
ATTRS = """charset http-equiv content color face size encoding type id class href src name style lang
xlink:actuate xlink:arcrole xlink:href xlink:role xlink:show xlink:title xlink:type xml:lang xml:space
xmlns xmlns:xlink actuate arcrole href role show title space xlink xml selectedcontent selected multiple disabled datalist""".split()


def pairs(text):
    out = []
    for item in re.sub(r"\s+", " ", text).split("|"):
        a, b = item.strip().split(" ")
        out.append((a, b))
    return out


names = []
seen = set()


def add(n):
    if n not in seen:
        seen.add(n)
        names.append(n)


for n in HTML:
    add(n)
for lo, camel in pairs(SVG_TAGS) + pairs(SVG_ATTRS) + MATHML_ATTRS:
    add(lo)
    add(camel)
for n in ATTRS:
    add(n)


def ident(n):
    parts = re.split(r"[-:]", n)
    s = "".join(p[:1].upper() + p[1:] for p in parts)
    if any(c.isupper() for c in n):
        s = "Cc" + s
    return s


idents = {}
for n in names:
    i = ident(n)
    assert i not in idents.values(), (n, i)
    idents[n] = i

out = []
out.append("// <auto-generated> by tools/generate-names.py; do not edit by hand. </auto-generated>")
out.append("namespace RecursiveAI.OneHtml.Dom;")
out.append("")
out.append("/// <summary>Ids of the names the tree builder compares by number (see tools/generate-names.py).</summary>")
out.append("internal static class N")
out.append("{")
for i, n in enumerate(names):
    out.append(f"    public const int {idents[n]} = {i};")
out.append(f"    public const int Count = {len(names)};")
out.append("")
out.append("    public static readonly string[] Names =")
out.append("    [")
for n in names:
    out.append(f'        "{n}",')
out.append("    ];")
out.append("")
out.append("    /// <summary>Pairs of (lowercase, repaired) ids for SVG element names (13.2.6.5).</summary>")
out.append("    public static readonly int[] SvgTagFixes =")
out.append("    [")
for lo, camel in pairs(SVG_TAGS):
    out.append(f"        {idents[lo]}, {idents[camel]},")
out.append("    ];")
out.append("")
out.append("    /// <summary>Pairs of (lowercase, repaired) ids for SVG attribute names (13.2.6.1).</summary>")
out.append("    public static readonly int[] SvgAttributeFixes =")
out.append("    [")
for lo, camel in pairs(SVG_ATTRS):
    out.append(f"        {idents[lo]}, {idents[camel]},")
out.append("    ];")
out.append("}")
out.append("")

path = os.path.join(ROOT, "src/OneHtml/Dom/Generated/KnownNames.g.cs")
os.makedirs(os.path.dirname(path), exist_ok=True)
with open(path, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(out))
print(len(names), "names")
