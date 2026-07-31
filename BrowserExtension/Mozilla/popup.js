"use strict";

const statusDot = document.querySelector("#status-dot");
const statusTitle = document.querySelector("#status-title");
const statusDetail = document.querySelector("#status-detail");

chrome.runtime.sendMessage({ type: "getStatus" }, (status) => {
  if (chrome.runtime.lastError || !status) {
    statusTitle.textContent = "연결 상태를 확인할 수 없음";
    statusDetail.textContent = "YouTube Music 탭을 새로고침해 주세요.";
    return;
  }

  if (status.bridgeConnected) {
    statusDot.classList.add("connected");
    statusTitle.textContent = "Reprise와 연결됨";
    statusDetail.textContent = status.activeTitle
      ? status.activeTitle
      : `열린 YouTube Music 탭 ${status.tabCount}개`;
    return;
  }

  statusTitle.textContent = "Reprise 연결 대기 중";
  statusDetail.textContent = "Reprise를 실행한 뒤 탭을 새로고침해 주세요.";
});

document
  .querySelector("#open-youtube-music")
  .addEventListener("click", () => {
    chrome.tabs.create({ url: "https://music.youtube.com/" });
  });
