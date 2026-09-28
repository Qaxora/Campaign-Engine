"""Minimal Campaign Engine client for Python 3.9+ using only the standard library.

    from campaign_client import CampaignEngineClient
    client = CampaignEngineClient("http://localhost:5080", "dev-pos-key")
    result = client.evaluate(cart)

Money values come back as JSON numbers; they are parsed as ``Decimal`` so no
cent is lost to floating point.
"""

from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from decimal import Decimal
from typing import Any, Optional


class CampaignEngineError(Exception):
    """Non-2xx response. ``problem`` is the RFC 9457 problem-details body."""

    def __init__(self, status: int, problem: dict[str, Any]):
        self.status = status
        self.problem = problem
        self.errors: list[str] = problem.get("errors", [])
        super().__init__(f"{status} {problem.get('title')}: {problem.get('detail')}")


def _default(value: Any) -> Any:
    if isinstance(value, Decimal):
        # Keep full precision; the API accepts numbers as strings too.
        return str(value)
    raise TypeError(f"Cannot serialize {type(value).__name__}")


class CampaignEngineClient:
    def __init__(self, base_url: str, api_key: str, timeout: float = 10.0):
        self.base_url = base_url.rstrip("/") + "/api/v1"
        self.api_key = api_key
        self.timeout = timeout

    # -- channel ---------------------------------------------------------

    def evaluate(self, cart: dict[str, Any], explain: bool = False) -> dict[str, Any]:
        return self._request("POST", f"/evaluate?explain={str(explain).lower()}", cart)

    def redeem(self, transaction_id: str, cart: dict[str, Any]) -> dict[str, Any]:
        """Confirms a completed sale. Safe to retry with the same transaction id."""
        return self._request("POST", "/redemptions", {"transactionId": transaction_id, "cart": cart})

    def reverse(self, transaction_id: str, reason: Optional[str] = None) -> dict[str, Any]:
        tid = urllib.parse.quote(transaction_id, safe="")
        return self._request("POST", f"/redemptions/{tid}/reverse", {"reason": reason})

    def import_offline(self, sale: dict[str, Any]) -> dict[str, Any]:
        return self._request("POST", "/redemptions/offline", sale)

    def snapshot(self, etag: Optional[str] = None) -> tuple[Optional[dict[str, Any]], Optional[str]]:
        """Returns (snapshot, etag); snapshot is None when nothing changed since ``etag``."""
        headers = {"If-None-Match": etag} if etag else {}
        try:
            body, response_headers = self._raw("GET", "/snapshot", None, headers)
        except CampaignEngineError as error:
            if error.status == 304:
                return None, etag
            raise
        return body, response_headers.get("ETag")

    # -- management (admin key) -----------------------------------------

    def create_campaign(self, campaign: dict[str, Any]) -> dict[str, Any]:
        return self._request("POST", "/campaigns", campaign)

    def activate_campaign(self, campaign_id: str, force: bool = False) -> dict[str, Any]:
        return self._request("POST", f"/campaigns/{campaign_id}/activate?force={str(force).lower()}")

    def conflicts(self, candidate: dict[str, Any]) -> list[dict[str, Any]]:
        return self._request("POST", "/campaigns/conflicts", candidate)

    def replace_skus(self, list_code: str, skus: list[str]) -> dict[str, Any]:
        code = urllib.parse.quote(list_code, safe="")
        return self._request("PUT", f"/product-lists/{code}/skus", {"skus": skus})

    # -- plumbing --------------------------------------------------------

    def _request(self, method: str, path: str, body: Any = None) -> Any:
        return self._raw(method, path, body, {})[0]

    def _raw(self, method: str, path: str, body: Any, extra_headers: dict[str, str]):
        data = None if body is None else json.dumps(body, default=_default).encode("utf-8")
        headers = {"X-Api-Key": self.api_key, "Accept": "application/json", **extra_headers}
        if data is not None:
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(self.base_url + path, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                payload = response.read()
                parsed = json.loads(payload, parse_float=Decimal) if payload else None
                return parsed, dict(response.headers)
        except urllib.error.HTTPError as error:
            payload = error.read()
            try:
                problem = json.loads(payload) if payload else {}
            except ValueError:
                problem = {"title": error.reason, "detail": payload.decode("utf-8", "replace")}
            raise CampaignEngineError(error.code, problem) from None


if __name__ == "__main__":
    client = CampaignEngineClient("http://localhost:5080", "dev-pos-key")
    cart = {
        "channel": "web",
        "customer": {"id": "C-1001", "segments": ["gold"]},
        "couponCodes": ["WELCOME10"],
        "lines": [
            {"lineId": "1", "sku": "TS-001-M", "quantity": 3, "unitPrice": Decimal("299.90"), "categories": ["apparel-tshirt"]},
        ],
        "shippingAmount": Decimal("39.90"),
    }
    result = client.evaluate(cart, explain=True)
    print(f"Total {result['total']} (discount {result['totalDiscount']})")
    for applied in result["appliedCampaigns"]:
        print(f"  {applied['code']}: -{applied['discount']}")
    for hint in result["hints"]:
        print(f"  Almost there: {hint['name']} (missing {hint.get('missingAmount')})")
