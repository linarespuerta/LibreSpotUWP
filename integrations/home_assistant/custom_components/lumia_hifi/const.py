"""Constants for the Lumia HiFi integration."""

from __future__ import annotations

DOMAIN = "lumia_hifi"
CONF_SECRET = "secret"
LINK_PATH = "/api/lumia_hifi/v1/link"
COMMAND_TIMEOUT_SECONDS = 5.0

# WebSocket close codes defined by contract v1.
CLOSE_REPLACED = 4001
CLOSE_UNSUPPORTED_VERSION = 4002
CLOSE_HELLO_FIRST = 4003
CLOSE_TOO_LARGE = 4004
CLOSE_INVALID_HELLO = 4005
