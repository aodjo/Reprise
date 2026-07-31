// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const pageBridgeSource = readFileSync(
  new URL("../Chromium/page-bridge.js", import.meta.url),
  "utf8"
);
const contentScriptSource = readFileSync(
  new URL("../Chromium/content-script.js", import.meta.url),
  "utf8"
);
const serviceWorkerSource = readFileSync(
  new URL("../Chromium/service-worker.js", import.meta.url),
  "utf8"
);

function numericConstant(source, name) {
  const match = source.match(new RegExp(`const ${name} = (\\d+);`));
  assert.ok(match, `${name} must be a numeric constant`);
  return Number(match[1]);
}

function pageBridgeHarness({ duration = 240 } = {}) {
  const window = new EventTarget();
  const media = {
    isConnected: true,
    paused: false,
    ended: false,
    readyState: 4,
    duration,
    currentTime: 12,
    volume: 0.4,
    muted: false,
    playbackRate: 1,
    currentSrc: "https://example.invalid/audio",
  };
  const calls = [];
  const playerListeners = new Map();
  const player = {
    state: 1,
    duration,
    position: 12,
    volume: 40,
    muted: false,
    playbackRate: 1,
    videoId: "video-1",
    title: "Bridge Song",
    artist: "Bridge Artist",
    getPlayerState() {
      return this.state;
    },
    getDuration() {
      return this.duration;
    },
    getCurrentTime() {
      return this.position;
    },
    getVolume() {
      return this.volume;
    },
    isMuted() {
      return this.muted;
    },
    getPlaybackRate() {
      return this.playbackRate;
    },
    getVideoData() {
      return {
        video_id: this.videoId,
        title: this.title,
        author: this.artist,
      };
    },
    getVideoUrl() {
      return `https://www.youtube.com/watch?v=${this.videoId}`;
    },
    setVolume(value) {
      calls.push(["setVolume", value]);
      this.volume = value;
    },
    mute() {
      calls.push(["mute"]);
      this.muted = true;
    },
    unMute() {
      calls.push(["unMute"]);
      this.muted = false;
    },
    seekTo(value, allowSeekAhead) {
      calls.push(["seekTo", value, allowSeekAhead]);
      this.position = value;
    },
    playVideo() {
      calls.push(["playVideo"]);
      this.state = 1;
    },
    pauseVideo() {
      calls.push(["pauseVideo"]);
      this.state = 2;
    },
    nextVideo() {
      calls.push(["nextVideo"]);
      this.videoId = "video-2";
      this.title = "Next Song";
      this.position = 0;
      this.state = 1;
    },
    previousVideo() {
      calls.push(["previousVideo"]);
      this.position = 0;
      this.state = 1;
    },
    addEventListener(name, listener) {
      playerListeners.set(name, listener);
    },
    removeEventListener(name, listener) {
      if (playerListeners.get(name) === listener) {
        playerListeners.delete(name);
      }
    },
    emit(name) {
      playerListeners.get(name)?.();
    },
  };
  const volumeSlider = {
    value: 40,
    immediateValue: 40,
    dispatchEvent(event) {
      calls.push(["sliderEvent", event.type]);
      if (event.type === "change") {
        playerBar.volume = this.value;
        playerBar.isMuted = false;
      }
    },
  };
  const playerBar = {
    volume: 40,
    isMuted: false,
    querySelector() {
      return volumeSlider;
    },
    updateVolume(value) {
      calls.push(["updateVolume", value]);
      this.volume = value;
      this.isMuted = false;
      volumeSlider.value = value;
      volumeSlider.immediateValue = value;
      player.volume = Math.min(
        Math.max(Math.pow(2, 0.03917 * value + 2.82823) - 7.13431, 0),
        100
      );
      player.muted = false;
    },
  };
  const document = {
    querySelector(selector) {
      if (selector === "ytmusic-player-bar") {
        return playerBar;
      }
      return selector.includes("#movie_player") ? player : null;
    },
    querySelectorAll(selector) {
      return selector.includes("video") || selector.includes("audio")
        ? [media]
        : [];
    },
  };

  vm.runInNewContext(pageBridgeSource, {
    window,
    document,
    Event,
    EventTarget,
    CustomEvent,
    setTimeout,
    clearTimeout,
  });

  let requestNumber = 0;
  function request(action, values = {}) {
    const id = `test-${requestNumber++}`;
    return new Promise((resolve, reject) => {
      const listener = (event) => {
        const message = JSON.parse(event.detail);
        if (message.id !== id) {
          return;
        }
        window.removeEventListener(
          "reprise-youtube-music-response-v1",
          listener
        );
        if (message.success) {
          resolve(message.result);
        } else {
          reject(new Error(message.error));
        }
      };
      window.addEventListener(
        "reprise-youtube-music-response-v1",
        listener
      );
      window.dispatchEvent(
        new CustomEvent("reprise-youtube-music-request-v1", {
          detail: JSON.stringify({ id, action, ...values }),
        })
      );
    });
  }

  return {
    calls,
    media,
    player,
    playerBar,
    request,
    volumeSlider,
    window,
  };
}

test("page bridge reads and controls the actual YouTube player", async () => {
  const harness = pageBridgeHarness();

  const initial = await harness.request("snapshot");
  assert.equal(initial.position, 12);
  assert.equal(initial.duration, 240);
  assert.equal(initial.volume, 40);
  assert.equal(initial.videoId, "video-1");

  const volume = await harness.request("command", {
    command: "setVolume",
    volume: 73,
  });
  assert.equal(volume.volume, 73);
  assert.deepEqual(harness.calls.at(-1), ["updateVolume", 73]);
  assert.notEqual(harness.player.volume, 73);

  harness.playerBar.updateVolume = null;
  const sliderFallback = await harness.request("command", {
    command: "setVolume",
    volume: 55,
  });
  assert.equal(sliderFallback.volume, 55);
  assert.deepEqual(harness.calls.at(-1), ["sliderEvent", "change"]);

  harness.playerBar.volume = 31;
  harness.player.volume = 9;
  const webVolume = await harness.request("snapshot");
  assert.equal(webVolume.volume, 31);

  harness.playerBar.isMuted = true;
  const mutedWebVolume = await harness.request("snapshot");
  assert.equal(mutedWebVolume.volume, 0);
  harness.playerBar.isMuted = false;

  let volumeNotifications = 0;
  harness.window.addEventListener(
    "reprise-youtube-music-state-changed-v1",
    () => {
      volumeNotifications += 1;
    }
  );
  harness.player.emit("onVolumeChange");
  assert.equal(volumeNotifications, 1);

  const seek = await harness.request("command", {
    command: "seek",
    position: 91,
  });
  assert.equal(seek.position, 91);
  assert.deepEqual(harness.calls.at(-1), ["seekTo", 91, true]);

  const pause = await harness.request("command", { command: "pause" });
  assert.equal(pause.state, "paused");

  const play = await harness.request("command", { command: "playPause" });
  assert.equal(play.state, "playing");

  harness.player.position = 45;
  const previous = await harness.request("command", { command: "previous" });
  assert.equal(previous.position, 0);

  const next = await harness.request("command", { command: "next" });
  assert.equal(next.videoId, "video-2");

  harness.player.position = 30;
  const stopped = await harness.request("command", { command: "stop" });
  assert.equal(stopped.state, "paused");
  assert.equal(stopped.position, 0);
});

test("seek is not clamped to zero while duration is unavailable", async () => {
  const harness = pageBridgeHarness({ duration: 0 });

  const result = await harness.request("command", {
    command: "seek",
    position: 37,
  });

  assert.equal(result.position, 37);
  assert.deepEqual(harness.calls.at(-1), ["seekTo", 37, true]);
});

test("content script prefers the currently playing media element", () => {
  const staleVideo = {
    isConnected: true,
    paused: true,
    ended: false,
    readyState: 4,
    currentSrc: "https://example.invalid/stale",
    duration: 180,
  };
  const playingVideo = {
    isConnected: true,
    paused: false,
    ended: false,
    readyState: 4,
    currentSrc: "https://example.invalid/current",
    duration: 200,
  };
  const window = new EventTarget();
  window.location = new URL("https://music.youtube.com/watch?v=current");
  const context = vm.createContext({
    window,
    document: {
      body: {},
      visibilityState: "visible",
      addEventListener() {},
      querySelector() {
        return null;
      },
      querySelectorAll(selector) {
        return selector === "video" ? [staleVideo, playingVideo] : [];
      },
    },
    navigator: { mediaSession: null },
    chrome: {
      runtime: {
        connect() {
          throw new Error("not connected in unit test");
        },
      },
    },
    MutationObserver: class {
      observe() {}
    },
    Event,
    EventTarget,
    CustomEvent,
    URL,
    setTimeout() {
      return 0;
    },
    clearTimeout() {},
    setInterval() {
      return 0;
    },
  });
  vm.runInContext(contentScriptSource, context);

  assert.equal(
    vm.runInContext("currentMediaElement()", context),
    playingVideo
  );

  const capturedAtMs = Date.now();
  context.testPageState = {
    state: "playing",
    duration: 200,
    position: 20,
    volume: 60,
    playbackRate: 2,
    capturedAtMs,
    videoId: "current",
    title: "Current Song",
    artist: "Current Artist",
  };
  const snapshot = vm.runInContext("readSnapshot(testPageState)", context);
  assert.equal(snapshot.playbackRate, 2);
  assert.equal(snapshot.capturedAtMs, capturedAtMs);

  context.testPageState.capturedAtMs = capturedAtMs - 60000;
  const validatedSnapshot = vm.runInContext(
    "readSnapshot(testPageState)",
    context
  );
  assert.ok(validatedSnapshot.capturedAtMs > capturedAtMs - 5000);

  context.volumeEvent = {
    composedPath() {
      return [{ id: "volume-slider" }];
    },
  };
  assert.equal(
    vm.runInContext("isVolumeControlEvent(volumeEvent)", context),
    true
  );

  context.snapshotSchedules = 0;
  vm.runInContext(
    "scheduleSnapshot = () => { snapshotSchedules += 1; }",
    context
  );
  window.dispatchEvent(
    new CustomEvent("reprise-youtube-music-state-changed-v1")
  );
  assert.equal(context.snapshotSchedules, 1);
});

test("tab selection stays sticky while candidates have the same score", () => {
  class FakeWebSocket {
    static CONNECTING = 0;
    static OPEN = 1;

    constructor() {
      this.readyState = FakeWebSocket.CONNECTING;
    }

    addEventListener() {}
    close() {}
    send() {}
  }

  const sentMessages = [];
  const context = vm.createContext({
    chrome: {
      runtime: {
        getManifest() {
          return { version: "1.0.0" };
        },
        id: "test-extension",
        onConnect: { addListener() {} },
        onMessage: { addListener() {} },
      },
      action: {
        setBadgeText() {},
        setBadgeBackgroundColor() {},
        setTitle() {},
      },
    },
    WebSocket: FakeWebSocket,
    Date,
    JSON,
    Map,
    Set,
    sentMessages,
    setTimeout,
    clearTimeout,
    setInterval() {
      return 0;
    },
  });
  vm.runInContext(serviceWorkerSource, context);

  context.navigator = {
    userAgent: "Mozilla/5.0 Chrome/140.0.0.0 Edg/140.0.0.0",
  };
  assert.equal(
    vm.runInContext("detectedBrowserName()", context),
    "Microsoft Edge"
  );
  context.navigator = {
    userAgent: "Mozilla/5.0 Chrome/140.0.0.0",
    brave: {},
  };
  assert.equal(vm.runInContext("detectedBrowserName()", context), "Brave");
  context.navigator = {
    userAgent: "Mozilla/5.0 Firefox/142.0",
  };
  assert.equal(vm.runInContext("detectedBrowserName()", context), "Firefox");

  context.now = Date.now();
  vm.runInContext(
    `
      tabConnections.set(1, {
        port: {},
        snapshot: { state: "playing", visible: true },
        updatedAt: now,
      });
      tabConnections.set(2, {
        port: {},
        snapshot: { state: "playing", visible: true },
        updatedAt: now - 10,
      });
    `,
    context
  );

  assert.equal(vm.runInContext("selectedConnection().tabId", context), 1);
  vm.runInContext("tabConnections.get(2).updatedAt = now + 10", context);
  assert.equal(vm.runInContext("selectedConnection().tabId", context), 1);

  vm.runInContext(
    `
      tabConnections.get(1).updatedAt = now - 6000;
      tabConnections.get(2).updatedAt = now;
    `,
    context
  );
  assert.equal(vm.runInContext("selectedConnection().tabId", context), 2);

  vm.runInContext(
    `
      tabConnections.get(1).snapshot.state = "playing";
      tabConnections.get(1).updatedAt = now - 7000;
      tabConnections.get(2).updatedAt = now - 6000;
    `,
    context
  );
  assert.equal(
    vm.runInContext("selectedConnection().tabId", context),
    2,
    "all-stale candidates retain the sticky fallback"
  );

  context.snapshotTimestamp = Date.now();
  const normalized = vm.runInContext(
    `normalizedSnapshot({
      type: "snapshot",
      state: "playing",
      title: "Bridge Song",
      album: "Bridge Album",
      artist: "Bridge Artist",
      duration: 200,
      position: 20,
      volume: 60,
      playbackRate: 2,
      capturedAtMs: snapshotTimestamp,
      artworkUrl: null,
      videoId: "video-1",
      trackUrl: "https://music.youtube.com/watch?v=video-1",
      visible: true,
    })`,
    context
  );
  assert.equal(normalized.playbackRate, 2);
  assert.equal(normalized.capturedAtMs, context.snapshotTimestamp);

  context.normalized = normalized;
  vm.runInContext(
    `
      tabConnections.clear();
      lastSelectedTabId = null;
      tabConnections.set(3, {
        port: {},
        snapshot: normalized,
        updatedAt: Date.now(),
      });
      socket = {
        readyState: WebSocket.OPEN,
        send(value) { sentMessages.push(JSON.parse(value)); },
      };
      sendSelectedSnapshot();
    `,
    context
  );
  const forwarded = sentMessages.at(-1);
  assert.equal(forwarded.playbackRate, 2);
  assert.equal(forwarded.capturedAtMs, context.snapshotTimestamp);

  vm.runInContext(
    `
      tabConnections.set(4, {
        port: {},
        snapshot: {
          ...normalized,
          state: "paused",
          title: "Background Song",
          artist: "Background Artist",
          visible: false,
        },
        updatedAt: Date.now(),
      });
      sendSessionList();
    `,
    context
  );
  const sessionList = sentMessages.at(-1);
  assert.equal(sessionList.type, "sessions");
  assert.equal(sessionList.protocolVersion, 1);
  assert.equal(sessionList.selectedTabId, 3);
  assert.equal(sessionList.sessions.length, 2);
  assert.deepEqual(
    Array.from(sessionList.sessions, (session) => session.tabId),
    [3, 4]
  );
  assert.equal(sessionList.sessions[0].title, "Bridge Song");
  assert.equal(sessionList.sessions[0].visible, true);
  assert.equal(sessionList.sessions[1].title, "Background Song");
  assert.equal(sessionList.sessions[1].artist, "Background Artist");
  assert.equal(sessionList.sessions[1].visible, false);
});

test("service worker binds tab state and acknowledgements to the exact port", () => {
  let connectListener = null;
  const socketMessages = [];

  class FakeWebSocket {
    static CONNECTING = 0;
    static OPEN = 1;

    constructor() {
      this.readyState = FakeWebSocket.CONNECTING;
    }

    addEventListener() {}
    close() {}
    send() {}
  }

  function makePort(tabId) {
    let messageListener = null;
    let disconnectListener = null;
    const posted = [];
    return {
      name: "reprise-youtube-music",
      sender: { tab: { id: tabId } },
      posted,
      onMessage: {
        addListener(listener) {
          messageListener = listener;
        },
      },
      onDisconnect: {
        addListener(listener) {
          disconnectListener = listener;
        },
      },
      postMessage(message) {
        posted.push(message);
      },
      emitMessage(message) {
        messageListener(message);
      },
      emitDisconnect() {
        disconnectListener();
      },
      disconnect() {},
    };
  }

  const context = vm.createContext({
    chrome: {
      runtime: {
        getManifest() {
          return { version: "1.0.0" };
        },
        id: "test-extension",
        onConnect: {
          addListener(listener) {
            connectListener = listener;
          },
        },
        onMessage: { addListener() {} },
      },
      action: {
        setBadgeText() {},
        setBadgeBackgroundColor() {},
        setTitle() {},
      },
    },
    WebSocket: FakeWebSocket,
    Date,
    JSON,
    Map,
    Set,
    setTimeout,
    clearTimeout,
    setInterval() {
      return 0;
    },
    socketMessages,
    socketClosed: false,
  });
  vm.runInContext(serviceWorkerSource, context);

  const oldPort = makePort(7);
  connectListener(oldPort);
  oldPort.emitMessage({
    type: "snapshot",
    state: "paused",
    title: "Old",
    duration: 100,
    position: 10,
    volume: 20,
    playbackRate: 1,
    capturedAtMs: Date.now(),
    visible: true,
  });

  const newPort = makePort(7);
  connectListener(newPort);
  newPort.emitMessage({
    type: "snapshot",
    state: "playing",
    title: "New",
    duration: 200,
    position: 20,
    volume: 40,
    playbackRate: 1,
    capturedAtMs: Date.now(),
    visible: true,
  });

  oldPort.emitMessage({
    type: "snapshot",
    state: "playing",
    title: "Stale",
    duration: 100,
    position: 90,
    volume: 90,
    playbackRate: 1,
    capturedAtMs: Date.now(),
    visible: true,
  });
  oldPort.emitDisconnect();

  assert.equal(
    vm.runInContext("tabConnections.get(7).snapshot.title", context),
    "New"
  );

  vm.runInContext(
    `socket = {
      readyState: WebSocket.OPEN,
      send(value) { socketMessages.push(JSON.parse(value)); },
      close() { socketClosed = true; },
    }`,
    context
  );

  const backgroundPort = makePort(8);
  connectListener(backgroundPort);
  backgroundPort.emitMessage({
    type: "snapshot",
    state: "playing",
    title: "Background",
    duration: 180,
    position: 30,
    volume: 60,
    playbackRate: 1,
    capturedAtMs: Date.now(),
    visible: false,
  });

  context.commandJSON = JSON.stringify({
    type: "command",
    protocolVersion: 1,
    id: "command-1",
    command: "pause",
  });
  vm.runInContext("receiveCommand(commandJSON)", context);
  assert.equal(newPort.posted.at(-1).id, "command-1");

  oldPort.emitMessage({
    type: "ack",
    id: "command-1",
    success: true,
  });
  assert.equal(
    socketMessages.some((message) => message.id === "command-1"),
    false
  );

  newPort.emitMessage({
    type: "ack",
    id: "command-1",
    success: true,
  });
  assert.equal(socketMessages.at(-1).id, "command-1");
  assert.equal(socketMessages.at(-1).success, true);

  context.targetedCommandJSON = JSON.stringify({
    type: "command",
    protocolVersion: 1,
    id: "command-2",
    command: "pause",
    tabId: 8,
  });
  vm.runInContext("receiveCommand(targetedCommandJSON)", context);
  assert.equal(backgroundPort.posted.at(-1).id, "command-2");
  assert.notEqual(newPort.posted.at(-1).id, "command-2");

  newPort.emitMessage({
    type: "ack",
    id: "command-2",
    success: true,
  });
  assert.equal(
    socketMessages.some((message) => message.id === "command-2"),
    false
  );

  backgroundPort.emitMessage({
    type: "ack",
    id: "command-2",
    success: true,
  });
  assert.equal(socketMessages.at(-1).id, "command-2");
  assert.equal(socketMessages.at(-1).success, true);

  newPort.emitDisconnect();
  assert.equal(context.socketClosed, false);
  backgroundPort.emitDisconnect();
  assert.equal(context.socketClosed, true);
  assert.equal(vm.runInContext("tabConnections.size", context), 0);
  const emptySessionList = socketMessages.findLast(
    (message) => message.type === "sessions"
  );
  assert.equal(emptySessionList.sessions.length, 0);
});

test("content script ignores a late disconnect from an old runtime port", async () => {
  function makeRuntimePort() {
    let disconnectListener = null;
    return {
      onMessage: { addListener() {} },
      onDisconnect: {
        addListener(listener) {
          disconnectListener = listener;
        },
      },
      postMessage() {},
      emitDisconnect() {
        disconnectListener();
      },
    };
  }

  const firstPort = makeRuntimePort();
  const secondPort = makeRuntimePort();
  const ports = [firstPort, secondPort];
  const window = new EventTarget();
  window.location = new URL("https://music.youtube.com/watch?v=current");
  window.addEventListener("reprise-youtube-music-request-v1", (event) => {
    const request = JSON.parse(event.detail);
    window.dispatchEvent(
      new CustomEvent("reprise-youtube-music-response-v1", {
        detail: JSON.stringify({
          id: request.id,
          success: true,
          result: {
            state: "paused",
            duration: 100,
            position: 10,
            volume: 50,
            playbackRate: 1,
            capturedAtMs: Date.now(),
            videoId: "current",
            title: "Current",
            artist: "Artist",
          },
        }),
      })
    );
  });

  const context = vm.createContext({
    window,
    document: {
      body: {},
      visibilityState: "visible",
      addEventListener() {},
      querySelector() {
        return null;
      },
      querySelectorAll() {
        return [];
      },
    },
    navigator: { mediaSession: null },
    chrome: {
      runtime: {
        connect() {
          return ports.shift();
        },
      },
    },
    MutationObserver: class {
      observe() {}
    },
    Event,
    EventTarget,
    CustomEvent,
    URL,
    Date,
    setTimeout,
    clearTimeout,
    setInterval() {
      return 0;
    },
  });
  vm.runInContext(contentScriptSource, context);
  vm.runInContext("connectToServiceWorker()", context);
  firstPort.emitDisconnect();

  assert.equal(vm.runInContext("port !== null", context), true);
  assert.equal(vm.runInContext("port", context), secondPort);

  await new Promise((resolve) => setTimeout(resolve, 0));
});

test("worker command timeout covers page timeout and the longest fallback", () => {
  const pageTimeout = numericConstant(
    contentScriptSource,
    "PAGE_COMMAND_TIMEOUT_MS"
  );
  const workerTimeout = numericConstant(
    serviceWorkerSource,
    "COMMAND_TIMEOUT_MS"
  );

  assert.ok(workerTimeout > pageTimeout + 2000);
});
