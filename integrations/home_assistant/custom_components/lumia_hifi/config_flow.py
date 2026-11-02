"""Config and options flow: the secret is generated here and shown once."""

from __future__ import annotations

import secrets
from typing import Any

try:  # Home Assistant 2026.10+ validates with probatio; older releases use voluptuous.
    import probatio as vol
except ImportError:  # pragma: no cover
    import voluptuous as vol  # type: ignore[no-redef]
from homeassistant.config_entries import ConfigFlow, ConfigFlowResult, OptionsFlow
from homeassistant.const import CONF_NAME
from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.network import NoURLAvailableError, get_url

from . import LumiaConfigEntry
from .const import CONF_SECRET, DOMAIN, LINK_PATH

ROTATE = "rotate_secret"


def _new_secret() -> str:
    return secrets.token_urlsafe(32)


def _link_url(hass: HomeAssistant) -> str:
    """Return the WebSocket URL the phone should be configured with."""
    try:
        base = get_url(hass, allow_external=False, allow_cloud=False, prefer_external=False)
    except NoURLAvailableError:
        base = "http://<home-assistant-host>:8123"
    scheme, _, rest = base.partition("://")
    return f"{'wss' if scheme == 'https' else 'ws'}://{rest.rstrip('/')}{LINK_PATH}"


class LumiaHifiConfigFlow(ConfigFlow, domain=DOMAIN):
    """Create an entry and show the link URL and secret once."""

    VERSION = 1

    def __init__(self) -> None:
        self._name = ""
        self._secret = ""

    @staticmethod
    @callback
    def async_get_options_flow(config_entry: LumiaConfigEntry) -> OptionsFlow:
        """Return the options flow."""
        return LumiaHifiOptionsFlow()

    async def async_step_user(self, user_input: dict[str, Any] | None = None) -> ConfigFlowResult:
        """Ask for a display name."""
        if user_input is not None:
            self._name = user_input[CONF_NAME].strip()
            self._secret = _new_secret()
            return await self.async_step_secret()
        schema = vol.Schema(
            {
                vol.Required(CONF_NAME, default="Marantz Sala"): vol.All(
                    str, vol.Length(min=1, max=64)
                )
            }
        )
        return self.async_show_form(step_id="user", data_schema=schema)

    async def async_step_secret(self, user_input: dict[str, Any] | None = None) -> ConfigFlowResult:
        """Show the URL and secret to type into the Lumia app, then create the entry."""
        if user_input is not None:
            return self.async_create_entry(title=self._name, data={CONF_SECRET: self._secret})
        return self.async_show_form(
            step_id="secret",
            description_placeholders={"url": _link_url(self.hass), "secret": self._secret},
        )


class LumiaHifiOptionsFlow(OptionsFlow):
    """Rotate the secret, which immediately drops the live link."""

    def __init__(self) -> None:
        self._secret = ""

    async def async_step_init(self, user_input: dict[str, Any] | None = None) -> ConfigFlowResult:
        """Offer to rotate the secret."""
        if user_input is not None:
            if not user_input[ROTATE]:
                return self.async_create_entry(data=dict(self.config_entry.options))
            self._secret = _new_secret()
            self.hass.config_entries.async_update_entry(
                self.config_entry, data={**self.config_entry.data, CONF_SECRET: self._secret}
            )
            return await self.async_step_secret()
        return self.async_show_form(
            step_id="init",
            data_schema=vol.Schema({vol.Required(ROTATE, default=False): bool}),
        )

    async def async_step_secret(self, user_input: dict[str, Any] | None = None) -> ConfigFlowResult:
        """Show the new secret once."""
        if user_input is not None:
            return self.async_create_entry(data=dict(self.config_entry.options))
        return self.async_show_form(
            step_id="secret",
            description_placeholders={"url": _link_url(self.hass), "secret": self._secret},
        )
