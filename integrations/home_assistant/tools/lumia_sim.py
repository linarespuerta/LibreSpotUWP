"""Scripted Lumia simulator speaking contract v1.

Used by the test suite and by hand against a development Home Assistant:

    LUMIA_SIM_SECRET=<secret> uv run python tools/lumia_sim.py \
        --url ws://localhost:8123/api/lumia_hifi/v1/link

The simulator is deliberately independent of the integration code: it builds frames itself,
so the tests also check that what it sends validates against the JSON Schemas.
"""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import json
import os
import uuid
from typing import Any

import aiohttp

ALL_COMMANDS = (
    "play",
    "pause",
    "next",
    "previous",
    "seek",
    "volume_set",
    "shuffle_set",
    "repeat_set",
)
TRACKS: tuple[dict[str, Any], ...] = (
    {
        "title": "Blue in Green",
        "artist": "Miles Davis",
        "album": "Kind of Blue",
        "artwork_url": "https://i.scdn.co/image/ab67616d00001e02example1",
        "content_id": "spotify:track:0aBcDeFgHiJkLmNoPqRsTu",
        "duration_ms": 337_000,
    },
    {
        "title": "So What",
        "artist": "Miles Davis",
        "album": "Kind of Blue",
        "artwork_url": None,
        "content_id": "spotify:track:1AbCdEfGhIjKlMnOpQrStU",
        "duration_ms": 562_000,
    },
    {
        "title": "Freddie Freeloader",
        "artist": "Miles Davis",
        "album": "Kind of Blue",
        "artwork_url": "https://i.scdn.co/image/ab67616d00001e02example3",
        "content_id": "spotify:track:2BcDeFgHiJkLmNoPqRsTuV",
        "duration_ms": 586_000,
    },
)


class LumiaSimulator:
    """A fake Lumia: connects, says hello, publishes state and obeys commands."""

    def __init__(
        self,
        session: Any,
        url: str,
        secret: str,
        *,
        device_name: str = "Marantz Sala",
        commands: tuple[str, ...] = ALL_COMMANDS,
        device_id: str | None = None,
        nack: str | None = None,
        answer: bool = True,
        first_track: int = 0,
    ) -> None:
        self._session = session
        self._url = url
        self._secret = secret
        self.device_name = device_name
        self.commands = commands
        self.device_id = device_id or str(uuid.uuid4())
        self.nack = nack  # reply ok=false with this error to every command
        self.answer = answer  # False: never acknowledge (timeout tests)
        self.track = first_track
        self.playing = True
        self.position_ms = 61_250
        self.volume = 0.5
        self.shuffle = False
        self.repeat = "off"
        self.seq = 0
        self.ws: aiohttp.ClientWebSocketResponse | None = None
        self.sent: list[str] = []  # raw frames sent to Home Assistant
        self.received: list[str] = []  # raw frames received from Home Assistant
        self._task: asyncio.Task[None] | None = None

    # -- connection -------------------------------------------------------------------

    async def connect(self) -> None:
        """Open the WebSocket and send hello followed by the first snapshot."""
        self.ws = await self._session.ws_connect(
            self._url, headers={"Authorization": f"Bearer {self._secret}"}
        )
        await self.send(
            {
                "v": 1,
                "type": "hello",
                "device_id": self.device_id,
                "device_name": self.device_name,
                "app_version": "1.0.34.0",
                "commands": list(self.commands),
            }
        )
        await self.send_state()

    def start(self) -> None:
        """Process incoming commands in the background."""
        self._task = asyncio.create_task(self._pump())

    async def close(self) -> None:
        """Close the connection and stop the background task."""
        if self.ws is not None and not self.ws.closed:
            await self.ws.close()
        if self._task is not None:
            with contextlib.suppress(asyncio.CancelledError, Exception):
                await self._task

    @property
    def close_code(self) -> int | None:
        """WebSocket close code once closed."""
        return None if self.ws is None else self.ws.close_code

    # -- frames -----------------------------------------------------------------------

    async def send(self, frame: dict[str, Any]) -> None:
        """Send one frame."""
        assert self.ws is not None
        text = json.dumps(frame, separators=(",", ":"))
        self.sent.append(text)
        await self.ws.send_str(text)

    def snapshot(self) -> dict[str, Any]:
        """Build the current full state frame."""
        track = TRACKS[self.track]
        frame = {
            "v": 1,
            "type": "state",
            "seq": self.seq,
            "state": "playing" if self.playing else "paused",
            **track,
            "position_ms": self.position_ms,
            "volume": self.volume,
            "shuffle": self.shuffle,
            "repeat": self.repeat,
            "session": "connected",
            "output": "local",
        }
        self.seq += 1
        return frame

    async def send_state(self) -> None:
        """Publish a snapshot."""
        await self.send(self.snapshot())

    async def _pump(self) -> None:
        assert self.ws is not None
        async for message in self.ws:
            if message.type is aiohttp.WSMsgType.TEXT:
                self.received.append(message.data)
                frame = json.loads(message.data)
                if frame.get("type") == "command":
                    await self._on_command(frame)

    async def _on_command(self, frame: dict[str, Any]) -> None:
        if not self.answer:
            return
        if self.nack is not None:
            await self.send(
                {"v": 1, "type": "ack", "id": frame["id"], "ok": False, "error": self.nack}
            )
            return
        self._apply(frame)
        await self.send({"v": 1, "type": "ack", "id": frame["id"], "ok": True})
        await self.send_state()

    def _apply(self, frame: dict[str, Any]) -> None:
        name = frame["name"]
        if name == "play":
            self.playing = True
        elif name == "pause":
            self.playing = False
        elif name in ("next", "previous"):
            step = 1 if name == "next" else -1
            self.track = (self.track + step) % len(TRACKS)
            self.position_ms = 0
        elif name == "seek":
            self.position_ms = frame["position_ms"]
        elif name == "volume_set":
            self.volume = frame["volume"]
        elif name == "shuffle_set":
            self.shuffle = frame["shuffle"]
        elif name == "repeat_set":
            self.repeat = frame["repeat"]

    async def run_forever(self) -> None:
        """Interactive mode: advance the position once a second and roll tracks over."""
        await self.connect()
        self.start()
        while self.ws is not None and not self.ws.closed:
            await asyncio.sleep(1)
            if self.playing:
                self.position_ms += 1000
                if self.position_ms >= TRACKS[self.track]["duration_ms"]:
                    self.track = (self.track + 1) % len(TRACKS)
                    self.position_ms = 0
                    await self.send_state()


async def _main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", required=True, help="ws://<host>:8123/api/lumia_hifi/v1/link")
    parser.add_argument("--name", default="Marantz Sala")
    parser.add_argument("--secret", default=os.environ.get("LUMIA_SIM_SECRET"))
    args = parser.parse_args()
    if not args.secret:
        parser.error("pass --secret or set LUMIA_SIM_SECRET")
    async with aiohttp.ClientSession() as session:
        await LumiaSimulator(session, args.url, args.secret, device_name=args.name).run_forever()


if __name__ == "__main__":
    asyncio.run(_main())
