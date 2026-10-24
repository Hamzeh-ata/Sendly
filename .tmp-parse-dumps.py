import json
import collections
from pathlib import Path

ROOT = Path(r"c:\Users\User\source\repos\BulkMail")


def load(p):
    with open(p, encoding="utf-8-sig") as f:
        return json.load(f)


def summarize_events(label, path):
    evs = load(path)
    print("=" * 80)
    print(label, "event count", len(evs))
    types = collections.Counter(e.get("eventType") for e in evs)
    stages = collections.Counter(e.get("stage") for e in evs)
    sevs = collections.Counter(e.get("severity") for e in evs)
    print("eventTypes:", dict(types))
    print("stages:", dict(stages))
    print("severities:", dict(sevs))
    print("--- first sample of each eventType ---")
    seen = set()
    for e in evs:
        t = e.get("eventType")
        if t in seen:
            continue
        seen.add(t)
        msg = (e.get("message") or "")[:220]
        print(f"  {t} | stage={e.get('stage')} sev={e.get('severity')} provider={e.get('provider')}")
        print(f"    {msg}")
        dj = e.get("dataJson")
        if dj:
            print(f"    dataJson={dj[:280]}")

    print("--- search-like events ---")
    searchish = []
    for e in evs:
        blob = " ".join(
            [
                str(e.get("eventType") or ""),
                str(e.get("stage") or ""),
                str(e.get("message") or ""),
                str(e.get("provider") or ""),
                str(e.get("dataJson") or ""),
            ]
        ).lower()
        if any(k in blob for k in ["search", "query", "searx", "brave", "websearch"]):
            searchish.append(e)
    print("count", len(searchish))
    qtexts = collections.Counter()
    for e in searchish:
        dj = e.get("dataJson") or ""
        msg = e.get("message") or ""
        for src in (dj, msg):
            try:
                obj = json.loads(src) if src.startswith("{") or src.startswith("[") else None
            except Exception:
                obj = None
            if isinstance(obj, dict):
                for k in ("query", "searchQuery", "text", "q"):
                    if obj.get(k):
                        qtexts[str(obj[k])] += 1
        if "query" in msg.lower() or "search" in msg.lower():
            qtexts[msg[:180]] += 0
    print("parsed query texts", len(qtexts))
    for q, c in qtexts.most_common(40):
        print(f"  {c}x {q}")
    print("--- first 20 searchish ---")
    for e in searchish[:20]:
        print(
            " ",
            e.get("eventType"),
            "|",
            e.get("stage"),
            "|",
            (e.get("message") or "")[:180],
            "| provider=",
            e.get("provider"),
        )
        if e.get("dataJson"):
            print("    dataJson", e["dataJson"][:320])

    print("--- DateTimeOffset / SQLite / SearXNG ---")
    for kw in ["DateTimeOffset", "SQLite", "SearXNG", "searx", "Maximum search"]:
        n = sum(1 for e in evs if kw.lower() in json.dumps(e, ensure_ascii=False).lower())
        print(f"  {kw}: {n}")
        if n:
            for e in evs:
                blob = json.dumps(e, ensure_ascii=False)
                if kw.lower() in blob.lower():
                    print("   sample:", e.get("eventType"), (e.get("message") or "")[:200])
                    if e.get("dataJson"):
                        print("   dataJson:", e["dataJson"][:400])
                    break

    print("--- distinct messages for Error/Warning ---")
    msgs = collections.Counter()
    for e in evs:
        if (e.get("severity") or "") in ("Error", "Warning") or (e.get("eventType") or "") in (
            "Error",
            "Failed",
            "ProviderFailed",
            "SearchFailed",
        ):
            msgs[(e.get("eventType"), (e.get("message") or "")[:160])] += 1
    for (t, m), c in msgs.most_common(30):
        print(f"  {c}x {t}: {m}")


def summarize_json_array(label, path, url_key="url"):
    data = load(path)
    print("=" * 80)
    print(label, "count", len(data) if isinstance(data, list) else type(data))
    if not isinstance(data, list):
        return
    hosts = collections.Counter()
    statuses = collections.Counter()
    sources = collections.Counter()
    purposes = collections.Counter()
    for item in data:
        url = item.get(url_key) or item.get("normalizedUrl") or ""
        host = item.get("normalizedDomain") or item.get("domain")
        if not host and url:
            host = url.split("/")[2] if "://" in url else url
        hosts[host] += 1
        statuses[item.get("status") or item.get("httpStatusCode") or item.get("error")] += 1
        sources[item.get("sourceType") or item.get("sourceName")] += 1
        purposes[item.get("pagePurpose")] += 1
    print("hosts", hosts.most_common(10))
    print("statuses", statuses.most_common(10))
    print("sources", sources.most_common(10))
    print("purposes", purposes.most_common(10))
    interesting = [
        "pricing",
        "privacy",
        "terms",
        "rss",
        "feed",
        "advanced-search",
        "0007.ae",
        "academia",
        "government",
    ]
    print("interesting urls:")
    for item in data:
        url = (item.get(url_key) or item.get("normalizedUrl") or "").lower()
        if any(k in url for k in interesting):
            print(
                " ",
                item.get("status") or item.get("httpStatusCode"),
                item.get("error"),
                item.get(url_key) or item.get("normalizedUrl"),
                "title=",
                item.get("title"),
            )


def summarize_prospects(label, path):
    data = load(path)
    print("=" * 80)
    print(label, "count", len(data))
    junk_needles = [
        "advanced search",
        "academia",
        "government",
        "important information",
        "make offer",
        "terms of",
        "privacy",
        "legal",
        "pricing",
    ]
    print("all company names:")
    for p in data:
        name = p.get("companyName")
        print(
            " ",
            p.get("researchRunId", "")[:8],
            p.get("status"),
            p.get("qualificationScore"),
            p.get("sourceName"),
            "|",
            name,
            "|",
            p.get("sourceUrl"),
        )
        lname = (name or "").lower()
        if any(n in lname for n in junk_needles):
            print("   JUNK MATCH")


def main():
    summarize_events("this-run b9276d24", ROOT / ".tmp-this-run" / "events.json")
    summarize_events("this-run2 040a6670", ROOT / ".tmp-this-run2" / "events.json")
    summarize_json_array("this-run crawled-pages", ROOT / ".tmp-this-run" / "crawled-pages.json")
    summarize_json_array("this-run2 crawled-pages", ROOT / ".tmp-this-run2" / "crawled-pages.json")
    summarize_json_array("this-run discovered-urls", ROOT / ".tmp-this-run" / "discovered-urls.json")
    summarize_json_array("this-run2 discovered-urls", ROOT / ".tmp-this-run2" / "discovered-urls.json")
    summarize_prospects("this-run prospects", ROOT / ".tmp-this-run" / "prospects.json")
    summarize_prospects("this-run2 prospects", ROOT / ".tmp-this-run2" / "prospects.json")

    details = load(ROOT / ".tmp-this-run" / "prospect-details.json")
    print("=" * 80)
    print("prospect-details count", len(details) if isinstance(details, list) else type(details))
    if isinstance(details, list) and details:
        print("keys", sorted(details[0].keys()))
        names = [d.get("companyName") or d.get("organizationName") or d.get("extractedName") for d in details]
        print("detail names sample", names[:20])
        # look for extracted orgs
        for d in details:
            blob = json.dumps(d, ensure_ascii=False).lower()
            if any(k in blob for k in ["advanced search", "academia", "government", "make offer", "important information"]):
                print("junk-ish detail:", {k: d.get(k) for k in list(d)[:12]})


if __name__ == "__main__":
    import sys
    out = ROOT / ".tmp-parse-dumps-out.txt"
    with open(out, "w", encoding="utf-8") as fh:
        sys.stdout = fh
        main()
    sys.stdout = sys.__stdout__
    print("wrote", out)
