import json
import collections
from pathlib import Path

ROOT = Path(r"c:\Users\User\source\repos\BulkMail")
OUT = ROOT / ".tmp-parse-dumps-out2.txt"


def load(p):
    with open(p, encoding="utf-8-sig") as f:
        return json.load(f)


def dump_listing_orgs(label, path, fh):
    evs = load(path)
    names = collections.Counter()
    for e in evs:
        if e.get("eventType") != "ListingOrganizationExtracted":
            continue
        dj = e.get("dataJson") or "{}"
        try:
            obj = json.loads(dj)
        except Exception:
            obj = {}
        names[obj.get("name") or e.get("message")] += 1
    print(label, "listing org names", len(names), "events", sum(names.values()), file=fh)
    junk_needles = [
        "advanced search",
        "academia",
        "government",
        "important information",
        "make offer",
        "terms",
        "privacy",
        "legal",
        "pricing",
        "api calls",
        "who we are",
        "offer must",
        "marketplace",
        "spaceship",
        "comments",
        "feed",
        "newsletter",
        "partners",
        "case studies",
        "purpose",
        "impact",
        "contact",
    ]
    junk = []
    for name, c in names.most_common():
        lname = (name or "").lower()
        is_junk = any(n in lname for n in junk_needles)
        if is_junk:
            junk.append((c, name))
    print("junk-like extracted orgs:", len(junk), "of", len(names), file=fh)
    for c, name in junk[:80]:
        print(f"  {c}x {name}", file=fh)
    print("top 40 listing names:", file=fh)
    for name, c in names.most_common(40):
        print(f"  {c}x {name}", file=fh)


def dump_source_elig(label, path, fh):
    evs = load(path)
    print("=" * 80, file=fh)
    print(label, "SourceEligibilityEvaluated", file=fh)
    states = collections.Counter()
    interesting = []
    for e in evs:
        if e.get("eventType") != "SourceEligibilityEvaluated":
            continue
        dj = json.loads(e.get("dataJson") or "{}")
        states[dj.get("state")] += 1
        name = (dj.get("sourceName") or "") + " " + (dj.get("sourceUrl") or "")
        blob = json.dumps(dj, ensure_ascii=False).lower()
        if any(k in blob for k in ["opencorporates", "common crawl", "0007", "yellowpages.ae", "dubai"]):
            interesting.append(dj)
        print(
            f"  {dj.get('state')} | {dj.get('sourceName')} | {dj.get('sourceKind')} | {dj.get('reasonCode')} | {dj.get('explanation')}",
            file=fh,
        )
    print("states", dict(states), file=fh)
    print("interesting:", file=fh)
    for dj in interesting:
        print(json.dumps(dj, ensure_ascii=False)[:500], file=fh)


def dump_company_extracted(label, path, fh):
    evs = load(path)
    print("=" * 80, file=fh)
    print(label, "CompanyExtracted", file=fh)
    for e in evs:
        if e.get("eventType") != "CompanyExtracted":
            continue
        print(e.get("message"), e.get("dataJson"), file=fh)


def dump_discovery_decision(label, path, fh):
    evs = load(path)
    print("=" * 80, file=fh)
    for e in evs:
        if e.get("eventType") in ("DiscoveryCoverageDecision", "Started", "Completed"):
            print(e.get("eventType"), e.get("message"), file=fh)
            print(e.get("dataJson"), file=fh)


def main():
    with open(OUT, "w", encoding="utf-8") as fh:
        dump_listing_orgs("this-run", ROOT / ".tmp-this-run" / "events.json", fh)
        dump_listing_orgs("this-run2", ROOT / ".tmp-this-run2" / "events.json", fh)
        dump_source_elig("this-run", ROOT / ".tmp-this-run" / "events.json", fh)
        dump_source_elig("this-run2", ROOT / ".tmp-this-run2" / "events.json", fh)
        dump_company_extracted("this-run", ROOT / ".tmp-this-run" / "events.json", fh)
        dump_company_extracted("this-run2", ROOT / ".tmp-this-run2" / "events.json", fh)
        dump_discovery_decision("this-run", ROOT / ".tmp-this-run" / "events.json", fh)
        dump_discovery_decision("this-run2", ROOT / ".tmp-this-run2" / "events.json", fh)

        # discovery sources selected
        src = load(ROOT / ".tmp-this-run" / "discovery-sources.json")
        print("=" * 80, file=fh)
        print("discovery-sources count", len(src), file=fh)
        attempted = [s for s in src if (s.get("attemptCount") or 0) > 0]
        print("attempted", len(attempted), file=fh)
        for s in attempted:
            print(
                s.get("name"),
                s.get("baseUrl"),
                "attempts",
                s.get("attemptCount"),
                "success",
                s.get("successCount"),
                "cands",
                s.get("candidatesDiscovered"),
                "last",
                s.get("lastAttemptedAtUtc"),
                file=fh,
            )
        uae = [s for s in src if "united arab" in (s.get("countriesJson") or "").lower() or "uae" in (s.get("countriesJson") or "").lower()]
        print("UAE-tagged sources", len(uae), file=fh)
        for s in uae:
            print(" ", s.get("name"), s.get("baseUrl"), s.get("industriesJson"), file=fh)


if __name__ == "__main__":
    main()
    print("wrote", OUT)
