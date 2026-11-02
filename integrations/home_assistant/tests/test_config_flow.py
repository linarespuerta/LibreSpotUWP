"""Config flow and options flow."""

from __future__ import annotations

from homeassistant import config_entries
from homeassistant.core import HomeAssistant
from homeassistant.data_entry_flow import FlowResultType
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.lumia_hifi.const import CONF_SECRET, DOMAIN, LINK_PATH


async def test_user_flow_shows_the_secret_once_and_creates_the_entry(hass: HomeAssistant) -> None:
    result = await hass.config_entries.flow.async_init(
        DOMAIN, context={"source": config_entries.SOURCE_USER}
    )
    assert result["type"] is FlowResultType.FORM
    assert result["step_id"] == "user"

    result = await hass.config_entries.flow.async_configure(result["flow_id"], {"name": "  Sala  "})
    assert result["type"] is FlowResultType.FORM
    assert result["step_id"] == "secret"
    placeholders = result["description_placeholders"]
    secret = placeholders["secret"]
    assert len(secret) >= 43  # 256 bits, URL-safe base64
    assert placeholders["url"].startswith(("ws://", "wss://"))
    assert placeholders["url"].endswith(LINK_PATH)

    result = await hass.config_entries.flow.async_configure(result["flow_id"], {})
    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert result["title"] == "Sala"
    assert result["data"] == {CONF_SECRET: secret}


async def test_two_entries_get_different_secrets(hass: HomeAssistant) -> None:
    secrets_seen = set()
    for _ in range(2):
        result = await hass.config_entries.flow.async_init(
            DOMAIN, context={"source": config_entries.SOURCE_USER}
        )
        result = await hass.config_entries.flow.async_configure(result["flow_id"], {"name": "A"})
        secrets_seen.add(result["description_placeholders"]["secret"])
    assert len(secrets_seen) == 2


async def test_options_flow_without_rotation_keeps_the_secret(
    hass: HomeAssistant, entry: MockConfigEntry
) -> None:
    result = await hass.config_entries.options.async_init(entry.entry_id)
    result = await hass.config_entries.options.async_configure(
        result["flow_id"], {"rotate_secret": False}
    )
    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert entry.data[CONF_SECRET] == "test-secret-not-a-real-credential"


async def test_options_flow_rotates_the_secret(hass: HomeAssistant, entry: MockConfigEntry) -> None:
    old = entry.data[CONF_SECRET]
    result = await hass.config_entries.options.async_init(entry.entry_id)
    result = await hass.config_entries.options.async_configure(
        result["flow_id"], {"rotate_secret": True}
    )
    assert result["step_id"] == "secret"
    new = result["description_placeholders"]["secret"]
    assert new != old
    assert entry.data[CONF_SECRET] == new
    result = await hass.config_entries.options.async_configure(result["flow_id"], {})
    assert result["type"] is FlowResultType.CREATE_ENTRY
