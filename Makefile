# Build entry points for every supported target.
#
# The only prerequisites are Make and Docker or Podman. Both the OCI image and
# the Proxmox LXC template build inside containers, so no .NET, Node, or Go
# toolchain and no Debian bootstrap tooling has to be installed on the host.
#
# Run `make` or `make help` for the list.
#
# This is a task runner, not a compilation graph: every target is .PHONY and the
# builds themselves are driven by the container engine. Make's built-in suffix
# and implicit rules are switched off so nothing here is inferred.

MAKEFLAGS += --no-builtin-rules --no-builtin-variables
.SUFFIXES:

ENGINE ?= $(shell for engine in docker podman; do command -v $$engine >/dev/null 2>&1 && { echo $$engine; break; }; done)
ARCH ?= $(shell uname -m | sed -e 's/^x86_64$$/amd64/' -e 's/^aarch64$$/arm64/')
VERSION ?= $(shell if [ -z "$$(git status --porcelain 2>/dev/null)" ]; then v=$$(git describe --tags --exact-match 2>/dev/null); fi; echo "$${v:-0.0.0-local}" | sed 's/^v//')
COMMIT ?= $(shell git rev-parse HEAD 2>/dev/null || echo unknown)

TAG ?= speedtest-dashboard:local
BUILDER_TAG ?= speedtest-dashboard-proxmox-builder:local
OUTPUT ?= artifacts
DASHBOARD_PORT ?= 8080
INSTALL_OOKLA ?= false
ALLOW_INSECURE_HTTP ?= false
OOKLA_ACCEPT_LICENSE ?= false
OOKLA_ACCEPT_GDPR ?= false

COMPOSE_LOCAL := packaging/containers/compose.local.yml
OOKLA_FLAG := $(if $(filter true,$(INSTALL_OOKLA)),--install-ookla,)
LOCAL_IMAGE_FORMAT_FLAG := $(if $(filter podman,$(notdir $(ENGINE))),--format docker,)
COMPOSE_BUILD_FORMAT_ENV := $(if $(filter podman,$(notdir $(ENGINE))),BUILDAH_FORMAT=docker,)

# Bind mounts need the :Z relabel only where SELinux is active.
SELINUX := $(shell command -v getenforce >/dev/null 2>&1 && getenforce 2>/dev/null || echo Disabled)
MOUNT_FLAG := $(if $(filter Enforcing Permissive,$(SELINUX)),:Z,)

.DEFAULT_GOAL := help
.PHONY: help doctor image run stop logs proxmox-template proxmox-template-native clean

help:
	@echo 'Speedtest Dashboard - build targets'
	@echo
	@echo '  make image              Build the Docker/Podman image ($(TAG))'
	@echo '  make run                Build and start it locally on port $(DASHBOARD_PORT)'
	@echo '  make stop               Stop the local stack'
	@echo '  make logs               Follow the local stack logs'
	@echo '  make proxmox-template   Build a Proxmox LXC template into $(OUTPUT)/'
	@echo '  make doctor             Check that this machine can build'
	@echo '  make clean              Remove build outputs in $(OUTPUT)/'
	@echo
	@echo 'Common overrides:'
	@echo '  ENGINE=podman           Container engine        (detected: $(or $(ENGINE),none))'
	@echo '  ARCH=arm64              Target architecture     (detected: $(ARCH))'
	@echo '  VERSION=1.2.3           Version stamp           (detected: $(VERSION))'
	@echo '  DASHBOARD_PORT=8008     Host port for `make run`'
	@echo '  INSTALL_OOKLA=true      Include the proprietary Ookla CLI - see docs/ookla.md'
	@echo '  ALLOW_INSECURE_HTTP=true  Allow login over plain HTTP for local testing'
	@echo
	@echo 'Installing what you built: packaging/containers/README.md, packaging/proxmox/README.md'

doctor:
	@if [ -z "$(ENGINE)" ]; then \
	  echo 'FAIL  No container engine found.'; \
	  echo '      Install Docker (https://docs.docker.com/get-started/get-docker/)'; \
	  echo '      or Podman (https://podman.io/docs/installation), then re-run.'; \
	  echo '      Already have one under another name? Use: make <target> ENGINE=<name>'; \
	  exit 1; \
	fi
	@command -v $(ENGINE) >/dev/null 2>&1 \
	  || { echo 'FAIL  ENGINE is set to "$(ENGINE)", which is not on PATH.'; exit 1; }
	@printf 'ok    engine       %s (%s)\n' "$(ENGINE)" "$$($(ENGINE) --version 2>/dev/null | head -1)"
	@$(ENGINE) info >/dev/null 2>&1 \
	  || { echo 'FAIL  $(ENGINE) is installed but not responding. Is the daemon or machine running?'; exit 1; }
	@echo 'ok    daemon       responding'
	@echo 'ok    architecture $(ARCH)'
	@echo 'ok    version      $(VERSION) ($(COMMIT))'
	@echo 'ok    selinux      $(SELINUX)$(if $(MOUNT_FLAG), - bind mounts use :Z,)'
	@echo
	@echo 'This machine can build both the container image and the Proxmox template.'

image: doctor
	$(ENGINE) build \
	  --file packaging/containers/Dockerfile $(LOCAL_IMAGE_FORMAT_FLAG) \
	  --build-arg INSTALL_OOKLA=$(INSTALL_OOKLA) \
	  --build-arg APP_VERSION=$(VERSION) \
	  --build-arg SOURCE_REVISION=$(COMMIT) \
	  --tag $(TAG) \
	  .
	@echo
	@echo 'Built $(TAG). Start it with: make run'

run: doctor
	$(COMPOSE_BUILD_FORMAT_ENV) DASHBOARD_PORT=$(DASHBOARD_PORT) \
	APP_VERSION=$(VERSION) \
	SOURCE_REVISION=$(COMMIT) \
	INSTALL_OOKLA=$(INSTALL_OOKLA) \
	ALLOW_INSECURE_HTTP=$(ALLOW_INSECURE_HTTP) \
	OOKLA_ACCEPT_LICENSE=$(OOKLA_ACCEPT_LICENSE) \
	OOKLA_ACCEPT_GDPR=$(OOKLA_ACCEPT_GDPR) \
	  $(ENGINE) compose --file $(COMPOSE_LOCAL) up --build --detach
	@echo
	@echo 'Dashboard starting on http://localhost:$(DASHBOARD_PORT)'

stop:
	DASHBOARD_PORT=$(DASHBOARD_PORT) $(ENGINE) compose --file $(COMPOSE_LOCAL) down

logs:
	DASHBOARD_PORT=$(DASHBOARD_PORT) $(ENGINE) compose --file $(COMPOSE_LOCAL) logs --follow

# Builds inside the pinned toolchain container, so the host needs no .NET, Node,
# Go, or debootstrap. --privileged is required by debootstrap in the builder;
# the finished template does not need it.
proxmox-template: doctor
	$(ENGINE) build --file packaging/proxmox/Containerfile.builder --tag $(BUILDER_TAG) .
	$(ENGINE) run --rm --privileged \
	  --volume "$(CURDIR):/source$(MOUNT_FLAG)" \
	  $(BUILDER_TAG) \
	  packaging/proxmox/build-template.sh \
	    --version "$(VERSION)" \
	    --commit "$(COMMIT)" \
	    --architecture "$(ARCH)" \
	    --output "$(OUTPUT)" \
	    $(OOKLA_FLAG)
	@echo
	@echo 'Template written to $(OUTPUT)/. Upload it per packaging/proxmox/README.md'

# For a host that already has the full toolchain installed.
proxmox-template-native:
	packaging/proxmox/build-template.sh \
	  --version "$(VERSION)" \
	  --commit "$(COMMIT)" \
	  --architecture "$(ARCH)" \
	  --output "$(OUTPUT)" \
	  $(OOKLA_FLAG)

clean:
	@case "$(abspath $(OUTPUT))" in \
	  "$(abspath artifacts)"|"$(abspath artifacts)"/*) ;; \
	  *) echo 'REFUSE  make clean only removes artifacts/ or one of its children.'; exit 1 ;; \
	esac
	rm -rf -- "$(abspath $(OUTPUT))"
