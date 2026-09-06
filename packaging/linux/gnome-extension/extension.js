/**
 * Reprise in the GNOME top bar.
 *
 * The tray protocol every desktop shares carries a label as a plain string,
 * so a title there cannot move: the best it could do is swap in a different
 * slice of the text, which reads as the letters changing rather than the
 * words sliding past. GNOME lets an extension draw its own panel button, so
 * this one does, and the title slides pixel by pixel the way the macOS menu
 * bar does. Clicking the button asks Reprise to show its panel.
 *
 * While this extension runs it holds the bus name Reprise watches for, and
 * Reprise withdraws its own tray entry, so the two never appear at once.
 */

import Clutter from 'gi://Clutter';
import GObject from 'gi://GObject';
import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import St from 'gi://St';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import { Extension } from 'resource:///org/gnome/shell/extensions/extension.js';

/** Bus name Reprise publishes its top-bar content on. */
const SERVICE = 'dev.junx.Reprise';

/** Object path carrying that content. */
const OBJECT_PATH = '/dev/junx/Reprise';

/** Interface carrying that content. */
const INTERFACE = 'dev.junx.Reprise.MenuBar';

/** Gap drawn between the end of a scrolling title and its repeat, in pixels. */
const SCROLL_GAP = 28;

/** How long a title rests before it starts scrolling, in milliseconds. */
const INITIAL_PAUSE = 1400;

/** Edge length of the album cover in the panel, in pixels. */
const ICON_SIZE = 18;

/** Bus name held while this extension is drawing Reprise's entry. */
const SHELL_NAME = 'dev.junx.Reprise.Shell';

/**
 * How far the text has slid at a moment of the scroll.
 *
 * The text and a repeat of it sit one distance apart, so sliding the pair
 * left by exactly that distance puts the repeat where the original began:
 * wrapping there is invisible, which is what makes the motion endless
 * rather than a jump back to the start. The rest is constant speed, with a
 * rest at each end that is spent parked on the repeat.
 *
 * @param {number} elapsed - Milliseconds since the text appeared.
 * @param {number} distance - Width of the text plus the gap, in pixels.
 * @param {number} pointsPerSecond - Scrolling speed.
 * @param {number} [pause=INITIAL_PAUSE] - Rest before moving, in milliseconds.
 * @returns {number} Pixels to slide left, from 0 to distance.
 *
 * @example
 * scrollOffset(0, 100, 30);    // 0, still resting
 * scrollOffset(2400, 100, 30); // 30, one second of travel
 */
export function scrollOffset(elapsed, distance, pointsPerSecond, pause = INITIAL_PAUSE) {
    if (distance <= 0 || pointsPerSecond <= 0) {
        return 0;
    }

    const moving = elapsed - pause;
    if (moving <= 0) {
        return 0;
    }

    const travel = (distance / pointsPerSecond) * 1000;
    const cycle = travel + pause;
    const phase = moving % cycle;
    return Math.min((phase / 1000) * pointsPerSecond, distance);
}

/**
 * A panel button showing the album cover and a scrolling title.
 *
 * The title lives in a fixed-width viewport that clips it, and a repeat of
 * the same text sits one gap further along, so sliding the pair left by the
 * distance between them and snapping back reads as an endless scroll.
 */
const RepriseButton = GObject.registerClass(
class RepriseButton extends PanelMenu.Button {
    /**
     * Builds the button with an empty label.
     *
     * @param {function(): void} onClick - Runs when the button is pressed.
     */
    _init(onClick) {
        super._init(0.0, 'Reprise', true);
        this._onClick = onClick;
        this._offset = 0;
        this._textWidth = 0;
        this._viewportWidth = 0;
        this._scrolls = true;
        this._pointsPerSecond = 30;
        this._timeline = null;
        this._startedAt = 0;

        this._icon = new St.Icon({ icon_size: ICON_SIZE, style_class: 'system-status-icon' });
        this._first = new St.Label({ y_align: Clutter.ActorAlign.CENTER });
        this._second = new St.Label({ y_align: Clutter.ActorAlign.CENTER });
        this._track = new St.BoxLayout({ y_align: Clutter.ActorAlign.CENTER });
        this._track.add_child(this._first);
        this._track.add_child(this._second);

        this._viewport = new St.Widget({
            layout_manager: new Clutter.BinLayout(),
            clip_to_allocation: true,
            y_align: Clutter.ActorAlign.CENTER,
        });
        this._viewport.add_child(this._track);

        const box = new St.BoxLayout({ style_class: 'panel-status-menu-box' });
        box.add_child(this._icon);
        box.add_child(this._viewport);
        this.add_child(box);
        this.connect('destroy', () => this._stopScrolling());
    }

    /**
     * Passes a click on and keeps the shell from opening an empty menu.
     *
     * @param {Clutter.Event} event - The press.
     * @returns {number} Whether the event was handled.
     */
    vfunc_event(event) {
        const type = event.type();
        if (type === Clutter.EventType.BUTTON_PRESS || type === Clutter.EventType.TOUCH_BEGIN) {
            this._onClick();
            return Clutter.EVENT_STOP;
        }
        return Clutter.EVENT_PROPAGATE;
    }

    /**
     * Applies new content from Reprise.
     *
     * @param {object} state - Text, icon, and scrolling settings.
     * @param {string} state.text - The full line to show.
     * @param {Uint8Array} state.iconPng - Album cover, or an empty array.
     * @param {boolean} state.scrolls - Whether long text should scroll.
     * @param {number} state.pointsPerSecond - Scrolling speed.
     * @param {number} state.maxWidthChars - Width to hold the text to, in
     *   characters of the text itself, which is what makes the setting mean
     *   the same for a Hangul line as for a Latin one.
     * @returns {void} Nothing; the button redraws in place.
     */
    update(state) {
        this._scrolls = state.scrolls;
        this._pointsPerSecond = Math.max(state.pointsPerSecond, 1);
        this._setIcon(state.iconPng);

        const changed = this._first.text !== state.text;
        if (changed) {
            this._first.text = state.text;
            this._second.text = state.text;
        }

        this._viewport.visible = state.text.length > 0;
        if (!this._viewport.visible) {
            this._stopScrolling();
            return;
        }

        this._textWidth = this._first.get_preferred_width(-1)[1];
        const characters = Math.max([...state.text].length, 1);
        const perCharacter = this._textWidth / characters;
        this._viewportWidth = Math.min(
            this._textWidth,
            Math.max(state.maxWidthChars, 1) * perCharacter);
        this._viewport.set_width(this._viewportWidth);
        this._second.visible = this._scrolls && this._textWidth > this._viewportWidth;
        this._second.set_style(`margin-left: ${SCROLL_GAP}px;`);

        if (!this._second.visible) {
            this._stopScrolling();
            this._track.set_translation(0, 0, 0);
            return;
        }

        if (changed || !this._timeline) {
            this._startScrolling();
        }
    }

    /**
     * Sets the panel icon from PNG bytes, falling back to a note.
     *
     * @param {Uint8Array} png - Encoded cover, or an empty array.
     * @returns {void} Nothing; an unreadable image falls back to a note icon.
     */
    _setIcon(png) {
        if (!png || png.length === 0) {
            this._icon.gicon = null;
            this._icon.icon_name = 'audio-x-generic-symbolic';
            return;
        }

        try {
            this._icon.gicon = Gio.BytesIcon.new(new GLib.Bytes(png));
        } catch (error) {
            this._icon.gicon = null;
            this._icon.icon_name = 'audio-x-generic-symbolic';
        }
    }

    /**
     * Starts the scroll, driven by a per-frame timeline.
     *
     * A timeline rather than an eased animation because the motion is
     * constant speed with a pause at each end, which an easing curve cannot
     * express, and because the frame callback keeps the two copies of the
     * text exactly one gap apart at any width.
     *
     * @returns {void} Nothing; any previous scroll is stopped first.
     */
    _startScrolling() {
        this._stopScrolling();
        this._startedAt = GLib.get_monotonic_time() / 1000;
        this._timeline = new Clutter.Timeline({
            actor: this,
            duration: 1000,
            repeat_count: -1,
        });
        this._timeline.connect('new-frame', () => this._step());
        this._timeline.start();
    }

    /**
     * Places the text for this frame.
     *
     * @returns {void} Nothing; the track actor is moved in place.
     */
    _step() {
        const elapsed = GLib.get_monotonic_time() / 1000 - this._startedAt;
        const offset = scrollOffset(
            elapsed,
            this._textWidth + SCROLL_GAP,
            this._pointsPerSecond);
        this._track.set_translation(-offset, 0, 0);
    }

    /**
     * Stops the scroll and releases its timeline.
     *
     * @returns {void} Nothing; safe to call when nothing is scrolling.
     */
    _stopScrolling() {
        if (this._timeline) {
            this._timeline.stop();
            this._timeline.run_dispose();
            this._timeline = null;
        }
    }
});

export default class RepriseExtension extends Extension {
    /**
     * Adds the panel button and starts following Reprise.
     *
     * @returns {void} Nothing; the button appears as soon as the shell draws.
     */
    enable() {
        this._button = new RepriseButton(() => this._activate());
        Main.panel.addToStatusArea('reprise', this._button, 0, 'right');

        this._nameId = Gio.bus_own_name(
            Gio.BusType.SESSION,
            SHELL_NAME,
            Gio.BusNameOwnerFlags.REPLACE,
            null,
            null,
            null);

        this._proxy = null;
        this._watchId = Gio.bus_watch_name(
            Gio.BusType.SESSION,
            SERVICE,
            Gio.BusNameWatcherFlags.NONE,
            () => this._connect(),
            () => this._disconnect());
    }

    /**
     * Removes the panel button and stops following Reprise.
     *
     * @returns {void} Nothing; every watch and proxy is released.
     */
    disable() {
        if (this._nameId) {
            Gio.bus_unown_name(this._nameId);
            this._nameId = null;
        }

        if (this._watchId) {
            Gio.bus_unwatch_name(this._watchId);
            this._watchId = null;
        }

        this._disconnect();
        this._button?.destroy();
        this._button = null;
    }

    /**
     * Opens a proxy to Reprise and reads its current content.
     *
     * @returns {void} Nothing; the proxy arrives through a callback.
     */
    _connect() {
        Gio.DBusProxy.new_for_bus(
            Gio.BusType.SESSION,
            Gio.DBusProxyFlags.NONE,
            null,
            SERVICE,
            OBJECT_PATH,
            INTERFACE,
            null,
            (_source, result) => {
                try {
                    this._proxy = Gio.DBusProxy.new_for_bus_finish(result);
                } catch (error) {
                    logError(error, 'Reprise: cannot reach the player');
                    return;
                }

                this._changedId = this._proxy.connect('g-properties-changed', () => this._refresh());
                this._refresh();
            });
    }

    /**
     * Drops the proxy and empties the button.
     *
     * @returns {void} Nothing; the button is left showing no track.
     */
    _disconnect() {
        if (this._proxy && this._changedId) {
            this._proxy.disconnect(this._changedId);
        }

        this._changedId = null;
        this._proxy = null;
        this._button?.update({
            text: '',
            iconPng: null,
            scrolls: false,
            pointsPerSecond: 30,
            maxWidthChars: 20,
        });
    }

    /**
     * Reads the current content and hands it to the button.
     *
     * @returns {void} Nothing; cached properties are read, so it never blocks.
     */
    _refresh() {
        if (!this._proxy || !this._button) {
            return;
        }

        const read = (name, fallback) => {
            const value = this._proxy.get_cached_property(name);
            return value === null ? fallback : value.deepUnpack();
        };

        this._button.update({
            text: read('Text', ''),
            iconPng: read('IconPng', null),
            scrolls: read('ScrollsText', true),
            pointsPerSecond: read('PointsPerSecond', 30),
            maxWidthChars: read('MaxWidthChars', 20),
        });
    }

    /**
     * Asks Reprise to show its panel.
     *
     * @returns {void} Nothing; the call is sent without waiting for a reply.
     */
    _activate() {
        this._proxy?.call('Activate', null, Gio.DBusCallFlags.NONE, -1, null, null);
    }
}
