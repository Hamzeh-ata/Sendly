import re
import urllib.request

def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "SendlyResearchProbe/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return resp.geturl(), resp.read().decode("utf-8", errors="replace")

print("=== NAIS school-directory ===")
final, html = fetch("https://www.nais.org/school-directory")
print("final:", final, "len:", len(html))
for hint in ["sso.nais", "login", "sign-in", "app-root", "__NEXT_DATA__", "school-search", "iframe"]:
    print(f"  contains {hint}:", hint.lower() in html.lower())
hrefs = re.findall(r'href=["\']([^"\']+)["\']', html, flags=re.I)
schoolish = [h for h in hrefs if re.search(r"school|member|profile|directory", h, re.I)]
print("schoolish hrefs sample:")
for h in schoolish[:30]:
    print(" ", h)

print("\n=== PSR california ===")
final, html = fetch("https://www.privateschoolreview.com/california")
print("final:", final, "len:", len(html))
hrefs = re.findall(r'<a[^>]*href=["\']([^"\']+)["\'][^>]*>(.*?)</a>', html, flags=re.I | re.S)
profiles = []
for href, text in hrefs:
    t = re.sub(r"<[^>]+>", "", text).strip()
    if "profile" in href.lower() or re.search(r"school", t, re.I):
        if "blog" in href.lower() or "tuition" in href.lower():
            continue
        profiles.append((href, t[:80]))
print("profile/school links:")
for h, t in profiles[:25]:
    print(f"  {h} | {t}")
