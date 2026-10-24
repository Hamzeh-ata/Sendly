import json
import sys
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8')

def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)

e = load('.tmp-run-case2/events.json')
print('=== MADARES EVENTS ===')
for x in e:
    blob = ' '.join([
        str(x.get('message') or ''),
        str(x.get('url') or ''),
        str(x.get('dataJson') or ''),
        str(x.get('domain') or ''),
        str(x.get('eventType') or ''),
    ]).lower()
    if 'madares' in blob or x.get('eventType') in ('SpaShellDetected', 'DirectorySiblingQueued'):
        print(f"[{x.get('eventType')}/{x.get('stage')}] {x.get('message')}")
        print(f"  url={x.get('url')} data={x.get('dataJson')}")

print('\n=== BAD LISTING NAMES ===')
bad_hints = ['visit', 'tuition', 'register', 'admission', 'login', 'welcome', 'why choose',
             'our team', 'our location', 'our campus', 'schoology', 'powerschool', 'fees']
for x in e:
    if x.get('eventType') != 'ListingOrganizationExtracted':
        continue
    msg = x.get('message') or ''
    print(msg, '|', x.get('dataJson'))

d = load('.tmp-run-case2/discovered-urls.json')
print('\n=== MADARES URLS ===')
for x in d:
    if 'madares' in (x.get('url') or '').lower():
        print(x.get('status'), 'd='+str(x.get('depth')), 'parent=', x.get('parentUrl'),
              'reason=', x.get('discoveryReason') or x.get('sourceName'), x.get('url'))

print('\n=== PROSPECTS ===')
r = load('.tmp-run-case2/run-detail.json')
diag = r.get('searchProviderDiagnostics') or {}
print('identityVerified', diag.get('identityVerifiedBusinesses'),
      'emailReady', diag.get('emailReadyProspects'),
      'pages', r.get('pagesCrawled'), 'pending', diag.get('usefulPendingUrls'))
for p in r.get('prospects') or []:
    print('---', p.get('status'), 'score', p.get('qualificationScore'))
    print('  name:', p.get('companyName'))
    print('  email:', p.get('email'), 'site:', p.get('website'))
    print('  source:', p.get('sourceUrl'))

print('\n=== QUALIFIED EVENTS ===')
for x in e:
    if x.get('eventType') in ('Qualified', 'Rejected', 'CompanyExtracted'):
        print(x.get('eventType'), x.get('message'), x.get('dataJson'))
