"use strict";

const BRIDGE_URL = "ws://127.0.0.1:19436";
const BRIDGE_SUBPROTOCOL = "reprise-youtube-music-v1";
const PROTOCOL_VERSION = 1;
const EXTENSION_VERSION = chrome.runtime.getManifest().version;
const SNAPSHOT_FRESHNESS_MS = 4000;
const ALLOWED_COMMANDS = new Set([
  "previous",
  "pause",
  "playPause",
  "stop",
  "next",
  "seek",
  "setVolume",
]);

const tabConnections = new Map();
let socket = null;
let reconnectTimer = null;
let reconnectDelay = 500;
let outboundSequence = 0;
let lastSelectedTabId = null;

function ensureBridgeConnection() {
  if (
    socket &&
    (socket.readyState === WebSocket.OPEN ||
      socket.readyState === WebSocket.CONNECTING)
  ) {
    return;
  }

  clearTimeout(reconnectTimer);
  reconnectTimer = null;

  try {
    socket = new WebSocket(BRIDGE_URL, BRIDGE_SUBPROTOCOL);
  } catch (_error) {
    scheduleReconnect();
    return;
  }

  socket.addEventListener("open", () => {
    reconnectDelay = 500;
    sendToReprise({
      type: "hello",
      protocolVersion: PROTOCOL_VERSION,
      extensionVersion: EXTENSION_VERSION,
    });
    sendSelectedSnapshot();
    updateActionBadge();
  });

  socket.addEventListener("message", (event) => {
    receiveCommand(event.data);
  });

  socket.addEventListener("close", () => {
    socket = null;
    updateActionBadge();
    scheduleReconnect();
  });

  socket.addEventListener("error", () => {
    socket?.close();
  });
}

function scheduleReconnect() {
  if (reconnectTimer || tabConnections.size === 0) {
    return;
  }

  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    ensureBridgeConnection();
  }, reconnectDelay);
  reconnectDelay = Math.min(reconnectDelay * 2, 10000);
}

function sendToReprise(message) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    ensureBridgeConnection();
    return false;
  }

  try {
    socket.send(JSON.stringify(message));
    return true;
  } catch (_error) {
    socket.close();
    return false;
  }
}

function receiveCommand(rawMessage) {
  let message;
  try {
    message = JSON.parse(rawMessage);
  } catch (_error) {
    return;
  }

  if (
    !message ||
    message.type !== "command" ||
    message.protocolVersion !== PROTOCOL_VERSION ||
    typeof message.id !== "string" ||
    !ALLOWED_COMMANDS.has(message.command)
  ) {
    return;
  }

  const selected = selectedConnection();
  if (!selected) {
    sendAcknowledgement(message.id, false, "YouTube Music 탭이 없습니다.");
    return;
  }

  selected.port.postMessage(message);
}

function selectedConnection() {
  const now = Date.now();
  const candidates = [...tabConnections.entries()]
    .filter(([, entry]) => {
      return entry.snapshot && now - entry.updatedAt <= SNAPSHOT_FRESHNESS_MS;
    })
    .map(([tabId, entry]) => ({ tabId, ...entry }));

  candidates.sort((left, right) => {
    const leftPlaying = left.snapshot.state === "playing" ? 1 : 0;
    const rightPlaying = right.snapshot.state === "playing" ? 1 : 0;
    if (leftPlaying !== rightPlaying) {
      return rightPlaying - leftPlaying;
    }

    const leftVisible = left.snapshot.visible ? 1 : 0;
    const rightVisible = right.snapshot.visible ? 1 : 0;
    if (leftVisible !== rightVisible) {
      return rightVisible - leftVisible;
    }

    return right.updatedAt - left.updatedAt;
  });

  const selected = candidates[0] ?? null;
  lastSelectedTabId = selected?.tabId ?? null;
  return selected;
}

function sendSelectedSnapshot() {
  const selected = selectedConnection();
  const sequence = outboundSequence++;

  if (!selected) {
    sendToReprise({
      type: "snapshot",
      protocolVersion: PROTOCOL_VERSION,
      sequence,
      tabId: null,
      state: "stopped",
      title: "",
      album: "",
      artist: "",
      duration: 0,
      position: 0,
      volume: 100,
      artworkUrl: null,
      videoId: "",
      trackUrl: null,
    });
    return;
  }

  const snapshot = selected.snapshot;
  sendToReprise({
    type: "snapshot",
    protocolVersion: PROTOCOL_VERSION,
    sequence,
    tabId: selected.tabId,
    state: snapshot.state,
    title: snapshot.title,
    album: snapshot.album,
    artist: snapshot.artist,
    duration: snapshot.duration,
    position: snapshot.position,
    volume: snapshot.volume,
    artworkUrl: snapshot.artworkUrl,
    videoId: snapshot.videoId,
    trackUrl: snapshot.trackUrl,
  });
}

function sendAcknowledgement(id, success, error = null) {
  sendToReprise({
    type: "ack",
    protocolVersion: PROTOCOL_VERSION,
    id,
    success,
    error,
  });
}

function normalizedSnapshot(message) {
  if (!message || message.type !== "snapshot") {
    return null;
  }

  const state = ["playing", "paused", "stopped"].includes(message.state)
    ? message.state
    : "stopped";

  return {
    state,
    title: cleanString(message.title, 512),
    album: cleanString(message.album, 512),
    artist: cleanString(message.artist, 512),
    duration: finiteNumber(message.duration),
    position: finiteNumber(message.position),
    volume: Math.min(Math.max(finiteNumber(message.volume), 0), 100),
    artworkUrl: optionalString(message.artworkUrl, 2048),
    videoId: cleanString(message.videoId, 128),
    trackUrl: optionalString(message.trackUrl, 2048),
    visible: Boolean(message.visible),
  };
}

function cleanString(value, maximumLength) {
  return typeof value === "string" ? value.trim().slice(0, maximumLength) : "";
}

function optionalString(value, maximumLength) {
  const result = cleanString(value, maximumLength);
  return result || null;
}

function finiteNumber(value) {
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function updateActionBadge() {
  const connected = socket?.readyState === WebSocket.OPEN;
  chrome.action.setBadgeText({ text: connected ? "✓" : "" });
  chrome.action.setBadgeBackgroundColor({ color: "#24a148" });
  chrome.action.setTitle({
    title: connected
      ? "Reprise와 연결됨"
      : "Reprise를 실행하고 YouTube Music 탭을 열어 주세요",
  });
}

chrome.runtime.onConnect.addListener((port) => {
  if (port.name !== "reprise-youtube-music") {
    return;
  }

  const tabId = port.sender?.tab?.id;
  if (!Number.isInteger(tabId)) {
    port.disconnect();
    return;
  }

  tabConnections.set(tabId, {
    port,
    snapshot: null,
    updatedAt: Date.now(),
  });
  ensureBridgeConnection();

  port.onMessage.addListener((message) => {
    const entry = tabConnections.get(tabId);
    if (!entry) {
      return;
    }

    if (message?.type === "snapshot") {
      const snapshot = normalizedSnapshot(message);
      if (!snapshot) {
        return;
      }
      entry.snapshot = snapshot;
      entry.updatedAt = Date.now();
      sendSelectedSnapshot();
      return;
    }

    if (
      message?.type === "ack" &&
      typeof message.id === "string" &&
      typeof message.success === "boolean"
    ) {
      sendAcknowledgement(
        message.id,
        message.success,
        optionalString(message.error, 512)
      );
    }
  });

  port.onDisconnect.addListener(() => {
    tabConnections.delete(tabId);
    sendSelectedSnapshot();
    updateActionBadge();
  });
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== "getStatus") {
    return false;
  }

  const selected = selectedConnection();
  sendResponse({
    bridgeConnected: socket?.readyState === WebSocket.OPEN,
    tabCount: tabConnections.size,
    activeTitle: selected?.snapshot?.title ?? "",
  });
  return false;
});

setInterval(() => {
  if (socket?.readyState === WebSocket.OPEN) {
    sendToReprise({
      type: "heartbeat",
      protocolVersion: PROTOCOL_VERSION,
    });
  } else if (tabConnections.size > 0) {
    ensureBridgeConnection();
  }
}, 2000);

updateActionBadge();
