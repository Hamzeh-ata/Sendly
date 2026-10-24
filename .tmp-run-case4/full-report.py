import json
import re
import sys
from collections import Counter
from urllib.parse import urlparse

sys.stdout.reconfigure(encoding="utf-8")

ev = json.load(open(".tmp-run-case4/events.json", encoding="utf-8-sig"))
if isinstance(ev, dict):
    for v in ev.values():
        if isinstance(v, list):
            ev = v
            break

names = Counter()
for e in ev:
    if e.get("eventType") != "ListingOrganizationExtracted":
        continue
    m = e.get("message") or ""
    mm = re.search(r"Extracted organization '(.+?)' from listing", m)
    if mm:
        names[mm.group(1)] += 1

print("TOP EXTRACTED ORGS", len(names))
for n, c in names.most_common(60):
    print(f"{c:3} {n}")

print("\n=== WARNING MESSAGES ===")
for e in ev:
    if e.get("eventType") == "Warning":
        print("-", (e.get("message") or "")[:160])
        p = e.get("payloadJson") or e.get("payload")
        if isinstance(p, str) and p:
            print(" ", p[:220])
        elif isinstance(p, dict):
            print(" ", json.dumps(p, ensure_ascii=False)[:220])

print("\n=== REJECTED EVENTS ===")
for e in ev:
    if e.get("eventType") != "Rejected":
        continue
    print("-", (e.get("message") or "")[:140])
    p = e.get("payloadJson") or e.get("payload")
    if isinstance(p, str) and p:
        print(" ", p[:260])

synonyms_map = {
    "cleaning": [
        "commercial cleaning", "cleaning services", "janitorial", "facility cleaning",
        "sanitation", "نظافة", "شركات نظافة", "تنظيف", "خدمات النظافة",
    ],
    "commercial cleaning": [
        "cleaning", "cleaning services", "janitorial", "facility management",
        "building cleaning", "نظافة تجارية", "شركات نظافة", "تنظيف منشآت",
    ],
    "education": [
        "school", "schools", "private school", "academy", "كلية",
        "تعليم", "مدارس", "مدرسة", "تعليم خاص",
    ],
    "school": ["education", "private school", "schools", "academy", "مدارس", "مدرسة"],
    "construction": ["contractor", "building", "engineering", "civil", "مقاولات", "إنشاءات", "بناء"],
    "hospitality": ["hotel", "hotels", "tourism", "ضيافة", "فنادق", "فندق"],
    "facility": ["facilities", "facility management", "facilities management", "FM", "إدارة مرافق", "مرافق"],
    "healthcare": ["clinic", "hospital", "medical", "صحة", "عيادة", "مستشفى"],
    "logistics": ["transport", "shipping", "freight", "نقل", "شحن", "لوجستيات"],
    "retail": ["store", "shop", "تجزئة", "متجر", "محل"],
}

inds = [
    "Private Primary School", "Fee-Paying School", "Private Elementary School",
    "Independent Senior School", "Private Boarding School", "Private High School",
    "Independent Boarding School", "Private School", "Independent All-Through School",
    "Private Secondary School", "International College", "International School",
    "Independent School", "Independent Preparatory School",
]


def matches_key(value, key):
    v = value.lower()
    k = key.lower()
    return v == k or k in v or v in k


result = []
seen = set()
for industry in inds:
    t = industry.strip()
    if t.lower() not in seen:
        seen.add(t.lower())
        result.append(t)
    for key, vals in synonyms_map.items():
        if not matches_key(t, key) and not any(matches_key(t, s) for s in vals):
            continue
        if key.lower() not in seen:
            seen.add(key.lower())
            result.append(key)
        for s in vals:
            if s.lower() not in seen:
                seen.add(s.lower())
                result.append(s)

print("\n=== PLAN INDUSTRIES ===")
print("count", len(inds))
for x in inds:
    print("-", x)
print("synonyms added (+%d)" % (len(result) - len(inds)))
for x in result:
    if x not in inds:
        print("-", x)

print("\nMinCandidatesBeforeRecovery =", max(5, 200 // 10))
print("stage1 early-stop threshold = min(3, 20) = 3")

urls = json.load(open(".tmp-run-case4/discovered-urls.json", encoding="utf-8-sig"))
if isinstance(urls, dict):
    for v in urls.values():
        if isinstance(v, list):
            urls = v
            break

print("\n=== DEPTH0 SEEDS ===")
for u in urls:
    if u.get("depth") == 0:
        print(u.get("status"), u.get("sourceName"), u.get("url"))

print("\n=== TOP PENDING BY PRIORITY ===")
pending = [u for u in urls if u.get("status") == "Pending"]
pending.sort(key=lambda x: -(x.get("priorityScore") or x.get("priority") or 0))
for u in pending[:15]:
    print(u.get("priorityScore") or u.get("priority"), u.get("url"))
