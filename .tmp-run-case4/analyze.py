import json
import sys
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8')

def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)

r = load('.tmp-run-case4/run-detail.json')
e = load('.tmp-run-case4/events.json')
d = load('.tmp-run-case4/discovered-urls.json')
p = load('.tmp-run-case4/crawled-pages.json')
if isinstance(e, dict):
    e = e.get('value', [e])
if isinstance(d, dict):
    d = d.get('value', [d])
if isinstance(p, dict):
    p = p.get('value', [p])

diag = r.get('searchProviderDiagnostics') or {}
print('=== RUN ===')
print('id', r.get('id'))
print('status', r.get('status'))
print('project', r.get('projectName'), r.get('projectId'))
print('query', r.get('researchQueryName'), r.get('researchQueryId'))
print('pages', r.get('pagesCrawled'), 'urls', r.get('urlsDiscovered'), 'qualified', r.get('qualifiedCount'))
print('companiesFound', r.get('companiesFound'), 'rejected', r.get('rejectedCount'), 'duplicates', r.get('duplicatesFound'))
print('started', r.get('startedAtUtc'), 'completed', r.get('completedAtUtc'))
print('error', r.get('error'), 'warning', r.get('warning'))

print('\n=== DIAG ===')
keys = [
    'automaticDiscoverySummary','discoveryPlanSummary','uniqueDomainsDiscovered',
    'companiesIdentified','identityVerifiedBusinesses','emailReadyProspects',
    'publicContactsExtracted','pagesFetched','fetchBudgetLimit','usefulPendingUrls',
    'fetchesSucceeded','fetchesFailed','lastBudgetStopReason','urlsRejectedFiltered',
    'urlsAdmitted','previouslyKnownProspects','newlyDiscoveredProspects',
    'sourcesReused','sourcesAdded','recoveryAttempts'
]
for k in keys:
    print(f'{k}={diag.get(k)}')
print('urlRejectionReasons', diag.get('urlRejectionReasons'))
print('fetchFailureReasons', diag.get('fetchFailureReasons'))

print('\n=== PROSPECTS ===')
for x in r.get('prospects') or []:
    print(f"- {x.get('status')} score={x.get('qualificationScore')} email={x.get('email')}")
    print(f"  name={x.get('companyName')}")
    print(f"  site={x.get('website')}")
    print(f"  src={x.get('sourceUrl')}")

print('\n=== EVENT TYPES ===')
for k,v in Counter(x.get('eventType') for x in e).most_common(30):
    print(v, k)

print('\n=== QUALIFIED / REJECTED / COMPANY ===')
for x in e:
    if x.get('eventType') in ('Qualified','Rejected','CompanyExtracted','Started','Completed','Warning') and (
        x.get('eventType') != 'Warning' or 'prior' in (x.get('message') or '').lower() or 'deferred' in (x.get('message') or '').lower() or 'spa' in (x.get('message') or '').lower()
    ):
        print(x.get('eventType'), (x.get('message') or '')[:120])
        if x.get('dataJson'):
            print(' ', (x.get('dataJson') or '')[:220])

print('\n=== URL STATUS / SOURCE ===')
print('status', Counter(x.get('status') for x in d))
print('sourceType', Counter(x.get('sourceType') for x in d))
print('pending hosts', Counter(x.get('normalizedDomain') for x in d if x.get('status')=='Pending').most_common(15))
print('crawled hosts', Counter(x.get('normalizedDomain') for x in d if x.get('status')=='Crawled').most_common(15))

print('\n=== CRAWLED TITLES (sample) ===')
for x in sorted(p, key=lambda z: z.get('crawledAtUtc') or '')[:20]:
    print(x.get('httpStatusCode'), (x.get('title') or '')[:60], (x.get('url') or '')[:90])

print('\n=== LISTING / OFFICIAL / SPA SAMPLE ===')
for x in e:
    et = x.get('eventType') or ''
    if et in ('ListingOrganizationExtracted','OfficialWebsiteQueued','SpaShellDetected','DirectorySiblingQueued','DirectoryCategoryDiscovered'):
        print(f"[{et}] {(x.get('message') or '')[:130]}")
