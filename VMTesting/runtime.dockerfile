# SPDX-License-Identifier: Apache-2.0
#
# Roof VMTesting - Raspberry Pi 5 runtime environment
#
# Portions of this Dockerfile are derived from the balena base-images project.
# Original source:
# https://github.com/balena-io-library/base-images/blob/master/balena-base-images/device-base/raspberrypi5/debian/bookworm/build/Dockerfile
#
# Original project licensed under the Apache License, Version 2.0.
# Copyright and attribution notices of the original contributors are retained
# where applicable.
#
# Modified by Data Foundation Lab for Roof's development and testing environment.
# Modifications include Roof-specific runtime configuration and dependencies.
#
# See LICENSES/Apache-2.0.txt for the license text.
FROM balenalib/aarch64-debian:bookworm-build
LABEL io.balena.device-type="raspberrypi5"

# Update and upgrade the package lists
RUN apt-get update && apt-get upgrade -y && \
    apt-get install -y systemd systemd-sysv dbus sudo openssh-server iproute2

RUN apt-get update && apt-get install -y --no-install-recommends \
    less \
    kmod \
    nano \
    net-tools \
    ifupdown \
    iputils-ping \
    i2c-tools \
    usbutils \
    && rm -rf /var/lib/apt/lists/*

# Set default target to multi-user.target
RUN systemctl set-default multi-user.target

# Install necessary packages
RUN apt-get update && \
    apt-get install -y gcc zlib1g-dev libicu-dev && \
    rm -rf /var/lib/apt/lists/*

# Set root password. ONLY FOR LOCAL TESTING AND COMPILATION PURPOSES.
RUN echo 'root:root' | chpasswd

# Create a new user: roof with password password123 and give root privileges
RUN useradd -m -s /bin/bash roof && \
    echo 'roof:password123' | chpasswd && \
    usermod -aG sudo roof && \
    echo 'roof ALL=(ALL) NOPASSWD: ALL' >> /etc/sudoers

WORKDIR /roof

# Configure auto-login for roof user
RUN mkdir -p /etc/systemd/system/getty@tty1.service.d && \
    echo '[Service]' > /etc/systemd/system/getty@tty1.service.d/override.conf && \
    echo 'ExecStart=' >> /etc/systemd/system/getty@tty1.service.d/override.conf && \
    echo 'ExecStart=-/sbin/agetty --autologin roof --noclear %I $TERM' >> /etc/systemd/system/getty@tty1.service.d/override.conf

# Disable SSH password authentication for root (optional, for security)
RUN sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/' /etc/ssh/sshd_config

# Remove unnecessary systemd services
RUN rm -f /lib/systemd/system/multi-user.target.wants/* \
    /etc/systemd/system/*.wants/* \
    /lib/systemd/system/local-fs.target.wants/* \
    /lib/systemd/system/sockets.target.wants/*udev* \
    /lib/systemd/system/sockets.target.wants/*initctl* \
    /lib/systemd/system/sysinit.target.wants/systemd-tmpfiles-setup* \
    /lib/systemd/system/systemd-update-utmp*

CMD ["/lib/systemd/systemd"]

# Default command (can be overridden)
#ENTRYPOINT ["/lib/systemd/systemd"]