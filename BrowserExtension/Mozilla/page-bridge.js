// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

"use strict";

(() => {
  const REQUEST_EVENT = "reprise-youtube-music-request-v1";
  const RESPONSE_EVENT = "reprise-youtube-music-response-v1";
  const STATE_CHANGED_EVENT = "reprise-youtube-music-state-changed-v1";
  const PLAYBACK_CONFIRMATION_TIMEOUT_MS = 1200;
  const TRACK_CHANGE_CONFIRMATION_TIMEOUT_MS = 2000;
  const ALLOWED_COMMANDS = new Set([
    "previous",
    "pause",
    "playPause",
    "stop",
    "next",
    "seek",
    "setVolume",
  ]);
  let volumeListenerPlayer = null;

  function notifyStateChanged() {
    window.dispatchEvent(new CustomEvent(STATE_CHANGED_EVENT));
  }

  function observePlayerVolume(player) {
    if (volumeListenerPlayer === player) {
      return;
    }

    if (volumeListenerPlayer &&
        typeof volumeListenerPlayer.removeEventListener === "function") {
      try {
        volumeListenerPlayer.removeEventListener(
          "onVolumeChange",
          notifyStateChanged
        );
      } catch (_error) {
        // The periodic snapshot remains the fallback for private API changes.
      }
    }
    volumeListenerPlayer = null;

    if (!player || typeof player.addEventListener !== "function") {
      return;
    }
    try {
      player.addEventListener("onVolumeChange", notifyStateChanged);
      volumeListenerPlayer = player;
    } catch (_error) {
      // The periodic snapshot remains the fallback for private API changes.
    }
  }

  function playerController() {
    const selectors = [
      "ytmusic-player #movie_player",
      "ytmusic-player-page #movie_player",
      "#movie_player",
    ];

    return selectors
      .map((selector) => document.querySelector(selector))
      .find((candidate) => {
        return candidate && (
          typeof candidate.getCurrentTime === "function" ||
          typeof candidate.getPlayerState === "function"
        );
      }) ?? null;
  }

  function playerBarController() {
    return document.querySelector("ytmusic-player-bar");
  }

  function optionalFiniteNumber(value) {
    if (typeof value === "number" && Number.isFinite(value)) {
      return value;
    }
    if (typeof value === "string" && value.trim()) {
      const number = Number(value);
      return Number.isFinite(number) ? number : null;
    }
    return null;
  }

  function playerBarDisplayVolume() {
    const playerBar = playerBarController();
    if (!playerBar) {
      return null;
    }

    const slider = playerBar.querySelector?.(
      "#volume-slider, #expand-volume-slider"
    ) ?? document.querySelector(
      "ytmusic-player-bar #volume-slider, " +
        "ytmusic-player-bar #expand-volume-slider"
    );
    // YouTube Music can map its 0...100 slider through an exponential curve
    // before calling the underlying player API. The player-bar value is the
    // user-facing scale Reprise must display and send back.
    const storedVolume = optionalFiniteNumber(playerBar.volume);
    const sliderVolume = optionalFiniteNumber(
      slider?.immediateValue ?? slider?.value
    );
    const volume = storedVolume ?? sliderVolume;
    if (volume === null) {
      return null;
    }

    return playerBar.isMuted === true
      ? 0
      : Math.min(Math.max(volume, 0), 100);
  }

  function updatePlayerBarVolume(targetVolume) {
    const playerBar = playerBarController();
    if (!playerBar) {
      return false;
    }

    // Going through the player bar keeps its store, slider thumb, mute state,
    // and any current YouTube Music volume curve in sync.
    if (typeof playerBar.updateVolume === "function") {
      playerBar.updateVolume(targetVolume);
      return true;
    }

    const slider = playerBar.querySelector?.(
      "#volume-slider, #expand-volume-slider"
    ) ?? null;
    if (!slider) {
      return false;
    }

    slider.value = targetVolume;
    if ("immediateValue" in slider) {
      slider.immediateValue = targetVolume;
    }
    slider.dispatchEvent(
      new Event("change", { bubbles: true, composed: true })
    );
    return true;
  }

  function activeMediaElement() {
    const preferred = [
      ...document.querySelectorAll(
        "ytmusic-player #movie_player video.html5-main-video, " +
          "ytmusic-player-page #movie_player video.html5-main-video, " +
          "#movie_player video.html5-main-video"
      ),
    ];
    const allMedia = [...document.querySelectorAll("video, audio")];
    const candidates = [...new Set([...preferred, ...allMedia])]
      .filter((media) => media?.isConnected !== false);

    return (
      candidates.find((media) => {
        return !media.paused && !media.ended && media.readyState > 0;
      }) ??
      candidates.find((media) => {
        return media.readyState > 0 && (
          media.currentSrc || Number.isFinite(media.duration)
        );
      }) ??
      candidates[0] ??
      null
    );
  }

  function finiteNumber(value, fallback = 0) {
    return typeof value === "number" && Number.isFinite(value)
      ? value
      : fallback;
  }

  function optionalCall(target, method, ...arguments_) {
    try {
      return typeof target?.[method] === "function"
        ? target[method](...arguments_)
        : undefined;
    } catch (_error) {
      return undefined;
    }
  }

  function requiredCall(target, method, ...arguments_) {
    if (typeof target?.[method] !== "function") {
      throw new Error("YouTube Music 플레이어 제어기를 찾지 못했습니다.");
    }
    return target[method](...arguments_);
  }

  function readPlayerState() {
    const player = playerController();
    observePlayerVolume(player);
    const media = activeMediaElement();
    const playerState = finiteNumber(
      optionalCall(player, "getPlayerState"),
      Number.NaN
    );
    const rawDuration = finiteNumber(
      optionalCall(player, "getDuration"),
      finiteNumber(media?.duration)
    );
    const duration = Math.max(rawDuration, 0);
    const position = Math.max(
      finiteNumber(
        optionalCall(player, "getCurrentTime"),
        finiteNumber(media?.currentTime)
      ),
      0
    );
    const muted = optionalCall(player, "isMuted");
    const controllerVolume = optionalCall(player, "getVolume");
    const mediaVolume = media
      ? media.muted
        ? 0
        : finiteNumber(media.volume, 1) * 100
      : 100;
    const playerBarVolume = playerBarDisplayVolume();
    const volume = playerBarVolume ?? (
      muted === true
        ? 0
        : Math.min(
            Math.max(finiteNumber(controllerVolume, mediaVolume), 0),
            100
          )
    );
    const rawPlaybackRate = finiteNumber(
      optionalCall(player, "getPlaybackRate"),
      finiteNumber(media?.playbackRate, 1)
    );
    const playbackRate = rawPlaybackRate > 0 ? rawPlaybackRate : 1;
    const videoData = optionalCall(player, "getVideoData");
    const videoURL = optionalCall(player, "getVideoUrl");

    let state;
    if (media?.ended || playerState === 0) {
      state = "stopped";
    } else if (playerState === 1) {
      state = "playing";
    } else if (playerState === 2) {
      state = "paused";
    } else if (media) {
      state = media.paused ? "paused" : "playing";
    } else {
      state = "stopped";
    }

    return {
      state,
      duration,
      position: duration > 0 ? Math.min(position, duration) : position,
      volume,
      playbackRate,
      capturedAtMs: Date.now(),
      videoId: typeof videoData?.video_id === "string"
        ? videoData.video_id
        : "",
      title: typeof videoData?.title === "string" ? videoData.title : "",
      artist: typeof videoData?.author === "string" ? videoData.author : "",
      videoUrl: typeof videoURL === "string" ? videoURL : "",
      controllerAvailable: player !== null,
    };
  }

  function clickPlayerButton(direction) {
    const selectors = direction === "previous"
      ? [
          "ytmusic-player-bar #previous-button",
          "ytmusic-player-bar .previous-button",
          "ytmusic-player-bar [aria-label*='Previous']",
          "ytmusic-player-bar [aria-label*='이전']",
        ]
      : [
          "ytmusic-player-bar #next-button",
          "ytmusic-player-bar .next-button",
          "ytmusic-player-bar [aria-label*='Next']",
          "ytmusic-player-bar [aria-label*='다음']",
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

  async function waitFor(predicate, timeoutMs, intervalMs = 40) {
    const deadline = Date.now() + timeoutMs;
    let value = predicate();

    while (!value && Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, intervalMs));
      value = predicate();
    }
    return value;
  }

  function trackIdentityChanged(before, after) {
    if (before.videoId && after.videoId) {
      return before.videoId !== after.videoId;
    }

    const beforeIdentity = `${before.title}\n${before.artist}`.trim();
    const afterIdentity = `${after.title}\n${after.artist}`.trim();
    return beforeIdentity && afterIdentity && beforeIdentity !== afterIdentity;
  }

  async function confirmPlayerState(predicate, timeoutMs, errorMessage) {
    const confirmedState = await waitFor(() => {
      const state = readPlayerState();
      return predicate(state) ? state : null;
    }, timeoutMs);
    if (!confirmedState) {
      throw new Error(errorMessage);
    }
    return confirmedState;
  }

  async function performCommand(message) {
    if (!ALLOWED_COMMANDS.has(message.command)) {
      throw new Error("지원하지 않는 명령입니다.");
    }

    const player = playerController();
    if (!player) {
      throw new Error("YouTube Music 플레이어 제어기를 찾지 못했습니다.");
    }

    switch (message.command) {
      case "previous": {
        const before = readPlayerState();
        if (typeof player.previousVideo === "function") {
          player.previousVideo();
        } else {
          clickPlayerButton("previous");
        }
        await confirmPlayerState(
          (state) => {
            return trackIdentityChanged(before, state) ||
              (before.position > 1.5 && state.position <= 1.5);
          },
          TRACK_CHANGE_CONFIRMATION_TIMEOUT_MS,
          "이전 곡 전환을 확인하지 못했습니다."
        );
        break;
      }
      case "pause":
        requiredCall(player, "pauseVideo");
        await confirmPlayerState(
          (state) => state.state === "paused" || state.state === "stopped",
          PLAYBACK_CONFIRMATION_TIMEOUT_MS,
          "일시 정지를 확인하지 못했습니다."
        );
        break;
      case "playPause": {
        const wasPlaying = readPlayerState().state === "playing";
        if (wasPlaying) {
          requiredCall(player, "pauseVideo");
        } else {
          requiredCall(player, "playVideo");
        }
        await confirmPlayerState(
          (state) => wasPlaying
            ? state.state === "paused" || state.state === "stopped"
            : state.state === "playing",
          PLAYBACK_CONFIRMATION_TIMEOUT_MS,
          "재생 상태 변경을 확인하지 못했습니다."
        );
        break;
      }
      case "stop":
        requiredCall(player, "pauseVideo");
        requiredCall(player, "seekTo", 0, true);
        await confirmPlayerState(
          (state) => {
            const notPlaying = state.state === "paused" ||
              state.state === "stopped";
            return notPlaying && state.position <= 1.5;
          },
          PLAYBACK_CONFIRMATION_TIMEOUT_MS,
          "재생 정지를 확인하지 못했습니다."
        );
        break;
      case "next": {
        const before = readPlayerState();
        if (typeof player.nextVideo === "function") {
          player.nextVideo();
        } else {
          clickPlayerButton("next");
        }
        await confirmPlayerState(
          (state) => trackIdentityChanged(before, state),
          TRACK_CHANGE_CONFIRMATION_TIMEOUT_MS,
          "다음 곡 전환을 확인하지 못했습니다."
        );
        break;
      }
      case "seek": {
        if (!Number.isFinite(message.position)) {
          throw new Error("올바르지 않은 재생 위치입니다.");
        }
        const state = readPlayerState();
        const requestedPosition = Math.max(message.position, 0);
        const targetPosition = state.duration > 0
          ? Math.min(requestedPosition, state.duration)
          : requestedPosition;
        requiredCall(player, "seekTo", targetPosition, true);
        const confirmed = await waitFor(() => {
          return Math.abs(readPlayerState().position - targetPosition) <= 1.5;
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
        if (!updatePlayerBarVolume(targetVolume)) {
          requiredCall(player, "setVolume", targetVolume);
          if (targetVolume === 0) {
            optionalCall(player, "mute");
          } else {
            optionalCall(player, "unMute");
          }
        }
        const confirmed = await waitFor(() => {
          return Math.abs(readPlayerState().volume - targetVolume) <= 1;
        }, 800);
        if (!confirmed) {
          throw new Error("변경한 음량을 확인하지 못했습니다.");
        }
        break;
      }
    }

    return readPlayerState();
  }

  function respond(id, success, result = null, error = null) {
    window.dispatchEvent(
      new CustomEvent(RESPONSE_EVENT, {
        detail: JSON.stringify({ id, success, result, error }),
      })
    );
  }

  window.addEventListener(REQUEST_EVENT, (event) => {
    if (typeof event.detail !== "string") {
      return;
    }

    let message;
    try {
      message = JSON.parse(event.detail);
    } catch (_error) {
      return;
    }

    if (!message || typeof message.id !== "string") {
      return;
    }

    void (async () => {
      try {
        const result = message.action === "snapshot"
          ? readPlayerState()
          : message.action === "command"
            ? await performCommand(message)
            : (() => {
                throw new Error("지원하지 않는 요청입니다.");
              })();
        respond(message.id, true, result);
      } catch (error) {
        respond(
          message.id,
          false,
          null,
          String(error?.message ?? error).slice(0, 512)
        );
      }
    })();
  });
})();
