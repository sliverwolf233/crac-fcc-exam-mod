# -*- coding: utf-8 -*-
"""
Fetch latest NCVEC question pools (Technician / General / Extra).
Downloads DOCX files and reports version info.
"""
import os, sys, io, json, re, hashlib, urllib.request, urllib.error
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "downloads")
os.makedirs(OUT_DIR, exist_ok=True)

# NCVEC 题库页面
NCVEC_PAGE = "https://www.ncvec.org/index.php/ham-radio/question-pools"

# 已知的题池文件模式（NCVEC 发布的 DOCX 命名规则）
POOL_PATTERNS = {
    "T": re.compile(r'(Technician|Tech).*(?:Pool|pool).*(?:.docx|.doc)', re.I),
    "G": re.compile(r'(General).*(?:Pool|pool).*(?:.docx|.doc)', re.I),
    "E": re.compile(r'(Extra).*(?:Pool|pool).*(?:.docx|.doc)', re.I),
}

# 备用：ARRL 题库页面
ARRL_PAGE = "https://www.arrl.org/question-pools"

def fetch_url(url, timeout=30):
    """Fetch URL content with User-Agent."""
    req = urllib.request.Request(url, headers={
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ExamModBot/1.0"
    })
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()

def find_pool_links(html_text):
    """Extract DOCX download links from NCVEC page HTML."""
    links = {}
    # Find all <a href="...">...</a> pairs
    for match in re.finditer(r'href="([^"]+.(?:docx|doc))"[^>]*>([^<]+)', html_text, re.I):
        url, text = match.group(1), match.group(2)
        # Make absolute URL
        if url.startswith("/"):
            url = "https://www.ncvec.org" + url
        elif not url.startswith("http"):
            url = "https://www.ncvec.org/" + url
        # Match to pool type
        for pool_type, pattern in POOL_PATTERNS.items():
            if pool_type not in links and pattern.search(text):
                links[pool_type] = {"url": url, "label": text.strip()}
    return links

def download_pool(pool_type, url):
    """Download a pool file and return metadata."""
    try:
        data = fetch_url(url)
        filename = f"{pool_type}_pool.docx"
        filepath = os.path.join(OUT_DIR, filename)
        with open(filepath, "wb") as f:
            f.write(data)
        sha256 = hashlib.sha256(data).hexdigest()
        return {
            "type": pool_type,
            "url": url,
            "file": filepath,
            "size": len(data),
            "sha256": sha256,
            "status": "downloaded"
        }
    except Exception as e:
        return {"type": pool_type, "url": url, "status": "error", "error": str(e)}

def main():
    print("=== NCVEC Question Pool Fetcher ===")
    print(f"Time: {os.environ.get('GITHUB_RUN_ID', 'local')}")

    # Load previous state
    state_file = os.path.join(OUT_DIR, "pool_state.json")
    prev_state = {}
    if os.path.exists(state_file):
        with open(state_file) as f:
            prev_state = json.load(f)

    # Fetch NCVEC page
    try:
        print(f"Fetching: {NCVEC_PAGE}")
        html = fetch_url(NCVEC_PAGE).decode("utf-8", errors="replace")
        pools = find_pool_links(html)
        print(f"Found {len(pools)} pool links: {list(pools.keys())}")
    except Exception as e:
        print(f"Error fetching NCVEC page: {e}")
        pools = {}

    if not pools:
        print("No pool links found. Trying ARRL fallback...")
        try:
            html = fetch_url(ARRL_PAGE).decode("utf-8", errors="replace")
            pools = find_pool_links(html)
            print(f"ARRL fallback found {len(pools)} links")
        except Exception as e:
            print(f"ARRL fallback also failed: {e}")

    results = []
    updated = False

    for pool_type in ["T", "G", "E"]:
        if pool_type in pools:
            info = pools[pool_type]
            print(f"[{pool_type}] {info['label']} -> {info['url']}")
            result = download_pool(pool_type, info["url"])
            results.append(result)

            # Check if this is a new version
            old_sha = prev_state.get(pool_type, {}).get("sha256")
            if result["status"] == "downloaded":
                if old_sha != result["sha256"]:
                    print(f"  NEW VERSION detected (sha changed)")
                    updated = True
                else:
                    print(f"  No change (same sha256)")
        else:
            print(f"[{pool_type}] No link found")
            results.append({"type": pool_type, "status": "not_found"})

    # Save state
    new_state = {}
    for r in results:
        if r.get("status") == "downloaded":
            new_state[r["type"]] = {
                "sha256": r["sha256"],
                "url": r["url"],
                "size": r["size"],
                "fetched_at": __import__("datetime").datetime.now().isoformat()
            }
    with open(state_file, "w") as f:
        json.dump(new_state, f, indent=2)

    # Output summary for CI
    summary = {
        "updated": updated,
        "pools": {r["type"]: r.get("status", "unknown") for r in results},
        "timestamp": __import__("datetime").datetime.now().isoformat()
    }
    with open(os.path.join(OUT_DIR, "fetch_summary.json"), "w") as f:
        json.dump(summary, f, indent=2)

    print(f"\nSummary: {json.dumps(summary, indent=2)}")

    # Exit code indicates whether banks should be rebuilt
    if updated:
        print("::notice title=NewPools::New question pool versions detected")
        sys.exit(0)
    else:
        print("::notice title=NoChange::No new pool versions")
        sys.exit(0)  # Still exit 0 so workflow doesn't fail

if __name__ == "__main__":
    main()
