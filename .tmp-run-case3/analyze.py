import json
import sys
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8')

def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)

r = load('.tmp-run-case3/run-detail.json')
d = load('.tmp-run-case3/discovered-urls.json')
e = load('.tmp-run-case3/events.json')
p = load('.tmp-run-case3/crawled-pages.json')

diag = r.get('searchProviderDiagnostics') or {}
print('=== RUN ===')
print('status', r.get('status'), 'pages', r.get('pagesCrawled'), 'urls', r.get('urlsDiscovered'),
      'qualified', r.get('qualifiedCount'), 'companies', r.get('companiesFound'))
print('identityVerified', diag.get('identityVerifiedBusinesses'),
      'emailReady', diag.get('emailReadyProspects'),
      'contacts', diag.get('publicContactsExtracted'),
      'pending', diag.get('usefulPendingUrls'),
      'fetches', diag.get('fetchesSucceeded'), '/', diag.get('fetchBudgetLimit'))
print('stop', diag.get('lastBudgetStopReason'), diag.get('zeroYieldReason'))

print('\n=== PROSPECTS ===')
for x in r.get('prospects') or []:
    print(f"- {x.get('status')} score={x.get('qualificationScore')} email={x.get('email')}")
    print(f"  name={x.get('companyName')}")
    print(f"  site={x.get('website')} src={x.get('sourceUrl')}")

print('\n=== EVENT TYPES ===')
for k,v in Counter(x.get('eventType') for x in e).most_common(30):
    print(v, k)

print('\n=== MADARES / SPA / SIBLING / BAD LISTINGS ===')
for x in e:
    et = x.get('eventType') or ''
    msg = x.get('message') or ''
    url = x.get('url') or ''
    blob = f"{et} {msg} {url} {x.get('dataJson') or ''}".lower()
    if any(k in blob for k in ['madares', 'spashell', 'sibling', 'listingorganization',
                               'directorycategory', 'officialwebsite', 'qualified', 'rejected']):
        if et in ('ListingOrganizationExtracted', 'OfficialWebsiteQueued') and 'madares' not in blob and 'login' not in blob and 'visit' not in blob and 'tuition' not in blob and 'register' not in blob and 'بحث' not in msg and 'تسجيل' not in msg:
            continue
        print(f"[{et}] {msg[:140]}")
        if x.get('dataJson') and et in ('ListingOrganizationExtracted','Rejected','Qualified','SpaShellDetected','DirectorySiblingQueued'):
            print(' ', x.get('dataJson')[:200])

print('\n=== MADARES URLS ===')
madares = [x for x in d if 'madares' in (x.get('url') or '').lower()]
print('count', len(madares))
print('status', Counter(x.get('status') for x in madares))
for x in sorted(madares, key=lambda z: (z.get('depth') or 0, z.get('url') or ''))[:40]:
    print(x.get('status'), f"d={x.get('depth')}", (x.get('discoveryReason') or x.get('sourceName') or '')[:50], x.get('url'))

print('\n=== PENDING HOSTS ===')
for host,c in Counter(x.get('normalizedDomain') for x in d if x.get('status')=='Pending').most_common(15):
    print(c, host)

print('\n=== CRAWLED SAMPLE ===')
for x in sorted(p, key=lambda z: z.get('crawledAtUtc') or '')[:15]:
    print(x.get('httpStatusCode'), x.get('title'), x.get('url')[:100])

print('\n=== QUALIFIED/REJECTED EVENTS ===')
for x in e:
    if x.get('eventType') in ('Qualified','Rejected','CompanyExtracted'):
        print(x.get('eventType'), x.get('message'), x.get('dataJson'))
