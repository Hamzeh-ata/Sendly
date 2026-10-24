import json
import sys
from collections import Counter
from urllib.parse import urlparse

sys.stdout.reconfigure(encoding="utf-8")

urls = json.load(open(".tmp-run-case5/discovered-urls.json", encoding="utf-8-sig"))
if isinstance(urls, dict):
    for v in urls.values():
        if isinstance(v, list):
            urls = v
            break

print("=== DEPTH0 SEEDS ===")
for u in sorted(
    [x for x in urls if (x.get("depth") or 0) == 0],
    key=lambda z: z.get("discoveredAtUtc") or "",
):
    print(u.get("status"), u.get("sourceType"), u.get("sourceName"), u.get("url"))

print()
print("=== URL STATUS / SOURCE / DEPTH ===")
print(Counter(u.get("status") for u in urls))
print(Counter(u.get("sourceType") for u in urls))
print(Counter(u.get("depth") for u in urls))

pages = json.load(open(".tmp-run-case5/crawled-pages.json", encoding="utf-8-sig"))
if isinstance(pages, dict):
    for v in pages.values():
        if isinstance(v, list):
            pages = v
            break
hosts = Counter()
for p in pages:
    u = p.get("finalUrl") or p.get("url") or ""
    try:
        hosts[urlparse(u).netloc.replace("www.", "")] += 1
    except Exception:
        pass
print()
print("=== CRAWLED HOSTS ===")
for h, c in hosts.most_common(30):
    print(f"{c:3} {h}")

r = json.load(open(".tmp-run-case5/run-detail.json", encoding="utf-8-sig"))
print()
print("=== PROSPECTS ===")
for p in r.get("prospects") or []:
    print(f"{p.get('status'):12} score={p.get('qualificationScore')} email={p.get('email')}")
    print(f"  name={p.get('companyName')}")
    print(f"  site={p.get('website')}")
    print(f"  src={p.get('sourceUrl')}")

ev = json.load(open(".tmp-run-case5/events.json", encoding="utf-8-sig"))
if isinstance(ev, dict):
    for v in ev.values():
        if isinstance(v, list):
            ev = v
            break
print()
print("=== EVENT TYPES ===")
for t, c in Counter(e.get("eventType") for e in ev).most_common(30):
    print(f"{c:4} {t}")

print()
print("=== ELIGIBILITY / COVERAGE EVENTS ===")
for e in ev:
    t = e.get("eventType") or ""
    if t in ("SourceEligibilityEvaluated", "DiscoveryCoverageDecision"):
        print(t, "|", (e.get("message") or "")[:200])
        p = e.get("payloadJson") or e.get("dataJson") or e.get("payload")
        if isinstance(p, str) and p:
            print(" ", p[:400])
        elif isinstance(p, dict):
            print(" ", json.dumps(p, ensure_ascii=False)[:400])

print()
print("=== LISTING ORG NAMES (sample) ===")
names = Counter()
for e in ev:
    if e.get("eventType") != "ListingOrganizationExtracted":
        continue
    m = e.get("message") or ""
    if "Extracted organization '" in m:
        names[m.split("Extracted organization '")[1].split("' from")[0]] += 1
for n, c in names.most_common(25):
    print(f"{c:3} {n}")

print()
print("=== REJECTED / QUALIFIED ===")
for e in ev:
    if e.get("eventType") in ("Rejected", "Qualified", "Completed"):
        print(e.get("eventType"), "|", (e.get("message") or "")[:160])
        p = e.get("payloadJson") or e.get("dataJson")
        if isinstance(p, str) and p:
            print(" ", p[:250])

print()
print("=== TOP PENDING ===")
d = r.get("searchProviderDiagnostics") or {}
for u in (d.get("topPendingUrls") or [])[:12]:
    print(u.get("priority"), u.get("host"), u.get("url"))
