# -*- coding: utf-8 -*-
"""Prepare EN + ZH FAA bank variants; verify QuestionID maps identical."""
import shutil, sqlite3, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
B = "D:" + chr(92) + "Codes" + chr(92) + "MockExamProgram_AmateurRadio" + chr(92) + "banks" + chr(92)
# 当前 FAA.sqlite3 是双语版 -> 复制为 FAA_BI.sqlite3
shutil.copyfile(B + "FAA.sqlite3", B + "FAA_BI.sqlite3")
# 重建纯英文版（直接从双语版剥离中文行更快、且 ID 不变）
con = sqlite3.connect(B + "FAA.sqlite3")
cur = con.cursor()
rows = cur.execute("SELECT ID, Question, Option1, Option2, Option3, Option4 FROM Question").fetchall()
NL = chr(10)
MARK = chr(0x3010)
def strip_zh(text):
    if text is None: return text
    parts = text.split(NL)
    keep = []
    for p in parts:
        if p.startswith(MARK):  # 【中】行
            continue
        keep.append(p)
    return NL.join(keep)
def strip_opt(text):
    if text is None: return text
    parts = text.split(NL)
    # 选项：英文在前，中文第二行（无标记）——双语构建时 Option = EN + NL + ZH
    # 判定：若含中文（>0x4E00）则去掉中文行
    keep = [p for p in parts if not any(0x4E00 <= ord(c) <= 0x9FFF for c in p)]
    return NL.join(keep) if keep else text
n = 0
for qid, q, o1, o2, o3, o4 in rows:
    nq = strip_zh(q)
    no1, no2, no3, no4 = strip_opt(o1), strip_opt(o2), strip_opt(o3), strip_opt(o4)
    cur.execute("UPDATE Question SET Question=?, Option1=?, Option2=?, Option3=?, Option4=? WHERE ID=?",
                (nq, no1, no2, no3, no4, qid))
    n += 1
con.commit()
print("english-stripped rows:", n)
# 校验 ID 映射一致
bi = sqlite3.connect(B + "FAA_BI.sqlite3")
a = dict(con.execute("SELECT QuestionCode, ID FROM Question").fetchall())
b = dict(bi.execute("SELECT QuestionCode, ID FROM Question").fetchall())
same = all(a[k] == b[k] for k in a) and len(a) == len(b)
print("ID maps identical:", same, len(a))
print("EN bank integrity:", con.execute("PRAGMA integrity_check").fetchone()[0])
con.close(); bi.close()
