"use strict";

const PROTOCOL_VERSION = 1;
const PORT_NAME = "reprise-youtube-music";
const SNAPSHOT_INTERVAL_MS = 500;

let port = null;
let reconnectTimer = null;
let pendingSnapshotTimer = null;

function connectToServiceWorker() {
  clearTimeout(reconnectTimer);
  reconnectTimer = null;

  try {
    port = chrome.runtime.connect({ name: PORT_NAME });
  } catch (_error) {
    scheduleReconnect();
    return;
  }

  port.onMessage.addListener((message) => {
    if (message?.type === "command") {
      void executeCommand(message);
    }
  });

  port.onDisconnect.addListener(() => {
    port = null;
    scheduleReconnect();
  });

  sendSnapshot();
}

function scheduleReconnect() {
  if (reconnectTimer) {
    return;
  }
  reconnectTimer = setTimeout(connectToServiceWorker, 1000);
}

function currentMediaElement() {
  const videos = [...document.querySelectorAll("video")];
  return (
    videos.find((video) => video.currentSrc || Number.isFinite(video.duration)) ??
    videos[0] ??
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

function readSnapshot() {
  const media = currentMediaElement();
  const metadata = currentMetadata();
  const title =
    textFrom("ytmusic-player-bar .title") || cleanString(metadata?.title);
  const bylineLinks = [
    ...document.querySelectorAll(
      "ytmusic-player-bar .byline a, ytmusic-player-bar .subtitle a"
    ),
  ]
    .map((node) => cleanString(node.textContent))
    .filter(Boolean);
  const artist = cleanString(metadata?.artist) || bylineLinks[0] || "";
  const album = cleanString(metadata?.album) || bylineLinks[1] || "";
  const artworkUrl = metadataArtwork(metadata) || domArtwork();
  const duration = finiteNonnegative(media?.duration);
  const position = finiteNonnegative(media?.currentTime);
  const volume = media
    ? media.muted
      ? 0
      : Math.round(media.volume * 100)
    : 100;
  const state = !title || !media || media.ended
    ? "stopped"
    : media.paused
      ? "paused"
      : "playing";
  const pageURL = new URL(window.location.href);

  return {
    type: "snapshot",
    protocolVersion: PROTOCOL_VERSION,
    state,
    title,
    album,
    artist,
    duration,
    position,
    volume,
    artworkUrl,
    videoId: cleanString(pageURL.searchParams.get("v")),
    trackUrl: pageURL.origin === "https://music.youtube.com"
      ? pageURL.href
      : null,
    visible: document.visibilityState === "visible",
  };
}

function sendSnapshot() {
  if (!port) {
    return;
  }

  try {
    port.postMessage(readSnapshot());
  } catch (_error) {
    port = null;
    scheduleReconnect();
  }
}

function scheduleSnapshot(delay = 50) {
  clearTimeout(pendingSnapshotTimer);
  pendingSnapshotTimer = setTimeout(() => {
    pendingSnapshotTimer = null;
    sendSnapshot();
  }, delay);
}

async function executeCommand(message) {
  let error = null;

  try {
    const media = currentMediaElement();
    if (!media) {
      throw new Error("재생 중인 YouTube Music 미디어가 없습니다.");
    }

    switch (message.command) {
      case "previous":
        clickPlayerButton("previous");
        break;
      case "pause":
        media.pause();
        break;
      case "playPause":
        if (media.paused) {
          await media.play();
        } else {
          media.pause();
        }
        break;
      case "stop":
        media.pause();
        media.currentTime = 0;
        break;
      case "next":
        clickPlayerButton("next");
        break;
      case "seek":
        if (!Number.isFinite(message.position)) {
          throw new Error("올바르지 않은 재생 위치입니다.");
        }
        media.currentTime = Math.min(
          Math.max(message.position, 0),
          Number.isFinite(media.duration) ? media.duration : message.position
        );
        break;
      case "setVolume":
        if (!Number.isFinite(message.volume)) {
          throw new Error("올바르지 않은 음량입니다.");
        }
        media.muted = false;
        media.volume = Math.min(Math.max(message.volume, 0), 100) / 100;
        break;
      default:
        throw new Error("지원하지 않는 명령입니다.");
    }
  } catch (caughtError) {
    error = String(caughtError?.message ?? caughtError).slice(0, 512);
  }

  try {
    port?.postMessage({
      type: "ack",
      protocolVersion: PROTOCOL_VERSION,
      id: message.id,
      success: error === null,
      error,
    });
  } catch (_error) {
    // The periodic reconnect path handles a closed service-worker port.
  }

  scheduleSnapshot(50);
  setTimeout(sendSnapshot, 250);
}

function clickPlayerButton(direction) {
  const selectors = direction === "previous"
    ? [
        "ytmusic-player-bar .previous-button",
        "ytmusic-player-bar [aria-label*='Previous']",
        "ytmusic-player-bar [aria-label*='이전']",
        "ytmusic-player-bar [title*='Previous']",
        "ytmusic-player-bar [title*='이전']",
      ]
    : [
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

function finiteNonnegative(value) {
  return typeof value === "number" && Number.isFinite(value)
    ? Math.max(value, 0)
    : 0;
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

document.addEventListener("play", sendSnapshot, true);
document.addEventListener("pause", sendSnapshot, true);
document.addEventListener("durationchange", sendSnapshot, true);
document.addEventListener("volumechange", sendSnapshot, true);
document.addEventListener("loadedmetadata", sendSnapshot, true);
document.addEventListener("emptied", sendSnapshot, true);
document.addEventListener("ended", sendSnapshot, true);
document.addEventListener("visibilitychange", sendSnapshot);

const observer = new MutationObserver(() => {
  scheduleSnapshot();
});
observer.observe(document.documentElement, {
  subtree: true,
  childList: true,
  characterData: true,
  attributes: true,
  attributeFilter: ["src", "title"],
});

setInterval(sendSnapshot, SNAPSHOT_INTERVAL_MS);
connectToServiceWorker();
