/**
 * Reprise in the GNOME top bar.
 *
 * The tray protocol every desktop shares carries a label as a plain string,
 * so a title can only ever step a whole character at a time. GNOME lets an
 * extension draw its own panel button, which is what this does: it reads the
 * current track from Reprise over D-Bus and scrolls the title pixel by pixel,
 * the way the macOS menu bar does. Clicking the button asks Reprise to show
 * its panel.
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
     */
    _step() {
        const distance = this._textWidth + SCROLL_GAP;
        const elapsed = GLib.get_monotonic_time() / 1000 - this._startedAt - INITIAL_PAUSE;
        if (elapsed <= 0) {
            this._track.set_translation(0, 0, 0);
            return;
        }

        const travelled = (elapsed / 1000) * this._pointsPerSecond;
        const cycle = distance + (this._pointsPerSecond * INITIAL_PAUSE) / 1000;
        const position = travelled % cycle;
        this._track.set_translation(-Math.min(position, distance), 0, 0);
    }

    /**
     * Stops the scroll and releases its timeline.
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
     */
    enable() {
        this._button = new RepriseButton(() => this._activate());
        Main.panel.addToStatusArea('reprise', this._button, 0, 'right');

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
     */
    disable() {
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
     */
    _activate() {
        this._proxy?.call('Activate', null, Gio.DBusCallFlags.NONE, -1, null, null);
    }
}
