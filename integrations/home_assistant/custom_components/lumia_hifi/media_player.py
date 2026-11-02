"""Media player entity backed by the Lumia link."""

from __future__ import annotations

from datetime import datetime
from typing import Any

from homeassistant.components.media_player import (
    MediaPlayerEntity,
    MediaPlayerEntityFeature,
    MediaPlayerState,
    MediaType,
    RepeatMode,
)
from homeassistant.core import HomeAssistant
from homeassistant.helpers.device_registry import DeviceInfo
from homeassistant.helpers.entity_platform import AddConfigEntryEntitiesCallback

from . import LumiaConfigEntry
from .const import DOMAIN
from .link import LumiaLink
from .protocol import MAX_DURATION_MS

PARALLEL_UPDATES = 1

_STATES = {
    "playing": MediaPlayerState.PLAYING,
    "paused": MediaPlayerState.PAUSED,
    "buffering": MediaPlayerState.BUFFERING,
    "idle": MediaPlayerState.IDLE,
}
_FEATURES = {
    "play": MediaPlayerEntityFeature.PLAY,
    "pause": MediaPlayerEntityFeature.PAUSE,
    "next": MediaPlayerEntityFeature.NEXT_TRACK,
    "previous": MediaPlayerEntityFeature.PREVIOUS_TRACK,
    "seek": MediaPlayerEntityFeature.SEEK,
    "volume_set": MediaPlayerEntityFeature.VOLUME_SET | MediaPlayerEntityFeature.VOLUME_STEP,
    "shuffle_set": MediaPlayerEntityFeature.SHUFFLE_SET,
    "repeat_set": MediaPlayerEntityFeature.REPEAT_SET,
}
_REPEAT_FROM_WIRE = {"off": RepeatMode.OFF, "all": RepeatMode.ALL, "one": RepeatMode.ONE}
_REPEAT_TO_WIRE = {value: key for key, value in _REPEAT_FROM_WIRE.items()}


async def async_setup_entry(
    hass: HomeAssistant,
    entry: LumiaConfigEntry,
    async_add_entities: AddConfigEntryEntitiesCallback,
) -> None:
    """Add the media player for this entry."""
    async_add_entities([LumiaMediaPlayer(entry)])


class LumiaMediaPlayer(MediaPlayerEntity):
    """The Lumia as a Home Assistant media player."""

    _attr_has_entity_name = True
    _attr_name = None
    _attr_should_poll = False
    _attr_media_content_type = MediaType.MUSIC
    _attr_media_image_remotely_accessible = True

    def __init__(self, entry: LumiaConfigEntry) -> None:
        self._link: LumiaLink = entry.runtime_data
        self._attr_unique_id = entry.entry_id
        self._attr_device_info = DeviceInfo(
            identifiers={(DOMAIN, entry.entry_id)},
            name=entry.title,
            manufacturer="Lumia HiFi",
            model="Spotify Connect receiver",
        )

    async def async_added_to_hass(self) -> None:
        """Follow link changes."""
        self.async_on_remove(self._link.async_add_listener(self.async_write_ha_state))

    def _field(self, key: str) -> Any:
        state = self._link.state
        return None if state is None else state.get(key)

    @property
    def available(self) -> bool:
        """The entity is unavailable while no Lumia is connected."""
        return self._link.connected

    @property
    def supported_features(self) -> MediaPlayerEntityFeature:
        """Features follow the commands the app announced."""
        features = MediaPlayerEntityFeature(0)
        if self._link.device is not None:
            for command in self._link.device.commands:
                features |= _FEATURES[command]
        return features

    @property
    def state(self) -> MediaPlayerState | None:
        """Playback state."""
        value = self._field("state")
        return _STATES.get(value) if isinstance(value, str) else None

    @property
    def volume_level(self) -> float | None:
        """Volume from 0 to 1."""
        volume: float | None = self._field("volume")
        return volume

    @property
    def media_title(self) -> str | None:
        """Current title."""
        title: str | None = self._field("title")
        return title

    @property
    def media_artist(self) -> str | None:
        """Current artist."""
        artist: str | None = self._field("artist")
        return artist

    @property
    def media_album_name(self) -> str | None:
        """Current album."""
        album: str | None = self._field("album")
        return album

    @property
    def media_content_id(self) -> str | None:
        """Spotify URI of the current track."""
        content_id: str | None = self._field("content_id")
        return content_id

    @property
    def media_image_url(self) -> str | None:
        """Artwork on Spotify's CDN."""
        url: str | None = self._field("artwork_url")
        return url

    @property
    def media_duration(self) -> int | None:
        """Duration in seconds."""
        milliseconds = self._field("duration_ms")
        return milliseconds // 1000 if milliseconds else None

    @property
    def media_position(self) -> int | None:
        """Position in seconds when the snapshot was received."""
        if self._field("state") == "idle" or self._field("duration_ms") in (None, 0):
            return None
        return int(self._field("position_ms")) // 1000

    @property
    def media_position_updated_at(self) -> datetime | None:
        """When the position was captured."""
        if self.media_position is None:
            return None
        return self._link.state_received_at

    @property
    def shuffle(self) -> bool | None:
        """Shuffle flag."""
        flag: bool | None = self._field("shuffle")
        return flag

    @property
    def repeat(self) -> RepeatMode | None:
        """Repeat mode."""
        value = self._field("repeat")
        return _REPEAT_FROM_WIRE.get(value) if isinstance(value, str) else None

    @property
    def extra_state_attributes(self) -> dict[str, Any]:
        """Link details that have no media player field."""
        attributes: dict[str, Any] = {}
        if self._link.device is not None:
            attributes["app_version"] = self._link.device.app_version
        if self._link.state is not None:
            attributes["session"] = self._link.state["session"]
            attributes["output"] = self._link.state["output"]
        return attributes

    async def async_media_play(self) -> None:
        """Resume playback."""
        await self._link.async_send_command("play")

    async def async_media_pause(self) -> None:
        """Pause playback."""
        await self._link.async_send_command("pause")

    async def async_media_next_track(self) -> None:
        """Skip to the next track."""
        await self._link.async_send_command("next")

    async def async_media_previous_track(self) -> None:
        """Go to the previous track."""
        await self._link.async_send_command("previous")

    async def async_media_seek(self, position: float) -> None:
        """Seek to a position in seconds."""
        position_ms = min(MAX_DURATION_MS, max(0, round(position * 1000)))
        await self._link.async_send_command("seek", position_ms=position_ms)

    async def async_set_volume_level(self, volume: float) -> None:
        """Set the volume from 0 to 1."""
        await self._link.async_send_command("volume_set", volume=round(volume, 3))

    async def async_set_shuffle(self, shuffle: bool) -> None:
        """Enable or disable shuffle."""
        await self._link.async_send_command("shuffle_set", shuffle=shuffle)

    async def async_set_repeat(self, repeat: RepeatMode) -> None:
        """Set the repeat mode."""
        await self._link.async_send_command("repeat_set", repeat=_REPEAT_TO_WIRE[repeat])
