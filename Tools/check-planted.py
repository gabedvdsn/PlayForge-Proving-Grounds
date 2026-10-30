"""
Checks a run of the Planted category: every test there must have failed, each for the reason in its
Description. A planted test that passes, or fails for another reason, means a harness check has gone quiet.
"""
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
cases = [case for case in root.iter("test-case") if case.get("fullname", "").find(".PlantedFailures.") >= 0]
wrong = 0

for case in cases:
    reason = next((p.get("value") for p in case.iter("property") if p.get("name") == "Description"), None)
    message = case.find("failure/message")
    text = (message.text or "") if message is not None else ""

    if case.get("result") != "Failed":
        print(f"WRONG {case.get('name')}: {case.get('result')}, it must fail")
        wrong += 1
    elif reason is None or reason not in text:
        print(f"WRONG {case.get('name')}: failed, but not with \"{reason}\":\n    {text.strip()[:400]}")
        wrong += 1
    else:
        print(f"ok    {case.get('name')}")

if not cases:
    print("WRONG no planted tests ran")
    wrong = 1

print(f"{len(cases) - wrong} of {len(cases)} planted failures caught")
sys.exit(1 if wrong else 0)
