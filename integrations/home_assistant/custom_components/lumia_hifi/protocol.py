"""Wire contract v1 between the Lumia HiFi app and Home Assistant.

The authoritative definition is ``contracts/lumia_hifi/v1`` (JSON Schema plus shared
fixtures). This module is the runtime validator used by the integration; it is kept free
of Home Assistant imports so it stays easy to test.
"""

from __future__ import annotations

import json
import math
import re
from collections.abc import Callable, Iterable
from typing import Any

PROTOCOL_VERSION = 1
MAX_FRAME_BYTES = 16384
# Above this the WebSocket layer itself cuts the connection (close 1009); between the two limits
# the contract's too_large handling applies.
TRANSPORT_MAX_BYTES = 1_048_576
MAX_DURATION_MS = 86_400_000

COMMAND_NAMES = (
    "play",
    "pause",
    "next",
    "previous",
    "seek",
    "volume_set",
    "shuffle_set",
    "repeat_set",
)
PLAYBACK_STATES = ("playing", "paused", "buffering", "idle")
REPEAT_MODES = ("off", "all", "one")
SESSIONS = ("connected", "offline", "recovering")
OUTPUTS = ("local", "remote")
ACK_ERRORS = ("unsupported", "invalid", "failed", "busy")
ERROR_CODES = ("unknown_type", "invalid_frame", "too_large")

LUMIA_TO_HA = frozenset({"hello", "state", "ack", "error"})
ALL_TYPES = frozenset({"hello", "state", "ack", "error", "command"})

_DEVICE_ID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
_APP_VERSION = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$")
_ARTWORK = re.compile(r"^https://\S+$")
_CONTENT_ID = re.compile(r"^spotify:(track|episode):[A-Za-z0-9]{22}$")

Validator = Callable[[Any], Any]


class FrameError(Exception):
    """A frame violated the contract.

    ``code`` is one of ``unknown_type``, ``invalid_frame``, ``too_large`` or
    ``unsupported_version``; the last one closes the connection, the others do not.
    """

    def __init__(self, code: str, detail: str = "", frame_type: str | None = None) -> None:
        super().__init__(f"{code}: {detail}" if detail else code)
        self.code = code
        self.detail = detail
        # The frame's declared type when it was known, e.g. to tell a malformed hello apart.
        self.frame_type = frame_type


def _reject_constant(name: str) -> Any:
    raise ValueError(f"{name} is not valid JSON")


def _unique_members(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate member '{key}'")
        result[key] = value
    return result


def _integer(minimum: int, maximum: int) -> Validator:
    def check(value: Any) -> int:
        if isinstance(value, bool) or not isinstance(value, int):
            raise ValueError("expected an integer")
        if not minimum <= value <= maximum:
            raise ValueError("integer out of range")
        return value

    return check


def _unit_number(value: Any) -> float:
    if isinstance(value, bool) or not isinstance(value, int | float):
        raise ValueError("expected a number")
    if not math.isfinite(value) or not 0 <= value <= 1:
        raise ValueError("number out of range")
    return float(value)


def _boolean(value: Any) -> bool:
    if not isinstance(value, bool):
        raise ValueError("expected a boolean")
    return value


def _constant(expected: int | str) -> Validator:
    def check(value: Any) -> int | str:
        if isinstance(value, bool) or type(value) is not type(expected) or value != expected:
            raise ValueError(f"expected {expected!r}")
        return value

    return check


def _choice(options: Iterable[str]) -> Validator:
    allowed = tuple(options)

    def check(value: Any) -> str:
        if not isinstance(value, str) or value not in allowed:
            raise ValueError("unexpected value")
        return value

    return check


def _text(minimum: int, maximum: int, *, nullable: bool = False) -> Validator:
    def check(value: Any) -> str | None:
        if value is None and nullable:
            return None
        if not isinstance(value, str) or not minimum <= len(value) <= maximum:
            raise ValueError("invalid text")
        return value

    return check


def _pattern(regex: re.Pattern[str], maximum: int, *, nullable: bool = False) -> Validator:
    def check(value: Any) -> str | None:
        if value is None and nullable:
            return None
        if not isinstance(value, str) or len(value) > maximum or not regex.match(value):
            raise ValueError("invalid format")
        return value

    return check


def _unique_commands(value: Any) -> list[str]:
    if not isinstance(value, list):
        raise ValueError("expected a list")
    items = [_choice(COMMAND_NAMES)(item) for item in value]
    if len(set(items)) != len(items):
        raise ValueError("duplicate commands")
    return items


def _object(
    frame_type: str,
    required: dict[str, Validator],
    optional: dict[str, Validator] | None = None,
) -> Callable[[dict[str, Any]], dict[str, Any]]:
    """Build a strict validator: every required key present, no key outside the schema."""
    fields = {
        "v": _constant(PROTOCOL_VERSION),
        "type": _constant(frame_type),
        **required,
    }
    optional = optional or {}

    def check(frame: dict[str, Any]) -> dict[str, Any]:
        for key in frame:
            if key not in fields and key not in optional:
                raise ValueError(f"unexpected field '{key}'")
        result: dict[str, Any] = {}
        for key, validator in fields.items():
            if key not in frame:
                raise ValueError(f"missing field '{key}'")
            result[key] = validator(frame[key])
        for key, validator in optional.items():
            if key in frame:
                result[key] = validator(frame[key])
        return result

    return check


_COMMAND_ID: dict[str, Validator] = {"id": _text(1, 64)}

_HELLO = _object(
    "hello",
    {
        "device_id": _pattern(_DEVICE_ID, 36),
        "device_name": _text(1, 64),
        "app_version": _pattern(_APP_VERSION, 32),
        "commands": _unique_commands,
    },
)
_STATE = _object(
    "state",
    {
        "seq": _integer(0, 4_294_967_295),
        "state": _choice(PLAYBACK_STATES),
        "title": _text(0, 256, nullable=True),
        "artist": _text(0, 256, nullable=True),
        "album": _text(0, 256, nullable=True),
        "artwork_url": _pattern(_ARTWORK, 512, nullable=True),
        "content_id": _pattern(_CONTENT_ID, 64, nullable=True),
        "duration_ms": _integer(0, MAX_DURATION_MS),
        "position_ms": _integer(0, MAX_DURATION_MS),
        "volume": _unit_number,
        "shuffle": _boolean,
        "repeat": _choice(REPEAT_MODES),
        "session": _choice(SESSIONS),
        "output": _choice(OUTPUTS),
    },
)
_ACK_FIELDS = _object(
    "ack",
    {**_COMMAND_ID, "ok": _boolean},
    {"error": _choice(ACK_ERRORS)},
)


def _ack(frame: dict[str, Any]) -> dict[str, Any]:
    result = _ACK_FIELDS(frame)
    if result["ok"] and "error" in result:
        raise ValueError("a successful ack carries no error")
    if not result["ok"] and "error" not in result:
        raise ValueError("a failed ack needs an error")
    return result


_ERROR = _object("error", {"code": _choice(ERROR_CODES)})

_COMMAND_FIELDS: dict[str, dict[str, Validator]] = {
    "play": {},
    "pause": {},
    "next": {},
    "previous": {},
    "seek": {"position_ms": _integer(0, MAX_DURATION_MS)},
    "volume_set": {"volume": _unit_number},
    "shuffle_set": {"shuffle": _boolean},
    "repeat_set": {"repeat": _choice(REPEAT_MODES)},
}
_COMMANDS = {
    name: _object("command", {**_COMMAND_ID, "name": _constant(name), **fields})
    for name, fields in _COMMAND_FIELDS.items()
}


def _command(frame: dict[str, Any]) -> dict[str, Any]:
    name = frame.get("name")
    validator = _COMMANDS.get(name) if isinstance(name, str) else None
    if validator is None:
        raise ValueError("unknown command")
    return validator(frame)


_VALIDATORS: dict[str, Callable[[dict[str, Any]], dict[str, Any]]] = {
    "hello": _HELLO,
    "state": _STATE,
    "ack": _ack,
    "error": _ERROR,
    "command": _command,
}


def parse_frame(raw: str | bytes, accept: frozenset[str] = ALL_TYPES) -> dict[str, Any]:
    """Validate one received frame and return it, or raise ``FrameError``."""
    data = raw.encode("utf-8") if isinstance(raw, str) else raw
    if len(data) > MAX_FRAME_BYTES:
        raise FrameError("too_large")
    try:
        # Strict RFC 8259: no NaN/Infinity, no duplicate members (see the contract README).
        frame = json.loads(data, parse_constant=_reject_constant, object_pairs_hook=_unique_members)
    except (ValueError, RecursionError) as err:
        raise FrameError("invalid_frame", "not JSON") from err
    if not isinstance(frame, dict):
        raise FrameError("invalid_frame", "not an object")
    version = frame.get("v")
    # The integer 1 only: 1.0 and True compare equal to 1 in Python but are not version 1.
    if type(version) is not int or version != PROTOCOL_VERSION:
        raise FrameError("unsupported_version")
    frame_type = frame.get("type")
    if not isinstance(frame_type, str) or frame_type not in _VALIDATORS:
        raise FrameError("unknown_type")
    if frame_type not in accept:
        raise FrameError(
            "invalid_frame", f"{frame_type} is not valid in this direction", frame_type
        )
    try:
        return _VALIDATORS[frame_type](frame)
    except ValueError as err:
        raise FrameError("invalid_frame", str(err), frame_type) from err


def encode_frame(frame: dict[str, Any]) -> str:
    """Validate an outgoing frame and serialize it compactly."""
    parse_frame(json.dumps(frame))
    return json.dumps(frame, separators=(",", ":"))


def build_command(command_id: str, name: str, **fields: Any) -> str:
    """Build a validated ``command`` frame."""
    return encode_frame(
        {"v": PROTOCOL_VERSION, "type": "command", "id": command_id, "name": name, **fields}
    )


def build_error(code: str) -> str:
    """Build an ``error`` frame."""
    return encode_frame({"v": PROTOCOL_VERSION, "type": "error", "code": code})
