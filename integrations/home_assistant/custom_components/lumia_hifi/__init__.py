"""Lumia HiFi: Spotify Connect receiver exposed as a media player."""

from __future__ import annotations

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import Platform
from homeassistant.core import HomeAssistant
from homeassistant.helpers import config_validation as cv
from homeassistant.helpers.typing import ConfigType

from .const import DOMAIN
from .link import LumiaLink
from .view import LumiaLinkView

PLATFORMS = [Platform.MEDIA_PLAYER]
CONFIG_SCHEMA = cv.config_entry_only_config_schema(DOMAIN)

type LumiaConfigEntry = ConfigEntry[LumiaLink]


async def async_setup(hass: HomeAssistant, config: ConfigType) -> bool:
    """Register the HTTP endpoint once for all entries."""
    hass.http.register_view(LumiaLinkView())
    return True


async def async_setup_entry(hass: HomeAssistant, entry: LumiaConfigEntry) -> bool:
    """Create the (initially disconnected) link and its media player."""
    entry.runtime_data = LumiaLink(hass)
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
    entry.async_on_unload(entry.add_update_listener(_async_reload))
    return True


async def _async_reload(hass: HomeAssistant, entry: LumiaConfigEntry) -> None:
    await hass.config_entries.async_reload(entry.entry_id)


async def async_unload_entry(hass: HomeAssistant, entry: LumiaConfigEntry) -> bool:
    """Drop the live connection and unload the platform."""
    unloaded = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
    if unloaded:
        await entry.runtime_data.async_close()
    return unloaded
