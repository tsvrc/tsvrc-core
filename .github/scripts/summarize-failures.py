import sys
import xml.etree.ElementTree as ET

failed = [tc for tc in ET.parse(sys.argv[1]).iter("test-case") if tc.get("result") == "Failed"]

if not failed:
    print("All tests passed.")

for tc in failed:
    msg = tc.find("failure/message")
    print(f"- **{tc.get('fullname')}**: {msg.text.strip() if msg is not None else ''}")
