"""Prints a Unity Test Framework results file: the totals, then every test that didn't pass."""
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
print({key: root.get(key) for key in ("result", "total", "passed", "failed", "skipped")})

for case in root.iter("test-case"):
    if case.get("result") in ("Passed", "Skipped"):
        continue

    message = case.find("failure/message")
    print(f"- {case.get('fullname')}: {case.get('result')}")
    if message is not None and message.text:
        print("    " + message.text.strip()[:600].replace("\n", "\n    "))
