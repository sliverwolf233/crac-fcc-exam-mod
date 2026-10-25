# -*- coding: utf-8 -*-
"""Extract the 10 figure blobs from the Class E docx (images live in tables,
so v2 parser missed them). Same pkl structure as T/G: list of {idx, blob, after}."""
import os, io, pickle, sys
sys.path.insert(0, r"D:\Codes\MockExamProgram_AmateurRadio\_work\pylibs")
from docx import Document
from docx.oxml.ns import qn
from PIL import Image

SRC = r"D:\Codes\MockExamProgram_AmateurRadio\最新版本题库 截止2026-10-17\FAA\Class E\2024-2028 Extra Class Question Pool and Syllabus Public Release with 4th Errata Feb 4 2026.docx"
OUT = r"D:\Codes\MockExamProgram_AmateurRadio\_work\faa_E_imgs.pkl"
REP = r"D:\Codes\MockExamProgram_AmateurRadio\_work\extract_e_report.txt"

rep = open(REP, "w", encoding="utf-8")
doc = Document(SRC)
imgs = []
for blip in doc.element.body.iter(qn('a:blip')):
    rid = blip.get(qn('r:embed'))
    if not rid:
        continue
    part = doc.part.related_parts[rid]
    imgs.append({"idx": len(imgs), "blob": part.blob, "after": []})
    rep.write(f"[{len(imgs)-1}] rid={rid} raw_size={len(part.blob)}\n")

rep.write(f"total blips found: {len(imgs)}\n")
assert len(imgs) == 10, f"expected 10 images, got {len(imgs)}"

# sanity: every blob opens with PIL; report dims (build's Image_ensure does <=700 JPEG)
for im in imgs:
    img = Image.open(io.BytesIO(im["blob"]))
    img.verify()
    img = Image.open(io.BytesIO(im["blob"]))
    fmt = img.format
    w, h = img.size
    conv_w = min(w, 700)
    rep.write(f"  idx={im['idx']} fmt={fmt} {w}x{h} -> JPEG width {conv_w}\n")

pickle.dump(imgs, open(OUT, "wb"))
rep.write(f"saved {OUT} ({os.path.getsize(OUT)} bytes)\n")
rep.close()
print("ok")
