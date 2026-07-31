// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

"use strict";

const PROTOCOL_VERSION = 1;
const PORT_NAME = "reprise-youtube-music";
const SNAPSHOT_INTERVAL_MS = 500;
const PAGE_COMMAND_TIMEOUT_MS = 2500;
const PAGE_TIMESTAMP_MAX_AGE_MS = 30000;
const PAGE_TIMESTAMP_MAX_FUTURE_MS = 5000;
const MAX_PLAYBACK_RATE = 16;
const PAGE_REQUEST_EVENT = "reprise-youtube-music-request-v1";
const PAGE_RESPONSE_EVENT = "reprise-youtube-music-response-v1";
const PAGE_STATE_CHANGED_EVENT = "reprise-youtube-music-state-changed-v1";

let port = null;
let reconnectTimer = null;
let pendingSnapshotTimer = null;
let snapshotInFlight = false;
let snapshotQueued = false;
let nextPageRequestID = 0;
const pendingPageRequests = new Map();

window.addEventListener(PAGE_RESPONSE_EVENT, (event) => {
  if (typeof event.detail !== "string") {
    return;
  }

  let message;
  try {
    message = JSON.parse(event.detail);
  } catch (_error) {
    return;
  }

  const pending = pendingPageRequests.get(message?.id);
  if (!pending) {
    return;
  }

  pendingPageRequests.delete(message.id);
  clearTimeout(pending.timer);
  if (message.success) {
    pending.resolve(message.result ?? null);
  } else {
    pending.reject(new Error(cleanString(message.error) || "YouTube Music 명령이 거부되었습니다."));
  }
});
window.addEventListener(PAGE_STATE_CHANGED_EVENT, () => {
  scheduleSnapshot(0);
});

function requestPage(action, values = {}, timeoutMs = 400) {
  const id = `reprise-${Date.now()}-${nextPageRequestID++}`;

  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      pendingPageRequests.delete(id);
      const error = new Error("YouTube Music 페이지 응답 시간이 초과되었습니다.");
      error.bridgeUnavailable = true;
      reject(error);
    }, timeoutMs);

    pendingPageRequests.set(id, { resolve, reject, timer });
    window.dispatchEvent(
      new CustomEvent(PAGE_REQUEST_EVENT, {
        detail: JSON.stringify({ id, action, ...values }),
      })
    );
  });
}

function connectToServiceWorker() {
  clearTimeout(reconnectTimer);
  reconnectTimer = null;

  let connectedPort;
  try {
    connectedPort = chrome.runtime.connect({ name: PORT_NAME });
  } catch (_error) {
    scheduleReconnect();
    return;
  }
  port = connectedPort;

  connectedPort.onMessage.addListener((message) => {
    if (port !== connectedPort) {
      return;
    }
    if (message?.type === "command") {
      void executeCommand(message, connectedPort);
    }
  });

  connectedPort.onDisconnect.addListener(() => {
    if (port !== connectedPort) {
      return;
    }
    port = null;
    scheduleReconnect();
  });

  void sendSnapshot();
}

function scheduleReconnect() {
  if (reconnectTimer) {
    return;
  }
  reconnectTimer = setTimeout(connectToServiceWorker, 1000);
}

function currentMediaElement() {
  const preferred = [
    ...document.querySelectorAll(
      "ytmusic-player #movie_player video.html5-main-video, " +
        "ytmusic-player-page #movie_player video.html5-main-video, " +
        "#movie_player video.html5-main-video"
    ),
  ];
  const videos = [...document.querySelectorAll("video")];
  const candidates = [...new Set([...preferred, ...videos])]
    .filter((video) => video?.isConnected !== false);

  return (
    candidates.find((video) => {
      return !video.paused && !video.ended && video.readyState > 0;
    }) ??
    candidates.find((video) => {
      return video.readyState > 0 && (
        video.currentSrc || Number.isFinite(video.duration)
      );
    }) ??
    candidates[0] ??
    null
  );
}

function currentMetadata() {
  try {
    return navigator.mediaSession?.metadata ?? null;
  } catch (_error) {
    return null;
  }
}

function normalizedTitle(value) {
  return cleanString(value).toLocaleLowerCase().replace(/\s+/g, " ");
}

function currentTrackMetadata(pageState) {
  const metadata = currentMetadata();
  const sessionTitle = cleanString(metadata?.title);
  const controllerTitle = cleanString(pageState?.title);

  if (controllerTitle) {
    const sameSessionTrack = sessionTitle &&
      normalizedTitle(sessionTitle) === normalizedTitle(controllerTitle);
    return {
      title: controllerTitle,
      artist: sameSessionTrack
        ? cleanString(metadata?.artist)
        : cleanString(pageState?.artist),
      album: sameSessionTrack ? cleanString(metadata?.album) : "",
      artworkUrl: sameSessionTrack ? metadataArtwork(metadata) : domArtwork(),
    };
  }

  if (sessionTitle) {
    return {
      title: sessionTitle,
      artist: cleanString(metadata?.artist),
      album: cleanString(metadata?.album),
      artworkUrl: metadataArtwork(metadata) || domArtwork(),
    };
  }

  const bylineLinks = [
    ...document.querySelectorAll(
      "ytmusic-player-bar .byline a, ytmusic-player-bar .subtitle a"
    ),
  ]
    .map((node) => cleanString(node.textContent))
    .filter(Boolean);
  return {
    title: textFrom("ytmusic-player-bar .title"),
    artist: bylineLinks[0] || "",
    album: bylineLinks[1] || "",
    artworkUrl: domArtwork(),
  };
}

function readSnapshot(pageState = null) {
  const media = currentMediaElement();
  const track = currentTrackMetadata(pageState);
  const duration = finiteNonnegative(
    pageState?.duration,
    finiteNonnegative(media?.duration)
  );
  const position = finiteNonnegative(
    pageState?.position,
    finiteNonnegative(media?.currentTime)
  );
  const fallbackVolume = media
    ? media.muted
      ? 0
      : Math.round(finiteNonnegative(media.volume, 1) * 100)
    : 100;
  const volume = Math.min(
    Math.max(finiteNonnegative(pageState?.volume, fallbackVolume), 0),
    100
  );
  const mediaPlaybackRate = finiteNonnegative(media?.playbackRate, 1);
  const rawPlaybackRate = finiteNonnegative(
    pageState?.playbackRate,
    mediaPlaybackRate
  );
  const playbackRate = Math.min(
    rawPlaybackRate > 0 ? rawPlaybackRate : 1,
    MAX_PLAYBACK_RATE
  );
  const capturedAtMs = validatedCapturedAtMs(pageState?.capturedAtMs);
  const state = ["playing", "paused", "stopped"].includes(pageState?.state)
    ? pageState.state
    : !track.title || !media || media.ended
      ? "stopped"
      : media.paused
        ? "paused"
        : "playing";
  const pageURL = new URL(window.location.href);
  const videoId = cleanString(pageState?.videoId) ||
    cleanString(pageURL.searchParams.get("v"));
  const trackUrl = pageURL.origin === "https://music.youtube.com"
    ? videoId
      ? `https://music.youtube.com/watch?v=${encodeURIComponent(videoId)}`
      : pageURL.href
    : null;

  return {
    type: "snapshot",
    protocolVersion: PROTOCOL_VERSION,
    state,
    title: track.title,
    album: track.album,
    artist: track.artist,
    duration,
    position: duration > 0 ? Math.min(position, duration) : position,
    volume: Math.round(volume),
    playbackRate,
    capturedAtMs,
    artworkUrl: track.artworkUrl,
    videoId,
    trackUrl,
    visible: document.visibilityState === "visible",
  };
}

async function sendSnapshot() {
  const targetPort = port;
  if (!targetPort) {
    return;
  }
  if (snapshotInFlight) {
    snapshotQueued = true;
    return;
  }

  snapshotInFlight = true;
  try {
    let pageState = null;
    try {
      pageState = await requestPage("snapshot");
    } catch (_error) {
      // Older/unsupported pages continue with the active media element.
    }
    if (port === targetPort) {
      targetPort.postMessage(readSnapshot(pageState));
    }
  } catch (_error) {
    if (port === targetPort) {
      port = null;
      scheduleReconnect();
    }
  } finally {
    snapshotInFlight = false;
    if (snapshotQueued) {
      snapshotQueued = false;
      scheduleSnapshot(0);
    }
  }
}

function scheduleSnapshot(delay = 50) {
  clearTimeout(pendingSnapshotTimer);
  pendingSnapshotTimer = setTimeout(() => {
    pendingSnapshotTimer = null;
    void sendSnapshot();
  }, delay);
}

async function executeCommand(message, replyPort) {
  let error = null;

  try {
    try {
      await requestPage(
        "command",
        {
          command: message.command,
          position: message.position,
          volume: message.volume,
        },
        PAGE_COMMAND_TIMEOUT_MS
      );
    } catch (pageError) {
      if (!pageError?.bridgeUnavailable &&
          !String(pageError?.message).includes("제어기를 찾지 못했습니다")) {
        throw pageError;
      }
      await executeMediaFallback(message);
    }
  } catch (caughtError) {
    error = String(caughtError?.message ?? caughtError).slice(0, 512);
  }

  try {
    replyPort.postMessage({
      type: "ack",
      protocolVersion: PROTOCOL_VERSION,
      id: message.id,
      success: error === null,
      error,
    });
  } catch (_error) {
    // The periodic reconnect path handles a closed service-worker port.
  }

  scheduleSnapshot(0);
  setTimeout(() => void sendSnapshot(), 250);
}

async function executeMediaFallback(message) {
  const media = currentMediaElement();
  if (!media) {
    throw new Error("재생 중인 YouTube Music 미디어가 없습니다.");
  }

  switch (message.command) {
    case "previous": {
      const beforeSource = media.currentSrc;
      const beforePosition = finiteNonnegative(media.currentTime);
      clickPlayerButton("previous");
      const confirmed = await waitFor(() => {
        const current = currentMediaElement();
        if (!current) {
          return false;
        }
        const sourceChanged = beforeSource && current.currentSrc &&
          beforeSource !== current.currentSrc;
        const restarted = beforePosition > 1.5 &&
          finiteNonnegative(current.currentTime) <= 1.5;
        return current !== media || sourceChanged || restarted;
      }, 2000);
      if (!confirmed) {
        throw new Error("이전 곡 전환을 확인하지 못했습니다.");
      }
      break;
    }
    case "pause":
      media.pause();
      if (!await waitFor(() => media.paused || media.ended, 1200)) {
        throw new Error("일시 정지를 확인하지 못했습니다.");
      }
      break;
    case "playPause": {
      const wasPaused = media.paused;
      if (wasPaused) {
        await media.play();
      } else {
        media.pause();
      }
      const confirmed = await waitFor(
        () => wasPaused ? !media.paused : media.paused || media.ended,
        1200
      );
      if (!confirmed) {
        throw new Error("재생 상태 변경을 확인하지 못했습니다.");
      }
      break;
    }
    case "stop":
      media.pause();
      media.currentTime = 0;
      if (!await waitFor(() => {
        return (media.paused || media.ended) && media.currentTime <= 1.5;
      }, 1200)) {
        throw new Error("재생 정지를 확인하지 못했습니다.");
      }
      break;
    case "next": {
      const beforeSource = media.currentSrc;
      clickPlayerButton("next");
      const confirmed = await waitFor(() => {
        const current = currentMediaElement();
        if (!current) {
          return false;
        }
        const sourceChanged = beforeSource && current.currentSrc &&
          beforeSource !== current.currentSrc;
        return current !== media || sourceChanged;
      }, 2000);
      if (!confirmed) {
        throw new Error("다음 곡 전환을 확인하지 못했습니다.");
      }
      break;
    }
    case "seek": {
      if (!Number.isFinite(message.position)) {
        throw new Error("올바르지 않은 재생 위치입니다.");
      }
      const requestedPosition = Math.max(message.position, 0);
      const targetPosition = Number.isFinite(media.duration) && media.duration > 0
        ? Math.min(requestedPosition, media.duration)
        : requestedPosition;
      media.currentTime = targetPosition;
      const confirmed = await waitFor(() => {
        return Math.abs(media.currentTime - targetPosition) <= 1.5;
      }, 1200);
      if (!confirmed) {
        throw new Error("변경한 재생 위치를 확인하지 못했습니다.");
      }
      break;
    }
    case "setVolume": {
      if (!Number.isFinite(message.volume)) {
        throw new Error("올바르지 않은 음량입니다.");
      }
      const targetVolume = Math.min(Math.max(message.volume, 0), 100);
      media.muted = targetVolume === 0;
      media.volume = targetVolume / 100;
      const confirmed = await waitFor(() => {
        const actualVolume = media.muted ? 0 : media.volume * 100;
        return Math.abs(actualVolume - targetVolume) <= 1;
      }, 800);
      if (!confirmed) {
        throw new Error("변경한 음량을 확인하지 못했습니다.");
      }
      break;
    }
    default:
      throw new Error("지원하지 않는 명령입니다.");
  }
}

async function waitFor(predicate, timeoutMs, intervalMs = 40) {
  const deadline = Date.now() + timeoutMs;
  let value = predicate();
  while (!value && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
    value = predicate();
  }
  return value;
}

function clickPlayerButton(direction) {
  const selectors = direction === "previous"
    ? [
        "ytmusic-player-bar #previous-button",
        "ytmusic-player-bar .previous-button",
        "ytmusic-player-bar [aria-label*='Previous']",
        "ytmusic-player-bar [aria-label*='이전']",
        "ytmusic-player-bar [title*='Previous']",
        "ytmusic-player-bar [title*='이전']",
      ]
    : [
        "ytmusic-player-bar #next-button",
        "ytmusic-player-bar .next-button",
        "ytmusic-player-bar [aria-label*='Next']",
        "ytmusic-player-bar [aria-label*='다음']",
        "ytmusic-player-bar [title*='Next']",
        "ytmusic-player-bar [title*='다음']",
      ];
  const button = selectors
    .map((selector) => document.querySelector(selector))
    .find(Boolean);

  if (!button) {
    throw new Error(
      direction === "previous"
        ? "이전 곡 버튼을 찾지 못했습니다."
        : "다음 곡 버튼을 찾지 못했습니다."
    );
  }
  button.click();
}

function textFrom(selector) {
  return cleanString(document.querySelector(selector)?.textContent);
}

function cleanString(value) {
  return typeof value === "string" ? value.trim() : "";
}

function finiteNonnegative(value, fallback = 0) {
  return typeof value === "number" && Number.isFinite(value)
    ? Math.max(value, 0)
    : Math.max(fallback, 0);
}

function validatedCapturedAtMs(value, now = Date.now()) {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return now;
  }
  if (value < now - PAGE_TIMESTAMP_MAX_AGE_MS ||
      value > now + PAGE_TIMESTAMP_MAX_FUTURE_MS) {
    return now;
  }
  return value;
}

function isVolumeControlEvent(event) {
  const path = typeof event.composedPath === "function"
    ? event.composedPath()
    : [event.target];
  return path.some((node) => {
    const id = typeof node?.id === "string" ? node.id : "";
    if ([
      "volume-slider",
      "expand-volume-slider",
      "expand-volume",
    ].includes(id)) {
      return true;
    }
    return typeof node?.matches === "function" && node.matches(
      "ytmusic-player-bar .volume, ytmusic-player-bar [class~='volume']"
    );
  });
}

function metadataArtwork(metadata) {
  const artwork = Array.isArray(metadata?.artwork) ? metadata.artwork : [];
  for (let index = artwork.length - 1; index >= 0; index -= 1) {
    const source = cleanString(artwork[index]?.src);
    if (source.startsWith("https://")) {
      return source;
    }
  }
  return null;
}

function domArtwork() {
  const image = document.querySelector(
    "ytmusic-player-bar .thumbnail-image-wrapper img, " +
      "ytmusic-player-bar img.image, ytmusic-player-bar img"
  );
  const source = cleanString(image?.currentSrc || image?.src);
  return source.startsWith("https://") ? source : null;
}

for (const eventName of [
  "play",
  "pause",
  "durationchange",
  "volumechange",
  "loadedmetadata",
  "emptied",
  "ended",
]) {
  document.addEventListener(eventName, () => void sendSnapshot(), true);
}
document.addEventListener("visibilitychange", () => void sendSnapshot());
for (const eventName of ["change", "immediate-value-change", "click"]) {
  document.addEventListener(eventName, (event) => {
    if (isVolumeControlEvent(event)) {
      scheduleSnapshot(0);
    }
  }, true);
}

const observerTarget = document.querySelector("ytmusic-player-bar") ?? document.body;
if (observerTarget) {
  const observer = new MutationObserver(() => {
    scheduleSnapshot();
  });
  observer.observe(observerTarget, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
    attributeFilter: ["src", "title", "aria-label"],
  });
}

setInterval(() => void sendSnapshot(), SNAPSHOT_INTERVAL_MS);
connectToServiceWorker();
