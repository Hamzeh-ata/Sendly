import json
import sys
from collections import Counter
from urllib.parse import urlparse

sys.stdout.reconfigure(encoding="utf-8")
base = r"c:\Users\User\source\repos\BulkMail\.tmp-this-run"


def load(name):
    data = json.load(open(f"{base}/{name}", encoding="utf-8-sig"))
    if isinstance(data, dict):
        for v in data.values():
            if isinstance(v, list) and name != "run-detail.json":
                return v
    return data


urls = load("discovered-urls.json")
pages = load("crawled-pages.json")
events = load("events.json")
run = load("run-detail.json")
prospects = load("prospects.json")

print("=== DISCOVERED URL SAMPLE KEYS ===")
if urls:
    print(list(urls[0].keys()))

print("\n=== DEPTH0 SEEDS ===")
for u in sorted(
    [x for x in urls if (x.get("depth") or 0) == 0],
    key=lambda z: z.get("discoveredAtUtc") or "",
):
    print(
        u.get("status"),
        u.get("sourceType"),
        u.get("sourceName") or u.get("discoverySource") or "",
        (u.get("url") or "")[:140],
    )

print("\nSTATUS", Counter(u.get("status") for u in urls))
print("SOURCE TYPE", Counter(u.get("sourceType") for u in urls))
print("SOURCE NAME", Counter(u.get("sourceName") or u.get("discoverySource") for u in urls))
print("DEPTH", Counter(u.get("depth") for u in urls))
print("PURPOSE", Counter(u.get("pagePurpose") for u in urls))

print("\n=== ALL DISCOVERED HOSTS ===")
hosts = Counter()
for u in urls:
    raw = u.get("url") or ""
    try:
        hosts[urlparse(raw).netloc.replace("www.", "")] += 1
    except Exception:
        pass
for h, c in hosts.most_common(40):
    print(f"{c:3} {h}")

print("\n=== CRAWLED PAGES KEYS ===")
if pages:
    print(list(pages[0].keys()))

print("\n=== CRAWLED PAGES ===")
for p in pages:
    u = p.get("finalUrl") or p.get("url") or ""
    print(
        p.get("httpStatus") or p.get("status"),
        p.get("pagePurpose"),
        (p.get("title") or "")[:80],
        "|",
        u[:140],
    )

print("\n=== EVENT TYPES ===")
for t, c in Counter(e.get("eventType") for e in events).most_common(50):
    print(f"{c:4} {t}")

print("\n=== EVENT SEVERITY ===")
print(Counter(e.get("severity") for e in events))

print("\n=== ERROR / WARNING EVENTS ===")
for e in events:
    sev = (e.get("severity") or "").lower()
    if sev in ("error", "warning", "critical"):
        print(e.get("severity"), e.get("eventType"), e.get("stage"), "|", (e.get("message") or "")[:240])

print("\n=== LIFECYCLE / DISCOVERY / QUALIFICATION ===")
interesting = (
    "RunStarted",
    "RunCompleted",
    "DiscoveryStarted",
    "DiscoveryCompleted",
    "Provider",
    "Recovery",
    "Qualified",
    "Rejected",
    "NeedsReview",
    "ProspectSaved",
    "SearchLimit",
    "Budget",
    "Seed",
)
for e in events:
    t = e.get("eventType") or ""
    m = e.get("message") or ""
    if any(x.lower() in t.lower() or x.lower() in m.lower() for x in interesting):
        print(t, "|", m[:220])
