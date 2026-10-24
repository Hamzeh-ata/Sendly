import json
import sys
from collections import Counter
from urllib.parse import urlparse

sys.stdout.reconfigure(encoding="utf-8")
base = "."

urls = json.load(open(f"{base}/discovered-urls.json", encoding="utf-8-sig"))
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
print("STATUS", Counter(u.get("status") for u in urls))
print("SOURCE", Counter(u.get("sourceType") for u in urls))
print("DEPTH", Counter(u.get("depth") for u in urls))

pages = json.load(open(f"{base}/crawled-pages.json", encoding="utf-8-sig"))
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
for h, c in hosts.most_common(40):
    print(f"{c:3} {h}")

r = json.load(open(f"{base}/run-detail.json", encoding="utf-8-sig"))
print()
print("=== RUN KEYS ===")
for k, v in r.items():
    if not isinstance(v, (list, dict)):
        print(f"  {k}: {v}")

print()
print("=== PROSPECTS ===")
for p in r.get("prospects") or []:
    print(
        f"{p.get('status'):16} score={p.get('qualificationScore')} email={p.get('email')} country={p.get('country')}"
    )
    print(f"  name={p.get('companyName')}")
    print(f"  site={p.get('website')}")
    print(f"  city={p.get('city')} src={p.get('sourceUrl')}")
    crit = p.get("criteria") or p.get("qualificationCriteria") or []
    for c in crit:
        print(
            f"    crit {c.get('criterionKey')}: passed={c.get('passed')} mand={c.get('isMandatory')} reason={c.get('reason')}"
        )

ev = json.load(open(f"{base}/events.json", encoding="utf-8-sig"))
if isinstance(ev, dict):
    for v in ev.values():
        if isinstance(v, list):
            ev = v
            break
print()
print("=== EVENT TYPES ===")
for t, c in Counter(e.get("eventType") for e in ev).most_common(40):
    print(f"{c:4} {t}")

print()
print("=== LISTING ORG NAMES ===")
names = Counter()
for e in ev:
    if e.get("eventType") != "ListingOrganizationExtracted":
        continue
    m = e.get("message") or ""
    if "Extracted organization '" in m:
        names[m.split("Extracted organization '")[1].split("' from")[0]] += 1
for n, c in names.most_common(40):
    print(f"{c:3} {n}")

print()
print("=== REJECTED / QUALIFIED / COUNTRY ===")
for e in ev:
    t = e.get("eventType") or ""
    m = (e.get("message") or "")
    if t in ("Rejected", "Qualified", "Completed", "CountryGateFailed", "NeedsReview") or "country" in m.lower() or "vietnam" in m.lower() or "hanoi" in m.lower():
        print(t, "|", m[:200])

print()
print("=== TOP PENDING ===")
d = r.get("searchProviderDiagnostics") or r.get("diagnostics") or {}
for u in (d.get("topPendingUrls") or [])[:15]:
    print(u.get("priority"), u.get("host"), (u.get("url") or "")[:120])

print()
print("=== NAIS / SSO / CLOSEUP / OPENCORP URLS FETCHED ===")
for p in pages:
    u = (p.get("finalUrl") or p.get("url") or "").lower()
    if any(x in u for x in ["nais", "sso", "closeup", "opencorporates", "international-schools"]):
        print((p.get("finalUrl") or p.get("url") or "")[:160])
