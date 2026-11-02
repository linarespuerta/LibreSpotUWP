"""Drive a fresh throwaway Home Assistant through onboarding, add the integration, connect the
Lumia simulator and check that the media player entity works.

Everything here is local and disposable; the dev credentials below protect nothing real.
"""

from __future__ import annotations

import asyncio
import sys
import time
from typing import Any

import aiohttp

sys.path.insert(0, ".")
from tools.lumia_sim import LumiaSimulator  # noqa: E402

BASE = "http://127.0.0.1:18123"
CLIENT_ID = f"{BASE}/"
USER = {"name": "Dev", "username": "dev", "password": "dev-only-throwaway", "language": "en"}
ENTITY = "media_player.marantz_sala"


async def wait_until(check: Any, what: str, seconds: float = 180.0) -> Any:
    deadline = time.monotonic() + seconds
    last: Exception | None = None
    while time.monotonic() < deadline:
        try:
            result = await check()
            if result:
                return result
        except (aiohttp.ClientError, OSError) as err:
            last = err
        await asyncio.sleep(1)
    raise SystemExit(f"timed out waiting for {what} ({last})")


async def token(http: aiohttp.ClientSession) -> str:
    async with http.get(f"{BASE}/api/onboarding") as response:
        steps = await response.json() if response.status == 200 else []
    user_done = any(step["step"] == "user" and step["done"] for step in steps)
    if not user_done:
        async with http.post(
            f"{BASE}/api/onboarding/users", json={"client_id": CLIENT_ID, **USER}
        ) as response:
            code = (await response.json())["auth_code"]
    else:
        async with http.post(
            f"{BASE}/auth/login_flow",
            json={
                "client_id": CLIENT_ID,
                "handler": ["homeassistant", None],
                "redirect_uri": CLIENT_ID,
            },
        ) as response:
            flow_id = (await response.json())["flow_id"]
        async with http.post(
            f"{BASE}/auth/login_flow/{flow_id}",
            json={
                "client_id": CLIENT_ID,
                "username": USER["username"],
                "password": USER["password"],
            },
        ) as response:
            code = (await response.json())["result"]
    async with http.post(
        f"{BASE}/auth/token",
        data={"grant_type": "authorization_code", "code": code, "client_id": CLIENT_ID},
    ) as response:
        return str((await response.json())["access_token"])


async def add_integration(http: aiohttp.ClientSession) -> str:
    async with http.post(
        f"{BASE}/api/config/config_entries/flow", json={"handler": "lumia_hifi"}
    ) as response:
        flow = await response.json()
    async with http.post(
        f"{BASE}/api/config/config_entries/flow/{flow['flow_id']}", json={"name": "Marantz Sala"}
    ) as response:
        flow = await response.json()
    secret = flow["description_placeholders"]["secret"]
    print("link url shown to the user:", flow["description_placeholders"]["url"])
    async with http.post(
        f"{BASE}/api/config/config_entries/flow/{flow['flow_id']}", json={}
    ) as response:
        result = await response.json()
    assert result["type"] == "create_entry", result
    return str(secret)


async def state(http: aiohttp.ClientSession) -> dict[str, Any] | None:
    async with http.get(f"{BASE}/api/states/{ENTITY}") as response:
        return await response.json() if response.status == 200 else None


async def main() -> None:
    async with aiohttp.ClientSession() as anonymous:

        async def ready() -> bool:
            async with anonymous.get(f"{BASE}/api/onboarding") as response:
                return response.status == 200

        print("waiting for Home Assistant ...")
        await wait_until(ready, "Home Assistant to start")
        access = await token(anonymous)

    headers = {"Authorization": f"Bearer {access}"}
    async with aiohttp.ClientSession(headers=headers) as http:
        secret = await add_integration(http)
        await wait_until(lambda: state(http), "the media player entity")
        current = await state(http)
        assert current is not None and current["state"] == "unavailable", current
        print("entity registered, unavailable until the Lumia connects: ok")

        async with aiohttp.ClientSession() as phone:
            sim = LumiaSimulator(phone, "ws://127.0.0.1:18123/api/lumia_hifi/v1/link", secret)
            await sim.connect()
            sim.start()

            async def playing() -> bool:
                current = await state(http)
                return current is not None and current["state"] == "playing"

            await wait_until(playing, "the entity to report playing", 30)
            current = await state(http)
            assert current is not None
            attributes = current["attributes"]
            assert attributes["media_title"] == "Blue in Green", attributes
            print("playing:", attributes["media_artist"], "-", attributes["media_title"])

            async with http.post(
                f"{BASE}/api/services/media_player/media_pause", json={"entity_id": ENTITY}
            ) as response:
                assert response.status == 200, await response.text()

            async def paused() -> bool:
                current = await state(http)
                return current is not None and current["state"] == "paused"

            await wait_until(paused, "the entity to report paused", 10)
            print("media_pause reached the simulator and the state followed: ok")
            await sim.close()

            async def unavailable() -> bool:
                current = await state(http)
                return current is not None and current["state"] == "unavailable"

            await wait_until(unavailable, "the entity to become unavailable", 10)
            print("disconnect -> unavailable: ok")
    print("SMOKE OK")


if __name__ == "__main__":
    asyncio.run(main())
