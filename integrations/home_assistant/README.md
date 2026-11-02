# Lumia HiFi for Home Assistant

Custom integration that shows the Lumia HiFi Spotify Connect receiver as a `media_player`.
The Lumia dials Home Assistant over a WebSocket; Home Assistant never connects to the phone.
Wire contract: [`contracts/lumia_hifi/v1`](../../contracts/lumia_hifi/v1/README.md). Plan:
[`docs/HOME_ASSISTANT_CODE_ROUND.md`](../../docs/HOME_ASSISTANT_CODE_ROUND.md).

Status: **not installed on any real Home Assistant.** Verified only against the simulator and a
throwaway container. Behaviour on the Lumia and the LAN is unverified (WP7).

## Install (when approved)

Copy `custom_components/lumia_hifi` into the Home Assistant `custom_components` directory, restart,
then add the "Lumia HiFi" integration. The last config-flow screen shows the address and a
one-time secret to type into the app. Rotate the secret from the integration options.
Rollback: remove the config entry, then delete the `custom_components/lumia_hifi` directory.

## Develop

```
uv run pytest                       # unit and end-to-end tests with the Lumia simulator
uv run ruff check . && uv run mypy  # lint and strict types
dev/smoke.sh                        # fresh throwaway Home Assistant in podman + simulator
```

`tools/lumia_sim.py` is a scripted Lumia that speaks contract v1.
