# -*- coding: utf-8 -*-
"""
Convert downloaded NCVEC DOCX pools to SQLite banks.
Reads from downloads/*.docx, outputs to release/banks/.
"""
import os, sys, io, json, re, glob, sqlite3, shutil, hashlib
from datetime import datetime
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

BASE = os.path.join(os.path.dirname(__file__), "..")
DL_DIR = os.path.join(BASE, "downloads")
BANKS_DIR = os.path.join(BASE, "release", "banks")
os.makedirs(BANKS_DIR, exist_ok=True)

QRX = re.compile(r"^([TEG]\d[A-Z]\d{2})\s*\(([A-D])\)\s*(?:\[([^]]*)\])?\s*$")
OPTRX = re.compile(r"^([A-D])\.\s*(.*)$")

def parse_docx(path):
    """Parse a DOCX question pool file."""
    from docx import Document
    doc = Document(path)
    questions = []
    cur = None
    for para in doc.paragraphs:
        t = para.text.strip()
        if not t:
            continue
        t = re.sub(r"~{2,}.*$", "", t).strip()  # strip ~~ tails
        if not t:
            continue
        mq = QRX.match(t)
        if mq:
            if cur:
                questions.append(cur)
            cur = {
                "id": mq.group(1), "ans": mq.group(2),
                "ref": mq.group(3) or "", "group": mq.group(1)[:3],
                "q": [], "opts": {}
            }
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
    if cur:
        questions.append(cur)
    for q in questions:
        q["q"] = " ".join(q["q"]).strip()
        q["opts"] = {k: " ".join(v).strip() for k, v in q["opts"].items()}
    return questions

def build_bank(questions, pool_type, version_str):
    """Build a clean SQLite bank from parsed questions."""
    # Map pool type to KindID
    kind_map = {"T": 1, "G": 2, "E": 3}
    kid = kind_map.get(pool_type, 1)
    
    # Create fresh DB
    db_path = os.path.join(BANKS_DIR, f"FAA_{pool_type}_auto.sqlite3")
    if os.path.exists(db_path):
        os.remove(db_path)
    con = sqlite3.connect(db_path)
    cur = con.cursor()
    
    # Minimal schema (matches app expectations)
    cur.execute("CREATE TABLE Kind (ID INTEGER PRIMARY KEY AUTOINCREMENT, KindName TEXT, Version TEXT, QuestionAmount INTEGER, CutOffScores INTEGER, ExamDuration INTEGER)")
    cur.execute("CREATE TABLE Type (ID INTEGER PRIMARY KEY AUTOINCREMENT, TypeName TEXT, KindID INTEGER, InsertDt TEXT DEFAULT (datetime('now','localtime')), UpdateDt TEXT DEFAULT (datetime('now','localtime')), IsDelete INTEGER DEFAULT 0)")
    cur.execute("CREATE TABLE Question (ID INTEGER PRIMARY KEY AUTOINCREMENT, QuestionCode TEXT, Question TEXT, QuestionPic TEXT, Option1 TEXT, Option2 TEXT, Option3 TEXT, Option4 TEXT, Remark TEXT, InsertDt TEXT DEFAULT (datetime('now','localtime')), UpdateDt TEXT DEFAULT (datetime('now','localtime')), IsDelete INTEGER DEFAULT 0, CorrectOptions TEXT)")
    cur.execute("CREATE TABLE TypeMapping (ID INTEGER PRIMARY KEY AUTOINCREMENT, TypeID INTEGER, QuestionID INTEGER, InsertDt TEXT DEFAULT (datetime('now','localtime')), UpdateDt TEXT DEFAULT (datetime('now','localtime')), IsDelete INTEGER DEFAULT 0)")
    cur.execute("CREATE TABLE Version (ID INTEGER PRIMARY KEY AUTOINCREMENT, KindID INTEGER, Version TEXT, InsertDt TEXT)")
    
    # Exam params per pool
    params = {
        "T": ("T", 35, 26, 30),
        "G": ("G", 35, 26, 30),
        "E": ("E", 50, 37, 60),
    }
    kname, amount, cutoff, duration = params.get(pool_type, ("T", 35, 26, 30))
    
    cur.execute("INSERT INTO Kind (KindName,Version,QuestionAmount,CutOffScores,ExamDuration) VALUES(?,?,?,?,?)",
                (kname, version_str, amount, cutoff, duration))
    kind_id = cur.lastrowid
    cur.execute("INSERT INTO Version (KindID,Version,InsertDt) VALUES(?,?,datetime('now','localtime'))",
                (kind_id, version_str))
    
    # Group questions by group code
    groups = {}
    for q in questions:
        g = q["group"]
        if g not in groups:
            groups[g] = []
        groups[g].append(q)
    
    # Insert types and questions
    type_ids = {}
    for gname in sorted(groups):
        cur.execute("INSERT INTO Type (TypeName,KindID,IsDelete) VALUES(?,?,0)", (gname, kind_id))
        type_ids[gname] = cur.lastrowid
    
    count = 0
    for q in questions:
        letters = "ABCD"
        ci = letters.index(q["ans"])
        rest = [q["opts"].get(letters[i], "") for i in range(4) if i != ci]
        options = [q["opts"].get(q["ans"], "")] + rest
        options = [o if o else "" for o in options]
        
        cur.execute(
            "INSERT INTO Question (QuestionCode,Question,QuestionPic,Option1,Option2,Option3,Option4,Remark,IsDelete) VALUES(?,?,?,?,?,?,?,?,0)",
            (q["id"], q["q"], "", options[0], options[1], options[2], options[3], 
             f"FCC Part {q['ref']}" if q["ref"] else "")
        )
        qid = cur.lastrowid
        cur.execute("INSERT INTO TypeMapping (TypeID,QuestionID,IsDelete) VALUES(?,?,0)",
                    (type_ids[q["group"]], qid))
        count += 1
    
    con.commit()
    integrity = cur.execute("PRAGMA integrity_check").fetchone()[0]
    con.close()
    
    return {"file": db_path, "questions": count, "types": len(groups), "integrity": integrity}

def main():
    print("=== Pool Converter ===")
    
    # Load fetch summary
    summary_file = os.path.join(DL_DIR, "fetch_summary.json")
    if not os.path.exists(summary_file):
        print("No fetch_summary.json found. Run fetch_pools.py first.")
        sys.exit(1)
    
    with open(summary_file) as f:
        summary = json.load(f)
    
    print(f"Fetch timestamp: {summary.get('timestamp', 'unknown')}")
    
    results = {}
    for pool_type in ["T", "G", "E"]:
        docx_path = os.path.join(DL_DIR, f"{pool_type}_pool.docx")
        if not os.path.exists(docx_path):
            print(f"[{pool_type}] No DOCX file found, skipping")
            results[pool_type] = {"status": "skipped"}
            continue
        
        print(f"[{pool_type}] Parsing {docx_path}")
        try:
            questions = parse_docx(docx_path)
            print(f"  Parsed {len(questions)} questions")
            
            if len(questions) < 100:
                print(f"  WARNING: suspiciously few questions ({len(questions)}), skipping")
                results[pool_type] = {"status": "too_few", "count": len(questions)}
                continue
            
            # Extract version from filename or content
            version_str = f"Auto-updated {datetime.now().strftime('%Y-%m-%d')}"
            
            result = build_bank(questions, pool_type, version_str)
            results[pool_type] = {"status": "ok", **result}
            print(f"  Built: {result['questions']} questions, {result['types']} types, integrity={result['integrity']}")
            
        except Exception as e:
            print(f"  ERROR: {e}")
            results[pool_type] = {"status": "error", "error": str(e)}
    
    # Save conversion results
    conv_file = os.path.join(DL_DIR, "convert_results.json")
    with open(conv_file, "w") as f:
        json.dump(results, f, indent=2)
    
    ok_count = sum(1 for r in results.values() if r.get("status") == "ok")
    print(f"\nConversion complete: {ok_count}/3 pools OK")
    
    if ok_count == 0:
        print("::error::No pools were successfully converted")
        sys.exit(1)

if __name__ == "__main__":
    main()
