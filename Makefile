# AAEmu local standalone: build / stop / start Login+Game (host MySQL).
# Usage: make up | make down | make build | make status | make logs

ROOT        := $(abspath $(dir $(lastword $(MAKEFILE_LIST))))
SOLUTION    := $(ROOT)/AAEmu.slnx
LOGIN_DIR   := $(ROOT)/AAEmu.Login
GAME_DIR    := $(ROOT)/AAEmu.Game
LOG_DIR     := $(ROOT)/.server_files/logs
LOGIN_LOG   := $(LOG_DIR)/login.log
GAME_LOG    := $(LOG_DIR)/game.log

DOTNET      ?= dotnet
BUILD_CFG   ?= Debug

# Pattern for running server binaries (and their `dotnet run` parents).
LOGIN_MATCH := AAEmu\.Login
GAME_MATCH  := AAEmu\.Game

.PHONY: help up down build start stop restart status logs wait-login wait-game

help:
	@echo "Targets:"
	@echo "  make up       build, stop old processes, start Login then Game"
	@echo "  make down     stop Login and Game"
	@echo "  make build    dotnet build $(notdir $(SOLUTION))"
	@echo "  make start    start Login then Game (no rebuild)"
	@echo "  make restart  down + start"
	@echo "  make status   show listening ports / PIDs"
	@echo "  make logs     tail Login + Game logs"

up: build down start
	@$(MAKE) --no-print-directory status

build:
	$(DOTNET) build "$(SOLUTION)" -c $(BUILD_CFG)

down stop:
	@-pkill -f '$(LOGIN_MATCH)/bin/|$(LOGIN_MATCH)$$|dotnet.*AAEmu\.Login' 2>/dev/null || true
	@-pkill -f '$(GAME_MATCH)/bin/|$(GAME_MATCH)$$|dotnet.*AAEmu\.Game' 2>/dev/null || true
	@sleep 1
	@-pkill -9 -f '$(LOGIN_MATCH)/bin/|AAEmu\.Login$$' 2>/dev/null || true
	@-pkill -9 -f '$(GAME_MATCH)/bin/|AAEmu\.Game$$' 2>/dev/null || true
	@echo "stopped"

start: $(LOG_DIR)
	@echo "starting Login → $(LOGIN_LOG)"
	@: >"$(LOGIN_LOG)"
	@cd "$(LOGIN_DIR)" && nohup $(DOTNET) run --no-build -c $(BUILD_CFG) >>"$(LOGIN_LOG)" 2>&1 & echo $$! >"$(LOG_DIR)/login.pid"
	@$(MAKE) --no-print-directory wait-login
	@echo "starting Game → $(GAME_LOG)"
	@: >"$(GAME_LOG)"
	@cd "$(GAME_DIR)" && nohup $(DOTNET) run --no-build -c $(BUILD_CFG) >>"$(GAME_LOG)" 2>&1 & echo $$! >"$(LOG_DIR)/game.pid"
	@$(MAKE) --no-print-directory wait-game
	@echo "started (logs: $(LOG_DIR)/)"

restart: down start
	@$(MAKE) --no-print-directory status

$(LOG_DIR):
	@mkdir -p "$(LOG_DIR)"

# Wait until Login internal (1234) or public (1237) is listening.
wait-login:
	@i=0; \
	while [ $$i -lt 60 ]; do \
	  if ss -ltn 2>/dev/null | grep -qE ':1237|:1234'; then \
	    echo "Login listening"; \
	    exit 0; \
	  fi; \
	  sleep 1; \
	  i=$$((i+1)); \
	done; \
	echo "WARNING: Login did not open 1237/1234 within 60s — check $(LOGIN_LOG)"; \
	exit 1

# Game load (managers + worlds) can take a few minutes on cold start.
wait-game:
	@i=0; \
	while [ $$i -lt 180 ]; do \
	  if ss -ltn 2>/dev/null | grep -qE ':1239'; then \
	    echo "Game listening"; \
	    exit 0; \
	  fi; \
	  sleep 2; \
	  i=$$((i+2)); \
	done; \
	echo "WARNING: Game did not open 1239 within 180s — check $(GAME_LOG)"; \
	exit 1

status:
	@echo "--- processes ---"
	@pgrep -af 'AAEmu\.(Login|Game)' || echo "(none)"
	@echo "--- ports ---"
	@ss -ltnp 2>/dev/null | grep -E ':1237|:1234|:1239|:1250' || echo "(ports not listening)"

logs:
	@mkdir -p "$(LOG_DIR)"
	@touch "$(LOGIN_LOG)" "$(GAME_LOG)"
	@tail -n 40 -F "$(LOGIN_LOG)" "$(GAME_LOG)"
