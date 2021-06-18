#!/bin/bash
cd "$(dirname "$0")" || exit
docker-compose up --build -d