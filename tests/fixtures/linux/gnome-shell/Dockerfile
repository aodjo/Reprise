FROM ubuntu:24.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update -qq && apt-get install -y --no-install-recommends gnome-shell dbus-x11 mesa-utils libgl1-mesa-dri fonts-noto-core python3 && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home --uid 1001 repriseqa
USER repriseqa
ENV LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
