import json
import sys
from collections import Counter
from urllib.parse import urlparse

sys.stdout.reconfigure(encoding="utf-8")
base = r"c:\Users\User\source\repos\BulkMail\.tmp-this-run2"

def load(name):
    data = json.load(open(f"{base}/{name}", encoding="utf-8-sig"))
    if isinstance(data, dict) and name != "run-detail.json":
        for v in data.values():
            if isinstance(v, list):
                return v
    return data

urls = load("discovered-urls.json")
pages = load("crawled-pages.json")
events = load("events.json")
run = load("run-detail.json")

print("=== RUN ===")
d = run.get("searchProviderDiagnostics") or {}
print("status", run.get("status"), "progress", run.get("progressPercent"))
print("started", run.get("startedAtUtc"), "completed", run.get("completedAtUtc"))
print("query", run.get("researchQueryId"), run.get("researchQueryName"))
print("warning", run.get("warning"))
print("error", run.get("error"))
print("requested", run.get("requestedProspectCount"), "minScore", run.get("minQualificationScore"))
print("urls", run.get("urlsDiscovered"), "pages", run.get("pagesCrawled"), "errors", run.get("crawlErrors"))
print("companies", run.get("companiesFound"), "contacts", run.get("contactsFound"))
print("qualified", run.get("qualifiedCount"), "saved", run.get("prospectsSaved"), "dupes", run.get("duplicatesFound"))
print("search q", run.get("searchQueriesExecuted"), "results", run.get("searchResultsReceived"))
print("candidates", run.get("candidatesDiscovered"), "afterDedup", run.get("candidatesAfterDeduplication"), "validated", run.get("candidatesValidated"))
print("provider", d.get("provider"))
print("auto", d.get("automaticDiscoverySummary"))
print("plan", d.get("discoveryPlanSummary"))
print("stop", d.get("discoveryStopReason"))
print("domains", d.get("uniqueDomainsDiscovered"))
print("search exhausted", d.get("searchLimitsExhausted"), d.get("searchLimitsExhaustedReason"))
print("durations", d.get("stageDurationsMs"))
print("url rejects", d.get("urlRejectionReasons"))
print("fetch fails", d.get("fetchFailureReasons"))
print("providers:")
for p in d.get("discoveryProviders") or []:
    print(" ", p.get("providerName"), "stage", p.get("stageNumber"), "cands", p.get("candidatesReturned"), "err", p.get("error"), "zero", p.get("zeroYieldReason"))

print("\n=== DEPTH0 ===")
for u in sorted([x for x in urls if (x.get("depth") or 0) == 0], key=lambda z: z.get("discoveredAtUtc") or ""):
    print(u.get("status"), u.get("sourceType"), u.get("sourceName"), (u.get("url") or "")[:140])

print("\nSTATUS", Counter(u.get("status") for u in urls))
print("SOURCE", Counter(u.get("sourceType") for u in urls))
print("NAME", Counter(u.get("sourceName") for u in urls))
print("DEPTH", Counter(u.get("depth") for u in urls))
print("PURPOSE", Counter(u.get("pagePurpose") for u in urls))

hosts = Counter()
for u in urls:
    try:
        hosts[urlparse(u.get("url") or "").netloc.replace("www.", "")] += 1
    except Exception:
        pass
print("\nHOSTS")
for h, c in hosts.most_common(20):
    print(f"{c:3} {h}")

print("\n=== CRAWLED ===")
phosts = Counter()
for p in pages:
    u = p.get("finalUrl") or p.get("url") or ""
    try:
        phosts[urlparse(u).netloc.replace("www.", "")] += 1
    except Exception:
        pass
    print((p.get("title") or "")[:70], "|", u[:130])
print("PAGE HOSTS", phosts)

print("\n=== EVENT TYPES ===")
for t, c in Counter(e.get("eventType") for e in events).most_common(40):
    print(f"{c:4} {t}")
print("SEV", Counter(e.get("severity") for e in events))

print("\n=== ERROR/WARN ===")
for e in events:
    if (e.get("severity") or "").lower() in ("error", "warning", "critical"):
        print(e.get("severity"), e.get("eventType"), e.get("stage"), "|", (e.get("message") or "")[:220])

print("\n=== LIFECYCLE / QUAL / SEARCH ===")
keys = ("Started", "Completed", "Qualified", "Rejected", "CompanyExtracted", "DiscoveryCoverage", "Duplicate", "Search")
for e in events:
    t = e.get("eventType") or ""
    m = e.get("message") or ""
    if any(k.lower() in t.lower() or k.lower() in m.lower() for k in keys):
        print(t, "|", m[:240])

print("\n=== LISTING NAMES ===")
names = Counter()
for e in events:
    if e.get("eventType") != "ListingOrganizationExtracted":
        continue
    m = e.get("message") or ""
    if "Extracted organization '" in m:
        names[m.split("Extracted organization '")[1].split("' from")[0]] += 1
    else:
        names[m[:100]] += 1
for n, c in names.most_common(40):
    print(f"{c:3} {n}")

print("\n=== PROSPECTS ON RUN ===")
for p in run.get("prospects") or []:
    print(p.get("status"), p.get("qualificationScore"), p.get("companyName"), p.get("sourceUrl"), p.get("createdAtUtc"))
