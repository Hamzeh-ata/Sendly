import re
import urllib.request

def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "SendlyResearchProbe/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return resp.geturl(), resp.read().decode("utf-8", errors="replace")

print("=== NAIS school profile ===")
final, html = fetch("https://www.nais.org/school-directory/school/40748")
print("final:", final, "len:", len(html))
title = re.search(r"<title[^>]*>(.*?)</title>", html, re.I | re.S)
print("title:", re.sub(r"\s+", " ", title.group(1)) if title else None)
# external links
hrefs = re.findall(r'href=["\'](https?://[^"\']+)["\']', html, flags=re.I)
external = [h for h in hrefs if "nais.org" not in h.lower() and "sso." not in h.lower()]
print("external hrefs:")
for h in external[:20]:
    print(" ", h)
emails = re.findall(r"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", html)
print("emails:", emails[:10])
# address-ish
for pat in [r"United States", r"USA", r"\b[A-Z]{2}\s+\d{5}", r"address", r"city"]:
    print(f"contains {pat}:", bool(re.search(pat, html, re.I)))

print("\n=== PSR profile ===")
final, html = fetch("https://www.privateschoolreview.com/action-day-elementary-school-profile")
print("final:", final, "len:", len(html))
title = re.search(r"<title[^>]*>(.*?)</title>", html, re.I | re.S)
print("title:", re.sub(r"\s+", " ", title.group(1)) if title else None)
hrefs = re.findall(r'href=["\'](https?://[^"\']+)["\']', html, flags=re.I)
external = [h for h in hrefs if "privateschoolreview" not in h.lower() and "publicschoolreview" not in h.lower()]
print("external hrefs:")
for h in external[:25]:
    print(" ", h)
emails = re.findall(r"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", html)
print("emails:", emails[:10])
text_bits = re.findall(r"(Website|Address|Location|California|CA)[^<]{0,80}", html, re.I)
print("location bits:", text_bits[:15])
