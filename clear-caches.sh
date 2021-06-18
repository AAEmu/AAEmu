#!/bin/bash
# shellcheck disable=SC2046
docker rmi $(docker images -aq -f 'dangling=true')