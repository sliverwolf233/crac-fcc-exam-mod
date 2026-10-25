# -*- coding: utf-8 -*-
"""Build CRAC-2025 and FAA SQLite banks compatible with Simulation.exe."""
import sqlite3, json, re, os, io, shutil, base64, glob, collections, itertools, sys

WORK = r"D:\Codes\MockExamProgram_AmateurRadio\_work"
ORIG = r"D:\Codes\MockExamProgram_AmateurRadio\_extracted\SimulationSys.sqlite3"
OUTDIR = r"D:\Codes\MockExamProgram_AmateurRadio\banks"
os.makedirs(OUTDIR, exist_ok=True)
FIGS = os.path.join(WORK, "figs")
LOG = open(os.path.join(WORK, "build_log.txt"), "w", encoding="utf-8")
def log(*a):
    s = " ".join(str(x) for x in a)
    LOG.write(s + "\n"); print(s)

CJK = r"[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]"
def norm_ws(s):
    """Remove wrap-induced spaces between CJK chars; keep others."""
    prev = None
    out = []
    for ch in s:
        out.append(ch)
    s = "".join(out)
    s = re.sub(r"(?<=%s) (?=%s)" % (CJK, CJK), "", s)
    s = re.sub(r"(?<=%s) (?=[，。；：、？！）》】”])" % CJK, "", s)
    s = re.sub(r"(?<=[（《【“]) (?=%s)" % CJK, "", s)
    return s.strip()

def fig_b64(code):
    p = os.path.join(FIGS, code + ".jpg")
    if os.path.exists(p):
        return base64.b64encode(open(p, "rb").read()).decode("ascii")
    return None

# ---------- Type slot allocation ----------
def allocate_slots(section_counts, total):
    """section_counts: {section: n_questions}; return {section: k_slots}, sum k == total."""
    n = sum(section_counts.values())
    exact = {s: c * total / n for s, c in section_counts.items()}
    base = {s: int(v) for s, v in exact.items()}
    rem = total - sum(base.values())
    order = sorted(exact, key=lambda s: (-(exact[s] - base[s]), -section_counts[s], s))
    for s in order[:rem]:
        base[s] += 1
    # every section must have >=1 slot if it has questions and total allows
    for s, c in section_counts.items():
        if base[s] == 0 and c > 0:
            donor = max(base, key=lambda x: base[x] * 1000 - section_counts.get(x, 0))
            if base[donor] > 1:
                base[donor] -= 1; base[s] = 1
    assert sum(base.values()) == total, (base, total)
    return base

def chunk_questions(qs, k):
    """Split the section's question list (already ordered by point, then original
    order) into exactly k contiguous, balanced, non-empty chunks. Question-level
    so k can always be reached when k <= len(qs), unlike point-level chunking."""
    n = len(qs)
    assert 1 <= k <= n, (k, n)
    base, rem = divmod(n, k)
    chunks, i = [], 0
    for j in range(k):
        sz = base + (1 if j < rem else 0)
        chunks.append(qs[i:i + sz]); i += sz
    assert sum(len(c) for c in chunks) == n
    return chunks

# ---------- multi -> combo conversion ----------
CIRC = {0: "①", 1: "②", 2: "③", 3: "④"}
def to_combo(q_text, opts, answer_set):
    opts = {k: norm_ws(v) for k, v in opts.items()}
    q_text = norm_ws(q_text)
    letters = "ABCD"
    correct_idx = sorted(letters.index(c) for c in answer_set)
    correct = "".join(CIRC[i] for i in correct_idx)
    n = len(correct_idx)
    allsets = [s for s in itertools.combinations(range(4), n) if list(s) != correct_idx]
    if n == 4:
        allsets = [s for s in itertools.combinations(range(4), 3)]
    # deterministic pick of 3 distractors: rotate for variety
    seed = sum(ord(c) for c in q_text[:10])
    pick = [allsets[(seed + i) % len(allsets)] for i in range(3)]
    seen, final = set(), []
    for s in pick + allsets:
        if s not in seen:
            seen.add(s); final.append("".join(CIRC[i] for i in s))
        if len(final) == 3: break
    stem = (q_text + "\n【多选题·单项作答】请从①~④中选出全部正确的选项组合：\n"
            + "\n".join(f"{CIRC[i]} {opts[letters[i]]}" for i in range(4)))
    return stem, [correct] + final

# ---------- generic DB write ----------
def fresh_db(path):
    shutil.copyfile(ORIG, path)
    con = sqlite3.connect(path)
    cur = con.cursor()
    for t in ["Question", "TypeMapping", "Type", "Kind", "Version", "Examinee", "Learn",
              "LearnList", "Mistake", "Simulation", "SimulationList", "Collection", "ExportLog", "Admin"]:
        cur.execute(f"DELETE FROM {t}")
    cur.execute("UPDATE sqlite_sequence SET seq = 0")
    try:
        cur.execute("ALTER TABLE Question ADD COLUMN CorrectOptions TEXT")
    except Exception:
        pass  # already exists
    con.commit()
    return con

def insert_kind(con, name, version, amount, cutoff, duration):
    cur = con.cursor()
    cur.execute("INSERT INTO Kind (KindName,Version,QuestionAmount,CutOffScores,ExamDuration) VALUES (?,?,?,?,?)",
                (name, version, amount, cutoff, duration))
    kid = cur.lastrowid
    cur.execute("INSERT INTO Version (KindID,Version,InsertDt) VALUES (?,?,datetime('now','localtime'))", (kid, version))
    return kid

def insert_type(con, kind_id, name):
    cur = con.cursor()
    cur.execute("INSERT INTO Type (TypeName,KindID,IsDelete) VALUES (?,?,0)", (name, kind_id))
    return cur.lastrowid

def insert_question(con, code, q, pic_b64, options, remark, correct_options=None):
    cur = con.cursor()
    cur.execute("INSERT INTO Question (QuestionCode,Question,QuestionPic,Option1,Option2,Option3,Option4,Remark,IsDelete,CorrectOptions) VALUES (?,?,?,?,?,?,?,?,0,?)",
                (code, q, pic_b64 or "", options[0], options[1], options[2], options[3], remark, correct_options))
    return cur.lastrowid

def map_qt(con, type_id, qid):
    con.cursor().execute("INSERT INTO TypeMapping (TypeID,QuestionID,IsDelete) VALUES (?,?,0)", (type_id, qid))

# ---------- CRAC build ----------
def build_crac():
    out = os.path.join(OUTDIR, "CRAC.sqlite3")
    con = fresh_db(out)
    log("=== CRAC build:", out)
    spec = {
        "A": ("2025.08.09", 40, 30, 40),
        "B": ("2025.08.09", 60, 45, 60),
        "C": ("2025.08.09", 90, 70, 90),
    }
    for cls, (ver, amount, cutoff, dur) in spec.items():
        qs = json.load(open(glob.glob(os.path.join(WORK, f"parsed_{cls}类题库.json"))[0], encoding="utf-8"))
        kid = insert_kind(con, cls, ver, amount, cutoff, dur)
        # sections x.y -> counts ; points x.y.z -> counts
        sec_cnt = collections.Counter(q["p"].rsplit(".", 1)[0] for q in qs)
        pt_cnt = collections.Counter(q["p"] for q in qs)
        slots = allocate_slots(dict(sec_cnt), amount)
        log(f" Kind {cls}: {len(qs)} questions, {len(sec_cnt)} sections, slots={sorted(slots.items())}")
        # build section -> {point: questions}
        by_sec = collections.defaultdict(lambda: collections.defaultdict(list))
        for q in qs:
            by_sec[q["p"].rsplit(".", 1)[0]][q["p"]].append(q)
        multi_n = fig_n = 0
        for sec in sorted(by_sec):
            k = slots[sec]
            # questions of this section ordered by (knowledge point, original order)
            qs_sec = [q for p in sorted(by_sec[sec]) for q in by_sec[sec][p]]
            assert 1 <= k <= len(qs_sec), (cls, sec, k, len(qs_sec))
            for chunk in chunk_questions(qs_sec, k):
                pts = [q["p"] for q in chunk]
                tname = (f"{sec}（{pts[0]}）" if pts[0] == pts[-1]
                         else f"{sec}（{pts[0]}-{pts[-1]}）")
                tid = insert_type(con, kid, tname)
                for q in chunk:
                    qtext = norm_ws(q["q"])
                    letters = "ABCD"
                    ans = q["t"]
                    correct_options = None
                    if len(ans) > 1:
                        # 真·多选：原始四选项原位保留，正确项集合写入 CorrectOptions
                        qtext = "【多选题】" + qtext + "（请勾选全部正确选项，多选或少选均不得分）"
                        options = [norm_ws(q["opts"][letters[i]]) for i in range(4)]
                        correct_options = ",".join(str(letters.index(c)) for c in sorted(ans))
                        multi_n += 1
                    else:
                        ci = letters.index(ans)
                        rest = [q["opts"][letters[i]] for i in range(4) if i != ci]
                        options = [q["opts"][letters[ci]]] + rest
                        options = [norm_ws(o) for o in options]
                    pic = fig_b64(q["j"]) if q["j"] != "LX" else None
                    if pic: fig_n += 1
                    remark = f"原题号{q['j']}" if q["j"] != "LX" else ""
                    qid = insert_question(con, q["i"], qtext, pic, options, remark, correct_options)
                    map_qt(con, tid, qid)
        log(f"   multi-questions={multi_n}, with-figure={fig_n}")
    con.commit()
    verify(con, "CRAC")
    con.close()

def verify(con, tag):
    cur = con.cursor()
    log(f"--- verify {tag} ---")
    log(" integrity:", cur.execute("PRAGMA integrity_check").fetchone()[0])
    for kid, name, ver, amt, cut, dur in cur.execute("SELECT * FROM Kind"):
        ntype = cur.execute("SELECT COUNT(*) FROM Type WHERE KindID=? AND IsDelete=0", (kid,)).fetchone()[0]
        nmap = cur.execute("""SELECT COUNT(DISTINCT m.QuestionID) FROM TypeMapping m
                              JOIN Type t ON t.ID=m.TypeID AND t.IsDelete=0 AND t.KindID=?""", (kid,)).fetchone()[0]
        bad = cur.execute("""SELECT COUNT(*) FROM TypeMapping m
                             JOIN Type t ON t.ID=m.TypeID AND t.IsDelete=0 AND t.KindID=?
                             JOIN Question q ON q.ID=m.QuestionID AND q.IsDelete=0
                             WHERE q.Option1 IS NULL OR q.Option1='' OR q.Question IS NULL OR q.Question=''""", (kid,)).fetchone()[0]
        log(f" Kind {name}(id={kid}) ver={ver} amount={amt} cutoff={cut} dur={dur}: types={ntype} questions={nmap} bad={bad}")
    pics = cur.execute("SELECT COUNT(*) FROM Question WHERE QuestionPic IS NOT NULL AND QuestionPic<>''").fetchone()[0]
    tot = cur.execute("SELECT COUNT(*) FROM Question").fetchone()[0]
    log(f" total questions={tot}, with pics={pics}")
    # orphan checks
    orph = cur.execute("SELECT COUNT(*) FROM Question q WHERE NOT EXISTS (SELECT 1 FROM TypeMapping m WHERE m.QuestionID=q.ID)").fetchone()[0]
    log(f" orphan questions (no mapping)={orph}")

# ---------- FAA build ----------
def build_faa():
    out = os.path.join(OUTDIR, "FAA.sqlite3")
    con = fresh_db(out)
    log("=== FAA build:", out)
    import pickle
    # Figure labels are normalized "uppercase, hyphen stripped" (T-1 -> T1,
    # G7-1 -> G71, E5-1 -> E51). Labels follow the NCVEC public-release figure
    # order for each pool (document order of the images in the docx).
    spec = {
        "T": ("2026-2030 Technician (eff. 2026-07-01)", 35, 26, 30, "T1,T2,T3"),
        "G": ("2023-2027 General (6th errata 2026-02-04)", 35, 26, 30, "G71"),
        "E": ("2024-2028 Extra (4th errata 2026-02-04)", 50, 37, 60, "E51,E61,E62,E63,E71,E72,E73,E91,E92,E93"),
    }
    for cls, (ver, amount, cutoff, dur, figlabels_csv) in spec.items():
        qs = json.load(open(os.path.join(WORK, f"faa_{cls}.json"), encoding="utf-8"))
        imgs = pickle.load(open(os.path.join(WORK, f"faa_{cls}_imgs.pkl"), "rb"))
        labels = figlabels_csv.split(",")
        blobs = [im["blob"] for im in imgs]
        assert len(blobs) == len(labels), (cls, len(blobs), len(labels))
        figmap = {}
        for lab, blob in zip(labels, blobs):
            img = Image_ensure(blob)
            figmap[lab] = base64.b64encode(img).decode("ascii")
        kid = insert_kind(con, cls, ver, amount, cutoff, dur)
        groups = collections.defaultdict(list)
        for q in qs: groups[q["group"]].append(q)
        assert len(groups) == amount, (cls, len(groups), amount)
        fig_n = 0
        for g in sorted(groups):
            tid = insert_type(con, kid, g)
            for q in groups[g]:
                ci = "ABCD".index(q["ans"])
                rest = [q["opts"]["ABCD"[i]] for i in range(4) if i != ci]
                options = [q["opts"][q["ans"]]] + rest
                blob = q["q"] + " " + " ".join(q["opts"].values())
                m = re.search(r"Figure\s+([TEG][\w-]*\d)", blob, re.I)
                pic = None
                if m:
                    lab = m.group(1).upper().replace("-", "")
                    if lab in figmap:
                        pic = figmap[lab]; fig_n += 1
                remark = f"FCC Part {q['ref']}" if q["ref"] else ""
                qid = insert_question(con, q["id"], q["q"], pic, options, remark)
                map_qt(con, tid, qid)
        log(f" Kind {cls}: {len(qs)} questions, {len(groups)} groups, figure-attached={fig_n}")
    con.commit()
    verify(con, "FAA")
    con.close()

def Image_ensure(blob):
    from PIL import Image
    img = Image.open(io.BytesIO(blob)).convert("RGB")
    if img.width > 700:
        img = img.resize((700, int(img.height * 700 / img.width)), Image.LANCZOS)
    buf = io.BytesIO(); img.save(buf, "JPEG", quality=82)
    return buf.getvalue()

if __name__ == "__main__":
    build_crac()
    build_faa()
    LOG.close()
    print("ALL DONE")
