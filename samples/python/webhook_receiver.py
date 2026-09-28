"""Tiny webhook receiver that verifies Campaign Engine signatures (standard library only).

    CAMPAIGN_WEBHOOK_SECRET=<secret from POST /api/v1/webhooks> python webhook_receiver.py

Signature: X-Campaign-Signature = "sha256=" + hex(HMAC_SHA256(secret, f"{timestamp}.{raw_body}"))
"""

import hashlib
import hmac
import json
import os
import time
from http.server import BaseHTTPRequestHandler, HTTPServer

SECRET = os.environ.get("CAMPAIGN_WEBHOOK_SECRET", "")
MAX_AGE_SECONDS = 300


def verify(secret: str, timestamp: str, body: bytes, signature: str) -> bool:
    if not timestamp.isdigit() or abs(time.time() - int(timestamp)) > MAX_AGE_SECONDS:
        return False  # stale or missing timestamp: possible replay
    expected = "sha256=" + hmac.new(secret.encode(), f"{timestamp}.".encode() + body, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, signature)


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):  # noqa: N802 (http.server naming)
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        ok = verify(
            SECRET,
            self.headers.get("X-Campaign-Timestamp", ""),
            body,
            self.headers.get("X-Campaign-Signature", ""),
        )
        if not ok:
            self.send_response(401)
            self.end_headers()
            return

        event = json.loads(body)
        print(f"{event['type']} {event['data'].get('code')} -> refresh the local snapshot")
        # Deliveries are retried until a 2xx arrives, so make handling idempotent
        # (X-Campaign-Delivery is unique per delivery).
        self.send_response(204)
        self.end_headers()


if __name__ == "__main__":
    if not SECRET:
        raise SystemExit("Set CAMPAIGN_WEBHOOK_SECRET")
    HTTPServer(("0.0.0.0", 8085), Handler).serve_forever()
