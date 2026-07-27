//
//  RepriseSettingsView.swift
//  Reprise
//

import AppKit
import SwiftUI

struct RepriseSettingsView: View {
    var body: some View {
        TabView {
            GeneralSettingsView()
                .tabItem {
                    Label("일반", systemImage: "gearshape")
                }

            SystemInfoSettingsView()
                .tabItem {
                    Label("시스템 정보", systemImage: "info.square")
                }
        }
        .frame(width: 500, height: 350)
    }
}

private struct GeneralSettingsView: View {
    @AppStorage(ReprisePreferenceKey.automaticallyScrollTitles)
    private var automaticallyScrollTitles = true

    @AppStorage(ReprisePreferenceKey.marqueeSpeed)
    private var marqueeSpeed = MarqueeSpeed.normal.rawValue

    @AppStorage(ReprisePreferenceKey.resetsMenuTitleWhenPanelOpens)
    private var resetsMenuTitleWhenPanelOpens = true

    @AppStorage(ReprisePreferenceKey.playerPanelTheme)
    private var playerPanelTheme = PlayerPanelTheme.liquid.rawValue

    var body: some View {
        Form {
            Section("테마") {
                Picker("플레이어 패널", selection: $playerPanelTheme) {
                    ForEach(PlayerPanelTheme.allCases) { theme in
                        Text(theme.displayName)
                            .tag(theme.rawValue)
                    }
                }
                .pickerStyle(.segmented)
            }

            Section("캐러셀") {
                Toggle(
                    "긴 곡 제목 캐러셀 움직이기",
                    isOn: $automaticallyScrollTitles
                )

                Picker("캐러셀 속도", selection: $marqueeSpeed) {
                    ForEach(MarqueeSpeed.allCases) { speed in
                        Text(speed.displayName)
                            .tag(speed.rawValue)
                    }
                }
                .pickerStyle(.segmented)
                .disabled(!automaticallyScrollTitles)
            }

            Section("패널") {
                Toggle(
                    "패널을 열면 메뉴바 제목을 처음으로 되돌리기",
                    isOn: $resetsMenuTitleWhenPanelOpens
                )
                .disabled(!automaticallyScrollTitles)
            }
        }
        .formStyle(.grouped)
    }
}

private struct SystemInfoSettingsView: View {
    private var version: String {
        let shortVersion = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleShortVersionString"
        ) as? String ?? "-"
        let build = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleVersion"
        ) as? String ?? "-"

        return "\(shortVersion) (\(build))"
    }

    private var operatingSystem: String {
        ProcessInfo.processInfo.operatingSystemVersionString
    }

    var body: some View {
        Form {
            Section("앱") {
                LabeledContent("버전", value: version)
            }

            Section("시스템") {
                LabeledContent("운영체제", value: operatingSystem)
            }

            Section("연결된 디스플레이") {
                ForEach(
                    Array(NSScreen.screens.enumerated()),
                    id: \.offset
                ) { _, screen in
                    LabeledContent(screen.localizedName) {
                        Text(resolution(of: screen))
                            .foregroundStyle(.secondary)
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func resolution(of screen: NSScreen) -> String {
        let width = Int(screen.frame.width * screen.backingScaleFactor)
        let height = Int(screen.frame.height * screen.backingScaleFactor)
        return "\(width) × \(height)"
    }
}

#Preview {
    RepriseSettingsView()
}
