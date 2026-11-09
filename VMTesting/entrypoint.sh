#!/bin/bash

# If the command starts with systemd, use that
if [[ "$1" == "/lib/systemd/systemd" ]]; then
    exec "$@"
else
    # Otherwise, run the command (allows for bash and other commands)
    exec "$@"
fi