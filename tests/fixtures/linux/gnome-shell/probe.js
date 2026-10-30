import GLib from 'gi://GLib';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import { Extension } from 'resource:///org/gnome/shell/extensions/extension.js';
/** Samples the production extension's actual Clutter actors on a private headless Shell. */
export default class MotionVerification extends Extension {
    /** Starts after GNOME has loaded both extensions. */
    enable() {
        this._source = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 3500, () => {
            this._source = 0;
            this._verify().catch(error => GLib.file_set_contents('/tmp/reprise-gnome-result.json', JSON.stringify({ok:false,error:String(error),stack:error.stack})));
            return GLib.SOURCE_REMOVE;
        });
    }
    /** Waits for rendered compositor frames rather than faking the animation timeline. */
    _wait(ms) { return new Promise(resolve => GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => { resolve(); return GLib.SOURCE_REMOVE; })); }
    /** Requires the measured actors to reflect a completed transition or an intermediate frame. */
    _assert(value, message) { if (!value) throw new Error(message); }
    /** Exercises width, vertical push, cover visibility, same-text track changes, and rapid updates. */
    async _verify() {
        // Headless llvmpipe inhibits Shell animations; lift that test-environment inhibition only.
        if (!St.Settings.get().enable_animations) St.Settings.get().uninhibit_animations();
        const button = Main.panel.statusArea.reprise;
        this._assert(button, 'Production Reprise extension did not load');
        const state = {text:'Hi',iconPng:null,scrolls:true,pointsPerSecond:30,maxWidthChars:20,trackKey:'qa-track',showsLyrics:true,lyricText:'Hi',showsArtwork:true,reservesTextWidth:false};
        button.update(state);
        await this._wait(400);
        const compact = button._viewport.width;
        const environment = {mapped:button.mapped,animations:St.Settings.get().enable_animations,trackKey:button._trackKey,hasLyric:button._hasLyric,startingUp:Main.layoutManager._startingUp};
        const next = {...state,text:'가사 전체가 부드럽게 올라오면서 너비도 변합니다',lyricText:'가사 전체가 부드럽게 올라오면서 너비도 변합니다'};
        const started = GLib.get_monotonic_time();
        button.update(next);
        const immediate = {width:button._viewport.width,y:button._track.translation_y,vmapped:button._viewport.mapped,tmapped:button._track.mapped,transition:Boolean(button._viewport.get_transition('width')),outgoing:Boolean(button._outgoing)};
        await this._wait(100);
        const middle = {elapsed:(GLib.get_monotonic_time()-started)/1000,immediate,width:button._viewport.width,y:button._track.translation_y,outgoing:button._outgoing?.translation_y};
        // Frequent artwork/playback updates must not snap the active width transition.
        button.update({...next,iconPng:null});
        const afterRefresh = button._viewport.width;
        await this._wait(400);
        const wide = button._viewport.width;
        this._assert(middle.width>compact && middle.width<wide, `Width jumped: ${JSON.stringify({compact,middle,wide,environment})}`);
        this._assert(middle.y>0 && middle.outgoing<0, `Rows did not push: ${JSON.stringify(middle)}`);
        this._assert(Math.abs(afterRefresh-middle.width)<1, 'Icon refresh snapped the width');
        this._assert(button._track.translation_y===0 && !button._outgoing, 'Obsolete outgoing row remained');
        button.update({...next,showsArtwork:false});
        this._assert(!button._icon.visible, 'Hidden cover remained visible');
        button.update({...next,text:'빠른 첫 줄',lyricText:'빠른 첫 줄'});
        button.update({...next,text:'마지막',lyricText:'마지막'});
        await this._wait(400);
        this._assert(button._first.text==='마지막' && !button._outgoing && button._track.translation_y===0, 'Rapid update retained an old line');
        button.update({...next,text:'마지막',lyricText:'마지막',trackKey:'other'});
        this._assert(!button._outgoing && button._track.translation_x===0, 'Same-text track change retained old motion');
        button.update({...state,reservesTextWidth:true});
        await this._wait(400);
        this._assert(button._viewport.width===240, 'Reserved width was ignored');
        GLib.file_set_contents('/tmp/reprise-gnome-result.json', JSON.stringify({ok:true,compact,middle,afterRefresh,wide,reserved:button._viewport.width}));
    }
    /** Releases the fixture startup timer when the private shell exits. */
    disable() { if (this._source) GLib.source_remove(this._source); this._source=0; }
}
