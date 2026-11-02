"""Contract v1: JSON Schema, runtime validator and shared fixtures must agree."""

from __future__ import annotations

import json
from typing import Any

import pytest
from jsonschema import Draft202012Validator

from custom_components.lumia_hifi.protocol import (
    MAX_FRAME_BYTES,
    FrameError,
    build_command,
    parse_frame,
)

from .conftest import load_expected, load_fixtures

VALID = load_fixtures("valid")
INVALID = load_fixtures("invalid")


def _schema_accepts(schemas: dict[str, dict[str, Any]], raw: str) -> bool:
    frame = json.loads(raw)
    schema = schemas.get(frame["type"])
    return schema is not None and Draft202012Validator(schema).is_valid(frame)


def test_fixture_inventory() -> None:
    assert len(VALID) >= 16
    assert len(INVALID) >= 24


def test_schemas_are_valid_schemas(schemas: dict[str, dict[str, Any]]) -> None:
    assert set(schemas) == {"hello", "state", "ack", "error", "command"}
    for schema in schemas.values():
        Draft202012Validator.check_schema(schema)


@pytest.mark.parametrize(("name", "raw"), VALID, ids=[name for name, _ in VALID])
def test_valid_fixture_is_accepted_everywhere(
    name: str, raw: str, schemas: dict[str, dict[str, Any]]
) -> None:
    assert _schema_accepts(schemas, raw), "JSON Schema rejects a valid fixture"
    assert parse_frame(raw) == json.loads(raw)


@pytest.mark.parametrize(("name", "raw"), INVALID, ids=[name for name, _ in INVALID])
def test_invalid_fixture_is_rejected_everywhere(
    name: str, raw: str, schemas: dict[str, dict[str, Any]]
) -> None:
    assert not _schema_accepts(schemas, raw), "JSON Schema accepts an invalid fixture"
    with pytest.raises(FrameError):
        parse_frame(raw)


def test_wrong_version_is_a_version_error() -> None:
    with pytest.raises(FrameError) as err:
        parse_frame('{"v":2,"type":"state"}')
    assert err.value.code == "unsupported_version"


def test_unknown_type_is_not_an_invalid_frame() -> None:
    with pytest.raises(FrameError) as err:
        parse_frame('{"v":1,"type":"reboot"}')
    assert err.value.code == "unknown_type"


@pytest.mark.parametrize("raw", ["not json", "[]", "null", "42", '"text"', b"\xff\xfe"])
def test_garbage_is_invalid_frame(raw: str | bytes) -> None:
    with pytest.raises(FrameError) as err:
        parse_frame(raw)
    assert err.value.code == "invalid_frame"


def test_oversized_frame_is_too_large_even_if_otherwise_valid() -> None:
    padding = "x" * MAX_FRAME_BYTES
    raw = json.dumps({"v": 1, "type": "command", "id": padding, "name": "play"})
    with pytest.raises(FrameError) as err:
        parse_frame(raw)
    assert err.value.code == "too_large"


def test_frame_at_the_limit_is_still_parsed() -> None:
    base = json.dumps({"v": 1, "type": "ack", "id": "", "ok": True}, separators=(",", ":"))
    assert len(base) < MAX_FRAME_BYTES
    # Valid JSON exactly at the limit, rejected by the schema (id too long), not by size.
    raw = json.dumps({"v": 1, "type": "ack", "id": "a" * 64, "ok": True})
    raw += " " * (MAX_FRAME_BYTES - len(raw))
    assert len(raw.encode()) == MAX_FRAME_BYTES
    assert parse_frame(raw)["id"] == "a" * 64


def test_booleans_are_not_numbers() -> None:
    raw = json.dumps({"v": 1, "type": "command", "id": "c", "name": "volume_set", "volume": True})
    with pytest.raises(FrameError):
        parse_frame(raw)
    raw = json.dumps({"v": True, "type": "command", "id": "c", "name": "play"})
    with pytest.raises(FrameError) as err:
        parse_frame(raw)
    assert err.value.code == "unsupported_version"


@pytest.mark.parametrize("volume", ["NaN", "Infinity", "-0.5"])
def test_non_finite_or_negative_volume_is_rejected(volume: str) -> None:
    raw = f'{{"v":1,"type":"command","id":"c","name":"volume_set","volume":{volume}}}'
    with pytest.raises(FrameError):
        parse_frame(raw)


def test_direction_is_enforced() -> None:
    from custom_components.lumia_hifi.protocol import LUMIA_TO_HA

    with pytest.raises(FrameError) as err:
        parse_frame('{"v":1,"type":"command","id":"c","name":"play"}', accept=LUMIA_TO_HA)
    assert err.value.code == "invalid_frame"


def test_built_commands_validate_against_the_schema(schemas: dict[str, dict[str, Any]]) -> None:
    validator = Draft202012Validator(schemas["command"])
    for name, fields in [
        ("play", {}),
        ("pause", {}),
        ("next", {}),
        ("previous", {}),
        ("seek", {"position_ms": 1234}),
        ("volume_set", {"volume": 0.25}),
        ("shuffle_set", {"shuffle": False}),
        ("repeat_set", {"repeat": "one"}),
    ]:
        frame = json.loads(build_command("id-1", name, **fields))
        assert validator.is_valid(frame), name


def test_building_an_invalid_command_fails() -> None:
    with pytest.raises(FrameError):
        build_command("id-1", "volume_set", volume=2)


# -- review fixes: rules stricter than JSON Schema ------------------------------------------

RUNTIME_ONLY = load_expected("runtime_only")
MALFORMED = load_expected("malformed")


@pytest.mark.parametrize(
    ("name", "raw", "code"), RUNTIME_ONLY, ids=[name for name, _, _ in RUNTIME_ONLY]
)
def test_schema_valid_but_runtime_invalid(
    name: str, raw: str, code: str, schemas: dict[str, dict[str, Any]]
) -> None:
    assert _schema_accepts(schemas, raw), "fixture must stay schema-valid to document the gap"
    with pytest.raises(FrameError) as err:
        parse_frame(raw)
    assert err.value.code == code


@pytest.mark.parametrize(("name", "raw", "code"), MALFORMED, ids=[name for name, _, _ in MALFORMED])
def test_malformed_json_is_an_invalid_frame(name: str, raw: str, code: str) -> None:
    with pytest.raises(FrameError) as err:
        parse_frame(raw)
    assert err.value.code == code


def test_python_json_quirks_are_disabled() -> None:
    # The standard library accepts these by default; the contract does not.
    assert json.loads('{"a":NaN}')
    assert json.loads('{"a":1,"a":2}') == {"a": 2}
    for raw in (
        '{"v":1,"type":"error","code":NaN}',
        '{"v":1,"v":1,"type":"error","code":"too_large"}',
    ):
        with pytest.raises(FrameError) as err:
            parse_frame(raw)
        assert err.value.code == "invalid_frame"


@pytest.mark.parametrize("version", ["1.0", "true", '"1"', "99999999999999999999", "-1", "null"])
def test_only_the_integer_one_is_version_one(version: str) -> None:
    with pytest.raises(FrameError) as err:
        parse_frame(f'{{"v":{version},"type":"error","code":"too_large"}}')
    assert err.value.code == "unsupported_version"


def test_invalid_version_fixtures_are_version_errors() -> None:
    for name, raw in INVALID:
        if "version" in name:
            with pytest.raises(FrameError) as err:
                parse_frame(raw)
            assert err.value.code == "unsupported_version", name


def test_frame_error_reports_the_declared_type() -> None:
    _, raw = next(item for item in INVALID if item[0] == "hello__bad_device_id.json")
    with pytest.raises(FrameError) as err:
        parse_frame(raw)
    assert err.value.frame_type == "hello"
    with pytest.raises(FrameError) as err:
        parse_frame("not json")
    assert err.value.frame_type is None
