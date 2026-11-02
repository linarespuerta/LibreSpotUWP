"""End to end: simulator -> HTTP endpoint -> link -> media player."""

from __future__ import annotations

import json
import logging
from typing import Any

import pytest
from aiohttp import WSMsgType, WSServerHandshakeError
from homeassistant.components.diagnostics import REDACTED
from homeassistant.components.media_player import MediaPlayerEntityFeature
from homeassistant.const import EVENT_STATE_CHANGED
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import HomeAssistantError, ServiceNotSupported
from homeassistant.helpers import entity_registry as er
from jsonschema import Draft202012Validator
from pytest_homeassistant_custom_component.common import MockConfigEntry, async_capture_events
from pytest_homeassistant_custom_component.components.diagnostics import (
    get_diagnostics_for_config_entry,
)

from custom_components.lumia_hifi.const import CONF_SECRET, LINK_PATH
from custom_components.lumia_hifi.link import LumiaDevice, LumiaLink
from custom_components.lumia_hifi.protocol import MAX_FRAME_BYTES
from tools.lumia_sim import ALL_COMMANDS, LumiaSimulator

from .conftest import ENTITY_ID, SECRET, wait_for


@pytest.fixture
async def client(hass: HomeAssistant, entry: MockConfigEntry, hass_client_no_auth: Any) -> Any:
    return await hass_client_no_auth()


async def connected_sim(client: Any, **kwargs: Any) -> LumiaSimulator:
    sim = LumiaSimulator(client, LINK_PATH, SECRET, **kwargs)
    await sim.connect()
    sim.start()
    return sim


async def call(hass: HomeAssistant, service: str, **data: Any) -> None:
    await hass.services.async_call(
        "media_player", service, {"entity_id": ENTITY_ID, **data}, blocking=True
    )


def attrs(hass: HomeAssistant) -> dict[str, Any]:
    return dict(hass.states.get(ENTITY_ID).attributes)  # type: ignore[union-attr]


# -- authentication ---------------------------------------------------------------------


async def test_missing_or_wrong_secret_is_rejected_before_upgrade(client: Any) -> None:
    for headers in ({}, {"Authorization": "Bearer nope"}, {"Authorization": SECRET}):
        with pytest.raises(WSServerHandshakeError) as err:
            await client.ws_connect(LINK_PATH, headers=headers)
        assert err.value.status == 401


async def test_plain_http_with_valid_secret_is_not_an_upgrade(client: Any) -> None:
    response = await client.get(LINK_PATH, headers={"Authorization": f"Bearer {SECRET}"})
    assert response.status == 400


# -- state ------------------------------------------------------------------------------


async def test_entity_is_unavailable_until_hello(hass: HomeAssistant, client: Any) -> None:
    assert hass.states.get(ENTITY_ID).state == "unavailable"  # type: ignore[union-attr]
    ws = await client.ws_connect(LINK_PATH, headers={"Authorization": f"Bearer {SECRET}"})
    await hass.async_block_till_done()
    assert hass.states.get(ENTITY_ID).state == "unavailable"  # type: ignore[union-attr]
    await ws.close()


async def test_hello_and_state_populate_the_entity(hass: HomeAssistant, client: Any) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    a = attrs(hass)
    assert a["media_title"] == "Blue in Green"
    assert a["media_artist"] == "Miles Davis"
    assert a["media_album_name"] == "Kind of Blue"
    assert a["media_content_id"] == "spotify:track:0aBcDeFgHiJkLmNoPqRsTu"
    assert a["entity_picture"] == "https://i.scdn.co/image/ab67616d00001e02example1"
    assert a["media_duration"] == 337
    assert a["media_position"] == 61
    assert "media_position_updated_at" in a
    assert a["volume_level"] == 0.5
    assert a["shuffle"] is False
    assert a["repeat"] == "off"
    assert a["session"] == "connected"
    assert a["output"] == "local"
    assert a["app_version"] == "1.0.34.0"
    expected = MediaPlayerEntityFeature(0)
    for feature in (
        "PLAY",
        "PAUSE",
        "NEXT_TRACK",
        "PREVIOUS_TRACK",
        "SEEK",
        "VOLUME_SET",
        "VOLUME_STEP",
        "SHUFFLE_SET",
        "REPEAT_SET",
    ):
        expected |= MediaPlayerEntityFeature[feature]
    assert a["supported_features"] == expected
    await sim.close()


async def test_disconnect_makes_the_entity_unavailable(hass: HomeAssistant, client: Any) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    await sim.close()
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "unavailable")  # type: ignore[union-attr]


async def test_supported_features_follow_hello(hass: HomeAssistant, client: Any) -> None:
    sim = await connected_sim(client, commands=("play", "pause"))
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    assert attrs(hass)["supported_features"] == (
        MediaPlayerEntityFeature.PLAY | MediaPlayerEntityFeature.PAUSE
    )
    with pytest.raises(ServiceNotSupported):
        await call(hass, "media_next_track")
    assert not [r for r in sim.received if "next" in r]
    await sim.close()


# -- commands ---------------------------------------------------------------------------


async def test_every_service_becomes_one_command_and_state_follows(
    hass: HomeAssistant, client: Any
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]

    await call(hass, "media_pause")
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "paused")  # type: ignore[union-attr]
    await call(hass, "media_play")
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]

    await call(hass, "media_next_track")
    await wait_for(lambda: attrs(hass).get("media_title") == "So What")
    await call(hass, "media_previous_track")
    await wait_for(lambda: attrs(hass).get("media_title") == "Blue in Green")

    await call(hass, "media_seek", seek_position=90.5)
    await wait_for(lambda: attrs(hass).get("media_position") == 90)

    await call(hass, "volume_set", volume_level=0.35)
    await wait_for(lambda: attrs(hass).get("volume_level") == 0.35)
    await call(hass, "volume_up")
    await wait_for(lambda: attrs(hass).get("volume_level") == 0.45)

    await call(hass, "shuffle_set", shuffle=True)
    await wait_for(lambda: attrs(hass).get("shuffle") is True)
    await call(hass, "repeat_set", repeat="one")
    await wait_for(lambda: attrs(hass).get("repeat") == "one")

    names = [json.loads(frame)["name"] for frame in sim.received]
    assert names == [
        "pause",
        "play",
        "next",
        "previous",
        "seek",
        "volume_set",
        "volume_set",
        "shuffle_set",
        "repeat_set",
    ]
    await sim.close()


async def test_a_refused_command_raises(hass: HomeAssistant, client: Any) -> None:
    sim = await connected_sim(client, nack="busy")
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    with pytest.raises(HomeAssistantError, match="busy"):
        await call(hass, "media_pause")
    await sim.close()


async def test_an_unanswered_command_times_out(
    hass: HomeAssistant, client: Any, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr("custom_components.lumia_hifi.link.COMMAND_TIMEOUT_SECONDS", 0.05)
    sim = await connected_sim(client, answer=False)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    with pytest.raises(HomeAssistantError, match="in time"):
        await call(hass, "media_pause")
    await sim.close()


async def test_disconnect_fails_a_command_that_is_waiting(hass: HomeAssistant, client: Any) -> None:
    import asyncio

    sim = await connected_sim(client, answer=False)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    pending = asyncio.create_task(call(hass, "media_pause"))
    await wait_for(lambda: len(sim.received) == 1)
    await sim.close()
    with pytest.raises(HomeAssistantError):
        await pending


async def test_commands_validate_against_the_schema_and_sim_frames_too(
    hass: HomeAssistant, client: Any, schemas: dict[str, dict[str, Any]]
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    await call(hass, "media_seek", seek_position=5)
    await call(hass, "set_volume_level" if False else "volume_set", volume_level=0.2)
    await wait_for(lambda: len(sim.sent) >= 6)
    for raw in sim.received:
        assert Draft202012Validator(schemas["command"]).is_valid(json.loads(raw)), raw
    for raw in sim.sent:
        frame = json.loads(raw)
        assert Draft202012Validator(schemas[frame["type"]]).is_valid(frame), raw
    assert {json.loads(raw)["type"] for raw in sim.sent} == {"hello", "state", "ack"}
    await sim.close()


# -- protocol rules ---------------------------------------------------------------------


async def test_second_connection_replaces_the_first(hass: HomeAssistant, client: Any) -> None:
    first = await connected_sim(client)
    await wait_for(lambda: attrs(hass).get("media_title") == "Blue in Green")
    second = await connected_sim(client, first_track=2, device_name="Other")
    await wait_for(lambda: attrs(hass).get("media_title") == "Freddie Freeloader")
    await wait_for(lambda: first.close_code is not None)
    assert first.close_code == 4001
    await call(hass, "media_pause")
    assert len(second.received) == 1
    assert not first.received
    await first.close()
    await second.close()


async def test_first_frame_must_be_hello(hass: HomeAssistant, client: Any) -> None:
    ws = await client.ws_connect(LINK_PATH, headers={"Authorization": f"Bearer {SECRET}"})
    state = json.dumps(
        {
            "v": 1,
            "type": "state",
            "seq": 0,
            "state": "idle",
            "title": None,
            "artist": None,
            "album": None,
            "artwork_url": None,
            "content_id": None,
            "duration_ms": 0,
            "position_ms": 0,
            "volume": 0,
            "shuffle": False,
            "repeat": "off",
            "session": "offline",
            "output": "local",
        }
    )
    await ws.send_str(state)
    message = await ws.receive()
    assert message.type in (WSMsgType.CLOSE, WSMsgType.CLOSED)
    assert ws.close_code == 4003


async def test_unsupported_version_closes_with_4002(hass: HomeAssistant, client: Any) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    assert sim.ws is not None
    await sim.ws.send_str('{"v":2,"type":"state"}')
    await wait_for(lambda: sim.close_code is not None)
    assert sim.close_code == 4002
    await sim.close()


@pytest.mark.parametrize(
    ("payload", "code"),
    [
        ('{"v":1,"type":"reboot"}', "unknown_type"),
        ('{"v":1,"type":"state","seq":1}', "invalid_frame"),
        ("not json", "invalid_frame"),
        ('{"v":1,"type":"command","id":"x","name":"play"}', "invalid_frame"),
        (
            '{"v":1,"type":"hello","device_id":"3f2b8c1e-6a4d-4e0b-9d57-1c2a7b9e4f10",'
            '"device_name":"x","app_version":"1.0.0.0","commands":[]}',
            "invalid_frame",
        ),
    ],
)
async def test_bad_frames_after_hello_get_an_error_and_keep_the_link(
    hass: HomeAssistant, client: Any, payload: str, code: str
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    assert sim.ws is not None
    await sim.ws.send_str(payload)
    await wait_for(lambda: any(json.loads(r).get("type") == "error" for r in sim.received))
    error = next(json.loads(r) for r in sim.received if json.loads(r).get("type") == "error")
    assert error == {"v": 1, "type": "error", "code": code}
    assert sim.close_code is None
    assert hass.states.get(ENTITY_ID).state == "playing"  # type: ignore[union-attr]
    await sim.close()


async def test_binary_frames_are_invalid(hass: HomeAssistant, client: Any) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    assert sim.ws is not None
    await sim.ws.send_bytes(b"\x00\x01")
    await wait_for(lambda: any('"invalid_frame"' in r for r in sim.received))
    assert sim.close_code is None
    await sim.close()


async def test_oversized_frame_gets_too_large_and_closes_with_4004(
    hass: HomeAssistant, client: Any
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    assert sim.ws is not None
    await sim.ws.send_str(" " * (MAX_FRAME_BYTES + 1))
    await wait_for(lambda: sim.close_code is not None)
    assert sim.close_code == 4004
    assert any('"too_large"' in r for r in sim.received)
    await sim.close()


# -- secrets ----------------------------------------------------------------------------


async def test_rotating_the_secret_drops_the_link_and_rekeys_the_endpoint(
    hass: HomeAssistant, entry: MockConfigEntry, client: Any
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]

    result = await hass.config_entries.options.async_init(entry.entry_id)
    result = await hass.config_entries.options.async_configure(
        result["flow_id"], {"rotate_secret": True}
    )
    new_secret = result["description_placeholders"]["secret"]
    await hass.config_entries.options.async_configure(result["flow_id"], {})
    await hass.async_block_till_done()

    await wait_for(lambda: sim.close_code is not None)
    with pytest.raises(WSServerHandshakeError) as err:
        await client.ws_connect(LINK_PATH, headers={"Authorization": f"Bearer {SECRET}"})
    assert err.value.status == 401
    fresh = LumiaSimulator(client, LINK_PATH, new_secret)
    await fresh.connect()
    fresh.start()
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    await fresh.close()
    await sim.close()


async def test_unloading_the_entry_closes_the_link(
    hass: HomeAssistant, entry: MockConfigEntry, client: Any
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    assert await hass.config_entries.async_unload(entry.entry_id)
    await wait_for(lambda: sim.close_code is not None)
    await sim.close()


async def test_secret_never_reaches_logs(
    hass: HomeAssistant,
    client: Any,
    caplog: pytest.LogCaptureFixture,
) -> None:
    caplog.set_level(logging.DEBUG)
    with pytest.raises(WSServerHandshakeError):
        await client.ws_connect(LINK_PATH, headers={"Authorization": "Bearer wrong-secret-xyz"})
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    await call(hass, "media_pause")
    await sim.close()
    assert SECRET not in caplog.text
    assert "wrong-secret-xyz" not in caplog.text


async def test_diagnostics_redact_the_secret(
    hass: HomeAssistant, entry: MockConfigEntry, hass_client: Any
) -> None:
    diagnostics = await get_diagnostics_for_config_entry(hass, hass_client, entry)
    assert diagnostics["entry"][CONF_SECRET] == REDACTED
    assert SECRET not in json.dumps(diagnostics)
    assert diagnostics["connected"] is False


async def test_entity_registered_with_stable_unique_id(
    hass: HomeAssistant, entry: MockConfigEntry
) -> None:
    registry = er.async_get(hass)
    item = registry.async_get(ENTITY_ID)
    assert item is not None
    assert item.unique_id == entry.entry_id


def test_sim_declares_every_contract_command() -> None:
    assert len(ALL_COMMANDS) == 8


# -- review fixes ------------------------------------------------------------------------------


async def _raw(client: Any) -> Any:
    return await client.ws_connect(LINK_PATH, headers={"Authorization": f"Bearer {SECRET}"})


async def _error_then_close(ws: Any) -> tuple[dict[str, Any] | None, int | None]:
    """Collect the error frame (if any) sent before the server closes the socket."""
    error = None
    async for message in ws:
        if message.type is WSMsgType.TEXT:
            error = json.loads(message.data)
    return error, ws.close_code


async def test_invalid_hello_gets_invalid_frame_and_close_4005(
    hass: HomeAssistant, client: Any
) -> None:
    ws = await _raw(client)
    await ws.send_str(
        json.dumps(
            {
                "v": 1,
                "type": "hello",
                "device_id": "3f2b8c1e-6a4d-4e0b-9d57-1c2a7b9e4f10",
                "device_name": "x" * 65,
                "app_version": "1.0.34.0",
                "commands": [],
            }
        )
    )
    error, code = await _error_then_close(ws)
    assert error == {"v": 1, "type": "error", "code": "invalid_frame"}
    assert code == 4005


@pytest.mark.parametrize(
    ("send", "expected_error", "expected_code"),
    [
        (lambda ws: ws.send_bytes(b"\x00"), "invalid_frame", 4003),
        (lambda ws: ws.send_str("not json"), "invalid_frame", 4003),
        (lambda ws: ws.send_str('{"v":1,"type":"reboot"}'), "unknown_type", 4003),
        (lambda ws: ws.send_str(" " * (MAX_FRAME_BYTES + 1)), "too_large", 4004),
    ],
    ids=["binary", "not-json", "unknown-type", "too-large"],
)
async def test_bad_first_frames_get_an_error_frame_before_the_close(
    hass: HomeAssistant, client: Any, send: Any, expected_error: str, expected_code: int
) -> None:
    ws = await _raw(client)
    await send(ws)
    error, code = await _error_then_close(ws)
    assert error == {"v": 1, "type": "error", "code": expected_error}
    assert code == expected_code


async def test_frames_above_64_kib_still_follow_the_contract(
    hass: HomeAssistant, client: Any
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    assert sim.ws is not None
    await sim.ws.send_str(" " * 70_000)
    await wait_for(lambda: sim.close_code is not None)
    assert sim.close_code == 4004
    assert any('"too_large"' in r for r in sim.received)
    await sim.close()


async def test_seek_beyond_the_contract_limit_is_clamped(hass: HomeAssistant, client: Any) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    await call(hass, "media_seek", seek_position=10**7)
    assert json.loads(sim.received[-1])["position_ms"] == 86_400_000
    await sim.close()


async def test_an_out_of_contract_value_is_a_clean_error(
    hass: HomeAssistant, entry: MockConfigEntry, client: Any
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    with pytest.raises(HomeAssistantError, match="Invalid value for 'volume_set'"):
        await entry.runtime_data.async_send_command("volume_set", volume=2)
    assert not sim.received
    await sim.close()


async def test_a_command_writes_state_once_and_acks_write_nothing(
    hass: HomeAssistant, client: Any
) -> None:
    sim = await connected_sim(client)
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "playing")  # type: ignore[union-attr]
    assert "last_seen" not in attrs(hass)
    events = async_capture_events(hass, EVENT_STATE_CHANGED)
    await call(hass, "media_pause")
    await wait_for(lambda: hass.states.get(ENTITY_ID).state == "paused")  # type: ignore[union-attr]
    await hass.async_block_till_done()
    assert len(events) == 1, [e.data["new_state"].state for e in events]

    # A frame that changes nothing visible (an unsolicited ack or error) writes no state.
    assert sim.ws is not None
    await sim.send({"v": 1, "type": "ack", "id": "unknown", "ok": True})
    await sim.send({"v": 1, "type": "error", "code": "unknown_type"})
    await sim.send_state()
    await wait_for(lambda: len(events) == 2)
    await hass.async_block_till_done()
    assert len(events) == 2
    await sim.close()


class _GoneSocket:
    """A WebSocket whose peer has already disappeared."""

    closed = True
    close_code = None

    async def close(self, code: int = 1000) -> None:
        raise ConnectionResetError

    async def send_str(self, text: str) -> None:
        raise ConnectionResetError


async def test_replying_to_a_peer_that_is_gone_does_not_raise(hass: HomeAssistant) -> None:
    link = LumiaLink(hass)
    gone: Any = _GoneSocket()
    assert await link._async_handle_text(gone, "not json") is False  # before hello: close
    link.device = LumiaDevice("id", "name", "1.0.0.0", frozenset())
    assert await link._async_handle_text(gone, "not json") is True  # after hello: reply only
    assert await link._async_handle_text(gone, '{"v":2}') is False
