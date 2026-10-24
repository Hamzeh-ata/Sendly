import json
from collections import Counter, defaultdict
from pathlib import Path
from urllib.parse import urlparse

out = Path(__file__).parent


def load(name):
    return json.loads((out / name).read_text(encoding="utf-8-sig"))


run = load("run-detail.json")
project = load("project.json")
queries = load("queries-list.json")
discovered = load("discovered-urls.json")
crawled = load("crawled-pages.json")
events = load("events.json")
errors = load("errors.json")
prospects = load("prospects.json")

run_id = run["id"]
project_id = run["projectId"]
query_id = run.get("researchQueryId")

print("=" * 72)
print("RESEARCH RUN FULL REPORT")
print("=" * 72)

print("\n--- RUN ---")
fields = [
    "id",
    "projectId",
    "projectName",
    "researchQueryId",
    "researchQueryName",
    "researchQueryVersionId",
    "status",
    "progressPercent",
    "requestedProspectCount",
    "includeEmail",
    "includeWhatsApp",
    "minQualificationScore",
    "urlsDiscovered",
    "pagesCrawled",
    "crawlErrors",
    "companiesFound",
    "contactsFound",
    "validEmails",
    "validWhatsApp",
    "duplicatesFound",
    "rejectedCount",
    "qualifiedCount",
    "searchQueriesExecuted",
    "searchResultsReceived",
    "candidatesDiscovered",
    "candidatesAfterDeduplication",
    "candidatesValidated",
    "prospectsSaved",
    "prospectsSkippedAsDuplicates",
    "researchLimitsReached",
    "researchLimitsReachedReason",
    "error",
    "warning",
    "createdAtUtc",
    "startedAtUtc",
    "completedAtUtc",
]
for k in fields:
    print(f"{k}: {run.get(k)}")

print("\n--- REJECTION REASONS ---")
print(json.dumps(run.get("rejectionReasons"), indent=2, ensure_ascii=False))

print("\n--- SEARCH PROVIDER DIAGNOSTICS ---")
print(json.dumps(run.get("searchProviderDiagnostics"), indent=2, ensure_ascii=False))

print("\n--- PROJECT ---")
proj_keys_skip = {"prospectCriteria", "templateBody", "bodyHtml", "body"}
for k, v in project.items():
    if k in proj_keys_skip:
        continue
    if isinstance(v, (dict, list)) and k not in (
        "targetCountries",
        "targetIndustries",
        "keywords",
        "excludeKeywords",
        "companySizes",
        "languages",
    ):
        if isinstance(v, list):
            print(f"{k}: list[{len(v)}]")
        else:
            print(f"{k}: object keys={list(v.keys())[:20]}")
    else:
        s = json.dumps(v, ensure_ascii=False) if isinstance(v, (dict, list)) else v
        text = str(s)
        if len(text) > 500:
            text = text[:500] + "..."
        print(f"{k}: {text}")

if "criteria" in project:
    print("\n--- PROJECT CRITERIA ---")
    print(json.dumps(project.get("criteria"), indent=2, ensure_ascii=False)[:3000])

for ck in ["prospectCriteria", "qualificationCriteria", "searchCriteria", "targetProfile"]:
    if ck in project and project[ck]:
        print(f"\n--- PROJECT.{ck} ---")
        print(json.dumps(project[ck], indent=2, ensure_ascii=False)[:4000])

print("\n--- RESEARCH QUERIES LIST ---")
print(json.dumps(queries, indent=2, ensure_ascii=False))

matched_query = None
if isinstance(queries, list):
    for q in queries:
        if q.get("id") == query_id:
            matched_query = q
            break

if query_id:
    import urllib.request

    url = f"http://localhost:5088/api/projects/{project_id}/research-queries/{query_id}"
    try:
        with urllib.request.urlopen(url) as resp:
            matched_query = json.loads(resp.read().decode("utf-8"))
            (out / "query-detail.json").write_text(
                json.dumps(matched_query, indent=2, ensure_ascii=False), encoding="utf-8"
            )
    except Exception as ex:
        print(f"query detail fetch failed: {ex}")

print("\n--- RESEARCH QUERY DETAIL ---")
if matched_query:
    print(json.dumps(matched_query, indent=2, ensure_ascii=False)[:8000])
else:
    print("not found")

print("\n--- DISCOVERED URLS ---")
print(f"count: {len(discovered)}")
status_c = Counter()
source_c = Counter()
domain_c = Counter()
for u in discovered:
    status_c[u.get("status") or u.get("state") or "?"] += 1
    source_c[u.get("source") or u.get("discoverySource") or u.get("provider") or "?"] += 1
    url = u.get("url") or u.get("normalizedUrl") or ""
    try:
        domain_c[urlparse(url).netloc.lower()] += 1
    except Exception:
        pass
print("by status:", dict(status_c))
print("by source:", dict(source_c))
print("top domains:")
for d, c in domain_c.most_common(25):
    print(f"  {d}: {c}")

print("\nSample discovered URLs (first 40):")
for u in discovered[:40]:
    print(
        f"  - {(u.get('url') or '')[:120]} | status={u.get('status') or u.get('state')} | source={u.get('source') or u.get('discoverySource') or u.get('provider')} | query={u.get('searchQuery') or u.get('query') or u.get('discoveredByQuery') or ''}"
    )

search_queries = Counter()
for u in discovered:
    q = u.get("searchQuery") or u.get("query") or u.get("discoveredByQuery") or u.get("sourceQuery")
    if q:
        search_queries[q] += 1

print("\n--- SEARCH QUERIES FROM DISCOVERED URLS ---")
if search_queries:
    for q, c in search_queries.most_common():
        print(f"  [{c}] {q}")
else:
    print("no searchQuery field on discovered urls; keys sample:", list(discovered[0].keys()) if discovered else [])

print("\n--- CRAWLED PAGES ---")
print(f"count: {len(crawled)}")
crawl_status = Counter()
crawl_domain = Counter()
for p in crawled:
    crawl_status[p.get("status") or p.get("fetchStatus") or p.get("outcome") or "?"] += 1
    url = p.get("url") or p.get("finalUrl") or ""
    try:
        crawl_domain[urlparse(url).netloc.lower()] += 1
    except Exception:
        pass
print("by status:", dict(crawl_status))
print("top domains:")
for d, c in crawl_domain.most_common(20):
    print(f"  {d}: {c}")

print("\nAll crawled pages:")
for p in crawled:
    title = (p.get("title") or "")[:80]
    print(
        f"  - {(p.get('url') or '')[:140]} | status={p.get('status') or p.get('fetchStatus')} | http={p.get('httpStatus') or p.get('statusCode')} | emails={p.get('emailsFound') or p.get('emailCount')} | title={title}"
    )
    if crawled and crawled.index(p) == 0:
        print("    keys:", list(p.keys()))

print("\n--- EVENTS ---")
print(f"count: {len(events)}")
sev = Counter()
stage = Counter()
etype = Counter()
for e in events:
    sev[e.get("severity") or "?"] += 1
    stage[e.get("stage") or "?"] += 1
    etype[e.get("eventType") or e.get("type") or "?"] += 1
print("by severity:", dict(sev))
print("by stage:", dict(stage))
print("by eventType (top 30):")
for t, c in etype.most_common(30):
    print(f"  {t}: {c}")

print("\nSearch-related events:")
for e in events:
    t = (e.get("eventType") or e.get("type") or "").lower()
    msg = e.get("message") or e.get("summary") or ""
    if "search" in t or "query" in t or "searx" in t.lower() or "brave" in t.lower() or "discover" in t:
        detail = e.get("detail") or e.get("payload") or e.get("data") or ""
        if isinstance(detail, (dict, list)):
            detail = json.dumps(detail, ensure_ascii=False)[:300]
        else:
            detail = str(detail)[:300]
        print(f"  [{e.get('severity')}] {e.get('stage')} / {e.get('eventType')}: {msg[:200]} | {detail}")

print("\nWarning/Error events:")
for e in events:
    if (e.get("severity") or "").lower() in ("warning", "error", "critical"):
        msg = e.get("message") or e.get("summary") or ""
        print(f"  [{e.get('severity')}] {e.get('stage')} / {e.get('eventType')}: {msg[:300]}")

print("\n--- ERROR GROUPS ---")
print(json.dumps(errors, indent=2, ensure_ascii=False)[:5000])

print("\n--- PROSPECTS ---")
print(f"all project prospects fetched: {len(prospects)}")
run_prospects = []
for p in prospects:
    if p.get("researchRunId") == run_id or p.get("sourceResearchRunId") == run_id:
        run_prospects.append(p)
embedded = run.get("prospects") or []
print(f"prospects linked to run via list filter fields: {len(run_prospects)}")
print(f"prospects embedded in run detail: {len(embedded)}")
use = run_prospects or embedded
print(f"using: {len(use)}")
if use:
    print("keys:", list(use[0].keys()))
    qual = Counter(p.get("qualification") or p.get("qualificationStatus") or p.get("status") for p in use)
    print("by qualification/status:", dict(qual))
    print("\nProspects:")
    for p in use[:100]:
        name = p.get("companyName") or p.get("name") or "?"
        email = p.get("email") or p.get("primaryEmail") or ""
        wa = p.get("whatsApp") or p.get("whatsapp") or p.get("phone") or ""
        score = p.get("qualificationScore") or p.get("score")
        print(
            f"  - {name} | email={email} | contact={wa} | score={score} | status={p.get('status')} | country={p.get('country')} | industry={p.get('industry')} | website={p.get('website') or p.get('websiteUrl')}"
        )

print("\nDONE")
