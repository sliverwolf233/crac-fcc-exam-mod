# -*- coding: utf-8 -*-
"""FAA pool parser v2: clean separators, extract embedded figures, dedup."""
import sys, io, re, json, glob, collections
from docx import Document
from docx.document import Document as _Doc
from docx.oxml.ns import qn

BASE = r"D:\Codes\MockExamProgram_AmateurRadio\最新版本题库 截止2026-10-17\FAA"
REP = r"D:\Codes\MockExamProgram_AmateurRadio\_work\faa2_report.txt"
rep = open(REP, "w", encoding="utf-8")

QRX = re.compile(r"^([TEG]\d[A-Z]\d{2})\s*\(([A-D])\)\s*(?:\[([^]]*)\])?\s*$")
OPTRX = re.compile(r"^([A-D])\.\s*(.*)$")
FIGRX = re.compile(r"Figure\s+([TEG]-\d+)", re.I)

def clean_text(s):
    s = re.sub(r"~{2,}.*$", "", s)  # drop ~~ tails
    return s.strip()

def iter_block_items(doc):
    """Yield ('p', paragraph) or ('img', bytes, partname) in document order."""
    body = doc.element.body
    # map rId -> part
    for child in body.iterchildren():
        if child.tag == qn("w:p"):
            # does this paragraph contain a drawing?
            blips = child.findall(".//" + qn("a:blip"))
            if blips:
                for b in blips:
                    rid = b.get(qn("r:embed"))
                    if rid:
                        part = doc.part.related_parts[rid]
                        yield ("img", part.blob, part.partname)
            texts = child.findall(".//" + qn("w:t"))
            yield ("p", "".join(t.text or "" for t in texts))
        elif child.tag == qn("w:tbl"):
            yield ("p", "[TABLE]")

def parse_pool(path):
    doc = Document(path)
    items = list(iter_block_items(doc))
    qs, cur = [], None
    images = []          # (index_in_stream, bytes)
    img_stream = []      # ordered blobs
    seen_ids = set()
    dups, figbuf = [], {}
    qs, cur, seen_ids, dups = [], None, set(), []
    img_idx = 0
    last_texts = collections.deque(maxlen=3)
    for it in items:
        if it[0] == "img":
            img_stream.append({"idx": img_idx, "blob": it[1], "after": list(last_texts)})
            img_idx += 1
            continue
        t = clean_text(it[1])
        if not t or t == "[TABLE]":
            continue
        last_texts.append(t)
        mq = QRX.match(t)
        if mq:
            if cur: qs.append(cur)
            qid, ans, ref = mq.group(1), mq.group(2), mq.group(3) or ""
            if qid in seen_ids: dups.append(qid)
            seen_ids.add(qid)
            cur = {"id": qid, "ans": ans, "ref": ref, "group": qid[:3], "q": [], "opts": {}, "figs": []}
            continue
        if cur is None:
            continue
        mo = OPTRX.match(t)
        if mo:
            cur["opts"][mo.group(1)] = [mo.group(2)]
            continue
        keys = list(cur["opts"].keys())
        if keys:
            cur["opts"][keys[-1]].append(t)
        else:
            cur["q"].append(t)
    if cur: qs.append(cur)
    for q in qs:
        q["q"] = " ".join(q["q"]).strip()
        q["opts"] = {k: " ".join(v).strip() for k, v in q["opts"].items()}
        blob = q["q"] + " " + " ".join(q["opts"].values())
        q["figs"] = sorted(set(FIGRX.findall(blob)))
    return qs, img_stream, dups

TOTAL = {}
for path in sorted(glob.glob(BASE + "\\*\\*.docx")):
    cls = path.split("\\")[-2].replace("Class ", "")
    qs, imgs, dups = parse_pool(path)
    ok = [q for q in qs if q["q"] and len(q["opts"]) == 4 and all(q["opts"].get(k) for k in "ABCD")]
    bad = [q["id"] for q in qs if q not in ok]
    groups = collections.Counter(q["group"] for q in ok)
    figrefs = collections.Counter(f for q in ok for f in q["figs"])
    rep.write(f"===== {cls}: total={len(qs)} ok={len(ok)} dups={dups} bad={bad}\n")
    rep.write(f"groups={len(groups)} sizes {min(groups.values())}..{max(groups.values())}\n")
    rep.write(f"figure refs: {dict(figrefs)}\n")
    rep.write(f"embedded images: {len(imgs)}\n")
    for im in imgs:
        rep.write(f"  img after: {im['after']} blob={len(im['blob'])}B\n")
    # attach figure images to questions by label
    json.dump(ok, open(rf"D:\Codes\MockExamProgram_AmateurRadio\_work\faa_{cls}.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    json.dump([{"idx": i["idx"], "after": i["after"], "size": len(i["blob"])} for i in imgs],
              open(rf"D:\Codes\MockExamProgram_AmateurRadio\_work\faa_{cls}_imgs.json", "w", encoding="utf-8"), indent=1)
    # keep blobs for next step
    import pickle
    pickle.dump(imgs, open(rf"D:\Codes\MockExamProgram_AmateurRadio\_work\faa_{cls}_imgs.pkl", "wb"))
    TOTAL[cls] = len(ok)
rep.write(f"TOTALS: {TOTAL}\n")
rep.close()
print("done", TOTAL)
