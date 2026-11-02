"""Shared fixtures."""

from __future__ import annotations

import asyncio
import json
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest
from homeassistant.core import HomeAssistant
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.lumia_hifi.const import CONF_SECRET, DOMAIN

CONTRACT_DIR = Path(__file__).resolve().parents[3] / "contracts" / "lumia_hifi" / "v1"
SECRET = "test-secret-not-a-real-credential"
ENTITY_ID = "media_player.marantz_sala"


@pytest.fixture(autouse=True)
def _enable_custom_integrations(enable_custom_integrations: None) -> None:
    """Let Home Assistant load custom_components/ from the working directory."""


@pytest.fixture
def schemas() -> dict[str, dict[str, Any]]:
    """The JSON Schemas of contract v1, keyed by frame type."""
    return {
        path.name.split(".")[0]: json.loads(path.read_text())
        for path in sorted((CONTRACT_DIR / "schema").glob("*.json"))
    }


def load_fixtures(kind: str) -> list[tuple[str, str]]:
    """Return (file name, raw text) for every fixture of a kind."""
    folder = CONTRACT_DIR / "fixtures" / kind
    return [(path.name, path.read_text()) for path in sorted(folder.glob("*.json"))]


@pytest.fixture
async def entry(hass: HomeAssistant) -> MockConfigEntry:
    """A loaded config entry named 'Marantz Sala'."""
    config_entry = MockConfigEntry(domain=DOMAIN, title="Marantz Sala", data={CONF_SECRET: SECRET})
    config_entry.add_to_hass(hass)
    assert await hass.config_entries.async_setup(config_entry.entry_id)
    await hass.async_block_till_done()
    return config_entry


async def wait_for(condition: Callable[[], bool], seconds: float = 3.0) -> None:
    """Wait for a condition that depends on socket traffic."""
    deadline = time.monotonic() + seconds
    while not condition():
        if time.monotonic() > deadline:
            raise AssertionError("condition not met in time")
        await asyncio.sleep(0.01)


def load_expected(kind: str) -> list[tuple[str, str, str]]:
    """Return (file name, raw text, expected error code) for a folder with an expected.json."""
    folder = CONTRACT_DIR / "fixtures" / kind
    expected: dict[str, str] = json.loads((folder / "expected.json").read_text())
    files = sorted(p for p in folder.iterdir() if p.name != "expected.json")
    assert sorted(expected) == [p.name for p in files], f"{kind}/expected.json is out of date"
    return [(p.name, p.read_text(), expected[p.name]) for p in files]
