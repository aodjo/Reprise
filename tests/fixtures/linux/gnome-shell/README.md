# GNOME lyric motion fixture

Runs the production extension's real Clutter actors in a private headless GNOME
46 Wayland session. No host session, player, or user preference is modified.
The test extension supplies diagnostic-only lines; it never ships in packages.
It samples intermediate width and vertical positions, then checks interrupted
transitions, cover hiding, reserved width, and unchanged text across tracks.
The software renderer inhibits Shell animation by default. Only this private
fixture lifts that inhibition; production continues to respect GNOME's animation
setting.

```sh
docker build -t reprise-gnome-qa:local tests/fixtures/linux/gnome-shell
docker run --name reprise-gnome-parity --user 0 \
  -v "$PWD/packaging/linux/gnome-extension:/source:ro" \
  -v "$PWD/tests/fixtures/linux/gnome-shell:/qa:ro" \
  reprise-gnome-qa:local bash /qa/run.sh
docker cp reprise-gnome-parity:/tmp/reprise-gnome-result.json /tmp/reprise-gnome-result.json
docker rm reprise-gnome-parity
```

Require `ok: true` in the JSON result; a running compositor alone is not a pass.
The private system bus and logind process are needed to initialize GNOME, and
remain inside the disposable container. No privileged container or host devices
are required.
