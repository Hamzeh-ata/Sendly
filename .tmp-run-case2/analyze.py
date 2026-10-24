import json
from collections import Counter

def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)

e = load('.tmp-run-case2/events.json')
print('events', len(e))
print('eventTypes', Counter(x.get('eventType') for x in e).most_common(30))
print('\n--- madares / spa / sibling / listing ---')
for x in e:
    et = (x.get('eventType') or '')
    blob = ' '.join([
        str(x.get('message') or ''),
        str(x.get('url') or ''),
        str(x.get('dataJson') or ''),
        str(x.get('domain') or ''),
        et,
    ]).lower()
    if any(k in blob for k in [
        'madares', 'spashell', 'sibling', 'listingorganization',
        'directorycategory', 'organizationprofile', 'investigationfollow',
        'officialwebsite', 'spa_shell', 'directory_sibling'
    ]):
        print(f"[{et}/{x.get('stage')}] {x.get('message')} url={x.get('url')}")

print('\n--- rejects ---')
for x in e:
    if x.get('eventType') == 'Rejected':
        print(x.get('message'), x.get('dataJson'))

print('\n--- qualified ---')
for x in e:
    if x.get('eventType') == 'Qualified':
        print(x.get('message'), x.get('dataJson'))

d = load('.tmp-run-case2/discovered-urls.json')
print('\nurls', len(d))
print('status', Counter(x.get('status') for x in d))
print('sourceType', Counter(x.get('sourceType') for x in d))
print('\n--- madares urls ---')
for x in d:
    if 'madares' in (x.get('url') or '').lower():
        print(
            x.get('status'),
            'd=' + str(x.get('depth')),
            'parent=' + str(x.get('parentUrl')),
            'reason=' + str(x.get('discoveryReason') or x.get('sourceName')),
            x.get('url'),
        )

print('\n--- pending hosts ---')
for host, c in Counter(x.get('normalizedDomain') for x in d if x.get('status') == 'Pending').most_common(15):
    print(c, host)

print('\n--- investigation-ish source names ---')
for x in d:
    sn = ((x.get('sourceName') or '') + ' ' + (x.get('discoveryReason') or '')).lower()
    if any(k in sn for k in ['sibling', 'investigation', 'spa', 'directory']):
        print(x.get('status'), x.get('sourceName'), x.get('url')[:140])

r = load('.tmp-run-case2/run-detail.json')
print('\nrun status', r.get('status'), 'pages', r.get('pagesCrawled'), 'qualified', r.get('qualifiedCount'))
for p in r.get('prospects') or []:
    print('---')
    print(p.get('status'), 'score', p.get('qualificationScore'), 'email', p.get('email'))
    print('name:', p.get('companyName'))
    print('site:', p.get('website'), 'src:', p.get('sourceUrl'))
