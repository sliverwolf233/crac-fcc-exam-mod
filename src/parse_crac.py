# -*- coding: utf-8 -*-
"""Parse CRAC 2025 question-bank PDF text into structured JSON."""
import sys, re, json, glob, collections, io

BASE = r"D:\Codes\MockExamProgram_AmateurRadio\_work"
TAG = re.compile(r"^\[([JPITABCDQ])\]\s*(.*)$")

def clean(s: str) -> str:
    s = s.replace("\x0c", "")
    return s.strip()

def parse(path):
    lines = [clean(l) for l in open(path, encoding="utf-8").read().splitlines()]
    qs, cur, field = [], None, None
    def flush_field():
        if cur is not None and field and field in ("q", "A", "B", "C", "D"):
            txt = " ".join(x for x in cur.setdefault(field, []) if x)
            cur[field] = txt
    for ln in lines:
        if not ln:
            continue
        # drop bare page numbers / artifacts
        if re.fullmatch(r"\d{1,3}", ln):
            continue
        m = TAG.match(ln)
        if m:
            tag, rest = m.group(1), m.group(2).strip()
            if tag == "J":
                flush_field()
                if cur: qs.append(cur)
                cur = {"j": rest, "opts": {}}
                field = None
            elif tag == "I":
                cur["i"] = rest; field = None
            elif tag == "P":
                cur["p"] = rest; field = None
            elif tag == "T":
                cur["t"] = rest; field = None
            elif tag == "Q":
                cur["q"] = [rest] if rest else []; field = "q"
            elif tag in "ABCD":
                cur[tag] = [rest] if rest else []
                field = tag
        else:
            if field:
                cur[field].append(ln)
            # stray text outside fields ignored
    flush_field()
    if cur: qs.append(cur)
    # normalize: move q list -> str, opts lists -> str
    out = []
    for q in qs:
        opts = {k: " ".join(q[k]) for k in "ABCD" if k in q}
        rec = {
            "j": q.get("j", ""), "i": q.get("i", ""), "p": q.get("p", ""),
            "q": q.get("q", "") if isinstance(q.get("q"), str) else " ".join(q.get("q", [])),
            "t": q.get("t", ""), "opts": opts,
        }
        out.append(rec)
    return out

def stats(name, qs):
    n = len(qs)
    multi = sum(1 for q in qs if len(q["t"]) > 1)
    bad = [q for q in qs if not (q["q"] and q["t"] and len(q["opts"]) == 4 and q["p"] and q["i"])]
    dupi = [k for k, v in collections.Counter(q["i"] for q in qs).items() if v > 1]
    rep = open(BASE + "\\parse_report.txt", "a", encoding="utf-8")
    rep.write(f"===== {name}: {n} questions, multi={multi} ({multi*100//n}%), incomplete={len(bad)}, dup-i={len(dupi)}\n")
    for b in bad[:6]: rep.write("  BAD: " + json.dumps(b, ensure_ascii=False)[:300] + "\n")
    for d in dupi[:6]: rep.write(f"  DUP {d}\n")
    rep.close()
    return n, multi, bad, dupi

if __name__ == "__main__":
    open(BASE + "\\parse_report.txt", "w").write("")
    allq = {}
    for path in sorted(glob.glob(BASE + "\\*题库.pdf.txt")):
        name = path.split("\\")[-1].replace(".pdf.txt", "")
        if "总题库" in name: continue
        qs = parse(path)
        stats(name, qs)
        allq[name] = qs
        json.dump(qs, open(BASE + "\\parsed_" + name + ".json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    # cross-check: A subset of B subset of C (by i-code)
    a, b, c = allq["A类题库"], allq["B类题库"], allq["C类题库"]
    ia, ib, ic = set(x["i"] for x in a), set(x["i"] for x in b), set(x["i"] for x in c)
    rep = open(BASE + "\\parse_report.txt", "a", encoding="utf-8")
    rep.write(f"A\u2282B: {ia <= ib} (B-A={len(ib-ia)})  B\u2282C: {ib <= ic} (C-B={len(ic-ib)})\n")
    # same i-code, different content between classes?
    for xs, ys, nm in [(a, b, "A/B"), (b, c, "B/C")]:
        ymap = {x["i"]: x for x in ys}
        diff = sum(1 for x in xs if x["i"] in ymap and (ymap[x["i"]]["q"], ymap[x["i"]]["t"]) != (x["q"], x["t"]))
        rep.write(f"{nm} content mismatches on shared codes: {diff}\n")
    rep.close()
    print("done")
