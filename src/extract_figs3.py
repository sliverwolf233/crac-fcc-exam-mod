# -*- coding: utf-8 -*-
"""Flow-row figure extraction: pair each image row with the NEXT label row in reading order."""
import sys, io, re, os, json
sys.path.insert(0, r"D:\Codes\MockExamProgram_AmateurRadio\_work\pylibs")
from pdfminer.high_level import extract_pages
from pdfminer.layout import LTTextLine, LTTextContainer, LTImage, LTFigure, LTChar
from pypdf import PdfReader
from PIL import Image

FIG_PDF = r"D:\Codes\MockExamProgram_AmateurRadio\最新版本题库 截止2026-10-17\CRAC\20250728134354_总题库附图.pdf"
OUTDIR = r"D:\Codes\MockExamProgram_AmateurRadio\_work\figs"
os.makedirs(OUTDIR, exist_ok=True)

# collect rows
rows = []  # (flowkey, kind, payload) flowkey=(page, -y1)
for pno, page_layout in enumerate(extract_pages(FIG_PDF)):
    for el in page_layout:
        if isinstance(el, LTTextContainer):
            for line in el:
                if isinstance(line, LTTextLine):
                    toks = []
                    for obj in line:
                        if isinstance(obj, LTChar) and re.fullmatch(r"[A-Z0-9]", obj.get_text() or ""):
                            toks.append(obj)
                    # group consecutive alnum chars into tokens
                    groups, cur = [], []
                    for ch in toks:
                        if cur and abs(ch.x0 - cur[-1].x1) < 20:
                            cur.append(ch)
                        else:
                            if cur: groups.append(cur)
                            cur = [ch]
                    if cur: groups.append(cur)
                    tokspec = []
                    for g in groups:
                        s = "".join(c.get_text() for c in g)
                        if re.fullmatch(r"LK\d{3,4}", s):
                            tokspec.append({"tok": s, "cx": (g[0].x0 + g[-1].x1) / 2})
                    if tokspec:
                        rows.append(((pno, -line.y1), "L", sorted(tokspec, key=lambda t: t["cx"])))
        elif isinstance(el, LTFigure):
            for sub in el:
                if isinstance(sub, LTImage):
                    rows.append(((pno, -sub.bbox[3]), "I", {"cx": (sub.bbox[0] + sub.bbox[2]) / 2, "name": sub.name}))

rows.sort(key=lambda r: r[0])
# pair: each image -> next label row in flow
pairs = {}
pending = []
for key, kind, payload in rows:
    if kind == "I":
        pending.append(payload)
    else:
        labs = payload
        if len(pending) == len(labs):
            for im, la in zip(sorted(pending, key=lambda p: p["cx"]), labs):
                pairs[im["name"]] = la["tok"]
        elif pending:
            # count mismatch: pair by nearest cx
            for im in pending:
                best = min(labs, key=lambda l: abs(l["cx"] - im["cx"]))
                pairs[im["name"]] = best["tok"]
        pending = []
if pending:
    print("WARNING: trailing images without labels:", [p["name"] for p in pending])

# fix LK060 typo: infer LK0603
names = sorted(pairs.items())
prev = None
fixed = []
for name, tok in names:
    if re.fullmatch(r"LK\d{3}", tok):
        # 3-digit -> likely missing trailing digit; keep, resolve later
        pass
    fixed.append((name, tok))

# extract bytes
reader = PdfReader(FIG_PDF)
def xobj_data(page, name):
    xo = page["/Resources"]["/XObject"]
    obj = xo.get("/" + name)
    if obj is None:
        for k in xo.keys():
            if k.lstrip("/").lower() == name.lower(): obj = xo[k]; break
    if obj is None: return None
    return obj.get_data()

# map pdfminer image name -> page
img_page = {}
for pno, page_layout in enumerate(extract_pages(FIG_PDF)):
    for el in page_layout:
        if isinstance(el, LTFigure):
            for sub in el:
                if isinstance(sub, LTImage):
                    img_page[sub.name] = pno

report = []
for name, tok in sorted(pairs.items()):
    try:
        data = xobj_data(reader.pages[img_page[name]], name)
        img = Image.open(io.BytesIO(data)).convert("RGB")
        if img.width > 700:
            img = img.resize((700, int(img.height * 700 / img.width)), Image.LANCZOS)
        buf = io.BytesIO()
        img.save(buf, "JPEG", quality=82)
        open(os.path.join(OUTDIR, tok + ".jpg"), "wb").write(buf.getvalue())
        report.append(f"{name} -> {tok} OK {img.width}x{img.height}")
    except Exception as e:
        report.append(f"{name} -> {tok} ERR {type(e).__name__} {e}")

# handle duplicates on disk (same tok twice = collision)
import collections
cnt = collections.Counter(t for _, t in pairs.items())
dups = {t: c for t, c in cnt.items() if c > 1}
with open(os.path.join(OUTDIR, "extract3_report.txt"), "w", encoding="utf-8") as f:
    f.write("\n".join(report) + "\n\ndups: " + json.dumps(dups) + f"\ncount={len(pairs)} distinct={len(cnt)}\n")
print("pairs:", len(pairs), "dups:", dups)
