# -*- coding: utf-8 -*-
"""Merge translation batches into a bilingual FAA bank (FAA.sqlite3 in-place rebuild)."""
import json, glob, sqlite3, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

TRANS = {}
for f in glob.glob(r"D:\Codes\MockExamProgram_AmateurRadio\_work\trans_T_*.json"):
    for line in open(f, encoding="utf-8"):
        line = line.strip()
        if not line: continue
        d = json.loads(line)
        TRANS[d["i"]] = d
print("translations loaded:", len(TRANS))

src = r"D:\Codes\MockExamProgram_AmateurRadio\banks\FAA.sqlite3"
con = sqlite3.connect(src)
cur = con.cursor()
rows = cur.execute("SELECT ID, QuestionCode, Question, Option1, Option2, Option3, Option4 FROM Question").fetchall()
bilingual = 0
for qid, code, q, o1, o2, o3, o4 in rows:
    t = TRANS.get(code)
    if not t: continue
    MARK = chr(12304) + u"中" + chr(12305)  # 【中】
    if MARK in q: continue  # idempotent guard
    newq = q + chr(10) + MARK + t["q"]
    opts = [o1, o2, o3, o4]
    newopts = []
    for i, o in enumerate(opts):
        zhs = t["o"][i] if i < len(t["o"]) else ""
        if zhs:
            newopts.append(o + chr(10) + zhs)
        else:
            newopts.append(o)
    cur.execute("UPDATE Question SET Question=?, Option1=?, Option2=?, Option3=?, Option4=? WHERE ID=?",
                (newq, newopts[0], newopts[1], newopts[2], newopts[3], qid))
    bilingual += 1
con.commit()
print("bilingual questions:", bilingual, "of", len(rows))
con.close()
