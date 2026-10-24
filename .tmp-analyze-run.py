import json, sys
sys.stdout.reconfigure(errors='replace')
d = json.load(open('.tmp-thisrun-detail.json'))
for k in ['id','projectName','status','requestedProspectCount','qualifiedCount','companiesFound','contactsFound','candidatesDiscovered','candidatesAfterDeduplication','urlsDiscovered','pagesFetched','pagesQueued','error','warning','startedAtUtc','completedAtUtc','progressPercent']:
    if k in d:
        print(k, '=', d[k])
print('--- detail keys ---')
print([k for k in d.keys()])
print('--- events ---')
e = json.load(open('.tmp-thisrun-events.json'))
items = e if isinstance(e, list) else e.get('items', [])
print('event count', len(items))
from collections import Counter
print(Counter([x.get('eventType') for x in items]))
print('--- last 30 ---')
for x in items[:30]:
    print(x.get('createdAtUtc'), x.get('severity'), x.get('eventType'), '|', (x.get('message') or '')[:200])
print('--- ollama / error / blocked events ---')
for x in items:
    m = (x.get('message') or '')
    if 'llama' in m or x.get('severity') in ('Error','Critical','Warning') or 'fail' in m.lower() or 'blocked' in m.lower():
        print(x.get('createdAtUtc'), x.get('severity'), x.get('eventType'), '|', m[:240])
print('--- ollama now ---')
o = json.load(open('.tmp-ollama-now.json'))
print([m.get('name') for m in o.get('models', [])])
