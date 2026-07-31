"use strict";

const BRIDGE_URL = "ws://127.0.0.1:19436";
const BRIDGE_SUBPROTOCOL = "reprise-youtube-music-v1";
const PROTOCOL_VERSION = 1;
const EXTENSION_VERSION = chrome.runtime.getManifest().version;
const BROWSER_NAME = detectedBrowserName();
const SNAPSHOT_FRESHNESS_MS = 5000;
const COMMAND_TIMEOUT_MS = 5000;
const MAX_PLAYBACK_RATE = 16;
const MAX_SESSION_COUNT = 32;
const SNAPSHOT_TIMESTAMP_MAX_AGE_MS = 30000;
const SNAPSHOT_TIMESTAMP_MAX_FUTURE_MS = 5000;
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
let outboundSessionSequence = 0;
let lastSelectedTabId = null;
const pendingCommandTargets = new Map();

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
      extensionId: chrome.runtime.id,
      browserName: BROWSER_NAME,
    });
    sendSelectedSnapshot();
    sendSessionList();
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

  const timeout = setTimeout(() => {
    const pending = pendingCommandTargets.get(message.id);
    if (pending?.tabId !== selected.tabId ||
        pending?.port !== selected.port) {
      return;
    }
    pendingCommandTargets.delete(message.id);
    sendAcknowledgement(
      message.id,
      false,
      "YouTube Music 탭의 응답 시간이 초과되었습니다."
    );
  }, COMMAND_TIMEOUT_MS);
  pendingCommandTargets.set(message.id, {
    tabId: selected.tabId,
    port: selected.port,
    timeout,
  });

  try {
    selected.port.postMessage(message);
  } catch (_error) {
    clearTimeout(timeout);
    pendingCommandTargets.delete(message.id);
    sendAcknowledgement(message.id, false, "YouTube Music 탭과 연결이 끊어졌습니다.");
  }
}

function selectedConnection() {
  const now = Date.now();
  const allCandidates = [...tabConnections.entries()]
    .filter(([, entry]) => entry.snapshot)
    .map(([tabId, entry]) => ({ tabId, ...entry }));
  const freshCandidates = allCandidates.filter((candidate) => {
    return now - candidate.updatedAt <= SNAPSHOT_FRESHNESS_MS;
  });
  const candidates = freshCandidates.length > 0
    ? freshCandidates
    : allCandidates;

  candidates.sort((left, right) => {
    const scoreDifference = selectionScore(right) - selectionScore(left);
    if (scoreDifference !== 0) {
      return scoreDifference;
    }
    return right.updatedAt - left.updatedAt;
  });

  const bestScore = candidates[0] ? selectionScore(candidates[0]) : null;
  const selected = candidates.find((candidate) => {
    return candidate.tabId === lastSelectedTabId &&
      selectionScore(candidate) === bestScore;
  }) ?? candidates[0] ?? null;
  lastSelectedTabId = selected?.tabId ?? null;
  return selected;
}

function selectionScore(candidate) {
  const playing = candidate.snapshot.state === "playing" ? 2 : 0;
  const visible = candidate.snapshot.visible ? 1 : 0;
  return playing + visible;
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
      playbackRate: 1,
      capturedAtMs: Date.now(),
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
    playbackRate: snapshot.playbackRate,
    capturedAtMs: snapshot.capturedAtMs,
    artworkUrl: snapshot.artworkUrl,
    videoId: snapshot.videoId,
    trackUrl: snapshot.trackUrl,
  });
}

function sendSessionList() {
  const selected = selectedConnection();
  const orderedEntries = [...tabConnections.entries()]
    .sort(([leftTabId], [rightTabId]) => leftTabId - rightTabId);
  const selectedEntry = selected
    ? orderedEntries.find(([tabId]) => tabId === selected.tabId)
    : null;
  const reportedEntries = selectedEntry
    ? [
        selectedEntry,
        ...orderedEntries.filter(([tabId]) => tabId !== selected.tabId),
      ].slice(0, MAX_SESSION_COUNT)
    : orderedEntries.slice(0, MAX_SESSION_COUNT);
  const sessions = reportedEntries
    .sort(([leftTabId], [rightTabId]) => leftTabId - rightTabId)
    .map(([tabId, entry]) => {
      const snapshot = entry.snapshot;
      return {
        tabId,
        state: snapshot?.state ?? "stopped",
        title: cleanString(snapshot?.title, 512),
        artist: cleanString(snapshot?.artist, 512),
        visible: Boolean(snapshot?.visible),
        updatedAtMs: entry.updatedAt,
      };
    });

  sendToReprise({
    type: "sessions",
    protocolVersion: PROTOCOL_VERSION,
    sequence: outboundSessionSequence++,
    selectedTabId: selected?.tabId ?? null,
    sessions,
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

  const now = Date.now();
  const rawPlaybackRate = finiteNumber(message.playbackRate, 1);
  return {
    state,
    title: cleanString(message.title, 512),
    album: cleanString(message.album, 512),
    artist: cleanString(message.artist, 512),
    duration: finiteNumber(message.duration),
    position: finiteNumber(message.position),
    volume: Math.min(Math.max(finiteNumber(message.volume), 0), 100),
    playbackRate: Math.min(
      rawPlaybackRate > 0 ? rawPlaybackRate : 1,
      MAX_PLAYBACK_RATE
    ),
    capturedAtMs: validatedSnapshotTimestamp(message.capturedAtMs, now),
    artworkUrl: optionalString(message.artworkUrl, 2048),
    videoId: cleanString(message.videoId, 128),
    trackUrl: optionalString(message.trackUrl, 2048),
    visible: Boolean(message.visible),
  };
}

function cleanString(value, maximumLength) {
  return typeof value === "string" ? value.trim().slice(0, maximumLength) : "";
}

function detectedBrowserName() {
  const userAgent = typeof navigator === "object"
    ? String(navigator.userAgent ?? "")
    : "";
  const brands = typeof navigator === "object" &&
      Array.isArray(navigator.userAgentData?.brands)
    ? navigator.userAgentData.brands
        .map((entry) => String(entry?.brand ?? ""))
        .join(" ")
    : "";

  if (/Firefox\//i.test(userAgent)) {
    return "Firefox";
  }
  if (/Edg(?:A|iOS)?\//i.test(userAgent) || /Microsoft Edge/i.test(brands)) {
    return "Microsoft Edge";
  }
  if (/OPR\//i.test(userAgent) || /Opera/i.test(brands)) {
    return "Opera";
  }
  if (/Vivaldi\//i.test(userAgent) || /Vivaldi/i.test(brands)) {
    return "Vivaldi";
  }
  if (/Whale\//i.test(userAgent) || /NAVER Whale/i.test(brands)) {
    return "Naver Whale";
  }
  if (typeof navigator === "object" && navigator.brave) {
    return "Brave";
  }
  if (/Chrome\//i.test(userAgent) || /Google Chrome/i.test(brands)) {
    return "Google Chrome";
  }
  if (/Chromium/i.test(userAgent) || /Chromium/i.test(brands)) {
    return "Chromium";
  }
  return chrome.runtime.id.includes("@") ? "Firefox" : "Chromium";
}

function optionalString(value, maximumLength) {
  const result = cleanString(value, maximumLength);
  return result || null;
}

function finiteNumber(value, fallback = 0) {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function validatedSnapshotTimestamp(value, now = Date.now()) {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return now;
  }
  if (value < now - SNAPSHOT_TIMESTAMP_MAX_AGE_MS ||
      value > now + SNAPSHOT_TIMESTAMP_MAX_FUTURE_MS) {
    return now;
  }
  return value;
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
  sendSessionList();

  port.onMessage.addListener((message) => {
    if (
      message?.type === "ack" &&
      typeof message.id === "string" &&
      typeof message.success === "boolean"
    ) {
      const pending = pendingCommandTargets.get(message.id);
      if (!pending || pending.tabId !== tabId || pending.port !== port) {
        return;
      }
      clearTimeout(pending.timeout);
      pendingCommandTargets.delete(message.id);
      sendAcknowledgement(
        message.id,
        message.success,
        optionalString(message.error, 512)
      );
      return;
    }

    const entry = tabConnections.get(tabId);
    if (!entry || entry.port !== port) {
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
      sendSessionList();
    }
  });

  port.onDisconnect.addListener(() => {
    for (const [commandID, pending] of pendingCommandTargets) {
      if (pending.tabId !== tabId || pending.port !== port) {
        continue;
      }
      clearTimeout(pending.timeout);
      pendingCommandTargets.delete(commandID);
      sendAcknowledgement(
        commandID,
        false,
        "YouTube Music 탭과 연결이 끊어졌습니다."
      );
    }
    if (tabConnections.get(tabId)?.port === port) {
      tabConnections.delete(tabId);
      sendSelectedSnapshot();
      sendSessionList();
      if (tabConnections.size === 0 && typeof socket?.close === "function") {
        socket.close();
      }
      updateActionBadge();
    }
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
