import json
import sys
from collections import Counter

sys.stdout.reconfigure(encoding="utf-8")
base = r"c:\Users\User\source\repos\BulkMail\.tmp-this-run"

events = json.load(open(f"{base}/events.json", encoding="utf-8-sig"))
urls = json.load(open(f"{base}/discovered-urls.json", encoding="utf-8-sig"))
details = json.load(open(f"{base}/prospect-details.json", encoding="utf-8-sig"))
crits = json.load(open(f"{base}/prospect-criteria.json", encoding="utf-8-sig"))
prospects = json.load(open(f"{base}/prospects.json", encoding="utf-8-sig"))

print("=== PROSPECT DETAILS ===")
items = details if isinstance(details, list) else [details]
for p in items:
    print("-" * 80)
    print(p.get("status"), "score=", p.get("qualificationScore"), "qualified=", p.get("isQualified"))
    print("name:", p.get("companyName"))
    print("industry:", p.get("industry"), "city:", p.get("city"), "country:", p.get("country"))
    print("website:", p.get("website"))
    print("email:", p.get("email"), "phone:", p.get("phone"), "wa:", p.get("whatsApp"))
    print("source:", p.get("sourceUrl"))
    print("sourceType:", p.get("sourceType"), "sourceName:", p.get("sourceName"))
    print("notes:", (p.get("researchNotes") or "")[:400])
    print("summary:", (p.get("qualificationSummary") or "")[:500])
    print("desc:", (p.get("companyDescription") or "")[:300])

print("\n=== QUALIFICATION CRITERIA ===")
crit_items = crits if isinstance(crits, list) else [crits]
for item in crit_items:
    print("-" * 80)
    print(item.get("name"))
    for c in item.get("criteria") or []:
        print(
            f"  {c.get('criterionKey') or c.get('key')}: passed={c.get('passed')} mand={c.get('isMandatory')} score={c.get('score')} reason={(c.get('reason') or '')[:180]}"
        )

print("\n=== LISTING ORG NAMES ===")
names = Counter()
for e in events:
    if e.get("eventType") != "ListingOrganizationExtracted":
        continue
    m = e.get("message") or ""
    if "Extracted organization '" in m:
        names[m.split("Extracted organization '")[1].split("' from")[0]] += 1
    else:
        names[m[:120]] += 1
for n, c in names.most_common(60):
    print(f"{c:3} {n}")

print("\n=== COMPANY EXTRACTED ===")
for e in events:
    if e.get("eventType") in ("CompanyExtracted", "ContactExtracted", "Qualified", "Rejected", "EvidenceMerged", "DuplicateSkipped"):
        print(e.get("eventType"), "|", (e.get("message") or "")[:250])
        payload = e.get("payload") or e.get("data") or e.get("details")
        if payload:
            s = json.dumps(payload, ensure_ascii=False)
            print("  payload:", s[:400])

print("\n=== INVESTIGATION / PROFILE ===")
for e in events:
    t = e.get("eventType") or ""
    if t in ("InvestigationFollowUpQueued", "OrganizationProfilesDiscovered", "DirectorySiblingQueued", "UrlDiscovered"):
        print(t, "|", (e.get("message") or "")[:220], "| url=", e.get("url") or "")

print("\n=== DISCOVERY COVERAGE / STARTED / COMPLETED ===")
for e in events:
    t = e.get("eventType") or ""
    if t in ("Started", "Completed", "DiscoveryCoverageDecision") or "eligib" in t.lower() or "search" in t.lower():
        print(t, e.get("severity"), "|", (e.get("message") or "")[:300])

print("\n=== INVESTIGATION URLS ===")
for u in urls:
    sn = u.get("sourceName") or ""
    if "investigation" in sn or "profile" in sn.lower() or u.get("pagePurpose") == "CompanyProfile":
        print(u.get("status"), u.get("pagePurpose"), sn, (u.get("url") or "")[:160])

print("\n=== EVENT SAMPLE KEYS ===")
print(list(events[0].keys()) if events else None)
print(json.dumps(events[0], ensure_ascii=False)[:800])
