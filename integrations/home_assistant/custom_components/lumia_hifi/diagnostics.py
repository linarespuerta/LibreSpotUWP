"""Diagnostics with the link secret redacted."""

from __future__ import annotations

from typing import Any

from homeassistant.components.diagnostics import async_redact_data
from homeassistant.core import HomeAssistant

from . import LumiaConfigEntry
from .const import CONF_SECRET

TO_REDACT = {CONF_SECRET}


async def async_get_config_entry_diagnostics(
    hass: HomeAssistant, entry: LumiaConfigEntry
) -> dict[str, Any]:
    """Return diagnostics for the entry."""
    link = entry.runtime_data
    return {
        "entry": async_redact_data(dict(entry.data), TO_REDACT),
        "connected": link.connected,
        "device": None
        if link.device is None
        else {
            "app_version": link.device.app_version,
            "commands": sorted(link.device.commands),
        },
        "state": link.state,
        "last_seen": None if link.last_seen is None else link.last_seen.isoformat(),
    }
