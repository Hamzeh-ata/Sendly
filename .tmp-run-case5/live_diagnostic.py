"""Bounded live diagnostic: fetch real US school directory pages and report extractable schools."""
import json
import re
import sys
import urllib.request
from collections import Counter
from html import unescape
from urllib.parse import urljoin, urlparse

sys.stdout.reconfigure(encoding="utf-8")

UA = "SendlyResearchDiagnostic/1.0"


def fetch(url, timeout=30):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.geturl(), resp.read().decode("utf-8", errors="replace")


def anchors(html, base):
    out = []
    for href, text in re.findall(r'<a[^>]*href=["\']([^"\']+)["\'][^>]*>(.*?)</a>', html, flags=re.I | re.S):
        label = re.sub(r"<[^>]+>", "", unescape(text)).strip()
        abs_url = urljoin(base, href)
        out.append((abs_url, label))
    return out


def is_chrome(label):
    lower = label.lower().strip()
    chrome = [
        "get direction", "join nais", "jobs at", "login", "sign in", "careers",
        "academic calendar", "preschools", "find schools", "school rankings",
        "membership", "privacy", "legal"
    ]
    if not label or len(label) < 4:
        return True
    return any(c in lower for c in chrome) or lower.startswith(("get ", "join ", "visit "))


print("=== LIVE DIAGNOSTIC: US private school discovery path ===\n")

# 1) NAIS school directory
nais_url = "https://www.nais.org/school-directory"
final, html = fetch(nais_url)
print(f"Fetched NAIS directory: {final} ({len(html)} bytes)")
nais_profiles = []
for url, label in anchors(html, final):
    if "/school-directory/school/" in url and not is_chrome(label):
        nais_profiles.append((url, label))
# unique by url
seen = set()
uniq = []
for u, l in nais_profiles:
    if u in seen:
        continue
    seen.add(u)
    uniq.append((u, l))
print(f"School profile links found: {len(uniq)}")
for u, l in uniq[:8]:
    print(f"  - {l[:60]} | {u}")

# 2) Fetch up to 3 NAIS profiles and extract official websites
print("\n=== NAIS profile -> official website ===")
us_schools = []
for profile_url, label in uniq[:3]:
    try:
        pfinal, phtml = fetch(profile_url)
    except Exception as ex:
        print(f"  FAIL {profile_url}: {ex}")
        continue
    title = re.search(r"<title[^>]*>(.*?)</title>", phtml, re.I | re.S)
    title_text = re.sub(r"\s+", " ", unescape(title.group(1))).strip() if title else label
    externals = []
    for url, _ in anchors(phtml, pfinal):
        host = urlparse(url).netloc.lower().replace("www.", "")
        if host and "nais.org" not in host and "closeup.org" not in host and "votervoice" not in host:
            if any(x in host for x in ["facebook", "instagram", "linkedin", "twitter", "x.com", "google.com", "cookiebot"]):
                continue
            externals.append(url.split("?")[0].rstrip("/"))
    # dedupe
    ext_seen = []
    for e in externals:
        if e not in ext_seen:
            ext_seen.append(e)
    has_us = "United States" in phtml or re.search(r"\b[A-Z]{2}\s+\d{5}", phtml)
    print(f"  {title_text[:70]}")
    print(f"    profile={pfinal}")
    print(f"    us_address_signal={bool(has_us)} official_candidates={ext_seen[:3]}")
    if ext_seen and has_us:
        us_schools.append({"name": title_text, "profile": pfinal, "website": ext_seen[0]})

# 3) Private School Review state listing
print("\n=== Private School Review /california ===")
psr_url = "https://www.privateschoolreview.com/california"
pfinal, phtml = fetch(psr_url)
print(f"Fetched PSR: {pfinal} ({len(phtml)} bytes)")
psr_schools = []
for url, label in anchors(phtml, pfinal):
    if url.endswith("-profile") and label and not is_chrome(label) and "View Profile" not in label:
        psr_schools.append((url, label))
seen = set()
psr_uniq = []
for u, l in psr_schools:
    if u in seen:
        continue
    seen.add(u)
    psr_uniq.append((u, l))
print(f"School profile links found: {len(psr_uniq)}")
for u, l in psr_uniq[:8]:
    print(f"  - {l[:60]} | {u}")

# 4) Country gate spot-check
print("\n=== Country path gate spot-check ===")
foreign_paths = [
    "/vn/school/unis-hanoi/",
    "/sg/school/sas/",
    "/my/school/marlborough/",
    "/ch/school/prefleuri/",
]
for path in foreign_paths:
    print(f"  foreign path {path} should be rejected for US-only query")

print("\n=== SUMMARY ===")
print(json.dumps({
    "naisProfilesFound": len(uniq),
    "naisUsSchoolsWithOfficialWebsite": len(us_schools),
    "psrCaliforniaProfilesFound": len(psr_uniq),
    "sampleUsSchools": us_schools,
    "samplePsrSchools": [{"name": l, "profile": u} for u, l in psr_uniq[:5]],
    "limitation": None if uniq and psr_uniq else "Directory access failed"
}, indent=2, ensure_ascii=False))
