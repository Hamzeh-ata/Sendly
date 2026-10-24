import json
import sys
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8')

def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)

r = load('.tmp-run-case3/run-detail.json')
raw_e = load('.tmp-run-case3/events.json')
e = raw_e if isinstance(raw_e, list) else raw_e.get('value', raw_e)
if isinstance(e, dict):
    e = [e]
d = load('.tmp-run-case3/discovered-urls.json')
if isinstance(d, dict) and 'value' in d:
    d = d['value']

print('project', r.get('projectName'), r.get('projectId'))
print('query', r.get('researchQueryName'), r.get('researchQueryId'))
print('countries', r.get('countries') or r.get('targetCountries'))
print('industries', r.get('industries') or r.get('targetIndustries'))
# dump top-level keys briefly
for k in sorted(r.keys()):
    v = r[k]
    if k in ('prospects','searchProviderDiagnostics','events'): 
        continue
    if isinstance(v, (str,int,float,bool)) or v is None:
        print(f'  {k}={v}')
    elif isinstance(v, list) and len(v) < 20 and all(isinstance(i,(str,int)) for i in v):
        print(f'  {k}={v}')

diag = r.get('searchProviderDiagnostics') or {}
print('\ndiag summary:', diag.get('automaticDiscoverySummary'))
print('plan:', diag.get('discoveryPlanSummary'))
print('uniqueDomains', diag.get('uniqueDomainsDiscovered'))

print('\nEVENT TYPES')
if e and isinstance(e[0], dict):
    for k,v in Counter(x.get('eventType') for x in e).most_common(25):
        print(v, k)
else:
    print('events type unexpected', type(e), type(e[0]) if e else None)

print('\nMADARES')
madares = [x for x in d if 'madares' in (x.get('url') or '').lower()]
print('madares urls', len(madares), Counter(x.get('status') for x in madares))
for x in madares[:25]:
    print(x.get('status'), x.get('depth'), x.get('discoveryReason') or x.get('sourceName'), x.get('url')[:120])

print('\nTOP DOMAINS CRAWLED/PENDING')
print('pending', Counter(x.get('normalizedDomain') for x in d if x.get('status')=='Pending').most_common(20))
print('crawled', Counter(x.get('normalizedDomain') for x in d if x.get('status')=='Crawled').most_common(20))

print('\nHOST COUNTRY-ISH')
sa = sum(1 for x in d if (x.get('normalizedDomain') or '').endswith('.sa') or '.sa.' in (x.get('normalizedDomain') or ''))
edu_sa = sum(1 for x in d if (x.get('normalizedDomain') or '').endswith('.edu.sa'))
us = sum(1 for x in d if (x.get('normalizedDomain') or '').endswith('.edu') or (x.get('normalizedDomain') or '').endswith('.org') or (x.get('normalizedDomain') or '').endswith('.com'))
print('sa-ish', sa, 'edu.sa', edu_sa, 'total', len(d))

print('\nBAD EMAIL / NAME EVENTS')
for x in (e if e and isinstance(e[0], dict) else []):
    if x.get('eventType') in ('Qualified','Rejected','CompanyExtracted','ContactExtracted'):
        print(x.get('eventType'), (x.get('message') or '')[:100], (x.get('dataJson') or '')[:160])
