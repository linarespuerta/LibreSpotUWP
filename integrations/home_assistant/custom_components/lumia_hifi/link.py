"""One live WebSocket link to a Lumia, owned by a config entry."""

from __future__ import annotations

import asyncio
import logging
import uuid
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime
from typing import Any

from aiohttp import WSMsgType, web
from homeassistant.core import HomeAssistant, callback
from homeassistant.exceptions import HomeAssistantError
from homeassistant.util import dt as dt_util

from .const import (
    CLOSE_HELLO_FIRST,
    CLOSE_INVALID_HELLO,
    CLOSE_REPLACED,
    CLOSE_TOO_LARGE,
    CLOSE_UNSUPPORTED_VERSION,
    COMMAND_TIMEOUT_SECONDS,
)
from .protocol import (
    LUMIA_TO_HA,
    FrameError,
    build_command,
    build_error,
    parse_frame,
)

_LOGGER = logging.getLogger(__name__)


@dataclass(frozen=True, slots=True)
class LumiaDevice:
    """Identity announced in the ``hello`` frame."""

    device_id: str
    device_name: str
    app_version: str
    commands: frozenset[str]


class LumiaLink:
    """Accepts the Lumia's WebSocket, tracks its state and relays commands."""

    def __init__(self, hass: HomeAssistant) -> None:
        self._hass = hass
        self._ws: web.WebSocketResponse | None = None
        self._send_lock = asyncio.Lock()
        self._pending: dict[str, asyncio.Future[dict[str, Any]]] = {}
        self._listeners: list[Callable[[], None]] = []
        self.device: LumiaDevice | None = None
        self.state: dict[str, Any] | None = None
        self.state_received_at: datetime | None = None
        self.last_seen: datetime | None = None

    @property
    def connected(self) -> bool:
        """Return True once the Lumia is connected and has said hello."""
        return self._ws is not None and not self._ws.closed and self.device is not None

    @callback
    def async_add_listener(self, listener: Callable[[], None]) -> Callable[[], None]:
        """Register a callback fired on every change; return an unsubscribe function."""
        self._listeners.append(listener)

        @callback
        def remove() -> None:
            if listener in self._listeners:
                self._listeners.remove(listener)

        return remove

    @callback
    def _notify(self) -> None:
        for listener in list(self._listeners):
            listener()

    def _reset_session(self) -> None:
        self.device = None
        self.state = None
        self.state_received_at = None
        for future in self._pending.values():
            if not future.done():
                future.set_exception(HomeAssistantError("The Lumia disconnected"))
        self._pending.clear()

    async def async_close(self) -> None:
        """Close the live connection (config entry unloaded or secret rotated)."""
        ws, self._ws = self._ws, None
        self._reset_session()
        if ws is not None and not ws.closed:
            await ws.close(code=1001)
        self._notify()

    async def async_serve(self, ws: web.WebSocketResponse) -> None:
        """Run an already-accepted and authenticated connection until it ends."""
        previous, self._ws = self._ws, ws
        self._reset_session()
        if previous is not None and not previous.closed:
            await previous.close(code=CLOSE_REPLACED)
        self._notify()
        try:
            async for message in ws:
                if message.type is WSMsgType.TEXT:
                    keep = await self._async_handle_text(ws, message.data)
                elif message.type is WSMsgType.BINARY:
                    keep = await self._async_reject(ws, "invalid_frame")
                else:  # ERROR, e.g. a frame above the transport limit
                    keep = False
                if not keep:
                    break
        finally:
            if self._ws is ws:
                self._ws = None
                self._reset_session()
                self._notify()

    async def _async_handle_text(self, ws: web.WebSocketResponse, raw: str) -> bool:
        """Handle one text frame; return False if the connection must end."""
        try:
            frame = parse_frame(raw, accept=LUMIA_TO_HA)
        except FrameError as err:
            _LOGGER.debug("Rejected frame from Lumia: %s", err.code)
            return await self._async_reject(ws, err.code, err.frame_type)

        self.last_seen = dt_util.utcnow()
        kind = frame["type"]
        if self.device is None:
            if kind != "hello":
                await self._async_close(ws, CLOSE_HELLO_FIRST)
                return False
            self.device = LumiaDevice(
                device_id=frame["device_id"],
                device_name=frame["device_name"],
                app_version=frame["app_version"],
                commands=frozenset(frame["commands"]),
            )
            self._notify()
        elif kind == "hello":
            await self._async_try_send(ws, build_error("invalid_frame"))
        elif kind == "state":
            self.state = frame
            self.state_received_at = self.last_seen
            self._notify()
        elif kind == "ack":
            # Acks change no entity state; the state frame that follows does.
            future = self._pending.pop(frame["id"], None)
            if future is not None and not future.done():
                future.set_result(frame)
        elif kind == "error":
            _LOGGER.warning("The Lumia reported a protocol error: %s", frame["code"])
        return True

    async def _async_reject(
        self, ws: web.WebSocketResponse, code: str, frame_type: str | None = None
    ) -> bool:
        """Answer a frame that broke the contract; return False if the connection must end."""
        if code == "unsupported_version":
            await self._async_close(ws, CLOSE_UNSUPPORTED_VERSION)
            return False
        await self._async_try_send(ws, build_error(code))
        if code == "too_large":
            await self._async_close(ws, CLOSE_TOO_LARGE)
            return False
        if self.device is None:
            await self._async_close(
                ws, CLOSE_INVALID_HELLO if frame_type == "hello" else CLOSE_HELLO_FIRST
            )
            return False
        return True

    async def _async_try_send(self, ws: web.WebSocketResponse, text: str) -> bool:
        """Best-effort protocol reply: a peer that is already gone is not an error."""
        try:
            await self._async_send(ws, text)
        except HomeAssistantError:
            return False
        return True

    @staticmethod
    async def _async_close(ws: web.WebSocketResponse, code: int) -> None:
        try:
            await ws.close(code=code)
        except (ConnectionError, RuntimeError):
            pass

    async def _async_send(self, ws: web.WebSocketResponse, text: str) -> None:
        async with self._send_lock:
            if ws.closed:
                raise HomeAssistantError("The Lumia is not connected")
            try:
                await ws.send_str(text)
            except (ConnectionError, RuntimeError) as err:
                raise HomeAssistantError("The Lumia is not connected") from err

    async def async_send_command(self, name: str, **fields: Any) -> None:
        """Send a command and wait for the Lumia's acknowledgement."""
        ws, device = self._ws, self.device
        if ws is None or ws.closed or device is None:
            raise HomeAssistantError("The Lumia is not connected")
        if name not in device.commands:
            raise HomeAssistantError(f"The Lumia does not support '{name}'")
        command_id = uuid.uuid4().hex
        try:
            frame = build_command(command_id, name, **fields)
        except FrameError as err:
            raise HomeAssistantError(f"Invalid value for '{name}': {err.detail}") from err
        future: asyncio.Future[dict[str, Any]] = self._hass.loop.create_future()
        self._pending[command_id] = future
        try:
            await self._async_send(ws, frame)
            async with asyncio.timeout(COMMAND_TIMEOUT_SECONDS):
                ack = await future
        except TimeoutError as err:
            raise HomeAssistantError(f"The Lumia did not answer '{name}' in time") from err
        finally:
            self._pending.pop(command_id, None)
        if not ack["ok"]:
            raise HomeAssistantError(f"The Lumia rejected '{name}': {ack['error']}")
