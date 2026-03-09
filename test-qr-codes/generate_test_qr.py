#!/usr/bin/env python3
"""
Generate valid SmartLog QR codes with HMAC-SHA256 signatures.
Format: SMARTLOG:{studentId}:{timestamp}:{hmacBase64}
"""

import hmac
import hashlib
import base64
import time
import json
from datetime import datetime

# Default HMAC secret for testing
HMAC_SECRET = "smartlog-test-secret-2026"

def generate_hmac(student_id: str, timestamp: str, secret: str) -> str:
    """Generate HMAC-SHA256 signature for QR code."""
    message = f"{student_id}:{timestamp}"
    signature = hmac.new(
        secret.encode('utf-8'),
        message.encode('utf-8'),
        hashlib.sha256
    ).digest()
    return base64.b64encode(signature).decode('utf-8')

def generate_qr_payload(student_id: str, secret: str) -> str:
    """Generate complete QR code payload with valid HMAC."""
    timestamp = str(int(time.time()))
    hmac_sig = generate_hmac(student_id, timestamp, secret)
    return f"SMARTLOG:{student_id}:{timestamp}:{hmac_sig}"

# Test students
students = [
    {"id": "STU-2026-001", "name": "Juan Dela Cruz", "grade": "Grade 11", "section": "A"},
    {"id": "STU-2026-002", "name": "Maria Santos", "grade": "Grade 11", "section": "B"},
    {"id": "STU-2026-003", "name": "Pedro Reyes", "grade": "Grade 12", "section": "A"},
    {"id": "STU-2026-004", "name": "Ana Lopez", "grade": "Grade 12", "section": "B"},
    {"id": "STU-2026-005", "name": "Carlos Garcia", "grade": "Grade 10", "section": "C"},
]

# Generate QR code payloads
print("Generated Test QR Code Payloads:")
print("=" * 80)
print(f"HMAC Secret: {HMAC_SECRET}")
print("=" * 80)

qr_codes = []
for student in students:
    payload = generate_qr_payload(student["id"], HMAC_SECRET)
    qr_codes.append({
        "student": student,
        "payload": payload
    })
    print(f"\n{student['name']} ({student['id']})")
    print(f"Grade: {student['grade']} {student['section']}")
    print(f"Payload: {payload}")
    print("-" * 80)

# Save to JSON for HTML generation
with open('/tmp/test_qr_codes.json', 'w') as f:
    json.dump(qr_codes, f, indent=2)

print("\n✓ Saved to /tmp/test_qr_codes.json")
