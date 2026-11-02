"""HTTP endpoint the Lumia dials."""

from __future__ import annotations

import hmac

from aiohttp import web
from homeassistant.components.http import KEY_HASS, HomeAssistantView
from homeassistant.components.http.ban import process_wrong_login
from homeassistant.config_entries import ConfigEntryState
from homeassistant.core import HomeAssistant

from .const import CONF_SECRET, DOMAIN, LINK_PATH
from .link import LumiaLink
from .protocol import TRANSPORT_MAX_BYTES

_BEARER = "Bearer "


def _find_link(hass: HomeAssistant, token: str) -> LumiaLink | None:
    """Return the link whose secret matches, comparing every entry in constant time."""
    match: LumiaLink | None = None
    candidate = token.encode()
    for entry in hass.config_entries.async_entries(DOMAIN):
        if entry.state is not ConfigEntryState.LOADED:
            continue
        secret = str(entry.data.get(CONF_SECRET, "")).encode()
        if hmac.compare_digest(secret, candidate) and secret:
            match = entry.runtime_data
    return match


class LumiaLinkView(HomeAssistantView):
    """WebSocket endpoint authenticated by a per-entry bearer secret."""

    url = LINK_PATH
    name = "api:lumia_hifi:link"
    requires_auth = False

    async def get(self, request: web.Request) -> web.StreamResponse:
        """Authenticate, then upgrade to the link WebSocket."""
        hass = request.app[KEY_HASS]
        header = request.headers.get("Authorization", "")
        token = header[len(_BEARER) :] if header.startswith(_BEARER) else ""
        link = _find_link(hass, token)
        if link is None:
            await process_wrong_login(request)
            return web.Response(status=401, headers={"WWW-Authenticate": "Bearer"})
        ws = web.WebSocketResponse(max_msg_size=TRANSPORT_MAX_BYTES)
        if not ws.can_prepare(request).ok:
            return web.Response(status=400, text="WebSocket upgrade required")
        await ws.prepare(request)
        await link.async_serve(ws)
        return ws
