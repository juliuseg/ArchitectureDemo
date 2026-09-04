#!/bin/bash
if [ ! -d "$UNITY_BUILD_DIR" ]; then
    mkdir -p "$UNITY_BUILD_DIR"
    echo "Created build directory: $UNITY_BUILD_DIR"
else
    echo "Build directory already exists: $UNITY_BUILD_DIR"
fi