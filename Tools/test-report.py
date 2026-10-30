"""
Prints a Unity Test Framework results file: the totals, then anything that failed. Tests in the Red category
pin bugs not fixed yet, so they're listed apart: a red test failing is expected, and anything else failing is new.
A red test that passes means its bug is fixed and it can lose the category.
"""
import sys
import xml.etree.ElementTree as ET


def walk(node, red=False):
    """Yields every test case with whether it's red. A parameterized test's category sits on its parent."""
    red = red or any(p.get("name") == "Category" and p.get("value") == "Red" for p in node.findall("properties/property"))
    if node.tag == "test-case":
        yield node, red
    for child in node:
        yield from walk(child, red)


def short_name(case):
    return case.get("fullname").split(".Tests.")[-1]


root = ET.parse(sys.argv[1]).getroot()
print({key: root.get(key) for key in ("result", "total", "passed", "failed", "skipped")})

cases = list(walk(root))
red_failed = [case for case, red in cases if red and case.get("result") == "Failed"]
red_passed = [case for case, red in cases if red and case.get("result") == "Passed"]
new_failed = [case for case, red in cases if not red and case.get("result") == "Failed"]

if red_failed:
    print(f"{len(red_failed)} known red:")
    for case in red_failed:
        print(f"  {short_name(case)}")

for case in red_passed:
    print(f"RED TEST NOW PASSES, remove its category: {short_name(case)}")

if new_failed:
    print(f"{len(new_failed)} NEW FAILURES:")
    for case in new_failed:
        message = case.find("failure/message")
        print(f"- {short_name(case)}")
        if message is not None and message.text:
            print("    " + message.text.strip()[:600].replace("\n", "\n    "))
